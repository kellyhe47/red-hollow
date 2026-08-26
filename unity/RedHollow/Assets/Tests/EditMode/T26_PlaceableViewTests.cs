using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Game.Net;
using RedHollow.Game.UI;
using RedHollow.Game.View;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 026 (T-26) — placeable views and the world-anchored wireframe marker states. The
    /// final render audit found four gaps: (1) <see cref="MatchViewBinder"/> reconciles heroes and
    /// monsters but never placeables, so a purchased barricade/trap/turret/med station is invisible
    /// and a sell/break/destroy changes nothing on screen (wireframe S3 "Existing placeables
    /// shown"); (2) barricades have no damage readout (S4 "Barricades show HP bars when damaged");
    /// (3) <see cref="MatchSceneBuilder"/> places no entry-tunnel markers at all, so S3's "ACTIVE
    /// entry points pulse red" and S4's "monster spawn → entry point flare" have no world anchor
    /// even though the models already expose <see cref="PlanningScreenModel.PulsingEntryTunnels"/>
    /// and <see cref="CombatHudModel.EntryFlares"/>; (4) an emptied hotspot's marker is never
    /// marked dark/lost (S4) even though the models answer <c>Lost</c>.
    ///
    /// <b>What is pinned</b>:
    ///  * placeable views ride the binder's existing state reconciliation (T16/T19's pattern):
    ///    one view per standing placeable, at its position, through the resolver seam with
    ///    <see cref="VisualClass.Placeable"/> and an art key spelled exactly as the sim's
    ///    <see cref="PlaceableType"/> constant; no duplicates across refreshes; the view follows
    ///    the entity; an <see cref="Placeable.Exists"/> flip — sold, broken, destroyed — releases
    ///    the view on the next refresh;
    ///  * the barricade damage readout is state-driven: none at full R-23 catalog HP, an
    ///    observable indicator when damaged, its displayed fraction monotone in the sim's Hp;
    ///  * the built scene anchors one marker per entry tunnel (keyed by the same index the wave
    ///    preview and the flare name) and the markers carry observable pulse/flare/lost state the
    ///    shell pump refreshes from the models.
    ///
    /// <b>What is deliberately NOT pinned</b>: the presentation of any of it — indicator shape
    /// (bar/tint/scale), pulse and flare animation, the dark-lost look, marker shapes and vertical
    /// offsets; the feel events for placeable_broken vs placeable_destroyed (ticket 013 locked
    /// them — the distinct events stay distinct there and are not re-pinned here); flare timing
    /// (only that a flare eventually clears — by the next planning screen at the latest); and any
    /// barricade HEAL path — the sim has none (med stations heal HEROES only, R-35; nothing in
    /// <see cref="MatchSim"/> ever raises a placeable's Hp), so no healed-again case exists to pin.
    ///
    /// EditMode throughout; plain C# where the logic lives (T-10's Cecil invariant keeps every
    /// MonoBehaviour here a mirror).
    /// </summary>
    [TestFixture]
    public class T26_PlaceableViewTests
    {
        private const double Step60Hz = 1.0 / 60.0;
        private const float PositionTolerance = 1e-3f;
        private const double SimTolerance = 1e-9;

        private const string HostPeerId = "peer_host";
        private const string HostAccount = "acc_calamity";

        /// <summary>The well-known roots the shell and the scene builder compose under.</summary>
        private static readonly string[] ShellRootNames =
        {
            "RedHollow_Shell", "RedHollow_MatchViews", "RedHollow_Match",
        };

        private readonly List<GameObject> _spawned = new List<GameObject>();

        private ShellBootstrap _shell;
        private InMemoryProfileStore _profiles;

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

            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();

            foreach (var name in ShellRootNames)
            {
                for (var go = GameObject.Find(name); go != null; go = GameObject.Find(name))
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        // ==========================================================================================
        //  AC 1 — a placeable in state gets a view through the resolver seam; no duplicates;
        //         the view tracks the entity
        // ==========================================================================================

        /// <summary>
        /// R-51 / S3 "Existing placeables shown": after a placeable exists in state, one Sync
        /// creates one view at its position, resolved through the seam as
        /// <see cref="VisualClass.Placeable"/> with the art key spelled EXACTLY as the sim's
        /// <see cref="PlaceableType"/> constant ("barricade") — the same key-is-the-type rule the
        /// hero binding locked (artKey == HeroClass literal), so the art pipeline and the binder
        /// can never disagree on spelling.
        /// </summary>
        [Test]
        public void A_placeable_in_state_gets_one_view_at_its_position_through_the_resolver_seam()
        {
            var state = StateWithBarricade("pl_1", new Vec2(4.0, -3.0), out _);
            var resolver = new RecordingResolver();
            var binder = NewBinder(resolver);

            binder.Sync(state);

            Assert.That(binder.BoundPlaceableIds, Is.EquivalentTo(new[] { "pl_1" }),
                "S3: a standing placeable has a view — exactly one, keyed by the sim's own id");

            var view = binder.PlaceableViewFor("pl_1");
            Assert.That(view, Is.Not.Null, "the bound id resolves to its view");
            Assert.That(view.PlaceableId, Is.EqualTo("pl_1"), "the view knows which entity it mirrors");

            AssertStandsAt(view.WorldPosition, new Vec2(4.0, -3.0), "the placeable view");
            AssertStandsAt(view.transform.position, new Vec2(4.0, -3.0), "the placeable view transform");

            var call = resolver.Calls.SingleOrDefault(c => c.Class == VisualClass.Placeable);
            Assert.That(call, Is.Not.Null,
                "the visual came through the resolver seam as VisualClass.Placeable — the seam "
                + "ticket 013 wires real art into");
            Assert.That(call.ArtKey, Is.EqualTo(PlaceableType.Barricade),
                "the art key IS the sim's PlaceableType constant (\"barricade\") — the same "
                + "key-equals-the-type rule the hero binding uses, so art registration and the "
                + "binder can never drift on spelling");

            Assert.That(view.Visual, Is.Not.Null, "the view wears the resolved visual");
            Assert.That(view.Visual.ArtKey, Is.EqualTo(PlaceableType.Barricade),
                "and the handle records the key it was resolved with");

            Assert.That(view.transform.IsChildOf(binder.Root.transform), Is.True,
                "placeable views hang under the binder's root like every other view, so a session "
                + "tears down in one call");
        }

        /// <summary>
        /// R-51 — Sync is idempotent for placeables exactly as it is for monsters: the host calls
        /// it every step, and a binder that created a view per step would stack stand-ins on every
        /// barricade in the colony. Same view instance, one resolver call, one bound id.
        /// </summary>
        [Test]
        public void Repeated_syncs_never_duplicate_a_placeable_view()
        {
            var state = StateWithBarricade("pl_1", new Vec2(4.0, -3.0), out _);
            var resolver = new RecordingResolver();
            var binder = NewBinder(resolver);

            binder.Sync(state);
            var first = binder.PlaceableViewFor("pl_1");

            binder.Sync(state);
            binder.Sync(state);

            Assert.That(binder.BoundPlaceableIds.Count, Is.EqualTo(1),
                "sixty syncs a second must not mean sixty views");
            Assert.That(binder.PlaceableViewFor("pl_1"), Is.SameAs(first),
                "the surviving view is the SAME view, not a fresh one per refresh");
            Assert.That(resolver.Calls.Count(c => c.Class == VisualClass.Placeable), Is.EqualTo(1),
                "the art was resolved once, when the view was created — not once per refresh");
        }

        /// <summary>
        /// R-51 — the view mirrors the entity when the sim moves it (nothing shipped moves a
        /// placeable today, but the binder's contract is a mirror of state, not of the purchase
        /// event — a retuned sim that nudges placeables must not strand the views).
        /// </summary>
        [Test]
        public void A_placeable_view_follows_the_entity_position_across_refreshes()
        {
            var state = StateWithBarricade("pl_1", new Vec2(4.0, -3.0), out var placeable);
            var binder = NewBinder(new RecordingResolver());

            binder.Sync(state);

            placeable.Pos = new Vec2(-6.5, 2.25);
            binder.Sync(state);

            AssertStandsAt(binder.PlaceableViewFor("pl_1").WorldPosition, new Vec2(-6.5, 2.25),
                "the placeable view after the sim moved the entity");
        }

        // ==========================================================================================
        //  AC 2 — sold / broken / destroyed: Exists flips false → view gone on the next refresh
        // ==========================================================================================

        /// <summary>
        /// R-22 / R-23 / R-16 — <see cref="Placeable.Exists"/> is the ONE predicate the sim flips
        /// for all three removals (sold refunds it, a spent trap breaks it, damage destroys it),
        /// and it is the one the binder reads: whatever flipped it, the next refresh releases the
        /// view and destroys its GameObject. The distinct placeable_broken / placeable_destroyed
        /// feel events are ticket 013's locked territory and are deliberately NOT re-pinned here —
        /// this is the view lifecycle only.
        /// </summary>
        [Test]
        public void A_placeable_that_stops_existing_loses_its_view_on_the_next_refresh()
        {
            var state = StateWithBarricade("pl_1", new Vec2(4.0, -3.0), out var placeable);
            var binder = NewBinder(new RecordingResolver());

            binder.Sync(state);
            var view = binder.PlaceableViewFor("pl_1");
            var viewObject = view == null ? null : view.gameObject;
            Assert.That(viewObject, Is.Not.Null, "sanity: the standing placeable had a view");

            placeable.Exists = false;
            binder.Sync(state);

            Assert.That(binder.BoundPlaceableIds, Is.Empty,
                "R-22/R-23: a placeable that no longer exists has no view");
            Assert.That(binder.PlaceableViewFor("pl_1"), Is.Null,
                "the released id no longer resolves to a view");
            Assert.That(viewObject == null, Is.True,
                "the view GameObject is destroyed — a stand-in left standing is a wall the "
                + "players will keep hiding behind");
        }

        /// <summary>
        /// The destroy path end to end through the REAL sim command: a monster's swing empties the
        /// barricade's HP, <see cref="MatchSim.ApplyPlaceableDamage"/> flips Exists (R-16 "until
        /// destroyed"), and the binder's next refresh takes the wall off the screen. This is the
        /// test that fails if the binder mirrors anything other than the sim's own predicate.
        /// </summary>
        [Test]
        public void A_barricade_destroyed_by_sim_damage_disappears_on_the_next_refresh()
        {
            var map = ColonyMap.V1();
            var config = new SimConfig();
            var state = map.CreateMatchState(config);
            var fullHp = config.Placeables.StatsFor(PlaceableType.Barricade).MaxHp;
            Assert.That(fullHp, Is.GreaterThan(0.0), "sanity (R-23): the barricade row has HP");

            state.Placeables["pl_wall"] = new Placeable
            {
                Id = "pl_wall",
                Type = PlaceableType.Barricade,
                Pos = new Vec2(5.0, 0.0),
                Hp = fullHp,
                Exists = true,
            };

            var sim = new MatchSim(state, config, null, new SimClock(), null) { ColonyMap = map };
            var binder = NewBinder(new RecordingResolver());

            binder.Sync(state);
            Assert.That(binder.BoundPlaceableIds, Does.Contain("pl_wall"),
                "sanity: the standing wall has a view");

            var result = sim.ApplyPlaceableDamage(new PlaceableDamageRequest
            {
                TargetId = "pl_wall",
                AttackerId = "m_test",
                Damage = fullHp,
            });
            Assert.That(result.Destroyed, Is.True, "sanity (R-16): the swing destroyed the wall");
            Assert.That(state.Placeables["pl_wall"].Exists, Is.False,
                "sanity: the sim's own predicate flipped");

            binder.Sync(state);

            Assert.That(binder.BoundPlaceableIds, Is.Empty,
                "R-16: a destroyed barricade is rubble — the view is released on the next refresh");
        }

        // ==========================================================================================
        //  AC 3 — barricade damage readout, state-driven (wireframe S4)
        // ==========================================================================================

        /// <summary>
        /// R-51 — the view is a mirror on exactly <see cref="MonsterView"/>'s shape: RenderFrom
        /// copies the replicated Hp and position out of the world and follows them when they
        /// change; nothing is derived, clamped or cached.
        /// </summary>
        [Test]
        public void A_placeable_view_mirrors_the_sims_hp_and_position()
        {
            var state = StateWithBarricade("pl_1", new Vec2(4.0, -3.0), out var placeable);
            var fullHp = new SimConfig().Placeables.StatsFor(PlaceableType.Barricade).MaxHp;
            placeable.Hp = fullHp;

            var view = NewView<PlaceableView>("placeable");
            view.Bind("pl_1", Placeholder(VisualClass.Placeable, PlaceableType.Barricade), fullHp);
            view.RenderFrom(state);

            Assert.That(view.DisplayedHp, Is.EqualTo(fullHp).Within(SimTolerance),
                "R-51: HP is whatever the sim says it is");
            AssertStandsAt(view.WorldPosition, placeable.Pos, "the placeable view");

            placeable.Hp = fullHp * 0.4;
            placeable.Pos = new Vec2(-2.0, 7.0);
            view.RenderFrom(state);

            Assert.That(view.DisplayedHp, Is.EqualTo(fullHp * 0.4).Within(SimTolerance),
                "R-51: a replicated HP change must reach the view");
            AssertStandsAt(view.WorldPosition, placeable.Pos, "the placeable view after the move");
        }

        /// <summary>
        /// Wireframe S4 — "Barricades show HP bars WHEN DAMAGED": at full R-23 catalog HP there is
        /// no damage indicator at all. The full-HP denominator is the catalog's MaxHp, never a
        /// literal typed into the view.
        /// </summary>
        [Test]
        public void A_barricade_at_full_catalog_hp_shows_no_damage_indicator()
        {
            var config = new SimConfig();
            var fullHp = config.Placeables.StatsFor(PlaceableType.Barricade).MaxHp;
            var state = StateWithBarricade("pl_1", new Vec2(4.0, -3.0), out var placeable);
            placeable.Hp = fullHp;

            var view = NewView<PlaceableView>("placeable");
            view.Bind("pl_1", Placeholder(VisualClass.Placeable, PlaceableType.Barricade), fullHp);
            view.RenderFrom(state);

            Assert.That(view.FullHp, Is.EqualTo(fullHp).Within(SimTolerance),
                "the denominator is the R-23 catalog's MaxHp — the same number the sim damages "
                + "against, never a second copy");
            Assert.That(view.DamageIndicatorVisible, Is.False,
                "S4: an undamaged barricade shows NO damage readout");
        }

        /// <summary>
        /// Wireframe S4 — a damaged barricade (Hp below catalog full) shows an observable damage
        /// indicator, and the displayed fraction is monotone in the sim's Hp: less HP reads as
        /// less. Shape and exact values are presentation and deliberately unpinned; presence,
        /// range and monotonicity are the contract. (No healed-again case: the sim has no path
        /// that raises a placeable's Hp — med stations heal heroes only.)
        /// </summary>
        [Test]
        public void A_damaged_barricade_shows_an_indicator_whose_fraction_falls_with_hp()
        {
            var config = new SimConfig();
            var fullHp = config.Placeables.StatsFor(PlaceableType.Barricade).MaxHp;
            var state = StateWithBarricade("pl_1", new Vec2(4.0, -3.0), out var placeable);

            var view = NewView<PlaceableView>("placeable");
            view.Bind("pl_1", Placeholder(VisualClass.Placeable, PlaceableType.Barricade), fullHp);

            placeable.Hp = fullHp * 0.6;
            view.RenderFrom(state);

            Assert.That(view.DamageIndicatorVisible, Is.True,
                "S4: a damaged barricade shows a damage readout");
            Assert.That(view.HpFraction, Is.InRange(0.0, 1.0),
                "the displayed fraction is a fraction");
            var atSixty = view.HpFraction;

            placeable.Hp = fullHp * 0.2;
            view.RenderFrom(state);

            Assert.That(view.DamageIndicatorVisible, Is.True,
                "still damaged, still shown");
            Assert.That(view.HpFraction, Is.LessThan(atSixty),
                "the displayed fraction is MONOTONE in the sim's Hp — deeper damage reads as less "
                + "(exact mapping is presentation; the ordering is contract)");
            Assert.That(view.HpFraction, Is.InRange(0.0, 1.0),
                "and it is still a fraction");
        }

        /// <summary>
        /// The binder feeds the readout: given the R-23 catalog, a synced barricade view carries
        /// the catalog's MaxHp as its denominator and its indicator follows the replicated Hp
        /// across refreshes — state-driven end to end, no event required.
        /// </summary>
        [Test]
        public void The_binder_wires_the_catalog_denominator_and_the_indicator_follows_state()
        {
            var config = new SimConfig();
            var fullHp = config.Placeables.StatsFor(PlaceableType.Barricade).MaxHp;
            var state = StateWithBarricade("pl_1", new Vec2(4.0, -3.0), out var placeable);
            placeable.Hp = fullHp;

            var binder = NewBinder(new RecordingResolver());
            binder.PlaceableCatalog = config.Placeables;

            binder.Sync(state);
            var view = binder.PlaceableViewFor("pl_1");

            Assert.That(view.FullHp, Is.EqualTo(fullHp).Within(SimTolerance),
                "the binder hands the view the R-23 catalog MaxHp for its row");
            Assert.That(view.DamageIndicatorVisible, Is.False,
                "S4: no readout at full HP");

            placeable.Hp = fullHp * 0.5;
            binder.Sync(state);

            Assert.That(view.DamageIndicatorVisible, Is.True,
                "S4: the refresh alone surfaces the damage — the readout is state-driven, "
                + "not event-driven");
        }

        // ==========================================================================================
        //  AC 4 — the built scene anchors entry-tunnel markers; markers carry observable state
        // ==========================================================================================

        /// <summary>
        /// R-14 / wireframe S3-S4 — the scene has one marker per <see cref="ColonyMap.EntryTunnels"/>
        /// entry, keyed by the tunnel's INDEX in that list (the spelling the wave preview's
        /// ActiveEntryTunnels and the HUD's EntryFlares both use, so a marker and the tunnel it
        /// stands for cannot be matched up wrongly downstream), standing at the map's position and
        /// wearing a resolver-supplied visual. Derived from the map, never hardcoded.
        /// </summary>
        [Test]
        public void The_built_scene_has_a_marker_on_every_entry_tunnel()
        {
            var map = ColonyMap.V1();
            var resolver = new RecordingResolver();
            var scene = Track(MatchSceneBuilder.Build(map, resolver));

            Assert.That(scene.EntryTunnelMarkers.Keys,
                Is.EquivalentTo(Enumerable.Range(0, map.EntryTunnels.Count)),
                "R-14: one marker per entry tunnel — keyed by the index the preview and the "
                + "flare name, no more, no fewer");

            for (var i = 0; i < map.EntryTunnels.Count; i++)
            {
                var marker = scene.EntryTunnelMarkers[i];
                Assert.That(marker, Is.Not.Null, "marker for tunnel " + i);
                AssertStandsAt(marker.transform.position, map.EntryTunnels[i],
                    "marker for tunnel " + i);
                Assert.That(marker.GetComponentInChildren<Renderer>(), Is.Not.Null,
                    "the marker renders something — an invisible anchor cannot pulse red");
                Assert.That(marker.transform.IsChildOf(scene.Root.transform), Is.True,
                    "and it tears down with the scene");

                var view = marker.GetComponent<EntryTunnelMarkerView>();
                Assert.That(view, Is.Not.Null,
                    "the marker carries its observable state component");
                Assert.That(view.TunnelIndex, Is.EqualTo(i),
                    "which knows its own index — the id the models speak in");
                Assert.That(view.Pulsing, Is.False,
                    "a freshly built marker pulses nothing — the models drive it, not the builder");
                Assert.That(view.Flaring, Is.False,
                    "and flares nothing");
            }
        }

        /// <summary>
        /// Wireframe S4 — every hotspot marker carries its observable lost-state component, named
        /// by the sim's own id and not lost at build time (the colony starts with everyone alive).
        /// </summary>
        [Test]
        public void Hotspot_markers_carry_an_identifying_lost_state_component()
        {
            var map = ColonyMap.V1();
            var scene = Track(MatchSceneBuilder.Build(map, new RecordingResolver()));

            foreach (var spec in map.Hotspots)
            {
                var view = scene.HotspotMarkers[spec.Id].GetComponent<HotspotMarkerView>();
                Assert.That(view, Is.Not.Null,
                    "marker " + spec.Id + " carries its observable state component");
                Assert.That(view.HotspotId, Is.EqualTo(spec.Id),
                    "named by the sim's own id");
                Assert.That(view.Lost, Is.False,
                    "nobody is lost at build time");
            }
        }

        // ==========================================================================================
        //  AC 5 — pump-driven marker states: planning pulse, spawn flare, lost-hotspot dark
        // ==========================================================================================

        /// <summary>
        /// Wireframe S3 — "ACTIVE entry points pulse red": during planning, exactly the tunnels
        /// the planning model's <see cref="PlanningScreenModel.PulsingEntryTunnels"/> names (R-05's
        /// partial preview) have their markers in the pulsing state, driven through the pump. The
        /// pulse animation is presentation; the state presence is the pin.
        /// </summary>
        [Test]
        public void Planning_pulses_exactly_the_previewed_entry_tunnel_markers()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            var scene = AttachColonyScene(shell);
            ReachPlanning(shell, match);

            var pulsing = shell.Planning.PulsingEntryTunnels;
            Assert.That(pulsing, Is.Not.Empty, "sanity (R-05): breaches will open next wave");

            foreach (var pair in scene.EntryTunnelMarkers)
            {
                var view = pair.Value.GetComponent<EntryTunnelMarkerView>();
                Assert.That(view.Pulsing, Is.EqualTo(pulsing.Contains(pair.Key)),
                    "S3: tunnel " + pair.Key + " pulses exactly when the preview names it — "
                    + "all of the named ones, none of the others");
                Assert.That(view.Flaring, Is.False,
                    "no flare during planning — the flare is the SPAWN'S state, not the preview's");
            }
        }

        /// <summary>
        /// Wireframe S4 — "monster spawn → entry point flare": when the wave spawns, the markers
        /// of the tunnels the planning preview named (the HUD's
        /// <see cref="CombatHudModel.EntryFlares"/> carries them across the phase change —
        /// DEC-018's event names no tunnels) are in the flaring state; and the flare is not
        /// forever — by the time the next planning screen is up it has cleared. Flare timing
        /// itself is presentation and deliberately unpinned.
        /// </summary>
        [Test]
        public void A_wave_spawn_flares_the_previewed_markers_and_the_flare_eventually_clears()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            var scene = AttachColonyScene(shell);
            ReachPlanning(shell, match);

            var previewed = shell.Planning.PulsingEntryTunnels.ToList();
            Assert.That(previewed, Is.Not.Empty, "sanity (R-05): breaches will open next wave");

            shell.Controls.PlanningReadyButton.onClick.Invoke();
            shell.Pump(0.0);
            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Combat),
                "sanity (R-03): the solo READY UP opened combat early");

            // A few frames for the spawn to land and the pump to route its event.
            for (var i = 0; i < 3 && !match.State.Monsters.Values.Any(m => m.Alive); i++)
            {
                shell.Pump(Step60Hz);
            }

            Assert.That(match.State.Monsters.Values.Any(m => m.Alive), Is.True,
                "sanity (R-19): the wave spawned");

            foreach (var pair in scene.EntryTunnelMarkers)
            {
                var view = pair.Value.GetComponent<EntryTunnelMarkerView>();
                Assert.That(view.Flaring, Is.EqualTo(previewed.Contains(pair.Key)),
                    "S4: tunnel " + pair.Key + " flares exactly when the wave was previewed to "
                    + "use it");
                Assert.That(view.Pulsing, Is.False,
                    "the planning pulse does not follow the match into combat");
            }

            // The flare is a moment, not a mode: clear the wave and ride the interstitial back to
            // planning — however the implementation times the decay, it has cleared by here.
            ReachPlanning(shell, match);

            foreach (var pair in scene.EntryTunnelMarkers)
            {
                Assert.That(pair.Value.GetComponent<EntryTunnelMarkerView>().Flaring, Is.False,
                    "the spawn flare on tunnel " + pair.Key + " eventually clears — it must not "
                    + "still be burning on the NEXT planning screen");
            }
        }

        /// <summary>
        /// Wireframe S4 — "hotspot emptied → building marked dark/lost": an emptied shelter's
        /// marker enters the lost state on the next pump, driven by the sim's own answer
        /// (Civilians == 0), and ONLY it — the standing shelters are not painted lost. The dark
        /// itself is presentation.
        /// </summary>
        [Test]
        public void An_emptied_hotspot_darkens_its_marker_and_only_its_marker()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            var scene = AttachColonyScene(shell);
            shell.Pump(0.0);

            var target = match.State.Hotspots.Values.First();
            var result = match.Sim.ApplyHotspotAttack(new HotspotAttackRequest
            {
                AttackerId = "m_test",
                AttackerType = MonsterType.Shambler,
                Damage = 9999.0,
                TargetId = target.Id,
            });
            Assert.That(result.CiviliansRemaining, Is.EqualTo(0),
                "sanity (R-11): the shelter is emptied");
            Assert.That(match.State.IsOver, Is.False,
                "sanity (R-02): one lost shelter of three does not end the match");

            shell.Pump(0.0);

            foreach (var pair in scene.HotspotMarkers)
            {
                var view = pair.Value.GetComponent<HotspotMarkerView>();
                Assert.That(view.Lost, Is.EqualTo(pair.Key == target.Id),
                    "S4: marker " + pair.Key + " is lost exactly when its shelter is emptied — "
                    + "the emptied one dark, the standing ones untouched");
            }
        }

        // ==========================================================================================
        //  AC 6 — end to end through the shell: purchase → view; sell → gone
        // ==========================================================================================

        /// <summary>
        /// S3 end to end on the REAL composition root: a purchase through the locked T23 shop
        /// seam (<see cref="PlanningScreenModel"/>) followed by a pump puts a view of the new
        /// placeable on screen at the placed position; the R-22 sell followed by a pump takes it
        /// off. This is the test that fails if placeable views ride anything other than the
        /// binder's per-pump refresh.
        /// </summary>
        [Test]
        public void A_purchase_through_the_shell_yields_a_view_and_a_sell_removes_it()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            var item = shell.Planning.ShopItems.OrderBy(i => i.Cost).First();
            Assert.That(item.Affordable, Is.True, "sanity: the cheapest item is affordable");

            var before = match.State.Placeables.Keys.ToList();
            var placePos = new Vec2(5.0, 0.0);

            shell.Planning.BeginPlacement(item.Type);
            shell.Planning.MoveGhost(placePos, true);
            var purchase = shell.Planning.ConfirmPlacement();
            Assert.That(purchase.Accepted, Is.True,
                "sanity (R-21/R-24): open ground away from buildings and mouths accepts");

            var placedId = match.State.Placeables.Keys.Single(k => !before.Contains(k));

            shell.Pump(0.0);

            Assert.That(shell.Views.BoundPlaceableIds, Does.Contain(placedId),
                "S3: the purchased placeable is on screen after the very next pump");
            AssertStandsAt(shell.Views.PlaceableViewFor(placedId).WorldPosition, placePos,
                "the purchased placeable's view");

            var sale = shell.Planning.Sell(placedId);
            Assert.That(sale.Accepted, Is.True, "sanity (R-22): a standing placeable sells");

            shell.Pump(0.0);

            Assert.That(shell.Views.BoundPlaceableIds, Does.Not.Contain(placedId),
                "R-22: the sold placeable is off the screen on the next pump — the refund is "
                + "not standing in the lane");
        }

        // ==========================================================================================
        //  scenario builders and assertions
        // ==========================================================================================

        /// <summary>A live combat state holding one standing barricade and nothing else.</summary>
        private static MatchState StateWithBarricade(string id, Vec2 pos, out Placeable placeable)
        {
            var state = new MatchState
            {
                Phase = MatchPhase.Combat,
                Status = MatchStatus.InProgress,
            };

            placeable = new Placeable
            {
                Id = id,
                Type = PlaceableType.Barricade,
                Pos = pos,
                PurchaseCost = 100,
                Hp = 300.0,
                Exists = true,
            };

            state.Placeables[id] = placeable;
            return state;
        }

        /// <summary>
        /// A fresh binder. Its root ("RedHollow_MatchViews", created lazily on first Sync) is
        /// destroyed by the name-based sweep in <see cref="DestroyEverythingThisTestBuilt"/>.
        /// </summary>
        private static MatchViewBinder NewBinder(IVisualResolver resolver)
        {
            return new MatchViewBinder(resolver);
        }

        /// <summary>A fresh shell on S1: loopback, in-memory profiles (T21/T23/T24's helper).</summary>
        private ShellBootstrap NewShell()
        {
            _profiles = new InMemoryProfileStore();

            _shell = new ShellBootstrap(new ShellBootstrapOptions
            {
                Transport = new LoopbackNetTransport(),
                Profiles = _profiles,
                SimConfig = new SimConfig(),
                LocalPeerId = HostPeerId,
                LocalAccountId = HostAccount,
            });

            return _shell;
        }

        /// <summary>A shell with the host seated — the S2 starting point.</summary>
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

        /// <summary>
        /// Build the colony scene over the shipped V1 map through the shell's own asset seam and
        /// hand it to the shell, so the pump refreshes its marker states.
        /// </summary>
        private MatchScene AttachColonyScene(ShellBootstrap shell)
        {
            var scene = Track(MatchSceneBuilder.Build(ColonyMap.V1(), shell.Visuals));
            shell.AttachScene(scene);
            return scene;
        }

        /// <summary>Clear the live wave and ride S5's hold into S3 (T21/T23/T24's recipe).</summary>
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

        /// <summary>A placeholder visual for a view to wear, registered for teardown.</summary>
        private VisualHandle Placeholder(VisualClass visualClass, string artKey)
        {
            var handle = new PlaceholderVisualResolver().Resolve(visualClass, artKey);
            if (handle != null)
            {
                Track(handle.Instance);
            }

            return handle;
        }

        private T NewView<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        private GameObject Track(GameObject go)
        {
            if (go != null)
            {
                _spawned.Add(go);
            }

            return go;
        }

        private MatchScene Track(MatchScene scene)
        {
            if (scene != null)
            {
                Track(scene.Root);
            }

            return scene;
        }

        /// <summary>
        /// Horizontal placement only — how high a marker or a stand-in floats is presentation
        /// (T16's rule), so every position assertion here is x/z against the sim point.
        /// </summary>
        private static void AssertStandsAt(Vector3 actualWorld, Vec2 expectedGround, string what)
        {
            var expected = SimSpace.ToWorld(expectedGround);

            Assert.That(actualWorld.x, Is.EqualTo(expected.x).Within(PositionTolerance),
                what + ": x must match the sim position " + expectedGround);
            Assert.That(actualWorld.z, Is.EqualTo(expected.z).Within(PositionTolerance),
                what + ": z must match the sim position " + expectedGround);
        }

        // ==========================================================================================
        //  test doubles
        // ==========================================================================================

        /// <summary>One recorded pass through the asset seam.</summary>
        private sealed class ResolveCall
        {
            public VisualClass Class;
            public string ArtKey;
        }

        /// <summary>
        /// The asset seam with a memory: answers exactly as the shipped placeholder resolver does
        /// (total, never null — R-30's delivery constraint) while recording what was asked, so a
        /// test can pin WHICH class and key a visual was resolved with.
        /// </summary>
        private sealed class RecordingResolver : IVisualResolver
        {
            private readonly PlaceholderVisualResolver _inner = new PlaceholderVisualResolver();

            public readonly List<ResolveCall> Calls = new List<ResolveCall>();

            public VisualHandle Resolve(VisualClass visualClass, string artKey)
            {
                Calls.Add(new ResolveCall { Class = visualClass, ArtKey = artKey });
                return _inner.Resolve(visualClass, artKey);
            }
        }
    }
}
