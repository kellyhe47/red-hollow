using System;

namespace RedHollow.Sim
{
    /// <summary>
    /// The production <see cref="IPathOracle"/> (R-16 / B-002 / G-004): a standing barricade
    /// whose footprint the mover's straight path crosses is the blocker.
    ///
    /// Until this existed, every factory-built match ran on <see cref="OpenPathOracle"/> —
    /// "nothing ever blocks" — so R-16's "the barricade becomes the target until destroyed" was
    /// live in the fixtures (which inject <see cref="DeclaredPathOracle"/>) and dead in the
    /// shipped game: a 100-scrip wall was scenery no monster would ever attack, because only the
    /// oracle can substitute a barricade for the target the monster picked.
    ///
    /// Geometry, not navigation: <see cref="MatchSim.TickMonsterMovement"/> walks a monster in a
    /// straight line at its target, so "does the straight segment cross a standing barricade's
    /// footprint" IS the honest production answer — a NavMesh would model paths the movement rule
    /// does not take. The interface doc's "NavMesh-backed" was written before movement shipped
    /// straight-line.
    ///
    /// Deterministic on purpose (R-51 — a host and a rebuilt world holding the same entities must
    /// answer alike): the blocker is the FIRST barricade along the path (smallest projection of
    /// its centre onto the segment), ties broken by ordinal id — the same tiebreak R-16's own
    /// targeting uses.
    ///
    /// Only barricades block. The other four placeables ship with no HP column
    /// (<see cref="MatchSim.ApplyPlaceableDamage"/> no-ops on them), so a turret returned as a
    /// blocker would park the wave chewing an indestructible box forever. Traps are meant to be
    /// walked over — that is what triggers them. The Burrower carve-out (DEC-007) needs nothing
    /// here: <see cref="MatchSim.SelectTarget"/> never consults the oracle for a monster that
    /// tunnels.
    /// </summary>
    public sealed class BarricadePathOracle : IPathOracle
    {
        private readonly MatchState _state;

        /// <summary>
        /// How near the segment a barricade's centre must pass to block it. Mirrors
        /// <see cref="MatchSim.PlaceableFootprintRadius"/> (the factory syncs it after building
        /// the sim), so the wall blocks exactly the ground the placement rules say it occupies.
        /// </summary>
        public double BlockingRadius { get; set; } = 1.5;

        public BarricadePathOracle(MatchState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            _state = state;
        }

        /// <summary>
        /// Id of the first standing barricade the straight walk from <paramref name="moverId"/>
        /// to <paramref name="targetId"/> would cross, or null when the lane is clear. The target
        /// itself is never its own blocker (a monster already sent at a wall keeps walking at it).
        /// </summary>
        public string BlockerBetween(string moverId, string targetId)
        {
            if (!TryResolveMover(moverId, out var from) || !TryResolveTarget(targetId, out var to))
            {
                return null;
            }

            string best = null;
            var bestAlong = double.MaxValue;

            foreach (var placeable in _state.Placeables.Values)
            {
                if (placeable == null || !placeable.Exists || !placeable.IsBarricade)
                {
                    continue;
                }

                // The wall the monster was SENT AT is what it walks to, not what blocks it.
                if (string.Equals(placeable.Id, targetId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!SegmentCrossesFootprint(from, to, placeable.Pos, BlockingRadius, out var along))
                {
                    continue;
                }

                var better = best == null
                    || along < bestAlong
                    || (along == bestAlong && string.CompareOrdinal(placeable.Id, best) < 0);

                if (better)
                {
                    best = placeable.Id;
                    bestAlong = along;
                }
            }

            return best;
        }

        /// <summary>Only monsters walk (R-17); an id that is not a live monster's has no path to ask about.</summary>
        private bool TryResolveMover(string moverId, out Vec2 pos)
        {
            pos = new Vec2(0.0, 0.0);
            if (moverId == null || !_state.Monsters.TryGetValue(moverId, out var monster) || monster == null)
            {
                return false;
            }

            pos = monster.Pos;
            return true;
        }

        /// <summary>The three things R-16 lets a monster walk at: heroes, hotspots, placeables.</summary>
        private bool TryResolveTarget(string targetId, out Vec2 pos)
        {
            pos = new Vec2(0.0, 0.0);
            if (targetId == null)
            {
                return false;
            }

            if (_state.Heroes.TryGetValue(targetId, out var hero) && hero != null)
            {
                pos = hero.Pos;
                return true;
            }

            if (_state.Hotspots.TryGetValue(targetId, out var hotspot) && hotspot != null)
            {
                pos = hotspot.Pos;
                return true;
            }

            if (_state.Placeables.TryGetValue(targetId, out var placeable) && placeable != null)
            {
                pos = placeable.Pos;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="centre"/> lies within <paramref name="radius"/> of the segment
        /// [<paramref name="from"/> → <paramref name="to"/>], and how far along the segment its
        /// closest approach sits (the "first wall on the walk" ordering key). Inclusive at the
        /// boundary, the convention G-019 set for every distance check in this sim.
        /// </summary>
        private static bool SegmentCrossesFootprint(
            Vec2 from, Vec2 to, Vec2 centre, double radius, out double along)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var lengthSquared = (dx * dx) + (dy * dy);

            if (lengthSquared <= 0.0)
            {
                // Mover standing on its target: no walk, nothing to cross.
                along = 0.0;
                return centre.DistanceTo(from) <= radius;
            }

            var t = (((centre.X - from.X) * dx) + ((centre.Y - from.Y) * dy)) / lengthSquared;
            if (t < 0.0)
            {
                t = 0.0;
            }
            else if (t > 1.0)
            {
                t = 1.0;
            }

            var closest = new Vec2(from.X + (dx * t), from.Y + (dy * t));
            along = t;

            return centre.DistanceTo(closest) <= radius;
        }
    }
}
