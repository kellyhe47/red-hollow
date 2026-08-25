---
id: 015
title: Monster attack cadence (R-18)
status: in-progress
depends_on: [002, 003, 007]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Combat.cs, sim/GameSim.Tests/T15_CadenceTests.cs]
iterations: 1
test_files: [sim/GameSim.Tests/T15_CadenceTests.cs]
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

`T15_CadenceTests.cs` — 15 cases. Frame-loop lands one hit per configured second; inclusive
boundary at -0.001/0.0/+0.001 across intervals 0.25 and 2.5; per-monster interleaving; never-attacked
may attack immediately (the fixture-safety property); a permitted attack adds nothing observable
(rebuilds G-006 and pins exactly 1 change / 1 event / 0 external calls); sad paths unpinned.

## Attempt log

- Opened by the Phase 3 requirement walk: R-18 was owned by green ticket T-02 but cited in
  zero tests, and its config knob was read nowhere in GameSim. Sixth instance of the
  fixture-grades-a-neighbouring-behaviour pattern.
- tests locked @ HEAD: 15 red, 30 golden fixtures still 30/30.
- Test-writer ran a MUTATION CHECK before handing over: correct impl -> 319/319; exclusive boundary
  -> 4 fail; hardcoded 1.0 -> 3 fail; shared timer -> per-monster fails; fail-closed first attack
  -> 4 fail incl. both fixture-safety tests; no gate -> 4 fail. The tests provably bite.
- KNOWN LIMIT: the gate is advisory. Nothing stops a host calling a damage op without asking.
  The Unity combat-loop wiring (blocked ticket 010/011) MUST call it first — recorded there.
