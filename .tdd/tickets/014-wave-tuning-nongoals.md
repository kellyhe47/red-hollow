---
id: 014
title: Wave-table playtest tuning and non-goal guard
status: blocked
depends_on: [004, 011, 012]
touches: [unity/RedHollow/Assets/GameSim/WaveTable.cs, unity/RedHollow/Assets/Game/Config/]
iterations: 0
test_files: []
branch: ""
board_id: T-14
owns_requirements: [R-06, R-70, R-71, R-73]
grades_fixtures: []
---

## Scope

Playtest the wave table to a 25-35 minute session (R-19 is deliberately unfixtured config). Confirm the v1 non-goals hold: no PvP, no host migration, no mid-match join, no spectator beyond dead-hero cam, no cross-match meta-economy, no second map, no boss, no difficulty settings.

## Acceptance criteria

- [ ] a full match lands in the 25-35 minute window
- [ ] no non-goal feature shipped

## Test plan

_Filled in by the test-writer._

## Attempt log

- BLOCKED (environment, pre-run): Unity Editor is not installed on this machine and needs the owner's Unity account/licence. `unity/RedHollow/` currently holds only `Assets/GameSim` — there is no Unity project (no ProjectSettings/, no Packages/). T-01..T-09 carry the entire 30-fixture acceptance contract and need no Unity.
