using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RedHollow.Game.Host;
using RedHollow.Game.Net;
using RedHollow.Sim;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 014 (T-14): the session half of the locked v1 non-goals — the R-70 clauses that live
    /// on <see cref="NetSession"/> (no host migration, no mid-match join, no spectator seat beyond
    /// the dead-hero cam) and R-71's rematch clause (scrip resets every match; only XP/levels
    /// persist). The sim half — no PvP, no currency on the persistent profile, single map, no
    /// boss, no difficulty knob — is guarded in the dotnet suite (T14_NonGoalGuardTests /
    /// T14_WaveTablePinTests), which this assembly's sources cannot see.
    ///
    /// <b>Expected green on arrival — that is the point.</b> A non-goal has no behaviour to
    /// fixture, so nothing else in the suite goes red when one quietly ships. Each guard here is
    /// written so the first commit that adds the feature fails a test that names the non-goal it
    /// broke.
    ///
    /// <b>Non-vacuity, per guard.</b> Behavioural guards carry an anti-vacuity arm: the refused
    /// operation is shown accepted in the situation v1 DOES allow, so a refusal cannot be a broken
    /// harness refusing everything. Reflection guards scan the real member surface (floor-checked,
    /// with the members the surface is KNOWN to carry asserted present) and first prove their
    /// pattern against the names the feature would plausibly arrive under.
    ///
    /// What T11 already pins is not re-pinned wholesale: T11 covers the in-match join refusal, the
    /// host-disconnect end state and the basic rematch reset. This file adds the guards those
    /// leave open — the surface scans, the ended-session-stays-dead-for-every-verb arm, the
    /// post-match join refusal, and the rematch-stake pin driven from a pool moved by real kills.
    /// </summary>
    [TestFixture]
    public class T14_NonGoalTests
    {
        private const double Step60Hz = 1.0 / 60.0;

        private const string HostPeerId = "peer_host_t14";
        private const string GuestPeerId = "peer_guest_t14";
        private const string HostAccount = "acc_t14_host";
        private const string GuestAccount = "acc_t14_guest";

        // ======================================================================================
        //  R-70 — no host migration
        // ======================================================================================

        /// <summary>
        /// R-70 / R-53. The session surface names no host-migration operation. T11 pins the
        /// behaviour that exists (host leaves → Ended, and driving on does not resurrect it); this
        /// scan is the tripwire for a migration API arriving BESIDE that behaviour under its own
        /// name — the shape v1.1 work would naturally take.
        /// </summary>
        [Test]
        public void The_session_surface_names_no_host_migration()
        {
            AssertNoPublicMemberMatches(
                new[] { typeof(NetSession), typeof(NetPeer), typeof(NetSessionConfig) },
                pattern: "migrat|promote|reassign|handover|takeover|electhost|successor",
                minimumMembersScanned: 15,
                why: "R-70: no host migration in v1 — the host leaving ends the match (R-53)",
                wouldMatch: new[]
                {
                    "MigrateHost", "TryMigrateHost", "PromoteToHost", "ReassignHost",
                    "HostHandover", "ElectHostSuccessor",
                });

            // The scan is looking at the real session type: the members R-53/R-07 DO require are
            // present on the surface it just swept.
            var sessionMembers = PublicMemberNames(typeof(NetSession));
            Assert.That(sessionMembers, Does.Contain("Disconnect"), "sanity: scanning the real NetSession");
            Assert.That(sessionMembers, Does.Contain("TryRematch"), "sanity: scanning the real NetSession");
        }

        /// <summary>
        /// R-70 / R-53 — the behavioural half: once the host has left, NO verb revives the
        /// session. The surviving guest cannot start a match, cannot rematch, nobody new can join,
        /// and the session cannot be re-hosted in place — each of which is a migration wearing a
        /// different verb ("the guest carries on hosting" is exactly what "no host migration"
        /// rules out). Every refusal is anti-vacuous against T11's green path: the same verbs from
        /// the same seats succeed while the session is alive.
        /// </summary>
        [Test]
        public void An_ended_session_is_dead_to_every_verb()
        {
            var lobby = NewTwoPlayerLoopbackLobby();

            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True,
                "anti-vacuity: the host CAN start a match while the session is alive");

            lobby.Session.Disconnect(HostPeerId);
            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.Ended),
                "sanity (R-53): the host leaving ends the session");

            Assert.That(lobby.Session.TryStartMatch(GuestPeerId), Is.False,
                "R-70: the surviving guest may not start a match from an ended session — that is "
                + "the guest becoming the host, i.e. a migration");
            Assert.That(lobby.Session.TryRematch(GuestPeerId), Is.False,
                "R-70: nor rematch it back to life");
            Assert.That(lobby.Session.TryJoin(NewPeer("peer_new", "acc_new", HeroClass.Rancher)),
                Is.False,
                "R-53/R-70: nobody joins an ended session — there is nothing to return to");

            Assert.That(
                () => lobby.Session.StartHost(NewPeer(GuestPeerId, GuestAccount, HeroClass.Sawbones, isHost: true)),
                Throws.InvalidOperationException,
                "R-70: an ended session cannot be re-hosted in place — a new party is a new "
                + "session, not this one resurrected under a new host");

            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.Ended),
                "R-70: and after every refused verb the session is still ended");
        }

        // ======================================================================================
        //  R-70 — no mid-match join (the half T11 leaves open)
        // ======================================================================================

        /// <summary>
        /// R-70 / R-53. T11 pins the in-match refusal; this pins the post-match screen, which is
        /// the other non-lobby phase a joiner can knock on — and the one a "let people join for
        /// the next round" convenience would open first. v1's rule is that joins happen in the
        /// LOBBY, so the anti-vacuity arm is the same peer being welcomed the moment the host's
        /// rematch returns the party there.
        /// </summary>
        [Test]
        public void A_join_on_the_post_match_screen_is_refused_until_the_rematch_reopens_the_lobby()
        {
            var lobby = NewTwoPlayerLoopbackLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");

            EndTheMatchByEmptyingTheColony(lobby.Session.Match);
            var reachedPostMatch = DriveUntil(
                lobby.Session, lobby.Session.Match.Clock,
                () => lobby.Session.Phase == NetSessionPhase.PostMatch, budgetSeconds: 5.0);
            Assert.That(reachedPostMatch, Is.True, "the finished match reaches the post-match screen");

            var knocker = NewPeer("peer_knocker", "acc_knocker", HeroClass.Rancher);

            var noticesBefore = lobby.Session.Notices.Count;
            Assert.That(lobby.Session.TryJoin(knocker), Is.False,
                "R-70/R-53: the post-match screen is not a lobby — joins are refused there too, "
                + "not just mid-match");
            Assert.That(lobby.Session.Seats.Count, Is.EqualTo(2),
                "a refused join seats nobody");
            Assert.That(lobby.Session.Notices.Count, Is.GreaterThan(noticesBefore),
                "R-53: the refusal is surfaced");
            Assert.That(lobby.Session.Notices.Last().Kind, Is.EqualTo(SessionNoticeKind.JoinRefused),
                "as a refused join (wording not asserted)");

            Assert.That(lobby.Session.TryRematch(HostPeerId), Is.True, "the host rematches");
            Assert.That(lobby.Session.TryJoin(knocker), Is.True,
                "anti-vacuity: the SAME peer is welcome the moment the party is back in the lobby "
                + "— so the refusal above was about the phase, not about this peer or a full party");
        }

        // ======================================================================================
        //  R-70 — no spectator beyond the dead-hero cam
        // ======================================================================================

        /// <summary>
        /// R-70. No spectator seat, slot, role or mode anywhere on the session surface. The
        /// dead-hero cam (R-33: a dead hero spectates a living ally) is a HERO state, not a
        /// session seat — so a spectator concept has exactly one place to arrive: the party/session
        /// types, where a seat kind or a spectators list would have to live for anyone to occupy
        /// it. <see cref="PlayerSlot"/> is swept with them because a slot-level role flag is the
        /// other likely shape.
        /// </summary>
        [Test]
        public void The_session_surface_has_no_spectator_concept()
        {
            AssertNoPublicMemberMatches(
                new[]
                {
                    typeof(NetSession), typeof(NetPeer), typeof(NetSessionConfig),
                    typeof(PartyRoster), typeof(PlayerSlot),
                },
                pattern: "spectat|observer|watchonly|caster",
                minimumMembersScanned: 20,
                why: "R-70: no spectator slots in v1 beyond the dead-hero cam (which lives on the "
                     + "hero, not the session)",
                wouldMatch: new[]
                {
                    "SpectatorSlots", "TryJoinAsSpectator", "IsSpectator", "ObserverSeat", "MaxSpectators",
                });

            Assert.That(Enum.GetNames(typeof(NetSessionPhase)).Concat(Enum.GetNames(typeof(SessionNoticeKind)))
                    .Any(n => Regex.IsMatch(n, "spectat|observer", RegexOptions.IgnoreCase)),
                Is.False,
                "R-70: nor a spectator phase or notice");
        }

        // ======================================================================================
        //  R-71 — no meta-economy: scrip resets every match
        // ======================================================================================

        /// <summary>
        /// R-71 / R-07 / R-20. A rematch after a WEALTHY match opens on the configured stake — the
        /// new pool is <see cref="SimConfig.StartingScrip"/>, regardless of what the previous
        /// match banked. The first match's pool is driven well above the stake through real kills
        /// (not by poking the field) so a carryover implementation has something concrete to
        /// carry, and the discriminator is two-sided: the new pool equals the stake AND does not
        /// equal the old pool. Meanwhile the one thing R-71 says DOES persist — XP — is asserted
        /// to have survived the same reset, so this cannot pass against a session that wiped
        /// everything.
        /// </summary>
        [Test]
        public void A_rematch_opens_on_the_starting_stake_however_rich_the_last_match_was()
        {
            var lobby = NewTwoPlayerLoopbackLobby();
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");

            var first = lobby.Session.Match;
            var stake = lobby.SimConfig.StartingScrip;
            Assert.That(first.State.Team.Scrip, Is.EqualTo(stake), "sanity (R-20): opens on the stake");

            // Bank a fortune through real kills (R-20), leaving at least one monster standing so
            // the colony can still be emptied mid-wave. Bounty is credited to the host's account
            // so R-71's "only XP persists" has XP to persist.
            var hostHero = first.State.Heroes.Values.Single(h => h.AccountId == HostAccount);
            var roster = first.State.Wave.LivingMonsterIds.ToList();
            Assert.That(roster.Count, Is.GreaterThanOrEqualTo(2), "sanity: wave 1 sends a group");
            foreach (var monsterId in roster.Take(roster.Count - 1))
            {
                first.Sim.RecordMonsterKill(new MonsterKillRequest
                {
                    MonsterId = monsterId,
                    MonsterType = first.State.Monsters[monsterId].Type,
                    Bounty = 200,
                    KillerHeroId = hostHero.Id,
                });
                first.Sim.AwardKillXp(new MonsterKillRequest
                {
                    MonsterId = monsterId,
                    MonsterType = first.State.Monsters[monsterId].Type,
                    Bounty = 200,
                    KillerHeroId = hostHero.Id,
                }, HostAccount);
            }

            var richPool = first.State.Team.Scrip;
            Assert.That(richPool, Is.GreaterThan(stake),
                "sanity (R-20): the pool moved well above the stake, so a carryover has something "
                + "to carry");
            var earnedXp = lobby.Profiles.Load(HostAccount).LifetimeXp;
            Assert.That(earnedXp, Is.GreaterThan(0.0), "sanity (R-40): XP was earned");

            EndTheMatchByEmptyingTheColony(first);
            var reachedPostMatch = DriveUntil(
                lobby.Session, first.Clock,
                () => lobby.Session.Phase == NetSessionPhase.PostMatch, budgetSeconds: 5.0);
            Assert.That(reachedPostMatch, Is.True, "the finished match reaches the post-match screen");

            Assert.That(lobby.Session.TryRematch(HostPeerId), Is.True, "the host plays again");
            Assert.That(lobby.Session.TryStartMatch(HostPeerId), Is.True, "and starts the next match");

            var second = lobby.Session.Match;
            Assert.That(second.State.Team.Scrip, Is.EqualTo(stake),
                "R-71: scrip resets every match — the new pool is the configured stake, full stop");
            Assert.That(second.State.Team.Scrip, Is.Not.EqualTo(richPool),
                "R-71: and is NOT the previous match's " + richPool
                + " — no pool, bank or bonus crosses a match boundary");

            Assert.That(lobby.Profiles.Load(HostAccount).LifetimeXp, Is.EqualTo(earnedXp).Within(1e-6),
                "R-71 / R-43: the one thing that DOES persist — lifetime XP — survived the same "
                + "reset, so this test cannot pass against a session that simply wiped everything");
        }

        // ======================================================================================
        //  R-73 — no difficulty settings on the session/shell config either
        // ======================================================================================

        /// <summary>
        /// R-73. The sim-side scan (dotnet suite) sweeps <see cref="SimConfig"/>; this sweeps the
        /// shell's config surface, which is the OTHER place a difficulty selector would arrive —
        /// a lobby picks the difficulty in most games, so <see cref="NetSessionConfig"/> and the
        /// session/party types are where such a knob would naturally land.
        /// </summary>
        [Test]
        public void The_shell_config_surface_has_no_difficulty_knob()
        {
            AssertNoPublicMemberMatches(
                new[] { typeof(NetSessionConfig), typeof(NetSession), typeof(NetPeer), typeof(HostedMatch) },
                pattern: "difficult|nightmare|hardmode|easymode|gamemode",
                minimumMembersScanned: 15,
                why: "R-73: no difficulty settings in v1",
                wouldMatch: new[] { "Difficulty", "DifficultySetting", "HardMode", "GameMode" });
        }

        // ======================================================================================
        //  scenario builders (the T11 convention, private per fixture)
        // ======================================================================================

        private sealed class LoopbackLobby
        {
            public SimConfig SimConfig;
            public InMemoryProfileStore Profiles;
            public NetSession Session;
        }

        private static LoopbackLobby NewTwoPlayerLoopbackLobby()
        {
            var simConfig = new SimConfig();
            var profiles = new InMemoryProfileStore();

            var session = new NetSession(
                new NetSessionConfig(),
                new LoopbackNetTransport(),
                new ColonyMatchFactory(ColonyMap.V1(), simConfig, profiles));

            session.StartHost(NewPeer(HostPeerId, HostAccount, HeroClass.Gunslinger, isHost: true));
            Assert.That(session.Phase, Is.EqualTo(NetSessionPhase.Lobby), "hosting opens a lobby");
            Assert.That(session.TryJoin(NewPeer(GuestPeerId, GuestAccount, HeroClass.Sawbones)), Is.True,
                "a second player joins the lobby");

            return new LoopbackLobby { SimConfig = simConfig, Profiles = profiles, Session = session };
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

        /// <summary>Ends a match the only way R-02 allows: every shelter emptied (T11's helper).</summary>
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
            NetSession session, SimClock clock, Func<bool> done, double budgetSeconds)
        {
            var deadline = clock.ElapsedSeconds + budgetSeconds;
            var maxSteps = (int)(budgetSeconds / Step60Hz) + 64;

            for (var i = 0; i < maxSteps; i++)
            {
                if (done())
                {
                    return true;
                }

                session.Step(Step60Hz);

                if (clock.ElapsedSeconds > deadline)
                {
                    break;
                }
            }

            return done();
        }

        // ======================================================================================
        //  scan plumbing (mirrors the dotnet-side guard file)
        // ======================================================================================

        private static List<string> PublicMemberNames(Type type)
        {
            return type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                            | BindingFlags.DeclaredOnly)
                .Select(m => m.Name)
                .ToList();
        }

        /// <summary>
        /// The shared absence scan. Non-vacuous by construction: the pattern must first match every
        /// name the feature would plausibly arrive under (a pattern that can match nothing fails its
        /// own self-test), and the scan must have seen at least the number of members the surface is
        /// known to carry (a scan of an empty surface certifies nothing).
        /// </summary>
        private static void AssertNoPublicMemberMatches(
            Type[] types, string pattern, int minimumMembersScanned, string why, string[] wouldMatch)
        {
            foreach (var probe in wouldMatch)
            {
                Assert.That(Regex.IsMatch(probe, pattern, RegexOptions.IgnoreCase), Is.True,
                    "self-test: the pattern '" + pattern + "' must match '" + probe
                    + "', or this guard could never catch the feature it exists to catch");
            }

            var scanned = 0;
            foreach (var type in types)
            {
                var names = PublicMemberNames(type);
                scanned += names.Count;

                foreach (var name in names)
                {
                    Assert.That(Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase), Is.False,
                        why + " — but " + type.Name + " has a public member named '" + name
                        + "', which matches the non-goal pattern '" + pattern + "'");
                }
            }

            Assert.That(scanned, Is.GreaterThanOrEqualTo(minimumMembersScanned),
                "non-vacuity: the scan saw only " + scanned + " member(s) across "
                + types.Length + " type(s) — fewer than the " + minimumMembersScanned
                + " this surface is known to carry, so it is probably scanning the wrong thing");
        }
    }
}
