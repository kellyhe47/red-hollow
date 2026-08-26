using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Keeps a 2.5D sprite card facing the match camera. HeroView's Y-facing would otherwise
    /// turn an upright quad edge-on (and Unity quads are single-sided). Presentation only —
    /// no sim state.
    /// </summary>
    public sealed class SpriteBillboard : MonoBehaviour
    {
        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            // Stand upright, yaw toward the camera. Flattened Y so they occupy the 3D
            // cavern as figures, not camera-aligned HUD cards.
            var toEye = cam.transform.position - transform.position;
            toEye.y = 0f;
            if (toEye.sqrMagnitude < 1e-8f)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(toEye.normalized, Vector3.up);
        }
    }
}
