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
using UnityEditor.SceneManagement;
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
        private const string TurretShotPath = "/workspace/unity/shots/turret-shot.png";
        private const string TurretLastHitPath = "/workspace/unity/shots/turret-lasthit.png";
        private const string EndShotPath = "/workspace/unity/shots/wave10-end.png";
        private const string HotspotFrontsPath = "/workspace/unity/shots/hotspot-fronts.png";
        private const string LookPath = "/workspace/unity/shots/lykos-look.png";
        private const string LitPath = "/workspace/unity/shots/lykos-lit.png";
        private const string Lit2Path = "/workspace/unity/shots/lykos-lit2.png";
        private const string Lit3Path = "/workspace/unity/shots/lykos-lit3.png";
        private const string UnitsPath = "/workspace/unity/shots/units-visible.png";
        private const double MatchTimeoutSeconds = 240.0;

        private static double _enteredAt;
        private static bool _driving;
        private static bool _wave1Captured;
        private static bool _readySent;
        private static bool _wave2Captured;
        private static bool _shopAttempted;
        private static bool _shopShot;
        private static double _planningSince;
        private static bool _holdFire;
        private static bool _turretProofCaptured;
        private static bool _proofPhaseDone;
        private static bool _wave3Captured;
        private static int _readiedWave;
        private static int _shoppedWave;
        private static int _furthestWave;
        private static double _wave2CombatSince;
        private static readonly Dictionary<string, double> Wave2HpAtHold = new Dictionary<string, double>();
        private static string _turretProofLine = "";
        private static bool _turretLastHitCaptured;
        private static string _turretLastHitLine = "";
        private static int _scripAtHold;
        private static int _livingAtHold;
        private static double _holdFireSince;
        private static readonly List<string> PurchaseLog = new List<string>();
        private static readonly StringBuilder Logs = new StringBuilder();
        private static bool _hotspotFrontsCaptured;
        private static bool _lookCaptured;
        private static string _playMode = "full";

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
                    var body = File.ReadAllText(RequestPath).Trim();
                    File.Delete(RequestPath);
                    _playMode = string.IsNullOrEmpty(body) ? "full" : body;
                    File.WriteAllText(ArmedPath, _playMode);
                    File.WriteAllText(StatusPath, "entering-play mode=" + _playMode + "\n");
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
                _holdFire = false;
                _turretProofCaptured = false;
                _proofPhaseDone = false;
                _wave3Captured = false;
                _readiedWave = 0;
                _shoppedWave = 0;
                _furthestWave = 0;
                _wave2CombatSince = 0;
                Wave2HpAtHold.Clear();
                _turretProofLine = "";
                _turretLastHitCaptured = false;
                _turretLastHitLine = "";
                _scripAtHold = 0;
                _livingAtHold = 0;
                _holdFireSince = 0;
                PurchaseLog.Clear();
                _hotspotFrontsCaptured = false;
                _lookCaptured = false;
                const string MatchScene = "Assets/Scenes/RedHollow.unity";
                if (SceneManager.GetActiveScene().path != MatchScene)
                {
                    EditorSceneManager.OpenScene(MatchScene);
                }

                EditorApplication.isPlaying = true;
                return;
            }

            if (!File.Exists(ArmedPath) || !EditorApplication.isPlaying)
            {
                return;
            }

            try
            {
                var disk = File.ReadAllText(ArmedPath).Trim();
                if (!string.IsNullOrEmpty(disk))
                {
                    _playMode = disk;
                }
            }
            catch (Exception)
            {
            }

            if (_enteredAt < 1.0)
            {
                _enteredAt = EditorApplication.timeSinceStartup;
                return;
            }

            var elapsed = EditorApplication.timeSinceStartup - _enteredAt;
            DriveCombatInput();
            DriveWaveProgression();
            TryCaptureHotspotFronts(elapsed);
            TryCaptureLook(elapsed);

            var timedOut = elapsed >= MatchTimeoutSeconds;
            var matchOver = MatchIsOver();
            var frontsOnlyDone = _playMode == "fronts" && _hotspotFrontsCaptured;
            var lookOnlyDone = (_playMode == "look" || _playMode == "lit" || _playMode == "lit2"
                    || _playMode == "lit3" || _playMode == "units")
                && _lookCaptured;
            // Turret last-hit is already proven. Stay in Play until victory, defeat, or timeout
            // so autoplay can finish a 10-wave run (or dump the leak if it cannot).
            // "fronts" mode exits after the hotspot-front dump so art wiring can be checked
            // without another 10-wave campaign. "look" dumps wave-1 Game view (no victory
            // overlay) and exits so a cavern pass does not wait on a 10-wave run.
            if (!timedOut && !matchOver && !frontsOnlyDone && !lookOnlyDone)
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
            if (_holdFire)
            {
                // Turret-proof window: SPACE/Q/E off so an HP drop cannot be the gunslinger.
                OverlayInputSource.CursorOverride = new Vector2(10f, 8f);
                ParkHeroEast(match);
                return;
            }

            OverlayInputSource.ExtraHeld.Add(PlayerKey.Space);
            OverlayInputSource.ExtraHeld.Add(PlayerKey.Q);
            OverlayInputSource.ExtraHeld.Add(PlayerKey.E);

            // Units recapture: keep the gunslinger in the spawn courtyard so the
            // follow cam sits in open street, not inside a west-lane GridHab.
            if (_playMode == "units")
            {
                OverlayInputSource.CursorOverride = new Vector2(6f, 10f);
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

            if (match.State.Wave != null && match.State.Wave.Number > _furthestWave)
            {
                _furthestWave = match.State.Wave.Number;
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
                if (_shoppedWave != match.State.Wave.Number)
                {
                    DriveShopPurchases(shell, match);
                    _shoppedWave = match.State.Wave.Number;
                    _planningSince = EditorApplication.timeSinceStartup;
                    PurchaseLog.Add("shopped planning-wave=" + match.State.Wave.Number
                        + " scrip=" + match.State.Team.Scrip
                        + " placeables=" + match.State.PlaceableCount);
                }

                var onPlanningUi = shell.Router != null
                    && shell.Router.Screen == UiScreen.Planning;
                if (!_shopShot && onPlanningUi)
                {
                    DumpCamera(Camera.main, ShopPath);
                    _shopShot = true;
                }

                // Place-then-ready. Short settle so the S3 shop bar can paint; never hang
                // autoplay if the router sticks on S5.
                var waited = _planningSince > 0
                    && (EditorApplication.timeSinceStartup - _planningSince) >= 1.5;
                if (_shoppedWave == match.State.Wave.Number
                    && _readiedWave != match.State.Wave.Number
                    && (_shopShot || waited))
                {
                    shell.Planning.ReadyUp();
                    _readySent = true;
                    _readiedWave = match.State.Wave.Number;
                    PurchaseLog.Add("ready-up wave=" + match.State.Wave.Number);
                }
            }

            if (!_wave2Captured && Wave2Live(match))
            {
                DumpCamera(Camera.main, Wave2Path);
                _wave2Captured = true;
                _wave2CombatSince = EditorApplication.timeSinceStartup;
                SnapshotLivingHp(match.State);
            }

            if (_holdFire && match.State.Phase == MatchPhase.Combat)
            {
                if (!_turretProofCaptured)
                {
                    TryCaptureTurretProof(match);
                }

                if (!_turretLastHitCaptured)
                {
                    TryCaptureTurretLastHit(match);
                }

                var waited = _holdFireSince > 0
                    && (EditorApplication.timeSinceStartup - _holdFireSince) >= 18.0;
                if (waited && !_turretLastHitCaptured)
                {
                    if (!_turretProofCaptured)
                    {
                        _turretProofLine = "turretProof=False timeout after 18s of wave-2 hold-fire";
                    }

                    _turretLastHitLine = "turretLastHit=False timeout after 18s of hold-fire"
                        + " living=" + match.State.Wave.LivingMonsterIds.Count
                        + " scrip=" + match.State.Team.Scrip
                        + " scripAtHold=" + _scripAtHold;
                    PurchaseLog.Add(_turretLastHitLine);
                    _holdFire = false;
                    _proofPhaseDone = true;
                }

                if (_turretLastHitCaptured)
                {
                    _holdFire = false;
                    _proofPhaseDone = true;
                }
            }

            if (!_wave3Captured
                && match.State.Phase == MatchPhase.Combat
                && match.State.Wave.Number >= 3
                && match.State.Wave.LivingMonsterIds.Count > 0)
            {
                _wave3Captured = true;
                PurchaseLog.Add("wave3 live monsters=" + match.State.Wave.LivingMonsterIds.Count);
            }
        }

        private static void ParkHeroEast(HostedMatch match)
        {
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

            // Walk toward +x so the gunslinger is not standing on the west lane the turret covers.
            if (hero.Pos.X < 8.0)
            {
                OverlayInputSource.ExtraHeld.Add(PlayerKey.D);
            }
        }

        private static void SnapshotLivingHp(MatchState state)
        {
            Wave2HpAtHold.Clear();
            foreach (var monster in state.Monsters.Values)
            {
                if (monster != null && monster.Alive)
                {
                    Wave2HpAtHold[monster.Id] = monster.Hp;
                }
            }

            _scripAtHold = state.Team != null ? state.Team.Scrip : 0;
            _livingAtHold = state.Wave != null ? state.Wave.LivingMonsterIds.Count : 0;
        }

        /// <summary>
        /// A turret shot is 20 dmg (R-23). Gunslinger SPACE is 25. With SPACE released, an HP
        /// drop of ~20 on a living wave-2 monster is the turret (we bought no trap).
        /// </summary>
        private static void TryCaptureTurretProof(HostedMatch match)
        {
            foreach (var monster in match.State.Monsters.Values)
            {
                if (monster == null)
                {
                    continue;
                }

                double before;
                if (!Wave2HpAtHold.TryGetValue(monster.Id, out before))
                {
                    continue;
                }

                var dropped = before - monster.Hp;
                if (dropped < 19.0)
                {
                    continue;
                }

                _turretProofCaptured = true;
                _turretProofLine = "turretProof=True monster=" + monster.Id
                    + " type=" + monster.Type
                    + " hp=" + before.ToString("0.0") + "->" + monster.Hp.ToString("0.0")
                    + " drop=" + dropped.ToString("0.0")
                    + " spaceHeld=False";
                DumpCamera(Camera.main, TurretShotPath);
                PurchaseLog.Add(_turretProofLine);
                return;
            }
        }

        /// <summary>
        /// A turret last-hit is 20 dmg emptying a body (R-23) PLUS the kill command
        /// (RecordMonsterKill): the monster leaves the living roster and the bounty is paid.
        /// SPACE is still released, so a reap here cannot be the gunslinger.
        /// </summary>
        private static void TryCaptureTurretLastHit(HostedMatch match)
        {
            var living = match.State.Wave.LivingMonsterIds;
            foreach (var monster in match.State.Monsters.Values)
            {
                if (monster == null)
                {
                    continue;
                }

                double before;
                if (!Wave2HpAtHold.TryGetValue(monster.Id, out before))
                {
                    continue;
                }

                var emptied = monster.Hp <= 0.0;
                var offRoster = !living.Contains(monster.Id);
                if (!emptied || !offRoster)
                {
                    continue;
                }

                var scripNow = match.State.Team.Scrip;
                _turretLastHitCaptured = true;
                _turretLastHitLine = "turretLastHit=True monster=" + monster.Id
                    + " type=" + monster.Type
                    + " hp=" + before.ToString("0.0") + "->" + monster.Hp.ToString("0.0")
                    + " alive=" + monster.Alive
                    + " offRoster=True"
                    + " livingAtHold=" + _livingAtHold
                    + " livingNow=" + living.Count
                    + " scripAtHold=" + _scripAtHold
                    + " scripNow=" + scripNow
                    + " bountyDelta=" + (scripNow - _scripAtHold)
                    + " spaceHeld=False";
                DumpCamera(Camera.main, TurretLastHitPath);
                PurchaseLog.Add(_turretLastHitLine);
                return;
            }
        }

        /// <summary>
        /// Drive the locked T-23 shop seam every planning phase. Spend leftover scrip on more
        /// than one barricade and turret (spikes/dynamite too) so later waves cannot walk every
        /// civilian down. Catalog rows only — never an invented placeable.
        /// </summary>
        private static void DriveShopPurchases(ShellBootstrap shell, HostedMatch match)
        {
            _shopAttempted = true;
            var oracle = ZoneOracleFor(match);
            var cart = new[]
            {
                // Wave-2 tunnels are west+east: wall those first, then a turret, then the
                // remaining lanes. Four walls before a turret spent the opening stake and
                // left wave 2 with no gun covering the chew.
                PlaceableType.Barricade, PlaceableType.Barricade,
                PlaceableType.Turret,
                PlaceableType.Barricade, PlaceableType.Barricade,
                PlaceableType.Turret,
                PlaceableType.SpikeTrap, PlaceableType.SpikeTrap,
                PlaceableType.SpikeTrap, PlaceableType.SpikeTrap,
                PlaceableType.DynamiteTrap, PlaceableType.DynamiteTrap,
                PlaceableType.Barricade, PlaceableType.Barricade,
                PlaceableType.Turret,
                PlaceableType.MedStation,
            };

            for (var i = 0; i < cart.Length; i++)
            {
                shell.Planning.Refresh();
                var type = cart[i];
                var stats = match.Sim != null ? match.Sim.Config.Placeables.TryGet(type) : null;
                if (stats != null && match.State.Team.Scrip < stats.Cost)
                {
                    continue;
                }

                TryBuy(shell, match, oracle, type);
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
            if (!TryOpenGround(oracle, match.State, placeableType, out pos))
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
        /// Lane-on-path first so a wall actually crosses the next wave's walk (west/east/north/
        /// south tunnels → nearest shelter). Radii come from the live sim via the T-24 oracle —
        /// never a second copy of R-24.
        /// </summary>
        private static bool TryOpenGround(
            PlacementZoneOracle oracle, MatchState state, string placeableType, out Vec2 pos)
        {
            var preferred = PreferredGround(placeableType);
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

        /// <summary>
        /// Points sit on (or cover) the four v1 tunnel→shelter walks. Computed from ColonyMap.V1
        /// tunnels and hotspot positions: west (-30,0)→saloon, east (30,0)→chapel,
        /// north (0,30)→chapel, south (0,-30)→homestead.
        /// </summary>
        private static Vec2[] PreferredGround(string placeableType)
        {
            if (placeableType == PlaceableType.Barricade)
            {
                return new[]
                {
                    new Vec2(-22.8, 2.4),
                    new Vec2(22.4, 3.6),
                    new Vec2(4.4, 21.6),
                    new Vec2(0.8, -23.2),
                    new Vec2(-19.56, 3.48),
                    new Vec2(18.98, 5.22),
                    new Vec2(6.38, 17.82),
                    new Vec2(1.16, -20.14),
                    new Vec2(-16.5, 4.5),
                    new Vec2(15.75, 6.75),
                };
            }

            if (placeableType == PlaceableType.Turret)
            {
                return new[]
                {
                    new Vec2(-18.0, 0.5),
                    new Vec2(18.0, 0.5),
                    new Vec2(0.0, 16.0),
                    new Vec2(4.5, -18.0),
                    new Vec2(-12.0, 0.0),
                    new Vec2(12.0, 2.0),
                    new Vec2(8.0, 12.0),
                };
            }

            if (placeableType == PlaceableType.SpikeTrap
                || placeableType == PlaceableType.DynamiteTrap)
            {
                return new[]
                {
                    new Vec2(-26.04, 1.32),
                    new Vec2(25.82, 1.98),
                    new Vec2(2.42, 25.38),
                    new Vec2(0.44, -26.26),
                    new Vec2(-16.5, 4.5),
                    new Vec2(15.75, 6.75),
                    new Vec2(8.25, 14.25),
                    new Vec2(1.5, -17.25),
                };
            }

            return new[]
            {
                new Vec2(6.0, 0.0),
                new Vec2(-6.0, -5.0),
                new Vec2(8.0, -4.0),
                new Vec2(0.0, 8.0),
            };
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

        private static bool MatchIsOver()
        {
            var match = LiveMatch();
            return match != null && match.State != null && match.State.IsOver;
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
                sb.Append("turretShot=").Append(TurretShotPath)
                    .Append(" exists=").Append(File.Exists(TurretShotPath)).Append('\n');
                sb.Append("turretLastHitShot=").Append(TurretLastHitPath)
                    .Append(" exists=").Append(File.Exists(TurretLastHitPath)).Append('\n');
                DumpCamera(cam, EndShotPath);
                sb.Append("endShot=").Append(EndShotPath).Append('\n');
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
            sb.Append("playMode=").Append(_playMode).Append('\n');
            DumpFacades(sb);
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
            var chewing = 0;
            foreach (var monster in state.Monsters.Values)
            {
                if (monster == null || !monster.Alive || string.IsNullOrEmpty(monster.TargetId))
                {
                    continue;
                }

                Placeable wall;
                if (state.Placeables.TryGetValue(monster.TargetId, out wall)
                    && wall != null
                    && wall.Exists
                    && wall.IsBarricade)
                {
                    chewing++;
                }
            }

            sb.Append("chewingBarricades=").Append(chewing)
                .Append(" pathOracle=BarricadePathOracle").Append('\n');
            sb.Append("wave1Captured=").Append(_wave1Captured)
                .Append(" readySent=").Append(_readySent)
                .Append(" wave2Captured=").Append(_wave2Captured)
                .Append(" shopAttempted=").Append(_shopAttempted)
                .Append(" shopShot=").Append(_shopShot)
                .Append(" furthestWave=").Append(_furthestWave)
                .Append(" shoppedWave=").Append(_shoppedWave).Append('\n');
            var deadInSim = 0;
            foreach (var m in state.Monsters.Values)
            {
                if (m != null && !m.Alive)
                {
                    deadInSim++;
                }
            }
            sb.Append("deadInSim=").Append(deadInSim)
                .Append(" livingRoster=").Append(state.Wave.LivingMonsterIds.Count)
                .Append(" scripAtHold=").Append(_scripAtHold).Append('\n');
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
                .Append(" shopPlaced=").Append(_shopAttempted)
                .Append(" turretProof=").Append(_turretProofCaptured)
                .Append(" turretLastHit=").Append(_turretLastHitCaptured)
                .Append(" wave3Live=").Append(_wave3Captured).Append('\n');
            if (!string.IsNullOrEmpty(_turretProofLine))
            {
                sb.Append(_turretProofLine).Append('\n');
            }
            if (!string.IsNullOrEmpty(_turretLastHitLine))
            {
                sb.Append(_turretLastHitLine).Append('\n');
            }
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

        private static void TryCaptureHotspotFronts(double elapsed)
        {
            if (_hotspotFrontsCaptured || elapsed < 1.6)
            {
                return;
            }

            var match = LiveMatch();
            if (match == null || match.State == null)
            {
                return;
            }

            DumpCamera(Camera.main, HotspotFrontsPath);
            _hotspotFrontsCaptured = true;
            PurchaseLog.Add("hotspot-fronts shot elapsed=" + elapsed.ToString("0.00")
                + " phase=" + match.State.Phase
                + " wave=" + (match.State.Wave != null ? match.State.Wave.Number : 0));
        }

        /// <summary>
        /// Wave-1 Game-camera dump with no victory overlay. Used by playtest mode "look".
        /// </summary>
        private static void TryCaptureLook(double elapsed)
        {
            if (_lookCaptured || elapsed < 1.7)
            {
                return;
            }

            var match = LiveMatch();
            if (match == null || match.State == null || match.State.IsOver)
            {
                return;
            }

            if (match.State.Wave == null || match.State.Wave.Number != 1)
            {
                return;
            }

            if (_playMode == "units")
            {
                if (match.State.Phase != MatchPhase.Combat
                    || match.State.Wave.LivingMonsterIds.Count == 0)
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

                // Courtyard recapture: pin the Game camera on the hero (held at
                // spawn) so hab sides + deck read, never a kit-wall clipping plane.
                var cam = Camera.main;
                MatchSceneBuilder.PlaceOver(cam, SimSpace.ToWorld(hero.Pos));
            }

            var lookPath = _playMode == "units" ? UnitsPath
                : _playMode == "lit3" ? Lit3Path
                : _playMode == "lit2" ? Lit2Path
                : _playMode == "lit" ? LitPath
                : LookPath;
            DumpCamera(Camera.main, lookPath);
            if (_playMode == "units" && File.Exists(lookPath))
            {
                var clean = "/workspace/unity/shots/units-visible-clean.png";
                File.Copy(lookPath, clean, true);
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                File.Copy(lookPath, "/workspace/unity/shots/progress-" + stamp + ".png", true);
            }
            _lookCaptured = true;
            PurchaseLog.Add("lykos-look shot path=" + lookPath
                + " elapsed=" + elapsed.ToString("0.00")
                + " phase=" + match.State.Phase
                + " wave=" + match.State.Wave.Number
                + " living=" + match.State.Wave.LivingMonsterIds.Count
                + " camPos=" + (Camera.main != null ? Camera.main.transform.position.ToString() : "null")
                + " litShader=" + ViewLook.LitShaderName);
        }

        private static void DumpFacades(StringBuilder sb)
        {
            var n = 0;
            var markers = UnityEngine.Object.FindObjectsByType<HotspotMarkerView>(FindObjectsSortMode.None);
            for (var i = 0; i < markers.Length; i++)
            {
                var marker = markers[i];
                if (marker == null)
                {
                    continue;
                }

                var facade = marker.transform.Find("Habitat/Facade");
                sb.Append("facade ").Append(marker.HotspotId)
                    .Append(" present=").Append(facade != null);
                if (facade != null)
                {
                    n++;
                    var renderer = facade.GetComponent<Renderer>();
                    Texture tex = null;
                    if (renderer != null && renderer.sharedMaterial != null)
                    {
                        tex = renderer.sharedMaterial.mainTexture;
                    }

                    sb.Append(" pos=").Append(facade.position)
                        .Append(" scale=").Append(facade.lossyScale)
                        .Append(" tex=").Append(tex != null ? tex.name : "null");
                }

                sb.Append('\n');
            }

            sb.Append("facadeCount=").Append(n).Append('\n');
            sb.Append("hotspotFrontsShot=").Append(HotspotFrontsPath)
                .Append(" exists=").Append(File.Exists(HotspotFrontsPath)).Append('\n');
            sb.Append("lykosLookShot=").Append(LookPath)
                .Append(" exists=").Append(File.Exists(LookPath)).Append('\n');
            sb.Append("lykosLitShot=").Append(LitPath)
                .Append(" exists=").Append(File.Exists(LitPath)).Append('\n');
            sb.Append("lykosLit2Shot=").Append(Lit2Path)
                .Append(" exists=").Append(File.Exists(Lit2Path)).Append('\n');
            sb.Append("lykosLit3Shot=").Append(Lit3Path)
                .Append(" exists=").Append(File.Exists(Lit3Path)).Append('\n');
            DumpShaders(sb);
        }

        private static void DumpShaders(StringBuilder sb)
        {
            sb.Append("litShader=").Append(ViewLook.LitShaderName).Append('\n');
            var names = new[]
            {
                "Ground", "Wall_North", "Body", "Roof", "Plaza", "billboard", "Cliff_North_W",
            };
            for (var i = 0; i < names.Length; i++)
            {
                var go = GameObject.Find(names[i]);
                sb.Append("mesh ").Append(names[i]).Append(" present=").Append(go != null);
                if (go != null)
                {
                    var renderer = go.GetComponentInChildren<Renderer>();
                    var mat = renderer != null ? renderer.sharedMaterial : null;
                    var shader = mat != null && mat.shader != null ? mat.shader.name : "null";
                    sb.Append(" shader=").Append(shader)
                        .Append(" receive=").Append(renderer != null && renderer.receiveShadows);
                    Texture bump = null;
                    if (mat != null && mat.HasProperty("_BumpMap"))
                    {
                        bump = mat.GetTexture("_BumpMap");
                    }

                    sb.Append(" bump=").Append(bump != null ? bump.name : "none");
                }

                sb.Append('\n');
            }

            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (var i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                if (light == null || !light.enabled || light.type != LightType.Point)
                {
                    continue;
                }

                sb.Append("lantern ").Append(light.gameObject.name)
                    .Append(" intensity=").Append(light.intensity.ToString("0.0"))
                    .Append(" range=").Append(light.range.ToString("0.0"))
                    .Append(" unit=").Append(light.lightUnit)
                    .Append(" shadows=").Append(light.shadows)
                    .Append(" pos=").Append(light.transform.position)
                    .Append('\n');
            }
        }

        /// <summary>
        /// Overlay dumps park ScreenSpaceOverlay canvases onto the camera. Inactive Screen_*
        /// children still batch unless their CanvasRenderers are culled — the S1-on-S2 gap.
        /// </summary>
        private static void CullInactiveScreenRoots(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            var t = canvas.transform;
            for (var i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (child == null || child.gameObject.activeSelf)
                {
                    continue;
                }

                var name = child.name;
                if (!name.StartsWith("Screen_", StringComparison.Ordinal)
                    && name != "HUD_TopBar"
                    && name != "HUD_SelfBar")
                {
                    continue;
                }

                var renderers = child.GetComponentsInChildren<CanvasRenderer>(true);
                for (var r = 0; r < renderers.Length; r++)
                {
                    if (renderers[r] != null)
                    {
                        renderers[r].cull = true;
                    }
                }
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

            for (var i = 0; i < restored.Count; i++)
            {
                CullInactiveScreenRoots(restored[i]);
            }

            Canvas.ForceUpdateCanvases();
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

