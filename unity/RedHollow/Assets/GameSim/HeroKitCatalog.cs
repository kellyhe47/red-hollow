using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// One ability's numbers (R-31), shape only — every field here is balance data that
    /// <see cref="SimConfig.HeroKits"/> supplies per class per slot, never a constant in the
    /// ability code. Not every ability uses every field: a burst reads
    /// <see cref="Damage"/> and <see cref="Hits"/>, an AoE reads <see cref="Radius"/>, a buff or
    /// slow reads <see cref="Magnitude"/> and <see cref="DurationSeconds"/>.
    ///
    /// The lasso is the one exception: its slow multiplier and duration are fixture-locked on
    /// <see cref="SimConfig.LassoSlowMultiplier"/> / <see cref="SimConfig.LassoDurationSeconds"/>
    /// because G-018 supplies them by those names, so the Rancher's Q row carries only its
    /// identity and its rank scaling.
    /// </summary>
    public sealed class AbilitySpec
    {
        /// <summary>R-31 — which ability this slot is, one of the <see cref="AbilityName"/> constants.</summary>
        public string Name;

        /// <summary>R-31 — damage per hit: per shot of a burst, per monster of an AoE.</summary>
        public double Damage;

        /// <summary>R-31 — hits one cast resolves (Fan the Hammer's 6-shot burst).</summary>
        public int Hits;

        /// <summary>R-31 — reach: AoE radius, dash distance, knockback distance.</summary>
        public double Radius;

        /// <summary>R-31 — how long this ability's status effect lasts (Bulwark's 2s).</summary>
        public double DurationSeconds;

        /// <summary>R-31 — the ability's multiplier-shaped number (Bulwark's 0.6 damage reduction).</summary>
        public double Magnitude;

        /// <summary>
        /// R-32 — fraction each rank above 1 adds to this ability's numbers ("~+25%/rank").
        /// Config rather than a constant so the curve is tunable without a code change, and
        /// per-ability so a class whose Q scales differently does not need a special case.
        /// </summary>
        public double RankScalingPerRank;
    }

    /// <summary>
    /// One class's kit numbers from R-31 plus its R-32 cooldowns. Kit values beyond the ones the
    /// fixtures pin are config-tunable, which is why they live here and not in the ability code.
    /// </summary>
    public sealed class HeroKit
    {
        /// <summary>R-31 — HP the hero spawns and respawns at (R-33).</summary>
        public double MaxHp;

        /// <summary>R-31 — damage of the SPACE basic attack (R-30).</summary>
        public double BasicAttackDamage;

        /// <summary>R-32 — Q cooldown in seconds; per-class tuning is allowed.</summary>
        public double QCooldownSeconds;

        /// <summary>R-32 — E cooldown in seconds; per-class tuning is allowed.</summary>
        public double ECooldownSeconds;

        /// <summary>R-31 — this class's Q ability and its numbers.</summary>
        public AbilitySpec Q = new AbilitySpec();

        /// <summary>R-31 — this class's E ability and its numbers.</summary>
        public AbilitySpec E = new AbilitySpec();
    }

    /// <summary>
    /// Hero kits keyed by the <see cref="HeroClass"/> constants (R-31), shipping the PRD's own
    /// class table and R-32's cooldowns (DEC-RUN-1) exactly as <see cref="MonsterCatalog"/> ships
    /// the R-17 roster — <see cref="SimConfig"/>'s contract is "defaults mirror the PRD; the Unity
    /// shell overrides them from ScriptableObjects". Asking for a class outside the roster still
    /// throws: a hero that spawned with 0 max HP would die on the first hit and look like a combat
    /// bug, not a config gap.
    ///
    /// Every row is built fresh per catalog instance, and a catalog is built fresh per SimConfig,
    /// so a balance tweak on one config can never leak into another's — which is exactly what
    /// shared static rows would do, and what the sim's per-match determinism depends on.
    /// </summary>
    public sealed class HeroKitCatalog
    {
        /// <summary>
        /// R-32 — "rank-ups (max 3) improve numbers ~+25%/rank", the same curve for every ability
        /// until balance says otherwise. Per-ability so a class whose Q should scale differently
        /// needs a config edit, not a code branch.
        /// </summary>
        private const double DefaultRankScalingPerRank = 0.25;

        /// <summary>R-32 — the PRD's Q/E cooldowns. Per-class tuning is allowed; nothing needs it yet.</summary>
        private const double DefaultQCooldownSeconds = 8.0;

        private const double DefaultECooldownSeconds = 20.0;

        private readonly Dictionary<string, HeroKit> _byClass = new Dictionary<string, HeroKit>();

        /// <summary>
        /// DEC-RUN-1 — the R-31 class table and R-32 cooldowns as shipped defaults.
        ///
        /// Numbers the PRD states outright are transcribed (HP, basic damage, Fan the Hammer's six
        /// shots, Bulwark's 60% for 2s, the 8s/20s cooldowns). The rest — per-shot ability damage,
        /// Whirl's radius, Stampede's reach — the PRD leaves to balance, so these are playtest
        /// starting points chosen against the class fantasy, not derived values:
        ///
        ///   * Fan the Hammer 12 x 6 = 72 burst on one target, roughly three basics for an 8s Q.
        ///   * Deadeye 60 to every monster on the line — a 20s E is worth two-and-a-half basics
        ///     per body, and its value is the number of bodies.
        ///   * Stampede 25 through the lane plus 4.0 of dash and knockback: repositioning first,
        ///     damage second.
        ///   * Whirl 35 inside 4.0 — a melee-range sweep that beats the Sawbones' own 40 basic
        ///     only when it catches two.
        ///
        /// The Rancher's 12 is the per-pellet quantum (DEC-RUN-8), not a 60-damage trigger-pull:
        /// the "x5 pellets" of the PRD row is spread geometry the shell resolves before it calls
        /// <see cref="MatchSim.ResolveHeroAttack"/>, which is also the only reading under which
        /// the class's "basics hit up to 2 targets" passive means anything.
        /// </summary>
        public HeroKitCatalog()
        {
            Set(HeroClass.Gunslinger, new HeroKit
            {
                MaxHp = 100.0,
                BasicAttackDamage = 25.0,
                QCooldownSeconds = DefaultQCooldownSeconds,
                ECooldownSeconds = DefaultECooldownSeconds,
                Q = new AbilitySpec
                {
                    Name = AbilityName.FanTheHammer,
                    Damage = 12.0,
                    Hits = 6,
                    RankScalingPerRank = DefaultRankScalingPerRank,
                },
                E = new AbilitySpec
                {
                    Name = AbilityName.Deadeye,
                    Damage = 60.0,
                    Hits = 1,
                    RankScalingPerRank = DefaultRankScalingPerRank,
                },
            });

            Set(HeroClass.Rancher, new HeroKit
            {
                MaxHp = 120.0,
                BasicAttackDamage = 12.0,
                QCooldownSeconds = DefaultQCooldownSeconds,
                ECooldownSeconds = DefaultECooldownSeconds,

                // The lasso row carries only its identity and its rank curve: its slow multiplier
                // and duration are fixture-locked on SimConfig, because G-018 supplies them by
                // those names.
                Q = new AbilitySpec
                {
                    Name = AbilityName.Lasso,
                    Hits = 1,
                    RankScalingPerRank = DefaultRankScalingPerRank,
                },
                E = new AbilitySpec
                {
                    Name = AbilityName.Stampede,
                    Damage = 25.0,
                    Hits = 1,
                    Radius = 4.0,
                    RankScalingPerRank = DefaultRankScalingPerRank,
                },
            });

            Set(HeroClass.Sawbones, new HeroKit
            {
                MaxHp = 200.0,
                BasicAttackDamage = 40.0,
                QCooldownSeconds = DefaultQCooldownSeconds,
                ECooldownSeconds = DefaultECooldownSeconds,
                Q = new AbilitySpec
                {
                    Name = AbilityName.Whirl,
                    Damage = 35.0,
                    Hits = 1,
                    Radius = 4.0,
                    RankScalingPerRank = DefaultRankScalingPerRank,
                },
                E = new AbilitySpec
                {
                    Name = AbilityName.Bulwark,
                    Hits = 1,
                    Magnitude = 0.6,
                    DurationSeconds = 2.0,
                    RankScalingPerRank = DefaultRankScalingPerRank,
                },
            });
        }

        /// <summary>How many classes are configured. Three on a fresh config (R-31).</summary>
        public int Count => _byClass.Count;

        /// <summary>Every configured class key.</summary>
        public IEnumerable<string> Classes => _byClass.Keys;

        /// <summary>Adds or replaces one class's kit (R-31).</summary>
        public void Set(string heroClass, HeroKit kit)
        {
            _byClass[heroClass] = kit;
        }

        /// <summary>True when <paramref name="heroClass"/> has a configured kit.</summary>
        public bool Contains(string heroClass) => _byClass.ContainsKey(heroClass);

        /// <summary>Kit for one class, or null when unconfigured. Prefer <see cref="KitFor"/>.</summary>
        public HeroKit TryGet(string heroClass)
        {
            return _byClass.TryGetValue(heroClass, out var kit) ? kit : null;
        }

        /// <summary>
        /// Kit for one class (R-31). Throws naming the missing key rather than returning a default.
        /// </summary>
        public HeroKit KitFor(string heroClass)
        {
            if (_byClass.TryGetValue(heroClass, out var kit))
            {
                return kit;
            }

            throw new KeyNotFoundException(
                "no hero kit configured for hero class '" + heroClass +
                "' (R-31); populate SimConfig.HeroKits before running the sim");
        }
    }
}
