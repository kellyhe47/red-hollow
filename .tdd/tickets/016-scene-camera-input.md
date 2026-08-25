---
id: 016
title: Scene, top-down camera, WASD + mouse-aim input, placeholder visuals
status: pending
depends_on: [010]
touches: [unity/RedHollow/Assets/Game/View/, unity/RedHollow/Assets/Game/Input/, unity/RedHollow/Assets/Scenes/, unity/RedHollow/Assets/Editor/SceneBuilder.cs]
iterations: 0
test_files: []
branch: ""
board_id: T-16
owns_requirements: [R-30]
grades_fixtures: []
---

## Scope

Split out of 010, which was too large for one session. The playable surface: a scene built
headlessly (camera, ground, spawn, hotspot markers), top-down camera, R-30 controls, and
primitive placeholder visuals driven off replicated sim state.

## Acceptance criteria

- [ ] a solo session is playable with primitive placeholder art
- [ ] W is movement only; SPACE is basic attack; Q and E are abilities; mouse buttons stay free
- [ ] the hero faces the mouse cursor rather than turning toward movement
- [ ] no code path blocks on an asset existing - placeholders resolve when art is absent
- [ ] visuals render from replicated sim state, never from locally recomputed rules

## Test plan

_Filled in by the test-writer._

## Attempt log
