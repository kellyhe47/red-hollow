using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RedHollow.Game.Host;
using RedHollow.Game.Input;
using RedHollow.Game.View;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 019 (T-19): the playable bootstrap. Grades no golden fixture — every rule below is
    /// already green inside <see cref="MatchSim"/>; what has never existed is anything that *calls*
    /// them in sequence. Three whole sim capabilities were reachable only from a unit test:
    /// <see cref="MatchSim.TickMonsterMovement"/> and <see cref="MatchSim.MoveHero"/> take a delta,
    /// so they fell outside T-10's parameterless-<c>Tick*</c> net and no host drove them;
    /// <see cref="MatchSim.SpawnWave"/> was called by nothing at all, so a running match held no
    /// monsters; and no view was ever bound to a live entity.
    ///
    /// Seven things are pinned here, one per acceptance criterion:
    ///
    ///  1. <b>The loop advances monster movement every step, with the delta it was given.</b>
    ///     Asserted on the world (a monster is closer to its target, by exactly speed x delta)
    ///     rather than on a call being made — a loop that passed a hardcoded 1/60 while the caller
    ///     stepped 0.5s would satisfy "it called it" and desynchronise the match.
    ///
    ///  2. <b>A resolved R-30 intent reaches the hero through <see cref="MatchSim.MoveHero"/>.</b>
    ///     The discriminating half is that the *direction survives the trip* and that the command
    ///     is routed by hero id — a bootstrap that moved every hero, or that let the cursor steer
    ///     (DEC-017), is what this catches.
    ///
    ///  3. <b>Wave progression.</b> Starting a match puts the current wave in the colony, a cleared
    ///     wave is eventually followed by the next one, and the final wave completing spawns
    ///     nothing further. What a wave *contains* is ticket 017's and what a clear does to the FSM
    ///     is ticket 004's; neither is re-pinned here.
    ///
    ///  4. <b>The view set follows the world.</b> Lifecycle only — a view exists per living entity
    ///     and is released when that entity dies. Ticket 016 owns what a view renders.
    ///
    ///  5. <b>A driven session reaches defeat.</b> The one test in this run that exercises
    ///     spawn -> target -> move -> gate -> damage -> defeat as a single chain, on the real
    ///     <see cref="ColonyMap.V1"/> with no defenders at all.
    ///
    ///  6. <b>Placeable combat is driven.</b> Turrets fire at 1 Hz, traps fire on footprint entry
    ///     (not occupancy), and a placeable kill is reaped through
    ///     <see cref="MatchSim.RecordMonsterKill"/> so the wave roster actually shrinks (R-23 / R-02).
    ///
    ///  7. <b>No game rule enters a MonoBehaviour.</b> T-10's Cecil scan is the enforcement and it
    ///     is unchanged; the guard here states the shape that keeps it green.
    ///
    /// <b>What is deliberately NOT asserted</b>, because the PRD is silent and a guessed number
    /// would ship as spec: exactly when in a step each sim op runs (the PRD orders none), exactly
    /// how long after a clear the next wave spawns (R-04's interstitial and R-03's planning timer
    /// both sit in between, so progression is asserted as "eventually, within a bound"), whether
    /// views are pooled or destroyed on release, interpolation, and hero move speed.
    ///
    /// EditMode throughout: the loop is driven by an explicit <c>Step</c> and views by an explicit
    /// <c>Sync</c>, so nothing here needs a frame to elapse.
    /// </summary>
    [TestFixture]
    public class T19_BootstrapTests
    {
        /// <summary>One host step at Unity's default fixed timestep, matching T10 and T16.</summary>
        private const double Step60Hz = 1.0 / 60.0;

        private const double SimTolerance = 1e-9;

        /// <summary>Positions cross a double-to-float boundary in the view layer.</summary>
        private const float PositionTolerance = 1e-3f;

        /// <summary>Everything a test put in the editor's scene, torn down after it.</summary>
        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>Binders whose views must be destroyed even if they were not parented to a root.</summary>
        private readonly List<MatchViewBinder> _binders = new List<MatchViewBinder>();

        [TearDown]
        public void DestroyEverythingThisTestBuilt()
        {
            foreach (var binder in _binders)
            {
                CollectViewsOf(binder);
            }

            _binders.Clear();

            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
        }

        // ==========================================================================================
        //  AC1 — the host loop advances monster movement every step
        // ==========================================================================================

        /// <summary>
        /// R-17 / R-18 / R-51. <see cref="HostLoop.Step"/> must drive
        /// <see cref="MatchSim.TickMonsterMovement"/>, and drive it with <i>this</i> step's delta.
        ///
        /// Pinned on the world rather than on a recorded call, for two reasons. A monster that is
        /// closer to its shelter than it was is the property the match actually depends on — R-02's
        /// defeat condition is unreachable without it. And the distance covered is the only thing
        /// that separates "the loop calls movement" from "the loop calls movement correctly": a
        /// loop passing a constant tick rate instead of the delta it was handed looks identical to
        /// a call recorder and runs the sim at the wrong speed forever.
        ///
        /// <c>speed x delta</c> is the sim's own rule (MatchSim.Movement.cs) and is read off the
        /// monster rather than typed here, so retuning R-17's Speed column retunes the assertion.
        /// Two steps are driven because the criterion is "every step", not "the first step".
        /// </summary>
        [Test]
        public void Every_host_step_advances_monster_movement_by_the_delta_it_was_given()
        {
            var state = NewCombatState();
            state.Hotspots["hs_saloon"] = new Hotspot { Id = "hs_saloon", Pos = new Vec2(0.0, 0.0), Civilians = 8 };
            state.Monsters["m1"] = new Monster
            {
                Id = "m1",
                Type = MonsterType.Shambler,
                Pos = new Vec2(20.0, 0.0),
                Hp = 60.0,
                Alive = true,
                BaseSpeed = 2.0,
                CurrentSpeed = 2.0,
                TargetId = "hs_saloon",
            };
            state.Wave.LivingMonsterIds.Add("m1");

            var clock = new SimClock();
            var sim = new MatchSim(state, new SimConfig(), null, clock, null);
            var loop = new HostLoop(new MatchSimHost(sim, clock));

            var monster = state.Monsters["m1"];
            var shelter = state.Hotspots["hs_saloon"].Pos;

            const double Delta = 0.5;
            var expectedStep = monster.CurrentSpeed * Delta;

            var start = monster.Pos;
            loop.Step(Delta);
            var afterOne = monster.Pos;

            Assert.That(afterOne.DistanceTo(shelter), Is.LessThan(start.DistanceTo(shelter)),
                "R-17/R-18: one host step must leave a targeted monster closer to its shelter; "
                + "HostLoop.Step does not drive TickMonsterMovement, so the wave never leaves its breach");

            Assert.That(start.DistanceTo(afterOne), Is.EqualTo(expectedStep).Within(SimTolerance),
                "the loop must hand TickMonsterMovement the delta it was stepped with (" + Delta
                + "s at speed " + monster.CurrentSpeed + " is " + expectedStep + " units); a "
                + "hardcoded tick rate runs the match at the wrong speed");

            loop.Step(Delta);

            Assert.That(afterOne.DistanceTo(monster.Pos), Is.EqualTo(expectedStep).Within(SimTolerance),
                "movement is driven EVERY step, not only the first");
        }

        // ==========================================================================================
        //  AC2 — a resolved move intent reaches the hero through MoveHero
        // ==========================================================================================

        /// <summary>
        /// R-30 / R-51. The seam where ticket 016's input map finally reaches the sim: a resolved
        /// <see cref="HeroIntent.MoveDirection"/> must arrive at <see cref="MatchSim.MoveHero"/>
        /// for the hero the intent names, carrying this step's delta.
        ///
        /// Three things are discriminated, and each is a bug a "it calls MoveHero" test would miss:
        ///
        ///  * <b>the direction survives the trip</b> — W is forward, so the hero's y grows and its
        ///    x does not move at all. The cursor sits at (-9, -9), a direction no assertion here
        ///    expects, so a bootstrap that steered by the aim point (DEC-017's click-to-move) fails
        ///    rather than coincidentally agreeing;
        ///  * <b>the command is routed by id</b> — a second hero on the field, named by no intent,
        ///    must not have moved;
        ///  * <b>a zero intent issues no move</b> — the keys are released and the hero holds the
        ///    ground it had. Asserted after a real move so the test cannot pass by never moving
        ///    anything at all.
        ///
        /// The intent comes out of the real <see cref="DefaultHeroInputMap"/> rather than being
        /// typed in: R-30's mapping is ticket 016's contract, and this is the wire from it.
        /// Distance is read off the sim's own <see cref="HeroMovementConfig"/> — the PRD names no
        /// hero move speed, so nothing here states one.
        /// </summary>
        [Test]
        public void A_resolved_move_intent_reaches_the_hero_and_the_direction_survives_the_trip()
        {
            var state = NewCombatState();
            state.Heroes["h_local"] = NewHero("h_local", new Vec2(0.0, 0.0));
            state.Heroes["h_other"] = NewHero("h_other", new Vec2(5.0, 5.0));

            var clock = new SimClock();
            var sim = new MatchSim(state, new SimConfig(), null, clock, null);
            var intents = new ScriptedHeroIntents();
            var loop = new HostLoop(new MatchSimHost(sim, clock), null, intents);

            var map = new DefaultHeroInputMap();
            var cursorBehindTheHero = new Vector2(-9f, -9f);

            intents.Set("h_local", map.Resolve(Snapshot(cursorBehindTheHero, PlayerKey.W)));

            const double Delta = 0.5;
            var expectedStep = sim.HeroMovement.DefaultMoveSpeed * Delta;

            loop.Step(Delta);

            var local = state.Heroes["h_local"];

            Assert.That(local.Pos.Y, Is.EqualTo(expectedStep).Within(SimTolerance),
                "R-30: a resolved W intent must reach MatchSim.MoveHero and walk the hero forward "
                + "by speed x delta; HostLoop.Step never calls MoveHero, so the hero is inert");
            Assert.That(local.Pos.X, Is.EqualTo(0.0).Within(SimTolerance),
                "R-30 / DEC-017: the direction must survive the trip — a hero moving toward the "
                + "cursor at " + cursorBehindTheHero + " is click-to-move wearing a WASD hat");

            Assert.That(state.Heroes["h_other"].Pos, Is.EqualTo(new Vec2(5.0, 5.0)),
                "R-51: the move must be routed to the hero the intent named and to no other");

            // Keys released. The hero holds the ground it just covered.
            var held = local.Pos;
            intents.Set("h_local", map.Resolve(Snapshot(cursorBehindTheHero)));

            loop.Step(Delta);
            loop.Step(Delta);

            Assert.That(local.Pos, Is.EqualTo(held),
                "R-30: a zero move intent issues no move — not a step toward the cursor, and not a "
                + "repeat of the last direction");
        }

        // ==========================================================================================
        //  AC3 — starting a match spawns wave 1, and each cleared wave spawns the next
        // ==========================================================================================

        /// <summary>
        /// R-19. A fresh match is on wave 1 (<see cref="WaveState.Number"/> defaults to 1), so
        /// starting it must put wave 1's monsters in the colony. Until this ticket nothing anywhere
        /// called <see cref="MatchSim.SpawnWave"/> and a running match contained no monsters at all.
        ///
        /// What the wave is *made of* is ticket 017's contract and is not re-pinned: this asserts
        /// only that monsters exist, that they are on the living roster
        /// <see cref="MatchSim.RecordMonsterKill"/> counts down (R-02), and that the counter still
        /// reads 1 — a bootstrap that opened by advancing the wave would silently skip wave 1.
        /// </summary>
        [Test]
        public void Starting_a_match_spawns_wave_one()
        {
            var match = NewMatch();
            var session = new MatchSession(match.Host);

            session.Start();

            Assert.That(match.State.Wave.Number, Is.EqualTo(1),
                "R-19: a fresh match opens on wave 1; starting it must not advance the counter");
            Assert.That(match.State.Wave.LivingMonsterIds, Is.Not.Empty,
                "R-19: starting a match must put wave 1's monsters in the colony — nothing has "
                + "ever called MatchSim.SpawnWave");
            Assert.That(match.State.Monsters.Count, Is.EqualTo(match.State.Wave.LivingMonsterIds.Count),
                "R-02: every spawned monster must be on the living roster, or the wave can never "
                + "be cleared");
        }

        /// <summary>
        /// R-02 / R-03 / R-19. The progression itself: clear wave 1 and wave 2's monsters must
        /// eventually appear.
        ///
        /// "Eventually" is the honest bound. R-02 returns the phase to planning on the clear, R-04
        /// puts a ~3s interstitial on top, R-03 runs a 60s planning phase, and
        /// <see cref="MatchSim.BeginPlanningPhase"/> is what advances the wave counter (G-016) —
        /// the PRD pins none of the timing in between, so this drives the session for several
        /// planning phases' worth of sim time and asserts the destination rather than the schedule.
        ///
        /// The kills go through <see cref="MatchSim.RecordMonsterKill"/> directly because ticket
        /// 019 wires no hero weapons: what is under test is what the session does *after* the wave
        /// is cleared. The new roster is asserted disjoint from the old one so a bootstrap that
        /// re-spawned wave 1, or that never cleared the corpses off the roster, cannot pass.
        /// </summary>
        [Test]
        public void Clearing_a_wave_eventually_spawns_the_next_one()
        {
            var match = NewMatch();
            var session = new MatchSession(match.Host);

            session.Start();

            var waveOne = match.State.Wave.LivingMonsterIds.ToList();
            Assert.That(waveOne, Is.Not.Empty, "R-19: wave 1 must be in the colony before it can be cleared");

            KillAll(match.Sim, match.State, waveOne);

            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Planning),
                "sanity (ticket 004 / G-010): clearing a non-final wave returns the phase to planning");

            var arrived = DriveUntil(
                session,
                match.Clock,
                () => match.State.Wave.Number == 2 && match.State.Wave.LivingMonsterIds.Count > 0,
                maxSeconds: 4.0 * match.Config.PlanningDurationSeconds);

            Assert.That(arrived, Is.True,
                "R-03/R-19: a cleared wave must be followed by the next one. After "
                + Fmt(match.Clock.ElapsedSeconds) + "s of driven session the match is at wave "
                + match.State.Wave.Number + " (" + match.State.Phase + ") with "
                + match.State.Wave.LivingMonsterIds.Count + " living monster(s). BeginPlanningPhase "
                + "is what advances the counter (G-016), and SpawnWave is what fills it");

            Assert.That(match.State.Wave.LivingMonsterIds.Intersect(waveOne), Is.Empty,
                "wave 2 must be new monsters — not wave 1's ids back on the roster");
        }

        /// <summary>
        /// R-01 / R-19. The other end of the progression: the campaign stops. The match is put on
        /// its final wave (<see cref="WaveState.TotalWaves"/> is the authority, DEC-RUN-5), the wave
        /// is opened by the session and then cleared, which wins the map (R-01) — and driving the
        /// session on from there must spawn nothing.
        ///
        /// Worth its own test because both of the sim's guards are refusals rather than throws
        /// (<see cref="MatchSim.SpawnWave"/> returns an empty wave for a finished match) while
        /// <see cref="MatchSim.BeginPlanningPhase"/> throws for one — so a bootstrap that keeps
        /// driving progression after a victory either manufactures an eleventh wave or takes the
        /// whole session down with an exception.
        /// </summary>
        [Test]
        public void Completing_the_final_wave_spawns_no_eleventh()
        {
            var match = NewMatch();
            match.State.Wave.Number = 10;
            match.State.Wave.TotalWaves = 10;

            var session = new MatchSession(match.Host);
            session.Start();

            var finale = match.State.Wave.LivingMonsterIds.ToList();
            Assert.That(finale, Is.Not.Empty, "R-19: the final wave must open with monsters in it");

            KillAll(match.Sim, match.State, finale);

            Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Victory),
                "sanity (ticket 004 / G-011): clearing the final wave wins the map");

            var monstersAtVictory = match.State.Monsters.Count;

            for (var i = 0; i < 2 * 60 * 60; i++)
            {
                session.Step(Step60Hz);
            }

            Assert.That(match.State.Wave.Number, Is.EqualTo(10),
                "R-01: there is no eleventh wave; a won match must not advance the counter");
            Assert.That(match.State.Monsters.Count, Is.EqualTo(monstersAtVictory),
                "R-01: a won match must spawn nothing further");
            Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Victory),
                "R-01: the match stays won");
        }

        // ==========================================================================================
        //  AC4 — views appear for spawned entities and are released when they die
        // ==========================================================================================

        /// <summary>
        /// R-51. The view set must follow the world. Ticket 016 built
        /// <see cref="MonsterView.RenderFrom"/> and pinned what it shows; nothing has ever created
        /// one for a live entity, so a spawned wave was invisible and a killed monster left its
        /// stand-in in the colony forever.
        ///
        /// <b>Lifecycle only.</b> Whether a released view is destroyed or returned to a pool is not
        /// in the PRD and is not asserted — what is asserted is that the binding set is exactly the
        /// set of living entities, before and after a death, and that a bound view keeps reading
        /// the sim rather than its own last frame (the read direction T-16 owns, checked here only
        /// far enough to prove the binder did not hand back a frozen view).
        ///
        /// Driven off a real <see cref="MatchSim.SpawnWave"/> and a real
        /// <see cref="MatchSim.RecordMonsterKill"/>, because "the ids the sim actually has" is the
        /// whole content of the criterion.
        /// </summary>
        [Test]
        public void A_view_appears_for_every_living_entity_and_is_released_when_it_dies()
        {
            var match = NewMatch();
            match.State.Heroes["h1"] = NewHero("h1", match.Map.TeamSpawn);

            var spawned = match.Sim.SpawnWave(1);
            Assert.That(spawned.MonsterIds, Is.Not.Empty, "sanity (ticket 017): wave 1 spawns monsters");

            var binder = TrackBinder(new MatchViewBinder(new PlaceholderVisualResolver()));

            binder.Sync(match.State);

            Assert.That(binder.BoundMonsterIds, Is.EquivalentTo(spawned.MonsterIds),
                "R-51: after a spawn there must be exactly one view per living monster");
            Assert.That(binder.BoundHeroIds, Does.Contain("h1"),
                "R-51: the hero on the field gets a view too");
            Assert.That(binder.MonsterViewFor(spawned.MonsterIds[0]), Is.Not.Null,
                "a bound id must resolve to the view bound to it");

            // The binder is called every step, so it must be idempotent: a second Sync over an
            // unchanged world must not stack a second view on every monster.
            binder.Sync(match.State);
            Assert.That(binder.BoundMonsterIds, Is.EquivalentTo(spawned.MonsterIds),
                "R-51: Sync runs every step; it must reconcile, not accumulate");

            // The world moves; a bound view must still be reading it.
            var walker = match.State.Monsters[spawned.MonsterIds[0]];
            walker.Pos = new Vec2(-4.0, 7.5);
            binder.Sync(match.State);

            var view = binder.MonsterViewFor(walker.Id);
            Assert.That(view, Is.Not.Null, "the living monster keeps its view");
            AssertStandsAt(view.WorldPosition, walker.Pos, "the bound monster view");

            // A death releases the view (R-02 / R-51).
            var casualty = spawned.MonsterIds[1];
            match.Sim.RecordMonsterKill(new MonsterKillRequest
            {
                MonsterId = casualty,
                MonsterType = match.State.Monsters[casualty].Type,
                Bounty = 0,
            });

            binder.Sync(match.State);

            Assert.That(binder.BoundMonsterIds, Does.Not.Contain(casualty),
                "R-51: a dead monster's view must be released — a stand-in left standing in the "
                + "colony is a monster the players will keep shooting at");
            Assert.That(binder.MonsterViewFor(casualty), Is.Null,
                "a released id must no longer resolve to a view");
            Assert.That(binder.BoundMonsterIds, Does.Contain(walker.Id),
                "R-51: one death must not release the rest of the wave");
        }

        // ==========================================================================================
        //  AC5 — a driven session reaches defeat when monsters are left to reach the shelters
        // ==========================================================================================

        /// <summary>
        /// R-02, end to end, and the strongest assertion this ticket can make: the real
        /// <see cref="ColonyMap.V1"/>, the real <see cref="MatchSim"/>, the real session loop, no
        /// heroes, no barricades, nobody firing a shot — and the colony falls.
        ///
        /// This is the first test in the run that exercises <b>spawn -> target -> move -> gate ->
        /// damage -> defeat</b> as one chain. Every stage is load-bearing and each one is broken
        /// today: wave 1 has to enter the colony (R-19), monsters spawn with no target so
        /// <see cref="MatchSim.SelectTarget"/> has to be driven and re-driven as R-12 empties each
        /// shelter, <see cref="MatchSim.TickMonsterMovement"/> has to walk them there,
        /// <see cref="MatchSim.TryMonsterAttack"/> has to gate the swing on R-18's cadence, and
        /// <see cref="MatchSim.ApplyHotspotAttack"/> has to spend the colony's 20 civilians down to
        /// zero (R-10 / R-11).
        ///
        /// <b>Bounded, not open-ended.</b> The whole march is arithmetic off the shipped data —
        /// six shamblers enter at one breach ~19 units from the nearest shelter and walk at 2
        /// units/s (R-17), each swing kills one civilian per second (R-11 / R-18), and there are
        /// three shelters to walk between — which lands around 35 sim-seconds. The cap is 90
        /// sim-seconds: generous enough that a correct-but-slower implementation still passes, tight
        /// enough that a bug fails in seconds instead of hanging the runner.
        ///
        /// <b>The failure message names the stage that stalled</b>, because whoever debugs this
        /// later will be reading it rather than the code: it reports how many monsters exist, how
        /// many are alive, how many hold a target, how far the nearest one still is from it, and
        /// what each shelter has left — so "nothing spawned", "nobody was targeted", "they never
        /// moved", "they arrived and never swung" and "they are chewing an emptied shelter nobody
        /// re-targeted them off" are five different reports rather than one red X.
        /// </summary>
        [Test]
        public void A_driven_session_with_no_defenders_walks_the_colony_into_defeat()
        {
            var match = NewMatch();
            var session = new MatchSession(match.Host);

            var civiliansAtStart = match.State.TotalCivilians;
            Assert.That(civiliansAtStart, Is.EqualTo(20),
                "sanity (R-10): the v1 colony holds 20 civilians across three shelters");
            Assert.That(match.State.Heroes, Is.Empty, "no defenders: nobody fights back");
            Assert.That(match.State.Placeables, Is.Empty, "no defenders: nothing is built");

            session.Start();

            Assert.That(match.State.Wave.LivingMonsterIds, Is.Not.Empty,
                "R-19: the session must open with wave 1 in the colony, or there is nothing to drive");

            // 90 sim-seconds. The shipped numbers put defeat near 35s; see the doc above.
            const int MaxSteps = 90 * 60;

            var steps = 0;
            while (!match.State.IsOver && steps < MaxSteps)
            {
                session.Step(Step60Hz);
                steps++;
            }

            if (match.State.Status != MatchStatus.Defeat)
            {
                Assert.Fail(DescribeStalledSession(match, civiliansAtStart, steps, MaxSteps));
            }

            Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Defeat),
                "R-02: defeat is the colony emptied — every shelter at zero, not a shortcut to the flag");
            Assert.That(match.State.TotalCivilians, Is.EqualTo(0),
                "R-02: defeat is the colony emptied — every shelter at zero, not a shortcut to the flag");
        }

        // ==========================================================================================
        //  AC7 — placeable combat is driven so a live match can use the catalog
        // ==========================================================================================

        /// <summary>
        /// R-23 / G-028. <see cref="MatchSim.TurretTick"/> is a per-entity command T-10 left to
        /// "ticket 016", and nothing shipped ever called it — a 250-scrip turret was scenery.
        /// The first positive-delta combat step must fire immediately at the nearest monster in
        /// range, for the catalog damage (20), which is what makes 20 DPS at 1 Hz.
        /// </summary>
        [Test]
        public void A_turret_fires_on_the_first_combat_step_at_the_nearest_monster()
        {
            var match = NewMatch();
            var session = new MatchSession(match.Host);
            session.Start();

            var victim = FirstLiving(match.State);
            Isolate(match.State, victim);
            var hpBefore = victim.Hp;
            PlaceTurret(match.State, at: victim.Pos);

            session.Step(0.0);
            Assert.That(victim.Hp, Is.EqualTo(hpBefore),
                "a zero-delta pump is a refresh: it must not take a free turret shot");

            session.Step(Step60Hz);

            var turretDamage = match.Config.Placeables.StatsFor(PlaceableType.Turret).Damage;
            Assert.That(victim.Hp, Is.EqualTo(hpBefore - turretDamage).Within(SimTolerance),
                "R-23: the first combat step fires every standing turret at the nearest monster in range");

            // 1 Hz, not 60 Hz: half a second more must not be a second volley, or 20 DPS becomes
            // 1200 and the catalog row is fiction.
            var afterFirst = victim.Hp;
            var halfSecond = (int)Math.Round(0.5 / Step60Hz);
            for (var i = 0; i < halfSecond; i++)
            {
                session.Step(Step60Hz);
            }

            Assert.That(victim.Hp, Is.EqualTo(afterFirst).Within(SimTolerance),
                "R-23: turrets fire at 1 Hz (20 DPS); a second shot inside 0.5s is a per-frame melt");

            var untilSecond = (int)Math.Round(0.6 / Step60Hz);
            for (var i = 0; i < untilSecond; i++)
            {
                session.Step(Step60Hz);
            }

            Assert.That(victim.Hp, Is.EqualTo(afterFirst - turretDamage).Within(SimTolerance),
                "R-23: the next volley lands about one sim-second after the first");
        }

        /// <summary>
        /// R-02 / R-23 / R-40. Placeable damage flips <c>alive</c> at 0 HP without shrinking the
        /// wave roster. A session that never calls <see cref="MatchSim.RecordMonsterKill"/> for
        /// those corpses leaves them on <see cref="WaveState.LivingMonsterIds"/> forever — the
        /// wave never clears, planning never returns, and a 10-wave match cannot be won by
        /// defences. One turret tick that drops the last monster must count the kill.
        /// </summary>
        [Test]
        public void A_turret_kill_is_recorded_so_the_wave_can_clear()
        {
            var match = NewMatch();
            var session = new MatchSession(match.Host);
            session.Start();

            var roster = match.State.Wave.LivingMonsterIds.ToList();
            Assert.That(roster.Count, Is.GreaterThan(1), "wave 1 has a pack, not a single probe");

            var lastId = roster[roster.Count - 1];
            KillAll(match.Sim, match.State, roster.Where(id => id != lastId));

            var last = match.State.Monsters[lastId];
            last.Hp = match.Config.Placeables.StatsFor(PlaceableType.Turret).Damage;
            PlaceTurret(match.State, at: last.Pos, ownerPlayerId: "p_turret");

            var scripBefore = match.State.Team.Scrip;
            session.Step(Step60Hz);

            Assert.That(last.Alive, Is.False, "R-23: the tick that empties HP flags the corpse");
            Assert.That(match.State.Wave.LivingMonsterIds, Does.Not.Contain(lastId),
                "R-02: the session must RecordMonsterKill the placeable victim or the wave never ends");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore + match.Config.Monsters.StatsFor(last.Type).Bounty),
                "R-20: the catalog bounty still lands in the shared pool");
        }

        /// <summary>
        /// R-23 / G-027. Spike traps fire on footprint <i>entry</i>. Occupancy every frame would
        /// spend all ten triggers in ten pumps while a shambler stood still, which is not the
        /// catalog row.
        /// </summary>
        [Test]
        public void A_spike_trap_triggers_on_enter_not_every_frame()
        {
            var match = NewMatch();
            var session = new MatchSession(match.Host);
            session.Start();

            var victim = FirstLiving(match.State);
            victim.CurrentSpeed = 0.0;
            victim.BaseSpeed = 0.0;
            Isolate(match.State, victim);

            var trap = new Placeable
            {
                Id = "spike_probe",
                Type = PlaceableType.SpikeTrap,
                Pos = victim.Pos,
                OwnerPlayerId = "p_trap",
                Exists = true,
                Damage = match.Config.Placeables.StatsFor(PlaceableType.SpikeTrap).Damage,
                TriggersRemaining = match.Config.Placeables.StatsFor(PlaceableType.SpikeTrap).TriggerCount,
            };
            match.State.Placeables[trap.Id] = trap;

            var hpBefore = victim.Hp;
            var triggersBefore = trap.TriggersRemaining;

            session.Step(Step60Hz);

            Assert.That(victim.Hp, Is.EqualTo(hpBefore - trap.Damage).Within(SimTolerance),
                "R-23: the first step a monster stands on a spike is a crossing");
            Assert.That(trap.TriggersRemaining, Is.EqualTo(triggersBefore - 1),
                "G-027: one crossing spends one trigger");

            for (var i = 0; i < 10; i++)
            {
                session.Step(Step60Hz);
            }

            Assert.That(trap.TriggersRemaining, Is.EqualTo(triggersBefore - 1),
                "a monster standing on the trap must not spend another trigger per frame");
            Assert.That(trap.Exists, Is.True, "ten idle frames must not break a ten-trigger trap");
        }

        // ==========================================================================================
        //  AC6 — no game rule enters a MonoBehaviour
        // ==========================================================================================

        /// <summary>
        /// R-51. <b>This is a structural guard and is expected to be GREEN as soon as the stubs
        /// compile.</b> The enforcement for this criterion is
        /// <c>T10_HostLoopTests.No_MonoBehaviour_in_the_shell_writes_sim_world_state</c>, which
        /// walks the shell assembly's IL with Mono.Cecil and is unchanged by this ticket — the
        /// criterion is literally "that test stays green", so re-implementing the scan here would
        /// grade the same thing twice and drift from it.
        ///
        /// What this states instead is the shape that keeps it green, and it fails the moment
        /// somebody takes the obvious shortcut. A bootstrap is exactly where a MonoBehaviour that
        /// pokes the world gets written: a spawner component that news up a <see cref="Monster"/>,
        /// a "GameManager" that advances the wave in <c>Update</c>. So every seam this ticket adds —
        /// the session, the view binder, the loop and every implementation of the widened sim seam —
        /// must be a plain C# class, leaving <see cref="MatchHostBehaviour"/> the two-member pump it
        /// was built as.
        /// </summary>
        [Test]
        public void The_bootstrap_is_plain_C_sharp_so_no_game_rule_can_enter_a_MonoBehaviour()
        {
            var seams = new[]
            {
                typeof(MatchSession),
                typeof(MatchViewBinder),
                typeof(HostLoop),
                typeof(MatchSimHost),
            };

            foreach (var seam in seams)
            {
                Assert.That(typeof(MonoBehaviour).IsAssignableFrom(seam), Is.False,
                    "R-51: " + seam.FullName + " drives sim commands, so it must be a plain C# "
                    + "class — a MonoBehaviour here is what T10's IL invariant exists to reject");
            }

            var simDrivers = typeof(HostLoop).Assembly
                .GetTypes()
                .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IMatchSimHost).IsAssignableFrom(t))
                .ToList();

            Assert.That(simDrivers, Is.Not.Empty,
                "anti-vacuity: the shell must contain at least one IMatchSimHost for this guard to "
                + "have anything to check");

            foreach (var driver in simDrivers)
            {
                Assert.That(typeof(MonoBehaviour).IsAssignableFrom(driver), Is.False,
                    "R-51: " + driver.FullName + " is the seam every sim command travels through; "
                    + "it must never be a component");
            }
        }

        // ==========================================================================================
        //  scenario builders
        // ==========================================================================================

        /// <summary>Everything a driven session is assembled from, so no test wires it twice.</summary>
        private sealed class Match
        {
            public ColonyMap Map;
            public SimConfig Config;
            public MatchState State;
            public SimClock Clock;
            public MatchSim Sim;
            public MatchSimHost Host;
        }

        /// <summary>
        /// A live match on the shipped v1 colony (R-10): three shelters, 20 civilians, four
        /// breaches, the shipped wave table and the shipped stat catalog. Deliberately built from
        /// production types rather than through the golden fixture loader — the loader is the
        /// adapter's contract with eval/golden, not a scenario builder (the convention T10 set).
        ///
        /// No heroes and no placeables: every test that wants one adds it, and the defeat test
        /// wants neither.
        /// </summary>
        private static Match NewMatch()
        {
            var map = ColonyMap.V1();
            var config = new SimConfig();
            var state = map.CreateMatchState(config);

            // A match that is already running: R-03's lobby edge and the first planning phase are
            // ticket 004's, and this ticket is about what happens once combat is live.
            state.Phase = MatchPhase.Combat;
            state.Status = MatchStatus.InProgress;

            var clock = new SimClock();
            var sim = new MatchSim(state, config, null, clock, null) { ColonyMap = map };

            return new Match
            {
                Map = map,
                Config = config,
                State = state,
                Clock = clock,
                Sim = sim,
                Host = new MatchSimHost(sim, clock),
            };
        }

        /// <summary>A bare live match with nothing in it, for the two loop-level tests.</summary>
        private static MatchState NewCombatState()
        {
            return new MatchState
            {
                Phase = MatchPhase.Combat,
                Status = MatchStatus.InProgress,
            };
        }

        private static Hero NewHero(string id, Vec2 pos)
        {
            return new Hero
            {
                Id = id,
                HeroClass = HeroClass.Gunslinger,
                AccountId = "acc_" + id,
                Pos = pos,
                Hp = 100.0,
                MaxHp = 100.0,
                Alive = true,
            };
        }

        /// <summary>One frame of input, spelled without a device (ticket 016's convention).</summary>
        private static InputSnapshot Snapshot(Vector2 cursorGroundPoint, params PlayerKey[] pressed)
        {
            var snapshot = new InputSnapshot { CursorGroundPoint = cursorGroundPoint };
            foreach (var key in pressed)
            {
                snapshot.Pressed.Add(key);
            }

            return snapshot;
        }

        /// <summary>
        /// Clears a wave the way a team would, one kill at a time through the sim's own command
        /// (R-02 / R-20). Bounty is zero because economy is ticket 005's and no assertion here
        /// reads scrip.
        /// </summary>
        private static void KillAll(MatchSim sim, MatchState state, IEnumerable<string> monsterIds)
        {
            foreach (var id in monsterIds.ToList())
            {
                sim.RecordMonsterKill(new MonsterKillRequest
                {
                    MonsterId = id,
                    MonsterType = state.Monsters.TryGetValue(id, out var monster) ? monster.Type : null,
                    Bounty = 0,
                });
            }
        }

        private static Monster FirstLiving(MatchState state)
        {
            foreach (var id in state.Wave.LivingMonsterIds)
            {
                if (state.Monsters.TryGetValue(id, out var monster) && monster != null && monster.Alive)
                {
                    return monster;
                }
            }

            Assert.Fail("R-19: a started match must have a living monster to aim a turret at");
            return null;
        }

        private static void Isolate(MatchState state, Monster keep)
        {
            foreach (var monster in state.Monsters.Values)
            {
                if (monster != null && monster.Id != keep.Id)
                {
                    monster.Pos = new Vec2(1000.0, 1000.0);
                }
            }
        }

        /// <summary>
        /// Drop a catalog turret onto the field without going through planning purchase — these
        /// tests grade combat drive, and wave 1 opens in combat (ticket 011).
        /// </summary>
        private static void PlaceTurret(MatchState state, Vec2 at, string ownerPlayerId = "p1")
        {
            var stats = new SimConfig().Placeables.StatsFor(PlaceableType.Turret);
            state.Placeables["turret_probe"] = new Placeable
            {
                Id = "turret_probe",
                Type = PlaceableType.Turret,
                Pos = at,
                OwnerPlayerId = ownerPlayerId,
                Exists = true,
                Damage = stats.Damage,
                Range = stats.Range,
            };
        }

        /// <summary>
        /// Drives the session until <paramref name="done"/> answers true or
        /// <paramref name="maxSeconds"/> of sim time has passed. Bounded so a session that never
        /// progresses fails as a test failure rather than as a hung runner — the same reading T10's
        /// StepUntil takes.
        /// </summary>
        private static bool DriveUntil(
            MatchSession session, SimClock clock, Func<bool> done, double maxSeconds, double dt = Step60Hz)
        {
            var maxSteps = (int)(maxSeconds / dt) + 64;

            for (var i = 0; i < maxSteps; i++)
            {
                if (done())
                {
                    return true;
                }

                session.Step(dt);

                if (clock.ElapsedSeconds > maxSeconds)
                {
                    break;
                }
            }

            return done();
        }

        /// <summary>
        /// Why the colony is still standing, stage by stage, in the order the chain runs. Written
        /// for whoever reads the failure rather than for whoever wrote the test: each line answers
        /// one "did this stage happen at all?" so the reader can see where the session stopped
        /// instead of bisecting the bootstrap.
        /// </summary>
        private static string DescribeStalledSession(Match match, int civiliansAtStart, int steps, int maxSteps)
        {
            var state = match.State;
            var living = state.Monsters.Values.Where(m => m.Alive).ToList();
            var targeted = living.Where(m => !string.IsNullOrEmpty(m.TargetId)).ToList();

            var sb = new StringBuilder();
            sb.Append("R-02: the colony never fell. Drove ").Append(steps).Append('/').Append(maxSteps)
              .Append(" host steps (").Append(Fmt(match.Clock.ElapsedSeconds))
              .Append("s of sim time); status is '").Append(state.Status).Append("', phase '")
              .Append(state.Phase).Append("'.").AppendLine();

            sb.Append("  stage 1 spawn   : ").Append(state.Monsters.Count).Append(" monster(s) in the world, ")
              .Append(living.Count).Append(" alive, ").Append(state.Wave.LivingMonsterIds.Count)
              .Append(" on the wave roster").AppendLine();

            sb.Append("  stage 2 target  : ").Append(targeted.Count).Append('/').Append(living.Count)
              .Append(" living monster(s) hold a target (SelectTarget, R-16 — SpawnWave leaves TargetId null)")
              .AppendLine();

            sb.Append("  stage 3 move    : ").Append(DescribeApproach(state, targeted)).AppendLine();

            sb.Append("  stage 4 damage  : civilians ").Append(civiliansAtStart).Append(" -> ")
              .Append(state.TotalCivilians).Append(" [")
              .Append(string.Join(", ", state.Hotspots.Values
                  .OrderBy(h => h.Id, StringComparer.Ordinal)
                  .Select(h => h.Id + "=" + h.Civilians)))
              .Append("] (R-18 gate then R-11 damage)").AppendLine();

            sb.Append("  stage 5 defeat  : R-02 fires on the hit that empties the last shelter");

            return sb.ToString();
        }

        private static string DescribeApproach(MatchState state, List<Monster> targeted)
        {
            if (targeted.Count == 0)
            {
                return "no targeted monster to measure — stage 2 has to work first";
            }

            var gaps = new List<double>();
            foreach (var monster in targeted)
            {
                if (state.Hotspots.TryGetValue(monster.TargetId, out var hotspot))
                {
                    gaps.Add(monster.Pos.DistanceTo(hotspot.Pos));
                }
                else if (state.Heroes.TryGetValue(monster.TargetId, out var hero))
                {
                    gaps.Add(monster.Pos.DistanceTo(hero.Pos));
                }
                else if (state.Placeables.TryGetValue(monster.TargetId, out var placeable))
                {
                    gaps.Add(monster.Pos.DistanceTo(placeable.Pos));
                }
            }

            if (gaps.Count == 0)
            {
                return "every target id resolves to nothing in the world — targeting is pointing at ghosts";
            }

            return "distance to target: nearest " + Fmt(gaps.Min()) + ", furthest " + Fmt(gaps.Max())
                   + " (0 means arrived, so the stall is downstream of movement)";
        }

        private static string Fmt(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Horizontal placement only, matching T16 — vertical offset is presentation.</summary>
        private static void AssertStandsAt(Vector3 actualWorld, Vec2 expectedGround, string what)
        {
            var expected = SimSpace.ToWorld(expectedGround);

            Assert.That(actualWorld.x, Is.EqualTo(expected.x).Within(PositionTolerance),
                what + ": x must match the sim position " + expectedGround
                + " — a bound view has to keep reading the world, not its first frame");
            Assert.That(actualWorld.z, Is.EqualTo(expected.z).Within(PositionTolerance),
                what + ": z must match the sim position " + expectedGround);
        }

        // ==========================================================================================
        //  editor-scene bookkeeping
        // ==========================================================================================

        private MatchViewBinder TrackBinder(MatchViewBinder binder)
        {
            _binders.Add(binder);
            return binder;
        }

        private void CollectViewsOf(MatchViewBinder binder)
        {
            if (binder == null)
            {
                return;
            }

            if (binder.Root != null)
            {
                _spawned.Add(binder.Root);
            }

            foreach (var id in binder.BoundMonsterIds)
            {
                var view = binder.MonsterViewFor(id);
                if (view != null)
                {
                    _spawned.Add(view.gameObject);
                }
            }

            foreach (var id in binder.BoundHeroIds)
            {
                var view = binder.HeroViewFor(id);
                if (view != null)
                {
                    _spawned.Add(view.gameObject);
                }
            }
        }

        // ==========================================================================================
        //  test doubles
        // ==========================================================================================

        /// <summary>
        /// This step's hero intents, scripted — the hero-side twin of T10's StubAttackSource. The
        /// intents it hands out come from the real <see cref="DefaultHeroInputMap"/>; only *when*
        /// they arrive is scripted.
        /// </summary>
        private sealed class ScriptedHeroIntents : IHeroIntentSource
        {
            private readonly List<HeroIntentCommand> _commands = new List<HeroIntentCommand>();

            /// <summary>Replaces the standing intent for one hero.</summary>
            public void Set(string heroId, HeroIntent intent)
            {
                _commands.RemoveAll(c => c.HeroId == heroId);
                _commands.Add(new HeroIntentCommand { HeroId = heroId, Intent = intent });
            }

            public IReadOnlyList<HeroIntentCommand> IntentsThisStep(ISimHost sim, double deltaSeconds)
            {
                return _commands;
            }
        }
    }
}
