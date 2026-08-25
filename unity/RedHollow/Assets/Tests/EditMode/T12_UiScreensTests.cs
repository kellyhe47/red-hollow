using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Game.Host;
using RedHollow.Game.Net;
using RedHollow.Game.UI;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 012 (T-12), part 1 of 2: the screens and the flow between them (R-60), the S1/S2
    /// screen contents, S5's interstitial data, S6/S7's post-match data, and the cross-cutting
    /// session states — host disconnect to S1 with an error, rematch back to S2 (DEC-RUN-11), and
    /// the non-pausing ESC menu (R-55). Part 2 (<see cref="T12_UiHudTests"/>) owns S3's planning
    /// screen and S4's combat HUD.
    ///
    /// The wireframe file is normative IN FULL (R-60): every screen and every listed state below
    /// is a requirement, not an illustration.
    ///
    /// <b>The architecture under test is the shell's standing one</b>: presenters are plain C#
    /// classes over the replicated <see cref="MatchState"/> and the <see cref="NetSession"/>,
    /// read-only over sim state (T-10's Cecil invariant scans every MonoBehaviour, so no rule and
    /// no write may live in a component — a UI MonoBehaviour may only mirror one of these models).
    /// Mutations are sim commands through the hosted match or session calls, never field writes.
    ///
    /// <b>What is deliberately not asserted</b>, because the PRD and the wireframe are silent and
    /// a guessed value would ship as spec: any toast/banner/error copy, colours, layout, the
    /// click-to-copy affordance, MAIN MENU's destination (the wireframe names the button and
    /// nothing else), and the exact length of S5's "~3s" hold — the router declares its own
    /// <see cref="UiRouter.InterstitialSeconds"/> and the tests hold it to sane bounds instead.
    /// </summary>
    [TestFixture]
    public class T12_UiScreensTests
    {
        private const double Step60Hz = 1.0 / 60.0;
        private const double SimTolerance = 1e-6;

        private const string HostPeerId = "peer_host";
        private const string GuestPeerId = "peer_guest";
        private const string HostAccount = "acc_calamity";
        private const string GuestAccount = "acc_doc";

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
        //  S1 — Title / Join
        // ==========================================================================================

        /// <summary>
        /// R-44 / R-60 — the callsign IS the account: typing one loads the server-side profile
        /// behind it and S1 shows lifetime level + XP once loaded.
        /// </summary>
        [Test]
        public void The_title_screen_shows_the_loaded_profiles_lifetime_level_and_xp()
        {
            var profiles = new InMemoryProfileStore();
            profiles.Seed(new AccountProfile
            {
                AccountId = HostAccount,
                LifetimeXp = 350.0,
                Level = 3,
                SkillPoints = 2,
            });

            var title = new TitleScreenModel(profiles);
            title.SetCallsign(HostAccount);

            Assert.That(title.Callsign, Is.EqualTo(HostAccount), "R-44: the callsign as typed");
            Assert.That(title.ProfileLoaded, Is.True, "R-44: the profile behind the callsign loads");
            Assert.That(title.Level, Is.EqualTo(3),
                "R-41 / R-60: S1 shows the lifetime level once loaded");
            Assert.That(title.LifetimeXp, Is.EqualTo(350.0).Within(SimTolerance),
                "R-40 / R-60: and the lifetime XP");
        }

        /// <summary>
        /// R-44 — an unknown callsign is simply a fresh account (v1: no password), never an error:
        /// level 1, zero XP, and no error text anywhere.
        /// </summary>
        [Test]
        public void An_unknown_callsign_is_a_fresh_account_and_not_an_error()
        {
            var title = new TitleScreenModel(new InMemoryProfileStore());
            title.SetCallsign("acc_never_seen_before");

            Assert.That(title.ProfileLoaded, Is.True,
                "R-44: an unknown callsign loads as a fresh account rather than failing");
            Assert.That(title.Level, Is.EqualTo(1), "R-41: a fresh account is level 1");
            Assert.That(title.LifetimeXp, Is.EqualTo(0.0).Within(SimTolerance),
                "R-40: with zero lifetime XP");
            Assert.That(title.JoinError, Is.Null,
                "an unknown callsign is not a join failure and must raise no error");
        }

        /// <summary>
        /// R-60, wireframe S1 state: "join failed (bad/expired code) → inline error under code
        /// input, stay on screen". The error's presence is contract, its copy is not; editing the
        /// code clears it, because a stale error under a corrected code blames the wrong input.
        /// </summary>
        [Test]
        public void A_failed_join_raises_an_inline_error_and_stays_on_the_title_screen()
        {
            var lobby = NewHostedLobby();
            var router = new UiRouter(NewOfflineSession());
            router.Update();
            Assert.That(router.Screen, Is.EqualTo(UiScreen.Title),
                "R-60: before anything is joined or hosted, the player is on S1");

            var title = new TitleScreenModel(new InMemoryProfileStore());
            title.SetCallsign(GuestAccount);
            title.SetJoinCodeInput("BADCOD");
            Assert.That(title.JoinError, Is.Null, "typing a code is not yet a failure");

            title.NoteJoinFailed();

            Assert.That(title.JoinError, Is.Not.Null.And.Not.Empty,
                "R-60: a bad/expired code puts an inline error under the code input "
                + "(its wording is presentation and is not asserted)");

            router.Update();
            Assert.That(router.Screen, Is.EqualTo(UiScreen.Title),
                "R-60: a failed join STAYS on S1 — no navigation happens");

            title.SetJoinCodeInput("ABC123");
            Assert.That(title.JoinError, Is.Null,
                "editing the code clears the inline error — a stale error under a corrected "
                + "code is blaming the wrong input");

            // Anti-vacuity: the code the player would join with really is refused right now —
            // the hosted party is mid-match, which R-53 refuses.
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "a match starts");
            Assert.That(
                lobby.Session.TryJoin(NewPeer("peer_late", "acc_late", HeroClass.Rancher)),
                Is.False,
                "R-53: the join the error above reports really is a refusable thing");
        }

        // ==========================================================================================
        //  R-60 — the screen flow: S1 → S2 → S3/S4 → S5 → … → S6/S7 → S2
        // ==========================================================================================

        /// <summary>
        /// R-60 — the front half of the flow, and the trap that motivates a router at all:
        /// <see cref="MatchStatus.InProgress"/> and <see cref="MatchPhase.Combat"/> are BOTH the
        /// literal string "combat" on two different fields. A router that read the wrong one would
        /// show a victory screen for every live combat phase, or a combat HUD over a won match.
        /// </summary>
        [Test]
        public void The_router_walks_title_to_lobby_to_the_match_screens()
        {
            var lobby = NewTwoPlayerLobby();
            var router = new UiRouter(lobby.Session);

            // NewTwoPlayerLobby has already hosted, so the session starts on S2.
            router.Update();
            Assert.That(router.Screen, Is.EqualTo(UiScreen.Lobby),
                "R-60: a hosted session is on S2");
            Assert.That(router.TitleError, Is.Null,
                "nothing has gone wrong, so S1 has no error waiting");

            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the match starts");
            var state = lobby.Session.Match.State;

            router.Update();

            // The mapping is phase-driven, stated for whichever phase the match opened in.
            var expected = state.Phase == MatchPhase.Planning ? UiScreen.Planning : UiScreen.Combat;
            Assert.That(router.Screen, Is.EqualTo(expected),
                "R-60: in a live match the screen follows MatchState.Phase — '" + state.Phase
                + "' maps to " + expected);

            Assert.That(state.Status, Is.EqualTo(MatchStatus.InProgress),
                "sanity: the status field also reads '" + MatchStatus.InProgress + "' here");
            Assert.That(router.Screen, Is.Not.EqualTo(UiScreen.Victory).And.Not.EqualTo(UiScreen.Defeat),
                "R-60: a status that spells 'combat' is a LIVE match — phase and status are "
                + "different fields that happen to share a literal, and the router must not "
                + "conflate them");
        }

        /// <summary>
        /// R-04 / R-60 — S5: a cleared (non-final) wave shows the interstitial, holds for the
        /// router's own declared ~3s, and falls back to S3. The hold is the shell's schedule, so
        /// the router declares it rather than every caller guessing; it must be positive and
        /// shorter than R-03's planning window, or the interstitial would eat the phase it
        /// decorates.
        /// </summary>
        [Test]
        public void A_cleared_wave_shows_the_interstitial_and_then_falls_back_to_planning()
        {
            var lobby = NewTwoPlayerLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the match starts");
            var match = lobby.Session.Match;

            var router = new UiRouter(lobby.Session);
            router.Update();

            Assert.That(router.InterstitialSeconds, Is.GreaterThan(0.0),
                "R-04: the interstitial is a hold, not a flicker");
            Assert.That(router.InterstitialSeconds, Is.LessThan(lobby.SimConfig.PlanningDurationSeconds),
                "R-04 / R-03: the hold must end well inside the planning window it overlaps");

            // Clear wave 1. The sim's own wave_complete is what the adapter forwards.
            var wave = match.State.Wave.Number;
            KillWave(match, match.State.Wave.LivingMonsterIds.ToList(), bounty: 0);

            router.OnSimEvent(new SimEvent("wave_complete", new Dictionary<string, object>
            {
                { "wave", wave },
            }));
            router.Update();

            Assert.That(router.Screen, Is.EqualTo(UiScreen.WaveInterstitial),
                "R-04 / R-60: a cleared wave shows S5");

            // The session keeps running underneath (the sim never pauses for a banner): drive it
            // past the declared hold and the router must be back on S3.
            var holdSteps = (int)Math.Ceiling(router.InterstitialSeconds / Step60Hz) + 2;
            for (var i = 0; i < holdSteps; i++)
            {
                lobby.Session.Step(Step60Hz);
                router.Update();
            }

            Assert.That(router.Screen, Is.EqualTo(UiScreen.Planning),
                "R-04 / R-60: after the ~3s hold, S5 falls back to S3 for the next wave");
            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Planning),
                "sanity: the sim really is planning the next wave underneath");
            Assert.That(match.State.Wave.Number, Is.EqualTo(wave + 1),
                "sanity (G-016): the campaign moved to the next wave");
        }

        /// <summary>
        /// R-04 / R-60 — S5's contents: "WAVE n CLEARED · bounty earned this wave · civilians
        /// remaining X/Y". Exactly <see cref="MatchSim.WaveSummary"/>'s answer: the bounty is the
        /// wave's own takings, NOT the shared pool — the pool opened on R-20's 500 stake and would
        /// dwarf the banner.
        /// </summary>
        [Test]
        public void The_interstitial_shows_this_waves_bounty_and_the_civilians_remaining()
        {
            var lobby = NewTwoPlayerLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the match starts");
            var match = lobby.Session.Match;

            var civiliansAtStart = match.State.TotalCivilians;
            Assert.That(civiliansAtStart, Is.EqualTo(20), "sanity (R-10): the v1 colony holds 20");

            var wave = match.State.Wave.Number;
            var roster = match.State.Wave.LivingMonsterIds.ToList();
            KillWave(match, roster, bounty: 10);
            var expectedBounty = roster.Count * 10;

            var interstitial = new WaveInterstitialModel(match, civiliansAtStart);
            interstitial.Refresh();

            Assert.That(interstitial.Wave, Is.EqualTo(wave),
                "R-04: the banner names the wave that just cleared");
            Assert.That(interstitial.BountyEarned, Is.EqualTo(expectedBounty),
                "R-04: bounty earned THIS wave — the sum of the wave's kills");
            Assert.That(interstitial.BountyEarned, Is.Not.EqualTo(match.State.Team.Scrip),
                "R-04: and not the shared pool, which carries R-20's opening stake");
            Assert.That(interstitial.CiviliansRemaining, Is.EqualTo(match.State.TotalCivilians),
                "R-04: civilians remaining is the colony's live total");
            Assert.That(interstitial.CiviliansAtStart, Is.EqualTo(civiliansAtStart),
                "R-04: over the full-population denominator");
        }

        // ==========================================================================================
        //  S6 — Victory (and rematch back to S2)
        // ==========================================================================================

        /// <summary>
        /// R-01 / R-07 / R-60 — the back half of the flow: a won ten-wave match lands on S6, S6
        /// keys off the STATUS field (the phase of a won match still reads "combat" forever — there
        /// is no eleventh planning phase), PLAY AGAIN is host-only, and DEC-RUN-11 returns the
        /// whole party to S2 with the same join code.
        /// </summary>
        [Test]
        public void A_won_match_lands_on_victory_and_play_again_returns_the_party_to_the_lobby()
        {
            var lobby = NewTwoPlayerLobby();
            var joinCode = lobby.Session.JoinCode;
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the match starts");
            var match = lobby.Session.Match;

            WinTheCampaign(lobby, match);

            var reachedPostMatch = DriveUntil(
                lobby.Session, match.Clock,
                () => lobby.Session.Phase == NetSessionPhase.PostMatch, budgetSeconds: 5.0);
            Assert.That(reachedPostMatch, Is.True, "the won match reaches the post-match phase");

            var router = new UiRouter(lobby.Session);
            router.Update();

            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Combat),
                "sanity: a won match's PHASE still spells 'combat' — which is why the router "
                + "must read the status");
            Assert.That(router.Screen, Is.EqualTo(UiScreen.Victory),
                "R-60: a won match is S6, keyed off MatchState.Status");

            var stats = new MatchStatsTracker(lobby.SimConfig.Placeables);
            var hostScreen = new PostMatchModel(lobby.Session, HostPeerId, stats, 20);
            var guestScreen = new PostMatchModel(lobby.Session, GuestPeerId, stats, 20);

            Assert.That(hostScreen.IsVictory, Is.True, "S6: 'THE COLONY STANDS'");
            Assert.That(hostScreen.CiviliansSaved, Is.EqualTo(match.State.TotalCivilians),
                "S6: civilians saved is the live total the colony kept");
            Assert.That(hostScreen.CiviliansSaved, Is.GreaterThan(0),
                "R-02: a victory is a colony that survived");
            Assert.That(hostScreen.CiviliansAtStart, Is.EqualTo(20), "S6: out of 20");

            // R-07 — host-only, refusals change nothing.
            Assert.That(guestScreen.CanRematch, Is.False, "R-07: PLAY AGAIN is host-only");
            Assert.That(guestScreen.RequestRematch(), Is.False, "R-07: and a guest's click is refused");
            router.Update();
            Assert.That(router.Screen, Is.EqualTo(UiScreen.Victory),
                "R-07: a refused rematch leaves everyone on S6");

            Assert.That(hostScreen.CanRematch, Is.True, "R-07: the host may PLAY AGAIN");
            Assert.That(hostScreen.RequestRematch(), Is.True, "R-07: and the click lands");

            router.Update();
            Assert.That(router.Screen, Is.EqualTo(UiScreen.Lobby),
                "DEC-RUN-11: PLAY AGAIN returns the party to S2, not into a new match");
            Assert.That(lobby.Session.JoinCode, Is.EqualTo(joinCode),
                "R-07: the SAME lobby — same join code on the S2 the party lands on");
        }

        // ==========================================================================================
        //  S7 — Defeat
        // ==========================================================================================

        /// <summary>
        /// R-02 / R-60 — S7: a fallen colony is the defeat screen, "reached wave N" is the wave the
        /// campaign died on (mid-campaign, because R-02 is the only loss rule), and zero civilians
        /// were saved by definition.
        /// </summary>
        [Test]
        public void A_fallen_colony_lands_on_the_defeat_screen_with_the_reached_wave()
        {
            var lobby = NewTwoPlayerLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the match starts");
            var match = lobby.Session.Match;

            EndTheMatchByEmptyingTheColony(match);

            var reachedPostMatch = DriveUntil(
                lobby.Session, match.Clock,
                () => lobby.Session.Phase == NetSessionPhase.PostMatch, budgetSeconds: 5.0);
            Assert.That(reachedPostMatch, Is.True, "the lost match reaches the post-match phase");

            var router = new UiRouter(lobby.Session);
            router.Update();
            Assert.That(router.Screen, Is.EqualTo(UiScreen.Defeat),
                "R-60: a lost match is S7, keyed off MatchState.Status");

            var screen = new PostMatchModel(
                lobby.Session, HostPeerId, new MatchStatsTracker(lobby.SimConfig.Placeables), 20);

            Assert.That(screen.IsVictory, Is.False, "S7: 'THE COLONY HAS FALLEN'");
            Assert.That(screen.ReachedWave, Is.EqualTo(match.State.Wave.Number),
                "S7: 'reached wave N' is the wave the colony fell on");
            Assert.That(screen.ReachedWave, Is.LessThan(match.State.Wave.TotalWaves),
                "R-02: defeat arrives mid-campaign, not by running out of waves");
            Assert.That(screen.CiviliansSaved, Is.EqualTo(0),
                "R-02: the colony was emptied — that is what defeat IS");
            Assert.That(screen.CanRematch, Is.True,
                "R-07: RETRY has the same semantics as PLAY AGAIN, host-only included");
        }

        /// <summary>
        /// S6/S7's stats table: kills per player and scrip spent. The sim keeps neither tally, so
        /// the tracker counts the host's own broadcast — one `xp_awarded` per credited kill
        /// (R-40 awards the killer per kill) and one `placeable_created` per accepted purchase,
        /// priced off the R-23 catalog rather than any number typed here.
        /// </summary>
        [Test]
        public void The_stats_table_is_counted_from_the_event_stream()
        {
            var catalog = new SimConfig().Placeables;
            var tracker = new MatchStatsTracker(catalog);

            tracker.OnSimEvent(XpAwarded("hero_host", 60));
            tracker.OnSimEvent(XpAwarded("hero_host", 25));
            tracker.OnSimEvent(XpAwarded("hero_host", 40));
            tracker.OnSimEvent(XpAwarded("hero_guest", 80));

            tracker.OnSimEvent(PlaceableCreated(PlaceableType.Barricade));
            tracker.OnSimEvent(PlaceableCreated(PlaceableType.Turret));

            // An event the tracker has no business counting.
            tracker.OnSimEvent(new SimEvent("combat_started", new Dictionary<string, object>()));

            Assert.That(tracker.KillsBy("hero_host"), Is.EqualTo(3),
                "S6: kills per player — one per kill credited to the hero");
            Assert.That(tracker.KillsBy("hero_guest"), Is.EqualTo(1), "for every player");
            Assert.That(tracker.KillsBy("hero_nobody"), Is.EqualTo(0),
                "a hero with no kills shows zero, not an error");

            var expectedSpend = catalog.StatsFor(PlaceableType.Barricade).Cost
                                + catalog.StatsFor(PlaceableType.Turret).Cost;
            Assert.That(tracker.ScripSpent, Is.EqualTo(expectedSpend),
                "S6: scrip spent is the catalog price of everything bought — the event names the "
                + "type and the R-23 catalog names the price");
        }

        // ==========================================================================================
        //  S2 — Lobby
        // ==========================================================================================

        /// <summary>
        /// R-60, wireframe S2: the join code to share, the player list (name · class · ready), the
        /// waiting-alone hint, and "player joins/leaves mid-lobby → list updates".
        /// </summary>
        [Test]
        public void The_lobby_screen_mirrors_the_party_and_updates_on_join_and_leave()
        {
            var lobby = NewHostedLobby();
            var model = new LobbyScreenModel(lobby.Session, HostPeerId);
            model.Update();

            Assert.That(model.JoinCode, Is.EqualTo(lobby.Session.JoinCode),
                "S2: the code shown is the session's, so the one on screen is the one that works");
            Assert.That(model.WaitingAlone, Is.True,
                "S2: one seat → the 'share code' hint state");
            Assert.That(model.Seats.Count, Is.EqualTo(1), "one row for the host");

            Assert.That(
                lobby.Session.TryJoin(NewPeer(GuestPeerId, GuestAccount, HeroClass.Sawbones)),
                Is.True, "a guest joins mid-lobby");
            model.Update();

            Assert.That(model.Seats.Count, Is.EqualTo(2),
                "S2: a join updates the list");
            Assert.That(model.WaitingAlone, Is.False, "and ends the waiting-alone state");

            var guestRow = model.Seats.FirstOrDefault(s => s.PeerId == GuestPeerId);
            Assert.That(guestRow, Is.Not.Null, "the joiner has a row");
            Assert.That(guestRow.AccountId, Is.EqualTo(GuestAccount), "S2: name (callsign)");
            Assert.That(guestRow.HeroClass, Is.EqualTo(HeroClass.Sawbones), "S2: class picked");
            Assert.That(guestRow.Ready, Is.False, "S2: ready starts unticked");

            lobby.Session.Disconnect(GuestPeerId);
            model.Update();

            Assert.That(model.Seats.Count, Is.EqualTo(1),
                "S2: a leave updates the list too");
            Assert.That(model.WaitingAlone, Is.True, "back to the hint state");
        }

        /// <summary>
        /// R-31 / R-60, wireframe S2: "Duplicate classes ALLOWED." The pick is never blocked by
        /// somebody else holding the same card.
        /// </summary>
        [Test]
        public void Duplicate_class_picks_are_allowed()
        {
            var lobby = NewTwoPlayerLobby();
            var host = new LobbyScreenModel(lobby.Session, HostPeerId);
            host.Update();

            // The guest already picked Sawbones (NewTwoPlayerLobby). The host picks it too.
            Assert.That(host.CanPick(HeroClass.Sawbones), Is.True,
                "S2: a taken class is still pickable — duplicates are allowed");

            host.PickClass(HeroClass.Sawbones);
            host.Update();

            var classes = lobby.Session.Seats.Select(s => s.HeroClass).ToList();
            Assert.That(classes, Is.EquivalentTo(new[] { HeroClass.Sawbones, HeroClass.Sawbones }),
                "S2: both seats hold the same class and nobody was bumped off it");
        }

        /// <summary>
        /// R-03 / R-60, wireframe S2: "match starts when ALL connected players ready" — no host
        /// force-start, so one ready of two is a lobby that waits, and the second ready is what
        /// starts the match.
        /// </summary>
        [Test]
        public void The_match_starts_when_all_connected_players_are_ready_and_not_before()
        {
            var lobby = NewTwoPlayerLobby();
            var host = new LobbyScreenModel(lobby.Session, HostPeerId);
            host.Update();

            Assert.That(host.ConnectedCount, Is.EqualTo(2), "S2: two connected players");
            Assert.That(host.ReadyCount, Is.EqualTo(0), "nobody is ready yet");

            host.SetReady(true);
            host.Update();

            Assert.That(host.ReadyCount, Is.EqualTo(1), "S2: ready 1/2");
            Assert.That(host.AllReady, Is.False, "one of two is not all");
            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "S2: the match must NOT start on a partial ready — there is no host force-start");

            host.NotePeerReady(GuestPeerId, true);
            host.Update();

            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.InMatch),
                "S2: everyone connected is ready, so the match starts");
            Assert.That(lobby.Session.Match.State.Heroes.Count, Is.EqualTo(2),
                "R-50: and it is the whole party's match");
        }

        /// <summary>R-50 / R-60 — "a solo lobby needs only your ready". Solo is a party of one.</summary>
        [Test]
        public void A_solo_lobby_needs_only_your_own_ready()
        {
            var lobby = NewHostedLobby();
            var model = new LobbyScreenModel(lobby.Session, HostPeerId);
            model.Update();

            Assert.That(model.ConnectedCount, Is.EqualTo(1), "a party of one");

            model.SetReady(true);
            model.Update();

            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.InMatch),
                "R-50 / DEC-020: solo is a 1-player lobby — your own ready starts the match");
        }

        // ==========================================================================================
        //  Cross-cutting — host disconnect → S1 with an error (DEC-RUN-10)
        // ==========================================================================================

        /// <summary>
        /// R-53 / DEC-RUN-10 — the host left: the session ends, everyone lands on S1, and S1 says
        /// why. The sharp half is what does NOT happen: the match status stays in-progress (an
        /// abandoned match is not a defeat, R-02 owns the only one) — and the router must land on
        /// Title anyway, off the SESSION's end rather than any match field.
        /// </summary>
        [Test]
        public void A_host_disconnect_lands_on_the_title_screen_with_an_error()
        {
            var lobby = NewTwoPlayerLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the match starts");
            var match = lobby.Session.Match;

            var router = new UiRouter(lobby.Session);
            router.Update();
            Assert.That(router.TitleError, Is.Null, "no error while everything is fine");

            lobby.Session.Disconnect(HostPeerId);
            router.Update();

            Assert.That(router.Screen, Is.EqualTo(UiScreen.Title),
                "R-53 / DEC-RUN-10: the host leaving ends the match for all → back to S1");
            Assert.That(router.TitleError, Is.Not.Null.And.Not.Empty,
                "R-53: with an error message (its wording is presentation and is not asserted)");

            Assert.That(match.State.Status, Is.EqualTo(MatchStatus.InProgress),
                "DEC-RUN-10: the match status stays in-progress — the router landed on S1 off "
                + "the session's Ended, and a router reading the status would still be showing "
                + "the combat HUD of an abandoned match");
        }

        // ==========================================================================================
        //  Cross-cutting — the ESC menu is a non-pausing overlay, not a screen (R-55)
        // ==========================================================================================

        /// <summary>
        /// R-55 — "ESC opens overlay menu (leave match, volume) without pausing sim". Three claims:
        /// the overlay is not a screen (S4 stays underneath), the world keeps moving with it open,
        /// and <c>Time.timeScale</c> — the one-line way to get this wrong, which desyncs a
        /// host-authoritative party — is never touched.
        /// </summary>
        [Test]
        public void The_esc_menu_overlays_the_combat_screen_without_pausing_anything()
        {
            var lobby = NewTwoPlayerLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the match starts");
            var match = lobby.Session.Match;

            var router = new UiRouter(lobby.Session);
            lobby.Session.Step(Step60Hz);
            router.Update();
            Assert.That(router.Screen, Is.EqualTo(UiScreen.Combat), "the party is on S4");

            var walker = match.State.Monsters.Values.FirstOrDefault(
                m => m.Alive && !string.IsNullOrEmpty(m.TargetId));
            Assert.That(walker, Is.Not.Null, "sanity (R-16): a targeted monster to watch");
            var posBefore = walker.Pos;
            var clockBefore = match.Clock.ElapsedSeconds;

            router.SetEscMenuOpen(true);
            Assert.That(router.EscMenuOpen, Is.True, "R-55: ESC opens the menu");
            Assert.That(lobby.Session.IsOverlayOpen, Is.True,
                "R-55: through the session's own overlay flag");

            const int Steps = 120;
            for (var i = 0; i < Steps; i++)
            {
                lobby.Session.Step(Step60Hz);
            }

            router.Update();
            Assert.That(router.Screen, Is.EqualTo(UiScreen.Combat),
                "R-55: the menu is an overlay — S4 is still the screen underneath");
            Assert.That(match.Clock.ElapsedSeconds,
                Is.EqualTo(clockBefore + (Steps * Step60Hz)).Within(SimTolerance),
                "R-55: sim time advances by every delta while the menu is open");
            Assert.That(posBefore.DistanceTo(walker.Pos), Is.GreaterThan(0.0),
                "R-55: the world keeps moving under the open menu");
            Assert.That(Time.timeScale, Is.EqualTo(1f),
                "R-55: never by way of Time.timeScale");

            router.SetEscMenuOpen(false);
            Assert.That(router.EscMenuOpen, Is.False, "and ESC closes it again");
            Assert.That(lobby.Session.IsOverlayOpen, Is.False, "both ways through the session");
        }

        // ==========================================================================================
        //  scenario builders (the T11 conventions, verbatim where they apply)
        // ==========================================================================================

        private sealed class Lobby
        {
            public SimConfig SimConfig;
            public InMemoryProfileStore Profiles;
            public NetSession Session;
        }

        private static NetSession NewOfflineSession()
        {
            return new NetSession(
                new NetSessionConfig(),
                new LoopbackNetTransport(),
                new ColonyMatchFactory(ColonyMap.V1(), new SimConfig(), new InMemoryProfileStore()));
        }

        private static Lobby NewHostedLobby()
        {
            var simConfig = new SimConfig();
            var profiles = new InMemoryProfileStore();
            var session = new NetSession(
                new NetSessionConfig(),
                new LoopbackNetTransport(),
                new ColonyMatchFactory(ColonyMap.V1(), simConfig, profiles));

            session.StartHost(NewPeer(HostPeerId, HostAccount, HeroClass.Gunslinger, isHost: true));
            Assert.That(session.Phase, Is.EqualTo(NetSessionPhase.Lobby), "R-50: hosting opens a lobby");

            return new Lobby { SimConfig = simConfig, Profiles = profiles, Session = session };
        }

        private static Lobby NewTwoPlayerLobby()
        {
            var lobby = NewHostedLobby();
            Assert.That(
                lobby.Session.TryJoin(NewPeer(GuestPeerId, GuestAccount, HeroClass.Sawbones)),
                Is.True, "R-50: a second player joins the lobby");
            return lobby;
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

        /// <summary>T11's campaign walk, condensed: clear all ten waves through the sim's own commands.</summary>
        private static void WinTheCampaign(Lobby lobby, HostedMatch match)
        {
            var totalWaves = match.State.Wave.TotalWaves;

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

                Assert.That(arrived, Is.True,
                    "the campaign must reach wave " + expected + " (it is on wave "
                    + match.State.Wave.Number + ", phase '" + match.State.Phase + "')");

                KillWave(match, match.State.Wave.LivingMonsterIds.ToList(), bounty: 0);
            }

            Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Victory),
                "sanity (R-01 / G-011): clearing the final wave wins the map");
        }

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

        private static SimEvent XpAwarded(string heroId, int amount)
        {
            return new SimEvent("xp_awarded", new Dictionary<string, object>
            {
                { "hero_id", heroId },
                { "amount", (double)amount },
            });
        }

        private static SimEvent PlaceableCreated(string placeableType)
        {
            return new SimEvent("placeable_created", new Dictionary<string, object>
            {
                { "placeable_type", placeableType },
                { "pos", new Vec2(1.0, 1.0) },
                { "by", "p1" },
            });
        }
    }
}
