using System.Collections.Generic;
using RedHollow.Game.Host;
using RedHollow.Sim;

namespace RedHollow.Game.UI
{
    /// <summary>
    /// Ticket 021 (T-21) — the per-command event tap the pump's feed is built on.
    ///
    /// The sim has no event stream: <see cref="ISimHost.LastObservation"/> is per-command, replaced
    /// wholesale by the NEXT command (<c>MatchSim.BeginCommand</c>). A host step runs dozens of
    /// commands, so anything that only read the observation after the step would see the last
    /// command's events and lose the rest. This decorator sits on the <see cref="IMatchSimHost"/>
    /// seam and drains the observation's <see cref="SimEvent"/>s after every command it forwards —
    /// each event is captured exactly once, in emission order.
    ///
    /// <see cref="Drain"/> is also called by the bootstrap BEFORE stepping, which is what catches
    /// out-of-band commands issued directly on <c>match.Sim</c> between pumps (a test's
    /// <c>ResolveHeroAttack</c>, the HUD's own <c>Spend</c>): their events are still sitting in
    /// <c>LastObservation</c> until a later command overwrites it, and the pre-step drain runs
    /// before this pump can issue one. Exactly-once is kept by remembering which observation
    /// instance was last read and how many of its events were already taken — the same instance
    /// never re-delivers, and a new instance delivers from the top.
    ///
    /// A decorator and not a wider seam on purpose: adding members to <see cref="ISimHost"/> would
    /// break ticket 010's locked recording fake, and the established widening pattern
    /// (<see cref="IMatchSimHost"/> : <see cref="ISimHost"/>) needs no new member here at all —
    /// every forward below is rule-free (R-51).
    /// </summary>
    internal sealed class SimEventTap : IMatchSimHost
    {
        private readonly IMatchSimHost _inner;

        private readonly List<SimEvent> _pending = new List<SimEvent>();

        /// <summary>The observation instance <see cref="Drain"/> last read events from.</summary>
        private SimObservation _seen;

        /// <summary>How many of <see cref="_seen"/>'s events have already been taken.</summary>
        private int _consumed;

        public SimEventTap(IMatchSimHost inner)
        {
            _inner = inner;

            // Baseline: whatever the factory's own setup commands emitted before this tap existed
            // (profile application, seeding) predates the shell and is not presentation's to replay.
            _seen = inner.LastObservation;
            _consumed = _seen == null ? 0 : _seen.EmittedEvents.Count;
        }

        /// <summary>
        /// Capture every not-yet-captured event out of the sim's current observation. Idempotent
        /// between commands; called after every forwarded command and once pre-step by the pump.
        /// </summary>
        public void Drain()
        {
            var observation = _inner.LastObservation;
            if (observation == null)
            {
                return;
            }

            if (!ReferenceEquals(observation, _seen))
            {
                _seen = observation;
                _consumed = 0;
            }

            var events = observation.EmittedEvents;
            for (var i = _consumed; i < events.Count; i++)
            {
                _pending.Add(events[i]);
            }

            _consumed = events.Count;
        }

        /// <summary>Move everything captured so far into <paramref name="into"/>, oldest first.</summary>
        public void TakePendingInto(List<SimEvent> into)
        {
            into.AddRange(_pending);
            _pending.Clear();
        }

        // ---- ISimHost / IMatchSimHost — every member forwards, commands drain after -------------

        public MatchState State => _inner.State;

        public SimConfig Config => _inner.Config;

        public IClock Clock => _inner.Clock;

        public SimObservation LastObservation => _inner.LastObservation;

        public void AdvanceClock(double deltaSeconds)
        {
            _inner.AdvanceClock(deltaSeconds);
            Drain();
        }

        public void TickPlanningTimer()
        {
            _inner.TickPlanningTimer();
            Drain();
        }

        public StatusTickResult TickStatusEffects()
        {
            var result = _inner.TickStatusEffects();
            Drain();
            return result;
        }

        public void TickHeroRegen()
        {
            _inner.TickHeroRegen();
            Drain();
        }

        public void TickHeroRespawns()
        {
            _inner.TickHeroRespawns();
            Drain();
        }

        public void TickMedStations()
        {
            _inner.TickMedStations();
            Drain();
        }

        public bool TryMonsterAttack(string monsterId)
        {
            var result = _inner.TryMonsterAttack(monsterId);
            Drain();
            return result;
        }

        public HotspotAttackResult ApplyHotspotAttack(HotspotAttackRequest request)
        {
            var result = _inner.ApplyHotspotAttack(request);
            Drain();
            return result;
        }

        public HeroDamageResult ApplyHeroDamage(HeroDamageRequest request)
        {
            var result = _inner.ApplyHeroDamage(request);
            Drain();
            return result;
        }

        public PlaceableDamageResult ApplyPlaceableDamage(PlaceableDamageRequest request)
        {
            var result = _inner.ApplyPlaceableDamage(request);
            Drain();
            return result;
        }

        public MonsterMovementResult TickMonsterMovement(double deltaSeconds)
        {
            var result = _inner.TickMonsterMovement(deltaSeconds);
            Drain();
            return result;
        }

        public HeroMoveResult MoveHero(HeroMoveRequest request)
        {
            var result = _inner.MoveHero(request);
            Drain();
            return result;
        }

        public TargetSelectionResult SelectTarget(string monsterId)
        {
            var result = _inner.SelectTarget(monsterId);
            Drain();
            return result;
        }

        public WaveSpawnResult SpawnWave(int waveNumber)
        {
            var result = _inner.SpawnWave(waveNumber);
            Drain();
            return result;
        }

        public PlanningPhaseResult BeginPlanningPhase()
        {
            var result = _inner.BeginPlanningPhase();
            Drain();
            return result;
        }

        public double PlaceableFootprintRadius => _inner.PlaceableFootprintRadius;

        public TurretTickResult TurretTick(string turretId)
        {
            var result = _inner.TurretTick(turretId);
            Drain();
            return result;
        }

        public ISimResult TriggerPlaceable(string placeableId, string monsterId)
        {
            var result = _inner.TriggerPlaceable(placeableId, monsterId);
            Drain();
            return result;
        }

        public MonsterKillResult RecordMonsterKill(MonsterKillRequest request)
        {
            var result = _inner.RecordMonsterKill(request);
            Drain();
            return result;
        }

        public XpAwardResult AwardKillXp(MonsterKillRequest kill, string accountId)
        {
            var result = _inner.AwardKillXp(kill, accountId);
            Drain();
            return result;
        }

        public HeroAttackResult ResolveHeroAttack(HeroAttackRequest request)
        {
            var result = _inner.ResolveHeroAttack(request);
            Drain();
            return result;
        }

        public AbilityCastOutcome CastAbility(HeroAbilityRequest request)
        {
            var result = _inner.CastAbility(request);
            Drain();
            return result;
        }
    }
}
