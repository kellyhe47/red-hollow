using System;

namespace RedHollow.Sim
{
    /// <summary>
    /// Production <see cref="IPathOracle"/> for a match that has no NavMesh: the first standing
    /// barricade whose footprint the mover would walk through on the way to its target (R-16 /
    /// B-002). Goldens and editor scenarios keep <see cref="DeclaredPathOracle"/>;
    /// <see cref="OpenPathOracle"/> stays the MatchSim default so tests that never declare a
    /// blocker still see an open field.
    ///
    /// Geometry, not physics. The sim is not allowed to ask UnityEngine (R-51); this answers the
    /// same "is something in the way?" question from live <see cref="MatchState"/> positions. The
    /// block radius is the placeable footprint (R-24 / trap occupancy) so a wall occupies the
    /// ground it was placed on and nothing wider.
    ///
    /// B-003 lives here as well as in <see cref="MatchSim.SelectTarget"/>: a Burrower is invisible
    /// to barricades, so the oracle must not name one even if the caller forgot the carve-out.
    /// </summary>
    public sealed class BarricadePathOracle : IPathOracle
    {
        /// <summary>
        /// Shipped <see cref="MatchSim.PlaceableFootprintRadius"/>. Kept as a default rather than
        /// read off a sim instance so the oracle can be built before MatchSim is (the factory
        /// constructs state, then the oracle, then the sim).
        /// </summary>
        public const double DefaultBlockRadius = 1.5;

        private readonly MatchState _state;

        public BarricadePathOracle(MatchState state, double blockRadius = DefaultBlockRadius)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            BlockRadius = blockRadius;
        }

        /// <summary>Half-width of a barricade across a walk. Strict-less-than, matching R-24.</summary>
        public double BlockRadius { get; }

        public string BlockerBetween(string moverId, string targetId)
        {
            if (string.IsNullOrEmpty(moverId) || string.IsNullOrEmpty(targetId) || _state == null)
            {
                return null;
            }

            if (!TryPosition(moverId, out var from) || !TryPosition(targetId, out var to))
            {
                return null;
            }

            // B-003: a Burrower tunnels. Named here so a caller that forgot the carve-out still
            // cannot pin a wall on one.
            if (_state.Monsters.TryGetValue(moverId, out var mover) && mover.IgnoresBarricadesAndHeroes)
            {
                return null;
            }

            string bestId = null;
            var bestT = double.MaxValue;

            foreach (var placeable in _state.Placeables.Values)
            {
                if (placeable == null
                    || !placeable.Exists
                    || !placeable.IsBarricade
                    || placeable.Id == targetId)
                {
                    continue;
                }

                var distance = DistanceToSegment(placeable.Pos, from, to, out var t);
                if (!(distance < BlockRadius))
                {
                    continue;
                }

                // "First" is nearest along the walk, then lowest ordinal id — the same tie the
                // targeting rule uses, so two walls on one lane pick the same blocker on every host.
                var better = bestId == null
                    || t < bestT
                    || (t == bestT && string.CompareOrdinal(placeable.Id, bestId) < 0);
                if (better)
                {
                    bestId = placeable.Id;
                    bestT = t;
                }
            }

            return bestId;
        }

        private bool TryPosition(string id, out Vec2 pos)
        {
            if (_state.Monsters.TryGetValue(id, out var monster))
            {
                pos = monster.Pos;
                return true;
            }

            if (_state.Heroes.TryGetValue(id, out var hero))
            {
                pos = hero.Pos;
                return true;
            }

            if (_state.Hotspots.TryGetValue(id, out var hotspot))
            {
                pos = hotspot.Pos;
                return true;
            }

            if (_state.Placeables.TryGetValue(id, out var placeable))
            {
                pos = placeable.Pos;
                return true;
            }

            pos = default;
            return false;
        }

        /// <summary>
        /// Distance from <paramref name="point"/> to the segment <paramref name="a"/>→<paramref name="b"/>,
        /// with <paramref name="t"/> the clamped 0..1 parameter along that walk.
        /// </summary>
        private static double DistanceToSegment(Vec2 point, Vec2 a, Vec2 b, out double t)
        {
            var abx = b.X - a.X;
            var aby = b.Y - a.Y;
            var lengthSq = (abx * abx) + (aby * aby);
            if (lengthSq <= 0.0)
            {
                t = 0.0;
                return point.DistanceTo(a);
            }

            t = (((point.X - a.X) * abx) + ((point.Y - a.Y) * aby)) / lengthSq;
            if (t < 0.0)
            {
                t = 0.0;
            }
            else if (t > 1.0)
            {
                t = 1.0;
            }

            var closest = new Vec2(a.X + (t * abx), a.Y + (t * aby));
            return point.DistanceTo(closest);
        }
    }
}
