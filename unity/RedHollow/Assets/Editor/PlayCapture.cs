#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using RedHollow.Game.UI;
using RedHollow.Game.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RedHollow.EditorTools
{
    /// <summary>
    /// Playtest driver: drop /workspace/unity/playtest.request and the open editor enters Play,
    /// waits for the match, then dumps a Game-camera PNG plus console. Armed state lives on
    /// disk so a play-mode domain reload cannot forget the request.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayCapture
    {
        private const string RequestPath = "/workspace/unity/playtest.request";
        private const string ArmedPath = "/workspace/unity/playtest.armed";
        private const string StatusPath = "/workspace/unity/playtest.status";
        private const string ShotPath = "/workspace/unity/shots/game-view.png";

        private static double _enteredAt;
        private static readonly StringBuilder Logs = new StringBuilder();

        static PlayCapture()
        {
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            Logs.Append(type).Append(": ").Append(message).Append('\n');
            if (!string.IsNullOrEmpty(stack) && type != LogType.Log)
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

            if (File.Exists(RequestPath) && !EditorApplication.isPlaying)
            {
                try
                {
                    File.Delete(RequestPath);
                    File.WriteAllText(ArmedPath, "1");
                    File.WriteAllText(StatusPath, "entering-play\n");
                }
                catch (Exception)
                {
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(ShotPath));
                Logs.Clear();
                _enteredAt = 0;
                EditorApplication.isPlaying = true;
                return;
            }

            if (!File.Exists(ArmedPath) || !EditorApplication.isPlaying)
            {
                return;
            }

            if (_enteredAt < 1.0)
            {
                _enteredAt = EditorApplication.timeSinceStartup;
                return;
            }

            if (EditorApplication.timeSinceStartup - _enteredAt < 8.0)
            {
                return;
            }

            Capture();
            try
            {
                File.Delete(ArmedPath);
            }
            catch (Exception)
            {
            }

            EditorApplication.isPlaying = false;
        }

        private static void Capture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ShotPath));

            var sb = new StringBuilder();
            sb.Append("playing=").Append(Application.isPlaying).Append('\n');
            sb.Append("scene=").Append(SceneManager.GetActiveScene().path).Append('\n');
            sb.Append("time=").Append(Time.timeSinceLevelLoad.ToString("0.00")).Append('\n');

            var cam = Camera.main;
            sb.Append("camera=").Append(cam == null ? "null" : cam.name).Append('\n');
            if (cam != null)
            {
                sb.Append("camPos=").Append(cam.transform.position).Append('\n');
                sb.Append("camFwd=").Append(cam.transform.forward).Append('\n');
                sb.Append("ortho=").Append(cam.orthographic).Append(" size=").Append(cam.orthographicSize).Append('\n');
                DumpCamera(cam, ShotPath);
                sb.Append("shot=").Append(ShotPath).Append('\n');
            }

            var match = GameObject.Find("RedHollow_Match");
            sb.Append("match=").Append(match != null).Append('\n');
            var views = GameObject.Find("RedHollow_MatchViews");
            sb.Append("views=").Append(views != null);
            if (views != null)
            {
                sb.Append(" children=").Append(views.transform.childCount);
            }
            sb.Append('\n');

            var lights = UnityEngine.Object.FindObjectsByType<Light>();
            var dir = 0;
            var point = 0;
            foreach (var light in lights)
            {
                if (light == null || !light.enabled)
                {
                    continue;
                }

                if (light.type == LightType.Directional)
                {
                    dir++;
                }
                else if (light.type == LightType.Point)
                {
                    point++;
                }
            }

            sb.Append("dirLights=").Append(dir).Append(" pointLights=").Append(point).Append('\n');
            sb.Append("fog=").Append(RenderSettings.fog).Append(" sun=")
                .Append(RenderSettings.sun == null ? "null" : RenderSettings.sun.name).Append('\n');

            DumpSim(sb);
            DumpViews(sb, views);

            sb.Append("--- console ---\n");
            sb.Append(Logs);
            File.WriteAllText(StatusPath, sb.ToString());
            Debug.Log("[PlayCapture] wrote " + ShotPath + " and " + StatusPath);
        }

        private static void DumpSim(StringBuilder sb)
        {
            var entry = UnityEngine.Object.FindFirstObjectByType<GameEntryBehaviour>();
            var match = entry != null && entry.Shell != null && entry.Shell.Session != null
                ? entry.Shell.Session.Match
                : null;
            if (match == null || match.State == null)
            {
                sb.Append("sim=null\n");
                return;
            }

            var state = match.State;
            sb.Append("phase=").Append(state.Phase)
                .Append(" status=").Append(state.Status)
                .Append(" wave=").Append(state.Wave.Number)
                .Append("/").Append(state.Wave.TotalWaves).Append('\n');
            sb.Append("civilians=").Append(state.TotalCivilians)
                .Append(" livingMonsters=").Append(state.Wave.LivingMonsterIds.Count)
                .Append(" placeables=").Append(state.PlaceableCount).Append('\n');

            foreach (var hero in state.Heroes.Values)
            {
                if (hero == null)
                {
                    continue;
                }

                sb.Append("hero ").Append(hero.Id)
                    .Append(" class=").Append(hero.HeroClass)
                    .Append(" pos=").Append(hero.Pos.X.ToString("0.00")).Append(",")
                    .Append(hero.Pos.Y.ToString("0.00"))
                    .Append(" hp=").Append(hero.Hp.ToString("0.0"))
                    .Append(" alive=").Append(hero.Alive).Append('\n');
            }

            foreach (var monster in state.Monsters.Values)
            {
                if (monster == null)
                {
                    continue;
                }

                sb.Append("monster ").Append(monster.Id)
                    .Append(" type=").Append(monster.Type)
                    .Append(" pos=").Append(monster.Pos.X.ToString("0.00")).Append(",")
                    .Append(monster.Pos.Y.ToString("0.00"))
                    .Append(" hp=").Append(monster.Hp.ToString("0.0"))
                    .Append(" alive=").Append(monster.Alive)
                    .Append(" target=").Append(monster.TargetId).Append('\n');
            }

            foreach (var hotspot in state.Hotspots.Values)
            {
                if (hotspot == null)
                {
                    continue;
                }

                sb.Append("hotspot ").Append(hotspot.Id)
                    .Append(" civ=").Append(hotspot.Civilians).Append('\n');
            }

            var markers = UnityEngine.Object.FindObjectsByType<HotspotMarkerView>(FindObjectsSortMode.None);
            foreach (var marker in markers)
            {
                if (marker == null)
                {
                    continue;
                }

                sb.Append("marker ").Append(marker.HotspotId)
                    .Append(" lost=").Append(marker.Lost).Append('\n');
            }
        }

        private static void DumpViews(StringBuilder sb, GameObject views)
        {
            if (views == null)
            {
                return;
            }

            for (var i = 0; i < views.transform.childCount; i++)
            {
                var child = views.transform.GetChild(i);
                sb.Append("view ").Append(child.name)
                    .Append(" pos=").Append(child.position).Append('\n');
            }
        }

        private static void DumpCamera(Camera camera, string path)
        {
            const int width = 1920;
            const int height = 1080;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var prev = camera.targetTexture;
            var prevActive = RenderTexture.active;
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            camera.targetTexture = prev;
            RenderTexture.active = prevActive;
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
#endif
