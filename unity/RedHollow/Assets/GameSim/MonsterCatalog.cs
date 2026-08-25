using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// One row of the R-17 roster: everything a monster archetype is, numerically. R-16 requires the
    /// roster be data, not code, so these are plain mutable fields the Unity shell fills from a
    /// ScriptableObject and the golden adapter fills from a fixture.
    /// </summary>
    public sealed class MonsterStats
    {
        /// <summary>R-17 — HP the archetype spawns with.</summary>
        public double MaxHp;

        /// <summary>R-17 — damage per attack, landed once per second (R-18).</summary>
        public double AttackDamage;

        /// <summary>R-17 — base move speed, before slows such as the lasso (R-31).</summary>
        public double MoveSpeed;

        /// <summary>
        /// R-17 — scrip paid into the shared pool on death (R-20), and by R-40 the XP granted to the
        /// killing player. One number, two uses: bounty *is* XP.
        /// </summary>
        public int Bounty;
    }

    /// <summary>
    /// The R-17 roster, keyed by the <see cref="MonsterType"/> constants. Ships empty: the stat table
    /// is balance data (R-16, "tunable in config without code changes"), owned by whoever configures
    /// the match, never baked into the sim. An unconfigured archetype throws rather than defaulting,
    /// so a missing roster row surfaces as a loud failure instead of a zero-HP monster.
    /// </summary>
    public sealed class MonsterCatalog
    {
        private readonly Dictionary<string, MonsterStats> _byType =
            new Dictionary<string, MonsterStats>();

        /// <summary>How many archetypes are configured. Zero on a fresh config.</summary>
        public int Count => _byType.Count;

        /// <summary>Every configured archetype key.</summary>
        public IEnumerable<string> Types => _byType.Keys;

        /// <summary>Adds or replaces one archetype's stats (R-17).</summary>
        public void Set(string monsterType, MonsterStats stats)
        {
            _byType[monsterType] = stats;
        }

        /// <summary>True when <paramref name="monsterType"/> has a configured row.</summary>
        public bool Contains(string monsterType) => _byType.ContainsKey(monsterType);

        /// <summary>Stats for one archetype, or null when unconfigured. Prefer <see cref="StatsFor"/>.</summary>
        public MonsterStats TryGet(string monsterType)
        {
            return _byType.TryGetValue(monsterType, out var stats) ? stats : null;
        }

        /// <summary>
        /// Stats for one archetype (R-17). Throws naming the missing key rather than returning a
        /// default — silently zeroed monster stats would corrupt every downstream number.
        /// </summary>
        public MonsterStats StatsFor(string monsterType)
        {
            if (_byType.TryGetValue(monsterType, out var stats))
            {
                return stats;
            }

            throw new KeyNotFoundException(
                "no monster stats configured for monster type '" + monsterType +
                "' (R-17); populate SimConfig.Monsters before running the sim");
        }
    }
}
