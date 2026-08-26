using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Game.Input;
using RedHollow.Game.Net;
using RedHollow.Game.UI;
using RedHollow.Game.View;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 024 (T-24) — the play-mode pointer adapter. Ticket 023 pinned S3 placement at the
    /// wiring seam (<see cref="ShellControls.PointerAt"/> / <see cref="ShellControls.ClickGround"/>
    /// / <see cref="ShellControls.ClickPlaceable"/>) with a CALLER-SUPPLIED <c>zoneValid</c>;
    /// nothing feeds that seam from a real mouse and nothing answers <c>zoneValid</c> client-side.
    /// This ticket adds the three missing pieces and the pump wiring that composes them:
    ///
    ///  1. <see cref="PointerProjection"/> — screen point + camera → ground-plane <see cref="Vec2"/>
    ///     (pure; the same plane-projection the combat aim path documents);
    ///  2. <see cref="PlaceablePicker"/> — a ground point → the nearest STANDING placeable within a
    ///     pick radius (pure over <see cref="MatchState"/>; sold placeables are ground again);
    ///  3. <see cref="PlacementZoneOracle"/> — the client-side R-24 answer for the red-tint UX,
    ///     ADVISORY ONLY (the sim's verdict stays authoritative, R-51) and property-tested here
    ///     against the real <c>MatchSim.PurchasePlacement</c> so drift fails loudly;
    ///  4. pump integration — during planning the shell samples its own
    ///     <see cref="IInputSource"/>: the cursor's ground point flows to <c>PointerAt</c> with the
    ///     oracle's answer, and a fresh MouseLeft press becomes ONE click, routed by the T23 seam's
    ///     own precedence (ghost up → ground click; no ghost → placeable click within pick radius;
    ///     otherwise nothing). The DEVICE stays faked — the wiring is the pin, not the mouse.
    ///
    /// <b>Deliberately NOT pinned</b>: the shell's pick-radius value (only its consequences at
    /// distance zero), the red tint itself (GhostInvalid is the contract — T-12's rule), which
    /// pump-internal order the cursor sample and the click resolve in, and the mouse-button rows of
    /// <see cref="DefaultHeroInputMap"/> — T16 already locks that MouseLeft/Right/Middle produce no
    /// gameplay intent, and a locked test is not re-pinned here.
    /// </summary>
    [TestFixture]
    public class T24_PointerTests
    {
        private const double Step60Hz = 1.0 / 60.0;
        private const double SimTolerance = 1e-6;

        /// <summary>Float round trips through camera matrices are float-precise, not double-precise.</summary>
        private const double RayTolerance = 1e-3;

        private const string HostPeerId = "peer_host";
        private const string HostAccount = "acc_calamity";

        /// <summary>The well-known roots the shell and the scene builder compose under.</summary>
        private static readonly string[] ShellRootNames =
        {
            "RedHollow_Shell", "RedHollow_MatchViews", "RedHollow_Match",
        };

        private ShellBootstrap _shell;
        private InMemoryProfileStore _profiles;
        private FakeInputSource _input;
        private RenderTexture _cameraTarget;

        [TearDown]
        public void DestroyEverythingThisTestBuilt()
        {
            if (_shell != null)
            {
                try
                {
                    _shell.TearDown();
                }
                catch (Exception)
                {
                    // A stub or half-built shell must not turn a red test into a teardown error.
                }

                _shell = null;
            }

            foreach (var name in ShellRootNames)
            {
                for (var go = GameObject.Find(name); go != null; go = GameObject.Find(name))
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            if (_cameraTarget != null)
            {
                _cameraTarget.Release();
                UnityEngine.Object.DestroyImmediate(_cameraTarget);
                _cameraTarget = null;
            }
        }

        // ==========================================================================================
        //  1 — ray math: screen point + camera → ground-plane Vec2 (pure, scripted camera)
        // ==========================================================================================

        /// <summary>
        /// The round trip on the REAL top-down rig: a sim ground point, projected to the screen by
        /// the same camera <see cref="MatchSceneBuilder"/> builds for play (orthographic, over the
        /// play-area, steep top-down tilt), must come back as the same sim point through
        /// <see cref="PointerProjection.TryScreenToGround"/>. This is the exact path a play-mode
        /// cursor takes to become a <see cref="Vec2"/> for the T23 seam, and it must agree with
        /// <see cref="SimSpace"/> or the ghost follows a cursor into the wrong coordinates.
        /// </summary>
        [Test]
        public void A_screen_point_round_trips_to_its_sim_ground_point_through_the_top_down_camera()
        {
            var camera = BuildTopDownCamera();

            var points = new[]
            {
                new Vec2(0.0, 0.0),      // the team spawn — dead centre of the colony
                new Vec2(-12.0, 6.0),    // a shelter's own position
                new Vec2(7.5, -3.25),    // an arbitrary off-axis point (fractions survive)
            };

            foreach (var expected in points)
            {
                var screen = camera.WorldToScreenPoint(SimSpace.ToWorld(expected));

                var resolved = PointerProjection.TryScreenToGround(
                    camera, new Vector2(screen.x, screen.y), out var ground);

                Assert.That(resolved, Is.True,
                    "a cursor over the colony always has a ground point (" + expected + ")");
                Assert.That(ground.X, Is.EqualTo(expected.X).Within(RayTolerance),
                    "the ray lands back on the sim x it left from (" + expected + ")");
                Assert.That(ground.Y, Is.EqualTo(expected.Y).Within(RayTolerance),
                    "the ray lands back on the sim y it left from (" + expected + ")");
            }
        }

        /// <summary>
        /// A ray parallel to the floor never meets it: a camera looking at the horizon answers
        /// false, never a throw and never a made-up point — the combat aim path's own rule
        /// ("the cursor is on the horizon and has no ground point at all").
        /// </summary>
        [Test]
        public void A_ray_parallel_to_the_ground_has_no_ground_point()
        {
            var camera = BuildScriptedCamera(
                new Vector3(0f, 5f, -10f), Quaternion.identity); // looking level at +z

            var centre = new Vector2(camera.pixelWidth / 2f, camera.pixelHeight / 2f);

            Assert.That(PointerProjection.TryScreenToGround(camera, centre, out _), Is.False,
                "a horizon ray meets no ground — false, not a fabricated point");
        }

        /// <summary>
        /// A ground point BEHIND the camera is refused: a camera under the floor looking further
        /// down would only meet the plane at negative ray distance, which is not a place a cursor
        /// can point at.
        /// </summary>
        [Test]
        public void A_ground_point_behind_the_camera_is_refused()
        {
            var camera = BuildScriptedCamera(
                new Vector3(0f, -5f, 0f),
                Quaternion.LookRotation(Vector3.down, Vector3.forward)); // floor is behind it

            var centre = new Vector2(camera.pixelWidth / 2f, camera.pixelHeight / 2f);

            Assert.That(PointerProjection.TryScreenToGround(camera, centre, out _), Is.False,
                "the plane intersection is behind the camera — false, not an extrapolation");
        }

        /// <summary>
        /// No camera, no ground point — false rather than a throw, the same tolerance the combat
        /// aim path already extends to an unwired camera (aiming fails soft, never NREs per frame).
        /// </summary>
        [Test]
        public void A_missing_camera_answers_no_ground_point_rather_than_throwing()
        {
            Assert.That(PointerProjection.TryScreenToGround(null, Vector2.zero, out _), Is.False,
                "a session whose camera is not wired yet must not throw sixty times a second");
        }

        // ==========================================================================================
        //  2 — placeable picking: nearest standing placeable within the pick radius
        // ==========================================================================================

        /// <summary>Two standing placeables in range: the NEAREST one is the pick.</summary>
        [Test]
        public void The_nearest_standing_placeable_within_the_pick_radius_is_picked()
        {
            var state = new MatchState();
            AddPlaceable(state, "pl_near", new Vec2(1.0, 0.0), exists: true);
            AddPlaceable(state, "pl_far", new Vec2(2.0, 0.0), exists: true);

            Assert.That(PlaceablePicker.Pick(state, new Vec2(0.0, 0.0), 5.0), Is.EqualTo("pl_near"),
                "both stand in range; the nearer one wins the click");
        }

        /// <summary>
        /// Beyond the pick radius there is no pick — a click on empty ground must not sell the
        /// nearest thing on the map. An empty state answers null too, never a throw.
        /// </summary>
        [Test]
        public void Nothing_is_picked_beyond_the_pick_radius()
        {
            var state = new MatchState();
            AddPlaceable(state, "pl_a", new Vec2(10.0, 0.0), exists: true);

            Assert.That(PlaceablePicker.Pick(state, new Vec2(0.0, 0.0), 2.0), Is.Null,
                "10 units away with a 2-unit radius is empty ground, not a click on pl_a");
            Assert.That(PlaceablePicker.Pick(new MatchState(), new Vec2(0.0, 0.0), 2.0), Is.Null,
                "an empty map picks nothing and throws nothing");
        }

        /// <summary>
        /// R-22 — a sold placeable is GONE (<see cref="Placeable.Exists"/> false): it is never
        /// picked, even when it is the nearest entry, and a farther STANDING one wins instead.
        /// Selling the same tile twice through a stale view is the duplicate-refund bug the sim
        /// already guards; the picker must not feed it.
        /// </summary>
        [Test]
        public void A_sold_placeable_is_never_picked()
        {
            var state = new MatchState();
            AddPlaceable(state, "pl_sold", new Vec2(0.5, 0.0), exists: false);
            AddPlaceable(state, "pl_standing", new Vec2(2.0, 0.0), exists: true);

            Assert.That(PlaceablePicker.Pick(state, new Vec2(0.0, 0.0), 5.0), Is.EqualTo("pl_standing"),
                "the sold tile is ground again — the standing placeable behind it takes the click");

            state.Placeables["pl_standing"].Exists = false;
            Assert.That(PlaceablePicker.Pick(state, new Vec2(0.0, 0.0), 5.0), Is.Null,
                "with everything sold there is nothing to pick at all");
        }

        /// <summary>
        /// The boundary is INCLUSIVE: exactly at the pick radius still picks, matching the sim's
        /// own edge-inclusive auras ("standing exactly on the edge of the radius is inside it").
        /// </summary>
        [Test]
        public void A_placeable_exactly_at_the_pick_radius_is_picked()
        {
            var state = new MatchState();
            AddPlaceable(state, "pl_edge", new Vec2(3.0, 0.0), exists: true);

            Assert.That(PlaceablePicker.Pick(state, new Vec2(0.0, 0.0), 3.0), Is.EqualTo("pl_edge"),
                "distance == radius is a hit — the same inclusive edge the sim's auras use");
        }

        // ==========================================================================================
        //  3 — the zone oracle, property-tested against the REAL sim (advisory mirror, R-24/R-51)
        // ==========================================================================================

        /// <summary>
        /// The oracle's shipped radii ARE the sim's shipped radii — read off a fresh
        /// <see cref="MatchSim"/>, never spelled as literals here, so a retuned sim default that
        /// forgets the oracle fails this test instead of silently tinting ghosts wrong.
        /// </summary>
        [Test]
        public void The_oracle_defaults_mirror_the_sims_shipped_radii()
        {
            var sim = new MatchSim(new MatchState());
            var oracle = new PlacementZoneOracle(ColonyMap.V1());

            Assert.That(oracle.HotspotBuildingRadius, Is.EqualTo(sim.HotspotBuildingRadius),
                "hotspot-building radius: oracle default == sim default");
            Assert.That(oracle.EntryTunnelMouthRadius, Is.EqualTo(sim.EntryTunnelMouthRadius),
                "entry-tunnel-mouth radius: oracle default == sim default");
            Assert.That(oracle.PlaceableFootprintRadius, Is.EqualTo(sim.PlaceableFootprintRadius),
                "placeable-footprint radius: oracle default == sim default");
        }

        /// <summary>
        /// THE property test: across a map-wide grid over <see cref="ColonyMap.V1"/> plus targeted
        /// samples straddling every hotspot-building and tunnel-mouth edge, the oracle's answer
        /// equals the real <c>MatchSim.PurchasePlacement</c> verdict. Each sample gets a FRESH
        /// scratch sim (accepted placements must not accumulate into later samples) funded far past
        /// any catalog price (scrip must never confound the zone answer), in a planning phase (the
        /// phase gate must never fire). Anti-vacuity: the sampled set must contain both verdicts.
        /// </summary>
        [Test]
        public void The_oracle_agrees_with_the_sims_verdict_across_the_map()
        {
            var samples = new List<Vec2>();
            for (var x = -32.0; x <= 32.0; x += 4.0)
            {
                for (var y = -32.0; y <= 32.0; y += 4.0)
                {
                    samples.Add(new Vec2(x, y));
                }
            }

            // Straddle every exclusion edge: just inside and just outside each hotspot building
            // and each tunnel mouth, plus the exact centres. The radii are read off a scratch sim,
            // never typed here.
            var reference = NewScratchSim(out _);
            var map = reference.ColonyMap;
            foreach (var hotspot in map.Hotspots)
            {
                samples.Add(hotspot.Pos);
                samples.Add(new Vec2(hotspot.Pos.X + reference.HotspotBuildingRadius - 0.5, hotspot.Pos.Y));
                samples.Add(new Vec2(hotspot.Pos.X + reference.HotspotBuildingRadius + 0.5, hotspot.Pos.Y));
            }

            foreach (var tunnel in map.EntryTunnels)
            {
                samples.Add(tunnel);
                samples.Add(new Vec2(tunnel.X + reference.EntryTunnelMouthRadius - 0.5, tunnel.Y));
                samples.Add(new Vec2(tunnel.X + reference.EntryTunnelMouthRadius + 0.5, tunnel.Y));
            }

            var verdicts = new HashSet<bool>();
            foreach (var pos in samples)
            {
                var sim = NewScratchSim(out var state);
                var oracle = OracleMirroring(sim);

                var advised = oracle.WouldAccept(state, pos);
                var actual = sim.PurchasePlacement(new PurchaseRequest
                {
                    PlayerId = "p1",
                    PlaceableType = PlaceableType.Barricade,
                    Pos = pos,
                    ZoneValid = true, // deliberately a lie the sim must ignore (R-51)
                }).Accepted;

                Assert.That(advised, Is.EqualTo(actual),
                    "R-24 drift at " + pos + ": the oracle advised " + advised
                    + " but the sim's own verdict was " + actual);
                verdicts.Add(actual);
            }

            Assert.That(verdicts, Is.EquivalentTo(new[] { true, false }),
                "anti-vacuity: the sampled set must exercise both accept and reject");
        }

        /// <summary>
        /// The overlap exclusion, against LIVE state: with a deliberately placed standing obstacle
        /// and a SOLD one, the oracle agrees with the sim around both — the standing footprint
        /// blocks, the sold tile is colony ground again. Fresh sim per sample, as above.
        /// </summary>
        [Test]
        public void The_oracle_agrees_with_the_sim_around_standing_and_sold_placeables()
        {
            var standingAt = new Vec2(4.0, 5.0);
            var soldAt = new Vec2(-4.0, -5.0);

            var reference = NewScratchSim(out _);
            var clearance = reference.PlaceableFootprintRadius * 2.0;

            var samples = new List<Vec2>
            {
                standingAt,
                new Vec2(standingAt.X + (clearance - 0.5), standingAt.Y),
                new Vec2(standingAt.X + (clearance + 0.5), standingAt.Y),
                soldAt,
                new Vec2(soldAt.X + 1.0, soldAt.Y),
            };

            var verdicts = new HashSet<bool>();
            foreach (var pos in samples)
            {
                var sim = NewScratchSim(out var state);
                AddPlaceable(state, "pl_block", standingAt, exists: true);
                AddPlaceable(state, "pl_gone", soldAt, exists: false);

                var oracle = OracleMirroring(sim);

                var advised = oracle.WouldAccept(state, pos);
                var actual = sim.PurchasePlacement(new PurchaseRequest
                {
                    PlayerId = "p1",
                    PlaceableType = PlaceableType.Barricade,
                    Pos = pos,
                    ZoneValid = true,
                }).Accepted;

                Assert.That(advised, Is.EqualTo(actual),
                    "R-24 overlap drift at " + pos + ": oracle " + advised + ", sim " + actual);
                verdicts.Add(actual);
            }

            Assert.That(verdicts, Is.EquivalentTo(new[] { true, false }),
                "anti-vacuity: the obstacle samples must exercise both accept and reject");
        }

        /// <summary>
        /// The radii are DATA, not constants: retune the hotspot-building radius on both sides and
        /// the shared verdict flips at a point the shipped default accepts. An oracle that
        /// hardcodes 4.0 goes green on the grid test and red here.
        /// </summary>
        [Test]
        public void Retuned_radii_move_the_oracle_and_the_sim_together()
        {
            // 6 units from the saloon: outside the shipped building radius, inside a retuned one.
            var probe = new Vec2(-6.0, 6.0);

            var defaultSim = NewScratchSim(out var defaultState);
            var defaultOracle = OracleMirroring(defaultSim);
            Assert.That(defaultOracle.WouldAccept(defaultState, probe), Is.True,
                "sanity: the probe point is buildable at the shipped radii");

            var tunedSim = NewScratchSim(out var tunedState);
            tunedSim.HotspotBuildingRadius = tunedSim.HotspotBuildingRadius * 2.0;
            var tunedOracle = OracleMirroring(tunedSim);

            var advised = tunedOracle.WouldAccept(tunedState, probe);
            var actual = tunedSim.PurchasePlacement(new PurchaseRequest
            {
                PlayerId = "p1",
                PlaceableType = PlaceableType.Barricade,
                Pos = probe,
                ZoneValid = true,
            }).Accepted;

            Assert.That(actual, Is.False,
                "sanity: the doubled building radius swallows the probe point sim-side");
            Assert.That(advised, Is.EqualTo(actual),
                "the oracle read the RETUNED radius rather than a hardcoded default");
        }

        // ==========================================================================================
        //  4 — pump integration: the faked device's cursor and clicks reach the T23 seam
        // ==========================================================================================

        /// <summary>
        /// During planning the shell's own <see cref="IInputSource"/> cursor drives the ghost
        /// through <see cref="ShellControls.PointerAt"/>, carrying the ORACLE'S zone answer: over
        /// clear colony ground the ghost is valid, over a hotspot building it reads invalid
        /// (R-24's red tint is presentation; <see cref="PlanningScreenModel.GhostInvalid"/> is the
        /// contract). No control is invoked directly — the pump does everything.
        /// </summary>
        [Test]
        public void The_cursor_moves_the_ghost_with_the_oracles_zone_answer()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);
            BeginCheapestPlacement(shell);

            _input.Cursor = new Vector2(5f, 5f); // clear colony ground
            shell.Pump(0.0);

            Assert.That(shell.Planning.GhostPos, Is.EqualTo(new Vec2(5.0, 5.0)),
                "the sampled cursor reached PointerAt — the ghost follows the real pointer");
            Assert.That(shell.Planning.GhostInvalid, Is.False,
                "clear ground: the oracle answered valid and the wiring passed it through");

            var hotspot = match.State.Hotspots.Values.First();
            _input.Cursor = new Vector2((float)hotspot.Pos.X, (float)hotspot.Pos.Y);
            shell.Pump(0.0);

            Assert.That(shell.Planning.GhostPos.X, Is.EqualTo(hotspot.Pos.X).Within(SimTolerance),
                "the ghost followed the cursor onto the shelter (x)");
            Assert.That(shell.Planning.GhostPos.Y, Is.EqualTo(hotspot.Pos.Y).Within(SimTolerance),
                "the ghost followed the cursor onto the shelter (y)");
            Assert.That(shell.Planning.GhostInvalid, Is.True,
                "R-24: inside a hotspot building the oracle answers invalid — the red-tint state");
        }

        /// <summary>
        /// The oracle reads LIVE state, not the map alone: after a placement lands, hovering
        /// inside the standing placeable's footprint clearance reads invalid, and hovering clear
        /// of it reads valid again.
        /// </summary>
        [Test]
        public void Hovering_inside_a_standing_placeables_clearance_reads_invalid()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            // Land one placeable through the LOCKED T23 seam, then start a second ghost.
            BeginCheapestPlacement(shell);
            shell.Controls.ClickGround(new Vec2(5.0, 5.0), zoneValid: true);
            shell.Pump(0.0);
            Assert.That(match.State.PlaceableCount, Is.EqualTo(1), "sanity: the obstacle stands");

            BeginCheapestPlacement(shell);

            _input.Cursor = new Vector2(6f, 5f); // 1 unit away — inside the footprint clearance
            shell.Pump(0.0);
            Assert.That(shell.Planning.GhostInvalid, Is.True,
                "R-24: overlapping a STANDING placeable is invalid — the oracle read live state");

            _input.Cursor = new Vector2(12f, 0f); // well clear of everything
            shell.Pump(0.0);
            Assert.That(shell.Planning.GhostInvalid, Is.False,
                "clear of the obstacle the same ghost is valid again");
        }

        /// <summary>
        /// R-63 through the REAL pointer path: with a ghost up, a fresh MouseLeft press over clear
        /// ground is ONE ground click — one catalog-priced purchase lands at the cursor's ground
        /// point and the ghost clears. Placement IS UI, so MouseLeft driving it honours R-30's
        /// "mouse buttons stay UI" rather than violating it.
        /// </summary>
        [Test]
        public void A_mouse_click_places_at_the_cursors_ground_point()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            var item = CheapestItem(shell);
            BeginCheapestPlacement(shell);

            var scripBefore = match.State.Team.Scrip;
            var before = match.State.Placeables.Keys.ToList();

            _input.Cursor = new Vector2(5f, 5f);
            _input.Held.Add(PlayerKey.MouseLeft);
            shell.Pump(0.0);

            var placed = match.State.Placeables
                .Where(kv => !before.Contains(kv.Key))
                .Select(kv => kv.Value)
                .ToList();
            Assert.That(placed.Count, Is.EqualTo(1),
                "one press is ONE purchase through the pump's pointer wiring");
            Assert.That(placed[0].Type, Is.EqualTo(item.Type), "the ghosted item was placed");
            Assert.That(placed[0].Pos.X, Is.EqualTo(5.0).Within(SimTolerance),
                "placed at the cursor's ground point (x)");
            Assert.That(placed[0].Pos.Y, Is.EqualTo(5.0).Within(SimTolerance),
                "placed at the cursor's ground point (y)");
            Assert.That(placed[0].Exists, Is.True, "the fresh placement stands");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore - item.Cost),
                "R-23: the pool paid the catalog price, never a UI literal");
            Assert.That(shell.Planning.GhostActive, Is.False,
                "R-63: the accepted placement cleared the ghost");
        }

        /// <summary>
        /// A press is ONE click, not one per pump: holding MouseLeft across further pumps neither
        /// buys again nor — with the ghost now down and the cursor still over the fresh placeable —
        /// sells what was just placed. A held button re-fired every frame is exactly that
        /// buy-then-sell churn.
        /// </summary>
        [Test]
        public void A_held_mouse_button_is_one_click_not_one_per_pump()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            var item = CheapestItem(shell);
            BeginCheapestPlacement(shell);

            var scripBefore = match.State.Team.Scrip;
            _input.Cursor = new Vector2(5f, 5f);
            _input.Held.Add(PlayerKey.MouseLeft);
            shell.Pump(0.0);

            Assert.That(match.State.PlaceableCount, Is.EqualTo(1), "sanity: the press placed once");

            shell.Pump(0.0);
            shell.Pump(0.0);

            Assert.That(match.State.PlaceableCount, Is.EqualTo(1),
                "the STILL-HELD button clicked nothing again — the placement neither repeated "
                + "nor was the fresh placeable sold out from under the cursor");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore - item.Cost),
                "exactly one catalog price left the pool across all three pumps");
        }

        /// <summary>
        /// R-24 through the real path: a click over a hotspot building places nothing, charges
        /// nothing, keeps the ghost up for the retry and surfaces the modeled rejection — the T23
        /// invalid-zone behavior, now reached by a real cursor and the oracle's own answer.
        /// </summary>
        [Test]
        public void A_click_in_an_invalid_zone_places_nothing_and_keeps_the_ghost()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);
            BeginCheapestPlacement(shell);

            var hotspot = match.State.Hotspots.Values.First();
            var scripBefore = match.State.Team.Scrip;
            var countBefore = match.State.Placeables.Count;

            _input.Cursor = new Vector2((float)hotspot.Pos.X, (float)hotspot.Pos.Y);
            _input.Held.Add(PlayerKey.MouseLeft);
            shell.Pump(0.0);

            Assert.That(match.State.Placeables.Count, Is.EqualTo(countBefore),
                "R-24: the invalid-zone click placed nothing");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore),
                "R-24: the rejected purchase charged nothing");
            Assert.That(shell.Planning.GhostActive, Is.True,
                "R-63: the ghost stays up for the retry");
            Assert.That(shell.Planning.LastPurchaseRejection, Is.Not.Null.And.Not.Empty,
                "the modeled rejection reason is surfaced for the UI");
        }

        /// <summary>
        /// R-22 through the real path: with NO ghost up, a fresh press with the cursor on a
        /// standing placeable picks it (distance zero is within any pick radius) and sells it for
        /// the MODELED refund — the pump composed the picker with the T23
        /// <see cref="ShellControls.ClickPlaceable"/> seam.
        /// </summary>
        [Test]
        public void A_click_on_a_standing_placeable_with_no_ghost_sells_it()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            var before = match.State.Placeables.Keys.ToList();
            BeginCheapestPlacement(shell);
            shell.Controls.ClickGround(new Vec2(5.0, 5.0), zoneValid: true);
            shell.Pump(0.0);
            var placedId = match.State.Placeables.Keys.Single(k => !before.Contains(k));

            var expectedRefund = shell.Planning.SellRefundFor(placedId);
            Assert.That(expectedRefund, Is.GreaterThan(0), "sanity (R-22): the refund is positive");

            var scripBefore = match.State.Team.Scrip;
            var standingBefore = match.State.PlaceableCount;

            _input.Cursor = new Vector2(5f, 5f);
            _input.Held.Add(PlayerKey.MouseLeft);
            shell.Pump(0.0);

            Assert.That(match.State.PlaceableCount, Is.EqualTo(standingBefore - 1),
                "R-22: the clicked placeable no longer stands");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore + expectedRefund),
                "R-22: the pool was credited exactly the modeled refund");
            Assert.That(shell.Planning.LastSellRefused, Is.False,
                "an accepted sale raises no refusal flag");
        }

        /// <summary>
        /// Precedence, exactly as the T23 seam pins it (<c>ClickPlaceable</c> is ignored while a
        /// ghost is up): with a ghost active, a click over a STANDING placeable is a placement
        /// attempt — rejected by the overlap rule — and never a sale. The placeable survives, the
        /// ghost stays up.
        /// </summary>
        [Test]
        public void With_a_ghost_up_a_click_over_a_standing_placeable_is_a_placement_attempt_not_a_sale()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            var before = match.State.Placeables.Keys.ToList();
            BeginCheapestPlacement(shell);
            shell.Controls.ClickGround(new Vec2(5.0, 5.0), zoneValid: true);
            shell.Pump(0.0);
            var placedId = match.State.Placeables.Keys.Single(k => !before.Contains(k));

            BeginCheapestPlacement(shell);
            var scripBefore = match.State.Team.Scrip;
            var standingBefore = match.State.PlaceableCount;

            _input.Cursor = new Vector2(5f, 5f); // dead on the standing placeable
            _input.Held.Add(PlayerKey.MouseLeft);
            shell.Pump(0.0);

            Assert.That(match.State.Placeables[placedId].Exists, Is.True,
                "the ghost's click belongs to placement — the standing placeable was NOT sold");
            Assert.That(match.State.PlaceableCount, Is.EqualTo(standingBefore),
                "nothing was placed (overlap) and nothing was sold");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore),
                "no money moved either way");
            Assert.That(shell.Planning.GhostActive, Is.True,
                "the rejected ghost stays up for the retry");
            Assert.That(shell.Planning.LastSellRefused, Is.False,
                "no sell was even attempted — the refusal flag stays down");
        }

        /// <summary>
        /// With no ghost and no placeable near the cursor, a click is NOTHING: no purchase, no
        /// sale, no refused-sale flag (the click routed nowhere, it was not a refused command).
        /// R-30's mouse-buttons-are-UI, observed at the state level.
        /// </summary>
        [Test]
        public void A_no_ghost_click_on_clear_ground_does_nothing()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            var scripBefore = match.State.Team.Scrip;
            var countBefore = match.State.Placeables.Count;

            _input.Cursor = new Vector2(10f, -5f); // clear ground, nothing standing anywhere
            _input.Held.Add(PlayerKey.MouseLeft);
            shell.Pump(0.0);

            Assert.That(match.State.Placeables.Count, Is.EqualTo(countBefore),
                "nothing was placed by a ghostless click");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore), "no money moved");
            Assert.That(shell.Planning.LastSellRefused, Is.False,
                "the click routed NOWHERE — it must not have been fired as a doomed sell");
        }

        // ==========================================================================================
        //  5 — combat: mouse clicks stay UI-only and the gameplay input path keeps working
        // ==========================================================================================

        /// <summary>
        /// R-30 in combat: the planning pointer path is planning-phase only, so a combat-phase
        /// click over a standing placeable sells nothing and buys nothing — and the gameplay input
        /// path is untouched by the adapter: held W (with MouseLeft still held) walks the local
        /// hero forward through the pump exactly as T22 pinned, cursor aiming and all.
        /// (T16 already locks that the mouse buttons produce no gameplay INTENT; this pins that
        /// the new pump wiring adds no gameplay EFFECT either.)
        /// </summary>
        [Test]
        public void Combat_clicks_touch_no_gameplay_and_the_hero_still_walks()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            // A placeable to tempt the sell path with, then into combat through the real button.
            BeginCheapestPlacement(shell);
            shell.Controls.ClickGround(new Vec2(5.0, 5.0), zoneValid: true);
            shell.Pump(0.0);
            shell.Controls.PlanningReadyButton.onClick.Invoke();
            shell.Pump(0.0);
            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Combat),
                "sanity (R-03): READY UP opened combat");

            var scripBefore = match.State.Team.Scrip;
            var standingBefore = match.State.PlaceableCount;

            _input.Cursor = new Vector2(5f, 5f); // dead on the standing placeable
            _input.Held.Add(PlayerKey.MouseLeft);
            shell.Pump(0.0);
            shell.Pump(0.0);

            Assert.That(match.State.PlaceableCount, Is.EqualTo(standingBefore),
                "R-22/R-30: a combat click sells nothing — selling is a planning action");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore),
                "no combat click moves money");

            var hero = OwnHero(match.State);
            Assert.That(hero, Is.Not.Null, "sanity: the local hero is seated");
            var before = hero.Pos;

            _input.Held.Add(PlayerKey.W);
            for (var i = 0; i < 5; i++)
            {
                shell.Pump(Step60Hz);
            }

            Assert.That(hero.Pos.Y, Is.GreaterThan(before.Y),
                "R-30: held W still walks the hero forward with the pointer adapter wired in and "
                + "MouseLeft held — the combat input path is untouched");
        }

        // ==========================================================================================
        //  thinness — the adapter pieces are plain C# inside the scanned assembly (T-10)
        // ==========================================================================================

        /// <summary>
        /// T-10's invariant at this ticket's seams: projection, picker and oracle are plain C#
        /// (never MonoBehaviours) and compile into the shell assembly the Cecil scan reads.
        /// </summary>
        [Test]
        public void The_pointer_adapter_pieces_are_plain_C_sharp_in_the_scanned_assembly()
        {
            foreach (var type in new[]
                     {
                         typeof(PointerProjection), typeof(PlaceablePicker),
                         typeof(PlacementZoneOracle),
                     })
            {
                Assert.That(typeof(MonoBehaviour).IsAssignableFrom(type), Is.False,
                    "T-10: " + type.Name + " is a plain C# type, never a MonoBehaviour");
                Assert.That(type.Assembly, Is.SameAs(typeof(ShellBootstrap).Assembly),
                    "T-10: " + type.Name + " compiles into the scanned shell assembly");
            }
        }

        // ==========================================================================================
        //  scenario builders and helpers
        // ==========================================================================================

        /// <summary>
        /// The play rig's own camera: <see cref="MatchSceneBuilder"/> over the shipped map, given a
        /// render target so screen space has real pixel dimensions in EditMode (an unrendered
        /// camera has none, and the round trip needs a defined viewport, not a particular size).
        /// </summary>
        private Camera BuildTopDownCamera()
        {
            var scene = MatchSceneBuilder.Build(ColonyMap.V1(), null);
            return WithRenderTarget(scene.Camera);
        }

        /// <summary>A bare scripted camera for the degenerate-ray cases.</summary>
        private Camera BuildScriptedCamera(Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("RedHollow_Match"); // the teardown convention's root name
            go.transform.position = position;
            go.transform.rotation = rotation;

            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 34f;
            return WithRenderTarget(camera);
        }

        private Camera WithRenderTarget(Camera camera)
        {
            _cameraTarget = new RenderTexture(640, 480, 0);
            camera.targetTexture = _cameraTarget;
            return camera;
        }

        /// <summary>A standing (or sold) placeable written straight into state, for the pure tests.</summary>
        private static void AddPlaceable(MatchState state, string id, Vec2 pos, bool exists)
        {
            state.Placeables[id] = new Placeable
            {
                Id = id,
                Type = PlaceableType.Barricade,
                Pos = pos,
                Exists = exists,
                PurchaseCost = 20,
            };
        }

        /// <summary>
        /// A fresh scratch sim for one oracle sample: the shipped map, a planning phase, and a pool
        /// funded so far past any catalog price that scrip can never confound the zone answer.
        /// </summary>
        private static MatchSim NewScratchSim(out MatchState state)
        {
            var map = ColonyMap.V1();
            state = map.CreateMatchState();
            state.Phase = MatchPhase.Planning;
            state.Team.Scrip = 1000000;

            var sim = new MatchSim(state);
            sim.ColonyMap = map;
            return sim;
        }

        /// <summary>An oracle wired the way the shell must wire one: the SIM'S radii, copied.</summary>
        private static PlacementZoneOracle OracleMirroring(MatchSim sim)
        {
            return new PlacementZoneOracle(sim.ColonyMap)
            {
                HotspotBuildingRadius = sim.HotspotBuildingRadius,
                EntryTunnelMouthRadius = sim.EntryTunnelMouthRadius,
                PlaceableFootprintRadius = sim.PlaceableFootprintRadius,
            };
        }

        /// <summary>A fresh shell on S1: loopback, in-memory profiles, a scripted input source.</summary>
        private ShellBootstrap NewShell()
        {
            _profiles = new InMemoryProfileStore();
            _input = new FakeInputSource();

            _shell = new ShellBootstrap(new ShellBootstrapOptions
            {
                Transport = new LoopbackNetTransport(),
                Profiles = _profiles,
                SimConfig = new SimConfig(),
                LocalPeerId = HostPeerId,
                LocalAccountId = HostAccount,
                InputSource = _input,
            });

            return _shell;
        }

        /// <summary>A shell with the host seated — the S2 starting point (T21/T23's helper).</summary>
        private ShellBootstrap NewHostedShell()
        {
            var shell = NewShell();

            shell.Session.StartHost(new NetPeer
            {
                PeerId = HostPeerId,
                AccountId = HostAccount,
                HeroClass = HeroClass.Gunslinger,
                IsHost = true,
            });

            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "sanity (R-50): hosting opens a lobby");

            return shell;
        }

        private static HostedMatch StartMatch(ShellBootstrap shell)
        {
            Assert.That(shell.Session.TryStartMatch(HostPeerId), Is.True,
                "sanity (R-50): the host starts the match");

            var match = shell.Session.Match;
            Assert.That(match, Is.Not.Null, "the session holds the live match");
            return match;
        }

        /// <summary>Clear the live wave and ride S5's hold into S3 (T21/T23's recipe).</summary>
        private static void ReachPlanning(ShellBootstrap shell, HostedMatch match)
        {
            shell.Pump(0.0);
            KillWave(match, match.State.Wave.LivingMonsterIds.ToList());
            shell.Pump(0.0);

            var holdSteps = (int)Math.Ceiling(shell.Router.InterstitialSeconds / Step60Hz) + 2;
            for (var i = 0; i < holdSteps; i++)
            {
                shell.Pump(Step60Hz);
            }

            Assert.That(shell.Router.Screen, Is.EqualTo(UiScreen.Planning),
                "sanity (R-04): the interstitial fell back to planning");
            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Planning),
                "sanity: the sim is in its planning phase");
        }

        /// <summary>Clears a wave through the sim's own kill command (T-12/T-21's helper).</summary>
        private static void KillWave(HostedMatch match, IEnumerable<string> monsterIds)
        {
            foreach (var id in monsterIds.ToList())
            {
                match.Sim.RecordMonsterKill(new MonsterKillRequest
                {
                    MonsterId = id,
                    MonsterType =
                        match.State.Monsters.TryGetValue(id, out var monster) ? monster.Type : null,
                    Bounty = 0,
                });
            }
        }

        private static ShopItem CheapestItem(ShellBootstrap shell)
        {
            var item = shell.Planning.ShopItems.OrderBy(i => i.Cost).First();
            Assert.That(item.Affordable, Is.True, "sanity: the cheapest item is affordable");
            return item;
        }

        /// <summary>Start the cheapest item's ghost through its locked T23 shop button.</summary>
        private static void BeginCheapestPlacement(ShellBootstrap shell)
        {
            var item = CheapestItem(shell);
            shell.Controls.ShopItemButton(item.Type).onClick.Invoke();
            Assert.That(shell.Planning.GhostActive, Is.True,
                "sanity (R-63): the shop click started the ghost");
        }

        private static Hero OwnHero(MatchState state)
        {
            return state.Heroes.Values.FirstOrDefault(
                h => string.Equals(h.AccountId, HostAccount, StringComparison.Ordinal));
        }

        // ==========================================================================================
        //  test doubles
        // ==========================================================================================

        /// <summary>
        /// The FAKED device layer (T22/T23's shape): held keys — mouse buttons included — plus a
        /// cursor already resolved to a ground point, exactly what a play-mode device source
        /// produces via <see cref="PointerProjection"/>. The wiring is the pin, not the mouse.
        /// </summary>
        private sealed class FakeInputSource : IInputSource
        {
            public readonly HashSet<PlayerKey> Held = new HashSet<PlayerKey>();
            public Vector2 Cursor;

            public InputSnapshot Sample()
            {
                var snapshot = new InputSnapshot { CursorGroundPoint = Cursor };
                foreach (var key in Held)
                {
                    snapshot.Pressed.Add(key);
                }

                return snapshot;
            }
        }
    }
}
