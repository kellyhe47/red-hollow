---
id: 023
title: Interactive UI controls — clickable screens over the 012 models
status: pending
depends_on: [022]
touches: [unity/RedHollow/Assets/Game/UI/]
iterations: 0
test_files:
  - unity/RedHollow/Assets/Tests/EditMode/T23_InteractiveControlsTests.cs
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

`T23_InteractiveControlsTests` (EditMode). Pinned contract — accessor convention is
**properties on `ShellControls`** via `ShellBootstrap.Controls`, plus model accessors
`ShellBootstrap.Title` / `.Lobby` / `.Planning` (like `.Hud`); screen-specific controls hang
UNDER their `ShellUi.ScreenRoot`, so R-60 activation shows/hides them. Pointer actions are
pinned at the wiring seam: `PointerAt(Vec2, zoneValid)` / `ClickGround(Vec2, zoneValid)` /
`ClickPlaceable(id)` — screen-ray → position/id resolution stays play-mode territory.
New `PlayerKey.L` / `PlayerKey.Escape` (UI keys; must produce no gameplay intent).

- S1: controls under Title root; typing callsign loads the seeded profile; HOST click seats
  the typed callsign as host and lands S2; failed JOIN raises the modeled inline error on
  `JoinErrorLabel`, stays S1, and editing the code clears it.
- S2: three PICK buttons + READY under Lobby root; pick writes the seat (re-pick allowed);
  READY alone auto-starts the solo match through the model's all-ready rule on the next pump.
- S3: one shop button per catalog row, `interactable == Affordable` (scrip engineered so both
  states exist); shop click → ghost, `ClickGround(valid)` → one catalog-priced placement at
  the click, ghost cleared; invalid-zone click → nothing placed/charged, ghost stays,
  rejection surfaced; `ClickPlaceable` → sell for exactly `SellRefundFor`; READY UP → combat.
- S4: badge visible == `SkillPointBadge`, click → `OpenPicker`; picker card buttons aligned
  with `PickerChoices`, click spends the point (profile proves it); held `L` through the
  shell's `IInputSource` opens the picker; held `Escape` opens the overlay
  (`SetOverlayOpen(true)`, root visible, sim keeps running), close button → false; LEAVE →
  local-peer `Disconnect` (solo host ⇒ session Ended, back to S1, overlay closed).
- S6/S7: rematch button per screen, `interactable == CanRematch`, click → lobby with same
  code and picks, match discarded; MAIN MENU pinned at honest minimum: S1 shown (see notes).
- Flow AC: title → host → pick → ready → live match through control invocations + pumps only.
- Thinness: `ShellControls` is plain C# in the scanned shell assembly.

**Flagged ambiguities (not invented, not pinned):**
- MAIN MENU: `NetSession` has no clean return-to-title — only `Disconnect(localPeer)`, which
  (host) ends the session, paints DEC-RUN-10's *"host left"* title error, and leaves the
  session in `Ended`, where `StartHost` THROWS (R-50 guard). So after MAIN MENU / LEAVE the
  player can never host again on this shell. Tests pin only "S1 shown"; the re-host gap
  needs an owner/implementer decision (session reset or a fresh session per host).
- Callsign vs `ShellBootstrapOptions.LocalAccountId`: HUD/hero binding key off the OPTIONS
  account; the wiring hosts as the TYPED callsign. Tests keep them equal; behavior when they
  differ is unpinned.
- Join success needs a second endpoint (transport territory); only the modeled failure path
  is EditMode-reachable and pinned.

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
