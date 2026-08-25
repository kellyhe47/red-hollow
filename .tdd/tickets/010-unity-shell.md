---
id: 010
title: Unity project shell: scene, top-down camera, input, sim host loop
status: blocked
depends_on: [001]
touches: [unity/RedHollow/ProjectSettings/, unity/RedHollow/Assets/Game/, unity/RedHollow/Packages/]
iterations: 0
test_files: []
branch: ""
board_id: T-10
owns_requirements: [R-30, R-50, R-52]
grades_fixtures: []
---

## Scope

Unity project that references GameSim via asmdef (noEngineReferences on GameSim mechanically enforces the invariant). Top-down camera, WASD move with W movement-only, hero faces cursor, SPACE basic, Q/E abilities; own-hero local prediction with host reconciliation; remote entity interpolation. MonoBehaviours send commands and render replicated state, never hold game rules.

## Acceptance criteria

- [ ] a playable solo session runs with primitive placeholder art
- [ ] no game rule appears in a MonoBehaviour
- [ ] GameSim asmdef has noEngineReferences: true

## Test plan

_Filled in by the test-writer._

## Attempt log

- BLOCKED (environment, pre-run): Unity Editor is not installed on this machine and needs the owner's Unity account/licence. `unity/RedHollow/` currently holds only `Assets/GameSim` — there is no Unity project (no ProjectSettings/, no Packages/). T-01..T-09 carry the entire 30-fixture acceptance contract and need no Unity.
