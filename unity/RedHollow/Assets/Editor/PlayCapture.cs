#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
using UnityEngine.UI;

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
        private const string WaveClearPath = "/workspace/unity/shots/wave1-clear.png";
        private const string Wave2Path = "/workspace/unity/shots/wave2.png";
        private const string ShopPath = "/workspace/unity/shots/shop.png";

        private static double _enteredAt;
        private static bool _driving;
        private static bool _wave1Captured;
        private static bool _readySent;
        private static bool _wave2Captured;
        private static bool _shopAttempted;
        private static bool _shopShot;
        private static double _planningSince;
        private static readonly List<string> PurchaseLog = new List<string>();
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
                _wave1Captured = false;
                _readySent = false;
                _wave2Captured = false;
                _shopAttempted = false;
                _shopShot = false;
                _planningSince = 0;
                PurchaseLog.Clear();
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
            DriveWaveProgression();

            var timedOut = elapsed >= 32.0;
            // Wave-loop proof: stay in Play until wave 1 is cleared AND wave 2 combat
            // has spawned (or we time out). Combat-first opening is unchanged.
            if (!_wave2Captured && !timedOut)
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
        /// Chase the nearest living monster with WASD, aim at it, and hold SPACE (plus Q/E)
        /// so the live input map, not a sim cheat, drives combat. Stops walking inside 8u
        /// so a long wave-clear does not walk the gunslinger off the cavern.
        /// </summary>
        private static void DriveCombatInput()
        {
            var match = LiveMatch();
            if (match == null || match.State == null || match.State.Phase != MatchPhase.Combat)
            {
                OverlayInputSource.ExtraHeld.Clear();
                return;
            }

            _driving = true;
            OverlayInputSource.ExtraHeld.Clear();
            OverlayInputSource.ExtraHeld.Add(PlayerKey.Space);
            OverlayInputSource.ExtraHeld.Add(PlayerKey.Q);
            OverlayInputSource.ExtraHeld.Add(PlayerKey.E);

            Hero hero = null;
            foreach (var h in match.State.Heroes.Values)
            {
                if (h != null && h.Alive)
                {
                    hero = h;
                    break;
                }
            }

            Monster target = NearestLiving(match.State, hero);
            if (target == null)
            {
                OverlayInputSource.CursorOverride = new Vector2(-12f, 6f);
                return;
            }

            OverlayInputSource.CursorOverride = new Vector2((float)target.Pos.X, (float)target.Pos.Y);
            if (hero == null)
            {
                return;
            }

            var dx = target.Pos.X - hero.Pos.X;
            var dy = target.Pos.Y - hero.Pos.Y;
            var dist = Math.Sqrt((dx * dx) + (dy * dy));
            if (dist > 8.0)
            {
                if (dx < -0.4)
                {
                    OverlayInputSource.ExtraHeld.Add(PlayerKey.A);
                }
                else if (dx > 0.4)
                {
                    OverlayInputSource.ExtraHeld.Add(PlayerKey.D);
                }

                if (dy > 0.4)
                {
                    OverlayInputSource.ExtraHeld.Add(PlayerKey.W);
                }
                else if (dy < -0.4)
                {
                    OverlayInputSource.ExtraHeld.Add(PlayerKey.S);
                }
            }
        }

        /// <summary>
        /// After wave 1's last kill the sim is already in planning (R-02). Buy from the
        /// existing R-23 shop (planning-phase only, R-21) BEFORE Ready-up, so wave 2 opens
        /// with standing defences. Ready-up remains the R-03 early exit so the 60s timer
        /// does not block autoplay — T21/T25/T12 still open matches in combat.
        /// </summary>
        private static void DriveWaveProgression()
        {
            var match = LiveMatch();
            var shell = LiveShell();
            if (match == null || match.State == null)
            {
                return;
            }

            if (!_wave1Captured && Wave1Cleared(match))
            {
                DumpCamera(Camera.main, WaveClearPath);
                _wave1Captured = true;
            }

            if (match.State.Phase == MatchPhase.Planning
                && (_wave1Captured || match.State.Wave.Number >= 2)
                && shell != null
                && shell.Planning != null)
            {
                if (!_shopAttempted)
                {
                    DriveShopPurchases(shell, match);
                    _planningSince = EditorApplication.timeSinceStartup;
                }

                var onPlanningUi = shell.Router != null
                    && shell.Router.Screen == UiScreen.Planning;
                if (!_shopShot && onPlanningUi)
                {
                    DumpCamera(Camera.main, ShopPath);
                    _shopShot = true;
                }

                // Place-then-ready. Wait for the S3 shop bar (post-interstitial) when we can,
                // but never hang autoplay if the router sticks on S5.
                var waited = _planningSince > 0
                    && (EditorApplication.timeSinceStartup - _planningSince) >= 5.0;
                if (!_readySent && _shopAttempted && (_shopShot || waited))
                {
                    shell.Planning.ReadyUp();
                    _readySent = true;
                }
            }

            if (!_wave2Captured && Wave2Live(match))
            {
                DumpCamera(Camera.main, Wave2Path);
                _wave2Captured = true;
            }
        }

        /// <summary>
        /// Drive the locked T-23 shop seam: one Barricade and one Turret (R-23 catalog rows)
        /// on open colony ground. Falls back to a Spike if the turret is refused.
        /// </summary>
        private static void DriveShopPurchases(ShellBootstrap shell, HostedMatch match)
        {
            _shopAttempted = true;
            shell.Planning.Refresh();

            var oracle = ZoneOracleFor(match);
            TryBuy(shell, match, oracle, PlaceableType.Barricade);
            if (!TryBuy(shell, match, oracle, PlaceableType.Turret))
            {
                TryBuy(shell, match, oracle, PlaceableType.SpikeTrap);
            }
        }

        private static PlacementZoneOracle ZoneOracleFor(HostedMatch match)
        {
            var map = match.Sim != null ? match.Sim.ColonyMap : ColonyMap.V1();
            var oracle = new PlacementZoneOracle(map);
            if (match.Sim != null)
            {
                oracle.HotspotBuildingRadius = match.Sim.HotspotBuildingRadius;
                oracle.EntryTunnelMouthRadius = match.Sim.EntryTunnelMouthRadius;
                oracle.PlaceableFootprintRadius = match.Sim.PlaceableFootprintRadius;
            }

            return oracle;
        }

        private static bool TryBuy(
            ShellBootstrap shell,
            HostedMatch match,
            PlacementZoneOracle oracle,
            string placeableType)
        {
            var planning = shell.Planning;
            ShopItem item = null;
            foreach (var candidate in planning.ShopItems)
            {
                if (candidate != null && candidate.Type == placeableType)
                {
                    item = candidate;
                    break;
                }
            }

            if (item == null)
            {
                PurchaseLog.Add("buy " + placeableType + " accepted=False reason=not_in_catalog");
                return false;
            }

            if (!item.Affordable)
            {
                PurchaseLog.Add("buy " + placeableType + " accepted=False reason=insufficient_scrip cost="
                    + item.Cost + " scrip=" + planning.Scrip);
                return false;
            }

            var button = shell.Controls != null ? shell.Controls.ShopItemButton(placeableType) : null;
            if (button != null)
            {
                button.onClick.Invoke();
            }
            else
            {
                planning.BeginPlacement(placeableType);
            }

            Vec2 pos;
            if (!TryOpenGround(oracle, match.State, out pos))
            {
                planning.CancelPlacement();
                PurchaseLog.Add("buy " + placeableType + " accepted=False reason=no_open_ground");
                return false;
            }

            var zoneOk = oracle.WouldAccept(match.State, pos);
            planning.MoveGhost(pos, zoneOk);
            var result = planning.ConfirmPlacement();
            var reason = result.RejectionReason ?? "";
            PurchaseLog.Add("buy " + placeableType
                + " accepted=" + result.Accepted
                + " reason=" + reason
                + " scripAfter=" + result.ScripAfter
                + " pos=" + pos.X.ToString("0.00") + "," + pos.Y.ToString("0.00"));
            return result.Accepted;
        }

        /// <summary>
        /// West-choke first (wave 1 walked in from x=-30), then a coarse grid. Radii come from
        /// the live sim via the T-24 oracle — never a second copy of R-24.
        /// </summary>
        private static bool TryOpenGround(PlacementZoneOracle oracle, MatchState state, out Vec2 pos)
        {
            var preferred = new[]
            {
                new Vec2(-20.0, 0.0),
                new Vec2(-16.0, -4.0),
                new Vec2(-18.0, 4.0),
                new Vec2(-14.0, 2.0),
                new Vec2(5.0, 0.0),
                new Vec2(-8.0, -6.0),
                new Vec2(8.0, -4.0),
                new Vec2(0.0, 8.0),
            };

            for (var i = 0; i < preferred.Length; i++)
            {
                if (oracle.WouldAccept(state, preferred[i]))
                {
                    pos = preferred[i];
                    return true;
                }
            }

            for (var x = -24; x <= 24; x += 4)
            {
                for (var y = -24; y <= 24; y += 4)
                {
                    var candidate = new Vec2(x, y);
                    if (oracle.WouldAccept(state, candidate))
                    {
                        pos = candidate;
                        return true;
                    }
                }
            }

            pos = new Vec2(0.0, 0.0);
            return false;
        }


        private static Monster NearestLiving(MatchState state, Hero hero)
        {
            Monster best = null;
            var bestD = double.MaxValue;
            foreach (var monster in state.Monsters.Values)
            {
                if (monster == null || !monster.Alive)
                {
                    continue;
                }

                if (hero == null)
                {
                    return monster;
                }

                var dx = monster.Pos.X - hero.Pos.X;
                var dy = monster.Pos.Y - hero.Pos.Y;
                var d = (dx * dx) + (dy * dy);
                if (d < bestD)
                {
                    bestD = d;
                    best = monster;
                }
            }

            return best;
        }

        private static bool Wave1Cleared(HostedMatch match)
        {
            if (match.State.Wave == null || match.State.Monsters.Count == 0)
            {
                return false;
            }

            return match.State.Wave.LivingMonsterIds.Count == 0
                && match.State.Wave.Number >= 1;
        }

        private static bool Wave2Live(HostedMatch match)
        {
            return match.State.Phase == MatchPhase.Combat
                && match.State.Wave.Number >= 2
                && match.State.Wave.LivingMonsterIds.Count > 0;
        }

        private static ShellBootstrap LiveShell()
        {
            var entry = UnityEngine.Object.FindFirstObjectByType<GameEntryBehaviour>();
            return entry != null ? entry.Shell : null;
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
                sb.Append("wave1ClearShot=").Append(WaveClearPath)
                    .Append(" exists=").Append(File.Exists(WaveClearPath)).Append('\n');
                sb.Append("wave2Shot=").Append(Wave2Path)
                    .Append(" exists=").Append(File.Exists(Wave2Path)).Append('\n');
                sb.Append("shopShot=").Append(ShopPath)
                    .Append(" exists=").Append(File.Exists(ShopPath)).Append('\n');
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
                .Append(" placeables=").Append(state.PlaceableCount)
                .Append(" scrip=").Append(state.Team.Scrip).Append('\n');
            sb.Append("wave1Captured=").Append(_wave1Captured)
                .Append(" readySent=").Append(_readySent)
                .Append(" wave2Captured=").Append(_wave2Captured)
                .Append(" shopAttempted=").Append(_shopAttempted)
                .Append(" shopShot=").Append(_shopShot).Append('\n');
            for (var i = 0; i < PurchaseLog.Count; i++)
            {
                sb.Append(PurchaseLog[i]).Append('\n');
            }
            var shell = entry != null ? entry.Shell : null;
            if (shell != null && shell.Router != null)
            {
                sb.Append("uiScreen=").Append(shell.Router.Screen).Append('\n');
            }
            if (shell != null && shell.Ui != null)
            {
                sb.Append("hudWave=").Append(shell.Ui.WaveLabel != null ? shell.Ui.WaveLabel.text : "")
                    .Append('\n');
                sb.Append("hudScrip=").Append(shell.Ui.ScripLabel != null ? shell.Ui.ScripLabel.text : "")
                    .Append('\n');
                sb.Append("hudLeft=").Append(shell.Ui.MonstersRemainingLabel != null
                    ? shell.Ui.MonstersRemainingLabel.text : "").Append('\n');
            }

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

            foreach (var placeable in state.Placeables.Values)
            {
                if (placeable == null)
                {
                    continue;
                }

                sb.Append("placeable ").Append(placeable.Id)
                    .Append(" type=").Append(placeable.Type)
                    .Append(" pos=").Append(placeable.Pos.X.ToString("0.00")).Append(",")
                    .Append(placeable.Pos.Y.ToString("0.00"))
                    .Append(" hp=").Append(placeable.Hp.ToString("0.0"))
                    .Append(" exists=").Append(placeable.Exists).Append('\n');
            }

            var shellViews = entry != null && entry.Shell != null ? entry.Shell.Views : null;
            if (shellViews != null)
            {
                sb.Append("boundPlaceables=").Append(shellViews.BoundPlaceableIds.Count).Append('\n');
                foreach (var id in shellViews.BoundPlaceableIds)
                {
                    var pv = shellViews.PlaceableViewFor(id);
                    sb.Append("placeableView ").Append(id);
                    if (pv != null)
                    {
                        sb.Append(" world=").Append(pv.WorldPosition)
                            .Append(" type=").Append(pv.Visual != null ? pv.Visual.ArtKey : "");
                    }

                    sb.Append('\n');
                }
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
            sb.Append("wave1Clear=").Append(_wave1Captured)
                .Append(" wave2Live=").Append(_wave2Captured)
                .Append(" shopPlaced=").Append(_shopAttempted).Append('\n');
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
            if (camera == null)
            {
                return;
            }

            const int width = 1920;
            const int height = 1080;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var prev = camera.targetTexture;
            var prevActive = RenderTexture.active;

            // ScreenSpaceOverlay is not drawn by Camera.Render. Park overlay canvases
            // on this camera for the capture so wave/scrip/civ HUD lands in the PNG.
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
    }
}
#endif

