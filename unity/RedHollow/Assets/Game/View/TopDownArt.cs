using UnityEngine;
using UnityEngine.Rendering;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Presentation sizes and paint for a y-down camera. The cavern itself is 3D meshes
    /// (see <see cref="CavernEnvironment"/>); heroes and monsters are 2.5D cards standing
    /// in that space. Capsules collapse to a speck at ortho ~34 — footprints stay large
    /// enough to read, small enough that stacked colony blocks still dwarf them.
    /// </summary>
    internal static class TopDownArt
    {
        internal const float HeroFootprint = 3.4f;
        internal const float MonsterFootprint = 2.8f;
        internal const float HotspotFootprint = 1.8f;
        internal const float PlaceableFootprint = 2.2f;

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
        /// A 1×1 quad lying on XZ, facing +Y. Placeables and icons stay floor-decals;
        /// heroes/monsters use <see cref="StandingCard"/>.
        /// </summary>
        internal static GameObject QuadOnXz(string name, float footprint, Texture texture, Color tint)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            StripCollider(go);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            go.transform.localScale = new Vector3(footprint, footprint, 1f);
            go.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            Paint(go, tint, texture, 1f);
            return go;
        }

        /// <summary>
        /// 2.5D sprite card standing in the cavern: an upright quad that billboards toward
        /// the match camera so a 65° tilt still reads the art (not an edge, not a floor sticker).
        /// </summary>
        internal static GameObject StandingCard(string name, float footprint, Texture texture, Color tint)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            StripCollider(go);
            go.transform.localScale = new Vector3(footprint, footprint * 1.35f, 1f);
            go.transform.localPosition = new Vector3(0f, footprint * 0.55f, 0f);
            Paint(go, tint, texture, 1f);
            go.AddComponent<SpriteBillboard>();
            return go;
        }

        /// <summary>A squat 3D token — placeholder heroes/monsters/lamps, not sculpted characters.</summary>
        internal static GameObject BlockToken(string name, float width, float height, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            StripCollider(go);
            go.transform.localScale = new Vector3(width, height, width * 0.7f);
            go.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            Paint(go, color);
            return go;
        }

        internal static void StripCollider(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var collider = go.GetComponent<Collider>();
            if (collider == null)
            {
                return;
            }

            Object.DestroyImmediate(collider);
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

        internal static Shader UnlitShader()
        {
            return Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Unlit/Texture")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Hidden/InternalErrorShader");
        }

        internal static Shader LitShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? UnlitShader();
        }

        /// <summary>Matte URP Lit so lanterns and fog actually model the cavern.</summary>
        internal static Material LitMaterial(Color color, float smoothness)
        {
            var shader = LitShader();
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader);
            ApplyColor(material, color);
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }

            return material;
        }

        /// <summary>
        /// Wrap a Comfy tile as albedo on a Lit mesh material (DEC-026: tiles are UV maps,
        /// not a 2D tilemap). Safe no-op when the texture is missing.
        /// </summary>
        internal static void BindAlbedo(Material material, Texture texture, float tiles)
        {
            if (material == null || texture == null)
            {
                return;
            }

            ApplyTexture(material, texture, tiles);
        }

        /// <summary>Window / shaft glow — does not consume the URP additional-light budget.</summary>
        internal static Material EmissiveMaterial(Color color, float intensity)
        {
            var material = LitMaterial(color, 0.35f);
            if (material == null)
            {
                return null;
            }

            var emission = color * intensity;
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", emission);
            }

            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return material;
        }

        internal static void PaintLit(GameObject go, Material material)
        {
            if (go == null || material == null)
            {
                return;
            }

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
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
