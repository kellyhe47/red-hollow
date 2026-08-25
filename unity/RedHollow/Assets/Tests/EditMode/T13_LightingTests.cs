using System.Linq;
using NUnit.Framework;
using RedHollow.Game.Art;
using RedHollow.Game.View;
using RedHollow.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 013 (T-13) — R-15 / DEC-025 "Lantern Deep". The style doc is explicit that the look
    /// is carried by SCENE LIGHTING, not by the textures (docs/comfy-prompts/00-shared-style.md
    /// §"Where the style lives in-engine"), and these tests pin every invariant of that list a
    /// machine can check:
    ///
    ///  * "Dark warm ambient (near-black umber)" → flat ambient, low value, warm hue, not void-black.
    ///  * "fog/volumetrics for the dust haze"     → fog on, fog color not a cool blue mist.
    ///  * "zero natural light — no sun, sky"      → no skybox material, no sun, no directional light.
    ///  * "all light artificial and sourced"      → at least one enabled warm POINT light.
    ///  * "the dome IS the sky"                   → dome geometry over the colony, in place of a skybox.
    ///
    /// Every color assertion is a BOUND, never an exact number: the PRD pins the language ("dark
    /// warm near-black umber", "amber"), and the exact values inside those bounds are the painterly
    /// judgment the ticket's scope hands to playtest. A test that demanded #1A0F08 would ship one
    /// agent's taste as spec.
    ///
    /// Contract shape: <see cref="LanternDeepLighting.Apply"/> imposes the look on a built
    /// <see cref="MatchScene"/>. The implementer may also fold the call into
    /// <see cref="MatchSceneBuilder.Build"/> itself — nothing in T16 pins the lighting — but
    /// Apply-over-a-built-scene must work, because it is what these tests and the headless builder
    /// call. RenderSettings are global editor state, so every test here saves and restores them.
    /// </summary>
    [TestFixture]
    public class T13_LightingTests
    {
        /// <summary>"Near-black": no ambient channel may reach a quarter of full daylight.</summary>
        private const float NearBlackCeiling = 0.25f;

        private MatchScene _scene;

        private AmbientMode _savedAmbientMode;
        private Color _savedAmbientLight;
        private bool _savedFog;
        private Color _savedFogColor;
        private Material _savedSkybox;
        private Light _savedSun;

        [SetUp]
        public void SaveRenderSettingsAndBuildTheLitScene()
        {
            _savedAmbientMode = RenderSettings.ambientMode;
            _savedAmbientLight = RenderSettings.ambientLight;
            _savedFog = RenderSettings.fog;
            _savedFogColor = RenderSettings.fogColor;
            _savedSkybox = RenderSettings.skybox;
            _savedSun = RenderSettings.sun;

            _scene = MatchSceneBuilder.Build(ColonyMap.V1(), new PlaceholderVisualResolver());
            LanternDeepLighting.Apply(_scene);
        }

        [TearDown]
        public void RestoreRenderSettingsAndTearDownTheScene()
        {
            RenderSettings.ambientMode = _savedAmbientMode;
            RenderSettings.ambientLight = _savedAmbientLight;
            RenderSettings.fog = _savedFog;
            RenderSettings.fogColor = _savedFogColor;
            RenderSettings.skybox = _savedSkybox;
            RenderSettings.sun = _savedSun;

            if (_scene != null && _scene.Root != null)
            {
                Object.DestroyImmediate(_scene.Root);
            }

            _scene = null;
        }

        /// <summary>
        /// R-15: "dark warm ambient (near-black umber)". Flat mode is part of the pin — skybox and
        /// trilight ambient reintroduce a sky's contribution, and Lantern Deep has no sky. The
        /// color is bounded: darker than a quarter of daylight, warmer than it is cool (red never
        /// below green, green never below blue, red strictly above blue), and not the void — umber
        /// is a color, pure black is an unlit render bug wearing a style's name.
        /// </summary>
        [Test]
        public void The_ambient_is_a_dark_warm_near_black_umber()
        {
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat),
                "R-15: ambient must be a flat color — skybox/gradient ambient is sky light, and "
                + "the cavern has no sky");

            var ambient = RenderSettings.ambientLight;
            var brightest = Mathf.Max(ambient.r, Mathf.Max(ambient.g, ambient.b));

            Assert.That(brightest, Is.LessThanOrEqualTo(NearBlackCeiling),
                "R-15: 'near-black' — no ambient channel may reach " + NearBlackCeiling
                + "; got " + ambient);
            Assert.That(brightest, Is.GreaterThan(0f),
                "R-15: umber is a color; a pure-black ambient is 'the lights are off', not a style");

            Assert.That(ambient.r, Is.GreaterThanOrEqualTo(ambient.g),
                "R-15: warm — red never below green; got " + ambient);
            Assert.That(ambient.g, Is.GreaterThanOrEqualTo(ambient.b),
                "R-15: warm — green never below blue; got " + ambient);
            Assert.That(ambient.r, Is.GreaterThan(ambient.b),
                "R-15: warm means warm — a grey ambient (r == b) is neither umber nor amber; got "
                + ambient);
        }

        /// <summary>
        /// R-15: "fog/volumetrics for the dust haze". Fog on, and its color at least as warm as it
        /// is cool — a blue fog is a night-exterior mist, not cavern dust under lamplight. Density
        /// and mode are free: the doc names haze, not a falloff curve.
        /// </summary>
        [Test]
        public void Fog_is_on_and_reads_as_dust_not_blue_mist()
        {
            Assert.That(RenderSettings.fog, Is.True,
                "R-15: the dust haze is fog — a clear atmosphere is a different game's look");
            Assert.That(RenderSettings.fogColor.r, Is.GreaterThanOrEqualTo(RenderSettings.fogColor.b),
                "R-15: dust under lamplight is warm; a blue fog is moonlight, which Lantern Deep "
                + "does not have; got " + RenderSettings.fogColor);
        }

        /// <summary>
        /// R-15: "zero natural light — no sun, sky, or horizon". Three sources of daylight, all
        /// closed: no skybox material, no RenderSettings.sun, and no enabled directional light in
        /// the scene — a directional light IS a sun, whatever its GameObject is called. (The
        /// pre-013 builder's placeholder KeyLight was exactly that; this is the test that retires
        /// it.)
        /// </summary>
        [Test]
        public void No_skybox_no_sun_no_directional_light()
        {
            Assert.That(RenderSettings.skybox, Is.Null,
                "R-15: no skybox — the cavern dome mesh is the sky");
            Assert.That(RenderSettings.sun, Is.Null,
                "R-15: zero natural light — nothing may be designated the sun");

            var directionals = _scene.Root.GetComponentsInChildren<Light>(true)
                .Where(l => l.enabled && l.type == LightType.Directional)
                .Select(l => l.gameObject.name)
                .ToList();

            Assert.That(directionals, Is.Empty,
                "R-15: a directional light is a sun by another name; all light is sourced "
                + "(points at lanterns/windows), found directional light(s): "
                + string.Join(", ", directionals));
        }

        /// <summary>
        /// R-15: "all light artificial and sourced — amber point lights". At least one enabled
        /// point light with actual output and an amber cast (red strictly above blue). How many
        /// lanterns, where they stand and how bright — playtest's.
        /// </summary>
        [Test]
        public void At_least_one_warm_sourced_point_light_lights_the_colony()
        {
            var warmPoints = _scene.Root.GetComponentsInChildren<Light>(true)
                .Where(l => l.enabled
                    && l.type == LightType.Point
                    && l.intensity > 0f
                    && l.color.r > l.color.b)
                .ToList();

            Assert.That(warmPoints, Is.Not.Empty,
                "R-15: the colony is lit by sourced amber point lights (lanterns, string lights, "
                + "windows) — none found under the scene root");
        }

        /// <summary>
        /// R-15: "cavern dome mesh as the sky". The dome is real geometry in the scene — it
        /// renders, it sits over the colony (its bounds top out above the ground plane), and it
        /// spans the play area so no camera angle sees past the world's edge into nothing. Its
        /// mesh, texture and radius are free.
        /// </summary>
        [Test]
        public void A_cavern_dome_arches_over_the_play_area_instead_of_a_skybox()
        {
            Assert.That(_scene.CavernDome, Is.Not.Null,
                "R-15: the dome IS the sky — the lit scene must raise one");
            Assert.That(_scene.CavernDome.transform.IsChildOf(_scene.Root.transform), Is.True,
                "the dome is part of the scene and torn down with it");

            var renderer = _scene.CavernDome.GetComponentInChildren<Renderer>();
            Assert.That(renderer, Is.Not.Null,
                "R-15: the dome is rendered geometry, not a marker — an invisible sky is a skybox "
                + "with extra steps");

            var bounds = renderer.bounds;
            Assert.That(bounds.max.y, Is.GreaterThan(SimSpace.GroundHeight),
                "the dome arches OVER the colony — its top must clear the ground plane");

            var play = PlayAreaBounds(ColonyMap.V1());
            Assert.That(bounds.min.x, Is.LessThanOrEqualTo(play.min.x),
                "the dome must span the play area on -x; a dome smaller than the colony shows "
                + "the void past its rim");
            Assert.That(bounds.max.x, Is.GreaterThanOrEqualTo(play.max.x), "dome spans +x");
            Assert.That(bounds.min.z, Is.LessThanOrEqualTo(play.min.z), "dome spans -z");
            Assert.That(bounds.max.z, Is.GreaterThanOrEqualTo(play.max.z), "dome spans +z");
        }

        /// <summary>Same derivation as T16: the world box the colony occupies, from the map.</summary>
        private static Bounds PlayAreaBounds(ColonyMap map)
        {
            var bounds = new Bounds(SimSpace.ToWorld(map.TeamSpawn), Vector3.zero);

            foreach (var spec in map.Hotspots)
            {
                bounds.Encapsulate(SimSpace.ToWorld(spec.Pos));
            }

            foreach (var tunnel in map.EntryTunnels)
            {
                bounds.Encapsulate(SimSpace.ToWorld(tunnel));
            }

            return bounds;
        }
    }
}
