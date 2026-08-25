using System;

namespace RedHollow.Sim
{
    /// <summary>
    /// Plain 2D point. The sim is deliberately UnityEngine-free (R-51), so it carries its own
    /// vector type rather than borrowing Vector2. The Unity shell converts at the boundary.
    /// </summary>
    public struct Vec2 : IEquatable<Vec2>
    {
        public readonly double X;
        public readonly double Y;

        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        /// <summary>Straight-line distance — the metric R-16 targets on.</summary>
        public double DistanceTo(Vec2 other)
        {
            double dx = X - other.X;
            double dy = Y - other.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);

        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();

        public override string ToString() => "(" + X + ", " + Y + ")";
    }

    /// <summary>What a monster is allowed to consider when picking a target (R-16).</summary>
    public enum TargetKind
    {
        Hero,
        Hotspot,
        Barricade,
    }

    /// <summary>Match phase — the FSM spine of R-03.</summary>
    public static class MatchPhase
    {
        public const string Lobby = "lobby";
        public const string Planning = "planning";
        public const string Combat = "combat";
    }

    /// <summary>
    /// Terminal match status. Distinct from phase: a wave completing moves the *phase* back to
    /// planning, while winning or losing moves the *status* (G-010 vs G-011/G-008).
    /// </summary>
    public static class MatchStatus
    {
        public const string InProgress = "combat";
        public const string Victory = "victory";
        public const string Defeat = "defeat";
    }

    /// <summary>Monster archetypes (R-17). Stats live in config, not here.</summary>
    public static class MonsterType
    {
        public const string Shambler = "shambler";
        public const string Ravager = "ravager";
        public const string Spitter = "spitter";
        public const string Burrower = "burrower";
        public const string BullBehemoth = "bull_behemoth";
    }

    /// <summary>Placeable catalog keys (R-23).</summary>
    public static class PlaceableType
    {
        public const string Barricade = "barricade";
        public const string SpikeTrap = "spike_trap";
        public const string DynamiteTrap = "dynamite_trap";
        public const string Turret = "turret";
        public const string MedStation = "med_station";
    }

    /// <summary>Hero classes (R-31).</summary>
    public static class HeroClass
    {
        public const string Gunslinger = "gunslinger";
        public const string Rancher = "rancher";
        public const string Sawbones = "sawbones";
    }

    /// <summary>
    /// Sim time source. The sim never reads a wall clock — the host advances this, which is what
    /// makes the time-sensitive fixtures (G-017/G-018/G-019/G-021) reproducible.
    /// </summary>
    public interface IClock
    {
        double ElapsedSeconds { get; }
    }

    /// <summary>Host-driven clock. The Unity shell advances it from its fixed-step loop.</summary>
    public sealed class SimClock : IClock
    {
        public double ElapsedSeconds { get; private set; }

        public SimClock(double elapsedSeconds = 0.0)
        {
            ElapsedSeconds = elapsedSeconds;
        }

        public void Advance(double deltaSeconds)
        {
            if (deltaSeconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "sim time never runs backwards");
            }

            ElapsedSeconds += deltaSeconds;
        }
    }
}
