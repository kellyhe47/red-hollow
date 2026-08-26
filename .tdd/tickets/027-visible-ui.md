---
id: 027
title: Visible, clickable UI — fonts, graphics and layout for the real player
status: green
depends_on: [026]
touches: [unity/RedHollow/Assets/Game/UI/]
iterations: 1
test_files: [unity/RedHollow/Assets/Tests/EditMode/T27_VisibleUiTests.cs]
branch: ""
board_id: T-27
owns_requirements: []
grades_fixtures: []
---

## Scope

Owner pressed Play (2026-08-26): the shell boots — hierarchy shows `RedHollow_Shell` /
`RedHollow_EventSystem` / `RedHollow_Match` — but the screen shows only the cavern. The UI
is invisible.

Root cause: `ShellUi`/`ShellControls` create `Text` with **no Font** (Unity 6 has no
implicit default — such a Text renders nothing) and `Button`s with **no Graphic**
(invisible AND unclickable — uGUI raycasting requires a Graphic). The EditMode tests read
`.text` values and invoked `onClick` directly, so pixels were never in the contract.

Adds the visible-presentation contract:
- every Text: non-null font (`Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` or
  an imported font), readable size, explicit color, non-degenerate RectTransform;
- every Button: an enabled raycastable Graphic background (the imported
  `button-normal_v1_320x96` sprite is available in Resources for exactly this);
- CanvasScaler (scale-with-screen-size) so QHD and other resolutions lay out sanely;
- screens laid out per the wireframe regions: top bar top, self/shop bars bottom, banners
  centered, picker as a card row, join-error under the code input.

## Acceptance criteria

- [x] every uGUI Text in the built shell has a non-null font, readable size and a
      non-degenerate rect
- [x] every Button has an enabled raycastable Graphic; greyed shop items and the ghost
      invalid tint are visually distinct states
- [x] the canvas has a CanvasScaler; screens lay out per the wireframe regions
- [x] booting to S1 yields visible title text and buttons (pinned via font/rect/graphic
      checks in EditMode — not screenshots)

## Test plan

`T27_VisibleUiTests` — 12 EditMode cases, all mechanical (anchor/property walks, no
screenshots, no dependency on the EditMode canvas having real pixels):

1. **Text renderability sweep** — every `Text` under the shell root (planning reached, shop
   bar grown, inactive screens included): `font != null`, `fontSize >= 14`, `color.a > 0`,
   rect non-degenerate (concrete size OR stretch anchors). **Expected GREEN today** —
   verified against the uGUI package source: `Text.Reset()` is `#if UNITY_EDITOR` and
   auto-assigns LegacyRuntime.ttf for any Text added outside play mode, so EditMode cannot
   turn the null-font omission red (Play mode/builds get no Reset — that is the owner's
   bug). Kept as the regression guard + size/color/rect contract; the implementer must
   still assign fonts explicitly per the acceptance criteria.
2. **Button visibility sweep** — every `Button`: non-null/enabled/raycastable `targetGraphic`
   on the button or a child, `color.a > 0` (sprite vs solid tint stays free).
3. **InputField renderability** — `textComponent` non-null, child of the field, fonted;
   `targetGraphic` non-null and raycastable.
4. **Canvas** — `ScreenSpaceOverlay` + `CanvasScaler` in `ScaleWithScreenSize` with a real
   reference resolution (`>= 640x360`; exact numbers free).
5. **Screen roots stretch full-screen** — RectTransform anchors (0,0)-(1,1) (±0.01), which is
   what makes the regional pins below read as screen regions.
6. **HUD regions** — wave/scrip/monsters/hotspot labels' containing bar (outermost ancestor
   below canvas/screen root — nesting free) `anchorMin.y > 0.7` (top bar); HP bar
   `anchorMax.y < 0.35` (SELF bottom bar). NOTE: forces HP out of the current shared HudPanel.
7. **Shop bar bottom band** — each shop button's bar `anchorMax.y < 0.4` under Planning.
8. **Banners centered + banner-sized** — S5 (while interstitial shows) and S6/S7 (TestCase
   victory/defeat): largest Text under the root exists, fonted, `fontSize >= 24`, bar anchor
   midpoint in x [0.2,0.8], y [0.25,0.9].
9. **Join error below code input** — anchor-midpoint comparison at the point where the two
   hierarchies diverge (deepest-common-ancestor children), so any nesting works.
10. **Shop affordability visibly distinct** — engineered mixed pool; per button:
    `targetGraphic != null`, `transition == ColorTint`, `disabledColor != normalColor`,
    `interactable == Affordable` (T23's pin, sanity here).
11. **Ghost visual** — new `ShellControls.GhostVisual` (additive throwing stub): a `Graphic`
    under the Planning root, inactive while `!GhostActive`, active after shop click + pump,
    valid-hover color != invalid-hover color, alpha > 0 in both states.
12. **S1-on-boot** — sessionless shell pumps to Title; all title Texts fonted/visible; some
    nonempty banner-sized (>= 24pt) title text; HOST/JOIN pass the visibility checks.

**Stub added (additive):** `ShellControls.GhostVisual` throws `NotImplementedException("T27:
ghost visual")`.

**Deliberately not pinned:** which font, exact sizes/colors beyond bands and "differs",
pixel positions, copy, sprite choice per control (button-normal available, not mandated),
the level-up picker's card-row layout (buttons only exist with live choices — flagged as a
gap), ESC-overlay regional placement.

## Attempt log

_(created 2026-08-26 by the orchestrator after the owner's Play test showed an invisible UI.)_

- 2026-08-26 green in 1 pass. Red 13/14 verified; green verified by orchestrator: EditMode
  266/266, dotnet 371/371. Explicit fonts at both Text-creation sites (play mode never
  auto-assigns). Picker card-row layout implemented but only play-exercised — worth an eye
  during the owner's session.

## Handoff notes

- Pin mechanically walkable properties: `GetComponentsInChildren<Text>(true)` → font,
  fontSize, rect; `GetComponentsInChildren<Button>(true)` → targetGraphic/raycastable
  Graphic. Layout pins should be regional/relative (anchors), not pixel-exact.
- The locked T21/T23/T26 tests must stay green — this ticket only adds presentation to the
  same objects.
- Art available in Resources (026 wiring): RedHollowArt/{button-normal,...}. The art/ui
  regen session may add more; do not block on it.
