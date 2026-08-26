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
            throw new NotImplementedException("T-25: aim-line geometry not implemented yet.");
        }
    }
}
