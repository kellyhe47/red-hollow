using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// v1 2.5D character card: stay upright (world up) and yaw to face the match camera.
    /// XZ-flat sprites go edge-on under the 65° tilt. HeroView's aim rotation would also
    /// turn a parented quad sideways. Presentation only — no sim state, no 8-dir cycles.
    /// A later 3D hero swaps the view mesh; this component comes off with the card.
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
