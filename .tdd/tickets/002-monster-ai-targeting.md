---
id: 002
title: Monster roster + nearest-target AI + barricade blocking
status: awaiting-merge
depends_on: [001]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Targeting.cs, sim/GameSim.Tests/T02_TargetingTests.cs]
iterations: 1
test_files: [sim/GameSim.Tests/T02_TargetingTests.cs]
branch: "tdd/002"
board_id: T-02
owns_requirements: [R-16, R-17, R-18]
grades_fixtures: [G-001, G-002, G-003, G-004, G-005]
---

## Scope

select_target: nearest of {living hero, hotspot with >=1 civilian} by straight-line distance, lowest-entity-id tiebreak; barricade on the path becomes the target; Burrower ignores barricades and heroes. Roster stats data-configured.

## Acceptance criteria

- [ ] G-001..G-005 pass
- [ ] monster stats live in config, not code
- [ ] B-003 Burrower carve-out beats B-001/B-002 per PRD precedence

## Test plan

`T02_TargetingTests.cs` — 12 tests. R-17 roster: `Configured_roster_matches_the_R17_table`
(5 parametrized rows), `Roster_holds_exactly_the_five_R17_archetypes`,
`Roster_stats_are_overridable_per_config_instance`. B-003 carve-out:
`Burrower_takes_the_nearest_populated_hotspot_over_a_nearer_hero_or_empty_hotspot`,
`Same_arrangement_targets_differently_by_monster_type` (burrower + shambler control).
Sad paths: no-available-target, unknown-monster-id. G-001..005 not re-encoded.

## Attempt log

- wave A: test-writer dispatched in worktree .tdd/worktrees/002 (branch tdd/002).
- tests locked on tdd/002 @ 60cf5fc: 12 tests, 0 passing (roster rows throw KeyNotFoundException,
  targeting rows throw NotImplementedException T-02). Orchestrator-verified.
- DEC-RUN-1 resolved the roster-defaults conflict the test-writer raised; tests stand as written.
- iter 1: implementer dispatched in worktree .tdd/worktrees/002.
- iter 1 GREEN in worktree: 25 failed / 36 passed / 61 total, exactly the target. Locked tests
  untouched. G-001..005 pass; remaining 25 fixtures keep their original owning-ticket tags.
  Verified: oracle-based blocking (no position scan), ordinal tiebreak. Checkpoint 91d96eb.
