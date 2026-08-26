using UnityEngine;
using UnityEngine.Rendering;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Presentation sizes and paint for the tilted Lykos camera. The cavern is 3D meshes
    /// (<see cref="CavernEnvironment"/>). Heroes and monsters are camera-facing upright
    /// cards (2.5D) with a blob shadow — never XZ-flat sprites (those go edge-on) and
    /// never 8-dir sprite cycles. A later 3D hero swaps this view mesh only.
    /// </summary>
    internal static class TopDownArt
    {
        internal const float HeroFootprint = 4.0f;
        internal const float MonsterFootprint = 3.2f;
        internal const float HotspotFootprint = 1.8f;
        internal const float PlaceableFootprint = 2.4f;

        private static Texture2D _rustPlate;

        internal static readonly Color Rust = new Color(0.55f, 0.28f, 0.14f);
        internal static readonly Color Amber = new Color(1.0f, 0.72f, 0.28f);
        internal static readonly Color HostileGreen = new Color(0.32f, 0.62f, 0.26f);
        internal static readonly Color CavernBrown = new Color(0.42f, 0.26f, 0.14f);
        internal static readonly Color Brass = new Color(0.50f, 0.38f, 0.18f);

        /// <summary>Warm multiply so unlit cards sit in lantern light, not studio-white.</summary>
        internal static readonly Color LanternTint = new Color(1.0f, 0.84f, 0.62f);

        internal static readonly Color BlobShadow = new Color(0.04f, 0.02f, 0.012f);

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
        /// v1 character visual: camera-facing upright billboard + blob shadow under the
        /// feet. The card yaws toward the match camera and stays world-up (not XZ-flat,
        /// not an 8-dir cycle). Later 3D heroes replace this object; they do not need a
        /// second sprite pipeline.
        /// </summary>
        internal static GameObject StandingCard(string name, float footprint, Texture texture, Color tint)
        {
            var root = new GameObject(name);

            var shadow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shadow.name = name + "_shadow";
            StripCollider(shadow);
            shadow.transform.SetParent(root.transform, false);
            // Unity cylinder: 1m diameter, 2m tall. Squash into a ground blob.
            shadow.transform.localScale = new Vector3(footprint * 0.72f, 0.035f, footprint * 0.48f);
            shadow.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            Paint(shadow, BlobShadow);

            var card = GameObject.CreatePrimitive(PrimitiveType.Quad);
            card.name = name + "_card";
            StripCollider(card);
            card.transform.SetParent(root.transform, false);
            card.transform.localScale = new Vector3(footprint, footprint * 1.35f, 1f);
            card.transform.localPosition = new Vector3(0f, footprint * 0.55f, 0f);
            Paint(card, tint * LanternTint, texture, 1f);
            card.AddComponent<SpriteBillboard>();

            return root;
        }

        /// <summary>
        /// Hotspot / spawn marker: an industrial lantern pylon, not a western signpost.
        /// Lost-state tinting hangs off this object's renderers (DEC-026).
        /// </summary>
        internal static GameObject LanternPylon(string name)
        {
            var root = new GameObject(name);

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pole.name = name + "_pole";
            StripCollider(pole);
            pole.transform.SetParent(root.transform, false);
            pole.transform.localScale = new Vector3(0.28f, 4.6f, 0.28f);
            pole.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            Paint(pole, new Color(0.20f, 0.12f, 0.08f));

            var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = name + "_arm";
            StripCollider(arm);
            arm.transform.SetParent(root.transform, false);
            arm.transform.localScale = new Vector3(1.4f, 0.16f, 0.16f);
            arm.transform.localPosition = new Vector3(0.55f, 4.4f, 0f);
            Paint(arm, Brass);

            var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lamp.name = name + "_lamp";
            StripCollider(lamp);
            lamp.transform.SetParent(root.transform, false);
            lamp.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
            lamp.transform.localPosition = new Vector3(1.15f, 4.2f, 0f);
            Paint(lamp, Amber);

            return root;
        }

        /// <summary>
        /// Riveted rust-plate albedo for habitat hulls when no Comfy metal tile is bound.
        /// Deterministic — no <see cref="Random"/>.
        /// </summary>
        internal static Texture2D RustPlate()
        {
            if (_rustPlate != null)
            {
                return _rustPlate;
            }

            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false)
            {
                name = "lykos-rust-plate",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color[n * n];
            for (var y = 0; y < n; y++)
            {
                for (var x = 0; x < n; x++)
                {
                    var n0 = Noise(x, y);
                    var n1 = Noise(x + 17, y * 3);
                    var seam = (x % 8 == 0 || y % 8 == 0) ? -0.10f : 0f;
                    var panel = (((x / 8) + (y / 8)) & 1) == 0 ? 0.05f : 0f;
                    var rust = 0.30f + n0 * 0.22f + n1 * 0.10f + seam + panel;
                    pixels[y * n + x] = new Color(
                        Mathf.Clamp01(rust + 0.10f),
                        Mathf.Clamp01(rust * 0.52f),
                        Mathf.Clamp01(rust * 0.28f),
                        1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            _rustPlate = tex;
            return tex;
        }

        internal static float Noise(int x, int z)
        {
            unchecked
            {
                var n = (uint)(x * 374761393 + z * 668265263);
                n = (n ^ (n >> 13)) * 1274126177u;
                return (n & 0xFFFF) / 65535f;
            }
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
