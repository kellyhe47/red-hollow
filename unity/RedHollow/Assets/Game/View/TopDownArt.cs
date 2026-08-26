using UnityEngine;
using UnityEngine.Rendering;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Presentation sizes and unlit paint for a y-down camera. Capsules and default cubes
    /// collapse to a one-unit speck at ortho ~34; these footprints are the readable stand-ins
    /// (and the world size catalog quads are scaled to).
    /// </summary>
    internal static class TopDownArt
    {
        internal const float HeroFootprint = 6f;
        internal const float MonsterFootprint = 5f;
        internal const float HotspotFootprint = 12f;
        internal const float PlaceableFootprint = 3f;

        /// <summary>How many world units one cavern-ground tile should cover when tiled.</summary>
        internal const float GroundTileWorldSize = 12f;

        internal static readonly Color Rust = new Color(0.55f, 0.28f, 0.14f);
        internal static readonly Color Amber = new Color(1.0f, 0.72f, 0.28f);
        internal static readonly Color HostileGreen = new Color(0.32f, 0.62f, 0.26f);
        internal static readonly Color CavernBrown = new Color(0.42f, 0.26f, 0.14f);
        internal static readonly Color Brass = new Color(0.50f, 0.38f, 0.18f);

        internal static Color ColorFor(VisualClass visualClass)
        {
            switch (visualClass)
            {
                case VisualClass.Ground:
                    return Rust;
                case VisualClass.Hero:
                    return Amber;
                case VisualClass.Monster:
                    return HostileGreen;
                case VisualClass.Hotspot:
                    return CavernBrown;
                default:
                    return Brass;
            }
        }

        internal static float FootprintFor(VisualClass visualClass)
        {
            switch (visualClass)
            {
                case VisualClass.Hero:
                    return HeroFootprint;
                case VisualClass.Monster:
                    return MonsterFootprint;
                case VisualClass.Hotspot:
                    return HotspotFootprint;
                case VisualClass.Placeable:
                    return PlaceableFootprint;
                default:
                    return HeroFootprint;
            }
        }

        /// <summary>
        /// A 1×1 quad lying on XZ, facing +Y, so a y-down camera sees the texture (not an edge).
        /// </summary>
        internal static GameObject QuadOnXz(string name, float footprint, Texture texture, Color tint)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            go.transform.localScale = new Vector3(footprint, footprint, 1f);
            go.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            Paint(go, tint, texture, 1f);
            return go;
        }

        /// <summary>Squash a Unity cylinder into a disc of <paramref name="diameter"/> on XZ.</summary>
        internal static void FlattenCylinder(GameObject cylinder, float diameter, float height)
        {
            // Default cylinder: radius 0.5? Unity cylinder is 1m radius, 2m tall.
            // scale.x/z = diameter, scale.y = height/2.
            cylinder.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
            cylinder.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        }

        internal static void Paint(GameObject go, Color color, Texture texture = null, float tile = 1f)
        {
            if (go == null)
            {
                return;
            }

            var shader = UnlitShader();
            if (shader == null)
            {
                return;
            }

            var material = new Material(shader);
            ApplyColor(material, color);
            if (texture != null)
            {
                ApplyTexture(material, texture, tile);
            }

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        internal static void TileAlbedo(Renderer renderer, float worldSpan)
        {
            if (renderer == null || renderer.sharedMaterial == null)
            {
                return;
            }

            var tiles = Mathf.Max(2f, worldSpan / GroundTileWorldSize);
            ApplyTexture(renderer.sharedMaterial, renderer.sharedMaterial.mainTexture, tiles);
        }

        internal static Shader UnlitShader()
        {
            return Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Unlit/Texture")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Hidden/InternalErrorShader");
        }

        private static void ApplyColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            material.color = color;
        }

        private static void ApplyTexture(Material material, Texture texture, float tiles)
        {
            if (texture == null)
            {
                return;
            }

            var scale = new Vector2(tiles, tiles);
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", scale);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
                material.SetTextureScale("_MainTex", scale);
            }

            material.mainTexture = texture;
            material.mainTextureScale = scale;
        }
    }
}
