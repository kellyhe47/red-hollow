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
    /// </summary>
    public sealed class MatchSimHost : IMatchSimHost
    {
        private readonly MatchSim _sim;
        private readonly SimClock _clock;

        /// <param name="sim">The host-authoritative sim. Must already have been handed <paramref name="clock"/>.</param>
        /// <param name="clock">
        /// R-51 — the same clock instance <paramref name="sim"/> reads its deadlines from. The host
        /// owns it because the sim schedules nothing for itself; handing the sim a different clock
        /// than the one advanced here leaves every deadline frozen at zero.
        /// </param>
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

            _sim = sim;
            _clock = clock;
        }

        public MatchState State => _sim.State;

        public SimConfig Config => _sim.Config;

        public IClock Clock => _clock;

        public SimObservation LastObservation => _sim.LastObservation;

        public void AdvanceClock(double deltaSeconds) => _clock.Advance(deltaSeconds);

        public void TickPlanningTimer() => _sim.TickPlanningTimer();

        public StatusTickResult TickStatusEffects() => _sim.TickStatusEffects();

        public void TickHeroRegen() => _sim.TickHeroRegen();

        public void TickHeroRespawns() => _sim.TickHeroRespawns();

        public void TickMedStations() => _sim.TickMedStations();

        public bool TryMonsterAttack(string monsterId) => _sim.TryMonsterAttack(monsterId);

        public HotspotAttackResult ApplyHotspotAttack(HotspotAttackRequest request) =>
            _sim.ApplyHotspotAttack(request);

        public HeroDamageResult ApplyHeroDamage(HeroDamageRequest request) =>
            _sim.ApplyHeroDamage(request);

        public PlaceableDamageResult ApplyPlaceableDamage(PlaceableDamageRequest request) =>
            _sim.ApplyPlaceableDamage(request);

        // ---- ticket 019 (T-19): the rest of the seam a playable match needs -----------------
        // Shape only. Every one of these is a forward onto the same _sim, exactly like the members
        // above; they throw until ticket 019 wires them so that "nothing calls this" fails loudly
        // rather than silently.

        public MonsterMovementResult TickMonsterMovement(double deltaSeconds) =>
            throw new NotImplementedException("ticket 019: forward to MatchSim.TickMonsterMovement");

        public HeroMoveResult MoveHero(HeroMoveRequest request) =>
            throw new NotImplementedException("ticket 019: forward to MatchSim.MoveHero");

        public TargetSelectionResult SelectTarget(string monsterId) =>
            throw new NotImplementedException("ticket 019: forward to MatchSim.SelectTarget");

        public WaveSpawnResult SpawnWave(int waveNumber) =>
            throw new NotImplementedException("ticket 019: forward to MatchSim.SpawnWave");

        public PlanningPhaseResult BeginPlanningPhase() =>
            throw new NotImplementedException("ticket 019: forward to MatchSim.BeginPlanningPhase");
    }
}
