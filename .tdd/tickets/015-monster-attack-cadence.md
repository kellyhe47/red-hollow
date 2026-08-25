---
id: 015
title: Monster attack cadence (R-18)
status: pending
depends_on: [002, 003, 007]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Combat.cs, sim/GameSim.Tests/T15_CadenceTests.cs]
iterations: 0
test_files: []
branch: ""
board_id: T-15
owns_requirements: [R-18]
grades_fixtures: []
---

## Scope

Found by the Phase 3 PRD walk, not by any fixture. R-18 says *"Monsters attack once per
second"*, and `SimConfig.MonsterAttackIntervalSeconds = 1.0` exists — but **nothing in
`GameSim` reads it**; only the test `ScenarioLoader` writes it. Nothing rate-limits how
often a monster lands a hit on a hotspot, a hero or a barricade, so a host loop running
at 60fps would apply 60x the intended damage.

R-18's other half — "movement uses NavMesh paths (Burrower path ignores barricade
obstacles)" — is Unity shell work and stays with the blocked tickets. The Burrower's
barricade carve-out is already implemented and green in ticket 002 at the targeting level.

## Acceptance criteria

- [ ] a monster that attacked cannot land another hit before `MonsterAttackIntervalSeconds` has elapsed
- [ ] the interval is read from config, not a constant
- [ ] the boundary is inclusive, matching the G-019 convention followed repo-wide
- [ ] cadence is per monster - one monster's attack does not gate another's
- [ ] the existing 30 golden fixtures still pass unchanged

## Test plan

_Filled in by the test-writer._

## Attempt log

- Opened by the Phase 3 requirement walk: R-18 was owned by green ticket T-02 but cited in
  zero tests, and its config knob was read nowhere in GameSim. Sixth instance of the
  fixture-grades-a-neighbouring-behaviour pattern.
