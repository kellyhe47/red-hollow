using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RedHollow.Game.Host;
using RedHollow.Game.Net;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 011 (T-11): the multiplayer session — lobby, loopback, disconnects and rematch.
    /// Requirements R-07, R-53 and R-55, resting on R-50/R-51 (ticket 010, green) and R-02/R-43
    /// (tickets 003/009, green). Grades no golden fixture: every *rule* below already lives in
    /// <see cref="MatchSim"/>, and what has never existed is anything that decides who may do what,
    /// and when.
    ///
    /// <b>What these tests deliberately do NOT cover, stated up front so nothing is read as
    /// verified that is not:</b> real Netcode for GameObjects transport, Unity Lobby join-code
    /// allocation and Relay allocation are not exercised anywhere in this file. They cannot be
    /// driven headlessly from EditMode, and a test that faked them would grade the fake. What is
    /// pinned instead is the seam they sit behind (<see cref="INetTransport"/>) and the fact that
    /// every requirement this ticket owns is decided above it — so the NGO/Lobby/Relay
    /// implementation is a swap rather than a rewrite, and hand-verifying it is a later ticket's
    /// job. Concretely: "the join code is a string that exists and survives a rematch" is asserted;
    /// "Lobby minted it" is not. "The session comes up with no UGS project id" is asserted; "Relay
    /// allocates when one is present" is not.
    ///
    /// Seven things are pinned here and nothing else:
    ///
    ///  1. <b>A 2-player loopback session completes a 10-wave match.</b> The headline criterion, and
    ///     the only test here that is expensive. Real <see cref="ColonyMap.V1"/>, real
    ///     <see cref="MatchSim"/>, real <see cref="MatchSession"/>, driven wave by wave to victory
    ///     with the wave counter walking 1 -> 10 and no eleventh wave behind it.
    ///
    ///  2. <b>A driven session reaches defeat, mid-wave.</b> R-02 is the only loss rule there is, so
    ///     it has to be reachable without clearing a single wave — and reaching it must move the
    ///     *session* to its post-match screen, not just the sim's status field.
    ///
    ///  3. <b>Rematch (R-07).</b> The sharpest test in the ticket: everything resets, the lobby
    ///     survives, and account progression survives the reset (R-43). That third clause is where a
    ///     rematch bug lives — "reset everything" and "profiles persist" are the same code path
    ///     pulling in opposite directions.
    ///
    ///  4. <b>Mid-match disconnect (R-53).</b> The hero despawns, the monsters that were walking at
    ///     it are walking at something else, and the match carries on.
    ///
    ///  5. <b>Host disconnect ends the match (R-53)</b> — and ends it without inventing a defeat,
    ///     because R-02 owns the only one.
    ///
    ///  6. <b>No mid-match joins (R-53)</b>, refused for being mid-match rather than for being full.
    ///
    ///  7. <b>ESC is a non-pausing overlay (R-55).</b> Time and the world keep moving with it open.
    ///
    /// <b>What is deliberately not asserted</b>, because the PRD is silent and a guessed value would
    /// ship as spec: toast copy (R-53 requires that one is shown and names none, so only the notice
    /// *kind* is asserted), join-code format, reconnection (v1 has none), host migration (v1 has
    /// none), and how many host steps after a match ends the post-match screen appears.
    ///
    /// EditMode throughout: the session is driven by an explicit <c>Step</c>, so nothing here needs
    /// a frame, a scene or a socket.
    /// </summary>
    [TestFixture]
    public class T11_SessionTests
    {
        /// <summary>One host step at Unity's default fixed timestep, matching T10, T16 and T19.</summary>
        private const double Step60Hz = 1.0 / 60.0;

        private const double SimTolerance = 1e-6;

        private const string HostPeerId = "peer_host";
        private const string GuestPeerId = "peer_guest";
        private const string HostAccount = "acc_calamity";
        private const string GuestAccount = "acc_doc";

        /// <summary>
        /// An opaque stand-in for a UGS cloud project id. Deliberately not the project's real id:
        /// nothing about this ticket depends on which project it is, only that whatever arrives in
        /// config is what the transport is handed (R-50).
        /// </summary>
        private const string ConfiguredProjectId = "ugs-project-under-test";

        /// <summary>
        /// R-55's trap. <c>Time.timeScale</c> is global editor state, so a session that pauses by
        /// reaching for it would fail this fixture's overlay test and then quietly poison every test
        /// that ran afterwards. Snapshotted and restored so the failure stays where it belongs.
        /// </summary>
        private float _timeScaleAtStart;

        [SetUp]
        public void RememberTimeScale()
        {
            _timeScaleAtStart = Time.timeScale;
        }

        [TearDown]
        public void RestoreTimeScale()
        {
            Time.timeScale = _timeScaleAtStart;
        }

        // ==========================================================================================
        //  AC1 — a 2-player loopback session completes a 10-wave match
        // ==========================================================================================

        /// <summary>
        /// R-01 / R-02 / R-50 / R-51, end to end and with no UGS project id anywhere in the setup:
        /// two players in one loopback lobby play all ten waves and win the map.
        ///
        /// <b>Driven through the sim's own operations, not blow by blow.</b> Each wave is cleared
        /// with <see cref="MatchSim.RecordMonsterKill"/> and each planning phase is ended with
        /// <see cref="MatchSim.SetPlayerReady"/> (R-03's early exit, which is why the party matters
        /// here at all) rather than by simulating ten waves of combat — a full-combat drive would
        /// cost minutes of sim time per wave and would be grading tickets 002/017 all over again.
        /// What is under test is that the *session* carries a party from wave 1 to a victory.
        ///
        /// <b>Every stage is load-bearing and none of it exists yet</b>: the lobby has to seat two
        /// players, the match has to be built for that party, the campaign has to advance ten times,
        /// and the session has to notice the match ended. The wave rosters are asserted disjoint
        /// across the whole run, so a session that re-spawned an earlier wave — or that never
        /// cleared the corpses off the roster — cannot walk to ten by standing still.
        ///
        /// <b>Bounded per wave, not per run.</b> Each wave gets 3x R-03's planning duration of sim
        /// time to arrive; with the ready-up above it normally arrives within a couple of steps, and
        /// the timer alone would get there in one. A wave that never arrives fails as a test failure
        /// naming that wave, rather than as a hung runner, and
        /// <see cref="DescribeStalledCampaign"/> reports which wave the campaign stopped on, what
        /// the counter and phase actually read, how many monsters are standing, whether the match
        /// had already ended, and which waves had been cleared before it stalled.
        /// </summary>
        [Test]
        public void A_two_player_loopback_session_completes_a_ten_wave_match()
        {
            var lobby = NewTwoPlayerLoopbackLobby();

            Assert.That(lobby.NetConfig.UgsProjectId, Is.Null,
                "the whole match below runs with no UGS project id configured (R-50)");
            Assert.That(lobby.Transport.RequiresUnityServices, Is.False,
                "R-50: loopback must need no Unity Gaming Services to host a match");

            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True,
                "R-50: the host starts the match for the seated party");

            var match = lobby.Session.Match;
            Assert.That(match, Is.Not.Null, "starting a match must produce one");
            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.InMatch),
                "the session is in a match once the host has started one");

            Assert.That(match.State.Players.Count, Is.EqualTo(2),
                "R-50: both seated players must be in the match state, or R-03's readiness and "
                + "R-53's disconnect have nobody to act on");
            Assert.That(match.State.Heroes.Count, Is.EqualTo(2),
                "R-50 / R-31: one hero per seated player");

            var totalWaves = match.State.Wave.TotalWaves;
            var cleared = new List<int>();
            var everyMonsterKilled = new HashSet<string>(StringComparer.Ordinal);

            for (var wave = 1; wave <= totalWaves; wave++)
            {
                var expected = wave;
                var arrived = DriveUntil(
                    lobby.Session,
                    match.Clock,
                    () => match.State.Wave.Number == expected
                          && match.State.Wave.LivingMonsterIds.Count > 0,
                    budgetSeconds: 3.0 * lobby.SimConfig.PlanningDurationSeconds,
                    beforeEachStep: () => ReadyPartyForWave(match, expected));

                if (!arrived)
                {
                    Assert.Fail(DescribeStalledCampaign(lobby.Session, match, expected, cleared));
                }

                var roster = match.State.Wave.LivingMonsterIds.ToList();
                foreach (var id in roster)
                {
                    Assert.That(everyMonsterKilled.Contains(id), Is.False,
                        "wave " + expected + " must be new monsters — id '" + id + "' was already "
                        + "killed in an earlier wave, so the campaign is re-serving a cleared roster");
                }

                KillWave(match, roster, bounty: 0);
                everyMonsterKilled.UnionWith(roster);
                cleared.Add(expected);
            }

            Assert.That(cleared.Count, Is.EqualTo(totalWaves),
                "R-01: the counter must walk 1 -> " + totalWaves + ", one wave at a time");

            Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Victory),
                "R-01 / G-011: clearing the final wave wins the map. The campaign reached wave "
                + match.State.Wave.Number + " and the status reads '" + match.State.Status + "'");

            Assert.That(match.State.TotalCivilians, Is.GreaterThan(0),
                "R-02: a victory is a colony that survived — an emptied one is a defeat, whatever "
                + "the status field ended up saying");

            var reachedPostMatch = DriveUntil(
                lobby.Session,
                match.Clock,
                () => lobby.Session.Phase == NetSessionPhase.PostMatch,
                budgetSeconds: 5.0);

            Assert.That(reachedPostMatch, Is.True,
                "R-07: a won match must put the party on the post-match screen, which is where "
                + "PLAY AGAIN lives. The session is still '" + lobby.Session.Phase + "'");

            // R-01 — there is no eleventh wave, and a session that kept advancing the campaign after
            // a victory would either manufacture one or take itself down inside BeginPlanningPhase.
            var monstersAtVictory = match.State.Monsters.Count;
            for (var i = 0; i < 2 * 60; i++)
            {
                lobby.Session.Step(Step60Hz);
            }

            Assert.That(match.State.Wave.Number, Is.EqualTo(totalWaves),
                "R-01: a won match must not advance the wave counter");
            Assert.That(match.State.Monsters.Count, Is.EqualTo(monstersAtVictory),
                "R-01: a won match must spawn nothing further");
        }

        // ==========================================================================================
        //  AC2 — the defeat path, mid-wave
        // ==========================================================================================

        /// <summary>
        /// R-02 / R-53. The same two-player loopback session, left to fend for itself: nobody fires
        /// a shot, nobody builds anything, and the colony falls.
        ///
        /// <b>Mid-wave is the point.</b> R-02 makes an emptied colony the only loss rule in the
        /// game, so defeat has to be reachable without clearing a single wave — asserted as "the
        /// campaign never left the early waves and the wave that killed the colony was still
        /// standing when it did". A session that could only lose by running out of waves would have
        /// quietly turned R-01's ten-wave structure into a second loss condition.
        ///
        /// The heroes are real and stay on the field, which is what separates this from ticket 019's
        /// defeat drive: they soak hits, die, respawn on R-33's timer and soak again, and the wave
        /// still has to get through them to the shelters. That is also why the bound is generous —
        /// 300 sim-seconds against a run that should land inside 100. Tight enough to fail in
        /// seconds rather than hang; loose enough that a correct-but-slower session still passes.
        /// </summary>
        [Test]
        public void A_driven_two_player_session_is_defeated_mid_wave_when_nobody_defends()
        {
            var lobby = NewTwoPlayerLoopbackLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");

            var match = lobby.Session.Match;
            Assert.That(match, Is.Not.Null, "starting a match must produce one");

            var civiliansAtStart = match.State.TotalCivilians;
            Assert.That(civiliansAtStart, Is.EqualTo(20),
                "sanity (R-10): the v1 colony holds 20 civilians across three shelters");
            Assert.That(match.State.Placeables, Is.Empty, "no defenders: nothing is built");

            const int MaxSteps = 300 * 60;

            var steps = 0;
            while (lobby.Session.Phase == NetSessionPhase.InMatch && steps < MaxSteps)
            {
                lobby.Session.Step(Step60Hz);
                steps++;
            }

            if (match.State.Status != MatchStatus.Defeat)
            {
                Assert.Fail(DescribeStalledColony(lobby.Session, match, civiliansAtStart, steps, MaxSteps));
            }

            Assert.That(match.State.TotalCivilians, Is.EqualTo(0),
                "R-02: defeat is the colony emptied — every shelter at zero, not a shortcut to the flag");

            Assert.That(match.State.Wave.Number, Is.LessThan(match.State.Wave.TotalWaves),
                "R-02: defeat is the ONLY loss rule, so it has to arrive mid-campaign. This match "
                + "reached wave " + match.State.Wave.Number + " of " + match.State.Wave.TotalWaves);
            Assert.That(match.State.Wave.LivingMonsterIds, Is.Not.Empty,
                "R-02: and mid-wave — the wave that emptied the colony was still standing");

            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.PostMatch),
                "R-07: a lost match must put the party on the post-match screen, which is where "
                + "RETRY lives");
        }

        /// <summary>
        /// R-16 / B-002 in a FACTORY-BUILT match: a barricade across wave 1's lane redirects the
        /// whole wave onto itself, is chewed down, and its collapse releases the lane. G-004 locks
        /// the sim rule through <see cref="DeclaredPathOracle"/>; what nothing locked was the
        /// production answerer, so <see cref="ColonyMatchFactory"/> ran on
        /// <see cref="OpenPathOracle"/> and a purchased wall was scenery no monster ever attacked.
        /// This is the pin that the shipped composition actually blocks.
        /// </summary>
        [Test]
        public void A_barricade_across_the_lane_redirects_the_wave_until_it_falls()
        {
            var lobby = NewTwoPlayerLoopbackLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");
            var match = lobby.Session.Match;

            // Wave 1 pours out of breach 0; its nearest valid target is the saloon (the heroes at
            // team spawn stand further). The wall sits mid-lane, seeded directly because R-21
            // gates purchases to planning and what is under test is the block, not the buy.
            var mouth = ColonyMap.V1().EntryTunnels[0];
            var shelter = match.State.Hotspots["hs_saloon"].Pos;
            match.State.Placeables["wall"] = new Placeable
            {
                Id = "wall",
                Type = PlaceableType.Barricade,
                Pos = new Vec2((mouth.X + shelter.X) / 2.0, (mouth.Y + shelter.Y) / 2.0),
                OwnerPlayerId = match.State.Players[0].Id,
                PurchaseCost = 100,
                Hp = 300.0,
                Exists = true,
            };

            lobby.Session.Step(Step60Hz);

            var living = match.State.Monsters.Values.Where(m => m.Alive).ToList();
            Assert.That(living, Is.Not.Empty, "sanity (R-19): wave 1 is in the colony");
            foreach (var monster in living)
            {
                Assert.That(monster.TargetId, Is.EqualTo("wall"),
                    "R-16/B-002: the wall across the lane IS the target — with OpenPathOracle "
                    + "(the pre-028 wiring) every monster walks past it at the shelter");
            }

            // The wave walks to the wall and chews it down (~10s at six shamblers): R-16's
            // "until destroyed" through the real contact gate and ApplyPlaceableDamage.
            var wall = match.State.Placeables["wall"];
            var fell = DriveUntil(
                lobby.Session, match.Clock, () => !wall.Exists, budgetSeconds: 40.0);

            Assert.That(fell, Is.True,
                "R-16/R-23: the redirected wave must actually destroy the wall — walking to it "
                + "and standing politely means the contact gate never routed a placeable hit");

            // The collapse releases the lane: the survivors' next retarget answers the shelter.
            var survivor = match.State.Monsters.Values.FirstOrDefault(m => m.Alive);
            Assert.That(survivor, Is.Not.Null,
                "sanity: a 300 HP wall does not outlive six shamblers' patience with none dying");

            lobby.Session.Step(Step60Hz);
            Assert.That(survivor.TargetId, Is.EqualTo("hs_saloon"),
                "R-16 'until destroyed': rubble blocks nothing, so the wave resumes its walk at "
                + "the shelter behind it");
        }

        /// <summary>
        /// The wave-stall bug, pinned dead (owner playtest, 2026-08-26): <c>MatchSim.TurretTick</c>
        /// flips <c>Alive</c> at 0 HP but deliberately leaves R-40's accounting to its caller —
        /// so a session that never reaps a turret LAST-HIT leaves the corpse on
        /// <see cref="WaveState.LivingMonsterIds"/> and the campaign stalls forever: no
        /// wave_complete, no planning, no wave 2. The fix is <see cref="MatchSession"/>'s
        /// placeable reap; this is its end-to-end pin through <see cref="NetSession.Step"/> —
        /// roster cleared, bounty paid (R-20), the PLACER's account credited (R-40), and the
        /// campaign actually moving on (R-03), which is the symptom the playtest saw.
        /// </summary>
        [Test]
        public void A_turret_last_hit_clears_the_wave_and_the_campaign_moves_on()
        {
            var lobby = NewTwoPlayerLoopbackLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");
            var match = lobby.Session.Match;

            // Whittle wave 1 to one survivor, standing at exactly one turret tick of HP.
            var roster = match.State.Wave.LivingMonsterIds.ToList();
            foreach (var id in roster.Take(roster.Count - 1))
            {
                match.Sim.RecordMonsterKill(new MonsterKillRequest
                {
                    MonsterId = id,
                    MonsterType = match.State.Monsters[id].Type,
                    Bounty = 0,
                });
            }

            var survivorId = roster[roster.Count - 1];
            var survivor = match.State.Monsters[survivorId];
            var turretStats = match.Sim.Config.Placeables.StatsFor(PlaceableType.Turret);
            survivor.Hp = turretStats.Damage;

            var ownerSlot = match.State.Players[0].Id;
            match.State.Placeables["turret_pin"] = new Placeable
            {
                Id = "turret_pin",
                Type = PlaceableType.Turret,
                Pos = survivor.Pos,
                OwnerPlayerId = ownerSlot,
                Exists = true,
                Damage = turretStats.Damage,
                Range = turretStats.Range,
            };

            var scripBefore = match.State.Team.Scrip;
            var ownerAccount = match.State.Players[0].AccountId;
            var xpBefore = lobby.Profiles.Load(ownerAccount).LifetimeXp;
            var bounty = match.Sim.Config.Monsters.StatsFor(survivor.Type).Bounty;

            lobby.Session.Step(Step60Hz);

            Assert.That(survivor.Alive, Is.False,
                "sanity (R-23/G-028): the tick emptied the survivor's HP and flagged the corpse");
            Assert.That(match.State.Wave.LivingMonsterIds, Does.Not.Contain(survivorId),
                "THE BUG: a turret last-hit must be reaped through RecordMonsterKill — a corpse "
                + "left on the roster is a wave that never completes and a match that stalls");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore + bounty),
                "R-20: the kill pays its catalog bounty into the shared pool");
            Assert.That(lobby.Profiles.Load(ownerAccount).LifetimeXp,
                Is.EqualTo(xpBefore + bounty).Within(SimTolerance),
                "R-40: a turret kill credits the PLACER's account");

            // The symptom the playtest saw was the campaign freezing — so the pin is the campaign
            // MOVING: planning opens for wave 2, both players ready, and wave 2's monsters arrive.
            var partyReady = new Action(() =>
            {
                if (match.State.Phase == MatchPhase.Planning)
                {
                    foreach (var player in match.State.Players)
                    {
                        match.Sim.SetPlayerReady(player.Id);
                    }
                }
            });

            var arrived = DriveUntil(
                lobby.Session, match.Clock,
                () => match.State.Wave.Number == 2 && match.State.Wave.LivingMonsterIds.Count > 0,
                budgetSeconds: 10.0,
                beforeEachStep: partyReady);

            Assert.That(arrived, Is.True,
                "R-03/R-19: the turret-cleared wave must be followed by wave 2 — the stalled "
                + "campaign is exactly the bug this test exists to keep dead");
        }

        /// <summary>
        /// R-17's "ranged acid, range 10" through the REAL driven session (ticket 029): a Spitter
        /// walks to its line, holds there, and drains the shelter from range — movement's
        /// hold-at-reach, the contact source's widened reach, the R-18 gate and R-11's civilian
        /// arithmetic, all in one pass. Before 029 a Spitter walked into hugging distance like
        /// every melee archetype, and nothing anywhere exercised the row's one distinguishing
        /// column.
        /// </summary>
        [Test]
        public void A_spitter_drains_a_shelter_from_its_acid_line()
        {
            var lobby = NewTwoPlayerLoopbackLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");
            var match = lobby.Session.Match;

            // Seeded by the chapel, far from wave 1's saloon-bound shamblers, with no target so
            // the session's own R-16 pass picks the shelter. Stats come off the shipped row.
            var stats = match.Sim.Config.Monsters.StatsFor(MonsterType.Spitter);
            var chapel = match.State.Hotspots["hs_chapel"];
            match.State.Monsters["m_spit"] = new Monster
            {
                Id = "m_spit",
                Type = MonsterType.Spitter,
                Pos = new Vec2(chapel.Pos.X, chapel.Pos.Y + 20.0),
                Hp = stats.MaxHp,
                BaseSpeed = stats.MoveSpeed,
                CurrentSpeed = stats.MoveSpeed,
                AttackRange = stats.AttackRange,
                Alive = true,
            };

            var civiliansBefore = chapel.Civilians;
            Assert.That(civiliansBefore, Is.GreaterThan(0), "sanity: the chapel is sheltering people");

            var spitter = match.State.Monsters["m_spit"];
            var drained = DriveUntil(
                lobby.Session, match.Clock,
                () => chapel.Civilians < civiliansBefore, budgetSeconds: 15.0);

            Assert.That(drained, Is.True,
                "R-17/R-11: the Spitter must hurt the shelter — walk in (10 units of ground at "
                + "speed 2), hold its line, clear the R-18 gate, land acid");
            Assert.That(spitter.Alive, Is.True, "nothing fought back; the spitter is still working");

            // The first acid can land from one step outside the line ("arrived this tick", the
            // same allowance melee contact has always had). A couple more steps settle the walk
            // exactly onto the line, where it holds for the rest of the match.
            lobby.Session.Step(Step60Hz);
            lobby.Session.Step(Step60Hz);
            Assert.That(spitter.Pos.DistanceTo(chapel.Pos), Is.EqualTo(stats.AttackRange).Within(1e-6),
                "R-17: the spitter works FROM its line — one standing on the shelter is the "
                + "pre-029 melee walk wearing a ranged monster's name");
        }

        // ==========================================================================================
        //  AC3 — rematch (R-07)
        // ==========================================================================================

        /// <summary>
        /// R-07, and the interaction that makes it hard: <b>everything in the match resets, and the
        /// two things outside it do not.</b>
        ///
        /// The finished match below is deliberately messy — bounty banked into the shared pool
        /// (R-20), a barricade standing on the field (R-23), every civilian dead (R-02) and XP
        /// earned by both accounts (R-40) — so that a rematch which reset *most* fields fails on the
        /// one it forgot. The reset is asserted as a different world rather than as a scrubbed one:
        /// a session that reached into <see cref="MatchState"/> and set fields back would have to
        /// remember every field the sim ever grows, and the first one it missed is a wave-3
        /// barricade standing in a fresh match.
        ///
        /// Retained across it: the join code and both class picks (R-07), and account progression
        /// (R-43 — lifetime XP never resets, not per wave and not per match).
        ///
        /// <b>The XP is split on purpose.</b> The host banks enough to level up, which R-43 already
        /// persists at the moment it happens (G-023); the guest banks less than one level, which
        /// nothing persists until the match ends (G-024). So the guest's saved profile is the
        /// discriminator for "profiles were persisted at match end", and the host's surviving level
        /// is the discriminator for "the reset did not take progression with it".
        /// </summary>
        [Test]
        public void A_rematch_resets_the_whole_match_while_the_lobby_and_profiles_survive()
        {
            var lobby = NewTwoPlayerLoopbackLobby();

            var joinCodeBefore = lobby.Session.JoinCode;
            Assert.That(joinCodeBefore, Is.Not.Null.And.Not.Empty,
                "R-07: a lobby has a join code to return to, loopback included");

            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");

            var first = lobby.Session.Match;
            var firstState = first.State;

            Assert.That(firstState.Team.Scrip, Is.EqualTo(lobby.SimConfig.StartingScrip),
                "sanity (R-20): a match opens on the configured stake");

            // R-40 / R-41 — XP for both accounts. 350 crosses two level thresholds (100, 300);
            // 50 crosses none.
            var hostHero = HeroFor(firstState, HostAccount);
            var guestHero = HeroFor(firstState, GuestAccount);

            first.Sim.AwardKillXp(
                new MonsterKillRequest
                {
                    MonsterId = "m_scored_host",
                    MonsterType = MonsterType.Shambler,
                    Bounty = 350,
                    KillerHeroId = hostHero.Id,
                },
                HostAccount);

            first.Sim.AwardKillXp(
                new MonsterKillRequest
                {
                    MonsterId = "m_scored_guest",
                    MonsterType = MonsterType.Shambler,
                    Bounty = 50,
                    KillerHeroId = guestHero.Id,
                },
                GuestAccount);

            var hostLevel = lobby.Profiles.Load(HostAccount).Level;
            Assert.That(hostLevel, Is.GreaterThan(1),
                "sanity (R-41): 350 lifetime XP is past the level-2 and level-3 thresholds, which is "
                + "what makes 'the level survived the rematch' worth asserting");

            // R-20 — move the shared pool off its opening stake.
            var aMonster = firstState.Wave.LivingMonsterIds.First();
            first.Sim.RecordMonsterKill(new MonsterKillRequest
            {
                MonsterId = aMonster,
                MonsterType = firstState.Monsters[aMonster].Type,
                Bounty = 10,
            });
            Assert.That(firstState.Team.Scrip, Is.Not.EqualTo(lobby.SimConfig.StartingScrip),
                "sanity (R-20): the pool moved, so 'scrip resets' has something to reset");

            // R-23 — something standing on the field. Seeded directly rather than purchased: R-21
            // gates purchases to a planning phase, and what is under test is the reset, not the buy.
            firstState.Placeables["p_wall"] = new Placeable
            {
                Id = "p_wall",
                Type = PlaceableType.Barricade,
                Pos = new Vec2(4.0, 4.0),
                OwnerPlayerId = firstState.Players[0].Id,
                PurchaseCost = 100,
                Hp = 200.0,
                Exists = true,
            };

            EndTheMatchByEmptyingTheColony(first);

            var reachedPostMatch = DriveUntil(
                lobby.Session,
                first.Clock,
                () => lobby.Session.Phase == NetSessionPhase.PostMatch,
                budgetSeconds: 5.0);

            Assert.That(reachedPostMatch, Is.True,
                "R-07: PLAY AGAIN / RETRY lives on the post-match screen, so the session has to "
                + "reach it. The session is '" + lobby.Session.Phase + "' and the match status is '"
                + first.State.Status + "'");

            // R-43 / R-07 — profiles are persisted when the match ends. The guest banked less than
            // one level, so nothing but a match-end save can have written it.
            Assert.That(lobby.Profiles.Saved.ContainsKey(GuestAccount), Is.True,
                "R-43 / R-07: every player's profile must be persisted at match end — the guest "
                + "never levelled, so nothing else would ever have written theirs");
            Assert.That(lobby.Profiles.Saved[GuestAccount].LifetimeXp, Is.EqualTo(50.0).Within(SimTolerance),
                "R-43: the XP earned since the last save must survive the end of the match");
            Assert.That(lobby.Profiles.Saved.ContainsKey(HostAccount), Is.True,
                "R-43: the host's profile is persisted too");

            // ---- the rematch itself -----------------------------------------------------------

            Assert.That(lobby.Session.TryRematch(HostPeerId), Is.True,
                "R-07: the host may PLAY AGAIN from the post-match screen");

            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "R-07: a rematch returns the whole party to the same lobby");
            Assert.That(lobby.Session.JoinCode, Is.EqualTo(joinCodeBefore),
                "R-07: the SAME lobby — the join code is retained, so a party member who was "
                + "reading it off the screen is still looking at the right one");

            Assert.That(lobby.Session.Seats.Count, Is.EqualTo(2),
                "R-07: the whole party returns, not just the host");
            Assert.That(ClassPickFor(lobby.Session, HostPeerId), Is.EqualTo(HeroClass.Gunslinger),
                "R-07: class picks are retained");
            Assert.That(ClassPickFor(lobby.Session, GuestPeerId), Is.EqualTo(HeroClass.Sawbones),
                "R-07: every class pick, not just the host's");

            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True,
                "the rematched lobby must be startable");

            var second = lobby.Session.Match;

            Assert.That(second, Is.Not.SameAs(first),
                "R-07: 'all match state resets fully' is a new match, not the old one scrubbed — "
                + "a reset that edits fields in place misses the first field the sim grows next");
            Assert.That(second.State, Is.Not.SameAs(firstState),
                "R-07: and a new world with it");

            Assert.That(second.State.Team.Scrip, Is.EqualTo(lobby.SimConfig.StartingScrip),
                "R-07 / R-20: scrip resets to the starting stake");
            Assert.That(second.State.Wave.Number, Is.EqualTo(1),
                "R-07: waves reset — a rematch starts the campaign again, not where it stopped");
            Assert.That(second.State.Placeables, Is.Empty,
                "R-07: placeables reset — nothing the last match built is standing");
            Assert.That(second.State.TotalCivilians, Is.EqualTo(20),
                "R-07 / R-10: civilians reset — the colony that was wiped out is whole again");
            Assert.That(second.State.Status, Is.EqualTo(MatchStatus.InProgress),
                "R-07: the match is live again, not still holding the last one's defeat");

            Assert.That(HeroFor(second.State, HostAccount).HeroClass, Is.EqualTo(HeroClass.Gunslinger),
                "R-07 / R-31: the retained pick is what the new match's hero is");
            Assert.That(HeroFor(second.State, GuestAccount).HeroClass, Is.EqualTo(HeroClass.Sawbones),
                "R-07 / R-31: for every player");

            // R-43 — the one thing a full reset must not take with it.
            var hostProfile = lobby.Profiles.Load(HostAccount);
            Assert.That(hostProfile.LifetimeXp, Is.EqualTo(350.0).Within(SimTolerance),
                "R-43 / R-07: lifetime XP never resets — not per wave, not per match, and not "
                + "because the host clicked PLAY AGAIN");
            Assert.That(hostProfile.Level, Is.EqualTo(hostLevel),
                "R-43: the level earned last match survives into this one");
            Assert.That(lobby.Profiles.Load(GuestAccount).LifetimeXp, Is.EqualTo(50.0).Within(SimTolerance),
                "R-43: for every account, whether or not it levelled");
        }

        /// <summary>
        /// R-07 — "when the host clicks it". A client that could restart the match could restart it
        /// out from under the party, so the refusal is a rule rather than a UI affordance: hiding
        /// the button on non-host clients leaves the message that presses it unguarded.
        ///
        /// Refused rather than thrown, matching how <see cref="PartyRoster.TryAdd"/> refuses a fifth
        /// joiner: a message from a client that is out of turn is an ordinary thing to receive.
        /// </summary>
        [Test]
        public void Only_the_host_may_rematch()
        {
            var lobby = NewTwoPlayerLoopbackLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");

            var match = lobby.Session.Match;
            EndTheMatchByEmptyingTheColony(match);

            var reachedPostMatch = DriveUntil(
                lobby.Session,
                match.Clock,
                () => lobby.Session.Phase == NetSessionPhase.PostMatch,
                budgetSeconds: 5.0);
            Assert.That(reachedPostMatch, Is.True, "the finished match must reach the post-match screen");

            Assert.That(lobby.Session.TryRematch(GuestPeerId), Is.False,
                "R-07: only the host restarts the match");
            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.PostMatch),
                "R-07: a refused rematch must change nothing — the party is still looking at the "
                + "post-match screen");
            Assert.That(lobby.Session.Match, Is.SameAs(match),
                "R-07: and is still looking at the same finished match");

            Assert.That(lobby.Session.TryRematch(HostPeerId), Is.True,
                "anti-vacuity: the host's rematch is accepted, so the refusal above is about who "
                + "asked and not about the session refusing everybody");
        }

        // ==========================================================================================
        //  AC4 — mid-match disconnect (R-53)
        // ==========================================================================================

        /// <summary>
        /// R-53. A player leaves mid-match: their hero despawns, the monsters that were walking at
        /// it retarget, and the match continues.
        ///
        /// <b>The retarget is asserted through the sim's own R-16 answer, never through a field.</b>
        /// The wave is staged so that the departing hero really is what every monster is chasing —
        /// it stands between the breach and the nearest shelter, and the other hero is parked far
        /// off the map — so "no monster still names the departed hero" is a claim about a decision
        /// that was actually re-made, not about a target nobody had. Each survivor's new target is
        /// then resolved against the world, because a monster pointed at a hero that no longer
        /// exists and a monster pointed at nothing are the same bug wearing different values.
        ///
        /// Whether the session retargets eagerly or lets the next host step do it is not asserted:
        /// R-53 says monsters retarget, and one step of a 60Hz loop is not a distinction the PRD
        /// draws. What is asserted is that after one step nobody is chasing a ghost.
        ///
        /// The toast is asserted as a notice of the right *kind* about the right peer, and its copy
        /// is not asserted at all — R-53 requires that one is shown and names none.
        /// </summary>
        [Test]
        public void A_mid_match_disconnect_despawns_the_hero_and_the_monsters_retarget()
        {
            var lobby = NewTwoPlayerLoopbackLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");

            var match = lobby.Session.Match;
            var leaverHero = HeroFor(match.State, GuestAccount);
            var leaverHeroId = leaverHero.Id;

            // Stage the wave onto the leaver: they stand between breach 0 (-30, 0) and the nearest
            // shelter, and the other hero is parked well outside the colony so it cannot be the
            // answer R-16 gives either before or after.
            HeroFor(match.State, HostAccount).Pos = new Vec2(60.0, 60.0);
            leaverHero.Pos = new Vec2(-20.0, 0.0);

            lobby.Session.Step(Step60Hz);

            var chasingTheLeaver = match.State.Monsters.Values
                .Where(m => m.Alive && m.TargetId == leaverHeroId)
                .Select(m => m.Id)
                .ToList();

            Assert.That(chasingTheLeaver, Is.Not.Empty,
                "sanity (R-16): the wave must actually be chasing the hero that is about to leave, "
                + "or 'the monsters retargeted' is a claim about nothing");

            var noticesBefore = lobby.Session.Notices.Count;

            lobby.Session.Disconnect(GuestPeerId);

            Assert.That(match.State.Heroes.ContainsKey(leaverHeroId), Is.False,
                "R-53: a mid-match disconnect despawns that player's hero");
            Assert.That(match.State.IsOver, Is.False,
                "R-53: the match continues — one player leaving is not a loss condition (R-02 owns "
                + "the only one)");
            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.InMatch),
                "R-53: and the session is still in it");

            var leaverSlot = match.State.Players.FirstOrDefault(p => p.AccountId == GuestAccount);
            Assert.That(leaverSlot, Is.Not.Null,
                "R-53 / R-03: the slot stays so readiness can stop waiting on it — it is marked "
                + "disconnected, not deleted");
            Assert.That(leaverSlot.Connected, Is.False,
                "R-53 / R-03: a disconnected player neither holds planning open nor counts as a yes");

            Assert.That(lobby.Session.Seats.Select(s => s.PeerId), Does.Not.Contain(GuestPeerId),
                "R-53: the seat is freed");

            Assert.That(lobby.Session.Notices.Count, Is.GreaterThan(noticesBefore),
                "R-53: a toast is shown when a player drops");
            var notice = lobby.Session.Notices.Last();
            Assert.That(notice.Kind, Is.EqualTo(SessionNoticeKind.PlayerDisconnected),
                "R-53: the toast is about the disconnect (its wording is presentation and is not "
                + "asserted anywhere)");
            Assert.That(notice.PeerId, Is.EqualTo(GuestPeerId),
                "R-53: and names who left");

            lobby.Session.Step(Step60Hz);

            foreach (var monsterId in chasingTheLeaver)
            {
                var monster = match.State.Monsters[monsterId];
                if (!monster.Alive)
                {
                    continue;
                }

                Assert.That(monster.TargetId, Is.Not.EqualTo(leaverHeroId),
                    "R-53: monster '" + monsterId + "' is still walking at a hero that has "
                    + "despawned — SelectTarget has to be re-driven for everything that was "
                    + "chasing the player who left");
                Assert.That(TargetExistsInTheWorld(match.State, monster.TargetId), Is.True,
                    "R-53 / R-16: monster '" + monsterId + "' now holds target '" + monster.TargetId
                    + "', which resolves to nothing in the world — a retarget that answers with a "
                    + "ghost is the same stall as no retarget at all");
            }

            // The match really does carry on: a second of driven session, and it is still live.
            for (var i = 0; i < 60; i++)
            {
                lobby.Session.Step(Step60Hz);
            }

            Assert.That(match.State.IsOver, Is.False,
                "R-53: the match continues after a disconnect");
            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.InMatch),
                "R-53: and keeps running");
        }

        /// <summary>
        /// R-53 — "host disconnect ends the match (no host migration in v1)".
        ///
        /// The sharp half is what it must NOT do: R-02 makes the emptied colony the only defeat in
        /// the game, so an abandoned match is not a lost one. A session that flipped
        /// <see cref="MatchState.Status"/> to defeat here would be inventing a second loss rule and
        /// writing it onto the players' post-match screen — and, worse, onto whatever reads the
        /// status afterwards.
        ///
        /// "No migration" is asserted as a session that stays ended when it is driven on, rather
        /// than as the absence of a mechanism that was never written.
        /// </summary>
        [Test]
        public void A_host_disconnect_ends_the_match_without_inventing_a_defeat()
        {
            var lobby = NewTwoPlayerLoopbackLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");

            var match = lobby.Session.Match;
            Assert.That(match.State.Status, Is.EqualTo(MatchStatus.InProgress),
                "sanity: the match is live before the host leaves");

            lobby.Session.Disconnect(HostPeerId);

            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.Ended),
                "R-53: the host leaving ends the match — v1 has no host migration to fall back on");

            Assert.That(match.State.Status, Is.EqualTo(MatchStatus.InProgress),
                "R-02 / R-53: an abandoned match is not a defeat. The only loss rule in the game is "
                + "the colony being emptied, and nobody emptied it");

            var notice = lobby.Session.Notices.LastOrDefault();
            Assert.That(notice, Is.Not.Null, "R-53: the party is told why the match stopped");
            Assert.That(notice.Kind, Is.EqualTo(SessionNoticeKind.HostDisconnected),
                "R-53: and told that it was the host (the wording is not asserted)");

            for (var i = 0; i < 60; i++)
            {
                lobby.Session.Step(Step60Hz);
            }

            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.Ended),
                "R-53: no host migration — driving the session on must not resurrect the match");
        }

        /// <summary>
        /// R-53 — "no mid-match joins".
        ///
        /// The joiner is the party's fourth, not its fifth, so the refusal can only be about the
        /// match being in progress: a test that filled the lobby first would pass on R-50's size cap
        /// (ticket 010, already green) while the mid-match rule was missing entirely.
        ///
        /// A refusal that had already seated somebody would be worse than no rule at all, so the
        /// party, the match's player slots and the hero roster are all asserted unchanged — a
        /// half-applied join is a hero standing in a match nobody is driving.
        /// </summary>
        [Test]
        public void No_player_may_join_a_match_already_in_progress()
        {
            var lobby = NewTwoPlayerLoopbackLobby();

            var third = NewPeer("peer_third", "acc_stranger", HeroClass.Rancher);
            Assert.That(lobby.Session.TryJoin(third), Is.True,
                "anti-vacuity (R-50): a third player is welcome while the party is in the lobby, so "
                + "the refusal below is about the match and not about the session refusing everybody");

            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");

            var match = lobby.Session.Match;
            var seatsBefore = lobby.Session.Seats.Count;
            var slotsBefore = match.State.Players.Count;
            var heroesBefore = match.State.Heroes.Count;

            Assert.That(seatsBefore, Is.EqualTo(3),
                "sanity: three seated, so a fourth is inside R-50's cap of " + PartyRoster.MaxPlayers);

            var latecomer = NewPeer("peer_late", "acc_latecomer", HeroClass.Gunslinger);

            Assert.That(lobby.Session.TryJoin(latecomer), Is.False,
                "R-53: no mid-match joins. The party had room, so nothing but the match being in "
                + "progress can refuse this");

            Assert.That(lobby.Session.Seats.Count, Is.EqualTo(seatsBefore),
                "R-53: a refused join must not seat anybody");
            Assert.That(match.State.Players.Count, Is.EqualTo(slotsBefore),
                "R-53: and must not add a player slot to the running match");
            Assert.That(match.State.Heroes.Count, Is.EqualTo(heroesBefore),
                "R-53: and must not put a hero on the field");

            var notice = lobby.Session.Notices.LastOrDefault();
            Assert.That(notice, Is.Not.Null, "R-53: the refusal is surfaced rather than silent");
            Assert.That(notice.Kind, Is.EqualTo(SessionNoticeKind.JoinRefused),
                "R-53: as a refused join (the wording is not asserted)");
        }

        // ==========================================================================================
        //  AC5 — ESC is a non-pausing overlay (R-55)
        // ==========================================================================================

        /// <summary>
        /// R-55 — "ESC = non-pausing overlay menu; multiplayer never pauses". The whole requirement,
        /// and it is easy to get wrong in exactly one way: reaching for <c>Time.timeScale = 0</c>,
        /// which in a host-authoritative session (R-51) stops one player's host loop and desynks
        /// everybody else.
        ///
        /// Asserted on the world rather than on a flag: with the overlay open the sim clock still
        /// advances by the deltas it was handed, and a monster that was walking somewhere is closer
        /// to it than it was. A session that froze the loop would keep <c>IsOverlayOpen</c> true and
        /// fail both.
        ///
        /// <c>Time.timeScale</c> is checked directly because it is the specific mistake, and because
        /// a session that paused that way would otherwise pass a world assertion driven by explicit
        /// <c>Step</c> calls: EditMode does not consult the time scale on its own.
        /// </summary>
        [Test]
        public void The_esc_overlay_does_not_pause_the_match()
        {
            var lobby = NewTwoPlayerLoopbackLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");

            var match = lobby.Session.Match;

            // One step so R-16 has handed the wave its targets and there is movement to measure.
            lobby.Session.Step(Step60Hz);

            var walker = match.State.Monsters.Values.FirstOrDefault(
                m => m.Alive && !string.IsNullOrEmpty(m.TargetId));
            Assert.That(walker, Is.Not.Null,
                "sanity (R-16 / R-19): the match must have a targeted monster to watch");

            var clockBefore = match.Clock.ElapsedSeconds;
            var posBefore = walker.Pos;

            lobby.Session.SetOverlayOpen(true);
            Assert.That(lobby.Session.IsOverlayOpen, Is.True, "R-55: ESC opens the overlay");

            const int Steps = 120;
            for (var i = 0; i < Steps; i++)
            {
                lobby.Session.Step(Step60Hz);
            }

            Assert.That(match.Clock.ElapsedSeconds,
                Is.EqualTo(clockBefore + (Steps * Step60Hz)).Within(SimTolerance),
                "R-55: the overlay is not a pause — sim time must keep advancing by the deltas the "
                + "session was stepped with");

            Assert.That(posBefore.DistanceTo(walker.Pos), Is.GreaterThan(0.0),
                "R-55: multiplayer never pauses — the world has to keep moving under an open "
                + "overlay, or one player's menu freezes everybody's match");

            Assert.That(Time.timeScale, Is.EqualTo(1f),
                "R-55: the overlay must never pause by way of Time.timeScale — it stops the host "
                + "loop for the one player who opened a menu and desyncs the rest of the party");

            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.InMatch),
                "R-55: an overlay is an overlay; it does not leave the match");

            lobby.Session.SetOverlayOpen(false);
            Assert.That(lobby.Session.IsOverlayOpen, Is.False, "R-55: and ESC closes it again");
        }

        // ==========================================================================================
        //  AC6 — the UGS project id arrives via config, and loopback needs none
        // ==========================================================================================

        /// <summary>
        /// R-50 — the transport stack is Lobby + Relay, both of which authenticate against a UGS
        /// cloud project. Two halves, and the first is the acceptance criterion:
        ///
        ///  * <b>loopback needs no project id.</b> A session configured with none comes up, opens a
        ///    lobby with a join code and runs a match. A project id baked into the shell is a build
        ///    that only works on one account; a project id *required* by the shell is a game that
        ///    cannot be played or tested offline, which R-50's "solo = 1-player lobby" rules out.
        ///
        ///  * <b>a configured id is carried.</b> When one is supplied it reaches the transport
        ///    unchanged, so the Lobby/Relay implementation has it to authenticate with.
        ///
        /// <b>Relay allocation is not tested here and cannot be</b> — see the fixture doc. What this
        /// pins is the injection, which is the half that is verifiable headlessly and the half that
        /// breaks silently.
        /// </summary>
        [Test]
        public void Loopback_needs_no_ugs_project_id_and_a_configured_id_is_carried()
        {
            // ---- half 1: no project id at all --------------------------------------------------
            var simConfig = new SimConfig();
            var netConfig = new NetSessionConfig();

            Assert.That(netConfig.UgsProjectId, Is.Null,
                "R-50: a session config names no project until somebody injects one — the offline "
                + "case must be the default, not a special mode");

            var transport = new LoopbackNetTransport();
            var session = new NetSession(
                netConfig,
                transport,
                new ColonyMatchFactory(ColonyMap.V1(), simConfig, new SnapshottingProfileStore()));

            session.StartHost(NewPeer(HostPeerId, HostAccount, HeroClass.Gunslinger, isHost: true));

            Assert.That(transport.RequiresUnityServices, Is.False,
                "R-50: loopback must come up without Unity Gaming Services");
            Assert.That(transport.ProjectId, Is.Null.Or.Empty,
                "R-50: and must not invent a project id it was never given — a defaulted id makes "
                + "the offline case indistinguishable from a misconfigured Relay one");

            Assert.That(session.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "R-50: hosting with no project id opens a lobby, it does not fail");
            Assert.That(session.JoinCode, Is.Not.Null.And.Not.Empty,
                "R-07 / R-50: the lobby has a join code offline too, so the lobby screen and the "
                + "rematch path have one shape rather than two (its format is not asserted)");

            Assert.That(session.TryStartMatch(HostPeerId), Is.True,
                "R-50: a solo loopback lobby is a playable match — solo is a 1-player lobby");

            for (var i = 0; i < 60; i++)
            {
                session.Step(Step60Hz);
            }

            Assert.That(session.Match, Is.Not.Null, "the loopback match exists");
            Assert.That(session.Match.Clock.ElapsedSeconds, Is.GreaterThan(0.0),
                "R-50 / R-51: and really runs — a session that needs UGS would not have got here");
            Assert.That(session.Phase, Is.EqualTo(NetSessionPhase.InMatch),
                "and is still in the match");

            // ---- half 2: a project id supplied through config -----------------------------------
            var configuredNet = new NetSessionConfig { UgsProjectId = ConfiguredProjectId };
            var configuredTransport = new LoopbackNetTransport();
            var configuredSession = new NetSession(
                configuredNet,
                configuredTransport,
                new ColonyMatchFactory(ColonyMap.V1(), new SimConfig(), new SnapshottingProfileStore()));

            configuredSession.StartHost(
                NewPeer(HostPeerId, HostAccount, HeroClass.Gunslinger, isHost: true));

            Assert.That(configuredSession.Config.UgsProjectId, Is.EqualTo(ConfiguredProjectId),
                "R-50: the id arrives through config and stays there");
            Assert.That(configuredTransport.ProjectId, Is.EqualTo(ConfiguredProjectId),
                "R-50: and is carried to the transport unchanged — this is what the Lobby/Relay "
                + "implementation authenticates with");
            Assert.That(configuredSession.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "R-50: supplying an id must not stop a loopback session from coming up");
        }

        // ==========================================================================================
        //  scenario builders
        // ==========================================================================================

        /// <summary>Everything a session is assembled from, so no test wires it twice.</summary>
        private sealed class LoopbackLobby
        {
            public SimConfig SimConfig;
            public NetSessionConfig NetConfig;
            public LoopbackNetTransport Transport;
            public SnapshottingProfileStore Profiles;
            public NetSession Session;
        }

        /// <summary>
        /// A two-player loopback lobby on the shipped v1 colony (R-10), hosted, with both class
        /// picks already made and <b>no UGS project id anywhere</b> — which is the state every test
        /// below starts from and is itself half of an acceptance criterion.
        ///
        /// Built from production types rather than through the golden fixture loader, following the
        /// convention T10 set: the loader is the adapter's contract with eval/golden, not a scenario
        /// builder.
        /// </summary>
        private static LoopbackLobby NewTwoPlayerLoopbackLobby()
        {
            var simConfig = new SimConfig();
            var profiles = new SnapshottingProfileStore();
            var netConfig = new NetSessionConfig();
            var transport = new LoopbackNetTransport();

            var session = new NetSession(
                netConfig,
                transport,
                new ColonyMatchFactory(ColonyMap.V1(), simConfig, profiles));

            session.StartHost(NewPeer(HostPeerId, HostAccount, HeroClass.Gunslinger, isHost: true));

            Assert.That(session.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "R-50: hosting opens a lobby");
            Assert.That(session.TryJoin(NewPeer(GuestPeerId, GuestAccount, HeroClass.Sawbones)), Is.True,
                "R-50: a second player joins the lobby");

            return new LoopbackLobby
            {
                SimConfig = simConfig,
                NetConfig = netConfig,
                Transport = transport,
                Profiles = profiles,
                Session = session,
            };
        }

        private static NetPeer NewPeer(string peerId, string accountId, string heroClass, bool isHost = false)
        {
            return new NetPeer
            {
                PeerId = peerId,
                AccountId = accountId,
                HeroClass = heroClass,
                IsHost = isHost,
            };
        }

        /// <summary>
        /// The hero an account is playing. Found by account rather than by id because no id format
        /// is contract — the PRD names none, and pinning one here would ship it as spec.
        /// </summary>
        private static Hero HeroFor(MatchState state, string accountId)
        {
            var heroes = state.Heroes.Values.Where(h => h.AccountId == accountId).ToList();

            Assert.That(heroes.Count, Is.EqualTo(1),
                "R-50 / R-31: exactly one hero must be on the field for account '" + accountId
                + "'; found " + heroes.Count + " (the match holds " + state.Heroes.Count
                + " hero(es) for " + state.Players.Count + " player slot(s))");

            return heroes[0];
        }

        /// <summary>The class this peer picked in the lobby, as the session still reports it (R-07).</summary>
        private static string ClassPickFor(NetSession session, string peerId)
        {
            var seat = session.Seats.FirstOrDefault(s => s.PeerId == peerId);

            Assert.That(seat, Is.Not.Null,
                "R-07: peer '" + peerId + "' must still hold a seat for its class pick to be retained");

            return seat.HeroClass;
        }

        /// <summary>
        /// Clears a wave through the sim's own command (R-02 / R-20), the way ticket 019 does. What
        /// killed each monster is not this ticket's business — the session's job starts once the
        /// wave is gone.
        /// </summary>
        private static void KillWave(HostedMatch match, IEnumerable<string> monsterIds, int bounty)
        {
            foreach (var id in monsterIds.ToList())
            {
                match.Sim.RecordMonsterKill(new MonsterKillRequest
                {
                    MonsterId = id,
                    MonsterType = match.State.Monsters.TryGetValue(id, out var monster) ? monster.Type : null,
                    Bounty = bounty,
                });
            }
        }

        /// <summary>
        /// R-03's early exit (G-017), used to move the campaign along without spending sixty
        /// sim-seconds in every planning phase.
        ///
        /// <b><paramref name="wave"/> is not optional and the guard on it is not defensive.</b> A
        /// wave clear leaves the phase at planning with the counter still on the wave that was just
        /// cleared (R-02/G-010), and <see cref="MatchSim.BeginPlanningPhase"/> is what advances it
        /// (G-016) — so readying up in *that* window starts combat for a wave the session has
        /// already fought, and the campaign stops dead with no monsters and nothing left to advance
        /// it. Readying only once the counter has reached the wave being waited for is the whole
        /// difference between a ten-wave drive and a hang.
        ///
        /// Idempotent otherwise, so it is safe to call every step: BeginPlanningPhase clears the
        /// ready flags each time it advances the counter, and this re-raises them.
        /// </summary>
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
        /// Ends a match the only way R-02 allows one to be lost: every shelter emptied. Driven
        /// through <see cref="MatchSim.ApplyHotspotAttack"/> rather than by walking a wave across
        /// the map, because the tests that use this are about what happens *after* a match ends —
        /// ticket 019 and the defeat test above already grade the walk.
        /// </summary>
        private static void EndTheMatchByEmptyingTheColony(HostedMatch match)
        {
            foreach (var hotspot in match.State.Hotspots.Values.ToList())
            {
                while (hotspot.Civilians > 0)
                {
                    match.Sim.ApplyHotspotAttack(new HotspotAttackRequest
                    {
                        AttackerId = "m_wipeout",
                        AttackerType = MonsterType.Shambler,
                        Damage = 1000.0,
                        TargetId = hotspot.Id,
                    });
                }
            }

            Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Defeat),
                "sanity (R-02 / G-008): emptying every shelter loses the match");
        }

        /// <summary>Whether a target id still resolves to something a monster could be walking at (R-16).</summary>
        private static bool TargetExistsInTheWorld(MatchState state, string targetId)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                return false;
            }

            return state.Heroes.ContainsKey(targetId)
                   || state.Hotspots.ContainsKey(targetId)
                   || state.Placeables.ContainsKey(targetId);
        }

        /// <summary>
        /// Drives the session until <paramref name="done"/> answers true or
        /// <paramref name="budgetSeconds"/> of sim time has elapsed <i>from now</i>. Bounded so a
        /// session that never progresses fails as a test failure rather than as a hung runner — the
        /// same reading T10's StepUntil and T19's DriveUntil take.
        /// </summary>
        private static bool DriveUntil(
            NetSession session,
            SimClock clock,
            Func<bool> done,
            double budgetSeconds,
            Action beforeEachStep = null)
        {
            var deadline = clock.ElapsedSeconds + budgetSeconds;
            var maxSteps = (int)(budgetSeconds / Step60Hz) + 64;

            for (var i = 0; i < maxSteps; i++)
            {
                if (done())
                {
                    return true;
                }

                if (beforeEachStep != null)
                {
                    beforeEachStep();
                }

                session.Step(Step60Hz);

                if (clock.ElapsedSeconds > deadline)
                {
                    break;
                }
            }

            return done();
        }

        // ==========================================================================================
        //  failure reports
        // ==========================================================================================

        /// <summary>
        /// Why the campaign stopped, written for whoever reads the red rather than for whoever wrote
        /// the test. It names the wave it stalled on first, because that is the one thing a ten-wave
        /// drive has to report and the one thing a bare "expected true" cannot.
        /// </summary>
        private static string DescribeStalledCampaign(
            NetSession session, HostedMatch match, int expectedWave, List<int> cleared)
        {
            var state = match.State;

            var sb = new StringBuilder();
            sb.Append("R-01/R-03: the campaign stalled on WAVE ").Append(expectedWave)
              .Append(" of ").Append(state.Wave.TotalWaves).Append('.').AppendLine();

            sb.Append("  cleared so far : ")
              .Append(cleared.Count == 0 ? "none" : string.Join(", ", cleared.Select(w => w.ToString(CultureInfo.InvariantCulture))))
              .AppendLine();

            sb.Append("  wave counter   : ").Append(state.Wave.Number)
              .Append(" (BeginPlanningPhase is the only thing that advances it, G-016)").AppendLine();

            sb.Append("  match          : phase '").Append(state.Phase).Append("', status '")
              .Append(state.Status).Append("', ").Append(state.Wave.LivingMonsterIds.Count)
              .Append(" living monster(s) on the roster, ").Append(state.Monsters.Count)
              .Append(" in the world").AppendLine();

            sb.Append("  session        : ").Append(session.Phase).Append(", ")
              .Append(state.Players.Count(p => p.Connected)).Append('/').Append(state.Players.Count)
              .Append(" player(s) connected, ")
              .Append(state.Players.Count(p => p.Ready)).Append(" ready (R-03's early exit)")
              .AppendLine();

            sb.Append("  colony         : ").Append(state.TotalCivilians).Append(" civilian(s) left [")
              .Append(string.Join(", ", state.Hotspots.Values
                  .OrderBy(h => h.Id, StringComparer.Ordinal)
                  .Select(h => h.Id + "=" + h.Civilians)))
              .Append(']').AppendLine();

            sb.Append("  sim time       : ").Append(Fmt(match.Clock.ElapsedSeconds))
              .Append("s (planning runs ").Append(Fmt(match.Sim.Config.PlanningDurationSeconds))
              .Append("s per wave, R-03)");

            return sb.ToString();
        }

        /// <summary>
        /// Why the colony is still standing, stage by stage in the order the chain runs — the same
        /// report ticket 019 writes, plus the two things this ticket adds: whether the session ever
        /// left the match, and whether the party is still connected.
        /// </summary>
        private static string DescribeStalledColony(
            NetSession session, HostedMatch match, int civiliansAtStart, int steps, int maxSteps)
        {
            var state = match.State;
            var living = state.Monsters.Values.Where(m => m.Alive).ToList();
            var targeted = living.Where(m => !string.IsNullOrEmpty(m.TargetId)).ToList();

            var sb = new StringBuilder();
            sb.Append("R-02: the colony never fell. Drove ").Append(steps).Append('/').Append(maxSteps)
              .Append(" host steps (").Append(Fmt(match.Clock.ElapsedSeconds))
              .Append("s of sim time); match status '").Append(state.Status).Append("', phase '")
              .Append(state.Phase).Append("', session '").Append(session.Phase).Append("'.")
              .AppendLine();

            sb.Append("  stage 1 session : ").Append(state.Players.Count(p => p.Connected)).Append('/')
              .Append(state.Players.Count).Append(" player(s) connected, ").Append(state.Heroes.Count)
              .Append(" hero(es) on the field").AppendLine();

            sb.Append("  stage 2 spawn   : ").Append(state.Monsters.Count).Append(" monster(s), ")
              .Append(living.Count).Append(" alive, on wave ").Append(state.Wave.Number).AppendLine();

            sb.Append("  stage 3 target  : ").Append(targeted.Count).Append('/').Append(living.Count)
              .Append(" living monster(s) hold a target (R-16)").AppendLine();

            sb.Append("  stage 4 damage  : civilians ").Append(civiliansAtStart).Append(" -> ")
              .Append(state.TotalCivilians).Append(" [")
              .Append(string.Join(", ", state.Hotspots.Values
                  .OrderBy(h => h.Id, StringComparer.Ordinal)
                  .Select(h => h.Id + "=" + h.Civilians)))
              .Append("] (R-18 gate then R-11 damage)").AppendLine();

            sb.Append("  stage 5 defeat  : R-02 fires on the hit that empties the last shelter, and "
                      + "the session then has to notice");

            return sb.ToString();
        }

        private static string Fmt(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        // ==========================================================================================
        //  test doubles
        // ==========================================================================================

        /// <summary>
        /// A profile store that can tell "saved" from "mutated" (R-43).
        ///
        /// <see cref="InMemoryProfileStore"/> cannot: it hands out the live object, so every
        /// in-place mutation is already visible through <c>Load</c> whether or not anything ever
        /// called <c>Save</c>, and an assertion driven off it would pass against a session that
        /// persists nothing at all. This one keeps the working profile the sim mutates separate
        /// from a <b>snapshot taken at each save</b>, which is what makes R-07's "account profiles
        /// persist" a claim about persistence rather than about object identity.
        /// </summary>
        private sealed class SnapshottingProfileStore : IProfileStore
        {
            private readonly Dictionary<string, AccountProfile> _working =
                new Dictionary<string, AccountProfile>(StringComparer.Ordinal);

            /// <summary>What the store was actually handed, by account. Cloned at save time.</summary>
            public readonly Dictionary<string, AccountProfile> Saved =
                new Dictionary<string, AccountProfile>(StringComparer.Ordinal);

            public AccountProfile Load(string accountId)
            {
                if (!_working.TryGetValue(accountId, out var profile))
                {
                    // R-44: an unknown callsign is simply a fresh account.
                    profile = new AccountProfile { AccountId = accountId };
                    _working[accountId] = profile;
                }

                return profile;
            }

            public void Save(AccountProfile profile)
            {
                _working[profile.AccountId] = profile;
                Saved[profile.AccountId] = profile.Clone();
            }
        }
    }
}
