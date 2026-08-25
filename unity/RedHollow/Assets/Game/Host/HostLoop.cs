using System;

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
    ///
    /// SHAPE ONLY (ticket 010, TDD stub) — implementation belongs to the implementing agent.
    /// </summary>
    public sealed class HostLoop
    {
        public HostLoop(ISimHost sim, IMonsterAttackSource monsterAttacks = null)
        {
            if (sim == null)
            {
                throw new ArgumentNullException(nameof(sim));
            }
        }

        /// <summary>
        /// Advance the match by <paramref name="deltaSeconds"/> of sim time.
        ///
        /// The PRD does not order the ticks against one another, so nothing here should be read as
        /// pinning that order. What IS load-bearing: the R-18 gate is asked BEFORE the damage
        /// command it guards, and a refused gate issues no damage command at all — asking after the
        /// hit lands 60 hits a second and the colony falls inside wave 1.
        /// </summary>
        public void Step(double deltaSeconds) =>
            throw new NotImplementedException(
                "T-10 not implemented: HostLoop.Step must advance the clock, drive every sim tick "
                + "the sim cannot schedule itself (R-03/R-23/R-31/R-33/R-35), and gate every "
                + "monster attack through TryMonsterAttack before applying damage (R-18)");
    }
}
