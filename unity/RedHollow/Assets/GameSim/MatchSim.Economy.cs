namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 005 (T-05) owns this half of <see cref="MatchSim"/>: the shared scrip economy.
    /// Requirements R-20, R-21, R-22, R-24, R-25; graded by fixtures G-013, G-014, G-015, G-022.
    ///
    /// The operations below are stubs that throw until ticket 005 lands. The shared core —
    /// fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
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
    }
}
