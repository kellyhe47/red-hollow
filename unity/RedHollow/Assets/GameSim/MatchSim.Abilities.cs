namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 008 (T-08) owns this half of <see cref="MatchSim"/>: hero abilities and status
    /// effects. Requirements R-31, R-32; graded by fixtures G-018, G-019.
    ///
    /// The operations below are stubs that throw until ticket 008 lands. The shared core —
    /// fields, constructor and recording plumbing — lives in MatchSim.cs.
    ///
    /// Two entry points, deliberately: <see cref="CastAbility"/> is the *gate* (is this slot
    /// unlocked, is it off cooldown, which ability does this class bind to it) and
    /// <see cref="ApplyAbility"/> is one ability's *effect*. G-018 calls the effect directly with
    /// a caster that is not in the match state at all, so the gate cannot live inside it.
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

        /// <summary>
        /// R-31, R-32 / R-34. A hero pressed Q or E: resolve the ability its class binds to that
        /// slot, at the rank the hero carries, if the slot is unlocked and off cooldown.
        /// Cooldowns are the only cast limit — heroes have no mana (R-34).
        /// </summary>
        public AbilityCastOutcome CastAbility(HeroAbilityRequest request)
        {
            BeginCommand();
            throw NotYet("T-08", "R-31/R-32 gated Q/E cast: unlock check, cooldown check, class kit effect");
        }

        /// <summary>
        /// R-31 / R-43. At match start every hero adopts the ability allocations saved on its
        /// player's account profile, so a veteran begins with previously unlocked abilities and a
        /// fresh account begins basic-attack-only.
        ///
        /// Void return and no arguments, the same seam shape as the other match-loop operations
        /// here: no fixture grades match start, so there is no result shape to honour.
        /// </summary>
        public void ApplySavedAbilityAllocations()
        {
            BeginCommand();
            throw NotYet("T-08", "R-31/R-43 match start applying each hero's saved ability allocations");
        }
    }
}
