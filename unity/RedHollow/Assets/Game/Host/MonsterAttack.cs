using System.Collections.Generic;
using RedHollow.Sim;

namespace RedHollow.Game.Host
{
    /// <summary>
    /// "Monster M wants to hit T for D this step." Produced by the shell's movement/animation layer,
    /// consumed by <see cref="HostLoop"/>, which is the only thing allowed to turn it into damage —
    /// and only after <see cref="ISimHost.TryMonsterAttack"/> says yes (R-18).
    ///
    /// Shaped like the sim's own request types (plain mutable fields) on purpose: the host loop
    /// copies these straight into a <see cref="HotspotAttackRequest"/> /
    /// <see cref="HeroDamageRequest"/> / <see cref="PlaceableDamageRequest"/> without computing
    /// anything. <see cref="Damage"/> comes from the R-17 catalog on
    /// <see cref="SimConfig.Monsters"/>, never from a number typed into the shell.
    /// </summary>
    public sealed class MonsterAttackIntent
    {
        public string MonsterId;
        public string MonsterType;
        public string TargetId;

        /// <summary>Which of the three damage commands this intent routes to. Reuses the sim's own R-16 enum.</summary>
        public TargetKind TargetKind;

        public double Damage;
    }

    /// <summary>
    /// Where <see cref="HostLoop"/> gets this step's candidate monster attacks. A seam rather than a
    /// concrete class so the loop can be driven from a NavMesh/animation source in the shell (ticket
    /// 016) and from a scripted list in tests, without either knowing about the other.
    /// </summary>
    public interface IMonsterAttackSource
    {
        /// <summary>
        /// Candidates only — every one still has to clear <see cref="ISimHost.TryMonsterAttack"/>
        /// before any damage command is issued (R-18).
        /// </summary>
        IReadOnlyList<MonsterAttackIntent> AttacksReadyThisStep(ISimHost sim, double deltaSeconds);
    }
}
