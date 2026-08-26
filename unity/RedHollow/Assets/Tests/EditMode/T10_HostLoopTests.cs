using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using RedHollow.Game.Host;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 010 (T-10): the sim host loop and the shell architecture invariant. Requirements
    /// R-50 and R-52, plus the R-51 boundary R-50/R-52 rest on. Grades no golden fixture — every
    /// rule the fixtures cover already lives in <see cref="MatchSim"/> and is green; what is not
    /// covered anywhere is whether anything ever *calls* it.
    ///
    /// Four things are pinned here and nothing else:
    ///
    ///  1. <b>Every sim tick the sim cannot schedule itself is driven by one host step.</b>
    ///     <see cref="MatchSim"/> has no timer of its own — <see cref="MatchSim.TickPlanningTimer"/>
    ///     (R-03), <see cref="MatchSim.TickStatusEffects"/> (R-31),
    ///     <see cref="MatchSim.TickHeroRegen"/> (R-35), <see cref="MatchSim.TickHeroRespawns"/>
    ///     (R-33) and <see cref="MatchSim.TickMedStations"/> (R-23) all sit there inert until a host
    ///     calls them. A host that forgets one loses a whole requirement silently: dead heroes never
    ///     come back, slows never expire, planning never ends.
    ///
    ///  2. <b>Monster damage is gated through <see cref="MatchSim.TryMonsterAttack"/>, before the
    ///     hit.</b> R-18's cadence is advisory by construction (see MatchSim.Combat.cs): the sim
    ///     cannot force the host to ask. A host that damages first, or never asks, lands one hit per
    ///     frame instead of one per second.
    ///
    ///  3. <b>No game rule appears in a MonoBehaviour</b> — enforced by walking the shell
    ///     assembly's IL, not by review. See
    ///     <see cref="No_MonoBehaviour_in_the_shell_writes_sim_world_state"/> for exactly what the
    ///     scan does and does not catch.
    ///
    ///  4. <b>The shell never mutates sim state directly.</b> Driven against a sim that does
    ///     nothing at all: if the world moved, the shell moved it.
    ///
    /// Scene, camera, input, visuals and real transport are tickets 016 and 011 and are deliberately
    /// absent. The R-50 and R-52 seams here are pinned at their boundaries only — party size, and
    /// the direction interpolation and reconciliation must travel — because the PRD states no curve,
    /// no smoothing rate and no error budget, and a guessed number here would ship as spec.
    /// </summary>
    [TestFixture]
    public class T10_HostLoopTests
    {
        private const double Tolerance = 1e-9;

        /// <summary>One host step at Unity's default fixed timestep. Nothing depends on the value.</summary>
        private const double Step60Hz = 1.0 / 60.0;

        /// <summary>
        /// The ticks <see cref="MatchSim"/> exposes that nothing but a host loop can ever call.
        /// Listed so a host that drops one fails by name; the completeness half — that this list is
        /// still the whole list — is checked reflectively in the same test.
        /// </summary>
        private static readonly string[] SimTicksOnlyTheHostCanDrive =
        {
            "TickPlanningTimer",
            "TickStatusEffects",
            "TickHeroRegen",
            "TickHeroRespawns",
            "TickMedStations",
        };

        // ==========================================================================================
        //  AC1 — every sim tick the sim cannot schedule itself is driven by the host loop
        // ==========================================================================================

        /// <summary>
        /// R-03/R-23/R-31/R-33/R-35. One step must pump every parameterless <c>Tick*</c> the sim
        /// exposes.
        ///
        /// The expected set is derived from <see cref="MatchSim"/> by reflection rather than
        /// hardcoded, so a tick added by a later ticket is automatically something the host must
        /// drive — the failure mode this ticket exists to close is precisely "a tick nobody calls".
        /// Parameterless is the discriminator that separates a host-loop tick from a per-entity
        /// command such as <see cref="MatchSim.TurretTick"/>, which the host drives per turret and
        /// which ticket 016 owns.
        ///
        /// Order is NOT asserted: the PRD states none, and pinning one would reject a correct host.
        /// </summary>
        [Test]
        public void One_host_step_drives_every_sim_tick_the_sim_cannot_schedule_itself()
        {
            var ticks = typeof(MatchSim)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name.StartsWith("Tick", StringComparison.Ordinal) && m.GetParameters().Length == 0)
                .Select(m => m.Name)
                .Distinct()
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.That(ticks, Is.SupersetOf(SimTicksOnlyTheHostCanDrive),
                "the five documented host-driven ticks must still exist on MatchSim");

            var sim = new RecordingSimHost(NewState());
            var loop = new HostLoop(sim, new StubAttackSource());

            loop.Step(Step60Hz);

            Assert.That(sim.Calls, Is.SupersetOf(ticks),
                "one host step must drive every parameterless MatchSim tick; missing: "
                + string.Join(", ", ticks.Where(t => !sim.Calls.Contains(t))));
        }

        /// <summary>
        /// R-03 / DEC-006. Planning ends on its own clock. Without
        /// <see cref="MatchSim.TickPlanningTimer"/> being driven, one AFK player hangs the match.
        /// </summary>
        [Test]
        public void Host_loop_ends_the_planning_phase_when_its_timer_elapses()
        {
            var state = NewState();
            state.Phase = MatchPhase.Planning;
            state.PlanningStartedAt = 0.0;

            var clock = new SimClock();
            var config = new SimConfig();
            var loop = LoopOver(state, config, clock);

            StepUntil(loop, clock, config.PlanningDurationSeconds + 1.0);

            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Combat),
                "R-03: planning runs for PlanningDurationSeconds and combat begins when it elapses");
        }

        /// <summary>
        /// R-31. Without <see cref="MatchSim.TickStatusEffects"/> being driven, a lasso slow or a
        /// Bulwark guard never expires. The effect type is deliberately an arbitrary string — the
        /// sim's own status keys are private, and this asserts expiry, not any one ability.
        /// </summary>
        [Test]
        public void Host_loop_expires_status_effects_when_their_deadline_passes()
        {
            var state = NewState();
            var monster = state.Monsters["m1"];
            monster.CurrentSpeed = 1.0;
            monster.StatusEffects.Add(new StatusEffect("t10_probe_effect", 3.0));

            var clock = new SimClock();
            var loop = LoopOver(state, new SimConfig(), clock);

            StepUntil(loop, clock, 4.0);

            Assert.That(monster.StatusEffects, Is.Empty,
                "R-31: an effect past its expires_at must be gone once the host has ticked");
        }

        /// <summary>
        /// R-33 / DEC-010. Without <see cref="MatchSim.TickHeroRespawns"/> being driven, a dead hero
        /// stays dead for the rest of the match — the single most expensive missing tick.
        /// </summary>
        [Test]
        public void Host_loop_respawns_a_dead_hero_once_its_deadline_passes()
        {
            var state = NewState();
            var hero = state.Heroes["h1"];
            hero.Alive = false;
            hero.Hp = 0.0;
            hero.RespawnAt = 10.0;

            var clock = new SimClock();
            var loop = LoopOver(state, new SimConfig(), clock);

            StepUntil(loop, clock, 11.0);

            Assert.That(hero.Alive, Is.True, "R-33: the hero is back");
            Assert.That(hero.Hp, Is.EqualTo(hero.MaxHp).Within(Tolerance), "R-33: back at full HP");
        }

        /// <summary>
        /// R-35. Without <see cref="MatchSim.TickHeroRegen"/> being driven there is no
        /// out-of-combat healing at all. Direction only — the rate is the sim's business.
        /// </summary>
        [Test]
        public void Host_loop_regenerates_a_hero_left_out_of_combat()
        {
            var state = NewState();
            var hero = state.Heroes["h1"];
            hero.Hp = 50.0;
            hero.LastDamagedAt = 0.0;

            var clock = new SimClock();
            var config = new SimConfig();
            var loop = LoopOver(state, config, clock);

            StepUntil(loop, clock, config.RegenDelaySeconds + 5.0);

            Assert.That(hero.Hp, Is.GreaterThan(50.0),
                "R-35: a hero untouched past RegenDelaySeconds heals while the host ticks");
        }

        /// <summary>
        /// R-23. Without <see cref="MatchSim.TickMedStations"/> being driven, a 200-scrip Med
        /// Station heals nobody. <see cref="Hero.LastDamagedAt"/> is pushed far into the future so
        /// out-of-combat regen (R-35) cannot be what moves the bar.
        /// </summary>
        [Test]
        public void Host_loop_runs_the_med_station_aura()
        {
            var state = NewState();
            var hero = state.Heroes["h1"];
            hero.Hp = 50.0;
            hero.Pos = new Vec2(0.0, 0.0);
            hero.LastDamagedAt = 1000.0;

            state.Placeables["med1"] = new Placeable
            {
                Id = "med1",
                Type = PlaceableType.MedStation,
                Pos = new Vec2(0.0, 0.0),
                OwnerPlayerId = "p1",
                PurchaseCost = 200,
                Exists = true,
            };

            var clock = new SimClock();
            var loop = LoopOver(state, new SimConfig(), clock);

            StepUntil(loop, clock, 2.0);

            Assert.That(hero.Hp, Is.GreaterThan(50.0),
                "R-23: a hero standing in a Med Station's radius heals while the host ticks");
        }

        // ==========================================================================================
        //  AC2 — monster damage is gated through TryMonsterAttack, before the hit
        // ==========================================================================================

        /// <summary>
        /// R-18. A permitted gate routes the intent to the damage command for its target kind, and
        /// to that one only — a monster swinging at a shelter must not also be charged to a hero.
        /// </summary>
        [TestCase(TargetKind.Hotspot, nameof(ISimHost.ApplyHotspotAttack))]
        [TestCase(TargetKind.Hero, nameof(ISimHost.ApplyHeroDamage))]
        [TestCase(TargetKind.Barricade, nameof(ISimHost.ApplyPlaceableDamage))]
        public void A_permitted_gate_routes_damage_to_the_command_for_that_target(
            TargetKind kind, string expectedCommand)
        {
            var sim = new RecordingSimHost(NewState()) { GateAnswer = true };
            var loop = new HostLoop(sim, new StubAttackSource(Intent(kind)));

            loop.Step(Step60Hz);

            Assert.That(sim.GateQueries, Does.Contain("m1"),
                "R-18: the host must ask TryMonsterAttack for the attacking monster");
            Assert.That(sim.Calls, Does.Contain(expectedCommand));
            foreach (var other in DamageCommands.Where(c => c != expectedCommand))
            {
                Assert.That(sim.Calls, Does.Not.Contain(other),
                    "only the command matching the intent's target kind may be issued");
            }
        }

        /// <summary>
        /// R-18. A refused gate issues NO damage command — for any target kind. This is the half
        /// that keeps the colony standing: without it the host applies damage every frame.
        /// </summary>
        [TestCase(TargetKind.Hotspot)]
        [TestCase(TargetKind.Hero)]
        [TestCase(TargetKind.Barricade)]
        public void A_refused_gate_issues_no_damage_command_at_all(TargetKind kind)
        {
            var sim = new RecordingSimHost(NewState()) { GateAnswer = false };
            var loop = new HostLoop(sim, new StubAttackSource(Intent(kind)));

            loop.Step(Step60Hz);

            Assert.That(sim.GateQueries, Does.Contain("m1"),
                "R-18: the gate must be asked even when the answer turns out to be no");
            foreach (var command in DamageCommands)
            {
                Assert.That(sim.Calls, Does.Not.Contain(command),
                    "R-18: a refused gate must apply no damage; " + command + " was issued anyway");
            }
        }

        /// <summary>
        /// R-18, asserted on the world rather than on call order — the property that actually
        /// separates gating BEFORE the hit from gating after it.
        ///
        /// A shambler swinging for 10 kills exactly one civilian per landed hit (R-11: 10 damage /
        /// DamagePerCivilian 10). Ninety 60Hz steps is 1.5 seconds of sim time, which permits
        /// exactly two swings at R-18's one-per-second cadence. A host that never gates, or that
        /// gates only after applying the hit, lands ninety.
        ///
        /// Driven through the real <see cref="MatchSim"/>, not a fake: the cadence bookkeeping is
        /// the sim's and the point is that the host consults it.
        /// </summary>
        [Test]
        public void Steps_inside_one_attack_interval_land_exactly_one_hit()
        {
            var state = NewState();
            var clock = new SimClock();
            var config = new SimConfig();
            var loop = LoopOver(state, config, clock, Intent(TargetKind.Hotspot));

            // 0.5s of sim time: strictly inside R-18's one-second interval.
            for (var i = 0; i < 30; i++)
            {
                loop.Step(Step60Hz);
            }

            Assert.That(state.Hotspots["chapel"].Civilians, Is.EqualTo(19),
                "R-18: thirty host steps inside one attack interval are still one swing");

            // On to 1.5s: exactly one more swing has come due.
            for (var i = 30; i < 90; i++)
            {
                loop.Step(Step60Hz);
            }

            Assert.That(state.Hotspots["chapel"].Civilians, Is.EqualTo(18),
                "R-18: 1.5s of host steps is two swings, not one and not ninety");
        }

        // ==========================================================================================
        //  AC3 — no game rule appears in a MonoBehaviour (mechanically enforced)
        // ==========================================================================================

        /// <summary>
        /// R-51. <b>The architecture invariant.</b> No MonoBehaviour in the shell assembly may write
        /// sim world state. Every mutation of the match belongs to a <see cref="MatchSim"/> command,
        /// which is a method call — so a MonoBehaviour that stores into
        /// <see cref="Monster.Hp"/>, <see cref="Hotspot.Civilians"/>, <see cref="TeamState.Scrip"/>
        /// or <see cref="Hero.Alive"/>, or that adds to / removes from one of the world's
        /// collections, has by definition taken a rule out of the sim and put it in the shell.
        ///
        /// <b>Mechanism.</b> Mono.Cecil reads the compiled shell assembly and, for every
        /// MonoBehaviour type in it — plus every nested type, which is where lambdas, local
        /// functions and iterator/async state machines end up — flags:
        ///  * <c>stfld</c> / <c>stsfld</c> targeting a field declared by a sim world-state type;
        ///  * <c>ldflda</c> / <c>ldsflda</c> on a <i>primitive</i> field of one (taking the address
        ///    of an int/double/bool field is a by-ref write; taking the address of a
        ///    <see cref="Vec2"/> field is just how a struct method is called, so it is not flagged);
        ///  * a call to a property setter declared by a sim world-state type;
        ///  * a mutating collection call (Add/Remove/Clear/Insert/RemoveAt/set_Item/...) whose
        ///    receiver was loaded out of a sim world-state member — which is how a spawner writing
        ///    <c>State.Monsters</c> or a buff writing <c>hero.StatusEffects</c> shows up.
        ///
        /// The set of "sim world-state types" is DERIVED, not listed: it is the transitive closure
        /// of field and property types reachable from <see cref="MatchState"/> and
        /// <see cref="AccountProfile"/> inside the GameSim assembly. So a field added to
        /// <see cref="Hero"/> tomorrow is covered without editing this test, while
        /// <see cref="SimConfig"/> and the command request/result types are correctly NOT covered:
        /// R-16 says the shell overrides tunables, and building a
        /// <see cref="HotspotAttackRequest"/> is how a command is issued, not a rule.
        ///
        /// <b>One deliberate strictness</b>, confirmed by the proof run: an object initializer such
        /// as <c>new Monster { Hp = ... }</c> inside a MonoBehaviour is flagged, because it compiles
        /// to <c>stfld Monster::Hp</c>. That is intended — authoring sim entities is the wave
        /// spawner's job (R-19) and belongs in a plain C# class like everything else here — and the
        /// fix is a move, not a rewrite.
        ///
        /// <b>What it does not catch</b>, stated plainly rather than implied: a MonoBehaviour that
        /// calls a plain helper class which then mutates; and rule arithmetic that never touches sim
        /// state (a MonoBehaviour computing damage into a local). Both were considered. Neither can
        /// be detected without either dataflow analysis or a literal-value denylist that would
        /// false-positive on legitimate presentation code (a health bar computing hp/maxHp, a layout
        /// constant that happens to equal a catalog number), and a test that rejects correct code is
        /// worse than one with a stated edge. <see cref="A_host_step_against_an_inert_sim_leaves_the_world_untouched"/>
        /// closes the first gap behaviourally for the host loop itself.
        /// </summary>
        [Test]
        public void No_MonoBehaviour_in_the_shell_writes_sim_world_state()
        {
            var worldStateTypes = SimWorldStateTypeNames();
            Assert.That(worldStateTypes, Does.Contain("RedHollow.Sim.Monster"),
                "sanity: the derived world-state set must include the entities R-16/R-18 mutate");

            var violations = new List<string>();

            using (var module = ReadShellModule())
            {
                foreach (var type in MonoBehaviourTypesAndTheirNested(module))
                {
                    foreach (var method in type.Methods.Where(m => m.HasBody))
                    {
                        violations.AddRange(WorldStateWritesIn(type, method, worldStateTypes));
                    }
                }
            }

            Assert.That(violations, Is.Empty,
                "R-51: a MonoBehaviour must never write sim state — issue a MatchSim command "
                + "instead, or move the code into a plain C# class. Found:\n  "
                + string.Join("\n  ", violations));
        }

        /// <summary>
        /// Anti-vacuity guard for the test above, and the one test in this file that is expected to
        /// be GREEN from the moment the stubs compile. An IL scan over an assembly containing no
        /// MonoBehaviour passes for the wrong reason and proves nothing, so the scan asserts it
        /// actually had something to look at.
        /// </summary>
        [Test]
        public void The_shell_assembly_contains_MonoBehaviours_for_the_invariant_to_scan()
        {
            using (var module = ReadShellModule())
            {
                Assert.That(MonoBehaviourTypesAndTheirNested(module), Is.Not.Empty,
                    "the architecture invariant scans MonoBehaviours in " + ShellAssembly().GetName().Name
                    + "; with none present it passes vacuously and enforces nothing");
            }
        }

        // ==========================================================================================
        //  AC4 — GameSim stays engine-free
        // ==========================================================================================

        /// <summary>
        /// R-51. The loaded GameSim assembly references nothing from Unity. Expected GREEN today —
        /// this is a structural guard against a later ticket adding a UnityEngine using to the sim,
        /// not a behaviour this ticket introduces.
        /// </summary>
        [Test]
        public void GameSim_is_loaded_with_zero_Unity_references()
        {
            var sim = typeof(MatchSim).Assembly;

            var unityRefs = sim.GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(n => n.StartsWith("Unity", StringComparison.Ordinal))
                .ToList();

            Assert.That(unityRefs, Is.Empty,
                "R-51: GameSim must run with no engine reference; found " + string.Join(", ", unityRefs));
        }

        /// <summary>
        /// R-51. The asmdef itself still declares the constraint, so the guarantee survives a future
        /// editor-side "fix" that adds a reference. Expected GREEN today — structural guard.
        /// </summary>
        [Test]
        public void GameSim_asmdef_still_declares_noEngineReferences()
        {
            var path = Path.Combine(Application.dataPath, "GameSim", "GameSim.asmdef");
            Assert.That(File.Exists(path), Is.True, "expected the GameSim asmdef at " + path);

            var json = File.ReadAllText(path);

            Assert.That(json, Does.Match("\"noEngineReferences\"\\s*:\\s*true"),
                "R-51: GameSim.asmdef must keep noEngineReferences true");
            Assert.That(json, Does.Match("\"references\"\\s*:\\s*\\[\\s*\\]"),
                "R-51: GameSim.asmdef must reference no other assembly");
        }

        // ==========================================================================================
        //  AC5 — the shell never mutates sim state directly
        // ==========================================================================================

        /// <summary>
        /// R-51. Driven against an <see cref="ISimHost"/> that records its calls and does nothing
        /// else: every tick is a no-op, the gate says yes, the damage commands are no-ops. If any
        /// part of the world moved during the step, the shell moved it — there was nobody else.
        ///
        /// This is the behavioural complement to the IL invariant: it covers the host loop
        /// regardless of whether the mutation was written in a MonoBehaviour or in a plain helper it
        /// delegates to.
        /// </summary>
        [Test]
        public void A_host_step_against_an_inert_sim_leaves_the_world_untouched()
        {
            var state = NewState();
            var before = Snapshot(state);

            var sim = new RecordingSimHost(state) { GateAnswer = true };
            var loop = new HostLoop(
                sim,
                new StubAttackSource(
                    Intent(TargetKind.Hotspot),
                    Intent(TargetKind.Hero),
                    Intent(TargetKind.Barricade)));

            loop.Step(Step60Hz);

            Assert.That(Snapshot(state), Is.EqualTo(before),
                "R-51: with a sim that applies nothing, one host step must change nothing — "
                + "all mutation goes through MatchSim commands");
        }

        /// <summary>
        /// R-51 / R-11. The other direction: with a real sim, the world moves by exactly what the
        /// sim's rule says and no more. 25 damage into a shelter is ceil(25 / DamagePerCivilian) = 3
        /// civilians; a shell that also decremented, rounded or "helpfully" applied a second hit
        /// lands somewhere else.
        /// </summary>
        [Test]
        public void One_gated_hit_moves_the_world_by_exactly_what_the_sim_computed()
        {
            var state = NewState();
            var clock = new SimClock();
            var loop = LoopOver(state, new SimConfig(), clock, Intent(TargetKind.Hotspot, 25.0));

            loop.Step(Step60Hz);

            Assert.That(state.Hotspots["chapel"].Civilians, Is.EqualTo(17),
                "R-11: the sim owns the arithmetic; the shell contributes nothing to it");
        }

        // ==========================================================================================
        //  R-23 / R-02 / R-20 — placeable last-hits reaped through RecordMonsterKill
        // ==========================================================================================

        /// <summary>
        /// TurretTick (G-028) drops HP and DamageMonster flips <c>alive</c> so a corpse is not
        /// shot twice, but wave roster and bounty still run through RecordMonsterKill — the same
        /// command hero last-hits use. A host that never issues it after a turret last-hit leaves
        /// a dead monster on the living roster and the wave stalls.
        /// </summary>
        [Test]
        public void A_turret_last_hit_is_reaped_through_RecordMonsterKill()
        {
            var state = NewState();
            var monster = state.Monsters["m1"];
            monster.Hp = 20.0;
            monster.Pos = new Vec2(4.0, 0.0);
            monster.CurrentSpeed = 0.0;
            monster.BaseSpeed = 0.0;

            state.Placeables["t1"] = new Placeable
            {
                Id = "t1",
                Type = PlaceableType.Turret,
                Pos = new Vec2(0.0, 0.0),
                OwnerPlayerId = "p1",
                PurchaseCost = 250,
                Damage = 20.0,
                Range = 8.0,
                Exists = true,
            };

            var scripBefore = state.Team.Scrip;
            var bounty = new SimConfig().Monsters.StatsFor(monster.Type).Bounty;
            var clock = new SimClock();
            var loop = LoopOver(state, new SimConfig(), clock);

            StepUntil(loop, clock, 1.0);

            Assert.That(monster.Hp, Is.EqualTo(0.0).Within(Tolerance),
                "R-23: one 20-damage turret tick emptied a 20 HP monster");
            Assert.That(monster.Alive, Is.False,
                "G-029's convention: a placeable last-hit flips alive so the corpse is not hit twice");
            Assert.That(state.Wave.LivingMonsterIds, Does.Not.Contain("m1"),
                "R-02: the host reaped the last-hit through RecordMonsterKill, so the roster shrank");
            Assert.That(state.Team.Scrip, Is.EqualTo(scripBefore + bounty),
                "R-20: the kill paid its catalog bounty into the shared pool exactly once");
            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Planning),
                "R-02: the last living monster's turret last-hit completed the wave");
        }

        /// <summary>
        /// Same gap for traps: TriggerPlaceable (G-027 / G-029) can empty a body without ever
        /// calling RecordMonsterKill. The host detects the enter and must reap the last-hit.
        /// </summary>
        [Test]
        public void A_trap_last_hit_is_reaped_through_RecordMonsterKill()
        {
            var state = NewState();
            var monster = state.Monsters["m1"];
            monster.Hp = 30.0;
            monster.Pos = new Vec2(3.0, 0.0);
            monster.CurrentSpeed = 0.0;
            monster.BaseSpeed = 0.0;

            state.Placeables["trap1"] = new Placeable
            {
                Id = "trap1",
                Type = PlaceableType.SpikeTrap,
                Pos = new Vec2(3.0, 0.0),
                OwnerPlayerId = "p1",
                PurchaseCost = 75,
                Damage = 30.0,
                TriggersRemaining = 10,
                Exists = true,
            };

            var scripBefore = state.Team.Scrip;
            var bounty = new SimConfig().Monsters.StatsFor(monster.Type).Bounty;
            var clock = new SimClock();
            var loop = LoopOver(state, new SimConfig(), clock);

            loop.Step(Step60Hz);

            Assert.That(monster.Hp, Is.EqualTo(0.0).Within(Tolerance),
                "R-23: the spike's 30 damage emptied a 30 HP monster on the crossing");
            Assert.That(monster.Alive, Is.False);
            Assert.That(state.Wave.LivingMonsterIds, Does.Not.Contain("m1"),
                "R-02: a trap last-hit is reaped through RecordMonsterKill, same as a turret");
            Assert.That(state.Team.Scrip, Is.EqualTo(scripBefore + bounty),
                "R-20: the trap last-hit paid bounty once");
        }

        /// <summary>
        /// A turret tick that does not kill must not reap: G-028 is a 40→20 ravager, still alive
        /// and still on the roster. Reaping a wounded body would complete the wave with walkers.
        /// </summary>
        [Test]
        public void A_turret_tick_that_does_not_kill_leaves_the_roster_alone()
        {
            var state = NewState();
            var monster = state.Monsters["m1"];
            monster.Hp = 40.0;
            monster.Pos = new Vec2(4.0, 0.0);
            monster.CurrentSpeed = 0.0;
            monster.BaseSpeed = 0.0;

            state.Placeables["t1"] = new Placeable
            {
                Id = "t1",
                Type = PlaceableType.Turret,
                Pos = new Vec2(0.0, 0.0),
                OwnerPlayerId = "p1",
                Damage = 20.0,
                Range = 8.0,
                Exists = true,
            };

            var scripBefore = state.Team.Scrip;
            var clock = new SimClock();
            var loop = LoopOver(state, new SimConfig(), clock);

            StepUntil(loop, clock, 1.0);

            Assert.That(monster.Hp, Is.EqualTo(20.0).Within(Tolerance));
            Assert.That(monster.Alive, Is.True);
            Assert.That(state.Wave.LivingMonsterIds, Does.Contain("m1"));
            Assert.That(state.Team.Scrip, Is.EqualTo(scripBefore),
                "a non-killing tick pays no bounty");
            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Combat));
        }

        // ==========================================================================================
        //  R-52 — client interpolation and host reconciliation seams (shape and direction only)
        // ==========================================================================================

        /// <summary>
        /// R-52, remote half. A sample taken between two replicated positions lies between them, and
        /// the endpoints are the samples themselves. The curve is not pinned — ticket 011 owns real
        /// replication — so nothing here asserts linearity or a rate.
        /// </summary>
        [Test]
        public void Remote_entity_interpolation_stays_between_its_two_samples()
        {
            var lerp = new RemoteEntityInterpolator();
            var from = new Vec2(0.0, 0.0);
            var to = new Vec2(10.0, -4.0);

            var quarter = lerp.Sample(from, to, 0.25);
            var half = lerp.Sample(from, to, 0.5);
            var threeQuarters = lerp.Sample(from, to, 0.75);

            Assert.That(lerp.Sample(from, to, 0.0), Is.EqualTo(from), "t=0 is the earlier sample");
            Assert.That(lerp.Sample(from, to, 1.0), Is.EqualTo(to), "t=1 is the later sample");

            foreach (var mid in new[] { quarter, half, threeQuarters })
            {
                Assert.That(mid.X, Is.GreaterThan(from.X).And.LessThan(to.X),
                    "R-52: an interpolated x must lie between the two samples");
                Assert.That(mid.Y, Is.LessThan(from.Y).And.GreaterThan(to.Y),
                    "R-52: an interpolated y must lie between the two samples");
            }

            Assert.That(quarter.X, Is.LessThan(half.X), "R-52: later t is further along");
            Assert.That(half.X, Is.LessThan(threeQuarters.X), "R-52: later t is further along");
        }

        /// <summary>
        /// R-52, own-hero half. The client predicts its own movement and the host is authoritative
        /// (R-51), so reconciling repeatedly against a fixed authoritative position must never move
        /// the prediction further away, and must eventually arrive.
        ///
        /// Direction and convergence only: the PRD states no smoothing rate and no error budget, so
        /// a snap and a slow blend both pass — as they should, until ticket 011 measures it against
        /// real latency.
        /// </summary>
        [Test]
        public void Local_hero_prediction_converges_on_the_authoritative_position()
        {
            var authoritative = new Vec2(0.0, 0.0);
            var prediction = new LocalHeroPrediction(authoritative);

            prediction.Predict(new Vec2(10.0, 0.0));

            var error = authoritative.DistanceTo(prediction.Predicted);
            Assert.That(error, Is.GreaterThan(0.0),
                "R-52: a locally predicted move is applied before the host confirms it");

            for (var i = 0; i < 500; i++)
            {
                prediction.Reconcile(authoritative);

                var next = authoritative.DistanceTo(prediction.Predicted);
                Assert.That(next, Is.LessThanOrEqualTo(error + Tolerance),
                    "R-52: reconciliation must never increase the error (step " + i + ")");
                error = next;
            }

            Assert.That(error, Is.LessThan(1e-3),
                "R-52: reconciliation must converge on the host's position, not merely approach it");
        }

        // ==========================================================================================
        //  R-50 — 1 to 4 players, solo is a one-player lobby
        // ==========================================================================================

        /// <summary>
        /// R-50 / DEC-020 / DEC-022. A party of one through four is playable; a fifth is refused.
        /// Transport is ticket 011 — this pins only the size rule, which a fifth joiner must bounce
        /// off however they arrived.
        /// </summary>
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, true)]
        [TestCase(5, false)]
        public void A_party_of_one_to_four_is_accepted_and_a_fifth_is_refused(int partySize, bool lastAccepted)
        {
            var roster = new PartyRoster();

            var accepted = false;
            for (var i = 0; i < partySize; i++)
            {
                accepted = roster.TryAdd("account_" + i);
            }

            Assert.That(accepted, Is.EqualTo(lastAccepted),
                "R-50: co-op is 1-4 players, solo being a one-player lobby");
            Assert.That(roster.Count, Is.EqualTo(Math.Min(partySize, PartyRoster.MaxPlayers)),
                "R-50: a refused join must not grow the party");
        }

        // ==========================================================================================
        //  scenario builders
        // ==========================================================================================

        private static readonly string[] DamageCommands =
        {
            nameof(ISimHost.ApplyHotspotAttack),
            nameof(ISimHost.ApplyHeroDamage),
            nameof(ISimHost.ApplyPlaceableDamage),
        };

        /// <summary>
        /// A small live match: one shelter, one hero, one barricade, one shambler, one player.
        /// Built from production types directly rather than through the golden fixture loader — the
        /// loader is the adapter's contract with eval/golden, not a scenario builder.
        /// </summary>
        private static MatchState NewState()
        {
            var state = new MatchState
            {
                Phase = MatchPhase.Combat,
                Status = MatchStatus.InProgress,
            };

            state.Team.Scrip = 500;

            state.Hotspots["chapel"] = new Hotspot
            {
                Id = "chapel",
                Pos = new Vec2(0.0, 0.0),
                Civilians = 20,
            };

            state.Heroes["h1"] = new Hero
            {
                Id = "h1",
                HeroClass = HeroClass.Gunslinger,
                AccountId = "acc1",
                Pos = new Vec2(1.0, 0.0),
                Hp = 100.0,
                MaxHp = 100.0,
                Alive = true,
            };

            state.Placeables["wall1"] = new Placeable
            {
                Id = "wall1",
                Type = PlaceableType.Barricade,
                Pos = new Vec2(2.0, 0.0),
                OwnerPlayerId = "p1",
                PurchaseCost = 100,
                Hp = 300.0,
                Exists = true,
            };

            state.Monsters["m1"] = new Monster
            {
                Id = "m1",
                Type = MonsterType.Shambler,
                Pos = new Vec2(3.0, 0.0),
                Hp = 60.0,
                Alive = true,
                BaseSpeed = 2.0,
                CurrentSpeed = 2.0,
                TargetId = "chapel",
            };

            state.Players.Add(new PlayerSlot
            {
                Id = "p1",
                AccountId = "acc1",
                HeroClass = HeroClass.Gunslinger,
                Ready = false,
                Connected = true,
            });

            state.Wave.LivingMonsterIds.Add("m1");

            return state;
        }

        private static MonsterAttackIntent Intent(TargetKind kind, double damage = 10.0)
        {
            string targetId;
            switch (kind)
            {
                case TargetKind.Hero:
                    targetId = "h1";
                    break;
                case TargetKind.Barricade:
                    targetId = "wall1";
                    break;
                default:
                    targetId = "chapel";
                    break;
            }

            return new MonsterAttackIntent
            {
                MonsterId = "m1",
                MonsterType = MonsterType.Shambler,
                TargetId = targetId,
                TargetKind = kind,
                Damage = damage,
            };
        }

        /// <summary>A host loop over a real <see cref="MatchSim"/> and the clock the host advances.</summary>
        private static HostLoop LoopOver(
            MatchState state, SimConfig config, SimClock clock, params MonsterAttackIntent[] intents)
        {
            var sim = new MatchSim(state, config, null, clock, null);
            return new HostLoop(new MatchSimHost(sim, clock), new StubAttackSource(intents));
        }

        /// <summary>
        /// Steps the loop until sim time reaches <paramref name="untilSeconds"/>. Bounded so a host
        /// that never advances the clock fails as a test failure rather than as a hung runner.
        /// </summary>
        private static void StepUntil(HostLoop loop, SimClock clock, double untilSeconds, double dt = Step60Hz)
        {
            var maxSteps = (int)((untilSeconds / dt) + 64);
            var steps = 0;

            while (clock.ElapsedSeconds < untilSeconds)
            {
                loop.Step(dt);

                if (++steps > maxSteps)
                {
                    Assert.Fail(
                        "the host loop ran " + steps + " steps without reaching " + untilSeconds
                        + "s of sim time (clock is at " + clock.ElapsedSeconds
                        + "s) — HostLoop.Step must advance the sim clock");
                }
            }
        }

        /// <summary>Everything about the world that a shell-side mutation could move.</summary>
        private static string Snapshot(MatchState state)
        {
            var sb = new StringBuilder();

            sb.Append("phase=").Append(state.Phase)
              .Append(";status=").Append(state.Status)
              .Append(";planningStartedAt=").Append(Num(state.PlanningStartedAt))
              .Append(";scrip=").Append(state.Team.Scrip)
              .Append(";wave=").Append(state.Wave.Number).Append('/').Append(state.Wave.TotalWaves)
              .Append(";living=").Append(string.Join(",", state.Wave.LivingMonsterIds));

            foreach (var m in state.Monsters.Values.OrderBy(entity => entity.Id, StringComparer.Ordinal))
            {
                sb.Append("|monster:").Append(m.Id)
                  .Append(" hp=").Append(Num(m.Hp))
                  .Append(" alive=").Append(m.Alive)
                  .Append(" pos=").Append(m.Pos)
                  .Append(" speed=").Append(Num(m.CurrentSpeed)).Append('/').Append(Num(m.BaseSpeed))
                  .Append(" target=").Append(m.TargetId)
                  .Append(" fx=").Append(m.StatusEffects.Count);
            }

            foreach (var h in state.Heroes.Values.OrderBy(entity => entity.Id, StringComparer.Ordinal))
            {
                sb.Append("|hero:").Append(h.Id)
                  .Append(" hp=").Append(Num(h.Hp)).Append('/').Append(Num(h.MaxHp))
                  .Append(" alive=").Append(h.Alive)
                  .Append(" pos=").Append(h.Pos)
                  .Append(" respawnAt=").Append(h.RespawnAt.HasValue ? Num(h.RespawnAt.Value) : "none")
                  .Append(" lastDamagedAt=").Append(Num(h.LastDamagedAt))
                  .Append(" fx=").Append(h.StatusEffects.Count)
                  .Append(" abilities=")
                  .Append(string.Join(",", h.Abilities.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                      .Select(kv => kv.Key + ":" + kv.Value)));
            }

            foreach (var h in state.Hotspots.Values.OrderBy(entity => entity.Id, StringComparer.Ordinal))
            {
                sb.Append("|hotspot:").Append(h.Id).Append(" civilians=").Append(h.Civilians);
            }

            foreach (var p in state.Placeables.Values.OrderBy(entity => entity.Id, StringComparer.Ordinal))
            {
                sb.Append("|placeable:").Append(p.Id)
                  .Append(" hp=").Append(Num(p.Hp))
                  .Append(" exists=").Append(p.Exists)
                  .Append(" triggers=").Append(p.TriggersRemaining);
            }

            foreach (var p in state.Players.OrderBy(entity => entity.Id, StringComparer.Ordinal))
            {
                sb.Append("|player:").Append(p.Id)
                  .Append(" ready=").Append(p.Ready)
                  .Append(" connected=").Append(p.Connected);
            }

            return sb.ToString();
        }

        private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        // ==========================================================================================
        //  IL inspection helpers (Mono.Cecil)
        // ==========================================================================================

        private static Assembly ShellAssembly() => typeof(HostLoop).Assembly;

        private static ModuleDefinition ReadShellModule()
        {
            var shell = ShellAssembly();
            var path = shell.Location;

            Assert.That(string.IsNullOrEmpty(path), Is.False,
                "cannot locate the compiled shell assembly " + shell.GetName().Name
                + " on disk; the architecture invariant cannot be enforced without it");
            Assert.That(File.Exists(path), Is.True, "expected the shell assembly at " + path);

            // InMemory so the scan never holds a lock on Library/ScriptAssemblies.
            return ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true });
        }

        /// <summary>
        /// Every MonoBehaviour-derived type in the shell, plus its nested types — where lambdas,
        /// local functions and iterator/async state machines put their bodies. Derivation is decided
        /// by reflection (which walks the real base chain); Cecil then supplies the IL.
        /// </summary>
        private static List<TypeDefinition> MonoBehaviourTypesAndTheirNested(ModuleDefinition module)
        {
            var monoBehaviourNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in ShellAssembly().GetTypes())
            {
                if (!type.IsInterface && typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    monoBehaviourNames.Add(type.FullName.Replace('+', '/'));
                }
            }

            return module.GetTypes().Where(t => IsOrIsNestedIn(t, monoBehaviourNames)).ToList();
        }

        private static bool IsOrIsNestedIn(TypeDefinition type, ICollection<string> names)
        {
            for (var t = type; t != null; t = t.DeclaringType)
            {
                if (names.Contains(t.FullName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The transitive closure of types reachable from <see cref="MatchState"/> and
        /// <see cref="AccountProfile"/> that live in the GameSim assembly: the world a match IS.
        /// Derived rather than listed so new fields are covered for free, and so
        /// <see cref="SimConfig"/> (R-16: the shell tunes it) and the command request/result types
        /// (issuing a command is not a rule) stay correctly outside it.
        /// </summary>
        private static HashSet<string> SimWorldStateTypeNames()
        {
            var simAssembly = typeof(MatchState).Assembly;
            var names = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<Type>();
            pending.Enqueue(typeof(MatchState));
            pending.Enqueue(typeof(AccountProfile));

            const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic
                                         | BindingFlags.Instance | BindingFlags.Static;

            while (pending.Count > 0)
            {
                var type = pending.Dequeue();
                if (type == null || type.Assembly != simAssembly || !names.Add(type.FullName))
                {
                    continue;
                }

                foreach (var field in type.GetFields(Members))
                {
                    foreach (var candidate in TypeCandidates(field.FieldType))
                    {
                        pending.Enqueue(candidate);
                    }
                }

                foreach (var property in type.GetProperties(Members))
                {
                    foreach (var candidate in TypeCandidates(property.PropertyType))
                    {
                        pending.Enqueue(candidate);
                    }
                }
            }

            return names;
        }

        private static IEnumerable<Type> TypeCandidates(Type type)
        {
            if (type == null)
            {
                yield break;
            }

            if (type.IsArray || type.IsByRef || type.IsPointer)
            {
                foreach (var inner in TypeCandidates(type.GetElementType()))
                {
                    yield return inner;
                }

                yield break;
            }

            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    foreach (var inner in TypeCandidates(argument))
                    {
                        yield return inner;
                    }
                }
            }

            yield return type;
        }

        private static readonly HashSet<string> MutatingCollectionCalls = new HashSet<string>(StringComparer.Ordinal)
        {
            "Add", "AddRange", "Remove", "RemoveAt", "RemoveAll", "RemoveRange",
            "Clear", "Insert", "InsertRange", "set_Item", "Sort", "Reverse", "TryAdd",
        };

        private static IEnumerable<string> WorldStateWritesIn(
            TypeDefinition type, MethodDefinition method, ICollection<string> worldState)
        {
            foreach (var instruction in method.Body.Instructions)
            {
                var what = ViolationFor(instruction, worldState);
                if (what != null)
                {
                    yield return type.FullName + "::" + method.Name + " -> " + what;
                }
            }
        }

        private static string ViolationFor(Instruction instruction, ICollection<string> worldState)
        {
            var code = instruction.OpCode.Code;

            if (code == Code.Stfld || code == Code.Stsfld)
            {
                var field = instruction.Operand as FieldReference;
                if (field != null && MentionsWorldState(field.DeclaringType, worldState))
                {
                    return "writes " + field.DeclaringType.Name + "." + field.Name;
                }

                return null;
            }

            // A by-ref write to a primitive sim field. Excluded for non-primitives because taking
            // the address of a Vec2 field is simply how a struct instance method is invoked.
            if (code == Code.Ldflda || code == Code.Ldsflda)
            {
                var field = instruction.Operand as FieldReference;
                if (field != null
                    && field.FieldType.IsPrimitive
                    && MentionsWorldState(field.DeclaringType, worldState))
                {
                    return "takes the address of " + field.DeclaringType.Name + "." + field.Name;
                }

                return null;
            }

            if (code != Code.Call && code != Code.Callvirt)
            {
                return null;
            }

            var callee = instruction.Operand as MethodReference;
            if (callee == null || !MentionsWorldState(callee.DeclaringType, worldState))
            {
                return null;
            }

            if (callee.Name.StartsWith("set_", StringComparison.Ordinal)
                && !MutatingCollectionCalls.Contains(callee.Name))
            {
                return "sets " + callee.DeclaringType.Name + "." + callee.Name.Substring(4);
            }

            if (!MutatingCollectionCalls.Contains(callee.Name))
            {
                return null;
            }

            // Only when the collection being mutated came out of the sim's own state. A shell-side
            // List<Monster> used as a view cache is not sim state and must not be flagged.
            var receiver = ReceiverOf(instruction, callee);
            if (receiver == null || !LoadsWorldStateMember(receiver, worldState))
            {
                return null;
            }

            return "mutates the sim-owned collection " + callee.DeclaringType.Name + "." + callee.Name;
        }

        private static bool LoadsWorldStateMember(Instruction instruction, ICollection<string> worldState)
        {
            var code = instruction.OpCode.Code;

            if (code == Code.Ldfld || code == Code.Ldsfld || code == Code.Ldflda || code == Code.Ldsflda)
            {
                var field = instruction.Operand as FieldReference;
                return field != null && MentionsWorldState(field.DeclaringType, worldState);
            }

            if (code == Code.Call || code == Code.Callvirt)
            {
                var callee = instruction.Operand as MethodReference;
                return callee != null
                       && callee.Name.StartsWith("get_", StringComparison.Ordinal)
                       && MentionsWorldState(callee.DeclaringType, worldState);
            }

            return false;
        }

        private static bool MentionsWorldState(TypeReference type, ICollection<string> worldState)
        {
            if (type == null)
            {
                return false;
            }

            var generic = type as GenericInstanceType;
            if (generic != null)
            {
                return MentionsWorldState(generic.ElementType, worldState)
                       || generic.GenericArguments.Any(a => MentionsWorldState(a, worldState));
            }

            var specification = type as TypeSpecification;
            if (specification != null)
            {
                return MentionsWorldState(specification.ElementType, worldState);
            }

            return worldState.Contains(type.FullName);
        }

        /// <summary>
        /// The instruction that produced the receiver of <paramref name="call"/>, found by walking
        /// backwards over the argument list accounting for each instruction's net stack effect.
        /// Straight-line only: a null (or, across a branch target, a wrong) answer simply means the
        /// caller declines to flag, because a false positive in an architecture test is worse than a
        /// missed exotic case that <c>stfld</c> would usually catch anyway.
        /// </summary>
        private static Instruction ReceiverOf(Instruction call, MethodReference callee)
        {
            if (!callee.HasThis)
            {
                return null;
            }

            var argumentSlots = callee.Parameters.Count;
            var depth = 0;

            for (var instruction = call.Previous; instruction != null; instruction = instruction.Previous)
            {
                // Everything between the receiver and the call is exactly the argument list, so the
                // first instruction reached once those slots are accounted for is the one that
                // produced the receiver.
                if (depth == argumentSlots)
                {
                    return instruction;
                }

                var pushed = PushCount(instruction);
                var popped = PopCount(instruction);
                if (pushed < 0 || popped < 0)
                {
                    return null;
                }

                depth += pushed - popped;
            }

            return null;
        }

        private static int PushCount(Instruction instruction)
        {
            switch (instruction.OpCode.StackBehaviourPush)
            {
                case StackBehaviour.Push0:
                    return 0;
                case StackBehaviour.Push1:
                case StackBehaviour.Pushi:
                case StackBehaviour.Pushi8:
                case StackBehaviour.Pushr4:
                case StackBehaviour.Pushr8:
                case StackBehaviour.Pushref:
                    return 1;
                case StackBehaviour.Push1_push1:
                    return 2;
                case StackBehaviour.Varpush:
                    var callee = instruction.Operand as MethodReference;
                    if (callee == null)
                    {
                        return -1;
                    }

                    return callee.ReturnType != null && callee.ReturnType.FullName == "System.Void" ? 0 : 1;
                default:
                    return -1;
            }
        }

        private static int PopCount(Instruction instruction)
        {
            switch (instruction.OpCode.StackBehaviourPop)
            {
                case StackBehaviour.Pop0:
                    return 0;
                case StackBehaviour.Pop1:
                case StackBehaviour.Popi:
                case StackBehaviour.Popref:
                    return 1;
                case StackBehaviour.Pop1_pop1:
                case StackBehaviour.Popi_pop1:
                case StackBehaviour.Popi_popi:
                case StackBehaviour.Popi_popi8:
                case StackBehaviour.Popi_popr4:
                case StackBehaviour.Popi_popr8:
                case StackBehaviour.Popref_pop1:
                case StackBehaviour.Popref_popi:
                    return 2;
                case StackBehaviour.Popi_popi_popi:
                case StackBehaviour.Popref_popi_popi:
                case StackBehaviour.Popref_popi_popi8:
                case StackBehaviour.Popref_popi_popr4:
                case StackBehaviour.Popref_popi_popr8:
                case StackBehaviour.Popref_popi_popref:
                    return 3;
                case StackBehaviour.Varpop:
                    var callee = instruction.Operand as MethodReference;
                    if (callee == null)
                    {
                        return -1;
                    }

                    var popped = callee.Parameters.Count;
                    if (callee.HasThis && instruction.OpCode.Code != Code.Newobj)
                    {
                        popped++;
                    }

                    return popped;
                default:
                    return -1;
            }
        }

        // ==========================================================================================
        //  test doubles
        // ==========================================================================================

        /// <summary>This step's monster attack candidates, scripted.</summary>
        private sealed class StubAttackSource : IMonsterAttackSource
        {
            private readonly MonsterAttackIntent[] _intents;

            public StubAttackSource(params MonsterAttackIntent[] intents)
            {
                _intents = intents ?? new MonsterAttackIntent[0];
            }

            public IReadOnlyList<MonsterAttackIntent> AttacksReadyThisStep(ISimHost sim, double deltaSeconds)
            {
                return _intents;
            }
        }

        /// <summary>
        /// An <see cref="ISimHost"/> that records which sim operations a host step made and
        /// otherwise does nothing at all — it never touches <see cref="State"/>. That inertness is
        /// what makes it able to answer two different questions: which calls did the loop make, and
        /// did the loop move the world behind the sim's back.
        ///
        /// If <see cref="ISimHost"/> gains a member, this fake gains one too — that is the price of
        /// the seam, and it is deliberately cheap.
        /// </summary>
        private sealed class RecordingSimHost : ISimHost
        {
            private readonly SimClock _clock;

            public RecordingSimHost(MatchState state, SimConfig config = null, SimClock clock = null)
            {
                State = state;
                Config = config ?? new SimConfig();
                _clock = clock ?? new SimClock();
            }

            public readonly List<string> Calls = new List<string>();

            public readonly List<string> GateQueries = new List<string>();

            /// <summary>What <see cref="TryMonsterAttack"/> answers — R-18's cadence, scripted.</summary>
            public bool GateAnswer = true;

            public MatchState State { get; }

            public SimConfig Config { get; }

            public IClock Clock => _clock;

            public SimObservation LastObservation { get; private set; } = new SimObservation();

            public void AdvanceClock(double deltaSeconds)
            {
                Calls.Add(nameof(AdvanceClock));
                _clock.Advance(deltaSeconds);
            }

            public void TickPlanningTimer() => Record(nameof(TickPlanningTimer));

            public StatusTickResult TickStatusEffects()
            {
                Record(nameof(TickStatusEffects));
                return new StatusTickResult();
            }

            public void TickHeroRegen() => Record(nameof(TickHeroRegen));

            public void TickHeroRespawns() => Record(nameof(TickHeroRespawns));

            public void TickMedStations() => Record(nameof(TickMedStations));

            public bool TryMonsterAttack(string monsterId)
            {
                Calls.Add(nameof(TryMonsterAttack));
                GateQueries.Add(monsterId);

                // Deliberately does NOT reset LastObservation, exactly as MatchSim does not: the
                // gate answers a question, it is not a command.
                return GateAnswer;
            }

            public HotspotAttackResult ApplyHotspotAttack(HotspotAttackRequest request)
            {
                Record(nameof(ApplyHotspotAttack));
                return new HotspotAttackResult { HotspotId = request == null ? null : request.TargetId };
            }

            public HeroDamageResult ApplyHeroDamage(HeroDamageRequest request)
            {
                Record(nameof(ApplyHeroDamage));
                return new HeroDamageResult { HeroId = request == null ? null : request.TargetId };
            }

            public PlaceableDamageResult ApplyPlaceableDamage(PlaceableDamageRequest request)
            {
                Record(nameof(ApplyPlaceableDamage));
                return new PlaceableDamageResult { PlaceableId = request == null ? null : request.TargetId };
            }

            private void Record(string call)
            {
                Calls.Add(call);
                LastObservation = new SimObservation();
            }
        }
    }
}
