using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// One-shot presentation traces for a basic that connected (or was fired).
    /// Play-mode only — EditMode combat tests must not leak primitives.
    /// </summary>
    public static class CombatVfx
    {
        public static void PulseShot(Vec2 from, Vec2 to)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var a = SimSpace.ToWorld(from) + (Vector3.up * 1.55f);
            var b = SimSpace.ToWorld(to) + (Vector3.up * 1.55f);
            var delta = b - a;
            var len = delta.magnitude;
            if (len < 0.05f)
            {
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "fx_shot";
            ViewLook.StripCollider(go);
            go.transform.position = (a + b) * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(delta / len, Vector3.up);
            go.transform.localScale = new Vector3(0.14f, 0.14f, len);
            ViewLook.Paint(go, ViewLook.Unlit(new Color(1f, 0.82f, 0.32f)));
            Object.Destroy(go, 0.14f);
        }

        public static void PulseHit(Vector3 world)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "fx_hit";
            ViewLook.StripCollider(go);
            go.transform.position = world + (Vector3.up * 1.9f);
            go.transform.localScale = Vector3.one * 1.15f;
            ViewLook.Paint(go, ViewLook.Unlit(new Color(1f, 0.28f, 0.10f)));
            Object.Destroy(go, 0.20f);
        }
    }
}
