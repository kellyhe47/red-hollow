#if UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using RedHollow.Game.UI;
using UnityEditor;
using RedHollow.Game.Net;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace RedHollow.EditorTools
{
    /// <summary>
    /// Single-editor LAN host bring-up. Drop /workspace/unity/lan.request: the open editor
    /// disables <see cref="GameEntryBehaviour"/> (the solo Play path stays the scene default),
    /// enters Play, adds <see cref="LanPartyBehaviour"/>, HOST GAMEs into the lobby, and writes
    /// /workspace/unity/lan.status with the join code and whether port 7777 is listening.
    /// Does not start the match — R-53 forbids mid-match joins, so a second player can still
    /// knock. Exit Play re-enables GameEntry.
    /// </summary>
    [InitializeOnLoad]
    public static class LanBringUp
    {
        private const string RequestPath = "/workspace/unity/lan.request";
        private const string ArmedPath = "/workspace/unity/lan.armed";
        private const string StatusPath = "/workspace/unity/lan.status";
        private const double ListenTimeoutSeconds = 25.0;

        private static double _enteredAt;
        private static bool _hosted;
        private static bool _spawned;

        static LanBringUp()
        {
            EditorApplication.update += Tick;
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
            _enteredAt = 0.0;
            WriteStatus("arming\n");

            var entry = UnityEngine.Object.FindFirstObjectByType<GameEntryBehaviour>();
            if (entry != null)
            {
                entry.enabled = false;
            }

            try
            {
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
                party.Shell.Title.SetCallsign("Kelly");
                party.Shell.RequestHost();
                _hosted = true;
            }

            var elapsed = EditorApplication.timeSinceStartup - _enteredAt;
            var running = party.Transport != null && party.Transport.IsRunning;
            var listening = PortOpen(7777);
            if (_hosted && running)
            {
                WriteStatus(
                    "pass joinCode=" + (party.JoinCode ?? "?")
                    + " transportRunning=True listening=" + listening
                    + " elapsed=" + elapsed.ToString("0.00") + "\n");
                EditorApplication.isPlaying = false;
                return;
            }

            if (elapsed > ListenTimeoutSeconds)
            {
                FailAndStop(
                    "timeout hosted=" + _hosted
                    + " running=" + running
                    + " joinCode=" + (party.JoinCode ?? "null")
                    + " listening=" + listening);
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
            _enteredAt = 0.0;
        }

        private static bool PortOpen(int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect(IPAddress.Loopback, port, null, null);
                    var ok = ar.AsyncWaitHandle.WaitOne(200);
                    if (!ok)
                    {
                        return false;
                    }

                    client.EndConnect(ar);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Headless LAN host listen: bind NGO on loopback:7777 without opening a Game view.
        /// Unity -batchmode -nographics -quit -executeMethod RedHollow.EditorTools.LanBringUp.RunHeadless
        /// </summary>
        public static void RunHeadless()
        {
            var ngo = new GameObject("RedHollow_NGO_Headless");
            var networkManager = ngo.AddComponent<NetworkManager>();
            var utp = ngo.AddComponent<UnityTransport>();
            networkManager.NetworkConfig = new NetworkConfig { NetworkTransport = utp };
            var wire = new NgoWire(networkManager);
            wire.SetLocalPeerId("peer_lan_host");
            var transport = new NgoNetTransport(new LanServices(), wire);
            try
            {
                transport.StartHost(new NetSessionConfig());
                var listening = PortOpen(7777);
                WriteStatus(
                    "pass joinCode=" + (transport.JoinCode ?? "?")
                    + " transportRunning=" + transport.IsRunning
                    + " listening=" + listening + "\n");
            }
            catch (Exception ex)
            {
                WriteStatus("fail " + ex.GetType().Name + ": " + ex.Message + " (NGO bind needs Play; use LanPartyBehaviour)\n");
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
