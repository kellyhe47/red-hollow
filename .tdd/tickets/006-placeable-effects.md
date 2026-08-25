---
id: 006
title: Placeable combat effects
status: pending
depends_on: [001, 005]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Placeables.cs, sim/GameSim.Tests/T06_PlaceableTests.cs]
iterations: 0
test_files: []
branch: ""
board_id: T-06
owns_requirements: [R-23]
grades_fixtures: [G-027, G-028, G-029]
---

## Scope

trigger_placeable (spike trap 30 dmg, 10 triggers then breaks; dynamite 150 AoE once then removed) and turret_tick (nearest living monster within range 8). Catalog numbers config-tunable, mechanics fixture-locked.

## Acceptance criteria

- [ ] G-027, G-028, G-029 pass
- [ ] dynamite hits every living monster inside blast radius
- [ ] turret ignores dead monsters and out-of-range monsters

## Test plan

_Filled in by the test-writer._

## Attempt log

