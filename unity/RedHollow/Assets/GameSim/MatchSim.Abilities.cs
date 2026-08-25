namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 008 (T-08) owns this half of <see cref="MatchSim"/>: hero abilities and status
    /// effects. Requirements R-31, R-32; graded by fixtures G-018, G-019.
    ///
    /// The operations below are stubs that throw until ticket 008 lands. The shared core —
    /// fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
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
    }
}
