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
    /// Hero kits keyed by the <see cref="HeroClass"/> constants (R-31). Ships empty: kit numbers are
    /// balance data. Asking for an unconfigured class throws — a hero that spawned with 0 max HP
    /// would die on the first hit and look like a combat bug, not a config gap.
    /// </summary>
    public sealed class HeroKitCatalog
    {
        private readonly Dictionary<string, HeroKit> _byClass = new Dictionary<string, HeroKit>();

        /// <summary>How many classes are configured. Zero on a fresh config.</summary>
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
