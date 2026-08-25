---
id: 006
title: Placeable combat effects
status: in-progress
depends_on: [001, 005]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Placeables.cs, sim/GameSim.Tests/T06_PlaceableTests.cs]
iterations: 1
test_files: [sim/GameSim.Tests/T06_PlaceableTests.cs]
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
- [ ] R-23/R-16: a barricade takes damage and is destroyed at 0 HP, releasing the path block - today NOTHING damages a placeable, so a targeted barricade is immortal and blocks forever
- [ ] R-23: Med Station heals heroes 5 HP/s within radius 5 - today only the string constant exists

## Test plan

`T06_PlaceableTests.cs` — 33 cases. Trap countdown as a rule; turret inclusive range, deterministic
tie, empty sky, never-hero-never-hotspot; blast boundary and living-only; no friendly fire from a
blast; R-23 effect table as config; barricade damage/destruction incl. the real-SelectTarget
retarget integration; Med Station heal, radius, MaxHp cap, dead-hero exclusion, stacking direction,
destroyed-station no-op; sad paths. G-027/028/029 not re-encoded.

## Attempt log

- CRITERIA AMENDED pre-dispatch (DEC-RUN-4 audit): requirements this ticket owns that had
  neither a fixture nor an acceptance criterion, and would have shipped unimplemented.
- tests locked @ HEAD: 33 cases, all red, none passing. Seams added: ApplyPlaceableDamage
  (shaped after HotspotAttackRequest/HeroDamageRequest) and TickMedStations (void tick).
