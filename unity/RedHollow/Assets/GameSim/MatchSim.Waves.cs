namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 004 (T-04) owns this half of <see cref="MatchSim"/>: the match FSM and wave
    /// lifecycle. Requirements R-01, R-02, R-03, R-04, R-05, R-14, R-19; graded by fixtures
    /// G-010, G-011, G-012, G-016, G-017.
    ///
    /// The operations below are stubs that throw until ticket 004 lands. The shared core —
    /// fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
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
    }
}
