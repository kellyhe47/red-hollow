using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RedHollow.Game.UI
{
    /// <summary>
    /// Ticket 027 (T-27) — the one place the shell's presentation constants live: the explicit
    /// runtime font (Unity 6 has NO implicit default — a Text built in play mode with a null font
    /// renders nothing, which is exactly the bug the owner's Play test hit; EditMode masks it
    /// because <c>Text.Reset()</c> is editor-only), the Lantern Deep palette
    /// (docs/comfy-prompts/00-shared-style.md: warm darks, amber lantern light), the imported
    /// button chrome, and the anchor helpers every screen lays out through.
    /// </summary>
    internal static class UiStyle
    {
        /// <summary>Warm parchment — body text over dark ground (high value contrast).</summary>
        internal static readonly Color Parchment = new Color(0.93f, 0.87f, 0.74f, 1f);

        /// <summary>Lantern amber — banners and accents.</summary>
        internal static readonly Color Ember = new Color(1f, 0.76f, 0.38f, 1f);

        /// <summary>Warm near-black — panel and input grounds.</summary>
        internal static readonly Color PanelDark = new Color(0.09f, 0.06f, 0.05f, 0.88f);

        /// <summary>Input-field well — a shade lighter than the panel ground.</summary>
        internal static readonly Color InputWell = new Color(0.16f, 0.11f, 0.08f, 0.95f);

        /// <summary>The inline-error tint (S1's join error).</summary>
        internal static readonly Color ErrorTint = new Color(0.95f, 0.45f, 0.33f, 1f);

        /// <summary>Solid button face when the imported chrome is unavailable.</summary>
        internal static readonly Color ButtonFace = new Color(0.62f, 0.42f, 0.2f, 1f);

        /// <summary>T-27 — the placement ghost over a VALID zone: translucent lantern amber.</summary>
        internal static readonly Color GhostValid = new Color(1f, 0.8f, 0.35f, 0.45f);

        /// <summary>T-27 — the ghost over an INVALID zone: the wireframe's red tint.</summary>
        internal static readonly Color GhostInvalidTint = new Color(0.85f, 0.18f, 0.12f, 0.55f);

        private static Font _font;
        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

        /// <summary>
        /// The explicit runtime font. LegacyRuntime.ttf is the built-in that exists in play mode
        /// and builds — relying on <c>Text.Reset()</c>'s editor-only auto-assign is the T-27 bug.
        /// </summary>
        internal static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }

                return _font;
            }
        }

        /// <summary>
        /// The imported button chrome (Assets/Game/UI/Resources/RedHollowArt/button-normal, a
        /// plain texture — spriteMode 0 — so it is wrapped here, same as the art catalog does).
        /// Null when the resource is missing; callers fall back to a solid face.
        /// </summary>
        internal static Sprite ButtonSprite => LoadSprite("RedHollowArt/button-normal");

        internal static Sprite LoadSprite(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
            {
                return null;
            }

            Sprite cached;
            if (SpriteCache.TryGetValue(resourcePath, out cached))
            {
                return cached;
            }

            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                SpriteCache[resourcePath] = null;
                return null;
            }

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            SpriteCache[resourcePath] = sprite;
            return sprite;
        }

        /// <summary>Explicit font, size, color, centered — everything a Text needs to render.</summary>
        internal static void StyleLabel(Text label, int size = 16)
        {
            label.font = Font;
            label.fontSize = size;
            label.color = Parchment;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
        }

        /// <summary>Anchor the rect to a normalized region of its parent, edges flush.</summary>
        internal static void Anchor(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Full-stretch over the parent.</summary>
        internal static void Stretch(RectTransform rt)
        {
            Anchor(rt, 0f, 0f, 1f, 1f);
        }
    }

    /// <summary>
    /// Ticket 021 (T-21) — the built uGUI hierarchy: one Canvas, one root GameObject per
    /// <see cref="UiScreen"/> (S1–S7), and the combat HUD's bound labels. This is a HANDLE object,
    /// not a presenter: every value a label shows comes out of the ticket-012 models
    /// (<see cref="CombatHudModel"/> and friends), pushed in by <see cref="ShellBootstrap.Pump"/> —
    /// nothing here reads sim state, decides a rule, or holds a sim reference (T-10's invariant).
    ///
    /// Label COPY is presentation and is never contract: the tests assert that a label's text
    /// contains the model's value ("Wave 1/10", "1", "wave 1" all pass), not what surrounds it.
    ///
    /// Built headlessly on purpose (plain GameObjects + components, no prefab, no scene asset), the
    /// same shape as <see cref="RedHollow.Game.View.MatchSceneBuilder"/>: one description of the UI
    /// serves the runtime bootstrap and an EditMode test without drifting.
    /// </summary>
    public sealed class ShellUi
    {
        /// <summary>
        /// The root everything below hangs from, named "RedHollow_Shell" so a session (or a test
        /// teardown) can find and destroy the whole UI in one call — the same convention as
        /// "RedHollow_Match" and "RedHollow_MatchViews".
        /// </summary>
        public GameObject Root;

        /// <summary>The one Canvas the shell renders through (the project has no UI Toolkit assets).</summary>
        public Canvas Canvas;

        /// <summary>R-61 — shows <see cref="CombatHudModel.WaveNumber"/> (TotalWaves may ride along).</summary>
        public Text WaveLabel;

        /// <summary>R-61 — shows <see cref="CombatHudModel.Scrip"/>.</summary>
        public Text ScripLabel;

        /// <summary>R-61 — shows the own hero's HP off <see cref="CombatHudModel.Hp"/>.</summary>
        public Text HpLabel;

        /// <summary>R-61 — Q slot face (locked / cooldown / ready). Presentation copy.</summary>
        public Text QLabel;

        /// <summary>R-61 — E slot face (locked / cooldown / ready). Presentation copy.</summary>
        public Text ELabel;

        /// <summary>R-61 — shows <see cref="CombatHudModel.MonstersRemaining"/>.</summary>
        public Text MonstersRemainingLabel;

        /// <summary>
        /// R-61 — one label per <see cref="CombatHudModel.Hotspots"/> readout, each showing that
        /// shelter's civilian count. Order matches the model's readout order.
        /// </summary>
        public IReadOnlyList<Text> HotspotLabels;

        /// <summary>
        /// R-63 — the planning countdown (wireframe S3's "⏱ 0:47"), off
        /// <see cref="PlanningScreenModel.TimerRemainingSeconds"/>. Empty outside planning: the
        /// combat top bar has no clock to show.
        /// </summary>
        public Text PlanningTimerLabel;

        /// <summary>
        /// R-63 — the ready fraction ("1/2 ready", denominator = connected players), off
        /// <see cref="PlanningScreenModel.ReadyCount"/> / <see cref="PlanningScreenModel.ConnectedCount"/>.
        /// Empty outside planning.
        /// </summary>
        public Text ReadyLabel;

        /// <summary>
        /// R-61 — account level and lifetime XP (wireframe S4's "XP bar + account level"), off
        /// <see cref="CombatHudModel.Level"/> / <see cref="CombatHudModel.LifetimeXp"/>. The model
        /// carried both since T-12; nothing rendered them.
        /// </summary>
        public Text XpLabel;

        /// <summary>
        /// The panel the HUD labels hang under, kept so the bootstrap can grow the per-hotspot
        /// label row to match the live colony without rebuilding the shell. Since T-27 this is
        /// the HUD's TOP BAR (wave · scrip · monsters · shelters), anchored to the top band of
        /// the screen per the wireframes; the HP readout lives in <see cref="SelfBar"/> instead.
        /// </summary>
        internal GameObject HudPanel;

        /// <summary>
        /// T-27 — the SELF bar (wireframe S4: the own hero's readout), anchored to the bottom
        /// band. Holds <see cref="HpLabel"/>, split out of the old shared HUD panel so the top
        /// and bottom regions can be pinned separately.
        /// </summary>
        internal GameObject SelfBar;

        /// <summary>The writable list behind <see cref="HotspotLabels"/>.</summary>
        internal readonly List<Text> HotspotLabelList = new List<Text>();

        private readonly Dictionary<UiScreen, GameObject> _screenRoots =
            new Dictionary<UiScreen, GameObject>();

        /// <summary>
        /// R-60 — the container GameObject for one screen. Total over the enum: every screen S1–S7
        /// has a root, they are distinct, and after a <see cref="ShellBootstrap.Pump"/> exactly the
        /// root of <see cref="UiRouter.Screen"/> is active in the hierarchy — screen switching is
        /// activation flipping, never rebuild-per-frame.
        /// </summary>
        public GameObject ScreenRoot(UiScreen screen)
        {
            GameObject root;
            return _screenRoots.TryGetValue(screen, out root) ? root : null;
        }

        /// <summary>
        /// Build the whole headless hierarchy: root, Canvas, one container per S1–S7, and the
        /// pinned HUD labels. Layout, fonts and copy are deliberately absent — presentation.
        /// </summary>
        internal static ShellUi Build()
        {
            var ui = new ShellUi();

            ui.Root = new GameObject("RedHollow_Shell");

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(ui.Root.transform, false);
            ui.Canvas = canvasGo.AddComponent<Canvas>();

            // T-27 — the canvas actually renders on a display: screen-space overlay, scaled with
            // the screen so QHD and 1080p lay out the same regions, and a raycaster so the
            // buttons' Graphics can be hit at all in play mode.
            ui.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            foreach (UiScreen screen in Enum.GetValues(typeof(UiScreen)))
            {
                var screenGo = new GameObject("Screen_" + screen, typeof(RectTransform));
                screenGo.transform.SetParent(canvasGo.transform, false);
                UiStyle.Stretch((RectTransform)screenGo.transform);
                screenGo.SetActive(false);
                ui._screenRoots[screen] = screenGo;
            }

            // T-27 — the HUD splits into the wireframes' regions: TOP BAR (wave · scrip ·
            // monsters · shelters) hangs from the top edge; the SELF bar (HP) sits in the bottom
            // band, above the planning shop bar's strip.
            ui.HudPanel = new GameObject("HUD_TopBar", typeof(RectTransform));
            ui.HudPanel.transform.SetParent(canvasGo.transform, false);
            UiStyle.Anchor((RectTransform)ui.HudPanel.transform, 0f, 0.93f, 1f, 1f);
            DressPanel(ui.HudPanel, "RedHollowArt/hud-topbar");

            ui.SelfBar = new GameObject("HUD_SelfBar", typeof(RectTransform));
            ui.SelfBar.transform.SetParent(canvasGo.transform, false);
            UiStyle.Anchor((RectTransform)ui.SelfBar.transform, 0.01f, 0.16f, 0.22f, 0.32f);
            DressPanel(ui.SelfBar, "RedHollowArt/dialog-panel");

            ui.WaveLabel = NewLabel(ui.HudPanel, "WaveLabel", 18);
            ui.PlanningTimerLabel = NewLabel(ui.HudPanel, "PlanningTimerLabel", 18);
            ui.ReadyLabel = NewLabel(ui.HudPanel, "ReadyLabel", 18);
            ui.ScripLabel = NewLabel(ui.HudPanel, "ScripLabel", 18);
            ui.HpLabel = NewLabel(ui.SelfBar, "HpLabel", 20);
            ui.QLabel = NewLabel(ui.SelfBar, "QLabel", 16);
            ui.ELabel = NewLabel(ui.SelfBar, "ELabel", 16);
            ui.XpLabel = NewLabel(ui.SelfBar, "XpLabel", 16);
            ui.MonstersRemainingLabel = NewLabel(ui.HudPanel, "MonstersRemainingLabel", 18);
            ui.HotspotLabels = ui.HotspotLabelList;
            ui.ArrangeTopBar();
            ui.ArrangeSelfBar();

            // T-27 — the S5/S6/S7 center-band banners (wireframes: a big verdict mid-screen).
            // Copy is presentation; existence, font, banner size and the center band are contract.
            NewBanner(ui._screenRoots[UiScreen.WaveInterstitial], "WaveBanner", "WAVE CLEARED");
            NewBanner(ui._screenRoots[UiScreen.Victory], "VictoryBanner", "THE HOLLOW HOLDS");
            NewBanner(ui._screenRoots[UiScreen.Defeat], "DefeatBanner", "THE COLONY IS LOST");

            return ui;
        }

        /// <summary>
        /// T-27 — one center-band banner Text: banner-sized (>= 24pt), lantern amber, its bar (a
        /// direct child of the full-screen root) anchored mid-screen.
        /// </summary>
        private static Text NewBanner(GameObject screenRoot, string name, string copy)
        {
            var banner = NewLabel(screenRoot, name, 44);
            banner.text = copy;
            banner.color = UiStyle.Ember;
            UiStyle.Anchor(banner.rectTransform, 0.2f, 0.45f, 0.8f, 0.72f);
            return banner;
        }

        /// <summary>
        /// T-27 — spread the top bar's labels evenly across its width, so each has stretch
        /// anchors (renderable at any resolution) and its own slice. Re-run whenever the
        /// per-hotspot row grows or shrinks.
        /// </summary>
        internal void ArrangeTopBar()
        {
            var bar = (RectTransform)HudPanel.transform;
            var count = bar.childCount;
            for (var i = 0; i < count; i++)
            {
                var slot = bar.GetChild(i) as RectTransform;
                if (slot != null)
                {
                    UiStyle.Anchor(slot, (i + 0.02f) / count, 0f, (i + 0.98f) / count, 1f);
                }
            }
        }

        /// <summary>Stack HP / Q / E inside the self bar so none sit on top of each other.</summary>
        internal void ArrangeSelfBar()
        {
            var bar = (RectTransform)SelfBar.transform;
            var count = bar.childCount;
            for (var i = 0; i < count; i++)
            {
                var slot = bar.GetChild(i) as RectTransform;
                if (slot != null)
                {
                    var yMax = 1f - (i / (float)count);
                    var yMin = 1f - ((i + 1) / (float)count);
                    UiStyle.Anchor(slot, 0.06f, yMin, 0.94f, yMax);
                }
            }
        }

        /// <summary>Paint a panel with imported chrome when the resource is present.</summary>
        private static void DressPanel(GameObject panel, string resourcePath)
        {
            var sprite = UiStyle.LoadSprite(resourcePath);
            var image = panel.GetComponent<Image>() ?? panel.AddComponent<Image>();
            if (sprite != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
                image.type = Image.Type.Simple;
            }
            else
            {
                image.color = UiStyle.PanelDark;
            }

            image.raycastTarget = false;
        }

        /// <summary>R-60 — exactly the routed screen's root is active; everything else is off.</summary>
        internal void SetActiveScreen(UiScreen active)
        {
            foreach (var pair in _screenRoots)
            {
                var shouldBeActive = pair.Key == active;
                if (pair.Value != null && pair.Value.activeSelf != shouldBeActive)
                {
                    pair.Value.SetActive(shouldBeActive);
                }
            }
        }

        /// <summary>
        /// R-61 — one label per shelter readout. Grows and shrinks with the live colony; the
        /// labels themselves persist across pumps (refresh, never rebuild-per-frame).
        /// </summary>
        internal void EnsureHotspotLabels(int count)
        {
            var changed = false;

            while (HotspotLabelList.Count < count)
            {
                HotspotLabelList.Add(
                    NewLabel(HudPanel, "HotspotLabel_" + HotspotLabelList.Count, 18));
                changed = true;
            }

            while (HotspotLabelList.Count > count)
            {
                var last = HotspotLabelList[HotspotLabelList.Count - 1];
                HotspotLabelList.RemoveAt(HotspotLabelList.Count - 1);
                if (last != null)
                {
                    DestroyGameObject(last.gameObject);
                }

                changed = true;
            }

            if (changed)
            {
                ArrangeTopBar();
            }
        }

        private static Text NewLabel(GameObject parent, string name, int size = 16)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            UiStyle.Stretch((RectTransform)go.transform);
            var label = go.AddComponent<Text>();
            label.text = string.Empty;
            UiStyle.StyleLabel(label, size);
            return label;
        }

        private static void DestroyGameObject(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(go);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
