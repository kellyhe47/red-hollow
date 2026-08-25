using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// R-23 / R-16. Something hit a placeable.
    ///
    /// Shaped exactly like <see cref="HotspotAttackRequest"/> and <see cref="HeroDamageRequest"/>:
    /// the three damageable things in this sim (shelter, hero, placeable) are hit through the same
    /// four fields, so a caller that can damage one can damage any of them without learning a new
    /// vocabulary. <see cref="AttackerType"/> is carried for the same reason the other two carry it
    /// — the shell wants it for hit feedback and it is what a future per-archetype rule would read.
    /// </summary>
    public sealed class PlaceableDamageRequest
    {
        public string AttackerId;
        public string AttackerType;
        public double Damage;
        public string TargetId;
    }

    /// <summary>
    /// R-23 / R-16. The outcome of a hit on a placeable, shaped like
    /// <see cref="HeroDamageResult"/> — how much went in, what is left, and whether that was the
    /// hit that removed it from the world (R-16's "until destroyed").
    /// </summary>
    public sealed class PlaceableDamageResult : ISimResult
    {
        public string PlaceableId;
        public double DamageTaken;
        public double HpAfter;
        public bool Destroyed;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "placeable_id", PlaceableId },
            { "damage_taken", DamageTaken },
            { "hp_after", HpAfter },
            { "destroyed", Destroyed },
        };
    }
}
