using RedHollow.Sim;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RedHollow.Game.View
{
    /// <summary>
    /// One-shot presentation traces for a basic that connected (or was fired).
    /// Play-mode only — EditMode combat tests must not leak primitives.
    /// A shot must READ as a shot: muzzle at the barrel, a thin tracer, sparks at impact.
    /// Not a fat bar and not a giant orange sphere.
    /// </summary>
    public static class CombatVfx
    {
        const float TracerWidth = 0.04f;
        const float TracerLife = 0.09f;
        const float MuzzleLife = 0.08f;
        const float ImpactLife = 0.12f;

        public static void PulseShot(Vec2 from, Vec2 to)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var origin = SimSpace.ToWorld(from);
            var aim = SimSpace.ToWorld(to);
            var muzzle = MuzzleWorld(origin);
            var impact = aim;
            impact.y = muzzle.y;

            var delta = impact - muzzle;
            var len = delta.magnitude;
            if (len < 0.05f)
            {
                SpawnMuzzle(muzzle);
                return;
            }

            SpawnMuzzle(muzzle);
            SpawnTracer(muzzle, impact, len);
            PulseHit(impact);
        }

        public static void PulseHit(Vector3 world)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var seed = world.GetHashCode();
            for (var i = 0; i < 7; i++)
            {
                seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                var ox = ((seed >> 8) % 200 - 100) * 0.0018f;
                var oy = ((seed >> 16) % 200 - 100) * 0.0018f;
                var oz = ((seed >> 4) % 200 - 100) * 0.0018f;
                var spark = GameObject.CreatePrimitive(PrimitiveType.Cube);
                spark.name = "fx_hit";
                ViewLook.StripCollider(spark);
                spark.transform.position = world + new Vector3(ox, oy, oz);
                spark.transform.localScale = Vector3.one * 0.055f;
                spark.transform.rotation = Quaternion.Euler(oy * 400f, ox * 400f, oz * 400f);
                var hot = i < 3
                    ? new Color(1f, 0.96f, 0.72f)
                    : new Color(1f, 0.62f, 0.22f);
                ViewLook.Paint(spark, ViewLook.Unlit(hot));
                Object.Destroy(spark, ImpactLife);
            }
        }

        static void SpawnMuzzle(Vector3 world)
        {
            var flare = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flare.name = "fx_muzzle";
            ViewLook.StripCollider(flare);
            flare.transform.position = world;
            flare.transform.localScale = Vector3.one * 0.16f;
            ViewLook.Paint(flare, ViewLook.Unlit(new Color(1f, 0.92f, 0.55f)));

            var lamp = new GameObject("fx_muzzle_light");
            lamp.transform.SetParent(flare.transform, false);
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.78f, 0.40f);
            light.lightUnit = LightUnit.Candela;
            light.intensity = 18f;
            light.range = 1.6f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.GetUniversalAdditionalLightData();

            Object.Destroy(flare, MuzzleLife);
        }

        static void SpawnTracer(Vector3 from, Vector3 to, float len)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "fx_shot";
            ViewLook.StripCollider(go);
            var delta = to - from;
            go.transform.position = (from + to) * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(delta / len, Vector3.up);
            go.transform.localScale = new Vector3(TracerWidth, TracerWidth, len);
            ViewLook.Paint(go, ViewLook.Unlit(new Color(1f, 0.97f, 0.82f)));
            Object.Destroy(go, TracerLife);
        }

        static Vector3 MuzzleWorld(Vector3 near)
        {
            Transform barrel = null;
            var best = 36f * 36f;
            var views = Object.FindObjectsByType<HeroView>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var i = 0; i < views.Length; i++)
            {
                var view = views[i];
                if (view == null)
                {
                    continue;
                }

                var found = FindChildNamed(view.transform, "rifle_barrel");
                if (found == null)
                {
                    continue;
                }

                var d = (found.position - near).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    barrel = found;
                }
            }

            if (barrel == null)
            {
                return near + (Vector3.up * 1.15f);
            }

            // Cylinder axis is local Y; after the 90° pitch that is along the barrel.
            // Cylinder primitive is 2 units tall: tip is one local-Y scale past center.
            return barrel.position + (barrel.up * barrel.lossyScale.y);
        }

        static Transform FindChildNamed(Transform root, string name)
        {
            if (root.name == name)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindChildNamed(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
