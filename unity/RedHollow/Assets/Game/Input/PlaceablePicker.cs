using System;
using RedHollow.Sim;

namespace RedHollow.Game.Input
{
    /// <summary>
    /// Ticket 024 (T-24) — hit-testing a pointer's ground point to a standing placeable, so a
    /// planning click can become <c>ShellControls.ClickPlaceable(id)</c> (the R-22 sell path the
    /// T-23 seam pinned). Pure over <see cref="MatchState"/>: no colliders, no physics — the sim's
    /// own positions are the truth a top-down click resolves against.
    /// </summary>
    public static class PlaceablePicker
    {
        /// <summary>
        /// The id of the nearest placeable that still stands (<see cref="Placeable.Exists"/>)
        /// within <paramref name="pickRadius"/> of <paramref name="groundPoint"/> (inclusive at the
        /// radius, matching the sim's own edge-inclusive auras), or null when nothing standing is
        /// that close. A sold or destroyed placeable is never picked — its tile is ground again.
        /// </summary>
        public static string Pick(MatchState state, Vec2 groundPoint, double pickRadius)
        {
            throw new NotImplementedException("T-24: placeable picking not implemented yet.");
        }
    }
}
