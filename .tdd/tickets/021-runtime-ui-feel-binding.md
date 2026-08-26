---
id: 021
title: Runtime binding — UI screens, feel router and art resolver into the playable shell
status: pending
depends_on: [012, 013, 019]
touches: [unity/RedHollow/Assets/Game/UI/, unity/RedHollow/Assets/Game/View/]
iterations: 0
test_files: []
branch: ""
board_id: T-21
owns_requirements: []
grades_fixtures: []
---

## Scope

Found by the §5 audit (2026-08-25): 012's screen models, 013's `FeelRouter` and
`ArtVisualResolver` are implemented and locked-tested, but **nothing at runtime constructs
them**. No Canvas renders any wireframe screen; sim events never reach the feel layer;
`MatchViewBinder(visuals: null)` defaults to the placeholder resolver everywhere.
(`LanternDeepLighting` IS wired — `MatchSceneBuilder` applies it.)

Binds all three into the playable bootstrap so a launched build shows S1–S7, plays feel
effects, and resolves real art.

## Acceptance criteria

- [ ] a launched scene renders the UI screens through the 012 models — visible UI elements
      bound to model state, screen switching driven by `UiRouter`
- [ ] the host's sim event stream reaches `FeelRouter`; feel state applied to views each frame
- [ ] `MatchViewBinder` receives an `ArtVisualResolver` whose catalog registers the imported
      representative assets, chained over the placeholder
- [ ] the Cecil invariant still holds — UI/feel MonoBehaviours never write sim state

## Test plan

_Filled in by the test-writer._

## Attempt log

_(created 2026-08-25 by the handoff-2 orchestrator after the §5 recheck.)_
