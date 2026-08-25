using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Ticket T-04 rule tests: the match FSM (R-01, R-02, R-03) as a *machine* rather than the five
    /// single-step arrangements G-010/G-011/G-012/G-016/G-017 happen to pin, plus the three
    /// requirements this ticket owns that no fixture covers at all — the wave table as config
    /// (R-19, R-14), the partial wave preview (R-05 / DEC-018) and the wave-complete interstitial
    /// data (R-04).
    ///
    /// Those fixtures are graded by the locked golden adapter and are deliberately NOT re-encoded
    /// here. Everything below is either a rule the fixtures under-cover (one wave clear does not
    /// prove ten; one victory does not prove the victory *rule*), a sad path they do not visit, or
    /// a requirement with no fixture at all.
    ///
    /// Scenarios are built straight from production types — going through the fixture JSON loader
    /// is the adapter's job, not these tests'.
    ///
    /// R-19 is explicitly playtest-tuned config ("exact table is an implementation-time config, not
    /// fixture-locked"), so the wave-table tests assert *shape and direction* — ten waves defined, a
    /// Shambler-only opener, no Behemoth before wave 5, a final wave from all four breaches, a
    /// difficulty curve that goes up — and never an exact composition.
    /// </summary>
    [TestFixture]
    public class T04_WaveTests
    {
        // ---- R-01 / R-02 / R-03: the FSM as a machine ---------------------------------------------

        /// <summary>
        /// R-03's whole loop, walked: `lobby → (planning → combat) × N → victory`. Parametrised on
        /// the campaign length so the tenth wave cannot be a hardcoded literal — a sim that only
        /// knows the number 10 fails the 3-wave case, and a sim that stops early fails the 10-wave
        /// one.
        ///
        /// The lobby edge pins one thing the PRD leaves implicit: the *first* planning phase is wave
        /// 1's, so opening planning out of the lobby must not advance the counter the way G-016's
        /// wave 4 → 5 transition does.
        /// </summary>
        [TestCase(3)]
        [TestCase(10)]
        public void Match_walks_from_lobby_through_every_wave_to_victory(int totalWaves)
        {
            var state = ColonyMap.V1().CreateMatchState();
            state.Phase = MatchPhase.Lobby;
            state.Wave.Number = 1;
            state.Wave.TotalWaves = totalWaves;
            state.Players.Add(Player("p0", ready: false, connected: true));

            var config = new SimConfig { TotalWaves = totalWaves };
            var sim = new MatchSim(state, config, null, new SimClock(0.0), null);

            for (var wave = 1; wave <= totalWaves; wave++)
            {
                var planning = sim.BeginPlanningPhase();
                Assert.That(planning.Wave, Is.EqualTo(wave),
                    "R-03: planning phase " + wave + " belongs to wave " + wave);
                Assert.That(state.Phase, Is.EqualTo(MatchPhase.Planning));
                Assert.That(state.Wave.Number, Is.EqualTo(wave));

                // R-05: the preview exists for the wave planning is preparing for.
                Assert.That(sim.PreviewUpcomingWave().Wave, Is.EqualTo(wave));

                var ready = sim.SetPlayerReady("p0");
                Assert.That(ready.CombatStarted, Is.True,
                    "R-03: a 1-player lobby needs only that player's ready");
                Assert.That(state.Phase, Is.EqualTo(MatchPhase.Combat));

                var monsterId = "m_w" + wave;
                AddLiving(state, monsterId);
                var kill = sim.RecordMonsterKill(Kill(monsterId, 10));

                Assert.That(kill.WaveComplete, Is.True);
                Assert.That(kill.MapVictory, Is.EqualTo(wave == totalWaves),
                    "R-01: only clearing the final wave wins the map");

                if (wave < totalWaves)
                {
                    Assert.That(state.Phase, Is.EqualTo(MatchPhase.Planning),
                        "R-02/G-010: a cleared wave returns the *phase* to planning");
                    Assert.That(state.Status, Is.EqualTo(MatchStatus.InProgress));
                }
            }

            Assert.That(state.Status, Is.EqualTo(MatchStatus.Victory));
            Assert.That(state.IsOver, Is.True);
            Assert.That(state.Phase, Is.Not.EqualTo(MatchPhase.Planning),
                "R-01: clearing the final wave wins — it does not open an eleventh planning phase");
        }

        /// <summary>
        /// The victory rule, not the one instance G-011 pins: a cleared wave wins the map exactly
        /// when it is <see cref="SimConfig.TotalWaves"/>, whatever that is configured to be. The
        /// non-10 rows are the point — they fail any implementation that compares against a literal.
        ///
        /// Also pins which field moves. `MatchStatus.InProgress` and `MatchPhase.Combat` are both
        /// the string "combat": a wave clear moves the *phase* (G-010) and a victory moves the
        /// *status* (G-011), so an implementation that conflates the two looks right until you check
        /// which one changed.
        /// </summary>
        [TestCase(4, 3, false)]
        [TestCase(4, 4, true)]
        [TestCase(3, 3, true)]
        [TestCase(12, 10, false)]
        [TestCase(12, 12, true)]
        public void Clearing_a_wave_wins_the_map_only_on_the_configured_final_wave(
            int totalWaves, int wave, bool expectVictory)
        {
            var state = CombatState(wave, totalWaves, "m1");
            state.Team.Scrip = 40;
            var sim = new MatchSim(state, new SimConfig { TotalWaves = totalWaves }, null, new SimClock(0.0), null);

            var result = sim.RecordMonsterKill(Kill("m1", 50));

            Assert.That(result.WaveComplete, Is.True, "R-02: the last living monster died");
            Assert.That(result.MapVictory, Is.EqualTo(expectVictory));
            Assert.That(result.ScripAfter, Is.EqualTo(90), "R-20: the bounty is paid either way");

            if (expectVictory)
            {
                Assert.That(state.Status, Is.EqualTo(MatchStatus.Victory));
                Assert.That(state.IsOver, Is.True);
                Assert.That(state.Phase, Is.EqualTo(MatchPhase.Combat),
                    "R-01/G-011: victory moves the match *status*; the phase is a separate field");
            }
            else
            {
                Assert.That(state.Status, Is.EqualTo(MatchStatus.InProgress));
                Assert.That(state.IsOver, Is.False);
                Assert.That(state.Phase, Is.EqualTo(MatchPhase.Planning),
                    "R-02/G-010: a non-final wave clear returns the phase to planning");
            }
        }

        /// <summary>
        /// R-03: the `combat → defeat` edge is available in *every* wave, not just the last. Ticket
        /// 003 owns the defeat rule itself, so this drives the seam that already exists rather than
        /// reimplementing it — what is being pinned here is that no wave is exempt, and that R-02's
        /// "defeat mid-wave ends the match immediately" survives the wave then being cleared: on the
        /// final wave that clear would otherwise be a victory, which is the sharpest form of the bug.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        [TestCase(9)]
        [TestCase(10)]
        public void Defeat_is_reachable_from_combat_in_every_wave_and_the_match_stays_lost(int wave)
        {
            var state = CombatState(wave, 10, "m1");
            state.Hotspots["hs_saloon"] = new Hotspot { Id = "hs_saloon", Pos = new Vec2(0, 0), Civilians = 1 };
            var sim = new MatchSim(state);

            sim.ApplyHotspotAttack(new HotspotAttackRequest
            {
                AttackerId = "m1",
                AttackerType = MonsterType.Shambler,
                Damage = 10.0,
                TargetId = "hs_saloon",
            });

            Assert.That(state.Status, Is.EqualTo(MatchStatus.Defeat), "R-02: the colony emptied in wave " + wave);
            Assert.That(state.IsOver, Is.True);

            MonsterKillResult kill = null;
            var thrown = Attempt(() => kill = sim.RecordMonsterKill(Kill("m1", 10)));

            AssertDefined(thrown);
            Assert.That(state.Status, Is.EqualTo(MatchStatus.Defeat),
                "R-02: defeat ends the match immediately — clearing the wave afterwards cannot undo it");
            Assert.That(state.Phase, Is.Not.EqualTo(MatchPhase.Planning));
            Assert.That(state.Wave.Number, Is.EqualTo(wave));
            if (kill != null)
            {
                Assert.That(kill.MapVictory, Is.False);
            }
        }

        /// <summary>
        /// A match that is over stays over: neither terminal status may be talked into another wave.
        /// Nothing about scrip, wave number or status may move.
        /// </summary>
        [TestCase(MatchStatus.Victory)]
        [TestCase(MatchStatus.Defeat)]
        public void A_finished_match_never_opens_another_planning_phase(string terminalStatus)
        {
            var state = CombatState(6, 10);
            state.Status = terminalStatus;
            state.Team.Scrip = 200;
            var sim = new MatchSim(state);

            var thrown = Attempt(() => sim.BeginPlanningPhase());

            AssertDefined(thrown);
            Assert.That(state.Wave.Number, Is.EqualTo(6), "R-01: a finished match does not advance");
            Assert.That(state.Status, Is.EqualTo(terminalStatus));
            Assert.That(state.Team.Scrip, Is.EqualTo(200));
            Assert.That(state.Phase, Is.Not.EqualTo(MatchPhase.Planning));
        }

        /// <summary>
        /// R-03: combat re-enters planning through wave completion and nothing else. Asking for a
        /// planning phase while monsters are still alive must not skip the rest of the wave. The PRD
        /// does not say whether that is rejected or ignored, so this pins only that the behaviour is
        /// *decided* and that the wave counter and phase are untouched either way.
        /// </summary>
        [Test]
        public void Opening_planning_during_a_live_combat_wave_does_not_advance_the_wave()
        {
            var state = CombatState(3, 10, "m1", "m2");
            var sim = new MatchSim(state);

            var thrown = Attempt(() => sim.BeginPlanningPhase());

            AssertDefined(thrown);
            Assert.That(state.Wave.Number, Is.EqualTo(3));
            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Combat));
            Assert.That(state.Wave.LivingMonsterIds, Is.EquivalentTo(new[] { "m1", "m2" }));
        }

        // ---- R-03 / R-53: combat starts when the *connected* players are ready --------------------

        /// <summary>
        /// R-03 — "combat starts early when all **connected** players ready up". G-017 pins the
        /// three-connected-players case; the rows that matter here are the ones it does not reach:
        /// a solo lobby, and a disconnected player who must not hold the phase hostage (R-53 keeps
        /// the match running after a mid-match disconnect, so a player who has left cannot be
        /// waited on).
        ///
        /// Each player is spelled as two letters: C/D = connected/disconnected, R/U = ready/unready.
        /// </summary>
        [TestCase(new[] { "CU" }, 0, true, TestName = "Solo_lobby_starts_on_that_players_ready")]
        [TestCase(new[] { "CR", "CU" }, 1, true, TestName = "Last_connected_player_readying_starts_combat")]
        [TestCase(new[] { "CU", "DU" }, 0, true, TestName = "A_disconnected_player_cannot_hold_planning_hostage")]
        [TestCase(new[] { "CR", "CU", "DU" }, 1, true, TestName = "Only_connected_players_are_waited_on")]
        [TestCase(new[] { "CU", "CU", "DR" }, 0, false, TestName = "A_connected_player_still_unready_keeps_planning_open")]
        public void Combat_starts_when_every_connected_player_is_ready(
            string[] players, int readyIndex, bool expectCombat)
        {
            var state = PlanningState(2, 10);
            for (var i = 0; i < players.Length; i++)
            {
                state.Players.Add(Player(
                    "p" + i,
                    ready: players[i][1] == 'R',
                    connected: players[i][0] == 'C'));
            }

            var sim = new MatchSim(state, new SimConfig(), null, new SimClock(22.0), null);

            var result = sim.SetPlayerReady("p" + readyIndex);

            Assert.That(state.Players[readyIndex].Ready, Is.True, "the readying player is now ready");
            Assert.That(result.AllReady, Is.EqualTo(expectCombat),
                "R-03: readiness is judged across connected players only");
            Assert.That(result.CombatStarted, Is.EqualTo(expectCombat));
            Assert.That(state.Phase, Is.EqualTo(expectCombat ? MatchPhase.Combat : MatchPhase.Planning));
            Assert.That(EventTypes(sim).Contains("combat_started"), Is.EqualTo(expectCombat));
        }

        /// <summary>
        /// R-03 — the 60-second planning phase is per wave, so the elapsed figure a ready-up reports
        /// is measured from when *this* planning phase opened, not from match start. G-017 cannot
        /// tell the two apart: its phase started at sim time 0.
        /// </summary>
        [Test]
        public void Planning_elapsed_is_measured_from_this_phase_start_not_from_match_start()
        {
            var state = PlanningState(4, 10);
            state.PlanningStartedAt = 12.0;
            state.Players.Add(Player("p0", ready: false, connected: true));
            var sim = new MatchSim(state, new SimConfig(), null, new SimClock(30.5), null);

            var result = sim.SetPlayerReady("p0");

            Assert.That(result.PlanningElapsed, Is.EqualTo(18.5).Within(1e-9));
        }

        // ---- R-19 / R-14: the wave table is config ------------------------------------------------

        /// <summary>
        /// R-19: the table covers the whole campaign, one entry per wave, numbered 1..N against
        /// <see cref="SimConfig.TotalWaves"/> rather than a literal.
        /// </summary>
        [Test]
        public void Wave_table_defines_every_wave_of_the_campaign()
        {
            var totalWaves = new SimConfig().TotalWaves;
            var table = WaveTable.V1();

            Assert.That(table.Waves.Count, Is.EqualTo(totalWaves));
            Assert.That(table.Waves.Select(w => w.Number).OrderBy(n => n),
                Is.EqualTo(Enumerable.Range(1, totalWaves)));

            for (var n = 1; n <= totalWaves; n++)
            {
                Assert.That(table.For(n).Number, Is.EqualTo(n), "lookup returns the wave asked for");
            }
        }

        /// <summary>
        /// R-19's stated *shape*, not its numbers: "wave 1 ≈ 6 Shamblers; Behemoths appear from wave
        /// 5; wave 10 ≈ 30 mixed monsters". The counts are explicitly playtest-tuned, so only the
        /// direction is asserted — the curve goes up and never dips below the opener — which leaves
        /// a tuner free to trade quantity for a Behemoth on any given wave.
        /// </summary>
        [Test]
        public void Wave_table_ramps_in_the_direction_R19_describes()
        {
            var waves = WaveTable.V1().Waves.OrderBy(w => w.Number).ToList();
            var counts = waves.Select(TotalMonsters).ToList();

            Assert.That(waves[0].Groups.Select(g => g.MonsterType).Distinct(),
                Is.EqualTo(new[] { MonsterType.Shambler }), "R-19: wave 1 is the Shambler-only opener");
            Assert.That(waves.All(w => w.Groups.Count > 0 && w.Groups.All(g => g.Count > 0)), Is.True,
                "every wave sends somebody");

            foreach (var wave in waves.Where(w => w.Number < 5))
            {
                Assert.That(wave.Groups.Any(g => g.MonsterType == MonsterType.BullBehemoth), Is.False,
                    "R-19: no Behemoth before wave 5 — wave " + wave.Number + " has one");
            }

            Assert.That(waves.Any(w => w.Number >= 5 && w.Groups.Any(g => g.MonsterType == MonsterType.BullBehemoth)),
                Is.True, "R-19: Behemoths appear from wave 5");

            Assert.That(counts.Last(), Is.GreaterThan(counts[0]),
                "R-19: the final wave is bigger than the opener");
            Assert.That(counts.All(c => c >= counts[0]), Is.True,
                "R-19: no wave is easier than wave 1");
            Assert.That(counts.Skip(counts.Count / 2).Sum(), Is.GreaterThan(counts.Take(counts.Count / 2).Sum()),
                "R-19: difficulty ramps across the campaign");
            Assert.That(waves.Last().Groups.Select(g => g.MonsterType).Distinct().Count(), Is.GreaterThan(1),
                "R-19: the final wave is mixed, not one archetype");
        }

        /// <summary>
        /// R-17/R-19: every archetype the table names must have a stat row, or the wave spawns
        /// monsters with no HP and no bounty. <see cref="MonsterCatalog.StatsFor"/> throws on a
        /// missing row, so this is the table's half of that contract.
        /// </summary>
        [Test]
        public void Wave_table_names_only_configured_monster_archetypes()
        {
            var catalog = new SimConfig().Monsters;

            var unknown = WaveTable.V1().Waves
                .SelectMany(w => w.Groups.Select(g => new { w.Number, g.MonsterType }))
                .Where(g => !catalog.Contains(g.MonsterType))
                .Select(g => "wave " + g.Number + " -> '" + g.MonsterType + "'")
                .ToList();

            Assert.That(unknown, Is.Empty,
                "R-17: the table names archetypes with no catalog row: " + string.Join(", ", unknown));
        }

        /// <summary>
        /// R-14: the four entry tunnels are fixed map features and *which subset activates varies
        /// per wave*. That is two claims — the indices must address real tunnels on the v1 map, and
        /// the subsets must actually differ across waves. A table that opens all four every wave
        /// satisfies R-19's final-wave line while quietly failing R-14, which is why the varies-part
        /// is asserted separately.
        /// </summary>
        [Test]
        public void Wave_table_activates_a_varying_subset_of_the_four_fixed_entry_tunnels()
        {
            var tunnelCount = ColonyMap.V1().EntryTunnels.Count;
            var waves = WaveTable.V1().Waves.OrderBy(w => w.Number).ToList();

            foreach (var wave in waves)
            {
                Assert.That(wave.ActiveTunnels, Is.Not.Empty,
                    "R-14: wave " + wave.Number + " must breach somewhere");
                Assert.That(wave.ActiveTunnels.Distinct().Count(), Is.EqualTo(wave.ActiveTunnels.Count),
                    "R-14: wave " + wave.Number + " activates the same tunnel twice");
                Assert.That(wave.ActiveTunnels.All(i => i >= 0 && i < tunnelCount), Is.True,
                    "R-14: wave " + wave.Number + " names a tunnel the v1 map does not have");
            }

            Assert.That(waves.Last().ActiveTunnels.Count, Is.EqualTo(tunnelCount),
                "R-19: the final wave comes from all four tunnels");
            Assert.That(waves.Any(w => w.ActiveTunnels.Count < tunnelCount), Is.True,
                "R-14: a subset that never varies is not a subset");
            Assert.That(waves.Select(TunnelSetKey).Distinct().Count(), Is.GreaterThan(1),
                "R-14: which tunnels are active must differ between waves");
        }

        /// <summary>
        /// R-19 is *config*, not constants inside the wave rules: a match ships with a table, the
        /// table is replaceable per instance, and two tables handed out are independent objects so
        /// tuning one match cannot move another's numbers (the bug <see cref="MonsterCatalog"/>
        /// avoids by seeding per instance).
        /// </summary>
        [Test]
        public void Wave_table_is_per_instance_config_the_sim_reads()
        {
            var sim = new MatchSim(new MatchState());

            Assert.That(sim.WaveTable, Is.Not.Null, "R-19: a match ships with a wave table");
            Assert.That(sim.WaveTable.Waves.Count, Is.EqualTo(sim.Config.TotalWaves));

            var tuned = TableWhereWave(2, new[] { 1, 3 }, (MonsterType.Ravager, 9));
            sim.WaveTable = tuned;
            Assert.That(sim.WaveTable, Is.SameAs(tuned), "R-19: the table is overridable per match");

            var first = WaveTable.V1();
            var second = WaveTable.V1();
            first.For(1).Groups[0].Count += 99;
            Assert.That(second.For(1).Groups[0].Count, Is.Not.EqualTo(first.For(1).Groups[0].Count),
                "R-19: each table is its own data — no shared static roster");
        }

        // ---- R-05 / DEC-018: the partial wave preview ---------------------------------------------

        /// <summary>
        /// R-05: during planning the sim tells clients which entry points will activate — the ones
        /// the table names for the coming wave, and only those. The all-four row matters as much as
        /// the partial ones: "highlight the active tunnels" collapses into "highlight everything" if
        /// the preview is not actually read off the table.
        /// </summary>
        [TestCase(new[] { 0, 2 })]
        [TestCase(new[] { 1 })]
        [TestCase(new[] { 0, 1, 2, 3 })]
        public void Wave_preview_names_exactly_the_entry_points_the_table_activates(int[] active)
        {
            var sim = PlanningSimForWave(2, TableWhereWave(2, active, (MonsterType.Ravager, 7)));

            var preview = sim.PreviewUpcomingWave();

            Assert.That(preview.Wave, Is.EqualTo(2), "R-05: the preview is of the wave about to be fought");
            Assert.That(preview.ActiveEntryTunnels.OrderBy(i => i), Is.EqualTo(active.OrderBy(i => i)));
        }

        /// <summary>
        /// R-05 / DEC-018's negative half, and the reason this ticket has a test at all: monster
        /// types and counts are hidden. The failure mode is leaking too much, which is trivially
        /// easy — replicating the <see cref="WaveSpec"/> and letting the UI ignore the composition
        /// looks fine on screen and hands every client the answer.
        ///
        /// So the whole observable surface is inspected, not the documented intent: the typed result
        /// object walked recursively, the replicated <c>ToFields</c> dictionary walked recursively,
        /// and the declared member types checked for any composition type. The wave under test uses
        /// deliberately odd counts (13, 21, sum 34) so a leaked number cannot be mistaken for a wave
        /// number or a tunnel index.
        /// </summary>
        [Test]
        public void Wave_preview_hides_monster_types_and_counts_across_its_whole_surface()
        {
            var sim = PlanningSimForWave(
                2,
                TableWhereWave(2, new[] { 0, 2 }, (MonsterType.BullBehemoth, 13), (MonsterType.Shambler, 21)));

            var preview = sim.PreviewUpcomingWave();

            var surface = Surface(preview).Concat(Surface(preview.ToFields())).ToList();
            Assert.That(surface, Is.Not.Empty, "the preview must expose something to inspect");

            var archetypes = new HashSet<string>(new[]
            {
                MonsterType.Shambler, MonsterType.Ravager, MonsterType.Spitter,
                MonsterType.Burrower, MonsterType.BullBehemoth,
            });

            var leakedTypes = surface
                .Where(kv => kv.Value is string text && archetypes.Contains(text))
                .Select(kv => kv.Key + " = " + kv.Value)
                .ToList();
            Assert.That(leakedTypes, Is.Empty,
                "DEC-018: monster types are hidden; leaked at " + string.Join(", ", leakedTypes));

            var secrets = new[] { 13.0, 21.0, 34.0 };
            var leakedCounts = surface
                .Where(kv => AsNumber(kv.Value).HasValue && secrets.Contains(AsNumber(kv.Value).Value))
                .Select(kv => kv.Key + " = " + kv.Value)
                .ToList();
            Assert.That(leakedCounts, Is.Empty,
                "DEC-018: monster counts are hidden; leaked at " + string.Join(", ", leakedCounts));

            var leakyNames = surface
                .Select(kv => kv.Key)
                .Where(path => Mentions(path, "monster") || Mentions(path, "composition") || Mentions(path, "group"))
                .Distinct()
                .ToList();
            Assert.That(leakyNames, Is.Empty,
                "DEC-018: nothing about composition belongs on the preview; found " + string.Join(", ", leakyNames));

            var composition = new[] { typeof(WaveTable), typeof(WaveSpec), typeof(MonsterGroup) };
            var leakyMembers = typeof(WavePreviewResult)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Select(MemberValueType)
                .Where(t => t != null)
                .Where(t => composition.Contains(t)
                            || (t.IsGenericType && t.GetGenericArguments().Any(composition.Contains)))
                .Select(t => t.Name)
                .ToList();
            Assert.That(leakyMembers, Is.Empty,
                "DEC-018: the preview must not carry the wave table's own types; found "
                + string.Join(", ", leakyMembers));
        }

        // ---- R-04: the wave-complete interstitial --------------------------------------------------

        /// <summary>
        /// R-04: the interstitial shows "bounty earned this wave" and "civilians remaining". The ~3s
        /// hold and the banner are the shell's (S5); the sim owes the two numbers.
        ///
        /// Bounty earned is the sum across the wave — three kills at 10, 15 and 50 make 75. That is
        /// neither the last kill (50) nor the shared pool (120 + 75 = 195), which are the two things
        /// an implementation reaches for when it has not actually accumulated anything. Civilians
        /// are knocked down from 20 to 18 first, so "remaining" cannot pass by reporting the map's
        /// starting total.
        /// </summary>
        [Test]
        public void Wave_complete_interstitial_reports_bounty_earned_during_that_wave_and_civilians_left()
        {
            var state = ColonyMap.V1().CreateMatchState();
            state.Phase = MatchPhase.Combat;
            state.Wave.Number = 3;
            state.Wave.TotalWaves = 10;
            state.Team.Scrip = 120;
            AddLiving(state, "m1", "m2", "m3");
            var sim = new MatchSim(state);

            sim.ApplyHotspotAttack(new HotspotAttackRequest
            {
                AttackerId = "m1",
                AttackerType = MonsterType.Shambler,
                Damage = 20.0,
                TargetId = "hs_saloon",
            });

            sim.RecordMonsterKill(Kill("m1", 10));
            sim.RecordMonsterKill(Kill("m2", 15));
            var last = sim.RecordMonsterKill(Kill("m3", 50));
            Assert.That(last.WaveComplete, Is.True);

            var summary = sim.WaveSummary();

            Assert.That(summary.Wave, Is.EqualTo(3));
            Assert.That(summary.BountyEarned, Is.EqualTo(75),
                "R-04: bounty earned across the wave — not the last kill (50), not the pool (195)");
            Assert.That(summary.CiviliansRemaining, Is.EqualTo(18));
            Assert.That(state.Team.Scrip, Is.EqualTo(195), "R-20: the pool itself is untouched by the report");
        }

        /// <summary>
        /// R-04's other half: the interstitial is per wave and planning follows it. The wave-3 total
        /// must not follow the match into wave 4 — a lifetime counter reports 85 here, a per-wave one
        /// reports 10.
        /// </summary>
        [Test]
        public void Bounty_earned_resets_each_wave_and_planning_follows_the_interstitial()
        {
            var state = ColonyMap.V1().CreateMatchState();
            state.Phase = MatchPhase.Combat;
            state.Wave.Number = 3;
            state.Wave.TotalWaves = 10;
            state.Players.Add(Player("p0", ready: false, connected: true));
            AddLiving(state, "m1", "m2");
            var sim = new MatchSim(state, new SimConfig(), null, new SimClock(0.0), null);

            sim.RecordMonsterKill(Kill("m1", 25));
            sim.RecordMonsterKill(Kill("m2", 50));
            Assert.That(sim.WaveSummary().BountyEarned, Is.EqualTo(75));

            var planning = sim.BeginPlanningPhase();
            Assert.That(planning.Wave, Is.EqualTo(4), "R-04: planning follows the interstitial");
            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Planning));

            sim.SetPlayerReady("p0");
            AddLiving(state, "m3");
            sim.RecordMonsterKill(Kill("m3", 10));

            var summary = sim.WaveSummary();
            Assert.That(summary.Wave, Is.EqualTo(4));
            Assert.That(summary.BountyEarned, Is.EqualTo(10),
                "R-04: bounty earned is per wave, not lifetime (85 would be the running total)");
        }

        // ---- sad paths -----------------------------------------------------------------------------

        /// <summary>
        /// Readying an id the match does not have. The PRD is silent on reject-versus-throw, so this
        /// pins only that the behaviour is *decided* and that a stray id cannot conjure a player slot
        /// or start combat on behalf of the real ones.
        /// </summary>
        [Test]
        public void Readying_a_player_who_is_not_in_the_match_is_defined_and_starts_nothing()
        {
            var state = PlanningState(2, 10);
            state.Players.Add(Player("p0", ready: false, connected: true));
            var sim = new MatchSim(state, new SimConfig(), null, new SimClock(5.0), null);

            ReadyResult result = null;
            var thrown = Attempt(() => result = sim.SetPlayerReady("nobody"));

            AssertDefined(thrown);
            Assert.That(state.Players.Count, Is.EqualTo(1), "no player slot was conjured");
            Assert.That(state.Players[0].Ready, Is.False);
            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Planning));
            if (result != null)
            {
                Assert.That(result.CombatStarted, Is.False);
            }
        }

        /// <summary>
        /// Readying a player who is already ready. Idempotent by construction — ready is a flag, not
        /// a toggle (R-03 has no un-ready) — so the phase must not move while another connected
        /// player is still unready, and the no-op must not replicate a state change: nothing changed.
        /// </summary>
        [Test]
        public void Readying_an_already_ready_player_changes_nothing()
        {
            var state = PlanningState(2, 10);
            state.Players.Add(Player("p0", ready: true, connected: true));
            state.Players.Add(Player("p1", ready: false, connected: true));
            var sim = new MatchSim(state, new SimConfig(), null, new SimClock(5.0), null);

            ReadyResult result = null;
            var thrown = Attempt(() => result = sim.SetPlayerReady("p0"));

            AssertDefined(thrown);
            Assert.That(state.Players[0].Ready, Is.True);
            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Planning), "p1 has not readied");
            if (result != null)
            {
                Assert.That(result.CombatStarted, Is.False);
                Assert.That(
                    sim.LastObservation.StateChanges.Any(c => c.Entity == "p0" && c.Field == "ready"),
                    Is.False,
                    "an unchanged flag is not a state change");
            }
        }

        /// <summary>
        /// Kills that are not a living monster of this wave dying: an id the match never had, and a
        /// second report for a monster already killed (a duplicated client message, or a turret and
        /// a hero both claiming the last hit). Neither may pay a bounty or clear the wave while a
        /// real monster is still alive.
        /// </summary>
        [TestCase(false, TestName = "Kill_for_a_monster_the_match_never_had")]
        [TestCase(true, TestName = "Second_kill_report_for_a_monster_already_dead")]
        public void Kills_outside_the_living_roster_pay_nothing_and_do_not_complete_the_wave(bool alreadyDead)
        {
            var state = CombatState(2, 10, "m1", "m2");
            state.Team.Scrip = 0;
            var sim = new MatchSim(state);

            var targetId = "m9";
            if (alreadyDead)
            {
                sim.RecordMonsterKill(Kill("m1", 10));
                targetId = "m1";
            }

            var scripBefore = state.Team.Scrip;

            MonsterKillResult result = null;
            var thrown = Attempt(() => result = sim.RecordMonsterKill(Kill(targetId, 10)));

            AssertDefined(thrown);
            Assert.That(state.Team.Scrip, Is.EqualTo(scripBefore), "R-20: a bounty is paid once, on death");
            Assert.That(state.Wave.LivingMonsterIds, Does.Contain("m2"), "the live roster is intact");
            Assert.That(state.Status, Is.EqualTo(MatchStatus.InProgress));
            Assert.That(state.Phase, Is.EqualTo(MatchPhase.Combat));
            if (result != null)
            {
                Assert.That(result.WaveComplete, Is.False);
                Assert.That(result.MapVictory, Is.False);
            }
        }

        // ---- R-03: the planning timer's ordinary exit -----------------------------------------------

        /// <summary>
        /// R-03 / DEC-006 — "each wave begins with a 60-second planning phase; combat starts
        /// <i>early</i> when all connected players ready up". The word <i>early</i> presupposes the
        /// ordinary path this pins: the timer simply runs out. Without it a lobby holding one player
        /// who never readies — AFK, or disconnected in a way that leaves the slot un-ready — sits in
        /// planning forever and the match cannot progress.
        ///
        /// The boundary is inclusive, matching how G-019 treats an expiry landing exactly on its
        /// deadline: at the duration the phase is over, not still running. Elapsed is measured from
        /// <see cref="MatchState.PlanningStartedAt"/>, not from match start, so the phase is opened
        /// at sim time 12 in every row. The 45-second rows are the ones that fail an implementation
        /// carrying the literal 60 instead of reading
        /// <see cref="SimConfig.PlanningDurationSeconds"/>.
        /// </summary>
        [TestCase(60.0, 59.5, false)]
        [TestCase(60.0, 60.0, true)]
        [TestCase(60.0, 60.5, true)]
        [TestCase(45.0, 44.0, false)]
        [TestCase(45.0, 45.0, true)]
        [TestCase(45.0, 120.0, true)]
        public void Planning_ends_and_combat_begins_when_the_configured_duration_elapses(
            double duration, double elapsed, bool expectCombat)
        {
            var state = PlanningState(2, 10);
            state.PlanningStartedAt = 12.0;
            state.Players.Add(Player("p0", ready: false, connected: true));
            var config = new SimConfig { PlanningDurationSeconds = duration };
            var sim = new MatchSim(state, config, null, new SimClock(12.0 + elapsed), null);

            sim.TickPlanningTimer();

            Assert.That(state.Phase, Is.EqualTo(expectCombat ? MatchPhase.Combat : MatchPhase.Planning),
                "R-03: planning runs for " + duration + "s from PlanningStartedAt");
            Assert.That(EventTypes(sim).Contains("combat_started"), Is.EqualTo(expectCombat));
        }

        /// <summary>
        /// R-03: the timeout and the all-ready early start land in the same place — combat — but a
        /// client has to be able to tell which happened, because the two want different stingers and
        /// different toasts (R-64). G-017 pins <c>combat_started{trigger:"all_ready"}</c>; the PRD
        /// gives no word for the timeout, so this asserts only that the trigger is stated and that it
        /// is not the all-ready one.
        /// </summary>
        [Test]
        public void The_planning_timeout_reaches_combat_the_same_way_all_ready_does_but_says_so_differently()
        {
            var timedOut = SoloPlanningSim(planningStartedAt: 12.0, now: 12.0 + 60.0);
            timedOut.TickPlanningTimer();

            var readiedUp = SoloPlanningSim(planningStartedAt: 12.0, now: 12.0 + 20.0);
            readiedUp.SetPlayerReady("p0");

            Assert.That(timedOut.State.Phase, Is.EqualTo(MatchPhase.Combat));
            Assert.That(readiedUp.State.Phase, Is.EqualTo(MatchPhase.Combat));

            var timeoutTrigger = TriggerOf(timedOut);
            var readyTrigger = TriggerOf(readiedUp);

            Assert.That(readyTrigger, Is.EqualTo("all_ready"), "G-017 pins the early-start trigger");
            Assert.That(timeoutTrigger, Is.Not.Null, "R-03: the timeout says why combat started");
            Assert.That(timeoutTrigger, Is.Not.EqualTo(readyTrigger),
                "R-03: the ordinary exit and the early exit are distinguishable");
        }

        /// <summary>
        /// The tick is called every host step, so it must be inert outside the phase it governs:
        /// during combat, in the lobby, and in a planning phase belonging to a match that is already
        /// over. Each row is well past any deadline, so an unguarded implementation fires.
        /// </summary>
        [TestCase(MatchPhase.Combat, MatchStatus.InProgress, TestName = "Planning_tick_does_nothing_during_combat")]
        [TestCase(MatchPhase.Lobby, MatchStatus.InProgress, TestName = "Planning_tick_does_nothing_in_the_lobby")]
        [TestCase(MatchPhase.Planning, MatchStatus.Defeat, TestName = "Planning_tick_does_nothing_once_the_match_is_lost")]
        [TestCase(MatchPhase.Planning, MatchStatus.Victory, TestName = "Planning_tick_does_nothing_once_the_match_is_won")]
        public void Ticking_the_planning_timer_outside_a_live_planning_phase_does_nothing(
            string phase, string status)
        {
            var state = PlanningState(2, 10);
            state.Phase = phase;
            state.Status = status;
            state.Players.Add(Player("p0", ready: false, connected: true));
            var sim = new MatchSim(state, new SimConfig(), null, new SimClock(500.0), null);

            sim.TickPlanningTimer();

            Assert.That(state.Phase, Is.EqualTo(phase), "the tick governs live planning and nothing else");
            Assert.That(state.Status, Is.EqualTo(status));
            Assert.That(EventTypes(sim), Does.Not.Contain("combat_started"));
        }

        /// <summary>
        /// Combat starts once. The host keeps ticking after the deadline passes, and a player whose
        /// ready message was in flight when the timer fired still arrives — neither may re-announce
        /// combat or restart the wave. The late ready is a sad path the PRD does not decide, so only
        /// non-corruption is asserted.
        /// </summary>
        [Test]
        public void Combat_starts_once_however_many_ticks_or_late_readies_arrive()
        {
            var sim = SoloPlanningSim(planningStartedAt: 12.0, now: 12.0 + 60.0);

            sim.TickPlanningTimer();
            Assert.That(sim.State.Phase, Is.EqualTo(MatchPhase.Combat));
            Assert.That(EventTypes(sim), Does.Contain("combat_started"));

            sim.TickPlanningTimer();
            Assert.That(sim.State.Phase, Is.EqualTo(MatchPhase.Combat));
            Assert.That(EventTypes(sim), Does.Not.Contain("combat_started"),
                "R-03: the timer fires once — a later tick has no phase left to end");

            ReadyResult late = null;
            var thrown = Attempt(() => late = sim.SetPlayerReady("p0"));

            AssertDefined(thrown);
            Assert.That(sim.State.Phase, Is.EqualTo(MatchPhase.Combat));
            Assert.That(sim.State.Wave.Number, Is.EqualTo(2), "a late ready does not restart the wave");
            if (late != null)
            {
                Assert.That(late.CombatStarted, Is.False, "combat was already running");
                Assert.That(EventTypes(sim), Does.Not.Contain("combat_started"));
            }
        }

        // ---- R-01: which field names the final wave ------------------------------------------------

        /// <summary>
        /// Orchestrator ruling, recorded here because nothing else in the repo states it:
        /// <see cref="WaveState.TotalWaves"/> is authoritative at runtime and
        /// <see cref="SimConfig.TotalWaves"/> is the tuning surface that seeds it at match creation —
        /// the same config-authors/state-lives split as <see cref="ColonyMap.CreateMatchState"/>, and
        /// the one the golden adapter drives, since every fixture states
        /// <c>preexisting_state.wave.total_waves</c>.
        ///
        /// Both fixtures and the tests above set the two consistently, so only a case where they
        /// disagree can pin which one wins. Config is left at its default 10 in both rows.
        /// </summary>
        [TestCase(3, 3, true, TestName = "State_shortens_the_campaign_below_the_config_default")]
        [TestCase(12, 10, false, TestName = "State_extends_the_campaign_past_the_config_default")]
        public void The_final_wave_is_the_one_the_match_state_declares_not_the_config_default(
            int stateTotalWaves, int wave, bool expectVictory)
        {
            var state = CombatState(wave, stateTotalWaves, "m1");
            var config = new SimConfig();
            Assert.That(config.TotalWaves, Is.EqualTo(10), "this case only bites while the default is 10");

            var result = new MatchSim(state, config, null, new SimClock(0.0), null)
                .RecordMonsterKill(Kill("m1", 20));

            Assert.That(result.WaveComplete, Is.True);
            Assert.That(result.MapVictory, Is.EqualTo(expectVictory),
                "R-01: the campaign length the *match state* declares is the one that ends it");
            Assert.That(state.Status,
                Is.EqualTo(expectVictory ? MatchStatus.Victory : MatchStatus.InProgress));
        }

        // ---- helpers ---------------------------------------------------------------------------------

        private static MonsterKillRequest Kill(string monsterId, int bounty) => new MonsterKillRequest
        {
            MonsterId = monsterId,
            MonsterType = MonsterType.Shambler,
            Bounty = bounty,
            KillerHeroId = "hero_a",
        };

        private static PlayerSlot Player(string id, bool ready, bool connected) => new PlayerSlot
        {
            Id = id,
            AccountId = "acct_" + id,
            HeroClass = HeroClass.Gunslinger,
            Ready = ready,
            Connected = connected,
        };

        /// <summary>Materialises each id as a living monster and lists it on the wave roster.</summary>
        private static void AddLiving(MatchState state, params string[] monsterIds)
        {
            foreach (var id in monsterIds)
            {
                state.Monsters[id] = new Monster
                {
                    Id = id,
                    Type = MonsterType.Shambler,
                    Pos = new Vec2(0, 0),
                    Hp = 60.0,
                    Alive = true,
                };
                state.Wave.LivingMonsterIds.Add(id);
            }
        }

        private static MatchState PlanningState(int wave, int totalWaves)
        {
            var state = new MatchState { Phase = MatchPhase.Planning };
            state.Wave.Number = wave;
            state.Wave.TotalWaves = totalWaves;
            return state;
        }

        private static MatchState CombatState(int wave, int totalWaves, params string[] livingMonsterIds)
        {
            var state = new MatchState { Phase = MatchPhase.Combat };
            state.Wave.Number = wave;
            state.Wave.TotalWaves = totalWaves;
            AddLiving(state, livingMonsterIds);
            return state;
        }

        /// <summary>
        /// A table covering the whole campaign whose <paramref name="waveNumber"/> row is the one
        /// under test; every other wave is a plain filler so the sim never sees an undefined wave.
        /// </summary>
        private static WaveTable TableWhereWave(
            int waveNumber, int[] tunnels, params (string Type, int Count)[] groups)
        {
            var table = new WaveTable();
            for (var n = 1; n <= new SimConfig().TotalWaves; n++)
            {
                var spec = new WaveSpec { Number = n };
                if (n == waveNumber)
                {
                    spec.ActiveTunnels.AddRange(tunnels);
                    foreach (var (type, count) in groups)
                    {
                        spec.Groups.Add(new MonsterGroup { MonsterType = type, Count = count });
                    }
                }
                else
                {
                    spec.ActiveTunnels.Add(0);
                    spec.Groups.Add(new MonsterGroup { MonsterType = MonsterType.Shambler, Count = 6 });
                }

                table.Waves.Add(spec);
            }

            return table;
        }

        private static MatchSim PlanningSimForWave(int wave, WaveTable table)
        {
            var state = ColonyMap.V1().CreateMatchState();
            state.Phase = MatchPhase.Planning;
            state.Wave.Number = wave;
            state.Wave.TotalWaves = 10;
            state.Players.Add(Player("p0", ready: false, connected: true));

            var sim = new MatchSim(state, new SimConfig(), null, new SimClock(0.0), null)
            {
                WaveTable = table,
            };
            return sim;
        }

        private static int TotalMonsters(WaveSpec wave) => wave.Groups.Sum(g => g.Count);

        private static string TunnelSetKey(WaveSpec wave) =>
            string.Join(",", wave.ActiveTunnels.OrderBy(i => i));

        private static List<string> EventTypes(MatchSim sim) =>
            sim.LastObservation.EmittedEvents.Select(e => e.Type).ToList();

        /// <summary>A one-player lobby sitting in wave 2's planning phase, opened at a known time.</summary>
        private static MatchSim SoloPlanningSim(double planningStartedAt, double now)
        {
            var state = PlanningState(2, 10);
            state.PlanningStartedAt = planningStartedAt;
            state.Players.Add(Player("p0", ready: false, connected: true));
            return new MatchSim(state, new SimConfig(), null, new SimClock(now), null);
        }

        /// <summary>The `trigger` on the combat_started the last command emitted, or null if absent.</summary>
        private static object TriggerOf(MatchSim sim)
        {
            var started = sim.LastObservation.EmittedEvents.FirstOrDefault(e => e.Type == "combat_started");
            Assert.That(started, Is.Not.Null, "R-03: entering combat announces combat_started");
            return started.Fields.TryGetValue("trigger", out var trigger) ? trigger : null;
        }

        private static bool Mentions(string text, string word) =>
            text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

        private static double? AsNumber(object value) => value switch
        {
            int i => i,
            long l => l,
            short s => s,
            double d => d,
            float f => f,
            decimal m => (double)m,
            _ => null,
        };

        /// <summary>
        /// Every leaf value reachable from an object, with the path that reached it. Walks
        /// dictionaries, sequences and the public members of sim types, so a secret buried a few
        /// fields deep is found rather than hidden by a shallow field-name check.
        /// </summary>
        private static List<KeyValuePair<string, object>> Surface(object root)
        {
            var found = new List<KeyValuePair<string, object>>();
            Walk(string.Empty, root, found, 0);
            return found;
        }

        private static void Walk(string path, object node, List<KeyValuePair<string, object>> found, int depth)
        {
            if (node == null || depth > 6)
            {
                return;
            }

            var type = node.GetType();
            if (node is string || type.IsPrimitive || node is decimal || type.IsEnum)
            {
                found.Add(new KeyValuePair<string, object>(path, node));
                return;
            }

            if (node is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    Walk(path + "/" + entry.Key, entry.Value, found, depth + 1);
                }

                return;
            }

            if (node is IEnumerable sequence)
            {
                var index = 0;
                foreach (var item in sequence)
                {
                    Walk(path + "[" + index++ + "]", item, found, depth + 1);
                }

                return;
            }

            if (type.Namespace == "RedHollow.Sim")
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    Walk(path + "/" + field.Name, field.GetValue(node), found, depth + 1);
                }

                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                             .Where(p => p.GetIndexParameters().Length == 0))
                {
                    Walk(path + "/" + property.Name, property.GetValue(node), found, depth + 1);
                }

                return;
            }

            found.Add(new KeyValuePair<string, object>(path, node));
        }

        private static Exception Attempt(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>Rejecting is fine, no-op is fine; "the rule does not exist" is not.</summary>
        private static void AssertDefined(Exception thrown) =>
            Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                "the sad path must have a decided behaviour, not an unimplemented one: " + thrown);

        private static Type MemberValueType(MemberInfo member) => member switch
        {
            FieldInfo field => field.FieldType,
            PropertyInfo property => property.PropertyType,
            _ => null,
        };
    }
}
