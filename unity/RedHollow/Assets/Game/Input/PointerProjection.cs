using RedHollow.Game.View;
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
            groundPos = default(Vec2);

            if (camera == null)
            {
                // An unwired camera fails soft — the combat aim path's own tolerance.
                return false;
            }

            var ray = camera.ScreenPointToRay(new Vector3(screenPoint.x, screenPoint.y, 0f));

            // Parallel to the floor: the cursor is on the horizon and has no ground point at all.
            if (Mathf.Approximately(ray.direction.y, 0f))
            {
                return false;
            }

            // The signed ray distance to the ground plane. Negative means the intersection is
            // BEHIND the camera — not a place a cursor can point at, so no extrapolation.
            var distance = (SimSpace.GroundHeight - ray.origin.y) / ray.direction.y;
            if (distance < 0f)
            {
                return false;
            }

            groundPos = SimSpace.ToGround(ray.GetPoint(distance));
            return true;
        }
    }
}
