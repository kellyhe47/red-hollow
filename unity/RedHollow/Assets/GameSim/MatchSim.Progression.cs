namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 009 (T-09) owns this half of <see cref="MatchSim"/>: XP, leveling, skill points
    /// and persistent profiles. Requirements R-40, R-41, R-42, R-43, R-44; graded by fixtures
    /// G-023 through G-026.
    ///
    /// The operations below are stubs that throw until ticket 009 lands. The shared core —
    /// fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
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
