using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// The one conversion between the sim's engine-free <see cref="Vec2"/> ground space and Unity
    /// world space (R-51: the sim carries its own vector type; the shell converts at the boundary).
    ///
    /// Centralised so every view, marker and camera in the shell agrees on where a sim coordinate
    /// is. The PRD picks no axis convention, so this ticket pins only the two properties a top-down
    /// game cannot be correct without: the map lands on one horizontal plane, and the conversion is
    /// an isometry — the colony is not stretched, mirrored into a different shape, or collapsed.
    ///
    /// The convention chosen here — sim x to world x, sim y to world z, world y left at
    /// <see cref="GroundHeight"/> — is the identity on the horizontal plane, so it is an isometry by
    /// construction rather than by a scale factor somebody has to keep true. Unity's Y is the
    /// vertical axis the top-down camera looks down (R-30), and it is the axis every presentation
    /// offset (a capsule standing half its height up) is free to use without moving anything the sim
    /// meant.
    /// </summary>
    public static class SimSpace
    {
        /// <summary>
        /// The world height of the colony floor. One plane for the whole map: the sim is 2D (R-51),
        /// so there is no second elevation for a coordinate to land on.
        /// </summary>
        public const float GroundHeight = 0f;

        /// <summary>Sim ground point to the world point it stands on.</summary>
        public static Vector3 ToWorld(Vec2 groundPoint)
        {
            return new Vector3((float)groundPoint.X, GroundHeight, (float)groundPoint.Y);
        }

        /// <summary>
        /// World point back to the sim's ground space, dropping the vertical axis. The inverse of
        /// <see cref="ToWorld"/>, and the path a cursor ray-cast onto the ground takes to become an
        /// <see cref="RedHollow.Game.Input.InputSnapshot.CursorGroundPoint"/> (R-30).
        /// </summary>
        public static Vec2 ToGround(Vector3 worldPoint)
        {
            return new Vec2(worldPoint.x, worldPoint.z);
        }

        /// <summary>
        /// A ground-space *direction* in world space. Separate from <see cref="ToWorld(Vec2)"/>
        /// because a direction must not pick up the floor's height: offsetting a point is placement,
        /// offsetting a direction is a tilt.
        /// </summary>
        public static Vector3 DirectionToWorld(Vector2 groundDirection)
        {
            return new Vector3(groundDirection.x, 0f, groundDirection.y);
        }

        /// <summary>The flat ground vector for a world point, in the input layer's spelling.</summary>
        public static Vector2 ToGroundVector(Vector3 worldPoint)
        {
            return new Vector2(worldPoint.x, worldPoint.z);
        }
    }
}
