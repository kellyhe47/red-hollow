using System;
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
    /// </summary>
    public static class SimSpace
    {
        public static Vector3 ToWorld(Vec2 groundPoint)
        {
            throw new NotImplementedException("ticket 016 — sim/world conversion");
        }

        public static Vec2 ToGround(Vector3 worldPoint)
        {
            throw new NotImplementedException("ticket 016 — sim/world conversion");
        }
    }
}
