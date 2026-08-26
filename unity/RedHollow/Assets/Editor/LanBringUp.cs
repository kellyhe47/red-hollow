#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RedHollow.Game.Net;
using RedHollow.Game.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RedHollow.EditorTools
{
    /// <summary>
    /// Single-editor LAN host bring-up. Drop /workspace/unity/lan.request: the open editor
    /// disables <see cref="GameEntryBehaviour"/> (the solo Play path stays the scene default),
    /// enters Play, adds <see cref="LanPartyBehaviour"/>, HOST GAMEs into the lobby, dumps
    /// /workspace/unity/shots/lan-lobby.png of S2 with the join code, and writes
    /// /workspace/unity/lan.status. Does not start the match — R-53 forbids mid-match joins.
    /// Exit Play re-enables GameEntry.
    /// </summary>
    [InitializeOnLoad]
    public static class LanBringUp
    {
        private const string RequestPath = "/workspace/unity/lan.request";
        private const string ArmedPath = "/workspace/unity/lan.armed";
        private const string StatusPath = "/workspace/unity/lan.status";
        private const string ShotPath = "/workspace/unity/shots/lan-lobby.png";
        private const string MatchScene = "Assets/Scenes/RedHollow.unity";
        private const double ListenTimeoutSeconds = 30.0;
        private const double PaintSettleSeconds = 2.0;

        private static double _enteredAt;
        private static bool _hosted;
        private static bool _spawned;
        private static bool _shotTaken;
        private static readonly StringBuilder Logs = new StringBuilder();

        static LanBringUp()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log)
            {
                return;
            }

            Logs.Append(type).Append(": ").Append(message).Append('\n');
            if (!string.IsNullOrEmpty(stack) && type != LogType.Warning)
            {
                Logs.Append(stack).Append('\n');
            }
        }

        private static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            if (EditorApplication.isPlaying)
            {
                if (!File.Exists(ArmedPath))
                {
                    return;
                }

                DrivePlay();
                return;
            }

            if (File.Exists(ArmedPath))
            {
                Restore();
                return;
            }

            if (!File.Exists(RequestPath))
            {
                return;
            }

            try
            {
                File.Delete(RequestPath);
            }
            catch (Exception)
            {
                return;
            }

            ArmAndPlay();
        }

        private static void ArmAndPlay()
        {
            _hosted = false;
            _spawned = false;
            _shotTaken = false;
            _enteredAt = 0.0;
            Logs.Clear();
            WriteStatus("arming\n");

            if (SceneManager.GetActiveScene().path != MatchScene)
            {
                EditorSceneManager.OpenScene(MatchScene);
            }

            var entry = UnityEngine.Object.FindFirstObjectByType<GameEntryBehaviour>();
            if (entry != null)
            {
                entry.enabled = false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ShotPath));
                File.WriteAllText(ArmedPath, "1\n");
            }
            catch (Exception ex)
            {
                if (entry != null)
                {
                    entry.enabled = true;
                }

                WriteStatus("fail arm: " + ex.Message + "\n");
                return;
            }

            EditorApplication.isPlaying = true;
        }

        private static void DrivePlay()
        {
            if (_enteredAt <= 0.0)
            {
                _enteredAt = EditorApplication.timeSinceStartup;
            }

            if (!_spawned)
            {
                var go = new GameObject("RedHollow_LanParty");
                go.AddComponent<LanPartyBehaviour>();
                _spawned = true;
                return;
            }

            var party = UnityEngine.Object.FindFirstObjectByType<LanPartyBehaviour>();
            if (party == null)
            {
                if (EditorApplication.timeSinceStartup - _enteredAt > 8.0)
                {
                    FailAndStop("LanPartyBehaviour missing after spawn");
                }

                return;
            }

            if (!_hosted && party.Shell != null)
            {
                try
                {
                    party.Shell.Title.SetCallsign("Kelly");
                    party.Shell.RequestHost();
                    _hosted = true;
                }
                catch (Exception ex)
                {
                    FailAndStop(
                        "RequestHost " + ex.GetType().Name + ": " + ex.Message
                        + " nmSingleton=" + (NetworkManager.Singleton != null)
                        + " nmListening=" + (NetworkManager.Singleton != null
                            && NetworkManager.Singleton.IsListening));
                    return;
                }
            }

            var elapsed = EditorApplication.timeSinceStartup - _enteredAt;
            var running = party.Transport != null && party.Transport.IsRunning;
            var listening = UdpPortBound(7777);
            var nmListening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            var screen = party.Shell != null && party.Shell.Router != null
                ? party.Shell.Router.Screen.ToString()
                : "null";
            var label = party.Shell != null && party.Shell.Controls != null
                && party.Shell.Controls.JoinCodeLabel != null
                ? party.Shell.Controls.JoinCodeLabel.text
                : "";
            var joinCode = party.JoinCode ?? "";

            var lobbyPainted = _hosted && running
                && screen == UiScreen.Lobby.ToString()
                && joinCode.IndexOf("LAN", StringComparison.OrdinalIgnoreCase) >= 0
                && label.IndexOf("LAN", StringComparison.OrdinalIgnoreCase) >= 0
                && elapsed >= PaintSettleSeconds;

            if (lobbyPainted)
            {
                if (!_shotTaken)
                {
                    DumpCamera(Camera.main, ShotPath);
                    _shotTaken = true;
                }

                WriteStatus(
                    "pass joinCode=" + joinCode
                    + " label=" + label.Replace('\n', ' ')
                    + " screen=" + screen
                    + " transportRunning=True"
                    + " nmListening=" + nmListening
                    + " udp7777=" + listening
                    + " shot=" + ShotPath
                    + " shotExists=" + File.Exists(ShotPath)
                    + " elapsed=" + elapsed.ToString("0.00")
                    + "\n--- console ---\n" + Logs);
                EditorApplication.isPlaying = false;
                return;
            }

            if (elapsed > ListenTimeoutSeconds)
            {
                if (!_shotTaken)
                {
                    DumpCamera(Camera.main, ShotPath);
                    _shotTaken = true;
                }

                FailAndStop(
                    "timeout hosted=" + _hosted
                    + " running=" + running
                    + " joinCode=" + (string.IsNullOrEmpty(joinCode) ? "null" : joinCode)
                    + " label=" + label.Replace('\n', ' ')
                    + " screen=" + screen
                    + " nmListening=" + nmListening
                    + " udp7777=" + listening
                    + " shot=" + ShotPath
                    + " shotExists=" + File.Exists(ShotPath)
                    + "\n--- console ---\n" + Logs);
            }
        }

        private static void FailAndStop(string reason)
        {
            WriteStatus("fail " + reason + "\n");
            EditorApplication.isPlaying = false;
        }

        private static void Restore()
        {
            try
            {
                File.Delete(ArmedPath);
            }
            catch (Exception)
            {
            }

            var leftover = GameObject.Find("RedHollow_LanParty");
            if (leftover != null)
            {
                UnityEngine.Object.DestroyImmediate(leftover);
            }

            var entry = UnityEngine.Object.FindFirstObjectByType<GameEntryBehaviour>(
                FindObjectsInactive.Include);
            if (entry != null)
            {
                entry.enabled = true;
            }

            _hosted = false;
            _spawned = false;
            _shotTaken = false;
            _enteredAt = 0.0;
        }

        /// <summary>
        /// UnityTransport listens on UDP, not TCP. Succeeds if 0.0.0.0:port cannot be bound.
        /// </summary>
        private static bool UdpPortBound(int port)
        {
            try
            {
                using (var socket = new Socket(
                    AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.ExclusiveAddressUse = true;
                    socket.SetSocketOption(
                        SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
                    socket.Bind(new IPEndPoint(IPAddress.Any, port));
                    return false;
                }
            }
            catch (SocketException)
            {
                return true;
            }
        }

        private static void DumpCamera(Camera camera, string path)
        {
            if (camera == null)
            {
                return;
            }

            const int width = 1920;
            const int height = 1080;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var prev = camera.targetTexture;
            var prevActive = RenderTexture.active;

            var restored = new List<Canvas>();
            var modes = new List<RenderMode>();
            var cams = new List<Camera>();
            var distances = new List<float>();
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            for (var i = 0; i < canvases.Length; i++)
            {
                var canvas = canvases[i];
                if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    continue;
                }

                restored.Add(canvas);
                modes.Add(canvas.renderMode);
                cams.Add(canvas.worldCamera);
                distances.Add(canvas.planeDistance);
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 2f;
            }

            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            camera.targetTexture = prev;
            RenderTexture.active = prevActive;
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            for (var i = 0; i < restored.Count; i++)
            {
                restored[i].renderMode = modes[i];
                restored[i].worldCamera = cams[i];
                restored[i].planeDistance = distances[i];
            }
        }

        /// <summary>
        /// Headless LAN host listen: bind NGO on loopback:7777 without opening a Game view.
        /// Unity -batchmode -nographics -quit -executeMethod RedHollow.EditorTools.LanBringUp.RunHeadless
        /// NGO StartHost needs Play (NetworkManager singleton); this path records that failure.
        /// </summary>
        public static void RunHeadless()
        {
            var ngo = new GameObject("RedHollow_NGO_Headless");
            var utp = ngo.AddComponent<UnityTransport>();
            var networkManager = ngo.AddComponent<NetworkManager>();
            if (networkManager.NetworkConfig == null)
            {
                networkManager.NetworkConfig = new NetworkConfig();
            }

            networkManager.NetworkConfig.NetworkTransport = utp;
            if (NetworkManager.Singleton != networkManager)
            {
                networkManager.SetSingleton();
            }

            var wire = new NgoWire(networkManager);
            wire.SetLocalPeerId("peer_lan_host");
            var transport = new NgoNetTransport(new LanServices(), wire);
            try
            {
                transport.StartHost(new NetSessionConfig());
                var listening = UdpPortBound(7777);
                WriteStatus(
                    "pass joinCode=" + (transport.JoinCode ?? "?")
                    + " transportRunning=" + transport.IsRunning
                    + " nmListening=" + networkManager.IsListening
                    + " udp7777=" + listening + "\n");
            }
            catch (Exception ex)
            {
                WriteStatus(
                    "fail " + ex.GetType().Name + ": " + ex.Message
                    + " (NGO bind needs Play; use LanPartyBehaviour)\n");
            }
            finally
            {
                try { transport.Shutdown(); } catch (Exception) { }
                UnityEngine.Object.DestroyImmediate(ngo);
            }
        }

        private static void WriteStatus(string text)
        {
            try
            {
                File.WriteAllText(StatusPath, text);
            }
            catch (Exception)
            {
            }
        }
    }
}
#endif
