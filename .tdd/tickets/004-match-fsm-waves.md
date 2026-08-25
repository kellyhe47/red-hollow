---
id: 004
title: Match FSM, wave lifecycle, kills, bounty, victory
status: in-progress
depends_on: [001, 003]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Waves.cs, unity/RedHollow/Assets/GameSim/WaveTable.cs, sim/GameSim.Tests/T04_WaveTests.cs]
iterations: 0
test_files: []
branch: "tdd/004"
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
- [ ] R-04: the sim exposes wave-complete interstitial data (bounty earned this wave, civilians remaining) and planning auto-advances after it
- [ ] R-05: the sim exposes which entry points activate next wave WITHOUT exposing monster types or counts
- [ ] R-14: which subset of the 4 fixed entry tunnels is active varies per wave via the wave table

## Test plan

_Filled in by the test-writer._

## Attempt log

- CRITERIA AMENDED pre-dispatch (DEC-RUN-4 audit): requirements this ticket owns that had
  neither a fixture nor an acceptance criterion, and would have shipped unimplemented.
- wave B: test-writer dispatched in worktree .tdd/worktrees/004 (branch tdd/004).
