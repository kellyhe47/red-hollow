namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 002 (T-02) owns this half of <see cref="MatchSim"/>: monster target selection.
    /// Requirements R-16, R-17, R-18; graded by fixtures G-001 through G-005.
    ///
    /// The shared core — fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>R-16 / B-001..B-003. Pick what this monster should be attacking.</summary>
        public TargetSelectionResult SelectTarget(string monsterId)
        {
            BeginCommand();

            if (monsterId == null || !State.Monsters.TryGetValue(monsterId, out var monster))
            {
                // A monster the match does not hold is a caller bug, not a game state. Answering
                // with an empty selection keeps one stale id from aborting a whole wave tick, and
                // an empty selection can never be mistaken for a real one: it names no target.
                return Finish(new TargetSelectionResult { MonsterId = monsterId });
            }

            // B-003 outranks B-001 and B-002 (PRD precedence): a Burrower tunnels, so heroes and
            // barricades are invisible to it and the general algorithm never runs. What the
            // carve-out does NOT suspend is R-12 — an emptied hotspot is nobody's target, so a
            // Burrower walks past one to the nearest hotspot that still holds civilians.
            bool tunnels = monster.IgnoresBarricadesAndHeroes;

            NearestAvailableTarget(monster, includeHeroes: !tunnels, targetId: out var targetId, distance: out var distance);

            if (!tunnels && targetId != null)
            {
                // B-002. Blocking is *declared* per (mover, target) pair by the injected oracle —
                // the sim owns no geometry (R-51), so it asks about the path to the target it just
                // chose rather than looking for barricades sitting near a line. A barricade in the
                // way of some other candidate is not in this monster's way.
                var blockerId = _pathOracle.BlockerBetween(monsterId, targetId);
                if (blockerId != null
                    && State.Placeables.TryGetValue(blockerId, out var blocker)
                    && blocker.Exists)
                {
                    // "Until destroyed": the barricade becomes the target, at its own distance —
                    // that is what the monster now walks to and chews on, not the shelter behind it.
                    targetId = blockerId;
                    distance = monster.Pos.DistanceTo(blocker.Pos);
                }
            }

            RecordChange(monsterId, "target_id", monster.TargetId, targetId);
            monster.TargetId = targetId;

            return Finish(new TargetSelectionResult
            {
                MonsterId = monsterId,
                TargetId = targetId,
                Distance = distance,
            });
        }

        /// <summary>
        /// B-001. The nearest available target by straight-line distance: living heroes (unless the
        /// caller tunnels past them) and hotspots that still hold civilians (R-12). Answers with a
        /// null <paramref name="targetId"/> when nothing is available — in a real match R-11's
        /// defeat rule has already fired by then, so this is a defined non-answer, not an error.
        /// </summary>
        private void NearestAvailableTarget(
            Monster monster, bool includeHeroes, out string targetId, out double distance)
        {
            targetId = null;
            distance = 0.0;

            if (includeHeroes)
            {
                foreach (var hero in State.Heroes.Values)
                {
                    if (!hero.Alive)
                    {
                        continue;
                    }

                    Consider(monster.Pos, hero.Id, hero.Pos, ref targetId, ref distance);
                }
            }

            foreach (var hotspot in State.Hotspots.Values)
            {
                if (!hotspot.IsValidTarget)
                {
                    continue;
                }

                Consider(monster.Pos, hotspot.Id, hotspot.Pos, ref targetId, ref distance);
            }
        }

        /// <summary>
        /// Folds one candidate into the running best. Ties break to the lowest entity id, compared
        /// ordinally: multiplayer needs every host and client to agree on the same monster's target,
        /// so the answer must not depend on dictionary iteration order or on a culture's collation.
        /// </summary>
        private static void Consider(
            Vec2 from, string candidateId, Vec2 candidatePos, ref string bestId, ref double bestDistance)
        {
            double candidateDistance = from.DistanceTo(candidatePos);

            bool better = bestId == null
                || candidateDistance < bestDistance
                || (candidateDistance == bestDistance && string.CompareOrdinal(candidateId, bestId) < 0);

            if (better)
            {
                bestId = candidateId;
                bestDistance = candidateDistance;
            }
        }
    }
}
