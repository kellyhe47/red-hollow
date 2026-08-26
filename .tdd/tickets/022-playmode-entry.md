---
id: 022
title: Play-mode entry point — the scene actually boots the shell
status: pending
depends_on: [021]
touches: [unity/RedHollow/Assets/Game/UI/, unity/RedHollow/Assets/Scenes/]
iterations: 0
test_files: []
branch: ""
board_id: T-22
owns_requirements: []
grades_fixtures: []
---

## Scope

Found by the owner pressing Play (2026-08-26): `RedHollow.unity` contains **zero
MonoBehaviours** — nothing runs. `ShellBootstrap` (021) is only ever constructed by tests.
Adds a thin scene-resident entry behaviour that constructs `ShellBootstrap` on load, pumps
it every frame with the frame delta, feeds `HeroInput` from real devices, ensures a camera
and `EventSystem` exist, and is actually serialized into the committed scene asset.

## Acceptance criteria

- [ ] the committed scene contains the entry behaviour; pressing Play boots the shell
      (title screen visible; hosting a solo match reaches combat with world + HUD)
- [ ] the entry behaviour is a thin pump — logic stays in `ShellBootstrap` (Cecil invariant)
- [ ] input reaches the hero (WASD + mouse aim + SPACE/Q/E) in play mode

## Test plan

_Filled in by the test-writer._

## Attempt log

_(created 2026-08-26 by the orchestrator after the owner's Play test found the empty scene.)_

## Handoff notes

- Precedent for the thin-pump shape: `MatchHostBehaviour` (Game/Host) — owns one plain-C#
  object, pumps it, holds no rule. T10's Cecil scan enforces this mechanically.
- Input seam exists: `HeroInput`/`IInputSource` (Game/Input) — device reads belong behind
  `IInputSource`; a Unity-device implementation is part of this ticket if none exists.
- Scene edits are EditMode-testable: load the scene asset via
  `UnityEditor.SceneManagement.EditorSceneManager.OpenScene` and inspect components; the
  entry behaviour's lifecycle can be driven reflectively (Awake/Update) headlessly.
- `activeInputHandler: 2` (both backends) — old `Input.GetKey` works, Input System 1.20
  also installed.
