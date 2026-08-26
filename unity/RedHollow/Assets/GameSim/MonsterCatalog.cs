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

        /// <summary>
        /// R-17 — the reach of the archetype's attack, in ground units. Zero means melee: the
        /// mover walks all the way in and the arrival clamp is its reach, exactly as every
        /// archetype behaved before this column existed. Only the Spitter row ships a number —
        /// the PRD's "ranged acid, range 10" — and until it did, a Spitter was a slow Shambler
        /// that walked into hugging distance to spit.
        /// </summary>
        public double AttackRange;
    }

    /// <summary>
    /// The R-17 roster, keyed by the <see cref="MonsterType"/> constants. Ships with the five PRD
    /// archetypes, exactly as <see cref="SimConfig"/> ships every other tunable at its PRD value —
    /// the stat table is still balance *data* (R-16, "tunable in config without code changes"), so
    /// the Unity shell overwrites rows from a ScriptableObject and never edits this file to rebalance.
    /// Seeding happens per instance, so one config's tuning cannot leak into another's.
    ///
    /// An archetype outside the roster still throws rather than defaulting, so a wave table naming a
    /// monster nobody specified surfaces as a loud failure instead of a zero-HP monster.
    /// </summary>
    public sealed class MonsterCatalog
    {
        private readonly Dictionary<string, MonsterStats> _byType =
            new Dictionary<string, MonsterStats>();

        public MonsterCatalog()
        {
            SeedRosterDefaults();
        }

        /// <summary>How many archetypes are configured. Five on a fresh config (R-17).</summary>
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

        /// <summary>
        /// The R-17 roster, verbatim from the PRD. Bounty is one number with two uses: the scrip
        /// paid into the shared pool on death (R-20) and the XP granted to the killer (R-40).
        ///
        /// Fresh <see cref="MonsterStats"/> per catalog, never a shared static table — a caller that
        /// mutates its own config's row must not move every other match's numbers with it.
        /// </summary>
        private void SeedRosterDefaults()
        {
            Set(MonsterType.Shambler, new MonsterStats
            {
                MaxHp = 60.0, AttackDamage = 10.0, MoveSpeed = 2.0, Bounty = 10,
            });
            Set(MonsterType.Ravager, new MonsterStats
            {
                MaxHp = 40.0, AttackDamage = 8.0, MoveSpeed = 5.0, Bounty = 15,
            });
            Set(MonsterType.Spitter, new MonsterStats
            {
                MaxHp = 50.0, AttackDamage = 12.0, MoveSpeed = 2.0, Bounty = 20, AttackRange = 10.0,
            });
            Set(MonsterType.Burrower, new MonsterStats
            {
                MaxHp = 80.0, AttackDamage = 15.0, MoveSpeed = 2.5, Bounty = 30,
            });
            Set(MonsterType.BullBehemoth, new MonsterStats
            {
                MaxHp = 400.0, AttackDamage = 40.0, MoveSpeed = 1.5, Bounty = 50,
            });
        }
    }
}
