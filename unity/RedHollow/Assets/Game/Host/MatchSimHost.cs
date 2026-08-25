using System;
using RedHollow.Sim;

namespace RedHollow.Game.Host
{
    /// <summary>
    /// The one production <see cref="ISimHost"/>: a thin forwarder onto a real
    /// <see cref="MatchSim"/> plus the <see cref="SimClock"/> the host owns and advances (R-51).
    ///
    /// It exists only because <see cref="MatchSim"/> is sealed and its clock is injected rather
    /// than settable — there is no rule here and there must never be one. Every member is a
    /// forward.
    ///
    /// SHAPE ONLY (ticket 010, TDD stub) — implementation belongs to the implementing agent.
    /// </summary>
    public sealed class MatchSimHost : ISimHost
    {
        public MatchSimHost(MatchSim sim, SimClock clock)
        {
            if (sim == null)
            {
                throw new ArgumentNullException(nameof(sim));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }
        }

        public MatchState State => throw NotYet(nameof(State));

        public SimConfig Config => throw NotYet(nameof(Config));

        public IClock Clock => throw NotYet(nameof(Clock));

        public SimObservation LastObservation => throw NotYet(nameof(LastObservation));

        public void AdvanceClock(double deltaSeconds) => throw NotYet(nameof(AdvanceClock));

        public void TickPlanningTimer() => throw NotYet(nameof(TickPlanningTimer));

        public StatusTickResult TickStatusEffects() => throw NotYet(nameof(TickStatusEffects));

        public void TickHeroRegen() => throw NotYet(nameof(TickHeroRegen));

        public void TickHeroRespawns() => throw NotYet(nameof(TickHeroRespawns));

        public void TickMedStations() => throw NotYet(nameof(TickMedStations));

        public bool TryMonsterAttack(string monsterId) => throw NotYet(nameof(TryMonsterAttack));

        public HotspotAttackResult ApplyHotspotAttack(HotspotAttackRequest request) =>
            throw NotYet(nameof(ApplyHotspotAttack));

        public HeroDamageResult ApplyHeroDamage(HeroDamageRequest request) =>
            throw NotYet(nameof(ApplyHeroDamage));

        public PlaceableDamageResult ApplyPlaceableDamage(PlaceableDamageRequest request) =>
            throw NotYet(nameof(ApplyPlaceableDamage));

        private static NotImplementedException NotYet(string member) =>
            new NotImplementedException("T-10 not implemented: MatchSimHost." + member);
    }
}
