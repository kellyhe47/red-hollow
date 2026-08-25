using System;
using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// Which way a mover should step (R-18) — the seam that keeps NavMesh pathing in the Unity
    /// shell while movement *distance* stays a sim rule.
    ///
    /// The division of labour is the whole point, and it is the one
    /// <see cref="IPathOracle"/> already established: the shell answers a geometry question the
    /// sim is not allowed to ask, and the sim keeps the rule that consumes the answer. R-18 routes
    /// monsters over a NavMesh, which is UnityEngine and can never live in GameSim (R-51, guarded
    /// at runtime by <c>Sim_assembly_has_no_unity_dependency</c>). But *how far* a mover gets is
    /// pure rule: R-17's per-archetype speed, DEC-008's lasso multiplier, and "the dead do not
    /// move" (R-33) are all sim concerns and belong nowhere else.
    ///
    /// So: the sim asks which way, and applies its own distance.
    ///
    /// A shell implementation wraps <c>NavMesh.CalculatePath</c> (or a live <c>NavMeshAgent</c>'s
    /// <c>steeringTarget</c>) and returns the direction of the first corner of the path, converted
    /// out of Unity space by <c>SimSpace</c>. <paramref name="moverId"/> is carried so the shell
    /// can pick the right agent type / area mask — R-18's parenthetical is that a Burrower's path
    /// ignores barricade obstacles, which is a *pathing* carve-out and therefore the shell's, the
    /// same way <see cref="Monster.IgnoresBarricadesAndHeroes"/> is the sim's half of DEC-007.
    ///
    /// Ticket 018 (T-18) declares the shape; nothing implements it yet.
    /// </summary>
    public interface IDirectionOracle
    {
        /// <summary>
        /// The direction <paramref name="moverId"/> should step, standing at
        /// <paramref name="from"/> and heading for <paramref name="to"/>.
        ///
        /// Magnitude carries no meaning — the caller owns speed, so a returned vector is treated
        /// as a direction only and never as a distance. A zero vector is the defined "no step":
        /// there is no path, or the mover is already where it is going. Answering with zero is how
        /// a NavMesh reports <c>PathPartial</c>/<c>PathInvalid</c> without the sim knowing what a
        /// NavMesh is.
        /// </summary>
        Vec2 DirectionFor(string moverId, Vec2 from, Vec2 to);
    }

    /// <summary>
    /// Straight at it. The default when no navigation data exists, exactly as
    /// <see cref="OpenPathOracle"/> is the default when nothing is known to block — solo play,
    /// editor scenarios and the whole sim test suite run on this, with no shell attached.
    ///
    /// Ticket 018 (T-18) declares the shape; nothing implements it yet.
    /// </summary>
    public sealed class StraightLineDirectionOracle : IDirectionOracle
    {
        public Vec2 DirectionFor(string moverId, Vec2 from, Vec2 to)
        {
            throw new NotImplementedException(
                "T-18 not implemented: unit-length direction from a mover straight toward its target");
        }
    }

    /// <summary>
    /// R-30 — how fast a hero walks.
    ///
    /// The PRD gives no number: R-31's class table is HP, basic attack, Q, E and passive, and R-30
    /// is about the *controls* (WASD), not the pace. So this is balance data with no source, and it
    /// is shaped like every other tunable in the repo — a default plus per-class overrides, keyed by
    /// the <see cref="HeroClass"/> constants the way <see cref="HeroKitCatalog"/> is.
    ///
    /// Plain fields, no resolution logic: which of the two a hero reads is a rule, and rules live in
    /// <see cref="MatchSim"/>.
    ///
    /// Ticket 018 (T-18) declares the shape; nothing reads it yet.
    /// </summary>
    public sealed class HeroMovementConfig
    {
        /// <summary>
        /// World units per second for a class with no entry in <see cref="MoveSpeedByClass"/>.
        ///
        /// A playtest starting point rather than a derived value — nothing in the PRD states it.
        /// Chosen against the R-17 roster it has to feel right beside: comfortably faster than the
        /// Shambler (2.0) and the Bull Behemoth (1.5) so a hero can reposition between shelters,
        /// and slower than the Ravager (5.0) so the archetype the PRD calls "fast" can actually
        /// run a hero down.
        /// </summary>
        public double DefaultMoveSpeed = 4.0;

        /// <summary>
        /// Per-class speed, keyed by the <see cref="HeroClass"/> constants. Empty by default: the
        /// three classes differ in HP by a factor of two (R-31) and may well want to differ in pace,
        /// but the PRD does not say they do, so shipping three invented numbers would be shipping a
        /// guess as spec. A class absent here moves at <see cref="DefaultMoveSpeed"/>.
        /// </summary>
        public readonly Dictionary<string, double> MoveSpeedByClass = new Dictionary<string, double>();
    }

    /// <summary>
    /// R-30 — one hero's movement input for one step.
    ///
    /// The direction is the client's, not an oracle's: WASD is a *command*, resolved by the shell's
    /// <c>HeroInput</c> into <c>HeroIntent.MoveDirection</c> and sent to the host like every other
    /// command (R-51). Monsters get their direction from <see cref="IDirectionOracle"/> because
    /// nobody is driving them; heroes are driven.
    ///
    /// Ticket 018 (T-18) declares the shape; nothing fills it in yet.
    /// </summary>
    public sealed class HeroMoveRequest
    {
        public string HeroId;

        /// <summary>
        /// Where the player is pushing. Magnitude carries no meaning — the sim owns speed — so a
        /// raw WASD diagonal (1, 1) is a direction, not a 1.41x sprint.
        /// </summary>
        public Vec2 Direction;

        /// <summary>The step this command covers, in sim seconds.</summary>
        public double DeltaSeconds;
    }

    /// <summary>
    /// What one hero's step produced. Ticket 018 (T-18) declares the shape; nothing fills it in yet.
    /// </summary>
    public sealed class HeroMoveResult : ISimResult
    {
        public string HeroId;

        /// <summary>Where the hero ended up — the host's authoritative answer to a predicted move.</summary>
        public Vec2 Pos;

        /// <summary>Ground actually covered, which is zero for a dead hero (R-33) or no input.</summary>
        public double DistanceMoved;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "hero_id", HeroId },
            { "x", Pos.X },
            { "y", Pos.Y },
            { "distance_moved", DistanceMoved },
        };
    }

    /// <summary>
    /// What one movement tick did to the monster roster (R-17 / R-18).
    ///
    /// A count rather than a per-monster list, deliberately. This command runs every tick of a
    /// match holding up to ~30 monsters (R-19), so it is the highest-frequency observation the sim
    /// produces; naming every mover on every tick would put a wave-sized payload on the wire 60
    /// times a second for information the client already re-reads from replicated positions.
    /// G-013 set the precedent by replicating <c>placeables.count</c> rather than the placeable, and
    /// <see cref="MatchSim.RecordMonsterKill"/> by declining to replicate the roster field by field.
    ///
    /// Ticket 018 (T-18) declares the shape; nothing fills it in yet.
    /// </summary>
    public sealed class MonsterMovementResult : ISimResult
    {
        /// <summary>The step this tick covered, in sim seconds.</summary>
        public double DeltaSeconds;

        /// <summary>How many monsters actually changed position.</summary>
        public int MonstersMoved;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "delta_seconds", DeltaSeconds },
            { "monsters_moved", MonstersMoved },
        };
    }
}
