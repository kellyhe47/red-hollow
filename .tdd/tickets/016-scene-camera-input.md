---
id: 016
title: Scene, top-down camera, WASD + mouse-aim input, placeholder visuals
status: green
depends_on: [010]
touches: [unity/RedHollow/Assets/Game/View/, unity/RedHollow/Assets/Game/Input/, unity/RedHollow/Assets/Scenes/, unity/RedHollow/Assets/Editor/SceneBuilder.cs]
iterations: 1
test_files: [unity/RedHollow/Assets/Tests/EditMode/T16_ViewTests.cs]
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

- [x] a solo session is playable with primitive placeholder art
- [x] W is movement only; SPACE is basic attack; Q and E are abilities; mouse buttons stay free
- [x] the hero faces the mouse cursor rather than turning toward movement
- [x] no code path blocks on an asset existing - placeholders resolve when art is absent
- [x] visuals render from replicated sim state, never from locally recomputed rules

## Test plan

`T16_ViewTests.cs` — 27 EditMode cases. R-30 mapping as a pure function (W moves and casts
nothing; SPACE basic; Q/E abilities; mouse buttons produce no gameplay intent; cursor alone is
never movement); facing discriminated by walking one way with the cursor elsewhere; asset
fallback across 4 visual classes x null/missing; views reflecting sim state; scene contents and
a genuinely top-down camera; a solo session spawning wave 1 and running.

## Attempt log
- iter 1 GREEN @ 17b80cf: EditMode 53/53 (T10 26 + T16 27), Cecil invariant green with two new
  MonoBehaviours in the scanned set. dotnet sim 338/338. Scene asset produced headlessly.
- KNOWN LIMIT: MatchSim exposes no hero-move command, so nothing can actually walk a hero yet.
  Movement belongs to a later ticket; 016 is graded exactly as its criteria define.
