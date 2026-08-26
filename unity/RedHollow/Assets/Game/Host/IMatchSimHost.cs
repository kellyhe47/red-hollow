using RedHollow.Sim;

namespace RedHollow.Game.Host
{
    /// <summary>
    /// Ticket 019 (T-19) — the rest of the sim seam a *playable* match needs (R-51).
    ///
    /// <see cref="ISimHost"/> deliberately stopped at the five ticks the sim cannot schedule for
    /// itself plus the R-18 attack gate, and its own doc says widening is expected. This is that
    /// widening, and it is a derived interface rather than five more members on
    /// <see cref="ISimHost"/> for one concrete reason: ticket 010's locked tests bind a recording
    /// fake to <see cref="ISimHost"/>, and a host loop that must keep working against a bare
    /// <see cref="ISimHost"/> is also the honest statement of what these ops are — the operations a
    /// loop driving a *live match* needs, which a planning-only or lobby harness does not.
    ///
    /// Every member forwards to a <see cref="MatchSim"/> command. None of them is a rule: R-51 puts
    /// all five rules on the far side of this seam, and this interface exists so the shell can be
    /// observed calling them.
    /// </summary>
    public interface IMatchSimHost : ISimHost
    {
        /// <summary>
        /// R-17 / R-18 / DEC-008 — advance every living monster toward its target by one step.
        /// Takes a delta, so it fell outside T-10's parameterless-<c>Tick*</c> net and nothing has
        /// ever called it: without this a wave stands in its breach for the whole match.
        /// </summary>
        MonsterMovementResult TickMonsterMovement(double deltaSeconds);

        /// <summary>
        /// R-30 / R-51 — one player's resolved WASD intent, applied to their hero. The shell
        /// resolves the keys (<see cref="RedHollow.Game.Input.DefaultHeroInputMap"/>) and sends the
        /// direction as a command; the sim owns the pace.
        /// </summary>
        HeroMoveResult MoveHero(HeroMoveRequest request);

        /// <summary>
        /// R-16 — pick what a monster should be walking at. Spawned monsters carry no target
        /// (see <see cref="MatchSim.SpawnWave"/>), and R-12 invalidates one the moment a shelter is
        /// emptied, so the host has to keep asking.
        /// </summary>
        TargetSelectionResult SelectTarget(string monsterId);

        /// <summary>R-19 / R-14 — put one wave's monsters in the colony.</summary>
        WaveSpawnResult SpawnWave(int waveNumber);

        /// <summary>
        /// R-03 / G-016 — open the next wave's planning phase. The wave counter advances here and
        /// nowhere else, which is what makes this the hinge of wave progression.
        /// </summary>
        PlanningPhaseResult BeginPlanningPhase();

        /// <summary>
        /// R-24 / R-23 — the live footprint a standing placeable occupies, the same radius planning
        /// uses to pick and to reject overlaps. Trap crossings in combat read this rather than a
        /// second guessed number, so a retuned sim moves the trigger volume with the ghost.
        /// </summary>
        double PlaceableFootprintRadius { get; }

        /// <summary>
        /// R-23 / G-028 — one turret's firing tick: nearest living monster in range takes the
        /// catalog damage. The host rate-limits this to 1 Hz so Damage 20 is 20 DPS; the command
        /// itself has no cooldown.
        /// </summary>
        TurretTickResult TurretTick(string turretId);

        /// <summary>
        /// R-23 / G-027 / G-029 — a monster crossed a spike trap or dynamite. Edge-triggered by the
        /// host (enter the footprint, not occupy it), or a monster standing on spikes would spend
        /// every trigger in as many frames.
        /// </summary>
        ISimResult TriggerPlaceable(string placeableId, string monsterId);

        /// <summary>
        /// R-02 / R-20 / R-40 — the kill command: roster, bounty, wave complete. Placeable damage
        /// flips <c>alive</c> at 0 HP but does not itself count the kill, so a live match that never
        /// asks this leaves turret and trap victims on the wave forever.
        /// </summary>
        MonsterKillResult RecordMonsterKill(MonsterKillRequest request);

        /// <summary>
        /// R-40 — XP equal to the bounty, to the credited account. Turret (and other placeable)
        /// kills credit the placer; the host resolves that account before calling.
        /// </summary>
        XpAwardResult AwardKillXp(MonsterKillRequest kill, string accountId);
    }
}
