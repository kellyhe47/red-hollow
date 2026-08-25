namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 007 (T-07) owns this half of <see cref="MatchSim"/>: hero damage, death and
    /// respawn, and the no-friendly-fire rule. Requirements R-26, R-33, R-34, R-35, R-36;
    /// graded by fixtures G-020, G-021, G-030.
    ///
    /// The operations below are stubs that throw until ticket 007 lands. The shared core —
    /// fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>R-31, R-33 / B-012, B-013. Something hit a hero.</summary>
        public HeroDamageResult ApplyHeroDamage(HeroDamageRequest request)
        {
            BeginCommand();
            throw NotYet("T-07", "Sawbones flat damage reduction and death with a 10s respawn clock");
        }

        /// <summary>R-26, R-36 / B-019. A hero's attack resolves along its aim line.</summary>
        public HeroAttackResult ResolveHeroAttack(HeroAttackRequest request)
        {
            BeginCommand();
            throw NotYet("T-07", "no friendly fire — hero attacks damage monsters only");
        }

        /// <summary>
        /// R-35. Out-of-combat regen: a hero untouched for <see cref="SimConfig.RegenDelaySeconds"/>
        /// heals <see cref="SimConfig.RegenHpPerSecond"/> per second up to MaxHp. Driven from the
        /// host's fixed-step loop, reading elapsed time off the injected clock the way
        /// <see cref="TickStatusEffects"/> does.
        ///
        /// Returns void rather than an ISimResult: no fixture grades regen, so there is no result
        /// shape to honour, and the healing it does is replicated through LastObservation's state
        /// changes like any other delta.
        /// </summary>
        public void TickHeroRegen()
        {
            BeginCommand();
            throw NotYet("T-07", "out-of-combat regen at 2 HP/s after 5s untouched");
        }
    }
}
