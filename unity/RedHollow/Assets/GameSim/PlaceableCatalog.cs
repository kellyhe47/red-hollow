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
    /// The R-23 catalog, keyed by the <see cref="PlaceableType"/> constants. Ships with the five PRD
    /// placeables, exactly as <see cref="MonsterCatalog"/> ships the R-17 roster (DEC-RUN-1) — the
    /// numbers are still balance *data* ("numeric stats config-tunable"), so the Unity shell
    /// overwrites rows from a ScriptableObject and never edits this file to rebalance. Seeding
    /// happens per instance, so one config's tuning cannot leak into another's.
    ///
    /// Looking up an unconfigured placeable throws — a free-to-place or no-op defence must never be
    /// shippable by omission.
    /// </summary>
    public sealed class PlaceableCatalog
    {
        private readonly Dictionary<string, PlaceableStats> _byType =
            new Dictionary<string, PlaceableStats>();

        public PlaceableCatalog()
        {
            SeedCatalogDefaults();
        }

        /// <summary>How many placeables are configured. Five on a fresh config (R-23).</summary>
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

        /// <summary>
        /// R-23 / DEC-023, verbatim from the PRD table — cost *and* effect columns:
        ///
        ///   barricade     100 — a 300 HP wall that blocks paths (R-16 / B-002, not Burrowers);
        ///   spike trap     75 — 30 damage per monster crossing, 10 triggers then it breaks;
        ///   dynamite trap 150 — 150 AoE damage, single use;
        ///   turret        250 — 20 damage per tick at range 8, nearest living monster;
        ///   med station   200 — heals heroes 5 HP/s inside radius 5 (R-35 says it stacks with
        ///                       out-of-combat regen, so it is a second source and not a cap).
        ///
        /// A row carries only the columns its mechanic reads: R-23 gives HP to the barricade row
        /// alone, which is what makes a wall the one placeable
        /// <see cref="MatchSim.ApplyPlaceableDamage"/> can destroy. Leaving the other four at
        /// <see cref="PlaceableStats.MaxHp"/> = 0 is deliberate — a turret is not a 0 HP entity
        /// waiting to be deleted, it is an entity the damage rule does not apply to.
        ///
        /// <see cref="PlaceableStats.BlastRadius"/> is the one number the PRD does NOT state:
        /// R-23's dynamite row says only "150 dmg AoE, single use", and 3.0 exists solely inside
        /// G-029's `given.inputs`. It is seeded to that 3.0 so the shipped catalog and the
        /// acceptance fixture describe the same weapon — flagged as owner-confirmable rather than
        /// spec, and tunable here like every other number.
        ///
        /// Fresh <see cref="PlaceableStats"/> per catalog, never a shared static table — a caller
        /// that retunes its own config's row must not move every other match's prices with it.
        /// </summary>
        private void SeedCatalogDefaults()
        {
            Set(PlaceableType.Barricade, new PlaceableStats { Cost = 100, MaxHp = 300.0 });
            Set(PlaceableType.SpikeTrap, new PlaceableStats { Cost = 75, Damage = 30.0, TriggerCount = 10 });
            Set(PlaceableType.DynamiteTrap, new PlaceableStats
            {
                Cost = 150,
                Damage = 150.0,
                TriggerCount = 1,
                BlastRadius = 3.0,
            });
            Set(PlaceableType.Turret, new PlaceableStats { Cost = 250, Damage = 20.0, Range = 8.0 });
            Set(PlaceableType.MedStation, new PlaceableStats
            {
                Cost = 200,
                HealPerSecond = 5.0,
                Range = 5.0,
            });
        }
    }
}
