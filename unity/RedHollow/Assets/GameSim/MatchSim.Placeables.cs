namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 006 (T-06) owns this half of <see cref="MatchSim"/>: placeable combat effects.
    /// Requirement R-23; graded by fixtures G-027, G-028, G-029.
    ///
    /// The operations below are stubs that throw until ticket 006 lands. The shared core —
    /// fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
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
    }
}
