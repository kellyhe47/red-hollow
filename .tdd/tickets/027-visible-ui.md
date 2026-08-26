---
id: 027
title: Visible, clickable UI — fonts, graphics and layout for the real player
status: pending
depends_on: [026]
touches: [unity/RedHollow/Assets/Game/UI/]
iterations: 0
test_files: []
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

- [ ] every uGUI Text in the built shell has a non-null font, readable size and a
      non-degenerate rect
- [ ] every Button has an enabled raycastable Graphic; greyed shop items and the ghost
      invalid tint are visually distinct states
- [ ] the canvas has a CanvasScaler; screens lay out per the wireframe regions
- [ ] booting to S1 yields visible title text and buttons (pinned via font/rect/graphic
      checks in EditMode — not screenshots)

## Test plan

_Filled in by the test-writer._

## Attempt log

_(created 2026-08-26 by the orchestrator after the owner's Play test showed an invisible UI.)_

## Handoff notes

- Pin mechanically walkable properties: `GetComponentsInChildren<Text>(true)` → font,
  fontSize, rect; `GetComponentsInChildren<Button>(true)` → targetGraphic/raycastable
  Graphic. Layout pins should be regional/relative (anchors), not pixel-exact.
- The locked T21/T23/T26 tests must stay green — this ticket only adds presentation to the
  same objects.
- Art available in Resources (026 wiring): RedHollowArt/{button-normal,...}. The art/ui
  regen session may add more; do not block on it.
