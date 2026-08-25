namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 003 (T-03) owns this half of <see cref="MatchSim"/>: hotspots, the civilian pool
    /// and the defeat rule. Requirements R-10, R-11, R-12, R-13, R-72; graded by fixtures
    /// G-006 through G-009.
    ///
    /// The operations below are stubs that throw until ticket 003 lands. The shared core —
    /// fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>R-11 / B-004, B-005. A monster connects with a civilian shelter.</summary>
        public HotspotAttackResult ApplyHotspotAttack(HotspotAttackRequest request)
        {
            BeginCommand();
            throw NotYet("T-03", "ceil(damage/10) civilian kills, clamped at zero, with the all-civilians-dead defeat rule");
        }
    }
}
