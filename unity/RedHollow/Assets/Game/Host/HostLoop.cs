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

        private readonly ISimHost _sim;
        private readonly IMonsterAttackSource _monsterAttacks;

        public HostLoop(ISimHost sim, IMonsterAttackSource monsterAttacks = null)
        {
            if (sim == null)
            {
                throw new ArgumentNullException(nameof(sim));
            }

            _sim = sim;

            // Optional on purpose: a host with no attack source (a lobby, a planning-only harness)
            // still has to drive the five ticks, so a missing source must not disable the loop.
            _monsterAttacks = monsterAttacks;
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

            ResolveMonsterAttacks(deltaSeconds);
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
