namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 002 (T-02) owns this half of <see cref="MatchSim"/>: monster target selection.
    /// Requirements R-16, R-17, R-18; graded by fixtures G-001 through G-005.
    ///
    /// The operations below are stubs that throw until ticket 002 lands. The shared core —
    /// fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>R-16 / B-001..B-003. Pick what this monster should be attacking.</summary>
        public TargetSelectionResult SelectTarget(string monsterId)
        {
            BeginCommand();
            throw NotYet("T-02", "nearest-target monster AI with barricade blocking and the Burrower carve-out");
        }
    }
}
