using System.Collections.Generic;
using System.Linq;

namespace RedHollow.Sim
{
    /// <summary>
    /// R-30. The two ability keys a hero can press. Cooldowns (R-32) and saved ranks (R-31/R-43)
    /// are both keyed by slot, not by ability name, because a class's Q *is* one specific ability —
    /// the name is a property of the kit row, the slot is what the client sends.
    /// </summary>
    public static class AbilitySlot
    {
        public const string Q = "Q";

        public const string E = "E";
    }

    /// <summary>
    /// R-31 ability identities — one Q and one E per class. Only <see cref="Lasso"/> is
    /// fixture-locked (G-018 spells it exactly this way); the other five are named here so the
    /// kit catalog and the sim agree on one spelling instead of scattering string literals.
    /// </summary>
    public static class AbilityName
    {
        /// <summary>Gunslinger Q — 6-shot burst.</summary>
        public const string FanTheHammer = "fan_the_hammer";

        /// <summary>Gunslinger E — piercing line skillshot.</summary>
        public const string Deadeye = "deadeye";

        /// <summary>Rancher Q — 50% slow for 3.0s (G-018/G-019).</summary>
        public const string Lasso = "lasso";

        /// <summary>Rancher E — dash plus knockback.</summary>
        public const string Stampede = "stampede";

        /// <summary>Sawbones Q — AoE spin.</summary>
        public const string Whirl = "whirl";

        /// <summary>Sawbones E — 60% damage reduction for 2s.</summary>
        public const string Bulwark = "bulwark";
    }

    /// <summary>
    /// R-31 / R-32. A hero pressed Q or E.
    ///
    /// The geometry fields are the shell's contribution, exactly as with
    /// <see cref="HeroAttackRequest"/>: physics decides what the cast *crossed*, the sim decides
    /// what it *affects*. Which fields an ability reads is a property of the ability — a
    /// single-target cast reads <see cref="TargetId"/>, a skillshot reads
    /// <see cref="EntitiesOnLine"/>, a dash reads <see cref="AimDirection"/>, and a
    /// centred-on-self AoE reads none of them.
    /// </summary>
    public sealed class HeroAbilityRequest
    {
        public string CasterId;

        /// <summary>One of the <see cref="AbilitySlot"/> constants.</summary>
        public string Slot;

        /// <summary>Single-target abilities only (lasso, and any burst aimed at one monster).</summary>
        public string TargetId;

        /// <summary>Direction the hero was facing, for dash and knockback displacement.</summary>
        public Vec2 AimDirection;

        /// <summary>Ordered nearest-first along the aim line, for line/skillshot abilities.</summary>
        public List<LineEntity> EntitiesOnLine = new List<LineEntity>();
    }

    /// <summary>
    /// R-31 / R-32. What one Q/E press did.
    ///
    /// Deliberately NOT <see cref="AbilityResult"/>: that type is lasso-shaped and G-018 pins its
    /// three fields exactly, so it cannot grow an `accepted` flag or a hit list. This is the
    /// gated-cast shape, covering all six abilities plus the two ways a cast can be refused
    /// (locked ability, R-31; running cooldown, R-32).
    /// </summary>
    public sealed class AbilityCastOutcome : ISimResult
    {
        public bool Accepted;

        public string CasterId;

        /// <summary>The slot that was pressed.</summary>
        public string Slot;

        /// <summary>The <see cref="AbilityName"/> the caster's class binds to that slot.</summary>
        public string Ability;

        /// <summary>Rank the cast resolved at, capped at <see cref="SimConfig.MaxAbilityRank"/>.</summary>
        public int Rank;

        /// <summary>Why a refused cast was refused. Null when accepted.</summary>
        public string RejectionReason;

        /// <summary>Sim time this slot is castable again. A refusal reports the *running* deadline.</summary>
        public double CooldownReadyAt;

        /// <summary>Monsters this cast damaged, slowed or displaced. Never a hero (R-26/R-36).</summary>
        public readonly List<string> MonstersAffected = new List<string>();

        /// <summary>Damage dealt across every target and every hit of the cast.</summary>
        public double TotalDamage;

        /// <summary>When this cast's status effect ends, for the abilities that apply one.</summary>
        public double? EffectExpiresAt;

        public IDictionary<string, object> ToFields()
        {
            var fields = new Dictionary<string, object>
            {
                { "accepted", Accepted },
                { "caster_id", CasterId },
                { "slot", Slot },
                { "ability", Ability },
                { "rank", Rank },
                { "cooldown_ready_at", CooldownReadyAt },
            };

            if (Accepted)
            {
                fields["monsters_affected"] = MonstersAffected.Cast<object>().ToList();
                fields["total_damage"] = TotalDamage;
                if (EffectExpiresAt.HasValue)
                {
                    fields["effect_expires_at"] = EffectExpiresAt.Value;
                }
            }
            else
            {
                fields["rejection_reason"] = RejectionReason;
            }

            return fields;
        }
    }
}
