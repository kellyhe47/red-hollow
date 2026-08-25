using System.Collections.Generic;

namespace RedHollow.Sim
{
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
