---
id: 004
title: Match FSM, wave lifecycle, kills, bounty, victory
status: green
depends_on: [001, 003]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Waves.cs, unity/RedHollow/Assets/GameSim/WaveTable.cs, sim/GameSim.Tests/T04_WaveTests.cs]
iterations: 1
test_files: [sim/GameSim.Tests/T04_WaveTests.cs]
branch: "tdd/004"
board_id: T-04
owns_requirements: [R-01, R-02, R-03, R-04, R-05, R-14, R-19]
grades_fixtures: [G-010, G-011, G-012, G-016, G-017]
---

## Scope

record_monster_kill (bounty to shared pool, wave complete on last kill, victory on wave 10), begin_planning_phase (scrip carryover, 60s), set_player_ready (all connected ready -> combat early). First-pass wave table config (R-19 deliberately unfixtured).

## Acceptance criteria

- [x] G-010, G-011, G-012, G-016, G-017 pass
- [x] lobby -> (planning -> combat) x10 -> victory with combat -> defeat edge in every wave
- [x] wave table is config, tunable without code change
- [x] R-04: the sim exposes wave-complete interstitial data (bounty earned this wave, civilians remaining) and planning auto-advances after it
- [x] R-05: the sim exposes which entry points activate next wave WITHOUT exposing monster types or counts
- [x] R-14: which subset of the 4 fixed entry tunnels is active varies per wave via the wave table

## Test plan

`T04_WaveTests.cs` — 50 cases. FSM as a machine (victory keyed to configured final wave, not a
literal 10; defeat edge from every wave; finished stays finished); R-19 table as config with
structural/monotonic assertions rather than pinned composition; R-14 varying tunnel subset;
R-04 bounty-earned-this-wave vs last-kill vs pool disambiguated by construction; R-05 recursive
leak walk over typed surface AND ToFields(); DEC-RUN-6 planning timer (inclusive boundary,
config-driven, inert outside planning, starts combat once); DEC-RUN-5 state-vs-config TotalWaves.
G-010/011/012/016/017 not re-encoded.

## Attempt log

- CRITERIA AMENDED pre-dispatch (DEC-RUN-4 audit): requirements this ticket owns that had
  neither a fixture nor an acceptance criterion, and would have shipped unimplemented.
- wave B: test-writer dispatched in worktree .tdd/worktrees/004 (branch tdd/004).
- tests locked on tdd/004 @ 9f92c54: 50 cases, all red; 132 passing tests unchanged.
- iter 1: implementer dispatched.
- iter 1 GREEN in worktree: 9 failed / 187 passed / 196 total, exactly the target. Zero T-04 stubs.
  Locked tests untouched.
- MERGED to main @ 48c1100. Post-merge full suite: 9 failed / 187 passed / 196 total; 21/30 fixtures
  green. Only T-05x4 T-06x3 T-08x2 remain. validate-spec and coverage green.
