using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RedHollow.Game.Host;
using RedHollow.Game.Net;
using RedHollow.Sim;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 014 (T-14): the R-06 session-length measurement harness. R-06 — "session length goal
    /// 25–35 minutes (AUD-4)" — is a PLAYTEST criterion, like R-64: the PRD says so in as many
    /// words, so no machine test may pass or fail a build on it.
    ///
    /// <b>This test therefore ALWAYS PASSES.</b> Its product is the report it writes to the test
    /// output: a full 10-wave loopback match driven by scripted bots, with per-wave planning and
    /// combat sim-time broken out and the session length projected under both readiness models.
    /// The orchestrator carries that report to the owner, who does the actual R-06 judging and the
    /// wave-table tuning (R-19 is per-instance config precisely so retuning never touches rule
    /// code).
    ///
    /// <b>What the numbers are, and are not.</b> Everything is SIM time accumulated on the match
    /// clock — wall-clock cost of the test is a few seconds; a real session's length is the sim
    /// time its host loop plays through. The bots are an idealised party, and the caveats are
    /// printed with every report:
    ///
    ///  * bots ready up the INSTANT planning opens, so measured planning is the floor. R-03 gives
    ///    every wave a 60-second planning ceiling: a party that never readies early spends
    ///    TotalWaves × 60s = 10 minutes in planning alone, and the report projects both ends.
    ///  * bots have perfect aim, unlimited range and never build placeables, so measured combat
    ///    is dominated by monster travel + time-to-kill under a fixed party DPS. Real combat runs
    ///    longer (aiming, repositioning, deaths) — the model's DPS assumptions are printed so the
    ///    owner can discount them.
    ///  * R-04's ~3s interstitial is presentation the sim does not hold; it is added to the
    ///    projection arithmetically, not simulated.
    ///
    /// Driven through the real session stack — <see cref="NetSession"/>, loopback transport,
    /// <see cref="MatchSession"/>, real <see cref="WaveTable.V1"/> — the way T11 drives it, so the
    /// clock that accumulates is the one a real match runs on.
    /// </summary>
    [TestFixture]
    public class T14_TimingTests
    {
        private const double Step60Hz = 1.0 / 60.0;

        /// <summary>
        /// The bot party's basic-attack cadence. The PRD fixes no hero attack rate (it is shell
        /// input cadence), so this is a MODEL PARAMETER, printed in the report: each living bot
        /// lands its class's basic-attack damage on the nearest living monster once per interval.
        /// 0.25s ≈ a competent player holding SPACE — chosen so the idealised party clears the
        /// campaign rather than dying at wave 6, which would truncate the measurement.
        /// </summary>
        private const double BotAttackIntervalSeconds = 0.25;

        /// <summary>Hard sim-time ceiling so a stalled campaign reports instead of hanging the runner.</summary>
        private const double SimTimeBudgetSeconds = 3600.0;

        private const string HostPeerId = "peer_timing_host";
        private const string GuestPeerId = "peer_timing_guest";

        private sealed class WaveTiming
        {
            public int Wave;
            public double PlanningSeconds;
            public double CombatSeconds;
            public int MonstersSent;
            public int CiviliansLeft;
        }

        /// <summary>
        /// R-06 — the measurement itself. Always passes; the assertions at the bottom check only
        /// that the harness really ran (a report about a match that never started would tune
        /// nothing), never the 25–35 minute window.
        /// </summary>
        [Test]
        public void Measure_a_botted_ten_wave_session_and_report_the_timing_breakdown()
        {
            // ---- assemble the real stack (T11's shape) -----------------------------------------
            var simConfig = new SimConfig();
            var session = new NetSession(
                new NetSessionConfig(),
                new LoopbackNetTransport(),
                new ColonyMatchFactory(ColonyMap.V1(), simConfig, new InMemoryProfileStore()));

            session.StartHost(new NetPeer
            {
                PeerId = HostPeerId,
                AccountId = "acc_timing_gunslinger",
                HeroClass = HeroClass.Gunslinger,
                IsHost = true,
            });
            Assert.That(session.TryJoin(new NetPeer
            {
                PeerId = GuestPeerId,
                AccountId = "acc_timing_sawbones",
                HeroClass = HeroClass.Sawbones,
            }), Is.True, "harness: a second bot joins");

            Assert.That(session.TryStartMatch(HostPeerId), Is.True, "harness: the match starts");

            var match = session.Match;
            var state = match.State;
            var clock = match.Clock;
            var totalWaves = state.Wave.TotalWaves;

            var timings = new List<WaveTiming>();
            var nextBotAttackAt = 0.0;
            var stallNote = (string)null;

            // ---- drive the campaign, wave by wave, measuring as it goes -----------------------
            for (var wave = 1; wave <= totalWaves; wave++)
            {
                var expected = wave;

                // Planning segment: from now until this wave's monsters are in the colony. Bots
                // ready up the instant planning opens for the awaited wave (T11's guard: readying
                // in the post-clear window before the counter advances would wedge the campaign).
                var planningStart = clock.ElapsedSeconds;
                var spawned = DriveUntil(
                    session, clock,
                    () => state.Wave.Number == expected && state.Wave.LivingMonsterIds.Count > 0,
                    () => ReadyPartyForWave(match, expected));
                var planningSeconds = clock.ElapsedSeconds - planningStart;

                if (!spawned || state.IsOver)
                {
                    stallNote = "campaign stopped while waiting for wave " + expected + " to spawn"
                        + " (phase '" + state.Phase + "', status '" + state.Status + "', session '"
                        + session.Phase + "')";
                    break;
                }

                var monstersSent = state.Wave.LivingMonsterIds.Count;

                // Combat segment: bots fight until the wave is cleared (or the colony falls).
                var combatStart = clock.ElapsedSeconds;
                var cleared = DriveUntil(
                    session, clock,
                    () => state.Wave.LivingMonsterIds.Count == 0 || state.IsOver,
                    () => BotsAct(match, simConfig, ref nextBotAttackAt));
                var combatSeconds = clock.ElapsedSeconds - combatStart;

                timings.Add(new WaveTiming
                {
                    Wave = expected,
                    PlanningSeconds = planningSeconds,
                    CombatSeconds = combatSeconds,
                    MonstersSent = monstersSent,
                    CiviliansLeft = state.TotalCivilians,
                });

                if (!cleared || state.Status == MatchStatus.Defeat)
                {
                    stallNote = state.Status == MatchStatus.Defeat
                        ? "the bot party was DEFEATED on wave " + expected
                          + " — per-wave numbers above it are still valid; the projection below is partial"
                        : "wave " + expected + " never cleared inside the sim-time budget";
                    break;
                }
            }

            // ---- the report --------------------------------------------------------------------
            var report = BuildReport(simConfig, timings, clock.ElapsedSeconds, totalWaves,
                state, stallNote);

            TestContext.Out.WriteLine(report);
            UnityEngine.Debug.Log(report);

            // ---- always-pass integrity checks (harness ran; never the R-06 window) -------------
            Assert.That(timings, Is.Not.Empty,
                "harness integrity: at least one wave must have been measured, or there is "
                + "nothing to report. " + (stallNote ?? ""));
            Assert.That(timings[0].CombatSeconds, Is.GreaterThan(0.0),
                "harness integrity: combat consumed sim time — a zero-length wave means the bots "
                + "or the clock are miswired and the report above is fiction");
        }

        // ==========================================================================================
        //  the bots
        // ==========================================================================================

        /// <summary>
        /// One bot decision tick, run before every session step. Each living hero lands its
        /// class's basic-attack damage (through <see cref="MatchSim.ResolveHeroAttack"/>, so class
        /// passives such as the Gunslinger crit apply) on the nearest living monster, once per
        /// <see cref="BotAttackIntervalSeconds"/>; a monster driven to 0 HP is then recorded
        /// killed through the sim's own kill command with its catalog bounty (R-20), exactly the
        /// division of labour the shell has: the sim damages, the host records the kill.
        /// </summary>
        private static void BotsAct(HostedMatch match, SimConfig config, ref double nextAttackAt)
        {
            var state = match.State;
            if (state.IsOver || state.Phase != MatchPhase.Combat)
            {
                return;
            }

            var now = match.Clock.ElapsedSeconds;
            if (now < nextAttackAt)
            {
                return;
            }

            nextAttackAt = now + BotAttackIntervalSeconds;

            foreach (var hero in state.Heroes.Values.ToList())
            {
                if (!hero.Alive)
                {
                    continue;
                }

                var target = NearestLivingMonster(state, hero.Pos);
                if (target == null)
                {
                    return;
                }

                var kit = config.HeroKits.KitFor(hero.HeroClass);
                var result = match.Sim.ResolveHeroAttack(new HeroAttackRequest
                {
                    AttackerId = hero.Id,
                    AttackerClass = hero.HeroClass,
                    Damage = kit.BasicAttackDamage,
                    EntitiesOnLine = new List<LineEntity>
                    {
                        new LineEntity { Id = target.Id, Kind = "monster", Pos = target.Pos },
                    },
                });

                if (result.HitId != null && result.TargetHpAfter <= 0.0
                    && state.Monsters.TryGetValue(result.HitId, out var felled) && felled.Alive)
                {
                    match.Sim.RecordMonsterKill(new MonsterKillRequest
                    {
                        MonsterId = felled.Id,
                        MonsterType = felled.Type,
                        Bounty = config.Monsters.StatsFor(felled.Type).Bounty,
                        KillerHeroId = hero.Id,
                    });
                }
            }
        }

        private static Monster NearestLivingMonster(MatchState state, Vec2 from)
        {
            Monster nearest = null;
            var best = double.MaxValue;

            foreach (var monster in state.Monsters.Values)
            {
                if (monster == null || !monster.Alive)
                {
                    continue;
                }

                var distance = from.DistanceTo(monster.Pos);
                if (distance < best)
                {
                    best = distance;
                    nearest = monster;
                }
            }

            return nearest;
        }

        /// <summary>R-03's early exit, guarded the way T11 guards it (ready only for the awaited wave).</summary>
        private static void ReadyPartyForWave(HostedMatch match, int wave)
        {
            var state = match.State;
            if (state.IsOver || state.Phase != MatchPhase.Planning || state.Wave.Number != wave)
            {
                return;
            }

            foreach (var player in state.Players.ToList())
            {
                if (player.Connected && !player.Ready)
                {
                    match.Sim.SetPlayerReady(player.Id);
                }
            }
        }

        /// <summary>
        /// Drives the session until <paramref name="done"/> or the GLOBAL sim-time budget is
        /// spent. The budget is global rather than per-phase because the product here is a total
        /// session length — a per-wave budget would hide exactly the pathology worth reporting.
        /// </summary>
        private static bool DriveUntil(
            NetSession session, SimClock clock, Func<bool> done, Action beforeEachStep)
        {
            while (clock.ElapsedSeconds < SimTimeBudgetSeconds)
            {
                if (done())
                {
                    return true;
                }

                beforeEachStep();
                session.Step(Step60Hz);
            }

            return done();
        }

        // ==========================================================================================
        //  the report
        // ==========================================================================================

        private static string BuildReport(
            SimConfig config, List<WaveTiming> timings, double elapsed,
            int totalWaves, MatchState state, string stallNote)
        {
            var planningMeasured = timings.Sum(t => t.PlanningSeconds);
            var combatMeasured = timings.Sum(t => t.CombatSeconds);
            var interstitialSeconds = 3.0 * Math.Max(0, timings.Count - 1); // R-04, ~3s, arithmetic only
            var planningCeiling = totalWaves * config.PlanningDurationSeconds;

            var earlyReadyProjection = combatMeasured + planningMeasured + interstitialSeconds;
            var ceilingProjection = combatMeasured + planningCeiling + interstitialSeconds;

            var sb = new StringBuilder();
            sb.AppendLine("==== T-14 / R-06 session-length measurement (SIM time; playtest criterion — this test never fails on it) ====");
            sb.AppendLine("model: 2 bots (gunslinger 25 dmg + sawbones 40 dmg), perfect aim, unlimited range,");
            sb.AppendLine("       one basic attack per " + Fmt(BotAttackIntervalSeconds) + "s each, no placeables bought, ready up instantly.");
            sb.AppendLine("       Real parties aim, miss, walk and die: treat combat numbers as a FLOOR.");
            sb.AppendLine();
            sb.AppendLine("wave | monsters | planning s | combat s | civilians left");
            foreach (var t in timings)
            {
                sb.AppendLine(
                    "  " + t.Wave.ToString(CultureInfo.InvariantCulture).PadLeft(2)
                    + " | " + t.MonstersSent.ToString(CultureInfo.InvariantCulture).PadLeft(8)
                    + " | " + Fmt(t.PlanningSeconds).PadLeft(10)
                    + " | " + Fmt(t.CombatSeconds).PadLeft(8)
                    + " | " + t.CiviliansLeft.ToString(CultureInfo.InvariantCulture).PadLeft(3));
            }

            sb.AppendLine();
            sb.AppendLine("measured (bots ready instantly):");
            sb.AppendLine("  combat total   : " + Fmt(combatMeasured) + " s");
            sb.AppendLine("  planning total : " + Fmt(planningMeasured) + " s (floor — instant ready-up)");
            sb.AppendLine("  match clock    : " + Fmt(elapsed) + " s at end (status '" + state.Status + "')");
            sb.AppendLine();
            sb.AppendLine("projected session length:");
            sb.AppendLine("  early-ready party : " + Fmt(earlyReadyProjection) + " s ("
                + FmtMinutes(earlyReadyProjection) + ") — combat + measured planning + ~3s interstitials (R-04)");
            sb.AppendLine("  never-ready party : " + Fmt(ceilingProjection) + " s ("
                + FmtMinutes(ceilingProjection) + ") — the R-03 60s planning ceiling alone contributes "
                + Fmt(planningCeiling) + " s (" + FmtMinutes(planningCeiling) + ") across " + totalWaves + " waves");
            sb.AppendLine();
            sb.AppendLine("R-06 target window: 25–35 min (1500–2100 s). Judgement and R-19 retuning are the");
            sb.AppendLine("owner's: the wave table is per-instance config, so counts/composition/tunnels move");
            sb.AppendLine("without touching rule code (WaveTable.V1 / the shell's ScriptableObject override).");

            if (stallNote != null)
            {
                sb.AppendLine();
                sb.AppendLine("NOTE: " + stallNote);
            }

            return sb.ToString();
        }

        private static string Fmt(double value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);

        private static string FmtMinutes(double seconds) =>
            (seconds / 60.0).ToString("0.#", CultureInfo.InvariantCulture) + " min";
    }
}
