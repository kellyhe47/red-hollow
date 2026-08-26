---
id: 026
title: Placeable views — placed defenses render and show damage
status: pending
depends_on: [025]
touches: [unity/RedHollow/Assets/Game/View/, unity/RedHollow/Assets/Game/UI/]
iterations: 0
test_files: []
branch: ""
board_id: T-26
owns_requirements: []
grades_fixtures: []
---

## Scope

Final render audit (2026-08-26): `MatchViewBinder` binds hero and monster views;
`MatchSceneBuilder` places hotspot/spawn markers; **nothing renders a placeable**. A
purchased barricade/trap/turret/med station is invisible, and sells/breaks/destroys change
nothing on screen. Wireframe S3: "Existing placeables shown"; S4: "Barricades show HP bars
when damaged".

Adds placeable view binding through the existing resolver seam:

- Create a view when a placeable exists (state-driven, like monsters — read how the binder
  tracks monster appear/despawn and follow it).
- Remove on sold / broken / destroyed (`Exists` flip; the distinct `placeable_broken` vs
  `placeable_destroyed` events stay distinct for the feel layer — already wired in 013).
- Damage readout for barricades driven by sim state (Hp vs full) — presentation shape
  (scale/color/bar) is free; the *presence when damaged* is the wireframe requirement.

## Acceptance criteria

- [ ] a purchased placeable appears at its position through the resolver seam
- [ ] sold, broken and destroyed placeables disappear
- [ ] a damaged barricade shows a damage readout driven by sim state; Cecil invariant holds

## Test plan

_Filled in by the test-writer._

## Attempt log

_(created 2026-08-26 by the orchestrator at 025 close.)_

## Handoff notes

- `VisualClass.Placeable` already exists on the resolver seam; art keys can follow the
  placeable type string (catalog icons exist under Assets/Game/Art for one representative).
- Binder precedent: monster views appear/despawn from state each refresh (T16/T19/T21 pin
  the pattern) — placeables should ride the same refresh, not a new pump hook.
- `Placeable` entity carries `Exists`, `Hp`, `PurchaseCost`, `OwnerPlayerId`, position;
  full HP for the bar denominator comes from the R-23 catalog.
