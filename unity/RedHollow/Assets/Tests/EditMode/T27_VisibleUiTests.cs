using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Game.Net;
using RedHollow.Game.UI;
using RedHollow.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 027 (T-27) — the VISIBLE-presentation contract. The owner pressed Play (2026-08-26):
    /// the shell boots, the hierarchy is complete, and the screen shows only the cavern. Root
    /// cause: every <see cref="Text"/> the shell builds has NO Font (Unity 6 has no implicit
    /// default — such a Text renders nothing) and every <see cref="Button"/> has NO Graphic
    /// (invisible AND unclickable — uGUI raycasting needs a Graphic). The locked T21/T23/T26
    /// tests read <c>.text</c> values and invoked <c>onClick</c> directly, so pixels were never
    /// in the contract. These tests put them there — mechanically, in EditMode, no screenshots:
    ///
    ///  1. <b>Every Text is renderable</b> — non-null font, readable size, visible color,
    ///     non-degenerate rect.
    ///  2. <b>Every Button/InputField is visible and raycastable</b> — an enabled Graphic with
    ///     alpha, raycastTarget on.
    ///  3. <b>The Canvas can actually lay out</b> — ScreenSpaceOverlay + a scale-with-screen-size
    ///     CanvasScaler with a real reference resolution.
    ///  4. <b>Regions follow the wireframes</b> (docs/ui-wireframes.html) via ANCHORS, not
    ///     pixels: screen roots stretch full-screen; the HUD top bar hangs from the top edge;
    ///     the self (HP) and shop bars sit in the bottom band; the S5/S6/S7 banners sit in the
    ///     center band; the join error sits below the code input.
    ///  5. <b>State is visually distinct</b> — an unaffordable shop item's disabled tint differs
    ///     from its normal tint, and the placement ghost has an on-screen Graphic that follows
    ///     GhostActive and tints differently while GhostInvalid.
    ///  6. <b>Booting to S1 yields a visible title screen</b> — the pin the owner's Play test
    ///     would have needed.
    ///
    /// <b>Deliberately NOT asserted</b>, because aesthetics stay playtest: which font, exact
    /// sizes or colors (only tolerant bands and "differs"), pixel positions (only anchor bands),
    /// copy, sprite choice (solid-color Graphics pass — the imported
    /// RedHollowArt/button-normal sprite is available but not mandated per-control), and the
    /// level-up picker's card-row layout (its buttons only exist with live choices).
    ///
    /// All layout pins are pure anchor math on the RectTransforms — nothing here depends on the
    /// EditMode canvas having a real pixel rect, so a headless runner cannot flake them.
    /// </summary>
    [TestFixture]
    public class T27_VisibleUiTests
    {
        private const double Step60Hz = 1.0 / 60.0;

        private const string HostPeerId = "peer_host";
        private const string HostAccount = "acc_calamity";

        /// <summary>Readable floor for any on-screen label (uGUI's own default is 14).</summary>
        private const int MinLabelSize = 14;

        /// <summary>A banner (S1 title, S5/S6/S7) must be visibly bigger than body labels.</summary>
        private const int MinBannerSize = 24;

        /// <summary>The well-known roots the shell composes under (T21's teardown convention).</summary>
        private static readonly string[] ShellRootNames =
        {
            "RedHollow_Shell", "RedHollow_MatchViews", "RedHollow_Match",
        };

        private ShellBootstrap _shell;

        [TearDown]
        public void DestroyEverythingThisTestBuilt()
        {
            if (_shell != null)
            {
                try
                {
                    _shell.TearDown();
                }
                catch (Exception)
                {
                    // A stub or half-built shell must not turn a red test into a teardown error.
                }

                _shell = null;
            }

            foreach (var name in ShellRootNames)
            {
                for (var go = GameObject.Find(name); go != null; go = GameObject.Find(name))
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        // ==========================================================================================
        //  AC1 — every Text in the built shell is renderable
        // ==========================================================================================

        /// <summary>
        /// The Play-test failure, pinned as far as EditMode can reach: a Text with a null font
        /// renders NOTHING in Unity 6. Swept over the whole shell (inactive screens included)
        /// with the shop bar grown, so every label any screen shows is covered. Free choices
        /// stay free: which font (LegacyRuntime.ttf or an import), exact size (only the
        /// readable floor), exact color (only "not fully transparent").
        ///
        /// <b>Expected GREEN today — a documented EditMode limitation, verified against the
        /// uGUI package source:</b> <c>Text.Reset()</c> is <c>#if UNITY_EDITOR</c> and
        /// auto-assigns LegacyRuntime.ttf whenever a Text is added OUTSIDE play mode, so an
        /// EditMode-built shell always carries fonts even though the same code path at runtime
        /// (the owner's Play press) yields null fonts and an invisible UI. This sweep therefore
        /// serves as (a) the regression guard against anyone explicitly nulling/shrinking/
        /// transparenting a label, and (b) the size/color/rect contract. The implementer must
        /// STILL assign fonts explicitly (ticket 027's acceptance criterion) — EditMode simply
        /// cannot turn that particular omission red.
        /// </summary>
        [Test]
        public void Every_text_in_the_shell_has_a_font_a_readable_size_and_a_visible_color()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            var texts = shell.Ui.Root.GetComponentsInChildren<Text>(true);
            Assert.That(texts, Is.Not.Empty, "sanity: the shell contains labels to check");

            foreach (var text in texts)
            {
                var path = PathOf(text.transform, shell.Ui.Root.transform);
                Assert.That(text.font, Is.Not.Null,
                    "T-27: " + path + " has no Font — Unity 6 renders such a Text as NOTHING "
                    + "(the exact bug the owner's Play test hit)");
                Assert.That(text.fontSize, Is.GreaterThanOrEqualTo(MinLabelSize),
                    "T-27: " + path + " must be readable (>= " + MinLabelSize + "pt)");
                Assert.That(text.color.a, Is.GreaterThan(0f),
                    "T-27: " + path + " is fully transparent — invisible by color");
                AssertNonDegenerateRect(text.rectTransform, path);
            }
        }

        // ==========================================================================================
        //  AC2 — every Button is visible and raycastable
        // ==========================================================================================

        /// <summary>
        /// The other half of the Play-test failure: a Button with no Graphic is invisible AND
        /// unclickable (uGUI's raycaster only hits Graphics). Pinned mechanically: a non-null,
        /// enabled, raycastable targetGraphic on the button's own GameObject or a child, with
        /// alpha — an Image with a sprite (RedHollowArt/button-normal is in Resources for this)
        /// or a solid tint both pass.
        /// </summary>
        [Test]
        public void Every_button_carries_an_enabled_raycastable_visible_graphic()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            var buttons = shell.Ui.Root.GetComponentsInChildren<Button>(true);
            Assert.That(buttons, Is.Not.Empty, "sanity: the shell contains buttons to check");

            foreach (var button in buttons)
            {
                AssertVisiblyClickable(button, shell.Ui.Root.transform);
            }
        }

        /// <summary>
        /// The S1 InputFields need the same treatment: an InputField renders its typed value
        /// through <see cref="InputField.textComponent"/> (null today — typing shows nothing)
        /// and is clicked-into through its targetGraphic (also null today).
        /// </summary>
        [Test]
        public void Every_input_field_renders_its_text_and_is_clickable()
        {
            var shell = NewShell();
            shell.Pump(0.0);

            var inputs = shell.Ui.Root.GetComponentsInChildren<InputField>(true);
            Assert.That(inputs, Is.Not.Empty, "sanity: S1 has its two inputs");

            foreach (var input in inputs)
            {
                var path = PathOf(input.transform, shell.Ui.Root.transform);
                Assert.That(input.textComponent, Is.Not.Null,
                    "T-27: " + path + " has no textComponent — typed text renders nowhere");
                Assert.That(input.textComponent.transform.IsChildOf(input.transform), Is.True,
                    "T-27: " + path + "'s text renders inside the field");
                Assert.That(input.textComponent.font, Is.Not.Null,
                    "T-27: " + path + "'s text needs a font to render");
                Assert.That(input.targetGraphic, Is.Not.Null,
                    "T-27: " + path + " needs a Graphic to be clicked into");
                Assert.That(input.targetGraphic.raycastTarget, Is.True,
                    "T-27: " + path + "'s graphic must catch the pointer");
            }
        }

        // ==========================================================================================
        //  AC3 — the Canvas can actually lay out on a real display
        // ==========================================================================================

        /// <summary>
        /// The owner plays on QHD: without a CanvasScaler in scale-with-screen-size mode the UI
        /// lays out in raw pixels and shrinks per display. Mode and "a real reference resolution
        /// is set" are the contract; the exact numbers are the implementer's.
        /// </summary>
        [Test]
        public void The_canvas_is_screen_space_overlay_with_a_scale_with_screen_size_scaler()
        {
            var shell = NewShell();
            shell.Pump(0.0);

            var canvas = shell.Ui.Canvas;
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay),
                "T-27: the shell UI renders as a screen-space overlay");

            var scaler = canvas.GetComponent<CanvasScaler>();
            Assert.That(scaler, Is.Not.Null,
                "T-27: the canvas needs a CanvasScaler or QHD renders a postage stamp");
            Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize),
                "T-27: scale-with-screen-size, so every resolution lays out the same regions");
            Assert.That(scaler.referenceResolution.x, Is.GreaterThanOrEqualTo(640f),
                "T-27: a real reference resolution is set (width)");
            Assert.That(scaler.referenceResolution.y, Is.GreaterThanOrEqualTo(360f),
                "T-27: a real reference resolution is set (height)");
        }

        /// <summary>
        /// Every screen root stretches over the whole canvas (anchors (0,0)-(1,1)). This is what
        /// makes the per-region anchor pins below meaningful: a bar anchored to the top of a
        /// full-screen root is anchored to the top of the screen. Today the roots are plain
        /// GameObjects with no RectTransform at all.
        /// </summary>
        [Test]
        public void Every_screen_root_stretches_full_screen()
        {
            var shell = NewShell();
            shell.Pump(0.0);

            foreach (UiScreen screen in Enum.GetValues(typeof(UiScreen)))
            {
                var root = shell.Ui.ScreenRoot(screen);
                var rt = root.transform as RectTransform;
                Assert.That(rt, Is.Not.Null,
                    "T-27: " + screen + "'s root needs a RectTransform to lay out at all");
                Assert.That(rt.anchorMin.x, Is.LessThanOrEqualTo(0.01f), screen + " stretches to the left edge");
                Assert.That(rt.anchorMin.y, Is.LessThanOrEqualTo(0.01f), screen + " stretches to the bottom edge");
                Assert.That(rt.anchorMax.x, Is.GreaterThanOrEqualTo(0.99f), screen + " stretches to the right edge");
                Assert.That(rt.anchorMax.y, Is.GreaterThanOrEqualTo(0.99f), screen + " stretches to the top edge");
            }
        }

        // ==========================================================================================
        //  AC3 — regional layout per the wireframes, via anchors
        // ==========================================================================================

        /// <summary>
        /// S3/S4's TOP BAR (wave · scrip · monsters · civilians) hangs from the top edge, and the
        /// SELF readout (HP) sits in the bottom band — the wireframes' regions, pinned on the
        /// containing bar's anchors (the "bar" is each label's outermost ancestor below the
        /// canvas / screen root, so the implementer nests freely).
        /// </summary>
        [Test]
        public void The_hud_top_bar_hangs_from_the_top_and_the_self_bar_sits_at_the_bottom()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            shell.Pump(0.0);
            Assert.That(match.State.Hotspots.Count, Is.GreaterThan(0), "sanity: the colony has shelters");

            var ui = shell.Ui;
            var topBarLabels = new List<Text> { ui.WaveLabel, ui.ScripLabel, ui.MonstersRemainingLabel };
            topBarLabels.AddRange(ui.HotspotLabels);

            foreach (var label in topBarLabels)
            {
                var bar = BarOf(label, ui, "the top-bar label " + label.name);
                Assert.That(bar.anchorMin.y, Is.GreaterThan(0.7f),
                    "T-27: " + label.name + "'s bar is anchored to the top band of the screen "
                    + "(wireframe: TOP BAR) — anchorMin.y was " + bar.anchorMin.y);
            }

            var selfBar = BarOf(ui.HpLabel, ui, "the HP readout");
            Assert.That(selfBar.anchorMax.y, Is.LessThan(0.35f),
                "T-27: the HP (SELF) bar sits in the bottom band (wireframe S4: SELF bottom bar) "
                + "— anchorMax.y was " + selfBar.anchorMax.y);
        }

        /// <summary>S3's SHOP BAR sits along the bottom of the planning screen (wireframe S3).</summary>
        [Test]
        public void The_shop_bar_sits_in_the_bottom_band_of_the_planning_screen()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            Assert.That(shell.Planning.ShopItems, Is.Not.Empty, "sanity (R-23): the catalog has rows");
            foreach (var item in shell.Planning.ShopItems)
            {
                var button = shell.Controls.ShopItemButton(item.Type);
                Assert.That(button, Is.Not.Null, "sanity (T-23): " + item.Type + " has a button");

                var bar = BarOf(button, shell.Ui, "the " + item.Type + " shop button");
                Assert.That(bar.anchorMax.y, Is.LessThan(0.4f),
                    "T-27: " + item.Type + "'s shop bar sits in the bottom band (wireframe S3: "
                    + "SHOP BAR bottom) — anchorMax.y was " + bar.anchorMax.y);
            }
        }

        /// <summary>
        /// S5's wave banner sits in the center band while the interstitial shows — and it is a
        /// real banner: a Text with a font, sized clearly above body labels.
        /// </summary>
        [Test]
        public void The_wave_complete_banner_is_centered_and_banner_sized()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            shell.Pump(0.0);

            KillWave(match, match.State.Wave.LivingMonsterIds.ToList());
            shell.Pump(0.0);
            Assert.That(shell.Router.Screen, Is.EqualTo(UiScreen.WaveInterstitial),
                "sanity (R-04): the cleared wave shows S5");

            AssertCenteredBanner(shell, UiScreen.WaveInterstitial);
        }

        /// <summary>S6/S7's verdict banner sits in the center band on the shown screen.</summary>
        [TestCase(true, TestName = "the victory banner is centered and banner sized")]
        [TestCase(false, TestName = "the defeat banner is centered and banner sized")]
        public void The_post_match_banner_is_centered_and_banner_sized(bool victory)
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            shell.Pump(0.0);

            FinishMatch(match, victory);
            shell.Pump(0.0);

            var screen = victory ? UiScreen.Victory : UiScreen.Defeat;
            Assert.That(shell.Router.Screen, Is.EqualTo(screen),
                "sanity (R-60): the finished match shows its post-match screen");

            AssertCenteredBanner(shell, screen);
        }

        /// <summary>
        /// The inline join error renders BELOW the code input it explains (wireframe S1: "inline
        /// error under code input"). Pinned as pure anchor math at the point where the two
        /// hierarchies diverge, so any nesting works — but "same default anchors, same spot"
        /// (today's layout-free hierarchy) honestly fails.
        /// </summary>
        [Test]
        public void The_join_error_sits_below_the_join_code_input()
        {
            var shell = NewShell();
            shell.Pump(0.0);

            var error = shell.Controls.JoinErrorLabel;
            var input = shell.Controls.JoinCodeInput;
            Assert.That(error, Is.Not.Null, "sanity (T-23): the inline error exists");
            Assert.That(input, Is.Not.Null, "sanity (T-23): the code input exists");

            RectTransform errorSide, inputSide;
            SplitAtCommonAncestor(error.transform, input.transform, shell.Ui.Root.transform,
                out errorSide, out inputSide);

            var errorMidY = (errorSide.anchorMin.y + errorSide.anchorMax.y) * 0.5f;
            var inputMidY = (inputSide.anchorMin.y + inputSide.anchorMax.y) * 0.5f;
            Assert.That(errorMidY, Is.LessThan(inputMidY),
                "T-27: the join error's anchor band sits below the code input's (wireframe S1: "
                + "inline error UNDER the code input) — error mid " + errorMidY
                + " vs input mid " + inputMidY);
        }

        // ==========================================================================================
        //  AC2 — visually distinct states: unaffordable shop items, the ghost's invalid tint
        // ==========================================================================================

        /// <summary>
        /// R-63 — T-23 pinned <c>interactable == Affordable</c>; this pins that the flip is
        /// VISIBLE: the button tints through a ColorTint transition whose disabled tint differs
        /// from its normal tint, over a real target graphic. The scrip is engineered so both
        /// states exist on screen at once.
        /// </summary>
        [Test]
        public void An_unaffordable_shop_item_is_visibly_distinct_from_an_affordable_one()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            var costs = shell.Planning.ShopItems.Select(i => i.Cost).ToList();
            Assert.That(costs.Min(), Is.LessThan(costs.Max()),
                "sanity (R-23): prices differ, so a mid-range pool splits the states");
            match.State.Team.Scrip = costs.Max() - 1;
            shell.Pump(0.0);

            var states = new HashSet<bool>();
            foreach (var item in shell.Planning.ShopItems)
            {
                var button = shell.Controls.ShopItemButton(item.Type);
                Assert.That(button.targetGraphic, Is.Not.Null,
                    "T-27: " + item.Type + "'s tint needs a graphic to land on — with no "
                    + "targetGraphic the disabled state renders identically (i.e. not at all)");
                Assert.That(button.transition, Is.EqualTo(Selectable.Transition.ColorTint),
                    "T-27: the pinned distinctness mechanism is the ColorTint transition");
                Assert.That(button.colors.disabledColor, Is.Not.EqualTo(button.colors.normalColor),
                    "T-27: " + item.Type + "'s disabled tint must differ from its normal tint, "
                    + "or greyed-out is invisible");
                Assert.That(button.interactable, Is.EqualTo(item.Affordable),
                    "sanity (T-23): interactability IS the modeled Affordable flag");
                states.Add(item.Affordable);
            }

            Assert.That(states, Is.EquivalentTo(new[] { true, false }),
                "anti-vacuity: the engineered pool shows both states at once");
        }

        /// <summary>
        /// The placement ghost was MODEL state only — nothing on screen followed it. Pins the new
        /// <see cref="ShellControls.GhostVisual"/> accessor: a Graphic under the Planning root,
        /// active exactly with <see cref="PlanningScreenModel.GhostActive"/>, visibly tinted, and
        /// tinted DIFFERENTLY while <see cref="PlanningScreenModel.GhostInvalid"/> (the
        /// wireframe's "invalid zones tint red" — the exact colors stay free).
        /// </summary>
        [Test]
        public void The_ghost_has_a_visual_that_follows_active_and_invalid_states()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            var ghost = shell.Controls.GhostVisual;
            Assert.That(ghost, Is.Not.Null, "T-27: the ghost has an on-screen visual");
            Assert.That(ghost.transform.IsChildOf(shell.Ui.ScreenRoot(UiScreen.Planning).transform),
                Is.True, "the ghost visual lives under the Planning root, so R-60 hides it with S3");
            Assert.That(ghost.gameObject.activeInHierarchy, Is.False,
                "no ghost is up yet, so nothing shows (GhostActive is false)");

            var item = shell.Planning.ShopItems.OrderBy(i => i.Cost).First();
            Assert.That(item.Affordable, Is.True, "sanity: the cheapest item is affordable");
            shell.Controls.ShopItemButton(item.Type).onClick.Invoke();
            Assert.That(shell.Planning.GhostActive, Is.True, "sanity (T-23): the ghost is up");

            shell.Controls.PointerAt(new Vec2(2.0, 3.0), zoneValid: true);
            shell.Pump(0.0);
            Assert.That(ghost.gameObject.activeInHierarchy, Is.True,
                "T-27: an active ghost is on screen after the pump");
            Assert.That(ghost.color.a, Is.GreaterThan(0f), "the valid-state ghost is visible");
            var validColor = ghost.color;

            shell.Controls.PointerAt(new Vec2(2.0, 3.0), zoneValid: false);
            shell.Pump(0.0);
            Assert.That(shell.Planning.GhostInvalid, Is.True, "sanity: the hovered zone is invalid");
            Assert.That(ghost.color, Is.Not.EqualTo(validColor),
                "T-27: the invalid-zone tint differs visibly from the valid one (wireframe S3: "
                + "invalid zones tint)");
            Assert.That(ghost.color.a, Is.GreaterThan(0f), "the invalid-state ghost is visible");
        }

        // ==========================================================================================
        //  AC4 — booting to S1 yields a visible title screen
        // ==========================================================================================

        /// <summary>
        /// The owner's exact repro, pinned: a fresh shell with no session shows S1 — and S1 is
        /// VISIBLE: a real banner text (nonempty, banner-sized, with a font) plus renderable
        /// labels and clickable PLAY MATCH / HOST GAME / JOIN buttons. Copy stays free;
        /// existence, font, size and graphics are the contract. PLAY MATCH is the large
        /// primary and sits above HOST GAME.
        /// </summary>
        [Test]
        public void Booting_with_no_session_shows_a_visible_title_screen()
        {
            var shell = NewShell();
            shell.Pump(0.0);

            Assert.That(shell.Router.Screen, Is.EqualTo(UiScreen.Title),
                "sanity (R-60): a sessionless shell is on S1");
            var title = shell.Ui.ScreenRoot(UiScreen.Title);
            Assert.That(title.activeInHierarchy, Is.True, "sanity: the title root is shown");

            var texts = title.GetComponentsInChildren<Text>(true);
            Assert.That(texts, Is.Not.Empty, "S1 carries labels");
            foreach (var text in texts)
            {
                var path = PathOf(text.transform, shell.Ui.Root.transform);
                Assert.That(text.font, Is.Not.Null, "T-27: " + path + " needs a font to render");
                Assert.That(text.color.a, Is.GreaterThan(0f), "T-27: " + path + " must not be transparent");
            }

            Assert.That(texts.Any(t => t.font != null
                                       && !string.IsNullOrEmpty(t.text)
                                       && t.text.Trim().Length > 0
                                       && t.fontSize >= MinBannerSize),
                Is.True,
                "T-27: S1 shows a visible TITLE banner — some nonempty text at banner size "
                + "(>= " + MinBannerSize + "pt); its copy is presentation and stays free");

            AssertVisiblyClickable(shell.Controls.PlayMatchButton, shell.Ui.Root.transform);
            AssertVisiblyClickable(shell.Controls.HostButton, shell.Ui.Root.transform);
            AssertVisiblyClickable(shell.Controls.JoinButton, shell.Ui.Root.transform);

            var playCaption = shell.Controls.PlayMatchButton.GetComponentInChildren<Text>(true);
            Assert.That(playCaption, Is.Not.Null, "PLAY MATCH has a caption");
            Assert.That(playCaption.text, Is.EqualTo("PLAY MATCH"));
            Assert.That(playCaption.fontSize, Is.GreaterThanOrEqualTo(MinBannerSize),
                "T-27: PLAY MATCH is the large primary — banner-sized caption");

            RectTransform playSide, hostSide;
            SplitAtCommonAncestor(
                shell.Controls.PlayMatchButton.transform,
                shell.Controls.HostButton.transform,
                shell.Ui.Root.transform,
                out playSide, out hostSide);
            var playMidY = (playSide.anchorMin.y + playSide.anchorMax.y) * 0.5f;
            var hostMidY = (hostSide.anchorMin.y + hostSide.anchorMax.y) * 0.5f;
            Assert.That(playMidY, Is.GreaterThan(hostMidY),
                "T-27: PLAY MATCH sits above HOST GAME — play mid " + playMidY
                + " vs host mid " + hostMidY);
        }

        /// <summary>
        /// Overlay dumps park the canvas on a camera and still batch inactive Screen_* children,
        /// which stacked S1 HOST GAME / callsign onto S2. Pins both directions: Lobby hides
        /// title chrome, Title hides lobby chrome (including a return to S1).
        /// </summary>
        [Test]
        public void Lobby_hides_title_chrome_and_title_hides_lobby_chrome()
        {
            var shell = NewShell();
            shell.Pump(0.0);
            AssertScreenPainted(shell, UiScreen.Title, true, "boot lands on S1");
            AssertScreenPainted(shell, UiScreen.Lobby, false, "S2 is not stacked on S1");

            shell.Title.SetCallsign(HostAccount);
            shell.RequestHost();
            shell.Pump(0.0);
            Assert.That(shell.Router.Screen, Is.EqualTo(UiScreen.Lobby),
                "sanity (R-50): HOST GAME opened S2");
            AssertScreenPainted(shell, UiScreen.Lobby, true, "S2 is the painted screen");
            AssertScreenPainted(shell, UiScreen.Title, false,
                "S1 HOST GAME / callsign chrome must not stack on the lobby");

            shell.LeaveToTitle();
            shell.Pump(0.0);
            Assert.That(shell.Router.Screen, Is.EqualTo(UiScreen.Title),
                "sanity: leave returns to S1");
            AssertScreenPainted(shell, UiScreen.Title, true, "returning to S1 shows title chrome");
            AssertScreenPainted(shell, UiScreen.Lobby, false, "returning to S1 hides S2 chrome");
        }

        // ==========================================================================================
        //  shared assertions
        // ==========================================================================================

        /// <summary>
        /// A screen root is either fully paintable or fully culled: activeInHierarchy, CanvasGroup
        /// alpha, and every CanvasRenderer.cull agree. The cull is what an overlay-to-camera dump
        /// honors when SetActive alone does not.
        /// </summary>
        private static void AssertScreenPainted(
            ShellBootstrap shell, UiScreen screen, bool painted, string because)
        {
            var root = shell.Ui.ScreenRoot(screen);
            Assert.That(root, Is.Not.Null, because + " — " + screen + " has a root");
            Assert.That(root.activeInHierarchy, Is.EqualTo(painted),
                because + " — " + screen + " activeInHierarchy");

            var group = root.GetComponent<CanvasGroup>();
            Assert.That(group, Is.Not.Null, because + " — " + screen + " has a CanvasGroup");
            Assert.That(group.alpha, painted ? Is.EqualTo(1f) : Is.EqualTo(0f),
                because + " — " + screen + " CanvasGroup.alpha");

            var renderers = root.GetComponentsInChildren<CanvasRenderer>(true);
            Assert.That(renderers, Is.Not.Empty, because + " — " + screen + " has graphics");
            foreach (var renderer in renderers)
            {
                Assert.That(renderer.cull, Is.EqualTo(!painted),
                    because + " — " + screen + "/" + renderer.gameObject.name
                    + " CanvasRenderer.cull must be " + (!painted));
            }
        }

        /// <summary>A Button the player can actually see and hit.</summary>
        private static void AssertVisiblyClickable(Button button, Transform shellRoot)
        {
            Assert.That(button, Is.Not.Null, "the button exists");
            var path = PathOf(button.transform, shellRoot);

            var graphic = button.targetGraphic;
            Assert.That(graphic, Is.Not.Null,
                "T-27: " + path + " has no targetGraphic — a graphicless Button is invisible AND "
                + "unclickable (uGUI raycasting only hits Graphics)");
            Assert.That(graphic.enabled, Is.True, "T-27: " + path + "'s graphic must be enabled");
            Assert.That(graphic.raycastTarget, Is.True,
                "T-27: " + path + "'s graphic must catch the pointer");
            Assert.That(graphic.transform == button.transform
                        || graphic.transform.IsChildOf(button.transform),
                Is.True, "T-27: " + path + "'s graphic renders on the button itself or a child");
            Assert.That(graphic.color.a, Is.GreaterThan(0f),
                "T-27: " + path + "'s graphic is fully transparent — invisible by color");
        }

        /// <summary>
        /// The rect can render at SOME size: either a concrete size already, or stretch anchors
        /// that take size from the parent. (Pure anchor/sizeDelta math — no dependency on the
        /// EditMode canvas having real pixels.)
        /// </summary>
        private static void AssertNonDegenerateRect(RectTransform rt, string path)
        {
            Assert.That(rt.rect.width > 0f || rt.anchorMax.x > rt.anchorMin.x, Is.True,
                "T-27: " + path + "'s rect has zero width and no horizontal stretch — it can "
                + "never render");
            Assert.That(rt.rect.height > 0f || rt.anchorMax.y > rt.anchorMin.y, Is.True,
                "T-27: " + path + "'s rect has zero height and no vertical stretch — it can "
                + "never render");
        }

        /// <summary>
        /// The screen's banner: the largest Text under its root has a font, banner size, and its
        /// bar sits in the center band of the (full-screen) root.
        /// </summary>
        private static void AssertCenteredBanner(ShellBootstrap shell, UiScreen screen)
        {
            var root = shell.Ui.ScreenRoot(screen);
            var texts = root.GetComponentsInChildren<Text>(true);
            Assert.That(texts, Is.Not.Empty,
                "T-27: " + screen + " carries a banner Text (wireframe: a centered verdict)");

            var banner = texts.OrderByDescending(t => t.fontSize).First();
            var path = PathOf(banner.transform, shell.Ui.Root.transform);
            Assert.That(banner.font, Is.Not.Null, "T-27: " + path + " needs a font to render");
            Assert.That(banner.fontSize, Is.GreaterThanOrEqualTo(MinBannerSize),
                "T-27: " + path + " is the screen's banner — visibly larger than body labels");

            var bar = BarOf(banner, shell.Ui, screen + "'s banner");
            var midX = (bar.anchorMin.x + bar.anchorMax.x) * 0.5f;
            var midY = (bar.anchorMin.y + bar.anchorMax.y) * 0.5f;
            Assert.That(midX, Is.InRange(0.2f, 0.8f),
                "T-27: " + screen + "'s banner sits in the horizontal center band — mid x " + midX);
            Assert.That(midY, Is.InRange(0.25f, 0.9f),
                "T-27: " + screen + "'s banner sits in the vertical center band — mid y " + midY);
        }

        // ==========================================================================================
        //  anchor-walking helpers
        // ==========================================================================================

        /// <summary>
        /// The element's BAR: its outermost ancestor strictly below the canvas / its screen root.
        /// Because every screen root is pinned full-screen, the bar's anchors read directly as
        /// screen regions — while the implementer nests labels inside panels freely.
        /// </summary>
        private static RectTransform BarOf(Component element, ShellUi ui, string what)
        {
            Assert.That(element, Is.Not.Null, what + " exists");

            var screenRoots = new HashSet<Transform>();
            foreach (UiScreen screen in Enum.GetValues(typeof(UiScreen)))
            {
                var root = ui.ScreenRoot(screen);
                if (root != null)
                {
                    screenRoots.Add(root.transform);
                }
            }

            var t = element.transform;
            while (t.parent != null
                   && t.parent != ui.Canvas.transform
                   && !screenRoots.Contains(t.parent))
            {
                t = t.parent;
            }

            var bar = t as RectTransform;
            Assert.That(bar, Is.Not.Null,
                "T-27: " + what + "'s containing bar (" + t.name + ") needs a RectTransform to "
                + "be laid out at all");
            return bar;
        }

        /// <summary>
        /// Walk both elements up to their deepest common ancestor and return each side's child of
        /// it — the two RectTransforms whose anchors decide their relative placement.
        /// </summary>
        private static void SplitAtCommonAncestor(Transform a, Transform b, Transform limit,
            out RectTransform aSide, out RectTransform bSide)
        {
            var aChain = ChainUpTo(a, limit);
            var bChain = ChainUpTo(b, limit);
            var bSet = new HashSet<Transform>(bChain);

            Transform common = null;
            foreach (var node in aChain)
            {
                if (bSet.Contains(node))
                {
                    common = node;
                    break;
                }
            }

            Assert.That(common, Is.Not.Null, "the two controls share the shell hierarchy");
            Assert.That(common, Is.Not.SameAs(a).And.Not.SameAs(b),
                "the join error and the code input are separate controls, one below the other");

            aSide = ChildOf(common, a);
            bSide = ChildOf(common, b);
            Assert.That(aSide, Is.Not.Null, "the error side lays out through a RectTransform");
            Assert.That(bSide, Is.Not.Null, "the input side lays out through a RectTransform");
        }

        private static List<Transform> ChainUpTo(Transform t, Transform limit)
        {
            var chain = new List<Transform>();
            for (var node = t; node != null; node = node.parent)
            {
                chain.Add(node);
                if (node == limit)
                {
                    break;
                }
            }

            return chain;
        }

        /// <summary>The ancestor of <paramref name="leaf"/> that is a direct child of <paramref name="parent"/>.</summary>
        private static RectTransform ChildOf(Transform parent, Transform leaf)
        {
            var t = leaf;
            while (t.parent != null && t.parent != parent)
            {
                t = t.parent;
            }

            return t as RectTransform;
        }

        private static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var node = t; node != null && node != root; node = node.parent)
            {
                parts.Insert(0, node.name);
            }

            return string.Join("/", parts.ToArray());
        }

        // ==========================================================================================
        //  scenario builders (T21/T23's recipes, verbatim)
        // ==========================================================================================

        /// <summary>A fresh shell on S1: loopback, in-memory profiles.</summary>
        private ShellBootstrap NewShell()
        {
            _shell = new ShellBootstrap(new ShellBootstrapOptions
            {
                Transport = new LoopbackNetTransport(),
                Profiles = new InMemoryProfileStore(),
                SimConfig = new SimConfig(),
                LocalPeerId = HostPeerId,
                LocalAccountId = HostAccount,
            });

            return _shell;
        }

        /// <summary>A shell with the host seated — the S2 starting point (T21's helper).</summary>
        private ShellBootstrap NewHostedShell()
        {
            var shell = NewShell();

            shell.Session.StartHost(new NetPeer
            {
                PeerId = HostPeerId,
                AccountId = HostAccount,
                HeroClass = HeroClass.Gunslinger,
                IsHost = true,
            });

            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "sanity (R-50): hosting opens a lobby");

            return shell;
        }

        private static HostedMatch StartMatch(ShellBootstrap shell)
        {
            Assert.That(shell.Session.TryStartMatch(HostPeerId), Is.True,
                "sanity (R-50): the host starts the match");

            var match = shell.Session.Match;
            Assert.That(match, Is.Not.Null, "the session holds the live match");
            return match;
        }

        /// <summary>Clear the live wave and ride S5's hold into S3 (T21/T23's recipe).</summary>
        private static void ReachPlanning(ShellBootstrap shell, HostedMatch match)
        {
            shell.Pump(0.0);
            KillWave(match, match.State.Wave.LivingMonsterIds.ToList());
            shell.Pump(0.0);

            var holdSteps = (int)Math.Ceiling(shell.Router.InterstitialSeconds / Step60Hz) + 2;
            for (var i = 0; i < holdSteps; i++)
            {
                shell.Pump(Step60Hz);
            }

            Assert.That(shell.Router.Screen, Is.EqualTo(UiScreen.Planning),
                "sanity (R-04): the interstitial fell back to planning");
            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Planning),
                "sanity: the sim is in its planning phase");
        }

        /// <summary>Victory by clearing the final wave; defeat by emptying the colony (T21).</summary>
        private static void FinishMatch(HostedMatch match, bool victory)
        {
            if (victory)
            {
                match.State.Wave.Number = match.State.Wave.TotalWaves;
                KillWave(match, match.State.Wave.LivingMonsterIds.ToList());
                Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Victory),
                    "sanity (R-01): clearing the final wave wins the map");
            }
            else
            {
                foreach (var hotspot in match.State.Hotspots.Values.ToList())
                {
                    while (hotspot.Civilians > 0)
                    {
                        match.Sim.ApplyHotspotAttack(new HotspotAttackRequest
                        {
                            AttackerId = "m_wipeout",
                            AttackerType = MonsterType.Shambler,
                            Damage = 1000.0,
                            TargetId = hotspot.Id,
                        });
                    }
                }

                Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Defeat),
                    "sanity (R-02): an emptied colony is the defeat");
            }
        }

        /// <summary>Clears a wave one kill at a time through the sim's own command (T-12's helper).</summary>
        private static void KillWave(HostedMatch match, IEnumerable<string> monsterIds)
        {
            foreach (var id in monsterIds.ToList())
            {
                match.Sim.RecordMonsterKill(new MonsterKillRequest
                {
                    MonsterId = id,
                    MonsterType = match.State.Monsters.TryGetValue(id, out var monster)
                        ? monster.Type
                        : null,
                    Bounty = 0,
                });
            }
        }
    }
}
