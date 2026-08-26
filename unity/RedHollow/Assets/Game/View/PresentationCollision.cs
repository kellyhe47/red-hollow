using System;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Presentation-only capsule query against colony geometry. HostLoop clips a
    /// <see cref="RedHollow.Sim.HeroMoveRequest"/> to the allowed distance — the view
    /// never writes sim state (T-10). EditMode with no hab colliders is a no-op.
    /// </summary>
    public static class PresentationCollision
    {
        public const float HeroRadius = 0.72f;
        public const float HeroHeight = 2.6f;
        public const float Skin = 0.08f;

        /// <summary>
        /// Matches <c>HeroMovementConfig.DefaultMoveSpeed</c> so a clipped delta
        /// covers exactly the CapsuleCast remainder (all shipped classes use it).
        /// </summary>
        private const double HeroPace = 4.0;

        /// <summary>
        /// How many seconds of <paramref name="deltaSeconds"/> remain after the first
        /// environment hit along <paramref name="direction"/>. Zero means the body is
        /// already against a wall in that direction.
        /// </summary>
        public static double ClipMoveSeconds(Vec2 from, Vec2 direction, double deltaSeconds)
        {
            if (!(deltaSeconds > 0.0))
            {
                return 0.0;
            }

            var length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
            if (!(length > 0.0) || double.IsInfinity(length))
            {
                return 0.0;
            }

            // Sit the probe on the deck so hab wall slabs are hit and walk plates are not.
            var world = SimSpace.ToWorld(from);
            var lift = CavernBlockout.DeckSurface + HeroRadius + 0.02f;
            var p1 = world + (Vector3.up * lift);
            var p2 = p1 + (Vector3.up * Mathf.Max(0.4f, HeroHeight - (HeroRadius * 2f)));
            var dir = new Vector3((float)(direction.X / length), 0f, (float)(direction.Y / length));
            var probe = (float)(HeroPace * deltaSeconds) + Skin;
            if (probe < 0.05f)
            {
                probe = 0.05f;
            }

            RaycastHit hit;
            if (!Physics.CapsuleCast(
                    p1, p2, HeroRadius, dir, out hit, probe,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return deltaSeconds;
            }

            var allowed = hit.distance - Skin;
            if (allowed <= 0f)
            {
                return 0.0;
            }

            var seconds = allowed / HeroPace;
            return seconds < deltaSeconds ? seconds : deltaSeconds;
        }

        public static void EnsureHeroMotor(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var cc = go.GetComponent<CharacterController>();
            if (cc == null)
            {
                cc = go.AddComponent<CharacterController>();
            }

            cc.radius = HeroRadius;
            cc.height = HeroHeight;
            cc.center = new Vector3(0f, CavernBlockout.DeckSurface + (HeroHeight * 0.5f), 0f);
            cc.slopeLimit = 55f;
            cc.stepOffset = 0.25f;
            cc.skinWidth = Skin;
            cc.minMoveDistance = 0f;
            // Ignore Raycast so HostLoop's CapsuleCast hits habs, not the hero's own volume.
            go.layer = 2;
        }
    }
}
