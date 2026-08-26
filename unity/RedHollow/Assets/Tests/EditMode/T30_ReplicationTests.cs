using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Game.Host;
using RedHollow.Game.Input;
using RedHollow.Game.Net;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 030 — match replication for a remote party seat (R-50/R-51/R-52): the snapshot
    /// codec, the client mirror, and the host-side driver that turns a remote player's wire
    /// commands into the SAME sim commands the local shell issues. Everything here runs over the
    /// in-memory channel pair; NGO custom messaging is the same seam with sockets under it, and
    /// carrying bytes is the one thing these tests deliberately do not grade (T-20's convention:
    /// the wire is hand-verified, the decisions are not left to it).
    ///
    /// What is pinned: capture/apply round-trip fidelity for every field a client renders;
    /// replace-all mirror semantics (the snapshot is the whole truth); a remote hero MOVING
    /// through the session's one intent seam; remote basic attacks resolved with a HOST-built aim
    /// line, reaped and credited to the remote account (R-40); remote planning clicks (buy /
    /// ready) landing as the peer's own slot with the sim's gates intact; and the R-63 countdown
    /// crossing as a host-computed number.
    ///
    /// What is deliberately NOT pinned: snapshot cadence (the shell's schedule), payload size,
    /// event/feel replication (a v1 client renders state), and client-side interpolation (R-52's
    /// smoothing curve is unstated in the PRD and would ship a guess as spec).
    /// </summary>
    [TestFixture]
    public class T30_ReplicationTests
    {
        private const double Step60Hz = 1.0 / 60.0;
        private const double SimTolerance = 1e-9;

        private const string HostPeerId = "peer_host";
        private const string GuestPeerId = "peer_guest";
        private const string HostAccount = "acc_calamity";
        private const string GuestAccount = "acc_doc";

        // ==========================================================================================
        //  the codec — capture/apply round-trips everything a client renders
        // ==========================================================================================

        [Test]
        public void A_snapshot_round_trips_every_field_a_client_renders()
        {
            var state = new MatchState
            {
                Phase = MatchPhase.Planning,
                Status = MatchStatus.InProgress,
            };
            state.Wave.Number = 4;
            state.Wave.TotalWaves = 10;
            state.Wave.LivingMonsterIds.Add("m1");
            state.Team.Scrip = 265;
            state.Players.Add(new PlayerSlot
            {
                Id = "player_1",
                AccountId = "acc_\"quoted\"\ncallsign",
                HeroClass = HeroClass.Gunslinger,
                Ready = true,
                Connected = true,
            });
            state.Heroes["hero_1"] = new Hero
            {
                Id = "hero_1",
                HeroClass = HeroClass.Gunslinger,
                AccountId = "acc_\"quoted\"\ncallsign",
                Pos = new Vec2(-3.25, 7.5),
                Hp = 62.5,
                MaxHp = 100.0,
                Alive = false,
                RespawnAt = 42.125,
            };
            state.Monsters["m1"] = new Monster
            {
                Id = "m1",
                Type = MonsterType.Spitter,
                Pos = new Vec2(11.0, -0.5),
                Hp = 12.0,
                Alive = true,
            };
            state.Monsters["m_dead"] = new Monster
            {
                Id = "m_dead",
                Type = MonsterType.Shambler,
                Pos = new Vec2(1.0, 1.0),
                Hp = 0.0,
                Alive = false,
            };
            state.Hotspots["hs_chapel"] = new Hotspot
            {
                Id = "hs_chapel",
                Pos = new Vec2(11.0, 9.0),
                Civilians = 4,
            };
            state.Placeables["pl_1"] = new Placeable
            {
                Id = "pl_1",
                Type = PlaceableType.SpikeTrap,
                OwnerPlayerId = "player_1",
                Pos = new Vec2(5.0, 5.0),
                Hp = 0.0,
                PurchaseCost = 75,
                TriggersRemaining = 7,
                Exists = true,
            };

            var mirror = new MatchState();
            mirror.Hotspots["hs_chapel"] = new Hotspot
            {
                Id = "hs_chapel",
                Pos = new Vec2(11.0, 9.0),
                Civilians = 6,
            };

            var remaining = MatchSnapshot.Apply(MatchSnapshot.Capture(state, 47.25), mirror);

            Assert.That(remaining, Is.EqualTo(47.25).Within(SimTolerance),
                "R-63: the countdown crosses as the host computed it");
            Assert.That(mirror.Phase, Is.EqualTo(MatchPhase.Planning));
            Assert.That(mirror.Status, Is.EqualTo(MatchStatus.InProgress));
            Assert.That(mirror.Wave.Number, Is.EqualTo(4));
            Assert.That(mirror.Wave.TotalWaves, Is.EqualTo(10));
            Assert.That(mirror.Wave.LivingMonsterIds, Is.EqualTo(new[] { "m1" }));
            Assert.That(mirror.Team.Scrip, Is.EqualTo(265));

            Assert.That(mirror.Players, Has.Count.EqualTo(1));
            Assert.That(mirror.Players[0].AccountId, Is.EqualTo("acc_\"quoted\"\ncallsign"),
                "a callsign is user-typed text; the codec must escape it, not trust it");
            Assert.That(mirror.Players[0].Ready, Is.True);

            var hero = mirror.Heroes["hero_1"];
            Assert.That(hero.Pos.X, Is.EqualTo(-3.25).Within(SimTolerance));
            Assert.That(hero.Pos.Y, Is.EqualTo(7.5).Within(SimTolerance));
            Assert.That(hero.Hp, Is.EqualTo(62.5).Within(SimTolerance));
            Assert.That(hero.MaxHp, Is.EqualTo(100.0).Within(SimTolerance));
            Assert.That(hero.Alive, Is.False, "a downed hero mirrors downed (R-33's grey overlay)");
            Assert.That(hero.RespawnAt, Is.EqualTo(42.125).Within(SimTolerance),
                "the respawn deadline crosses — the client draws 'Respawning in Ns' from it");

            Assert.That(mirror.Monsters["m1"].Type, Is.EqualTo(MonsterType.Spitter));
            Assert.That(mirror.Monsters["m1"].Hp, Is.EqualTo(12.0).Within(SimTolerance));
            Assert.That(mirror.Monsters["m_dead"].Alive, Is.False,
                "corpses mirror as corpses — the binder is what releases their views");

            Assert.That(mirror.Hotspots["hs_chapel"].Civilians, Is.EqualTo(4),
                "civilian counts are host truth");
            Assert.That(mirror.Hotspots["hs_chapel"].Pos.X, Is.EqualTo(11.0).Within(SimTolerance),
                "hotspot geometry is map truth — the snapshot must not zero it");

            var placeable = mirror.Placeables["pl_1"];
            Assert.That(placeable.Type, Is.EqualTo(PlaceableType.SpikeTrap));
            Assert.That(placeable.OwnerPlayerId, Is.EqualTo("player_1"));
            Assert.That(placeable.PurchaseCost, Is.EqualTo(75), "the R-22 sell tooltip needs it");
            Assert.That(placeable.TriggersRemaining, Is.EqualTo(7));
            Assert.That(placeable.Exists, Is.True);
        }

        [Test]
        public void A_snapshot_is_the_whole_truth_so_stale_mirror_entities_vanish()
        {
            var state = new MatchState { Phase = MatchPhase.Combat, Status = MatchStatus.InProgress };

            var mirror = new MatchState();
            mirror.Monsters["m_stale"] = new Monster { Id = "m_stale", Type = MonsterType.Shambler };
            mirror.Placeables["pl_sold_and_gone"] = new Placeable { Id = "pl_sold_and_gone" };
            mirror.Heroes["h_stale"] = new Hero { Id = "h_stale" };

            MatchSnapshot.Apply(MatchSnapshot.Capture(state, 0.0), mirror);

            Assert.That(mirror.Monsters, Is.Empty,
                "replace-all: an entity the host no longer carries leaves the mirror with it");
            Assert.That(mirror.Placeables, Is.Empty);
            Assert.That(mirror.Heroes, Is.Empty);
        }

        // ==========================================================================================
        //  the remote seat — a client's held input plays through the host's own seams
        // ==========================================================================================

        /// <summary>
        /// The headline: a remote player's whole in-match verb set, over the wire, against the
        /// REAL factory-built match. Held W walks the guest hero through the session's one intent
        /// seam; held SPACE at an aim point kills a monster with a HOST-built line, credited to
        /// the guest (R-40); the mirror tracks all of it from snapshots alone.
        /// </summary>
        [Test]
        public void A_remote_player_moves_fights_and_is_credited_through_the_wire()
        {
            var rig = NewReplicatedMatch();

            // ---- move: held W crosses as a direction and the sim paces it (R-30) ----------------
            rig.Input.Held.Add(PlayerKey.W);
            var guestHero = HeroFor(rig.Match.State, GuestAccount);
            var before = guestHero.Pos;

            PumpBoth(rig, steps: 30);

            Assert.That(guestHero.Pos.Y, Is.GreaterThan(before.Y),
                "R-30/R-50: the remote peer's held W walks ITS hero forward on the host");
            Assert.That(guestHero.Pos.X, Is.EqualTo(before.X).Within(SimTolerance),
                "and only forward — the wire carries direction, never a speed or a diagonal bonus");

            var localHero = HeroFor(rig.Match.State, HostAccount);
            Assert.That(localHero.Pos, Is.EqualTo(new Vec2(0.0, 0.0)),
                "the remote intent is addressed by hero id — it must never walk the host's hero");

            // ---- fight: held SPACE fires host-built aim lines at the aim point (R-51) -----------
            rig.Input.Held.Remove(PlayerKey.W);
            var victim = NearestLivingMonster(rig.Match.State, guestHero.Pos);
            victim.Hp = 10.0; // one basic (25) finishes it whatever the class math says
            rig.Input.Cursor = new Vector2((float)victim.Pos.X, (float)victim.Pos.Y);
            rig.Input.Held.Add(PlayerKey.Space);

            var scripBefore = rig.Match.State.Team.Scrip;
            PumpBoth(rig, steps: 30);

            Assert.That(victim.Alive, Is.False,
                "R-26: the remote basic resolved along a HOST-built line and dropped the monster");
            Assert.That(rig.Match.State.Wave.LivingMonsterIds, Does.Not.Contain(victim.Id),
                "R-02: the kill was reaped through RecordMonsterKill — the wave can still clear");
            Assert.That(rig.Match.State.Team.Scrip, Is.GreaterThan(scripBefore),
                "R-20: the bounty landed in the shared pool");
            Assert.That(rig.Profiles.Load(GuestAccount).LifetimeXp, Is.GreaterThan(0.0),
                "R-40: the kill's XP credits the REMOTE account that landed it");

            // ---- mirror: the client is looking at all of it from snapshots alone ----------------
            Assert.That(rig.Presenter.Live, Is.True, "snapshots have arrived");
            var mirrorHero = rig.Presenter.Mirror.Heroes.Values
                .First(h => h.AccountId == GuestAccount);
            Assert.That(mirrorHero.Pos.Y, Is.EqualTo(guestHero.Pos.Y).Within(1e-6),
                "R-52: the mirror stands where the host says the hero stands");
            Assert.That(rig.Presenter.Mirror.Monsters[victim.Id].Alive, Is.False,
                "the kill is visible client-side");
            Assert.That(rig.Presenter.Mirror.Team.Scrip, Is.EqualTo(rig.Match.State.Team.Scrip),
                "the shared pool mirrors exactly");
        }

        /// <summary>
        /// R-21/R-24/R-25 over the wire: the guest's BUY lands as the guest's own player slot
        /// through the sim's own gates, READY readies only the guest's slot, and the R-63
        /// countdown crosses host-computed. The host's ready then opens combat (R-03).
        /// </summary>
        [Test]
        public void A_remote_player_shops_and_readies_through_the_sims_own_gates()
        {
            var rig = NewReplicatedMatch();

            // Clear wave 1 the harness way; the session's next step opens wave 2's planning.
            foreach (var id in rig.Match.State.Wave.LivingMonsterIds.ToList())
            {
                rig.Match.Sim.RecordMonsterKill(new MonsterKillRequest
                {
                    MonsterId = id,
                    MonsterType = rig.Match.State.Monsters[id].Type,
                    Bounty = 0,
                });
            }

            PumpBoth(rig, steps: 2);
            Assert.That(rig.Match.State.Phase, Is.EqualTo(MatchPhase.Planning),
                "sanity (R-02/G-016): the cleared wave opened planning");

            Assert.That(rig.Presenter.PlanningRemainingSeconds, Is.GreaterThan(0.0),
                "R-63: the client shows the countdown the HOST computed");

            // ---- the guest buys a spike trap on open ground ------------------------------------
            var scripBefore = rig.Match.State.Team.Scrip;
            rig.Presenter.RequestPurchase(PlaceableType.SpikeTrap, new Vec2(5.0, 5.0));
            PumpBoth(rig, steps: 2);

            var bought = rig.Match.State.Placeables.Values
                .FirstOrDefault(p => p.Exists && p.Type == PlaceableType.SpikeTrap);
            Assert.That(bought, Is.Not.Null,
                "R-21: the remote BUY landed through PurchasePlacement's own gates");
            Assert.That(bought.OwnerPlayerId,
                Is.EqualTo(SlotFor(rig.Match.State, GuestAccount)),
                "R-25/R-40: the entity records the GUEST's slot — turret credit depends on it");
            Assert.That(rig.Match.State.Team.Scrip, Is.LessThan(scripBefore),
                "R-20: the shared pool paid for it");
            Assert.That(rig.Presenter.Mirror.Placeables.Values.Any(
                    p => p.Id == bought.Id && p.Exists), Is.True,
                "the purchase is visible client-side");

            // ---- ready: guest over the wire, host locally, combat opens early (R-03) -----------
            rig.Presenter.ReadyUp();
            PumpBoth(rig, steps: 2);

            Assert.That(rig.Match.State.Phase, Is.EqualTo(MatchPhase.Planning),
                "one ready of two connected players is not all of them (G-017)");

            rig.Match.Sim.SetPlayerReady(SlotFor(rig.Match.State, HostAccount));
            PumpBoth(rig, steps: 2);

            Assert.That(rig.Match.State.Phase, Is.EqualTo(MatchPhase.Combat),
                "R-03: every connected player ready — combat opens early");
            Assert.That(rig.Presenter.Mirror.Phase, Is.EqualTo(MatchPhase.Combat),
                "and the client sees the phase turn");
        }

        /// <summary>
        /// R-31/R-32 over the wire: a cast press-edge crosses ONCE and resolves on the host with
        /// its own rules. The guest's Q is rank-1 fanned fire, so the aim-line target loses HP.
        /// </summary>
        [Test]
        public void A_remote_cast_edge_crosses_once_and_resolves_host_side()
        {
            var rig = NewReplicatedMatch();
            var guestHero = HeroFor(rig.Match.State, GuestAccount);
            guestHero.Abilities[AbilitySlot.Q] = 1; // an account that unlocked Q (R-42)

            var castTokensOnTheWire = 0;
            rig.Channel.CommandReceived += (peer, payload) =>
            {
                if (payload.Contains("\"cast\""))
                {
                    castTokensOnTheWire++;
                }
            };

            var victim = NearestLivingMonster(rig.Match.State, guestHero.Pos);
            var hpBefore = victim.Hp;
            rig.Input.Cursor = new Vector2((float)victim.Pos.X, (float)victim.Pos.Y);
            rig.Input.Held.Add(PlayerKey.Q);

            PumpBoth(rig, steps: 10);

            Assert.That(victim.Hp, Is.LessThan(hpBefore),
                "R-31: the remote Q resolved on the host against the host-built line");

            // The key stayed held for ten pumps. Cooldown-rejection events are not replicated
            // yet, so a client that spammed the edge would rack up silent refusals — the pin is
            // that a level-held key crosses as ONE cast token (T-25's press-edge, wire edition).
            Assert.That(castTokensOnTheWire, Is.EqualTo(1),
                "wire hygiene: one press is one cast token, however long the key stays held");
        }

        // ==========================================================================================
        //  LAN services — the cloudless NGO bring-up (ticket 030's "NGO on loopback")
        // ==========================================================================================

        /// <summary>
        /// R-50 one layer further down: <see cref="NgoNetTransport"/>'s WHOLE pinned bring-up —
        /// order, join refusal, teardown — runs over <see cref="LanServices"/> with no cloud
        /// anywhere: the "allocation" is a <see cref="LocalEndpoint"/>, the join code is the dial
        /// string, and a code the parser cannot read refuses exactly like an expired lobby (T-20's
        /// shape, so S1's inline error works unchanged).
        /// </summary>
        [Test]
        public void The_ngo_transport_hosts_and_joins_over_lan_services_with_no_cloud()
        {
            var hostWire = new RecordingWire();
            var host = new NgoNetTransport(new LanServices(), hostWire);

            host.StartHost(new NetSessionConfig());

            Assert.That(host.IsRunning, Is.True, "the LAN host came up");
            Assert.That(host.JoinCode, Is.EqualTo(LanServices.CodePrefix),
                "the default dial string IS the join code the S2 screen shows");
            var endpoint = hostWire.LastEndpoint as LocalEndpoint;
            Assert.That(endpoint, Is.Not.Null, "the wire was handed a direct endpoint, not Relay");
            Assert.That(endpoint.Address, Is.EqualTo("127.0.0.1"));
            Assert.That(endpoint.Port, Is.EqualTo(LanServices.DefaultPort));

            var clientWire = new RecordingWire();
            var client = new NgoNetTransport(new LanServices(), clientWire);

            Assert.That(client.TryJoinAsClient(new NetSessionConfig(), "LAN"), Is.True,
                "the default code dials loopback");
            Assert.That(((LocalEndpoint)clientWire.LastEndpoint).Address, Is.EqualTo("127.0.0.1"));

            Assert.That(client.TryJoinAsClient(new NetSessionConfig(), "NOPE42"), Is.False,
                "a code the dial parser cannot read refuses like an expired lobby — never throws");

            var lanClient = new NgoNetTransport(new LanServices(), new RecordingWire());
            Assert.That(lanClient.TryJoinAsClient(new NetSessionConfig(), "LAN:192.168.0.12:7799"),
                Is.True, "an addressed code dials across the room");
        }

        /// <summary>Minimal T-20-shaped wire: records endpoints, carries no bytes.</summary>
        private sealed class RecordingWire : INetWire
        {
            public RelayEndpoint LastEndpoint;

            public bool IsUp { get; private set; }

            public event Action<string> PeerDisconnected
            {
                add { }
                remove { }
            }

            public void StartHost(RelayEndpoint endpoint)
            {
                LastEndpoint = endpoint;
                IsUp = true;
            }

            public void StartClient(RelayEndpoint endpoint)
            {
                LastEndpoint = endpoint;
                IsUp = true;
            }

            public void Shutdown()
            {
                IsUp = false;
            }
        }

        // ==========================================================================================
        //  rig — one host, one remote seat, one wire
        // ==========================================================================================

        private sealed class Rig
        {
            public NetSession Session;
            public HostedMatch Match;
            public RemotePartyDriver Driver;
            public ClientMatchPresenter Presenter;
            public FakeInputSource Input;
            public InMemoryMatchChannel Channel;
            public SnapshottingStore Profiles;
        }

        /// <summary>
        /// The production wiring in miniature: a loopback 2-player session (T-11's shape), the
        /// match's session REBUILT over a composite intent source so the remote driver feeds the
        /// same seam a local device does, and the channel pair carrying snapshots one way and
        /// commands the other.
        /// </summary>
        private static Rig NewReplicatedMatch()
        {
            var profiles = new SnapshottingStore();
            var session = new NetSession(
                new NetSessionConfig(),
                new LoopbackNetTransport(),
                new ColonyMatchFactory(ColonyMap.V1(), new SimConfig(), profiles));

            session.StartHost(new NetPeer
            {
                PeerId = HostPeerId,
                AccountId = HostAccount,
                HeroClass = HeroClass.Gunslinger,
                IsHost = true,
            });
            Assert.That(session.TryJoin(new NetPeer
            {
                PeerId = GuestPeerId,
                AccountId = GuestAccount,
                HeroClass = HeroClass.Gunslinger,
            }), Is.True, "sanity (R-50): the guest takes a seat");
            Assert.That(session.TryStartMatch(HostPeerId), Is.True, "the host starts the match");

            var match = session.Match;

            var driver = new RemotePartyDriver();
            driver.SeatPeer(GuestPeerId, GuestAccount);

            // The shell's ViewBoundMatchFactory move, test-side: one session over the composite
            // seam (no local device here — the host player is idle).
            match.Session = new MatchSession(
                match.Host, new CompositeHeroIntentSource(driver));

            var channel = new InMemoryMatchChannel();
            channel.CommandReceived += driver.HandleCommand;

            var input = new FakeInputSource();
            var presenter = new ClientMatchPresenter(channel.Connect(GuestPeerId), input);

            return new Rig
            {
                Session = session,
                Match = match,
                Driver = driver,
                Presenter = presenter,
                Input = input,
                Channel = channel,
                Profiles = profiles,
            };
        }

        /// <summary>
        /// One replicated frame each side, the production order: client pumps (applies the last
        /// snapshot, sends held input), host applies remote combat/planning, host steps the
        /// session, host broadcasts.
        /// </summary>
        private static void PumpBoth(Rig rig, int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                rig.Presenter.Pump(Step60Hz);
                rig.Driver.Step(rig.Match, Step60Hz);
                rig.Session.Step(Step60Hz);
                rig.Driver.BroadcastSnapshot(rig.Match, rig.Channel);
            }
        }

        private static Hero HeroFor(MatchState state, string accountId)
        {
            var hero = state.Heroes.Values.FirstOrDefault(h => h.AccountId == accountId);
            Assert.That(hero, Is.Not.Null, "sanity: a hero is seated for " + accountId);
            return hero;
        }

        private static string SlotFor(MatchState state, string accountId)
        {
            var slot = state.Players.FirstOrDefault(p => p.AccountId == accountId);
            Assert.That(slot, Is.Not.Null, "sanity: a slot is seated for " + accountId);
            return slot.Id;
        }

        private static Monster NearestLivingMonster(MatchState state, Vec2 from)
        {
            Monster nearest = null;
            var best = double.MaxValue;
            foreach (var monster in state.Monsters.Values)
            {
                if (!monster.Alive)
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

            Assert.That(nearest, Is.Not.Null, "sanity (R-19): the wave holds a living monster");
            return nearest;
        }

        /// <summary>A scripted device (T-25's fake): held keys plus a cursor ground point.</summary>
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

        /// <summary>An in-memory store the tests can read XP back out of (T-11's shape).</summary>
        private sealed class SnapshottingStore : IProfileStore
        {
            private readonly InMemoryProfileStore _inner = new InMemoryProfileStore();

            public AccountProfile Load(string accountId) => _inner.Load(accountId);

            public void Save(AccountProfile profile) => _inner.Save(profile);
        }
    }
}
