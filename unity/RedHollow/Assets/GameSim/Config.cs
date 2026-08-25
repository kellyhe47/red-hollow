namespace RedHollow.Sim
{
    /// <summary>
    /// Every tunable the sim reads. Defaults mirror the PRD; the Unity shell overrides them from
    /// ScriptableObjects so balance changes never require a code change (R-16, R-17, R-23, R-31).
    /// Values the golden fixtures pin are marked; those are contract, not taste.
    /// </summary>
    public sealed class SimConfig
    {
        /// <summary>R-01 / DEC-004. Clearing this wave wins the map.</summary>
        public int TotalWaves = 10;

        /// <summary>R-03 / DEC-006. Planning runs this long unless every connected player readies.</summary>
        public double PlanningDurationSeconds = 60.0;

        /// <summary>R-20. Starting shared stake.</summary>
        public int StartingScrip = 500;

        /// <summary>R-11 / DEC-002 — fixture-locked: civilians killed = ceil(damage / this).</summary>
        public double DamagePerCivilian = 10.0;

        /// <summary>R-22 / DEC-011 — fixture-locked: refund = floor(cost * this).</summary>
        public double SellRefundRatio = 0.5;

        /// <summary>R-31 / DEC-009 — fixture-locked: Sawbones' flat incoming damage reduction.</summary>
        public double SawbonesDamageReduction = 0.3;

        /// <summary>R-31 / DEC-008 — fixture-locked: lasso multiplies move speed by this.</summary>
        public double LassoSlowMultiplier = 0.5;

        /// <summary>R-31 / DEC-008 — fixture-locked: lasso lasts exactly this long.</summary>
        public double LassoDurationSeconds = 3.0;

        /// <summary>R-33 / DEC-010 — fixture-locked: respawn delay after a hero hits 0 HP.</summary>
        public double RespawnDelaySeconds = 10.0;

        /// <summary>R-33. Where dead heroes come back (team spawn, near map centre).</summary>
        public Vec2 RespawnPoint = new Vec2(0, 0);

        /// <summary>R-42 / DEC-014 — fixture-locked ceiling on ability ranks.</summary>
        public int MaxAbilityRank = 3;

        /// <summary>R-26 / DEC-019. Hero attacks never damage heroes or placeables.</summary>
        public bool FriendlyFire = false;

        /// <summary>R-41 / DEC-013. Cumulative XP needed for level L is 100 * L * (L - 1) / 2.</summary>
        public double LevelThresholdCoefficient = 100.0;

        /// <summary>R-35. Out-of-combat regen, applied after <see cref="RegenDelaySeconds"/> untouched.</summary>
        public double RegenHpPerSecond = 2.0;

        public double RegenDelaySeconds = 5.0;

        /// <summary>R-18. Monsters attack once per second.</summary>
        public double MonsterAttackIntervalSeconds = 1.0;

        /// <summary>
        /// R-17 roster stats, bounty included (R-40). Ships with the five PRD archetypes like every
        /// other tunable here; R-16 keeps it data, so the shell overrides rows rather than editing
        /// code. An archetype outside the roster throws instead of defaulting.
        /// </summary>
        public MonsterCatalog Monsters = new MonsterCatalog();

        /// <summary>
        /// R-23 placeable catalog — cost and effect numbers. Ships the PRD's whole five-row table,
        /// cost column and effect columns alike, like every other tunable here (DEC-RUN-1): a
        /// config is both buyable and playable out of the box. The mechanics are fixture-locked
        /// (G-027, G-028, G-029) but every number they read is tuned in config, never in code. A
        /// placeable outside that catalog throws rather than defaulting to a free placement.
        /// </summary>
        public PlaceableCatalog Placeables = new PlaceableCatalog();

        /// <summary>
        /// R-31 hero kits and their R-32 cooldowns. Ships the PRD's three-class table like every
        /// other tunable here (DEC-RUN-1), so a config is playable out of the box; a class outside
        /// that roster still throws rather than spawning a hero with no HP.
        /// </summary>
        public HeroKitCatalog HeroKits = new HeroKitCatalog();

        /// <summary>
        /// Cumulative lifetime XP required to have reached <paramref name="level"/> (R-41).
        /// Level 1 = 0, level 2 = 100, level 3 = 300, level 4 = 600 ...
        /// </summary>
        public double CumulativeXpForLevel(int level)
        {
            if (level <= 1)
            {
                return 0.0;
            }

            return LevelThresholdCoefficient * level * (level - 1) / 2.0;
        }
    }
}
