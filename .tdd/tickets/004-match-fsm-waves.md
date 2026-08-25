---
id: 004
title: Match FSM, wave lifecycle, kills, bounty, victory
status: pending
depends_on: [001, 003]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Waves.cs, unity/RedHollow/Assets/GameSim/WaveTable.cs, sim/GameSim.Tests/T04_WaveTests.cs]
iterations: 0
test_files: []
branch: ""
board_id: T-04
owns_requirements: [R-01, R-02, R-03, R-04, R-05, R-14, R-19]
grades_fixtures: [G-010, G-011, G-012, G-016, G-017]
---

## Scope

record_monster_kill (bounty to shared pool, wave complete on last kill, victory on wave 10), begin_planning_phase (scrip carryover, 60s), set_player_ready (all connected ready -> combat early). First-pass wave table config (R-19 deliberately unfixtured).

## Acceptance criteria

- [ ] G-010, G-011, G-012, G-016, G-017 pass
- [ ] lobby -> (planning -> combat) x10 -> victory with combat -> defeat edge in every wave
- [ ] wave table is config, tunable without code change

## Test plan

_Filled in by the test-writer._

## Attempt log

