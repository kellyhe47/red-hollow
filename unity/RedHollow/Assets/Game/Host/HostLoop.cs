using System;
using System.Collections.Generic;
using RedHollow.Sim;

namespace RedHollow.Game.Host
{
    /// <summary>
    /// The host-side spine (R-50, R-51). One <see cref="Step"/> is one fixed host step: it advances
    /// sim time, pumps every sim tick the sim cannot schedule for itself, and turns this step's
    /// monster attack intents into damage — each one gated through
    /// <see cref="ISimHost.TryMonsterAttack"/> first (R-18).
    ///
    /// Plain C#, not a MonoBehaviour, and that is the point of the ticket: no game rule may live in
    /// a MonoBehaviour, so the component (<see cref="MatchHostBehaviour"/>) does nothing but call
    /// <see cref="Step"/>. Being plain C# is also what makes it drivable from EditMode tests without
    /// a scene.
    /// </summary>
    public sealed class HostLoop
    {
        /// <summary>
        /// What a step resolves when there is no attack source, or the source returns null. Shared
        /// and empty so the quiet path allocates nothing sixty times a second.
        /// </summary>
        private static readonly IReadOnlyList<MonsterAttackIntent> NoAttacks = new MonsterAttackIntent[0];

        /// <summary>
        /// What a step resolves when there is no hero intent source, or the source returns null.
        /// Shared and empty for the same reason <see cref="NoAttacks"/> is.
        /// </summary>
        private static readonly IReadOnlyList<HeroIntentCommand> NoIntents = new HeroIntentCommand[0];

        private readonly ISimHost _sim;

        /// <summary>
        /// Ticket 019 — the same host, seen through the wider seam, or null when the caller bound a
        /// bare <see cref="ISimHost"/>. Tested for rather than demanded, because widening the loop's
        /// own dependency would break every harness that drives a planning-only or lobby host
        /// (T-10's fake among them): the five ticks above must keep running for a host that cannot
        /// move a monster, and a loop that refused to construct without the wider seam would make
        /// "drive the ticks" conditional on "drive a live match".
        /// </summary>
        private readonly IMatchSimHost _matchSim;

        private readonly IMonsterAttackSource _monsterAttacks;
        private readonly IHeroIntentSource _heroIntents;

        /// <summary>
        /// R-23 — 20 damage per TurretTick at 20 DPS means one call per second. Accumulated here
        /// rather than inside the sim: TurretTick has no cadence of its own (G-028 is "on its
        /// damage tick"), and inventing a TickTurrets() on MatchSim would be a new rule.
        /// </summary>
        private const double TurretTickIntervalSeconds = 1.0;

        private double _turretAcc;

        /// <summary>
        /// Trap/monster pairs overlapping last step, keyed "placeableId\0monsterId". TriggerPlaceable
        /// spends a spike trigger per CALL, so occupancy must not re-fire every frame — only the
        /// enter (a crossing) issues the command.
        /// </summary>
        private readonly HashSet<string> _onTrap = new HashSet<string>();

        private readonly HashSet<string> _onTrapNow = new HashSet<string>();

        private readonly List<string> _scratchIds = new List<string>();

        /// <param name="heroIntents">
        /// Ticket 019 / R-30 — where this step's resolved hero intents come from. Optional for the
        /// same reason <paramref name="monsterAttacks"/> is: a host driving no player-controlled
        /// hero (a headless harness, a lobby) still has to drive every tick.
        /// </param>
        public HostLoop(
            ISimHost sim,
            IMonsterAttackSource monsterAttacks = null,
            IHeroIntentSource heroIntents = null)
        {
            if (sim == null)
            {
                throw new ArgumentNullException(nameof(sim));
            }

            _sim = sim;
            _matchSim = sim as IMatchSimHost;

            // Optional on purpose: a host with no attack source (a lobby, a planning-only harness)
            // still has to drive the five ticks, so a missing source must not disable the loop.
            _monsterAttacks = monsterAttacks;

            _heroIntents = heroIntents;
        }

        /// <summary>
        /// Advance the match by <paramref name="deltaSeconds"/> of sim time.
        ///
        /// The PRD does not order the ticks against one another, so nothing here should be read as
        /// pinning that order. What IS load-bearing: the R-18 gate is asked BEFORE the damage
        /// command it guards, and a refused gate issues no damage command at all — asking after the
        /// hit lands 60 hits a second and the colony falls inside wave 1.
        ///
        /// The clock moves first. Every deadline in the sim (R-03 planning, R-31 status expiry,
        /// R-33 respawn, R-35 regen, R-23 med-station aura) is read off the injected clock at the
        /// moment its tick runs, so ticking a clock that has not moved is a no-op step.
        ///
        /// Nothing here writes <see cref="ISimHost.State"/>: every mutation below is a
        /// <see cref="MatchSim"/> command call (R-51).
        /// </summary>
        public void Step(double deltaSeconds)
        {
            _sim.AdvanceClock(deltaSeconds);

            _sim.TickPlanningTimer();   // R-03 — planning ends on its own clock, not on readiness alone.
            _sim.TickStatusEffects();   // R-31 — slows, guards and burns expire.
            _sim.TickHeroRegen();       // R-35 — out-of-combat healing.
            _sim.TickHeroRespawns();    // R-33 — dead heroes come back.
            _sim.TickMedStations();     // R-23 — the purchased aura heals.

            // R-17 / R-18 — the wave walks. Driven with the delta this step was handed and never
            // with a tick rate of the loop's own: a host that caught up a stalled frame with a
            // 0.5s step would otherwise advance the sim by 1/60s and run the match slower than its
            // own clock for the rest of the session.
            if (_matchSim != null)
            {
                _matchSim.TickMonsterMovement(deltaSeconds);
            }

            ResolveHeroMoves(deltaSeconds);

            // After movement so a monster that walked into range / onto a trap this step is
            // eligible the same tick. Gated on IMatchSimHost the same way TickMonsterMovement is:
            // a bare ISimHost (T-10's recording fake) has no per-entity placeable commands.
            TickTurrets(deltaSeconds);
            TriggerTrapCrossings();

            ResolveMonsterAttacks(deltaSeconds);
        }

        /// <summary>
        /// R-23 / G-028. One TurretTick per standing turret per second. The sim picks the nearest
        /// living monster in range; an empty-sky tick is a defined no-op.
        /// </summary>
        private void TickTurrets(double deltaSeconds)
        {
            if (_matchSim == null || _matchSim.State == null)
            {
                return;
            }

            _turretAcc += deltaSeconds;
            if (_turretAcc < TurretTickIntervalSeconds)
            {
                return;
            }

            _turretAcc -= TurretTickIntervalSeconds;

            _scratchIds.Clear();
            foreach (var placeable in _matchSim.State.Placeables.Values)
            {
                if (placeable != null
                    && placeable.Exists
                    && placeable.Type == PlaceableType.Turret)
                {
                    _scratchIds.Add(placeable.Id);
                }
            }

            for (var i = 0; i < _scratchIds.Count; i++)
            {
                _matchSim.TurretTick(_scratchIds[i]);
            }
        }

        /// <summary>
        /// R-23 / G-027 / G-029. A monster whose centre has just entered a standing trap's
        /// footprint (the existing R-24 occupancy radius) is a crossing. Inclusive, matching
        /// G-019's boundary convention. Re-issue is suppressed while the pair stays overlapping
        /// so a 10-trigger spike is not spent in ten frames.
        /// </summary>
        private void TriggerTrapCrossings()
        {
            if (_matchSim == null || _matchSim.State == null)
            {
                return;
            }

            var state = _matchSim.State;
            var radius = _matchSim.PlaceableFootprintRadius;
            _onTrapNow.Clear();

            foreach (var placeable in state.Placeables.Values)
            {
                if (placeable == null || !placeable.Exists)
                {
                    continue;
                }

                if (placeable.Type != PlaceableType.SpikeTrap
                    && placeable.Type != PlaceableType.DynamiteTrap)
                {
                    continue;
                }

                foreach (var monster in state.Monsters.Values)
                {
                    if (monster == null || !monster.Alive)
                    {
                        continue;
                    }

                    if (placeable.Pos.DistanceTo(monster.Pos) > radius)
                    {
                        continue;
                    }

                    var key = placeable.Id + "\0" + monster.Id;
                    _onTrapNow.Add(key);
                    if (_onTrap.Contains(key))
                    {
                        continue;
                    }

                    _matchSim.TriggerPlaceable(placeable.Id, monster.Id);
                }
            }

            _onTrap.Clear();
            foreach (var key in _onTrapNow)
            {
                _onTrap.Add(key);
            }
        }

        /// <summary>
        /// R-30 / R-51. Each intent this step becomes one <see cref="IMatchSimHost.MoveHero"/>
        /// command for the hero it names, carrying this step's own delta. A loop with no intent
        /// source drives no hero, which is why the null case returns rather than throws.
        ///
        /// A straight copy, the way <see cref="ApplyPermittedAttack"/> is: the direction goes over
        /// exactly as the input map resolved it (R-30), because the only other thing the shell
        /// could send is the aim point — and steering by the cursor is the click-to-move DEC-017
        /// ruled out. Speed is not carried at all; the sim owns it.
        ///
        /// The command is addressed by <see cref="HeroIntentCommand.HeroId"/> rather than applied
        /// to "the hero": a host drives up to four (R-50), and an intent that moved all of them
        /// would let one player walk the whole party.
        /// </summary>
        private void ResolveHeroMoves(double deltaSeconds)
        {
            if (_heroIntents == null || _matchSim == null)
            {
                return;
            }

            var commands = _heroIntents.IntentsThisStep(_sim, deltaSeconds) ?? NoIntents;

            for (var i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                if (command == null || command.Intent == null || command.HeroId == null)
                {
                    continue;
                }

                var direction = command.Intent.MoveDirection;

                // Nobody is holding a key. Skipped rather than sent as a zero command so a quiet
                // frame costs no observation — MatchSim.MoveHero would refuse it anyway, but the
                // refusal still resets LastObservation, which netcode replicates from (R-51).
                if (direction.x == 0f && direction.y == 0f)
                {
                    continue;
                }

                _matchSim.MoveHero(new HeroMoveRequest
                {
                    HeroId = command.HeroId,
                    Direction = new Vec2(direction.x, direction.y),
                    DeltaSeconds = deltaSeconds,
                });
            }
        }

        /// <summary>
        /// R-18. Every candidate this step is a *request to swing*, never a hit: the sim owns the
        /// cadence and answers it from <see cref="ISimHost.TryMonsterAttack"/>, which is ask-and-claim
        /// — a yes consumes the swing. Gating before the damage op also protects
        /// <see cref="ISimHost.LastObservation"/>, which each command resets and netcode replicates
        /// from: a gate asked afterwards has already applied a hit it may not have been owed.
        /// </summary>
        private void ResolveMonsterAttacks(double deltaSeconds)
        {
            if (_monsterAttacks == null)
            {
                return;
            }

            var intents = _monsterAttacks.AttacksReadyThisStep(_sim, deltaSeconds) ?? NoAttacks;

            for (var i = 0; i < intents.Count; i++)
            {
                var intent = intents[i];
                if (intent == null)
                {
                    continue;
                }

                if (!_sim.TryMonsterAttack(intent.MonsterId))
                {
                    continue;
                }

                ApplyPermittedAttack(intent);
            }
        }

        /// <summary>
        /// Routes one permitted swing to the single damage command for its target kind (R-11 for a
        /// shelter, R-33 for a hero, R-16/R-23 for a placeable). A straight copy of the intent's
        /// fields into the sim's own request type — the shell computes no damage number, because
        /// <see cref="MonsterAttackIntent.Damage"/> came from the R-17 catalog on
        /// <see cref="SimConfig.Monsters"/> and the arithmetic that consumes it is the sim's.
        /// </summary>
        private void ApplyPermittedAttack(MonsterAttackIntent intent)
        {
            switch (intent.TargetKind)
            {
                case TargetKind.Hotspot:
                    _sim.ApplyHotspotAttack(new HotspotAttackRequest
                    {
                        AttackerId = intent.MonsterId,
                        AttackerType = intent.MonsterType,
                        Damage = intent.Damage,
                        TargetId = intent.TargetId,
                    });
                    break;

                case TargetKind.Hero:
                    _sim.ApplyHeroDamage(new HeroDamageRequest
                    {
                        AttackerId = intent.MonsterId,
                        AttackerType = intent.MonsterType,
                        Damage = intent.Damage,
                        TargetId = intent.TargetId,
                    });
                    break;

                case TargetKind.Barricade:
                    _sim.ApplyPlaceableDamage(new PlaceableDamageRequest
                    {
                        AttackerId = intent.MonsterId,
                        AttackerType = intent.MonsterType,
                        Damage = intent.Damage,
                        TargetId = intent.TargetId,
                    });
                    break;

                default:
                    // A target kind with no damage command is a wiring bug, not a game state: the
                    // gate has already consumed the swing, so dropping it silently would cost a hit
                    // nobody could account for.
                    throw new ArgumentOutOfRangeException(
                        nameof(intent),
                        intent.TargetKind,
                        "no damage command is defined for this target kind");
            }
        }
    }
}
