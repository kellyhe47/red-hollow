using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// One row of the R-23 placeable catalog: purchase cost plus the effect numbers that row needs.
    /// Not every placeable uses every field — a barricade has HP and no range, a turret has range and
    /// no HP. R-23 fixture-locks the *mechanics*; these numbers stay config-tunable.
    /// </summary>
    public sealed class PlaceableStats
    {
        /// <summary>R-21 / R-23 — scrip charged on purchase, and the basis of the R-22 sell refund.</summary>
        public int Cost;

        /// <summary>R-23 — HP for placeables that can be destroyed (barricade wall).</summary>
        public double MaxHp;

        /// <summary>R-23 — damage dealt per application: per crossing, per blast, or per DPS tick.</summary>
        public double Damage;

        /// <summary>R-23 / G-027 — crossings a spike trap survives before it breaks.</summary>
        public int TriggerCount;

        /// <summary>R-23 / G-029 — AoE radius of a single-use dynamite blast.</summary>
        public double BlastRadius;

        /// <summary>R-23 / G-028 — reach: turret targeting range, med-station heal radius.</summary>
        public double Range;

        /// <summary>R-23 — HP per second a med station restores to heroes inside <see cref="Range"/>.</summary>
        public double HealPerSecond;
    }

    /// <summary>
    /// The R-23 catalog, keyed by the <see cref="PlaceableType"/> constants. Ships empty: costs and
    /// effect numbers are balance data, tuned without a code change. Looking up an unconfigured
    /// placeable throws — a free-to-place or no-op defence must never be shippable by omission.
    /// </summary>
    public sealed class PlaceableCatalog
    {
        private readonly Dictionary<string, PlaceableStats> _byType =
            new Dictionary<string, PlaceableStats>();

        /// <summary>How many placeables are configured. Zero on a fresh config.</summary>
        public int Count => _byType.Count;

        /// <summary>Every configured placeable key.</summary>
        public IEnumerable<string> Types => _byType.Keys;

        /// <summary>Adds or replaces one placeable's stats (R-23).</summary>
        public void Set(string placeableType, PlaceableStats stats)
        {
            _byType[placeableType] = stats;
        }

        /// <summary>True when <paramref name="placeableType"/> has a configured row.</summary>
        public bool Contains(string placeableType) => _byType.ContainsKey(placeableType);

        /// <summary>Stats for one placeable, or null when unconfigured. Prefer <see cref="StatsFor"/>.</summary>
        public PlaceableStats TryGet(string placeableType)
        {
            return _byType.TryGetValue(placeableType, out var stats) ? stats : null;
        }

        /// <summary>
        /// Stats for one placeable (R-23). Throws naming the missing key rather than returning a
        /// default — a zero cost would let the team buy it for nothing (R-21).
        /// </summary>
        public PlaceableStats StatsFor(string placeableType)
        {
            if (_byType.TryGetValue(placeableType, out var stats))
            {
                return stats;
            }

            throw new KeyNotFoundException(
                "no placeable stats configured for placeable type '" + placeableType +
                "' (R-23); populate SimConfig.Placeables before running the sim");
        }
    }
}
