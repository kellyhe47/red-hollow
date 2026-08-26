---
id: 023
title: Interactive UI controls — clickable screens over the 012 models
status: pending
depends_on: [022]
touches: [unity/RedHollow/Assets/Game/UI/]
iterations: 0
test_files: []
branch: ""
board_id: T-23
owns_requirements: []
grades_fixtures: []
---

## Scope

Found while closing 022: the shell UI has labels and screen roots but **zero interactive
controls** (no `Button`, no `InputField` anywhere). Play boots to a title screen nobody can
click through. Adds uGUI controls wired to the existing model actions:

- S1: callsign input, HOST GAME, join-code input + JOIN (inline error already modeled)
- S2: class pick buttons, READY
- S3: shop item clicks → ghost placement → click-to-place, placeable click → SELL,
  READY UP
- S4: level-up badge click + L hotkey → picker; picker choice cards → Spend; ESC overlay
  (leave match, volume)
- S6/S7: PLAY AGAIN / RETRY (host-only enable already modeled), MAIN MENU

All actions already exist on the 012 models — this is wiring, not logic.

## Acceptance criteria

- [ ] every wireframe control is clickable and routed to its model action
- [ ] a solo Play session drives title → host → lobby → ready → planning → combat entirely
      by mouse/keyboard
- [ ] controls stay thin — no logic beyond calling the model (Cecil invariant holds)

## Test plan

_Filled in by the test-writer._

## Attempt log

_(created 2026-08-26 by the orchestrator at 022 close.)_

## Handoff notes

- Model action surface: `TitleScreenModel` (callsign→profile, NoteJoinFailed),
  `LobbyScreenModel` (picks, ready, auto TryStartMatch), `PlanningScreenModel`
  (BeginPlacement/MoveGhost/ConfirmPlacement/CancelPlacement/Sell/SellRefundFor/ReadyUp),
  `CombatHudModel` (OpenPicker/ClosePicker/Spend), `PostMatchModel`
  (CanRematch/RequestRematch), `UiRouter` (ESC overlay flag via NetSession.SetOverlayOpen).
- ShellUi builds the label hierarchy; controls belong beside them under the same screen
  roots. EventSystem already ensured by GameEntryBehaviour (022).
- uGUI Button onClick with a closure calling a model method: the closure lives in a plain
  C# wiring class — MonoBehaviours never touch sim state (T10 scans compiler-generated
  closures too).
- EditMode can invoke Button.onClick.Invoke() directly — full click-path tests without
  play mode.
