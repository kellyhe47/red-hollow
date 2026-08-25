using System.Collections.Generic;
using System.Linq;

namespace RedHollow.Sim
{
    // ---- Requests: what a client (or the host's own AI loop) asks the sim to do -------------------

    public sealed class HotspotAttackRequest
    {
        public string AttackerId;
        public string AttackerType;
        public double Damage;
        public string TargetId;
    }

    public sealed class MonsterKillRequest
    {
        public string MonsterId;
        public string MonsterType;
        public int Bounty;

        /// <summary>R-40: the credited player. Turret kills credit the placer, not the turret.</summary>
        public string KillerHeroId;
    }

    public sealed class PurchaseRequest
    {
        public string PlayerId;
        public string PlaceableType;
        public int Cost;
        public Vec2 Pos;

        /// <summary>R-24: zone validity, decided by the shell's placement checker.</summary>
        public bool ZoneValid = true;
    }

    public sealed class SellRequest
    {
        public string PlayerId;
        public string PlaceableId;
    }

    public sealed class AbilityCastRequest
    {
        public string CasterId;
        public string Ability;
        public string TargetId;
    }

    public sealed class HeroDamageRequest
    {
        public string AttackerId;
        public string AttackerType;
        public double Damage;
        public string TargetId;
    }

    /// <summary>An entity the aim line crossed, as reported by the shell's raycast.</summary>
    public sealed class LineEntity
    {
        public string Id;
        public string Kind;
        public Vec2 Pos;
    }

    public sealed class HeroAttackRequest
    {
        public string AttackerId;
        public string AttackerClass;
        public double Damage;

        /// <summary>Ordered nearest-first along the aim line. Physics decides who is on it; the sim decides who is hit.</summary>
        public List<LineEntity> EntitiesOnLine = new List<LineEntity>();
    }

    public sealed class SpendSkillPointRequest
    {
        public string AccountId;
        public string HeroId;

        /// <summary>"unlock_Q", "unlock_E", "rank_Q", "rank_E" (R-42).</summary>
        public string Choice;
    }

    // ---- Results: typed for the host, field-shaped for replication and for the fixtures -----------

    public sealed class TargetSelectionResult : ISimResult
    {
        public string MonsterId;
        public string TargetId;
        public double Distance;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "monster_id", MonsterId },
            { "target_id", TargetId },
            { "distance", Distance },
        };
    }

    public sealed class HotspotAttackResult : ISimResult
    {
        public string HotspotId;
        public int CiviliansKilled;
        public int CiviliansRemaining;
        public int TotalCiviliansRemaining;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "hotspot_id", HotspotId },
            { "civilians_killed", CiviliansKilled },
            { "civilians_remaining", CiviliansRemaining },
            { "total_civilians_remaining", TotalCiviliansRemaining },
        };
    }

    public sealed class MonsterKillResult : ISimResult
    {
        public string MonsterId;
        public int BountyAwarded;
        public int ScripAfter;
        public int LivingMonstersRemaining;
        public bool WaveComplete;
        public bool MapVictory;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "monster_id", MonsterId },
            { "bounty_awarded", BountyAwarded },
            { "scrip_after", ScripAfter },
            { "living_monsters_remaining", LivingMonstersRemaining },
            { "wave_complete", WaveComplete },
            { "map_victory", MapVictory },
        };
    }

    public sealed class PurchaseResult : ISimResult
    {
        public bool Accepted;
        public string PlaceableType;
        public int ScripAfter;
        public string RejectionReason;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "accepted", Accepted },
            { "placeable_type", PlaceableType },
            { "scrip_after", ScripAfter },
            { "rejection_reason", RejectionReason },
        };
    }

    public sealed class PlanningPhaseResult : ISimResult
    {
        public int Wave;
        public int Scrip;
        public double PlanningSeconds;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "wave", Wave },
            { "scrip", Scrip },
            { "planning_seconds", PlanningSeconds },
        };
    }

    public sealed class ReadyResult : ISimResult
    {
        public bool AllReady;
        public bool CombatStarted;
        public double PlanningElapsed;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "all_ready", AllReady },
            { "combat_started", CombatStarted },
            { "planning_elapsed", PlanningElapsed },
        };
    }

    public sealed class AbilityResult : ISimResult
    {
        public string TargetId;
        public double SpeedAfter;
        public double SlowExpiresAt;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "target_id", TargetId },
            { "speed_after", SpeedAfter },
            { "slow_expires_at", SlowExpiresAt },
        };
    }

    public sealed class ExpiredStatus
    {
        public string TargetId;
        public string Status;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "target_id", TargetId },
            { "status", Status },
        };
    }

    public sealed class StatusTickResult : ISimResult
    {
        public readonly List<ExpiredStatus> Expired = new List<ExpiredStatus>();

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "expired", Expired.Select(e => e.ToFields()).ToList() },
        };
    }

    public sealed class HeroDamageResult : ISimResult
    {
        public string HeroId;
        public double DamageTaken;
        public double HpAfter;
        public bool Downed;
        public double? RespawnAt;

        public IDictionary<string, object> ToFields()
        {
            var fields = new Dictionary<string, object>
            {
                { "hero_id", HeroId },
                { "damage_taken", DamageTaken },
                { "hp_after", HpAfter },
                { "downed", Downed },
            };

            // Only a downed hero has a respawn clock (G-020 has no such key, G-021 does).
            if (Downed && RespawnAt.HasValue)
            {
                fields["respawn_at"] = RespawnAt.Value;
            }

            return fields;
        }
    }

    public sealed class SellResult : ISimResult
    {
        public bool Accepted;
        public int Refund;
        public int ScripAfter;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "accepted", Accepted },
            { "refund", Refund },
            { "scrip_after", ScripAfter },
        };
    }

    public sealed class XpAwardResult : ISimResult
    {
        public string HeroId;
        public double XpAwarded;
        public double LifetimeXp;
        public int Level;
        public bool LeveledUp;
        public int SkillPoints;
        public double XpIntoLevel;
        public double XpForNextLevel;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "hero_id", HeroId },
            { "xp_awarded", XpAwarded },
            { "lifetime_xp", LifetimeXp },
            { "level", Level },
            { "leveled_up", LeveledUp },
            { "skill_points", SkillPoints },
            { "xp_into_level", XpIntoLevel },
            { "xp_for_next_level", XpForNextLevel },
        };
    }

    public sealed class SpendSkillPointResult : ISimResult
    {
        public bool Accepted;
        public string Choice;
        public int SkillPointsAfter;
        public string RejectionReason;
        public IDictionary<string, int> Abilities;

        public IDictionary<string, object> ToFields()
        {
            var fields = new Dictionary<string, object>
            {
                { "accepted", Accepted },
                { "choice", Choice },
                { "skill_points_after", SkillPointsAfter },
            };

            if (Accepted)
            {
                fields["abilities"] = Abilities.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            }
            else
            {
                fields["rejection_reason"] = RejectionReason;
            }

            return fields;
        }
    }

    /// <summary>A single-target trap firing (spike trap, R-23 / G-027).</summary>
    public sealed class TrapTriggerResult : ISimResult
    {
        public string PlaceableId;
        public double DamageDealt;
        public double MonsterHpAfter;
        public int TriggersRemaining;
        public bool Broke;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "placeable_id", PlaceableId },
            { "damage_dealt", DamageDealt },
            { "monster_hp_after", MonsterHpAfter },
            { "triggers_remaining", TriggersRemaining },
            { "broke", Broke },
        };
    }

    /// <summary>An area trap firing once (dynamite, R-23 / G-029).</summary>
    public sealed class BlastTriggerResult : ISimResult
    {
        public string PlaceableId;
        public readonly List<string> MonstersHit = new List<string>();
        public double DamageEach;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "placeable_id", PlaceableId },
            { "monsters_hit", MonstersHit.Cast<object>().ToList() },
            { "damage_each", DamageEach },
        };
    }

    public sealed class TurretTickResult : ISimResult
    {
        public string TurretId;
        public string TargetId;
        public double Distance;
        public double DamageDealt;
        public double TargetHpAfter;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "turret_id", TurretId },
            { "target_id", TargetId },
            { "distance", Distance },
            { "damage_dealt", DamageDealt },
            { "target_hp_after", TargetHpAfter },
        };
    }

    public sealed class HeroAttackResult : ISimResult
    {
        public string AttackerId;
        public string HitId;
        public double DamageDealt;
        public double TargetHpAfter;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "attacker_id", AttackerId },
            { "hit_id", HitId },
            { "damage_dealt", DamageDealt },
            { "target_hp_after", TargetHpAfter },
        };
    }
}
