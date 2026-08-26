using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Game.Host;
using RedHollow.Game.Net;
using RedHollow.Game.UI;
using RedHollow.Sim;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 020 (T-20): real networking — the NGO + Unity Lobby + Relay transport behind the
    /// seam ticket 011 established (R-50). Grades no golden fixture.
    ///
    /// <b>What EditMode can and cannot verify here, stated up front.</b> A live NGO connection, a
    /// UGS sign-in and a Relay allocation need play mode, real sockets and cloud auth; the
    /// two-machine hand check is the OWNER'S acceptance step and stays unchecked on the ticket.
    /// The design answers that constraint rather than fighting it: every DECISION ticket 020 makes
    /// — which transport a config selects, the order of the host bring-up, what a bad join code
    /// does, when the lobby is heartbeated and released, how a wire drop reaches the session —
    /// lives in <see cref="NgoNetTransport"/> and <see cref="NetTransportFactory"/>, driven
    /// through two seams (<see cref="IUgsServices"/>, <see cref="INetWire"/>) that scripted fakes
    /// stand behind. The real adapters (<c>UnityGamingServices</c>, <c>NgoWire</c>) are thin,
    /// declarative wraps with no branching worth testing — untestable by construction, and kept
    /// that way. A test that faked deeper (a fake NetworkManager, a fake lobby protocol) would
    /// grade the fake.
    ///
    /// Six things are pinned and nothing else:
    ///
    ///  1. <b>Config gating (the acceptance criterion).</b> No UGS project id → loopback, and the
    ///     loopback path never touches <see cref="IUgsServices"/> at all — asserted as a fake that
    ///     recorded zero calls across a whole hosted match, not as an absence of configuration.
    ///  2. <b>Host bring-up.</b> Sign in → Relay allocation → lobby carrying the relay join code →
    ///     wire up at the allocation's endpoint. The code the transport surfaces is the LOBBY's
    ///     (the one players share, R-07), never the relay code (plumbing).
    ///  3. <b>Client bring-up and the bad-code path.</b> Sign in → lobby by typed code → Relay by
    ///     the code the lobby carried → wire connected. A bad/expired code is a refusal that
    ///     leaves everything untouched and retryable — the same inline-error surface S1 already
    ///     shows (T-12 pinned the UI side; this pins the transport side feeding it).
    ///  4. <b>Nothing half-started.</b> An auth or Relay failure during host start propagates as
    ///     <see cref="UgsUnavailableException"/> and leaves no lobby, no wire, no heartbeat — and
    ///     the session above it still Offline and retryable.
    ///  5. <b>Lifecycle upkeep.</b> The lobby is heartbeated while hosting and released at
    ///     shutdown, after which nothing beats. (Cadence is the service's business; only "beats
    ///     while alive, never after teardown" is contract.)
    ///  6. <b>Transport-agnosticism of the session.</b> The T-11 lifecycle — start, a full
    ///     10-wave match, rematch to the SAME lobby, R-53's disconnects including DEC-RUN-10's
    ///     host-leaves-ends-it — behaves identically over <see cref="NgoNetTransport"/> with fakes
    ///     as it does over loopback, with <see cref="NetSession"/> unchanged.
    ///
    /// <b>Deliberately not asserted</b>, because the PRD and the ticket are silent and a guessed
    /// value would ship as spec: join-code formats (lobby or relay), heartbeat cadence, lobby
    /// names, relay regions, how the connection payload maps NGO client ids to peer ids (adapter
    /// business), reconnection and host migration (v1 has none).
    /// </summary>
    [TestFixture]
    public class T20_NgoTransportTests
    {
        private const double Step60Hz = 1.0 / 60.0;

        private const string HostPeerId = "peer_host";
        private const string GuestPeerId = "peer_guest";
        private const string HostAccount = "acc_calamity";
        private const string GuestAccount = "acc_doc";

        /// <summary>
        /// An opaque stand-in for a UGS project id, deliberately not the real linked project's:
        /// nothing here may depend on WHICH project, only that the configured one is carried
        /// (R-50, same reading as T11).
        /// </summary>
        private const string ConfiguredProjectId = "ugs-project-under-test";

        // ==========================================================================================
        //  1 — config gating: no UGS id means loopback, and loopback never touches UGS
        // ==========================================================================================

        /// <summary>
        /// The acceptance criterion, pinned the strong way. It is not enough that loopback CAN run
        /// without a project id (T11 already holds that); the shell's transport CHOICE must route
        /// an id-less config to loopback, and that path must record <b>zero</b> calls on the
        /// services seam across a whole hosted match — sign-in included, because an offline
        /// machine fails on the first call, whichever it is.
        /// </summary>
        [Test]
        public void No_ugs_id_selects_loopback_and_the_services_seam_is_never_touched()
        {
            var services = new RecordingUgsServices();
            var wire = new RecordingWire();

            var transport = NetTransportFactory.Create(new NetSessionConfig(), services, wire);

            Assert.That(transport, Is.InstanceOf<LoopbackNetTransport>(),
                "R-50: no UGS project id is the loopback configuration — offline is the default, "
                + "not a special mode");

            // A null config is the same offline default NetSession already accepts.
            Assert.That(NetTransportFactory.Create(null, services, wire),
                Is.InstanceOf<LoopbackNetTransport>(),
                "R-50: a null config is the offline default too, matching NetSession's own reading");

            // The chosen transport carries a whole match, and UGS is never consulted once.
            var session = new NetSession(
                new NetSessionConfig(),
                transport,
                new ColonyMatchFactory(ColonyMap.V1(), new SimConfig(), new InMemoryProfileStore()));

            session.StartHost(NewPeer(HostPeerId, HostAccount, HeroClass.Gunslinger, isHost: true));
            Assert.That(session.TryJoin(NewPeer(GuestPeerId, GuestAccount, HeroClass.Sawbones)), Is.True,
                "R-50: a second player joins the loopback lobby");
            Assert.That(session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");

            for (var i = 0; i < 60; i++)
            {
                session.Step(Step60Hz);
            }

            Assert.That(session.Phase, Is.EqualTo(NetSessionPhase.InMatch),
                "R-50: the loopback match really runs");

            Assert.That(services.Steps, Is.Empty,
                "R-50: the loopback path must never touch Unity services — the fake recorded "
                + string.Join(", ", services.Steps) + " — a single call here is a session that "
                + "cannot come up on a machine with no cloud project linked");
            Assert.That(wire.HostEndpoints, Is.Empty,
                "R-50: nor the NGO wire — loopback is in-process by design");
        }

        /// <summary>
        /// The other half of the gate: a config that DOES carry a project id selects the real
        /// transport — but choosing is not starting, so construction records nothing. A factory
        /// that signed in eagerly would put a network call (and a failure mode) into every screen
        /// that merely builds a session it may never host.
        /// </summary>
        [Test]
        public void A_ugs_id_selects_the_ngo_transport_and_construction_touches_nothing()
        {
            var services = new RecordingUgsServices();
            var wire = new RecordingWire();

            var transport = NetTransportFactory.Create(
                new NetSessionConfig { UgsProjectId = ConfiguredProjectId }, services, wire);

            Assert.That(transport, Is.InstanceOf<NgoNetTransport>(),
                "R-50: a configured project id is the real Lobby + Relay transport");
            Assert.That(transport.RequiresUnityServices, Is.True,
                "R-50: and it says so — this is the property loopback answers false to");
            Assert.That(transport.IsRunning, Is.False, "nothing has been started");

            Assert.That(services.Steps, Is.Empty,
                "construction is passive: choosing a transport is not yet a reason to "
                + "authenticate (recorded: " + string.Join(", ", services.Steps) + ")");
            Assert.That(wire.HostEndpoints, Is.Empty, "and the wire is untouched");
        }

        // ==========================================================================================
        //  2 — host bring-up
        // ==========================================================================================

        /// <summary>
        /// The host orchestration, in the only order that works: auth before anything (both
        /// services refuse the unauthenticated), the Relay allocation before the lobby (the lobby
        /// carries the relay join code, so it cannot exist first), the wire up at the endpoint the
        /// allocation answered — the SAME endpoint object, because connection data is opaque here
        /// and a transport that rebuilt it would be deciding wire format in the testable layer.
        ///
        /// The surfaced <see cref="INetTransport.JoinCode"/> is the LOBBY's code — the fake mints
        /// distinct lobby and relay codes precisely so a transport that surfaced the relay code
        /// (the classic Relay-tutorial shape, where there is no lobby) fails here.
        /// </summary>
        [Test]
        public void A_host_start_signs_in_allocates_relay_creates_the_lobby_and_raises_the_wire()
        {
            var services = new RecordingUgsServices();
            var wire = new RecordingWire();
            var transport = new NgoNetTransport(services, wire);

            transport.StartHost(new NetSessionConfig { UgsProjectId = ConfiguredProjectId });

            Assert.That(services.Steps, Is.EqualTo(new[]
                {
                    UgsStep.SignIn, UgsStep.AllocateRelay, UgsStep.CreateLobby,
                }),
                "R-50: the host bring-up is sign in, then allocate Relay, then create the lobby — "
                + "auth gates both services, and the lobby stores the relay code so it cannot "
                + "come first (recorded: " + string.Join(", ", services.Steps) + ")");

            Assert.That(services.SignedInProjectId, Is.EqualTo(ConfiguredProjectId),
                "R-50: the configured id is what authentication receives — carried, never invented");

            Assert.That(services.RelayMaxConnections,
                Is.GreaterThanOrEqualTo(PartyRoster.MaxPlayers - 1),
                "R-50: the allocation must hold at least every non-host seat (" +
                (PartyRoster.MaxPlayers - 1) + " remote peers for a party of "
                + PartyRoster.MaxPlayers + ") — an under-allocated Relay refuses the last joiner "
                + "at the wire after the lobby said yes");

            Assert.That(services.CreateLobbyMaxPlayers, Is.EqualTo(PartyRoster.MaxPlayers),
                "R-50 / DEC-020: the lobby's cap is the party cap");
            Assert.That(services.CreateLobbyRelayCode, Is.EqualTo(services.MintedRelayCode),
                "the lobby must carry the relay join code the allocation answered — it is the "
                + "only path a joiner has from the typed code to the host's allocation");

            Assert.That(wire.HostEndpoints, Has.Count.EqualTo(1),
                "the wire is brought up exactly once");
            Assert.That(wire.HostEndpoints[0], Is.SameAs(services.HostEndpoint),
                "and at the very endpoint the allocation answered — connection data is opaque to "
                + "the orchestration layer, so it can only be carried, never rebuilt");

            Assert.That(transport.IsRunning, Is.True, "the transport is up");
            Assert.That(transport.ProjectId, Is.EqualTo(ConfiguredProjectId),
                "R-50: the id is carried onto the transport, as loopback already does");

            Assert.That(transport.JoinCode, Is.EqualTo(services.MintedLobbyCode),
                "R-07: the code the party shares is the LOBBY's code");
            Assert.That(transport.JoinCode, Is.Not.EqualTo(services.MintedRelayCode),
                "R-07: and never the relay code, which is plumbing a player must not see");
        }

        /// <summary>
        /// Lobby upkeep and teardown. UGS idles a lobby out within tens of seconds when nothing
        /// beats it, so a hosting transport that is ticked must heartbeat — pinned as "at least
        /// one heartbeat per 30 ticked seconds", not as a cadence, which is the service's
        /// business. At shutdown the lobby is released (a code that keeps resolving to a dead
        /// party is a joiner staring at a spinner), the wire comes down, and nothing ever beats
        /// again. Shutdown is idempotent, like loopback's — R-53 tears down from callbacks that
        /// can fire twice.
        /// </summary>
        [Test]
        public void The_lobby_is_heartbeated_while_hosting_and_released_exactly_once_at_shutdown()
        {
            var services = new RecordingUgsServices();
            var wire = new RecordingWire();
            var transport = new NgoNetTransport(services, wire);

            transport.StartHost(new NetSessionConfig { UgsProjectId = ConfiguredProjectId });

            for (var s = 0; s < 30; s++)
            {
                transport.Tick(1.0);
            }

            Assert.That(services.HeartbeatLobbyIds, Is.Not.Empty,
                "the hosted lobby must be heartbeated — UGS idles an unbeaten lobby out, which "
                + "kills the join code while the party is still forming");
            var afterFirstWindow = services.HeartbeatLobbyIds.Count;

            for (var s = 0; s < 30; s++)
            {
                transport.Tick(1.0);
            }

            Assert.That(services.HeartbeatLobbyIds.Count, Is.GreaterThan(afterFirstWindow),
                "and heartbeated continuously, not once at startup");
            Assert.That(services.HeartbeatLobbyIds, Has.All.EqualTo(services.MintedLobbyId),
                "every beat names the lobby this transport holds");

            transport.Shutdown();

            Assert.That(services.LeftLobbyIds, Is.EqualTo(new[] { services.MintedLobbyId }),
                "shutdown releases the hosted lobby, once");
            Assert.That(wire.ShutdownCount, Is.GreaterThanOrEqualTo(1),
                "and takes the wire down with it");
            Assert.That(transport.IsRunning, Is.False, "the transport is down");
            Assert.That(transport.JoinCode, Is.Null.Or.Empty,
                "the join code goes with the lobby it named, as loopback already does");

            var beatsAtShutdown = services.HeartbeatLobbyIds.Count;
            for (var s = 0; s < 60; s++)
            {
                transport.Tick(1.0);
            }

            Assert.That(services.HeartbeatLobbyIds.Count, Is.EqualTo(beatsAtShutdown),
                "nothing beats after teardown — a heartbeat is what keeps a lobby findable, and "
                + "this one is gone");

            Assert.That(() => transport.Shutdown(), Throws.Nothing,
                "shutdown is idempotent (R-53 tears down from callbacks that can fire twice)");
            Assert.That(services.LeftLobbyIds, Has.Count.EqualTo(1),
                "and a second shutdown does not leave the lobby a second time");
        }

        // ==========================================================================================
        //  3 — client bring-up, and the bad-code path
        // ==========================================================================================

        /// <summary>
        /// The joining side, in its only workable order: sign in, find the lobby by the code the
        /// player typed, join the Relay allocation by the code the lobby carried, connect the wire
        /// at the endpoint the Relay join answered — again the same endpoint object, for the same
        /// reason as the host side.
        /// </summary>
        [Test]
        public void A_client_join_signs_in_finds_the_lobby_joins_relay_and_connects_the_wire()
        {
            var services = new RecordingUgsServices();
            services.KnownLobbies["PARTY-42"] = new LobbyTicket
            {
                LobbyId = "lobby_remote",
                JoinCode = "PARTY-42",
                RelayJoinCode = "RLY-REMOTE",
            };

            var wire = new RecordingWire();
            var transport = new NgoNetTransport(services, wire);

            var joined = transport.TryJoinAsClient(
                new NetSessionConfig { UgsProjectId = ConfiguredProjectId }, "PARTY-42");

            Assert.That(joined, Is.True, "R-50: a valid code joins the party");

            Assert.That(services.Steps, Is.EqualTo(new[]
                {
                    UgsStep.SignIn, UgsStep.JoinLobby, UgsStep.JoinRelay,
                }),
                "R-50: the client bring-up is sign in, lobby by the typed code, then Relay by the "
                + "code the lobby carried (recorded: " + string.Join(", ", services.Steps) + ")");

            Assert.That(services.JoinedLobbyCodes, Is.EqualTo(new[] { "PARTY-42" }),
                "the lobby lookup uses the code as typed");
            Assert.That(services.JoinedRelayCodes, Is.EqualTo(new[] { "RLY-REMOTE" }),
                "and the Relay join uses the relay code the LOBBY answered — the typed code names "
                + "a lobby, never an allocation");

            Assert.That(wire.ClientEndpoints, Has.Count.EqualTo(1),
                "the wire connects exactly once");
            Assert.That(wire.ClientEndpoints[0], Is.SameAs(services.JoinEndpoint),
                "at the endpoint the Relay join answered, carried untouched");
        }

        /// <summary>
        /// R-53's join-refusal surface, on the wire path this time. T-12 pinned the UI half: a bad
        /// or expired code puts an inline error under S1's input and stays on S1
        /// (<see cref="TitleScreenModel.NoteJoinFailed"/>). This pins the transport half that
        /// feeds it: a bad code is a REFUSAL — false, not a thrown exception the shell would have
        /// to catch on a screen — and it leaves the transport so untouched that the corrected code
        /// can simply be tried again on the same instance. The glue between the two halves is
        /// exercised at the end, so the two tickets' surfaces are pinned to actually meet.
        /// </summary>
        [Test]
        public void A_bad_join_code_is_a_refusal_that_leaves_everything_retryable()
        {
            var services = new RecordingUgsServices();
            var wire = new RecordingWire();
            var transport = new NgoNetTransport(services, wire);
            var config = new NetSessionConfig { UgsProjectId = ConfiguredProjectId };

            var joined = transport.TryJoinAsClient(config, "PARTY-EXPIRED");

            Assert.That(joined, Is.False,
                "R-53 / T-12: a bad or expired code is an ordinary refusal, not a crash");
            Assert.That(services.Steps, Has.No.Member(UgsStep.JoinRelay),
                "a join that found no lobby has no relay code to join with");
            Assert.That(wire.ClientEndpoints, Is.Empty,
                "and must not have touched the wire — there is nothing to connect to");
            Assert.That(transport.IsRunning, Is.False,
                "a refused join leaves the transport down, exactly as it found it");

            // The S1 surface this refusal feeds (T-12 pinned the model; this pins the meeting).
            var title = new TitleScreenModel(new InMemoryProfileStore());
            title.SetJoinCodeInput("PARTY-EXPIRED");
            if (!joined)
            {
                title.NoteJoinFailed();
            }

            Assert.That(title.JoinError, Is.Not.Null.And.Not.Empty,
                "T-12 / R-53: the refusal lands as S1's inline error (its copy is presentation "
                + "and is not asserted)");

            // The player corrects the code and tries again — same transport instance.
            title.SetJoinCodeInput("PARTY-42");
            Assert.That(title.JoinError, Is.Null, "T-12: editing the code clears the error");

            services.KnownLobbies["PARTY-42"] = new LobbyTicket
            {
                LobbyId = "lobby_remote",
                JoinCode = "PARTY-42",
                RelayJoinCode = "RLY-REMOTE",
            };

            Assert.That(transport.TryJoinAsClient(config, "PARTY-42"), Is.True,
                "R-53: the refusal left the transport clean enough to retry — a joiner should "
                + "never have to restart the game because they mistyped a code");
        }

        // ==========================================================================================
        //  4 — a failed host start leaves nothing half-started
        // ==========================================================================================

        /// <summary>
        /// The failure paths of the host bring-up, one scripted failure per step. What is pinned
        /// is the INVARIANT rather than each message: the failure propagates as
        /// <see cref="UgsUnavailableException"/> naming its step, and everything downstream of the
        /// failed call was never touched — no lobby without a wire to serve it, no wire without a
        /// lobby to fill it, no heartbeat for a lobby that does not exist, and a transport still
        /// down and still startable once the service recovers.
        /// </summary>
        [Test]
        public void An_auth_or_relay_failure_during_host_start_leaves_nothing_half_started()
        {
            var services = new RecordingUgsServices();
            var wire = new RecordingWire();
            var transport = new NgoNetTransport(services, wire);
            var config = new NetSessionConfig { UgsProjectId = ConfiguredProjectId };

            // ---- auth is down -----------------------------------------------------------------
            services.FailAt = UgsStep.SignIn;

            var authFailure = Assert.Throws<UgsUnavailableException>(
                () => transport.StartHost(config),
                "an auth failure must surface, not vanish into a transport that looks started");
            Assert.That(authFailure.Step, Is.EqualTo(UgsStep.SignIn),
                "naming the step that failed — it is all the shell has to report");

            Assert.That(services.Steps, Has.No.Member(UgsStep.AllocateRelay),
                "nothing past the failed sign-in may have run");
            Assert.That(services.Steps, Has.No.Member(UgsStep.CreateLobby),
                "no lobby behind a failed auth");
            Assert.That(wire.HostEndpoints, Is.Empty, "and no wire");
            Assert.That(transport.IsRunning, Is.False, "the transport is still down");

            // ---- relay is down ----------------------------------------------------------------
            services.FailAt = UgsStep.AllocateRelay;

            var relayFailure = Assert.Throws<UgsUnavailableException>(
                () => transport.StartHost(config));
            Assert.That(relayFailure.Step, Is.EqualTo(UgsStep.AllocateRelay));

            Assert.That(services.Steps, Has.No.Member(UgsStep.CreateLobby),
                "a lobby created before the allocation failed would advertise a party nobody can "
                + "reach — the relay code it should carry does not exist");
            Assert.That(wire.HostEndpoints, Is.Empty, "no endpoint was ever answered");
            Assert.That(transport.IsRunning, Is.False, "still down");

            for (var s = 0; s < 60; s++)
            {
                transport.Tick(1.0);
            }

            Assert.That(services.HeartbeatLobbyIds, Is.Empty,
                "and nothing beats — there is no lobby to keep alive");

            // ---- the service recovers ---------------------------------------------------------
            services.FailAt = null;

            Assert.That(() => transport.StartHost(config), Throws.Nothing,
                "a failed start must leave the transport startable — HOST A PARTY is a button, "
                + "and a button gets pressed again");
            Assert.That(transport.IsRunning, Is.True, "and this time it is up");
            Assert.That(transport.JoinCode, Is.EqualTo(services.MintedLobbyCode),
                "with the lobby's join code surfaced as usual");
        }

        /// <summary>
        /// The same failure one layer up, where the player actually is. <see cref="NetSession"/>
        /// calls the transport before it seats anybody (T-11 wrote it that way), so a failed
        /// bring-up must leave the session exactly Offline — no host seat, no phase change, and a
        /// second <see cref="NetSession.StartHost"/> accepted once the service recovers. This is
        /// what keeps a UGS outage a toast on the title screen instead of a wedged session object.
        /// </summary>
        [Test]
        public void A_failed_host_start_leaves_the_session_offline_and_retryable()
        {
            var services = new RecordingUgsServices { FailAt = UgsStep.SignIn };
            var wire = new RecordingWire();
            var config = new NetSessionConfig { UgsProjectId = ConfiguredProjectId };

            var session = new NetSession(
                config,
                new NgoNetTransport(services, wire),
                new ColonyMatchFactory(ColonyMap.V1(), new SimConfig(), new InMemoryProfileStore()));

            var host = NewPeer(HostPeerId, HostAccount, HeroClass.Gunslinger, isHost: true);

            Assert.Throws<UgsUnavailableException>(() => session.StartHost(host),
                "the failure surfaces through the session — the shell needs something to toast");

            Assert.That(session.Phase, Is.EqualTo(NetSessionPhase.Offline),
                "a failed bring-up leaves the session where it was: Offline, not a phantom lobby");
            Assert.That(session.Seats, Is.Empty,
                "and seats nobody — a seated host in a lobby that does not exist is a party that "
                + "can never be joined");

            services.FailAt = null;

            Assert.That(() => session.StartHost(host), Throws.Nothing,
                "once the service recovers, the SAME session hosts — the failure did not burn it");
            Assert.That(session.Phase, Is.EqualTo(NetSessionPhase.Lobby), "and the lobby is open");
            Assert.That(session.JoinCode, Is.EqualTo(services.MintedLobbyCode),
                "with the lobby's code on screen (R-07)");
        }

        // ==========================================================================================
        //  5 — the T-11 lifecycle is transport-agnostic
        // ==========================================================================================

        /// <summary>
        /// Ticket 011's headline drive, re-run over the real transport's orchestration:
        /// <see cref="NetSession"/> UNCHANGED, <see cref="NgoNetTransport"/> underneath, fakes
        /// behind the seams. Two players, ten waves, victory, post-match, rematch, second match —
        /// the same milestones T11 pins over loopback, driven with the same helpers.
        ///
        /// The one thing this adds beyond parity is R-07's sharpest online reading: PLAY AGAIN
        /// returns the party to the SAME lobby — asserted as one <see cref="UgsStep.CreateLobby"/>
        /// for the whole run and a join code that never changed, because a rematch that quietly
        /// re-created the lobby would strand every party member holding the old code.
        /// </summary>
        [Test]
        public void The_t11_lifecycle_runs_identically_over_the_ngo_transport()
        {
            var rig = NewTwoPlayerNgoLobby();
            var session = rig.Session;

            var joinCodeBefore = session.JoinCode;
            Assert.That(joinCodeBefore, Is.EqualTo(rig.Services.MintedLobbyCode),
                "R-07: the lobby's code is on screen");

            Assert.That(session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");
            var match = session.Match;
            Assert.That(match.State.Heroes.Count, Is.EqualTo(2),
                "R-50 / R-31: one hero per seated player, exactly as over loopback");

            // The full ten-wave walk, T11's drive verbatim.
            var totalWaves = match.State.Wave.TotalWaves;
            for (var wave = 1; wave <= totalWaves; wave++)
            {
                var expected = wave;
                var arrived = DriveUntil(
                    session,
                    () => match.State.Wave.Number == expected
                          && match.State.Wave.LivingMonsterIds.Count > 0,
                    budgetSeconds: 3.0 * rig.SimConfig.PlanningDurationSeconds,
                    beforeEachStep: () => ReadyPartyForWave(match, expected));

                Assert.That(arrived, Is.True,
                    "R-01: the campaign must reach wave " + expected + " over the NGO transport "
                    + "exactly as it does over loopback — it stalled with the counter on "
                    + match.State.Wave.Number + ", phase '" + match.State.Phase + "', session '"
                    + session.Phase + "'");

                KillWave(match, match.State.Wave.LivingMonsterIds.ToList());
            }

            Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Victory),
                "R-01: ten cleared waves win the map, whatever the transport");

            var reachedPostMatch = DriveUntil(
                session,
                () => session.Phase == NetSessionPhase.PostMatch,
                budgetSeconds: 5.0);
            Assert.That(reachedPostMatch, Is.True,
                "R-07: the session reaches the post-match screen (it is '" + session.Phase + "')");

            // ---- rematch: the SAME lobby ------------------------------------------------------
            Assert.That(session.TryRematch(HostPeerId), Is.True,
                "R-07: the host may PLAY AGAIN");
            Assert.That(session.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "R-07: the party is back in the lobby");
            Assert.That(session.JoinCode, Is.EqualTo(joinCodeBefore),
                "R-07: the SAME lobby — the code a party member read off the screen still names it");
            Assert.That(rig.Services.Steps.Count(s => s == UgsStep.CreateLobby), Is.EqualTo(1),
                "R-07: one lobby for the whole run — a rematch that re-created it would strand "
                + "everyone holding the old code");
            Assert.That(rig.Services.LeftLobbyIds, Is.Empty,
                "and nothing left it — the party never went anywhere");

            Assert.That(session.Seats.Count, Is.EqualTo(2), "R-07: the whole party returned");

            Assert.That(session.TryStartMatch(HostPeerId), Is.True,
                "the rematched lobby starts a second match");
            Assert.That(session.Match, Is.Not.SameAs(match),
                "R-07: a NEW match — the reset is a rebuild, exactly as over loopback");
            Assert.That(session.Match.State.Wave.Number, Is.EqualTo(1),
                "R-07: starting from wave 1 again");
        }

        /// <summary>
        /// R-53 through the real path: the WIRE reports a guest's connection dropped, the
        /// transport surfaces it as <see cref="NgoNetTransport.PeerDisconnected"/> carrying the
        /// session's peer id, the shell's one-line forward hands it to
        /// <see cref="NetSession.Disconnect"/> — and from there everything T11 pinned holds: hero
        /// despawned, slot marked disconnected, seat freed, toast of the right kind, match
        /// carrying on. The forward is written out here exactly as the shell will write it,
        /// because the event's SHAPE (a session peer id, not an NGO client id) is this ticket's
        /// contract.
        /// </summary>
        [Test]
        public void A_wire_reported_guest_drop_reaches_the_session_as_r53()
        {
            var rig = NewTwoPlayerNgoLobby();
            var session = rig.Session;

            Assert.That(session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");
            var match = session.Match;

            // The shell's whole job on this path, verbatim.
            rig.Transport.PeerDisconnected += session.Disconnect;

            var noticesBefore = session.Notices.Count;

            rig.Wire.RaisePeerDisconnected(GuestPeerId);

            Assert.That(match.State.Heroes.Values.Any(h => h.AccountId == GuestAccount), Is.False,
                "R-53: the dropped player's hero despawns, driven from the wire this time");
            Assert.That(session.Phase, Is.EqualTo(NetSessionPhase.InMatch),
                "R-53: the match carries on — one player dropping is not a loss (R-02 owns the "
                + "only one)");
            Assert.That(session.Seats.Select(s => s.PeerId), Does.Not.Contain(GuestPeerId),
                "R-53: the seat is freed");
            Assert.That(rig.Transport.ConnectedPeers.Select(p => p.PeerId),
                Does.Not.Contain(GuestPeerId),
                "and the transport's roster agrees with the session's");

            var slot = match.State.Players.FirstOrDefault(p => p.AccountId == GuestAccount);
            Assert.That(slot, Is.Not.Null, "R-53 / R-03: the slot stays, marked disconnected");
            Assert.That(slot.Connected, Is.False, "R-53 / R-03: so readiness stops waiting on it");

            Assert.That(session.Notices.Count, Is.GreaterThan(noticesBefore),
                "R-53: a toast is shown");
            Assert.That(session.Notices.Last().Kind,
                Is.EqualTo(SessionNoticeKind.PlayerDisconnected),
                "R-53: of the disconnect kind (its copy is presentation and is not asserted)");
            Assert.That(session.Notices.Last().PeerId, Is.EqualTo(GuestPeerId),
                "R-53: naming who dropped");

            for (var i = 0; i < 60; i++)
            {
                session.Step(Step60Hz);
            }

            Assert.That(session.Phase, Is.EqualTo(NetSessionPhase.InMatch),
                "R-53: and a second of driven session later, the match is still running");
        }

        /// <summary>
        /// DEC-RUN-10 over the real transport path: the host drops, the session ends without
        /// inventing a defeat (R-02 owns the only loss rule) — T11 pinned that over loopback, and
        /// it must hold verbatim here — and this ticket's addition is the teardown underneath it:
        /// ending the session releases the LOBBY and drops the wire, so the dead party's join code
        /// stops resolving, and nothing heartbeats a lobby whose host is gone.
        /// </summary>
        [Test]
        public void A_host_drop_ends_the_session_and_releases_the_lobby_without_a_defeat()
        {
            var rig = NewTwoPlayerNgoLobby();
            var session = rig.Session;

            Assert.That(session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");
            var match = session.Match;

            rig.Transport.PeerDisconnected += session.Disconnect;

            rig.Wire.RaisePeerDisconnected(HostPeerId);

            Assert.That(session.Phase, Is.EqualTo(NetSessionPhase.Ended),
                "R-53 / DEC-RUN-10: the host dropping ends the session — no migration in v1");
            Assert.That(match.State.Status, Is.EqualTo(MatchStatus.InProgress),
                "R-02: an abandoned match is not a defeat — nobody emptied the colony");
            Assert.That(session.Notices.Last().Kind,
                Is.EqualTo(SessionNoticeKind.HostDisconnected),
                "R-53: the party is told it was the host (copy not asserted)");

            Assert.That(rig.Services.LeftLobbyIds, Is.EqualTo(new[] { rig.Services.MintedLobbyId }),
                "ending the session releases the lobby, so the join code stops resolving to a "
                + "party that no longer exists");
            Assert.That(rig.Wire.ShutdownCount, Is.GreaterThanOrEqualTo(1),
                "and the wire comes down");
            Assert.That(rig.Transport.IsRunning, Is.False, "the transport is down");

            var beats = rig.Services.HeartbeatLobbyIds.Count;
            for (var s = 0; s < 60; s++)
            {
                rig.Transport.Tick(1.0);
            }

            Assert.That(rig.Services.HeartbeatLobbyIds.Count, Is.EqualTo(beats),
                "nothing heartbeats a lobby whose host is gone");
        }

        // ==========================================================================================
        //  scenario builders — T11's rig, with the NGO transport swapped in behind the seam
        // ==========================================================================================

        /// <summary>Everything a driven NGO-transport session is assembled from.</summary>
        private sealed class NgoLobbyRig
        {
            public SimConfig SimConfig;
            public RecordingUgsServices Services;
            public RecordingWire Wire;
            public NgoNetTransport Transport;
            public NetSession Session;
        }

        /// <summary>
        /// A hosted two-player session riding <see cref="NgoNetTransport"/> over scripted fakes —
        /// the same party, picks and map as T11's loopback builder, because the whole point of the
        /// seam is that NOTHING above it changes when the transport does.
        /// </summary>
        private static NgoLobbyRig NewTwoPlayerNgoLobby()
        {
            var simConfig = new SimConfig();
            var services = new RecordingUgsServices();
            var wire = new RecordingWire();
            var transport = new NgoNetTransport(services, wire);

            var session = new NetSession(
                new NetSessionConfig { UgsProjectId = ConfiguredProjectId },
                transport,
                new ColonyMatchFactory(ColonyMap.V1(), simConfig, new InMemoryProfileStore()));

            session.StartHost(NewPeer(HostPeerId, HostAccount, HeroClass.Gunslinger, isHost: true));

            Assert.That(session.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "R-50: hosting over the NGO transport opens a lobby, exactly as loopback does");
            Assert.That(session.TryJoin(NewPeer(GuestPeerId, GuestAccount, HeroClass.Sawbones)),
                Is.True, "R-50: a second player joins it");

            return new NgoLobbyRig
            {
                SimConfig = simConfig,
                Services = services,
                Wire = wire,
                Transport = transport,
                Session = session,
            };
        }

        private static NetPeer NewPeer(
            string peerId, string accountId, string heroClass, bool isHost = false)
        {
            return new NetPeer
            {
                PeerId = peerId,
                AccountId = accountId,
                HeroClass = heroClass,
                IsHost = isHost,
            };
        }

        /// <summary>T11's wave clear, through the sim's own command (R-02 / R-20).</summary>
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

        /// <summary>
        /// T11's ready-up: R-03's early exit, raised only once the counter has reached the wave
        /// being waited for — readying in the post-clear window would start combat for a wave the
        /// party already fought (see T11's helper for the full reasoning).
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
        /// T11's bounded drive: steps the session until <paramref name="done"/> or the sim-time
        /// budget runs out, so a stalled session fails as a test failure rather than a hung runner.
        /// </summary>
        private static bool DriveUntil(
            NetSession session,
            Func<bool> done,
            double budgetSeconds,
            Action beforeEachStep = null)
        {
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
            }

            return done();
        }

        // ==========================================================================================
        //  the scripted fakes — one per seam, recording everything, deciding nothing
        // ==========================================================================================

        /// <summary>
        /// The scripted <see cref="IUgsServices"/>: records every call in order, mints stable
        /// distinct codes (lobby vs relay — distinct on purpose, so a transport that surfaces the
        /// wrong one fails), answers joins from <see cref="KnownLobbies"/>, and throws
        /// <see cref="UgsUnavailableException"/> at whichever step <see cref="FailAt"/> scripts.
        /// It decides nothing: every assertion in this fixture is about what the TRANSPORT did
        /// with these answers.
        /// </summary>
        private sealed class RecordingUgsServices : IUgsServices
        {
            public readonly string MintedRelayCode = "RLY-0001";
            public readonly string MintedLobbyCode = "PARTY-0001";
            public readonly string MintedLobbyId = "lobby_0001";

            /// <summary>Every call, in order.</summary>
            public readonly List<UgsStep> Steps = new List<UgsStep>();

            /// <summary>Lobbies a client join can find, keyed by lobby join code.</summary>
            public readonly Dictionary<string, LobbyTicket> KnownLobbies =
                new Dictionary<string, LobbyTicket>(StringComparer.Ordinal);

            /// <summary>Script the next call at this step to throw. Null scripts nothing.</summary>
            public UgsStep? FailAt;

            public string SignedInProjectId;
            public int RelayMaxConnections = -1;
            public int CreateLobbyMaxPlayers = -1;
            public string CreateLobbyRelayCode;
            public readonly List<string> JoinedLobbyCodes = new List<string>();
            public readonly List<string> JoinedRelayCodes = new List<string>();
            public readonly List<string> HeartbeatLobbyIds = new List<string>();
            public readonly List<string> LeftLobbyIds = new List<string>();

            /// <summary>The endpoint the host allocation answered — identity is the contract.</summary>
            public readonly RelayEndpoint HostEndpoint = new RelayEndpoint();

            /// <summary>The endpoint the client relay join answered.</summary>
            public readonly RelayEndpoint JoinEndpoint = new RelayEndpoint();

            private bool _signedIn;

            public bool IsSignedIn => _signedIn;

            public void SignIn(string projectId)
            {
                Record(UgsStep.SignIn);
                SignedInProjectId = projectId;
                _signedIn = true;
            }

            public RelayHostSlot AllocateRelay(int maxConnections)
            {
                Record(UgsStep.AllocateRelay);
                RelayMaxConnections = maxConnections;
                return new RelayHostSlot
                {
                    RelayJoinCode = MintedRelayCode,
                    Endpoint = HostEndpoint,
                };
            }

            public RelayJoinSlot JoinRelay(string relayJoinCode)
            {
                Record(UgsStep.JoinRelay);
                JoinedRelayCodes.Add(relayJoinCode);
                return new RelayJoinSlot { Endpoint = JoinEndpoint };
            }

            public LobbyTicket CreateLobby(int maxPlayers, string relayJoinCode)
            {
                Record(UgsStep.CreateLobby);
                CreateLobbyMaxPlayers = maxPlayers;
                CreateLobbyRelayCode = relayJoinCode;
                return new LobbyTicket
                {
                    LobbyId = MintedLobbyId,
                    JoinCode = MintedLobbyCode,
                    RelayJoinCode = relayJoinCode,
                };
            }

            public LobbyTicket JoinLobbyByCode(string joinCode)
            {
                Record(UgsStep.JoinLobby);
                JoinedLobbyCodes.Add(joinCode);

                if (!KnownLobbies.TryGetValue(joinCode ?? string.Empty, out var lobby))
                {
                    // The service's own answer for a bad or expired code.
                    throw new UgsUnavailableException(
                        UgsStep.JoinLobby, "no lobby answers to '" + joinCode + "'");
                }

                return lobby;
            }

            public void HeartbeatLobby(string lobbyId)
            {
                Record(UgsStep.Heartbeat);
                HeartbeatLobbyIds.Add(lobbyId);
            }

            public void LeaveLobby(string lobbyId)
            {
                Record(UgsStep.LeaveLobby);
                LeftLobbyIds.Add(lobbyId);
            }

            private void Record(UgsStep step)
            {
                Steps.Add(step);
                if (FailAt == step)
                {
                    throw new UgsUnavailableException(step, "scripted failure at " + step);
                }
            }
        }

        /// <summary>
        /// The scripted <see cref="INetWire"/>: records what it was handed (endpoint identity
        /// included), tracks up/down, and lets a test play the wire's one line —
        /// <see cref="RaisePeerDisconnected"/> — the way NGO's disconnect callback will.
        /// </summary>
        private sealed class RecordingWire : INetWire
        {
            public readonly List<RelayEndpoint> HostEndpoints = new List<RelayEndpoint>();
            public readonly List<RelayEndpoint> ClientEndpoints = new List<RelayEndpoint>();
            public int ShutdownCount;

            private bool _up;

            public bool IsUp => _up;

            public event Action<string> PeerDisconnected;

            public void StartHost(RelayEndpoint endpoint)
            {
                HostEndpoints.Add(endpoint);
                _up = true;
            }

            public void StartClient(RelayEndpoint endpoint)
            {
                ClientEndpoints.Add(endpoint);
                _up = true;
            }

            public void Shutdown()
            {
                ShutdownCount++;
                _up = false;
            }

            /// <summary>What NGO's disconnect callback will do, playable from a test.</summary>
            public void RaisePeerDisconnected(string peerId)
            {
                var handler = PeerDisconnected;
                if (handler != null)
                {
                    handler(peerId);
                }
            }
        }
    }
}
