using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Game.Art;
using RedHollow.Game.View;
using UnityEditor;
using UnityEngine;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 013 (T-13), the asset seam made real — owns the R-15 delivery half of the acceptance
    /// criteria: the placeholder build stays shippable, no code path blocks on an asset existing,
    /// and generated art drops in as a PURE ASSET SWAP.
    ///
    /// Two layers are pinned here:
    ///
    ///  1. <b>The chained resolver.</b> <see cref="ArtVisualResolver"/> sits in front of the total
    ///     fallback from ticket 016. A key the <see cref="ArtCatalog"/> knows resolves to real art
    ///     with <c>IsPlaceholder</c> false; unknown/null/empty keys delegate to the fallback and
    ///     come back as its placeholder handle. Never null, never a throw, for any input — the
    ///     seam's totality survives the layer that actually looks for assets.
    ///
    ///     The artKey→asset mapping is pinned as <b>data, not code</b>, behaviorally: registering a
    ///     key at runtime flips it from placeholder to real through an unchanged resolver, and the
    ///     catalog exposes its mapping for inspection. (An IL scan for "no per-key branching" was
    ///     considered and rejected: it would outlaw correct table implementations. The
    ///     register-flips-resolution test is the property that matters — adding art requires no new
    ///     code path.)
    ///
    ///  2. <b>One representative imported asset per class</b>, at real import settings — texture
    ///     tile, character, icon, UI frame — loaded through <see cref="AssetDatabase"/> from
    ///     <c>Assets/Game/Art/</c>. These fail naturally until the implementer copies the chosen
    ///     files from <c>art/</c> (exact sources named in each test) and the importer settings
    ///     hold: tiles wrap, UI keeps its alpha and its non-power-of-two pixel size.
    ///
    /// What is deliberately NOT pinned: which prefab/quad/sprite shape "real art" instantiates as
    /// (any GameObject is fine), placeholder shapes (016's call), and anything about how the art
    /// LOOKS — set-consistency and painterly quality are the pipeline docs' checks and playtest's.
    /// </summary>
    [TestFixture]
    public class T13_ArtSeamTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void DestroyEverythingThisTestBuilt()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
        }

        // ==========================================================================================
        //  AC — known artKey resolves to real art in front of the fallback
        // ==========================================================================================

        /// <summary>
        /// The seam's positive half, finally: a registered key resolves to the authored art, the
        /// handle says it is NOT a placeholder, and the fallback is never consulted — real art is
        /// an answer, not a decorated placeholder.
        /// </summary>
        [Test]
        public void A_registered_art_key_resolves_to_real_art_and_says_so()
        {
            var real = Track(new GameObject("real_gunslinger_art"));
            var catalog = new ArtCatalog();
            catalog.Register("characters/gunslinger", () => real);

            var fallback = new SpyResolver();
            var resolver = new ArtVisualResolver(catalog, fallback);

            var handle = resolver.Resolve(VisualClass.Hero, "characters/gunslinger");

            Assert.That(handle, Is.Not.Null, "the seam is total in both directions");
            Track(handle.Instance);

            Assert.That(handle.IsPlaceholder, Is.False,
                "a known key is the real art, and the handle must say so — IsPlaceholder exists "
                + "precisely so 'did the art resolve?' is an observable answer");
            Assert.That(handle.Instance, Is.Not.Null, "real art must be something in the scene");
            Assert.That(handle.Class, Is.EqualTo(VisualClass.Hero),
                "the handle stands for the class that was asked for");
            Assert.That(handle.ArtKey, Is.EqualTo("characters/gunslinger"),
                "the handle records which art answered");
            Assert.That(fallback.Calls, Is.Empty,
                "a key the catalog knows must not consult the fallback — real art is not a "
                + "fallback with a costume on");
        }

        /// <summary>
        /// The delegation half: an unknown key is the FALLBACK's answer — the very handle the
        /// fallback produced, not a re-wrapped imitation of it. Same for null and empty, which are
        /// absence wearing different coats.
        /// </summary>
        [TestCase("characters/never-generated_v1", TestName = "an unknown key delegates to the fallback")]
        [TestCase(null, TestName = "a null key delegates to the fallback")]
        [TestCase("", TestName = "an empty key delegates to the fallback")]
        public void An_unknown_key_delegates_to_the_fallback_and_returns_its_placeholder(string absentKey)
        {
            var fallback = new SpyResolver();
            var resolver = new ArtVisualResolver(new ArtCatalog(), fallback);

            var handle = resolver.Resolve(VisualClass.Monster, absentKey);
            Track(handle == null ? null : handle.Instance);

            Assert.That(fallback.Calls, Has.Count.EqualTo(1),
                "absent art has exactly one answer: ask the fallback");
            Assert.That(fallback.Calls[0].Class, Is.EqualTo(VisualClass.Monster),
                "the fallback must be asked for the same class the caller wanted");
            Assert.That(handle, Is.SameAs(fallback.LastHandle),
                "the fallback's handle IS the answer — wrapping it would let the two layers "
                + "disagree about IsPlaceholder");
            Assert.That(handle.IsPlaceholder, Is.True,
                "absent art resolves to the stand-in, and the handle says so");
        }

        /// <summary>
        /// The acceptance criterion itself, stated as a test: for garbage of every shape, across
        /// every visual class, the chained resolver NEVER throws and NEVER answers null — with the
        /// real <see cref="PlaceholderVisualResolver"/> underneath, exactly as shipped. No code
        /// path blocks on an asset existing.
        /// </summary>
        [Test]
        public void The_chained_resolver_is_total_for_any_input_whatsoever()
        {
            var resolver = new ArtVisualResolver(new ArtCatalog(), new PlaceholderVisualResolver());

            var hostileKeys = new[]
            {
                null,
                "",
                "   ",
                "art/characters/not-generated-yet_v1.png",
                "../../../etc/passwd",
                new string('k', 4096),
                "characters/gunslinger\0",
            };

            foreach (VisualClass visualClass in System.Enum.GetValues(typeof(VisualClass)))
            {
                foreach (var key in hostileKeys)
                {
                    VisualHandle handle = null;
                    Assert.That(() => { handle = resolver.Resolve(visualClass, key); }, Throws.Nothing,
                        "the seam never throws: " + visualClass + " / " + Describe(key));

                    Assert.That(handle, Is.Not.Null,
                        "the seam never answers null: " + visualClass + " / " + Describe(key));
                    Assert.That(handle.Instance, Is.Not.Null,
                        "a resolved visual exists in the scene: " + visualClass + " / " + Describe(key));

                    Track(handle.Instance);
                }
            }
        }

        /// <summary>
        /// "Generated art drops in as a pure asset swap", mechanically: the SAME resolver answers
        /// placeholder before the entry exists and real art after one Register call — no new
        /// resolver, no new code path, one more row in a table. This is the test that fails if the
        /// mapping is ever a switch statement someone has to extend.
        /// </summary>
        [Test]
        public void Adding_art_is_a_data_change_through_an_unchanged_resolver()
        {
            var catalog = new ArtCatalog();
            var resolver = new ArtVisualResolver(catalog, new PlaceholderVisualResolver());

            var before = resolver.Resolve(VisualClass.Placeable, "props/water-tower");
            Track(before.Instance);
            Assert.That(before.IsPlaceholder, Is.True,
                "before the art lands, the key is honestly a placeholder");

            var real = Track(new GameObject("real_water_tower"));
            catalog.Register("props/water-tower", () => real);

            var after = resolver.Resolve(VisualClass.Placeable, "props/water-tower");
            Track(after.Instance);

            Assert.That(after.IsPlaceholder, Is.False,
                "one registered table entry — zero code changes — flips the key to real art; "
                + "that is what 'pure asset swap' means");
        }

        /// <summary>The mapping is inspectable data: registered keys are enumerable and queryable.</summary>
        [Test]
        public void The_catalog_exposes_its_mapping_as_data()
        {
            var catalog = new ArtCatalog();
            catalog.Register("textures/cavern-ground", () => Track(new GameObject("t")));
            catalog.Register("ui/hp-bar-frame", () => Track(new GameObject("u")));

            Assert.That(catalog.Keys, Is.EquivalentTo(new[] { "textures/cavern-ground", "ui/hp-bar-frame" }),
                "the artKey→asset mapping is a table someone can read, not branching someone must find");
            Assert.That(catalog.Contains("textures/cavern-ground"), Is.True);
            Assert.That(catalog.Contains("textures/never-registered"), Is.False);

            GameObject ignored;
            Assert.That(catalog.TryInstantiate("textures/never-registered", out ignored), Is.False,
                "an unregistered key is absence, not an error");
            Assert.That(() => catalog.TryInstantiate(null, out ignored), Throws.Nothing,
                "a null key is absence, not an error");
        }

        // ==========================================================================================
        //  AC — feel/art code stays plain C#; nothing forces a MonoBehaviour to hold sim references
        // ==========================================================================================

        /// <summary>
        /// The T10 Cecil invariant scans every MonoBehaviour in the shell assembly for sim-state
        /// writes; it keeps this ticket honest automatically. This test pins the complementary
        /// structural choice: the new seam and feel logic are PLAIN C# — none of it is a
        /// MonoBehaviour, so none of it can even appear in that scan's blast radius, and no test in
        /// this ticket requires a component to hold a sim reference.
        /// </summary>
        [Test]
        public void The_art_and_feel_layer_is_plain_CSharp_not_MonoBehaviours()
        {
            var plainTypes = new[]
            {
                typeof(ArtCatalog),
                typeof(ArtVisualResolver),
                typeof(FeelRouter),
                typeof(FeelCue),
                typeof(EntityFeelState),
            };

            foreach (var type in plainTypes)
            {
                Assert.That(typeof(MonoBehaviour).IsAssignableFrom(type), Is.False,
                    type.Name + " must be plain C# — feel/art logic in a MonoBehaviour is exactly "
                    + "where sim references start to accumulate (R-51 / T10 invariant)");
            }

            Assert.That(typeof(LanternDeepLighting).IsAbstract && typeof(LanternDeepLighting).IsSealed,
                Is.True, "LanternDeepLighting is a static utility, same shape as ViewRig");
        }

        // ==========================================================================================
        //  AC — one representative imported asset per class, at real import settings
        // ==========================================================================================

        // The implementer copies EXACTLY these files (source → imported path):
        //   art/textures/cavern-ground_v1_1024.png   → Assets/Game/Art/Textures/cavern-ground_v1_1024.png
        //   art/characters/gunslinger-portrait_v1_512.png
        //                                            → Assets/Game/Art/Characters/gunslinger-portrait_v1_512.png
        //   art/icons/gs-revolver-shot_v1_256.png    → Assets/Game/Art/Icons/gs-revolver-shot_v1_256.png
        //   art/ui/hp-bar-frame_v1_320x32.png        → Assets/Game/Art/UI/hp-bar-frame_v1_320x32.png
        // All four are committed keepers (see art/asset-log.csv); copying is a file operation, never
        // a pipeline re-run (pinned seed makes reruns identical — CLAUDE.md §4).

        private const string TilePath = "Assets/Game/Art/Textures/cavern-ground_v1_1024.png";
        private const string CharacterPath = "Assets/Game/Art/Characters/gunslinger-portrait_v1_512.png";
        private const string IconPath = "Assets/Game/Art/Icons/gs-revolver-shot_v1_256.png";
        private const string UiPath = "Assets/Game/Art/UI/hp-bar-frame_v1_320x32.png";

        /// <summary>
        /// The environment tile imports at its full authored resolution and WRAPS — a ground tile
        /// that clamps shows smeared edges at every seam, which is the failure the seamcheck files
        /// in <c>art/textures/</c> exist to catch upstream.
        /// </summary>
        [Test]
        public void The_representative_tile_imports_at_1024_and_wraps()
        {
            var texture = LoadedTexture(TilePath, "art/textures/cavern-ground_v1_1024.png");

            Assert.That(texture.width, Is.EqualTo(1024), "the tile imports at its authored width");
            Assert.That(texture.height, Is.EqualTo(1024), "the tile imports at its authored height");

            var importer = ImporterFor(TilePath);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat),
                "a ground tile must wrap: clamped edges smear at every tiling seam");
        }

        /// <summary>The character asset imports at its authored 512 detail.</summary>
        [Test]
        public void The_representative_character_imports_at_512()
        {
            var texture = LoadedTexture(CharacterPath, "art/characters/gunslinger-portrait_v1_512.png");

            Assert.That(texture.width, Is.EqualTo(512), "the portrait imports at its authored width");
            Assert.That(texture.height, Is.EqualTo(512), "the portrait imports at its authored height");
        }

        /// <summary>The icon imports at the 256 in-game size (the set ships 1024/256/128 per icon).</summary>
        [Test]
        public void The_representative_icon_imports_at_256()
        {
            var texture = LoadedTexture(IconPath, "art/icons/gs-revolver-shot_v1_256.png");

            Assert.That(texture.width, Is.EqualTo(256), "the icon imports at its authored width");
            Assert.That(texture.height, Is.EqualTo(256), "the icon imports at its authored height");
        }

        /// <summary>
        /// The UI frame is the demanding one: 320x32 is not a power of two, so a default NPOT
        /// rescale would silently stretch it — the exact-size assertions are what catch that — and
        /// its transparency is the whole point of a frame, so the alpha channel must survive import.
        /// </summary>
        [Test]
        public void The_representative_ui_frame_keeps_its_exact_size_and_its_alpha()
        {
            var texture = LoadedTexture(UiPath, "art/ui/hp-bar-frame_v1_320x32.png");

            Assert.That(texture.width, Is.EqualTo(320),
                "320 is not a power of two — an importer left to rescale NPOT textures stretches "
                + "every UI frame; the exact width pins that setting");
            Assert.That(texture.height, Is.EqualTo(32), "the frame imports at its authored height");

            var importer = ImporterFor(UiPath);
            Assert.That(importer.DoesSourceTextureHaveAlpha(), Is.True,
                "the source PNG carries alpha (verified in art/); a copy that lost it is corrupt");
            Assert.That(importer.alphaSource, Is.Not.EqualTo(TextureImporterAlphaSource.None),
                "a UI frame with its alpha discarded is an opaque rectangle over the HUD");
        }

        // ==========================================================================================
        //  helpers and test doubles
        // ==========================================================================================

        private static Texture2D LoadedTexture(string importedPath, string sourceInArt)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(importedPath);
            Assert.That(texture, Is.Not.Null,
                "expected the representative asset at " + importedPath + " — copy it from "
                + sourceInArt + " (a file operation; never re-run the pipeline to re-deliver art)");
            return texture;
        }

        private static TextureImporter ImporterFor(string importedPath)
        {
            var importer = AssetImporter.GetAtPath(importedPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null, importedPath + " must import as a texture");
            return importer;
        }

        private static string Describe(string key)
        {
            if (key == null)
            {
                return "<null>";
            }

            if (key.Length > 32)
            {
                return key.Substring(0, 32) + "…(" + key.Length + " chars)";
            }

            return "'" + key + "'";
        }

        private GameObject Track(GameObject go)
        {
            if (go != null)
            {
                _spawned.Add(go);
            }

            return go;
        }

        /// <summary>A recording fallback: remembers what it was asked and what it answered.</summary>
        private sealed class SpyResolver : IVisualResolver
        {
            public sealed class Call
            {
                public VisualClass Class;
                public string ArtKey;
            }

            public readonly List<Call> Calls = new List<Call>();

            public VisualHandle LastHandle;

            private readonly PlaceholderVisualResolver _inner = new PlaceholderVisualResolver();

            public VisualHandle Resolve(VisualClass visualClass, string artKey)
            {
                Calls.Add(new Call { Class = visualClass, ArtKey = artKey });
                LastHandle = _inner.Resolve(visualClass, artKey);
                return LastHandle;
            }
        }
    }
}
