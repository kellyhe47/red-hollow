namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 015 (T-15) owns this half of <see cref="MatchSim"/>: the monster attack *cadence*
    /// half of R-18 — "monsters attack once per second". Grades no fixture, which is exactly why
    /// the gap survived to a requirement walk: <see cref="SimConfig.MonsterAttackIntervalSeconds"/>
    /// has been declared since ticket 001 and nothing in the sim has ever read it, so the host's
    /// combat loop could call <see cref="ApplyHotspotAttack"/>, <see cref="ApplyHeroDamage"/> or
    /// <see cref="ApplyPlaceableDamage"/> on every one of its 60 frames a second and land 60 hits.
    ///
    /// R-18's other half — NavMesh movement, and the Burrower path that ignores barricade
    /// obstacles — is not here. The pathing is Unity shell work, and the Burrower's barricade
    /// carve-out already lives in ticket 002 at the targeting level
    /// (<see cref="Monster.IgnoresBarricadesAndHeroes"/>, G-005).
    ///
    /// <b>Why a separate gate rather than a refusal folded into the damage operations.</b>
    /// Six golden fixtures call a damage entry point directly, with no prior attack and no cadence
    /// state: G-006/007/008/009 on <see cref="ApplyHotspotAttack"/> and G-020/021 on
    /// <see cref="ApplyHeroDamage"/>. Each pins an exact `result`, `state_changes` and
    /// `emitted_events` for what is that monster's *first* hit in the scenario. A gate inside those
    /// operations would have to either refuse a first attack (breaking all six) or record the
    /// cadence stamp as a delta (breaking all six a different way). Keeping the question in its own
    /// operation leaves the three damage operations byte-identical: the host asks here first, and
    /// only calls the damage operation when the answer is yes.
    ///
    /// The shared core — fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>
        /// R-18. "May this monster land a hit right now?" — and, when the answer is yes, the claim
        /// that starts its next cooldown.
        ///
        /// Ask-and-claim in one call rather than a pure predicate plus a separate "note that it
        /// attacked": two calls could be desynchronised by a host that forgot the second one, and
        /// the whole point of the operation is that the host cannot land more than one hit per
        /// <see cref="SimConfig.MonsterAttackIntervalSeconds"/> however often it asks.
        ///
        /// A monster that has never attacked is permitted immediately, at any clock reading
        /// including 0 — that is the property the six fixtures above depend on.
        ///
        /// The deadline is inclusive, the convention G-019 set for every boundary in this sim and
        /// that tickets 004, 007 and 008 follow: an attack at exactly last + interval lands.
        ///
        /// Per monster: one monster's cooldown must never gate another's, the same way
        /// <see cref="Hero.CooldownReadyAt"/> is per hero and per slot (R-32).
        /// </summary>
        public bool TryMonsterAttack(string monsterId)
        {
            BeginCommand();
            throw NotYet("T-15", "monster attack cadence (R-18)");
        }
    }
}
