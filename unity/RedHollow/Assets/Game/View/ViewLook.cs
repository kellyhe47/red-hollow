using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Shared unlit materials and primitive hygiene for the runtime colony blockout.
    /// Presentation only — no sim types.
    /// </summary>
    public static class ViewLook
    {
        public static Material Unlit(Color color, Texture texture = null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Texture");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader);
            Tint(material, color);
            // URP Unlit samples _BaseMap; a color-only material with a null map
            // draws black, which is why placeholder billboards vanished on the floor.
            BindTexture(material, texture != null ? texture : Texture2D.whiteTexture);
            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", 0f);
            }

            return material;
        }

        /// <summary>
        /// Unlit with alpha clip so authored PNG silhouettes (hab facades) punch out the
        /// transparent backdrop instead of drawing a black rectangle.
        /// </summary>
        public static Material UnlitCutout(Color color, Texture texture)
        {
            var material = Unlit(color, texture);
            if (material == null)
            {
                return null;
            }

            if (texture != null)
            {
                texture.wrapMode = TextureWrapMode.Clamp;
            }

            if (material.HasProperty("_Cutoff"))
            {
                material.SetFloat("_Cutoff", 0.35f);
            }

            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 1f);
            }

            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = 2450;
            return material;
        }

        public static void Tint(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        public static void BindTexture(Material material, Texture texture)
        {
            if (material == null || texture == null)
            {
                return;
            }

            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }
        }

        public static void SetTiling(Material material, Vector2 scale)
        {
            if (material == null)
            {
                return;
            }

            material.mainTextureScale = scale;
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTextureScale("_BaseMap", scale);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTextureScale("_MainTex", scale);
            }
        }

        public static void Paint(GameObject go, Material material)
        {
            if (go == null || material == null)
            {
                return;
            }

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// Per-renderer tint via property block so a shared unlit material is not mutated
        /// (lost-state darkening must not turn every hab in the colony dark).
        /// </summary>
        public static void TintBlock(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }

        public static void StripCollider(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }


        /// <summary>
        /// Canon standing cards are opaque RGB paintings (dusty haze behind the figure).
        /// Knock the connected backdrop out to alpha so a 2.5D billboard is a person, not a
        /// rectangle, then trim the sprite rect to the remaining opaque pixels.
        /// </summary>
        public static Sprite CreateStandingSprite(Texture2D source)
        {
            if (source == null)
            {
                return null;
            }

            Color[] pixels;
            try
            {
                pixels = source.GetPixels();
            }
            catch (Exception)
            {
                return Sprite.Create(
                    source,
                    new Rect(0f, 0f, source.width, source.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
            }

            var width = source.width;
            var height = source.height;
            var marked = new bool[pixels.Length];
            var seeds = new Color[pixels.Length];
            var queue = new Queue<int>();

            const float thresh = 48f / 255f;
            const float lumFloor = 40f / 255f;
            const float neighbor = 28f / 255f;

            void TrySeed(int x, int y)
            {
                var i = (y * width) + x;
                if (marked[i])
                {
                    return;
                }

                marked[i] = true;
                seeds[i] = pixels[i];
                queue.Enqueue(i);
            }

            for (var x = 0; x < width; x++)
            {
                TrySeed(x, 0);
                TrySeed(x, height - 1);
            }

            for (var y = 0; y < height; y++)
            {
                TrySeed(0, y);
                TrySeed(width - 1, y);
            }

            var dx = new[] { 1, -1, 0, 0 };
            var dy = new[] { 0, 0, 1, -1 };
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                var seed = seeds[i];
                var p = pixels[i];
                var luma = (0.3f * p.r) + (0.59f * p.g) + (0.11f * p.b);
                if (luma < lumFloor)
                {
                    marked[i] = false;
                    continue;
                }

                var x = i % width;
                var y = i / width;
                for (var n = 0; n < 4; n++)
                {
                    var nx = x + dx[n];
                    var ny = y + dy[n];
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    {
                        continue;
                    }

                    var ni = (ny * width) + nx;
                    if (marked[ni])
                    {
                        continue;
                    }

                    var np = pixels[ni];
                    var nluma = (0.3f * np.r) + (0.59f * np.g) + (0.11f * np.b);
                    if (nluma < lumFloor)
                    {
                        continue;
                    }

                    var toSeed = Mathf.Abs(np.r - seed.r) + Mathf.Abs(np.g - seed.g) + Mathf.Abs(np.b - seed.b);
                    var toParent = Mathf.Abs(np.r - p.r) + Mathf.Abs(np.g - p.g) + Mathf.Abs(np.b - p.b);
                    if (toSeed <= thresh || toParent <= neighbor)
                    {
                        marked[ni] = true;
                        seeds[ni] = toSeed <= thresh ? seed : np;
                        queue.Enqueue(ni);
                    }
                }
            }

            var minX = width;
            var minY = height;
            var maxX = 0;
            var maxY = 0;
            var any = false;
            for (var i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                if (marked[i])
                {
                    c.a = 0f;
                    pixels[i] = c;
                    continue;
                }

                c.a = 1f;
                c.r = Mathf.Min(1f, (c.r * 1.18f) + 0.04f);
                c.g = Mathf.Min(1f, (c.g * 1.12f) + 0.03f);
                c.b = Mathf.Min(1f, (c.b * 1.08f) + 0.02f);
                pixels[i] = c;
                any = true;
                var x = i % width;
                var y = i / width;
                if (x < minX)
                {
                    minX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels(pixels);
            tex.Apply(false, false);
            tex.name = source.name + "_standing";

            Rect rect;
            if (!any)
            {
                rect = new Rect(0f, 0f, width, height);
            }
            else
            {
                const int pad = 4;
                minX = Mathf.Max(0, minX - pad);
                minY = Mathf.Max(0, minY - pad);
                maxX = Mathf.Min(width - 1, maxX + pad);
                maxY = Mathf.Min(height - 1, maxY + pad);
                rect = new Rect(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
            }

            return Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f);
        }

        public static Texture2D LoadTexture(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
            {
                return null;
            }

            return Resources.Load<Texture2D>(resourcePath);
        }
    }
}
