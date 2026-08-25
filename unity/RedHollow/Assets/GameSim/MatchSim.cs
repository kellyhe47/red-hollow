using System;
using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// The host-authoritative simulation (R-51). Every fixture-covered rule lives here, and only the
    /// host ever holds an instance — clients send commands in and receive replicated state out.
    ///
    /// Each public operation is one command. It returns a typed result for the caller and records
    /// its state deltas, gameplay events and external calls into <see cref="LastObservation"/>,
    /// which is what the netcode layer replicates from and what the golden fixtures grade.
    ///
    /// This type must never reference UnityEngine. GameSim.asmdef enforces that in Unity;
    /// sim/GameSim/GameSim.csproj enforces it again by building with no Unity reference at all.
    /// </summary>
    public sealed class MatchSim
    {
        private readonly SimConfig _config;
        private readonly IProfileStore _profileStore;
        private readonly IClock _clock;
        private readonly IPathOracle _pathOracle;

        public MatchSim(
            MatchState state,
            SimConfig config = null,
            IProfileStore profileStore = null,
            IClock clock = null,
            IPathOracle pathOracle = null)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            _config = config ?? new SimConfig();
            _profileStore = profileStore ?? new InMemoryProfileStore();
            _clock = clock ?? new SimClock();
            _pathOracle = pathOracle ?? new OpenPathOracle();
        }

        public MatchState State { get; }

        public SimConfig Config => _config;

        public IClock Clock => _clock;

        /// <summary>The observation produced by the most recent command.</summary>
        public SimObservation LastObservation { get; private set; } = new SimObservation();

        // ---- recording plumbing ------------------------------------------------------------------

        private SimObservation BeginCommand()
        {
            LastObservation = new SimObservation();
            return LastObservation;
        }

        private void RecordChange(string entity, string field, object from, object to)
        {
            // Only genuine deltas are replicated — an unchanged field is not a state change.
            if (Equals(from, to))
            {
                return;
            }

            LastObservation.StateChanges.Add(new StateChange(entity, field, from, to));
        }

        private void Emit(string type, IDictionary<string, object> fields = null)
        {
            LastObservation.EmittedEvents.Add(new SimEvent(type, fields));
        }

        private void RecordExternalCall(string service, string op, IDictionary<string, object> fields = null)
        {
            LastObservation.ExternalCalls.Add(new ExternalCall(service, op, fields));
        }

        private TResult Finish<TResult>(TResult result) where TResult : ISimResult
        {
            LastObservation.Result = result.ToFields();
            return result;
        }

        private static NotImplementedException NotYet(string ticket, string behavior) =>
            new NotImplementedException(ticket + " not implemented: " + behavior);

        // ---- T-02: monster targeting -------------------------------------------------------------

        /// <summary>R-16 / B-001..B-003. Pick what this monster should be attacking.</summary>
        public TargetSelectionResult SelectTarget(string monsterId)
        {
            BeginCommand();
            throw NotYet("T-02", "nearest-target monster AI with barricade blocking and the Burrower carve-out");
        }

        // ---- T-03: hotspots, civilians, defeat ---------------------------------------------------

        /// <summary>R-11 / B-004, B-005. A monster connects with a civilian shelter.</summary>
        public HotspotAttackResult ApplyHotspotAttack(HotspotAttackRequest request)
        {
            BeginCommand();
            throw NotYet("T-03", "ceil(damage/10) civilian kills, clamped at zero, with the all-civilians-dead defeat rule");
        }

        // ---- T-04: wave lifecycle ----------------------------------------------------------------

        /// <summary>R-01, R-02, R-20 / B-006, B-007. A monster died.</summary>
        public MonsterKillResult RecordMonsterKill(MonsterKillRequest request)
        {
            BeginCommand();
            throw NotYet("T-04", "kill bounty into the shared pool, wave completion, and final-wave victory");
        }

        /// <summary>R-03, R-20 / B-009. Open the next wave's planning phase.</summary>
        public PlanningPhaseResult BeginPlanningPhase()
        {
            BeginCommand();
            throw NotYet("T-04", "planning phase start with full scrip carryover");
        }

        /// <summary>R-03 / B-010. A player toggled ready.</summary>
        public ReadyResult SetPlayerReady(string playerId)
        {
            BeginCommand();
            throw NotYet("T-04", "all-connected-players-ready early combat start");
        }

        // ---- T-05: economy -----------------------------------------------------------------------

        /// <summary>R-21 / B-008. Buy and place a defence.</summary>
        public PurchaseResult PurchasePlacement(PurchaseRequest request)
        {
            BeginCommand();
            throw NotYet("T-05", "planning-phase-only purchase with zone and scrip validation");
        }

        /// <summary>R-22 / B-014. Sell a placed defence back during planning.</summary>
        public SellResult SellPlacement(SellRequest request)
        {
            BeginCommand();
            throw NotYet("T-05", "sell at floor(cost * 0.5) refunded to the shared pool");
        }

        // ---- T-06: placeable combat effects ------------------------------------------------------

        /// <summary>R-23 / B-018. A monster crossed a trap.</summary>
        public ISimResult TriggerPlaceable(string placeableId, string monsterId)
        {
            BeginCommand();
            throw NotYet("T-06", "spike trap trigger count and break, dynamite single-use AoE");
        }

        /// <summary>R-23 / B-018. A turret's firing tick.</summary>
        public TurretTickResult TurretTick(string turretId)
        {
            BeginCommand();
            throw NotYet("T-06", "turret targeting the nearest living monster in range");
        }

        // ---- T-07: hero damage, death, no friendly fire ------------------------------------------

        /// <summary>R-31, R-33 / B-012, B-013. Something hit a hero.</summary>
        public HeroDamageResult ApplyHeroDamage(HeroDamageRequest request)
        {
            BeginCommand();
            throw NotYet("T-07", "Sawbones flat damage reduction and death with a 10s respawn clock");
        }

        /// <summary>R-26, R-36 / B-019. A hero's attack resolves along its aim line.</summary>
        public HeroAttackResult ResolveHeroAttack(HeroAttackRequest request)
        {
            BeginCommand();
            throw NotYet("T-07", "no friendly fire — hero attacks damage monsters only");
        }

        // ---- T-08: abilities and status effects --------------------------------------------------

        /// <summary>R-31 / B-011. A hero cast an ability.</summary>
        public AbilityResult ApplyAbility(AbilityCastRequest request)
        {
            BeginCommand();
            throw NotYet("T-08", "Rancher lasso applying a 50% slow for exactly 3.0s");
        }

        /// <summary>R-31 / B-011. Expire any status effects whose time is up.</summary>
        public StatusTickResult TickStatusEffects()
        {
            BeginCommand();
            throw NotYet("T-08", "status effect expiry restoring base move speed");
        }

        // ---- T-09: progression -------------------------------------------------------------------

        /// <summary>R-40, R-41, R-43 / B-015, B-017. Credit a kill's XP to a player.</summary>
        public XpAwardResult AwardKillXp(MonsterKillRequest kill, string accountId)
        {
            BeginCommand();
            throw NotYet("T-09", "XP equal to bounty, escalating level thresholds, and profile persistence");
        }

        /// <summary>R-42 / B-016. Spend a banked skill point.</summary>
        public SpendSkillPointResult SpendSkillPoint(SpendSkillPointRequest request)
        {
            BeginCommand();
            throw NotYet("T-09", "free-choice unlock or rank-up, rejected when no points are banked");
        }
    }
}
