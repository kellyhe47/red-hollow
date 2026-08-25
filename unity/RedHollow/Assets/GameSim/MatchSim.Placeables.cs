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

        /// <summary>
        /// R-23 / R-16. Something hit a placeable — today the only such attacker is a monster
        /// whose path a barricade blocked, which R-16 makes "the target until destroyed".
        ///
        /// A command taking a request, like <see cref="ApplyHotspotAttack"/> and
        /// <see cref="ApplyHeroDamage"/>, rather than a tick or a bare id pair: this is the third
        /// member of the same family (an attacker, an amount, a victim) and it is the operation
        /// that makes R-16's "until destroyed" clause reachable at all.
        /// </summary>
        public PlaceableDamageResult ApplyPlaceableDamage(PlaceableDamageRequest request)
        {
            BeginCommand();
            throw NotYet("T-06", "a barricade taking damage, and being destroyed at 0 HP (R-23/R-16)");
        }

        /// <summary>
        /// R-23 / R-35. The med station healing tick: every living hero inside a standing med
        /// station's radius regains HP for the time elapsed since the last tick.
        ///
        /// No arguments, elapsed time read off the injected clock, void return — the same seam
        /// shape as <see cref="TickHeroRegen"/>, <see cref="TickHeroRespawns"/> and
        /// <see cref="TickStatusEffects"/>. No fixture grades med stations, so there is no result
        /// shape to honour and the healing replicates through LastObservation's state changes like
        /// any other delta.
        /// </summary>
        public void TickMedStations()
        {
            BeginCommand();
            throw NotYet("T-06", "med station healing heroes inside its radius (R-23/R-35)");
        }
    }
}
