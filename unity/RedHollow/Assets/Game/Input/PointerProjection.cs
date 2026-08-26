using System;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Game.Input
{
    /// <summary>
    /// Ticket 024 (T-24) — the pure half of the play-mode pointer adapter: a screen point through a
    /// camera onto the sim's ground plane (<see cref="RedHollow.Game.View.SimSpace"/>'s
    /// y = <c>GroundHeight</c> plane), answered as a sim-space <see cref="Vec2"/>.
    ///
    /// Extracted from the plane-projection math <c>LegacyDeviceInputSource.CursorOnGround</c>
    /// already documents for combat aim, so the aim path and the planning pointer resolve a cursor
    /// identically. Pure — a scripted camera is the whole test rig; no device is read here.
    /// </summary>
    public static class PointerProjection
    {
        /// <summary>
        /// Resolve <paramref name="screenPoint"/> through <paramref name="camera"/> onto the ground
        /// plane. False — never a throw — when there is no ground point to give: a null camera, a
        /// ray parallel to the floor (the cursor is on the horizon), or a ground point behind the
        /// camera. <paramref name="groundPos"/> is meaningful only on true.
        /// </summary>
        public static bool TryScreenToGround(Camera camera, Vector2 screenPoint, out Vec2 groundPos)
        {
            throw new NotImplementedException("T-24: pointer-to-ground projection not implemented yet.");
        }
    }
}
