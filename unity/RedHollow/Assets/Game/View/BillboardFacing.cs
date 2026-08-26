using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Yaw-only camera facing for civilian huddle quads (not heroes/monsters — those
    /// are world-facing volumes). Presentation only.
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
