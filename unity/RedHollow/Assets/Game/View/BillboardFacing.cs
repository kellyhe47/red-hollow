using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Keeps a standing quad/sprite facing the match camera so a 2.5D unit never reads as a
    /// paper edge. Yaw only — the card stays upright on the deck so an isometric
    /// camera can still see it (full LookRotation at 60-70° down laid quads flat).
    /// Presentation only: reads <see cref="Camera.main"/> and writes this transform.
    /// </summary>
    public sealed class BillboardFacing : MonoBehaviour
    {
        private void OnEnable()
        {
            Face();
        }

        private void LateUpdate()
        {
            Face();
        }

        private void Face()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            var toCamera = camera.transform.position - transform.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude < 1e-8f)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
        }
    }
}
