using System;
using System.Collections.Generic;
using RedHollow.Sim;

namespace RedHollow.Game.Input
{
    /// <summary>
    /// Ticket 025 (T-25) — the aim-line geometry behind the SPACE basic attack and the line-shaped
    /// abilities (R-26/R-30/R-36): given an attacker, the cursor's ground point and the live
    /// <see cref="MatchState"/>, answer "who is on the line?" as the ordered nearest-first
    /// <see cref="LineEntity"/> list <see cref="HeroAttackRequest.EntitiesOnLine"/> and
    /// <see cref="HeroAbilityRequest.EntitiesOnLine"/> carry.
    ///
    /// Pure over sim state on purpose — "physics decides who is on it; the sim decides who is
    /// hit" (Commands.cs) — so the report is HONEST: every entity the segment crosses is listed
    /// with its fixture kind (hero / hotspot / monster / barricade), friendlies included, and the
    /// SIM's allowlist is what keeps a friendly unhurt (R-34), never a shell-side omission.
    /// The only entities the shell may omit are the ones that are not in the world any more:
    /// the attacker itself, dead monsters, broken placeables — the sim's own
    /// <c>FirstMonsterOnLine</c> does not re-check <c>Alive</c>, so offering a corpse would let a
    /// basic shoot the dead.
    /// </summary>
    public static class AimLine
    {
        /// <summary>The kind spellings the fixture loader (and the sim's allowlist) understand.</summary>
        private const string KindHero = "hero";

        private const string KindHotspot = "hotspot";

        private const string KindMonster = "monster";

        private const string KindBarricade = "barricade";

        /// <summary>
        /// Every live entity inside the line from <paramref name="origin"/> toward
        /// <paramref name="aimPoint"/>, nearest-first. <paramref name="length"/> caps the reach and
        /// <paramref name="width"/> is the full corridor width (shell policy, config-shaped —
        /// see <see cref="RedHollow.Game.UI.CombatActionConfig"/>). An aim with no direction
        /// (<paramref name="aimPoint"/> == <paramref name="origin"/>) is an empty line, never an
        /// error — a cursor parked on the hero must not NaN a frame.
        /// </summary>
        public static List<LineEntity> EntitiesAlong(
            MatchState state,
            string attackerId,
            Vec2 origin,
            Vec2 aimPoint,
            double length,
            double width)
        {
            var found = new List<Candidate>();
            if (state == null)
            {
                return Materialize(found);
            }

            var dx = aimPoint.X - origin.X;
            var dy = aimPoint.Y - origin.Y;
            var magnitude = Math.Sqrt((dx * dx) + (dy * dy));
            if (double.IsNaN(magnitude) || double.IsInfinity(magnitude) || magnitude <= 0.0)
            {
                // A cursor parked on the hero gives the line no direction: an ordinary frame,
                // answered with "nothing on it" rather than a throw or a NaN corridor.
                return Materialize(found);
            }

            var ux = dx / magnitude;
            var uy = dy / magnitude;
            var halfWidth = width / 2.0;

            // The report is honest: heroes and hotspots ride along whatever their state (the sim's
            // allowlist ignores them); the only omissions are entities no longer in the world —
            // the attacker itself, dead monsters, broken placeables.
            foreach (var hero in state.Heroes.Values)
            {
                if (hero != null)
                {
                    Consider(found, attackerId, origin, ux, uy, length, halfWidth,
                        hero.Id, KindHero, hero.Pos);
                }
            }

            foreach (var hotspot in state.Hotspots.Values)
            {
                if (hotspot != null)
                {
                    Consider(found, attackerId, origin, ux, uy, length, halfWidth,
                        hotspot.Id, KindHotspot, hotspot.Pos);
                }
            }

            foreach (var monster in state.Monsters.Values)
            {
                // Dead monsters have left the world: the sim resolves a line entry by id WITHOUT
                // re-checking Alive, so an offered corpse would soak basics forever (T-25's pin).
                if (monster != null && monster.Alive)
                {
                    Consider(found, attackerId, origin, ux, uy, length, halfWidth,
                        monster.Id, KindMonster, monster.Pos);
                }
            }

            foreach (var placeable in state.Placeables.Values)
            {
                // Broken placeables (sold, or spent traps) are ground again — same rule as corpses.
                if (placeable != null && placeable.Exists)
                {
                    Consider(found, attackerId, origin, ux, uy, length, halfWidth,
                        placeable.Id, KindBarricade, placeable.Pos);
                }
            }

            found.Sort((a, b) => a.Along.CompareTo(b.Along));
            return Materialize(found);
        }

        /// <summary>
        /// One candidate against the corridor: forward of the origin, within reach, within half the
        /// width of the axis. Boundary in/exclusivity is deliberately unpinned; strict exclusion at
        /// zero keeps "behind" honest and the pinned data sits clear of every edge.
        /// </summary>
        private static void Consider(
            List<Candidate> found,
            string attackerId,
            Vec2 origin,
            double ux,
            double uy,
            double length,
            double halfWidth,
            string id,
            string kind,
            Vec2 pos)
        {
            // The attacker stands at distance zero — geometrically always "on" its own line, never
            // reported: the honesty rule covers the world in front of the muzzle, not the muzzle.
            if (string.Equals(id, attackerId, StringComparison.Ordinal))
            {
                return;
            }

            var relX = pos.X - origin.X;
            var relY = pos.Y - origin.Y;
            var along = (relX * ux) + (relY * uy);
            if (along <= 0.0 || along > length)
            {
                return;
            }

            var lateral = Math.Abs((relX * -uy) + (relY * ux));
            if (lateral > halfWidth)
            {
                return;
            }

            found.Add(new Candidate { Id = id, Kind = kind, Pos = pos, Along = along });
        }

        private static List<LineEntity> Materialize(List<Candidate> found)
        {
            var result = new List<LineEntity>(found.Count);
            for (var i = 0; i < found.Count; i++)
            {
                result.Add(new LineEntity
                {
                    Id = found[i].Id,
                    Kind = found[i].Kind,
                    Pos = found[i].Pos,
                });
            }

            return result;
        }

        private struct Candidate
        {
            public string Id;
            public string Kind;
            public Vec2 Pos;
            public double Along;
        }
    }
}
