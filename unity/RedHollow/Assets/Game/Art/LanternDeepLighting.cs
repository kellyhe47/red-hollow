using RedHollow.Game.View;
using UnityEngine;
using UnityEngine.Rendering;

namespace RedHollow.Game.Art
{
    /// <summary>
    /// Ticket 013 (T-13) — R-15 / DEC-025. "Lantern Deep" is carried by SCENE LIGHTING, not by the
    /// textures (docs/comfy-prompts/00-shared-style.md §"Where the style lives in-engine"):
    ///
    ///  * dark warm ambient — near-black umber, never daylight;
    ///  * fog for the volumetric dust haze;
    ///  * all light artificial and SOURCED — amber point lights (lanterns, string lights, windows);
    ///  * zero natural light — no skybox, no sun, no directional light standing in for one;
    ///  * the cavern dome mesh IS the sky (<see cref="MatchScene.CavernDome"/>).
    ///
    /// Applied over a built <see cref="MatchScene"/> rather than baked into a .unity file so the
    /// look is reviewable in a diff and reproducible headlessly, same as the scene itself. The
    /// tests pin bounds (dark, warm, fog on, no skybox/sun, a dome, a warm point light); the exact
    /// painterly numbers inside those bounds are playtest's to tune, not the tests'.
    /// </summary>
    public static class LanternDeepLighting
    {
        /// <summary>Near-black umber ambient: dark, warm, and a color — never void-black.</summary>
        private static readonly Color AmbientUmber = new Color(0.22f, 0.14f, 0.07f);

        /// <summary>Dust under lamplight — warm haze, never a blue night mist.</summary>
        private static readonly Color FogDust = new Color(0.16f, 0.10f, 0.06f);

        /// <summary>Lantern amber.</summary>
        private static readonly Color LanternAmber = new Color(1.0f, 0.62f, 0.28f);

        /// <summary>How far above the colony floor the central lantern cluster hangs.</summary>
        private const float LanternHeight = 6f;

        /// <summary>
        /// Vertical size of the dome. The match camera sits at y=60; a 30-unit squash put the
        /// camera OUTSIDE looking through the shell. 140 puts the camera inside the arch so
        /// Play can call Apply without occluding the colony. T13 only pins max.y above ground
        /// and XZ span — height is free.
        /// </summary>
        private const float DomeHeight = 140f;

        /// <summary>Breathing room past the rendered content, so no camera angle sees the rim.</summary>
        private const float DomeMargin = 8f;

        /// <summary>
        /// Impose the Lantern Deep look on a built scene: RenderSettings (ambient, fog, no skybox,
        /// no sun), replace any daylight-style directional light with sourced amber point lights,
        /// and raise the cavern dome (assigned to <see cref="MatchScene.CavernDome"/>).
        /// </summary>
        public static void Apply(MatchScene scene)
        {
            ApplyRenderSettings();

            if (scene == null || scene.Root == null)
            {
                return;
            }

            RetireDirectionalLights(scene.Root);

            // Sized before the dome exists, so the dome spans the colony and not itself.
            var span = RenderedSpan(scene.Root);

            RaiseLanterns(scene.Root.transform, span);

            if (scene.CavernDome == null)
            {
                scene.CavernDome = RaiseDome(scene.Root.transform, span);
            }
        }

        /// <summary>R-15's global half: dark warm flat ambient, warm fog, and zero natural light.</summary>
        private static void ApplyRenderSettings()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientUmber;

            RenderSettings.fog = true;
            RenderSettings.fogColor = FogDust;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.018f;

            RenderSettings.skybox = null;
            RenderSettings.sun = null;
        }

        /// <summary>
        /// A directional light is a sun by another name, whatever its GameObject is called — the
        /// pre-013 placeholder KeyLight was exactly that. The builder no longer raises one, but
        /// Apply retires any the loaded scene still carries — including the default scene's
        /// "Directional Light", which <c>RenderSettings.sun</c> auto-picks as the sun while any
        /// enabled directional light exists, even after being assigned null.
        /// </summary>
        private static void RetireDirectionalLights(GameObject root)
        {
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional)
                {
                    light.enabled = false;
                }
            }

            // Belt over braces: whatever the auto-pick still answers with gets retired too.
            for (var guard = 0; guard < 8 && RenderSettings.sun != null; guard++)
            {
                RenderSettings.sun.enabled = false;
                RenderSettings.sun = null;
            }
        }

        /// <summary>
        /// The sourced light: one amber point light hung over the middle of the colony, ranged to
        /// reach its edges. How many lanterns, where they stand and how bright — playtest's; one
        /// that actually lights the play area is the floor this ticket ships.
        /// </summary>
        private static void RaiseLanterns(Transform root, Bounds span)
        {
            var go = new GameObject("Lantern_Central");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(
                span.center.x, SimSpace.GroundHeight + LanternHeight, span.center.z);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LanternAmber;
            light.intensity = 2.4f;
            light.range = Mathf.Max(span.extents.x, span.extents.z) + LanternHeight + DomeMargin;
            light.shadows = LightShadows.None;
        }

        /// <summary>
        /// R-15: the dome IS the sky. Real rendered geometry — a sphere squashed into an arch —
        /// spanning everything the scene renders, so no camera angle sees past the world's edge
        /// into the void a skybox would have papered over.
        /// </summary>
        private static GameObject RaiseDome(Transform root, Bounds span)
        {
            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "CavernDome";
            dome.transform.SetParent(root, false);
            dome.transform.position = new Vector3(span.center.x, SimSpace.GroundHeight, span.center.z);
            dome.transform.localScale = new Vector3(
                span.size.x + DomeMargin * 2f,
                DomeHeight,
                span.size.z + DomeMargin * 2f);

            var rock = TopDownArt.LitMaterial(new Color(0.12f, 0.07f, 0.04f), 0.05f);
            if (rock != null)
            {
                TopDownArt.BindAlbedo(
                    rock, Resources.Load<Texture2D>("RedHollowArt/cavern-ground"), 8f);
                TopDownArt.PaintLit(dome, rock);
                foreach (var renderer in dome.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }

            var collider = dome.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            return dome;
        }

        /// <summary>
        /// The world box the scene's rendered content occupies — derived from what was actually
        /// built rather than re-reading the map, so the dome covers whatever layout the builder
        /// composed, ground and markers included.
        /// </summary>
        private static Bounds RenderedSpan(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, new Vector3(100f, 0f, 100f));
            }

            var span = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                span.Encapsulate(renderers[i].bounds);
            }

            return span;
        }
    }
}
