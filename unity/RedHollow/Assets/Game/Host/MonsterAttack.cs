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

    /// <summary>
    /// Ticket 019 (T-19) — the shell's answer to "who is close enough to swing this step?" (R-18).
    ///
    /// R-18 splits the swing in two: the sim owns the *cadence* (one hit per
    /// <see cref="SimConfig.MonsterAttackIntervalSeconds"/>, claimed through
    /// <see cref="ISimHost.TryMonsterAttack"/>) and something outside it has to own *contact*,
    /// because contact is geometry and the sim holds none (R-51). This is that something, and it
    /// is deliberately the dumbest possible version: every candidate it names still has to clear
    /// the gate before a single point of damage is applied, so being generous here costs nothing
    /// and being clever here would be a second rate limiter fighting the first.
    ///
    /// <b>Reach is derived, never typed.</b> The PRD names no melee range, and a number invented
    /// in the shell would ship as spec. What it uses instead is the sim's own arrival rule
    /// (MatchSim.Movement.cs clamps a step to the ground there is left to cover, so a monster
    /// lands *on* its target and stays there): a monster is in contact when the gap left is no
    /// wider than the step it just took, which is exactly "it has arrived, or it arrived this
    /// tick". Retuning R-17's Speed column or the host's step rate retunes it with them.
    ///
    /// Plain C# and stateless — the cooldown it would otherwise have to remember is the sim's
    /// (<see cref="ISimHost.TryMonsterAttack"/> is ask-and-claim), so one of these can drive any
    /// number of matches.
    /// </summary>
    public sealed class ContactMonsterAttacks : IMonsterAttackSource
    {
        /// <summary>Shared and empty so a step in which nobody has arrived allocates nothing.</summary>
        private static readonly IReadOnlyList<MonsterAttackIntent> None = new MonsterAttackIntent[0];

        /// <summary>
        /// R-18 — every living monster standing on the target R-16 gave it. Damage comes off the
        /// R-17 catalog row for the archetype (<see cref="SimConfig.Monsters"/>) rather than from a
        /// number here, so a retuned catalog retunes the wave; an archetype the catalog has no row
        /// for is skipped rather than thrown on, because a host loop must not die mid-step over one
        /// mistyped monster.
        /// </summary>
        public IReadOnlyList<MonsterAttackIntent> AttacksReadyThisStep(ISimHost sim, double deltaSeconds)
        {
            if (sim == null || sim.State == null)
            {
                return None;
            }

            List<MonsterAttackIntent> ready = null;

            foreach (var monster in sim.State.Monsters.Values)
            {
                // Corpses stay in the roster until it is cleared (they are flagged, not deleted),
                // so a source that walked the dictionary blind would have the graveyard swinging.
                if (monster == null || !monster.Alive || string.IsNullOrEmpty(monster.TargetId))
                {
                    continue;
                }

                if (!TryTarget(sim.State, monster.TargetId, out var targetPos, out var targetKind))
                {
                    continue;
                }

                // See the class doc: the sim's arrival clamp is what makes this a derived reach
                // rather than an invented one — plus the archetype's own attack range (R-17),
                // which is zero for melee and the PRD's 10 for the Spitter's acid. Movement holds
                // a ranged monster at that line, so without adding it here a Spitter would stand
                // at its reach forever and never be offered a swing.
                var reach = (monster.AttackRange > 0.0 ? monster.AttackRange : 0.0)
                            + (monster.CurrentSpeed * deltaSeconds);
                if (monster.Pos.DistanceTo(targetPos) > reach)
                {
                    continue;
                }

                var stats = sim.Config == null ? null : sim.Config.Monsters.TryGet(monster.Type);
                if (stats == null)
                {
                    continue;
                }

                if (ready == null)
                {
                    ready = new List<MonsterAttackIntent>();
                }

                ready.Add(new MonsterAttackIntent
                {
                    MonsterId = monster.Id,
                    MonsterType = monster.Type,
                    TargetId = monster.TargetId,
                    TargetKind = targetKind,
                    Damage = stats.AttackDamage,
                });
            }

            return ready ?? None;
        }

        /// <summary>
        /// Where a target id stands and which damage command it routes to, across the three things
        /// R-16 lets a monster pick. Mirrors MatchSim's own target resolution, including its
        /// readings of "gone": a dead hero and a destroyed placeable have left the field, so
        /// neither can be swung at — while an emptied shelter is still a building standing in the
        /// colony, and whether hitting it is worth anything is R-11's answer to give, not this
        /// one's.
        /// </summary>
        private static bool TryTarget(MatchState state, string targetId, out Vec2 pos, out TargetKind kind)
        {
            pos = new Vec2(0.0, 0.0);
            kind = TargetKind.Hotspot;

            if (state.Heroes.TryGetValue(targetId, out var hero))
            {
                pos = hero.Pos;
                kind = TargetKind.Hero;
                return hero.Alive;
            }

            if (state.Hotspots.TryGetValue(targetId, out var hotspot))
            {
                pos = hotspot.Pos;
                kind = TargetKind.Hotspot;
                return true;
            }

            if (state.Placeables.TryGetValue(targetId, out var placeable))
            {
                pos = placeable.Pos;

                // TargetKind has one placeable member (R-16 names barricades as the thing a monster
                // stops to chew), and ApplyPlaceableDamage is the one command for all of them.
                kind = TargetKind.Barricade;
                return placeable.Exists;
            }

            return false;
        }
    }
}
