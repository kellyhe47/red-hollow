#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using RedHollow.Game.Input;
using RedHollow.Game.Net;
using RedHollow.Game.UI;
using RedHollow.Game.View;
using RedHollow.Sim;
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
        private const string ProofPath = "/workspace/unity/shots/combat-proof.png";

        private static double _enteredAt;
        private static bool _driving;
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
                OverlayInputSource.Clear();
                Logs.Clear();
                _enteredAt = 0;
                _driving = false;
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

            var elapsed = EditorApplication.timeSinceStartup - _enteredAt;
            DriveCombatInput();

            var proof = CombatProofReady();
            var timedOut = elapsed >= 12.0;
            // Hold the walk/fire long enough that the Game view shows the gunslinger
            // clearly off spawn (4 u/s * 2.5s ≈ 10 units) rather than a 0.2s twitch.
            if ((!proof || elapsed < 2.5) && !timedOut)
            {
                return;
            }

            Capture();
            OverlayInputSource.Clear();
            try
            {
                File.Delete(ArmedPath);
            }
            catch (Exception)
            {
            }

            EditorApplication.isPlaying = false;
        }

        /// <summary>
        /// Hold A+W (west + forward toward wave-1 shamblers), aim at a living shambler, and
        /// fire SPACE plus a Q/E press so the live input map, not a sim cheat, drives combat.
        /// </summary>
        private static void DriveCombatInput()
        {
            var match = LiveMatch();
            if (match == null || match.State == null || match.State.Phase != MatchPhase.Combat)
            {
                return;
            }

            if (!_driving)
            {
                _driving = true;
                OverlayInputSource.ExtraHeld.Clear();
                OverlayInputSource.ExtraHeld.Add(PlayerKey.A);
                OverlayInputSource.ExtraHeld.Add(PlayerKey.W);
                OverlayInputSource.ExtraHeld.Add(PlayerKey.Space);
                OverlayInputSource.ExtraHeld.Add(PlayerKey.Q);
                OverlayInputSource.ExtraHeld.Add(PlayerKey.E);
            }

            OverlayInputSource.CursorOverride = AimAtLivingShambler(match.State);
        }

        private static Vector2 AimAtLivingShambler(MatchState state)
        {
            foreach (var monster in state.Monsters.Values)
            {
                if (monster != null && monster.Alive)
                {
                    return new Vector2((float)monster.Pos.X, (float)monster.Pos.Y);
                }
            }

            return new Vector2(-12f, 6f);
        }

        private static bool CombatProofReady()
        {
            var match = LiveMatch();
            if (match == null || match.State == null)
            {
                return false;
            }

            var moved = false;
            foreach (var hero in match.State.Heroes.Values)
            {
                if (hero == null)
                {
                    continue;
                }

                var dist = Math.Sqrt((hero.Pos.X * hero.Pos.X) + (hero.Pos.Y * hero.Pos.Y));
                if (dist >= 5.0)
                {
                    moved = true;
                    break;
                }
            }

            var damaged = false;
            var living = 0;
            foreach (var monster in match.State.Monsters.Values)
            {
                if (monster == null || !monster.Alive)
                {
                    continue;
                }

                living++;
                if (monster.Hp < 59.9)
                {
                    damaged = true;
                }
            }

            if (match.State.Wave != null && match.State.Wave.LivingMonsterIds.Count < 6)
            {
                damaged = true;
            }

            return moved && (damaged || living == 0);
        }

        private static HostedMatch LiveMatch()
        {
            var entry = UnityEngine.Object.FindFirstObjectByType<GameEntryBehaviour>();
            if (entry == null || entry.Shell == null || entry.Shell.Session == null)
            {
                return null;
            }

            return entry.Shell.Session.Match;
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
                try
                {
                    CropHeroProof(cam, ProofPath);
                    sb.Append("proof=").Append(ProofPath).Append('\n');
                }
                catch (Exception ex)
                {
                    sb.Append("proofError=").Append(ex.Message).Append('\n');
                }
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
            DumpProof(sb);

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

        private static void DumpProof(StringBuilder sb)
        {
            sb.Append("overlayHeld=");
            if (OverlayInputSource.ExtraHeld.Count == 0)
            {
                sb.Append("(none)");
            }
            else
            {
                var first = true;
                foreach (var key in OverlayInputSource.ExtraHeld)
                {
                    if (!first)
                    {
                        sb.Append(",");
                    }

                    sb.Append(key);
                    first = false;
                }
            }

            sb.Append(" cursor=");
            sb.Append(OverlayInputSource.CursorOverride.HasValue
                ? OverlayInputSource.CursorOverride.Value.ToString()
                : "null");
            sb.Append('\n');

            var entry = UnityEngine.Object.FindFirstObjectByType<GameEntryBehaviour>();
            var shell = entry != null ? entry.Shell : null;
            if (shell != null && shell.LastAbilityOutcome != null)
            {
                var o = shell.LastAbilityOutcome;
                sb.Append("ability slot=").Append(o.Slot)
                    .Append(" accepted=").Append(o.Accepted)
                    .Append(" reason=").Append(o.RejectionReason ?? "")
                    .Append(" dmg=").Append(o.TotalDamage.ToString("0.0"))
                    .Append('\n');
            }

            foreach (var id in shell != null && shell.Views != null
                ? shell.Views.BoundHeroIds
                : System.Array.Empty<string>())
            {
                var view = shell.Views.HeroViewFor(id);
                if (view == null)
                {
                    continue;
                }

                sb.Append("heroView ").Append(id)
                    .Append(" world=").Append(view.WorldPosition)
                    .Append(" facing=").Append(view.Facing)
                    .Append('\n');
            }

            sb.Append("proofReady=").Append(CombatProofReady()).Append('\n');
        }

        private static void CropHeroProof(Camera camera, string path)
        {
            var match = LiveMatch();
            if (match == null || match.State == null || camera == null)
            {
                return;
            }

            Hero hero = null;
            foreach (var h in match.State.Heroes.Values)
            {
                if (h != null && h.Alive)
                {
                    hero = h;
                    break;
                }
            }

            if (hero == null)
            {
                return;
            }

            var world = SimSpace.ToWorld(hero.Pos);
            var viewport = camera.WorldToViewportPoint(world);
            if (viewport.z < 0f)
            {
                return;
            }

            const int width = 1920;
            const int height = 1080;
            var cx = Mathf.Clamp(Mathf.RoundToInt(viewport.x * width), 0, width - 1);
            var cy = Mathf.Clamp(Mathf.RoundToInt((1f - viewport.y) * height), 0, height - 1);
            const int crop = 720;
            var x = Mathf.Clamp(cx - (crop / 2), 0, width - crop);
            var y = Mathf.Clamp(cy - (crop / 2), 0, height - crop);

            if (!File.Exists(ShotPath))
            {
                return;
            }

            var bytes = File.ReadAllBytes(ShotPath);
            var src = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!src.LoadImage(bytes))
            {
                UnityEngine.Object.DestroyImmediate(src);
                return;
            }

            var dst = new Texture2D(crop, crop, TextureFormat.RGB24, false);
            dst.SetPixels(src.GetPixels(x, src.height - y - crop, crop, crop));
            dst.Apply();
            File.WriteAllBytes(path, dst.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(src);
            UnityEngine.Object.DestroyImmediate(dst);
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
