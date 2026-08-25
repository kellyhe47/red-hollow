using System.Collections.Generic;
using System.Linq;

namespace RedHollow.Sim
{
    /// <summary>A timed effect riding on a monster (today: lasso slow, R-31 / G-018 / G-019).</summary>
    public sealed class StatusEffect
    {
        public readonly string Type;
        public readonly double ExpiresAt;

        public StatusEffect(string type, double expiresAt)
        {
            Type = type;
            ExpiresAt = expiresAt;
        }

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "type", Type },
            { "expires_at", ExpiresAt },
        };
    }

    /// <summary>A monster instance. Archetype stats come from config (R-16, R-17).</summary>
    public sealed class Monster
    {
        public string Id;
        public string Type;
        public Vec2 Pos;
        public double Hp;
        public bool Alive = true;
        public double BaseSpeed;
        public double CurrentSpeed;
        public string TargetId;
        public readonly List<StatusEffect> StatusEffects = new List<StatusEffect>();

        /// <summary>DEC-007 / G-005: Burrowers tunnel — barricades and heroes are invisible to them.</summary>
        public bool IgnoresBarricadesAndHeroes => Type == MonsterType.Burrower;
    }

    /// <summary>A hero instance (R-31, R-33).</summary>
    public sealed class Hero
    {
        public string Id;
        public string HeroClass;
        public string AccountId;
        public Vec2 Pos;
        public double Hp;
        public double MaxHp;
        public bool Alive = true;
        public double? RespawnAt;
    }

    /// <summary>
    /// A civilian shelter. Its civilian count *is* its HP (R-11 / DEC-002) — there is no separate
    /// health bar and no civilian agent simulation (R-72).
    /// </summary>
    public sealed class Hotspot
    {
        public string Id;
        public Vec2 Pos;
        public int Civilians;

        /// <summary>R-12 / G-002: an emptied hotspot stops being a valid monster target.</summary>
        public bool IsValidTarget => Civilians >= 1;
    }

    /// <summary>A purchased defence (R-23). Barricades are placeables too.</summary>
    public sealed class Placeable
    {
        public string Id;
        public string Type;
        public Vec2 Pos;
        public string OwnerPlayerId;
        public int PurchaseCost;
        public double Hp;
        public bool Exists = true;

        // Type-specific stats, config-tunable (R-23).
        public double Damage;
        public int TriggersRemaining;
        public double BlastRadius;
        public double Range;

        public bool IsBarricade => Type == PlaceableType.Barricade;
    }

    /// <summary>A connected player. Ready state drives the early combat start (R-03 / G-017).</summary>
    public sealed class PlayerSlot
    {
        public string Id;
        public string AccountId;
        public string HeroClass;
        public bool Ready;
        public bool Connected = true;
    }

    /// <summary>The single shared scrip pool (R-20 / DEC-005). Any player may spend from it (R-25).</summary>
    public sealed class TeamState
    {
        public int Scrip;
    }

    /// <summary>Where we are in the 10-wave campaign (R-01, R-02).</summary>
    public sealed class WaveState
    {
        public int Number = 1;
        public int TotalWaves = 10;
        public readonly List<string> LivingMonsterIds = new List<string>();
    }

    /// <summary>
    /// The whole host-authoritative world. Everything fixture-covered reads and writes through here,
    /// and only the host ever owns an instance (R-51, R-54).
    /// </summary>
    public sealed class MatchState
    {
        public string Phase = MatchPhase.Combat;
        public string Status = MatchStatus.InProgress;
        public double PlanningStartedAt;

        public readonly TeamState Team = new TeamState();
        public readonly WaveState Wave = new WaveState();

        public readonly Dictionary<string, Monster> Monsters = new Dictionary<string, Monster>();
        public readonly Dictionary<string, Hero> Heroes = new Dictionary<string, Hero>();
        public readonly Dictionary<string, Hotspot> Hotspots = new Dictionary<string, Hotspot>();
        public readonly Dictionary<string, Placeable> Placeables = new Dictionary<string, Placeable>();
        public readonly List<PlayerSlot> Players = new List<PlayerSlot>();

        /// <summary>R-02 / DEC-002: the only loss condition is this reaching zero.</summary>
        public int TotalCivilians => Hotspots.Values.Sum(h => h.Civilians);

        /// <summary>Placeables still standing — G-013 replicates this as `placeables.count`.</summary>
        public int PlaceableCount => Placeables.Values.Count(p => p.Exists);

        public bool IsOver => Status == MatchStatus.Victory || Status == MatchStatus.Defeat;
    }

    /// <summary>
    /// Answers "is something blocking the path from A to B?" (R-16 / G-004). Production supplies a
    /// NavMesh-backed implementation from the Unity shell; the sim never touches physics itself.
    /// </summary>
    public interface IPathOracle
    {
        /// <summary>Id of the blocking placeable, or null when the path is clear.</summary>
        string BlockerBetween(string moverId, string targetId);
    }

    /// <summary>
    /// A path oracle built from explicitly declared blocking relations. Used by the golden adapter
    /// and by editor-time scenario tests, where there is no NavMesh to ask.
    /// </summary>
    public sealed class DeclaredPathOracle : IPathOracle
    {
        private readonly Dictionary<string, string> _blockers = new Dictionary<string, string>();

        public void Declare(string moverId, string targetId, string blockerId)
        {
            _blockers[moverId + "->" + targetId] = blockerId;
        }

        public string BlockerBetween(string moverId, string targetId)
        {
            return _blockers.TryGetValue(moverId + "->" + targetId, out var blocker) ? blocker : null;
        }
    }

    /// <summary>Nothing ever blocks. The default when no navigation data exists.</summary>
    public sealed class OpenPathOracle : IPathOracle
    {
        public string BlockerBetween(string moverId, string targetId) => null;
    }
}
