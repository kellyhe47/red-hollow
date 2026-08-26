using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RedHollow.Game.Host;
using RedHollow.Game.Input;
using RedHollow.Game.UI;
using RedHollow.Sim;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 030 — the wire format a remote party member's play crosses in. One INPUT message
    /// carries the whole held state (direction, aim, attack held, one consumed cast edge) exactly
    /// as the local shell samples its own device per pump; BUY / SELL / READY are the planning
    /// clicks. JSON, like <see cref="MatchSnapshot"/>, and built/parsed by the same reader.
    /// </summary>
    public static class RemoteCommands
    {
        public const string Input = "input";
        public const string Buy = "buy";
        public const string Sell = "sell";
        public const string Ready = "ready";

        public static string InputState(
            double moveX, double moveY, double aimX, double aimY, bool attack, string castSlot)
        {
            var sb = new StringBuilder(160);
            sb.Append("{\"t\":\"").Append(Input).Append('"');
            sb.Append(",\"mx\":").Append(moveX.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"my\":").Append(moveY.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"ax\":").Append(aimX.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"ay\":").Append(aimY.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"atk\":").Append(attack ? "true" : "false");
            if (!string.IsNullOrEmpty(castSlot))
            {
                sb.Append(",\"cast\":\"").Append(castSlot).Append('"');
            }

            sb.Append('}');
            return sb.ToString();
        }

        public static string BuyAt(string placeableType, double x, double y)
        {
            return "{\"t\":\"" + Buy + "\",\"type\":\"" + placeableType + "\",\"x\":"
                + x.ToString("R", CultureInfo.InvariantCulture) + ",\"y\":"
                + y.ToString("R", CultureInfo.InvariantCulture) + "}";
        }

        public static string SellPlaceable(string placeableId)
        {
            return "{\"t\":\"" + Sell + "\",\"id\":\"" + placeableId + "\"}";
        }

        public static string ReadyUp()
        {
            return "{\"t\":\"" + Ready + "\"}";
        }
    }

    /// <summary>
    /// Ticket 030 — the host-side half of a remote party member's PLAY (R-50/R-51): what the
    /// local player does through <c>ShellBootstrap</c>'s own routing, a remote player does
    /// through this class, over the same sim seams and the same rules.
    ///
    ///  * <b>Movement</b> rides the session's one hero-intent seam: this class is an
    ///    <see cref="IHeroIntentSource"/> contributing each remote peer's held direction, so
    ///    <see cref="HostLoop"/> paces a remote hero exactly as it paces the local one (R-30 —
    ///    the sim owns speed; a message carries direction only).
    ///  * <b>Attacks and casts</b> mirror the shell's combat routing: attack held re-fires on the
    ///    shipped cadence, the aim line is built HOST-side from the peer's aim point (the server
    ///    never trusts a client's raycast — R-51), and kills are reaped through
    ///    <c>RecordMonsterKill</c> crediting the remote hero (R-40).
    ///  * <b>Planning clicks</b> (buy / sell / ready) go to the same commands the planning model
    ///    issues, addressed by the peer's own player slot; the sim's phase/scrip/zone gates stay
    ///    the only judges (R-21/R-24/R-25).
    ///
    /// Plain C#, no Unity type anywhere, so every decision above runs in the headless suite. The
    /// shell's integration is three calls: <see cref="HandleCommand"/> from the channel,
    /// <see cref="Step"/> once per pump, <see cref="BroadcastSnapshot"/> after the step.
    /// </summary>
    public sealed class RemotePartyDriver : IHeroIntentSource
    {
        /// <summary>One remote peer's held input, replaced by each INPUT message.</summary>
        private sealed class PeerInput
        {
            public double MoveX;
            public double MoveY;
            public double AimX;
            public double AimY;
            public bool AttackHeld;

            /// <summary>One pending cast edge ("Q"/"E"), consumed by the next step.</summary>
            public string PendingCast;

            /// <summary>The shell's attack pacing, per peer (T-25's clock, one per remote).</summary>
            public double AttackClock;
            public bool AttackWasHeld;

            public readonly List<string> PendingBuys = new List<string>();
            public readonly List<string> PendingSells = new List<string>();
            public bool PendingReady;
        }

        private readonly Dictionary<string, PeerInput> _peers =
            new Dictionary<string, PeerInput>(StringComparer.Ordinal);

        private readonly Dictionary<string, string> _accountsByPeer =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly CombatActionConfig _combatActions;
        private readonly List<HeroIntentCommand> _intents = new List<HeroIntentCommand>();
        private readonly List<string> _reapScratch = new List<string>();

        /// <summary>
        /// Ran after every sim command this driver issues, so the shell can drain its event tap
        /// (the same per-command drain its own combat routing does). Null in headless drives.
        /// </summary>
        public Action AfterCommand { get; set; }

        public RemotePartyDriver(CombatActionConfig combatActions = null)
        {
            _combatActions = combatActions ?? new CombatActionConfig();
        }

        /// <summary>
        /// Seat a remote peer: which account its messages play as. Called when the session admits
        /// the peer (the same moment <see cref="NetSession.TryJoin"/> said yes).
        /// </summary>
        public void SeatPeer(string peerId, string accountId)
        {
            if (string.IsNullOrEmpty(peerId) || string.IsNullOrEmpty(accountId))
            {
                return;
            }

            _accountsByPeer[peerId] = accountId;
            if (!_peers.ContainsKey(peerId))
            {
                _peers[peerId] = new PeerInput();
            }
        }

        /// <summary>Drop a departed peer's held input so nothing keeps walking its hero (R-53).</summary>
        public void DropPeer(string peerId)
        {
            if (peerId != null)
            {
                _peers.Remove(peerId);
                _accountsByPeer.Remove(peerId);
            }
        }

        /// <summary>One raw message off the channel. Unknown peers and malformed payloads are dropped.</summary>
        public void HandleCommand(string peerId, string payload)
        {
            if (peerId == null || payload == null || !_peers.TryGetValue(peerId, out var peer))
            {
                return;
            }

            Dictionary<string, object> node;
            try
            {
                node = SnapshotJson.Parse(payload);
            }
            catch (FormatException)
            {
                // A malformed packet is the wire's ordinary weather; it must never take the host
                // down or wedge the peer's held state.
                return;
            }

            switch (SnapshotJson.Str(node, "t"))
            {
                case RemoteCommands.Input:
                    peer.MoveX = SnapshotJson.Num(node, "mx", 0.0);
                    peer.MoveY = SnapshotJson.Num(node, "my", 0.0);
                    peer.AimX = SnapshotJson.Num(node, "ax", peer.AimX);
                    peer.AimY = SnapshotJson.Num(node, "ay", peer.AimY);
                    peer.AttackHeld = SnapshotJson.Bool(node, "atk");
                    var cast = SnapshotJson.Str(node, "cast");
                    if (!string.IsNullOrEmpty(cast))
                    {
                        peer.PendingCast = cast;
                    }

                    break;

                case RemoteCommands.Buy:
                    peer.PendingBuys.Add(payload);
                    break;

                case RemoteCommands.Sell:
                    var placeableId = SnapshotJson.Str(node, "id");
                    if (!string.IsNullOrEmpty(placeableId))
                    {
                        peer.PendingSells.Add(placeableId);
                    }

                    break;

                case RemoteCommands.Ready:
                    peer.PendingReady = true;
                    break;
            }
        }

        // ---- movement: the session's hero-intent seam --------------------------------------------

        /// <summary>
        /// R-30 — each seated remote peer's held direction, addressed to its own hero. Runs inside
        /// <see cref="HostLoop"/>'s step exactly as the local player's source does; compose the two
        /// with <see cref="CompositeHeroIntentSource"/>.
        /// </summary>
        public IReadOnlyList<HeroIntentCommand> IntentsThisStep(ISimHost sim, double deltaSeconds)
        {
            _intents.Clear();
            if (sim == null || sim.State == null)
            {
                return _intents;
            }

            foreach (var pair in _peers)
            {
                var hero = HeroForPeer(sim.State, pair.Key);
                if (hero == null)
                {
                    continue;
                }

                var peer = pair.Value;
                _intents.Add(new HeroIntentCommand
                {
                    HeroId = hero.Id,
                    Intent = new HeroIntent
                    {
                        MoveDirection = new UnityEngine.Vector2((float)peer.MoveX, (float)peer.MoveY),
                        AimPoint = new UnityEngine.Vector2((float)peer.AimX, (float)peer.AimY),
                        BasicAttack = peer.AttackHeld,
                    },
                });
            }

            return _intents;
        }

        // ---- everything else: one host step ------------------------------------------------------

        /// <summary>
        /// Apply every remote peer's held combat input and pending planning clicks to the live
        /// match — the remote mirror of <c>ShellBootstrap</c>'s HandleCombatActions plus the
        /// planning model's click handlers, once per pump.
        /// </summary>
        public void Step(HostedMatch match, double deltaSeconds)
        {
            if (match == null || match.State == null || match.Sim == null)
            {
                return;
            }

            foreach (var pair in _peers)
            {
                StepPeer(match, pair.Key, pair.Value, deltaSeconds);
            }
        }

        /// <summary>
        /// R-51 — capture and send the world every pump. The payload carries the R-63 countdown
        /// computed host-side, so a client renders the authoritative clock rather than guessing.
        /// </summary>
        public void BroadcastSnapshot(HostedMatch match, IHostMatchChannel channel)
        {
            if (match == null || match.State == null || channel == null)
            {
                return;
            }

            channel.Broadcast(MatchSnapshot.Capture(match.State, PlanningRemaining(match)));
        }

        private static double PlanningRemaining(HostedMatch match)
        {
            if (match.State.Phase != MatchPhase.Planning || match.Clock == null)
            {
                return 0.0;
            }

            var elapsed = match.Clock.ElapsedSeconds - match.State.PlanningStartedAt;
            var remaining = match.Sim.Config.PlanningDurationSeconds - elapsed;
            return remaining > 0.0 ? remaining : 0.0;
        }

        private void StepPeer(HostedMatch match, string peerId, PeerInput peer, double deltaSeconds)
        {
            var state = match.State;
            var sim = match.Sim;

            // Planning clicks first, so a BUY sent during planning lands before a timer flip.
            if (peer.PendingBuys.Count > 0 || peer.PendingSells.Count > 0 || peer.PendingReady)
            {
                var slotId = SlotForPeer(state, peerId);
                if (slotId != null)
                {
                    foreach (var buy in peer.PendingBuys)
                    {
                        var node = SnapshotJson.Parse(buy);
                        var type = SnapshotJson.Str(node, "type");
                        if (string.IsNullOrEmpty(type))
                        {
                            continue;
                        }

                        var stats = sim.Config.Placeables.TryGet(type);
                        sim.PurchasePlacement(new PurchaseRequest
                        {
                            PlayerId = slotId,
                            PlaceableType = type,
                            Cost = stats == null ? 0 : stats.Cost,
                            Pos = new Vec2(
                                SnapshotJson.Num(node, "x", 0.0), SnapshotJson.Num(node, "y", 0.0)),
                        });
                        Drained();
                    }

                    foreach (var placeableId in peer.PendingSells)
                    {
                        sim.SellPlacement(new SellRequest
                        {
                            PlayerId = slotId,
                            PlaceableId = placeableId,
                        });
                        Drained();
                    }

                    if (peer.PendingReady)
                    {
                        sim.SetPlayerReady(slotId);
                        Drained();
                    }
                }

                peer.PendingBuys.Clear();
                peer.PendingSells.Clear();
                peer.PendingReady = false;
            }

            // Combat actions — the shell's own gates, per remote peer (T-25's shape).
            if (state.IsOver || state.Phase != MatchPhase.Combat)
            {
                peer.AttackClock = 0.0;
                peer.AttackWasHeld = false;
                peer.PendingCast = null;
                return;
            }

            var hero = HeroForPeer(state, peerId);
            if (hero == null || !hero.Alive)
            {
                peer.AttackClock = 0.0;
                peer.AttackWasHeld = peer.AttackHeld;
                return;
            }

            var aim = new Vec2(peer.AimX, peer.AimY);

            if (peer.AttackHeld)
            {
                var fresh = !peer.AttackWasHeld;
                if (fresh)
                {
                    peer.AttackClock = 0.0;
                    FireBasic(sim, state, hero, aim);
                }
                else
                {
                    peer.AttackClock += deltaSeconds;
                    if (peer.AttackClock >= _combatActions.AttackCadenceSeconds)
                    {
                        peer.AttackClock -= _combatActions.AttackCadenceSeconds;
                        FireBasic(sim, state, hero, aim);
                    }
                }
            }
            else
            {
                peer.AttackClock = 0.0;
            }

            peer.AttackWasHeld = peer.AttackHeld;

            if (peer.PendingCast != null)
            {
                var slot = peer.PendingCast;
                peer.PendingCast = null;
                CastSlot(sim, state, hero, aim, slot);
            }
        }

        /// <summary>The shell's FireBasicAttack, for a remote hero: host-built line, catalog damage, reap.</summary>
        private void FireBasic(MatchSim sim, MatchState state, Hero hero, Vec2 aim)
        {
            var kit = sim.Config.HeroKits.KitFor(hero.HeroClass);
            var line = AimLine.EntitiesAlong(
                state, hero.Id, hero.Pos, aim,
                _combatActions.AimLineLength, _combatActions.AimLineWidth);

            sim.ResolveHeroAttack(new HeroAttackRequest
            {
                AttackerId = hero.Id,
                AttackerClass = hero.HeroClass,
                Damage = kit.BasicAttackDamage,
                EntitiesOnLine = line,
            });
            Drained();

            ReapHeroKills(sim, state, hero);
        }

        /// <summary>The shell's CastAbilitySlot, for a remote hero.</summary>
        private void CastSlot(MatchSim sim, MatchState state, Hero hero, Vec2 aim, string slot)
        {
            var line = AimLine.EntitiesAlong(
                state, hero.Id, hero.Pos, aim,
                _combatActions.AimLineLength, _combatActions.AimLineWidth);

            var dx = aim.X - hero.Pos.X;
            var dy = aim.Y - hero.Pos.Y;
            var magnitude = Math.Sqrt((dx * dx) + (dy * dy));
            var direction = magnitude > 0.0
                ? new Vec2(dx / magnitude, dy / magnitude)
                : new Vec2(0.0, 0.0);

            var outcome = sim.CastAbility(new HeroAbilityRequest
            {
                CasterId = hero.Id,
                Slot = slot,
                AimDirection = direction,
                EntitiesOnLine = line,
            });
            Drained();

            if (outcome != null && outcome.Accepted)
            {
                ReapHeroKills(sim, state, hero);
            }
        }

        /// <summary>The shell's ReapDeadMonsters, crediting the remote hero (R-02/R-20/R-40).</summary>
        private void ReapHeroKills(MatchSim sim, MatchState state, Hero attacker)
        {
            _reapScratch.Clear();
            _reapScratch.AddRange(state.Wave.LivingMonsterIds);

            foreach (var monsterId in _reapScratch)
            {
                if (!state.Monsters.TryGetValue(monsterId, out var monster)
                    || monster == null || !monster.Alive || monster.Hp > 0.0)
                {
                    continue;
                }

                var stats = sim.Config.Monsters.TryGet(monster.Type);
                var kill = new MonsterKillRequest
                {
                    MonsterId = monsterId,
                    MonsterType = monster.Type,
                    Bounty = stats == null ? 0 : stats.Bounty,
                    KillerHeroId = attacker.Id,
                };

                sim.RecordMonsterKill(kill);
                Drained();

                if (!string.IsNullOrEmpty(attacker.AccountId))
                {
                    sim.AwardKillXp(kill, attacker.AccountId);
                    Drained();
                }
            }
        }

        private void Drained()
        {
            var handler = AfterCommand;
            if (handler != null)
            {
                handler();
            }
        }

        private Hero HeroForPeer(MatchState state, string peerId)
        {
            if (!_accountsByPeer.TryGetValue(peerId, out var accountId) || accountId == null)
            {
                return null;
            }

            foreach (var hero in state.Heroes.Values)
            {
                if (hero != null && string.Equals(hero.AccountId, accountId, StringComparison.Ordinal))
                {
                    return hero;
                }
            }

            return null;
        }

        private string SlotForPeer(MatchState state, string peerId)
        {
            if (!_accountsByPeer.TryGetValue(peerId, out var accountId) || accountId == null)
            {
                return null;
            }

            foreach (var player in state.Players)
            {
                if (player != null
                    && string.Equals(player.AccountId, accountId, StringComparison.Ordinal))
                {
                    return player.Id;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// The one intent source a session takes, made of many: the local player's and the remote
    /// party's, in order. Null entries are tolerated so composition sites need no branching.
    /// </summary>
    public sealed class CompositeHeroIntentSource : IHeroIntentSource
    {
        private readonly IHeroIntentSource[] _sources;
        private readonly List<HeroIntentCommand> _merged = new List<HeroIntentCommand>();

        public CompositeHeroIntentSource(params IHeroIntentSource[] sources)
        {
            _sources = sources ?? new IHeroIntentSource[0];
        }

        public IReadOnlyList<HeroIntentCommand> IntentsThisStep(ISimHost sim, double deltaSeconds)
        {
            _merged.Clear();
            foreach (var source in _sources)
            {
                var intents = source == null ? null : source.IntentsThisStep(sim, deltaSeconds);
                if (intents == null)
                {
                    continue;
                }

                for (var i = 0; i < intents.Count; i++)
                {
                    if (intents[i] != null)
                    {
                        _merged.Add(intents[i]);
                    }
                }
            }

            return _merged;
        }
    }
}
