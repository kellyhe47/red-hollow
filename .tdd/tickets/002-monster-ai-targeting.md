---
id: 002
title: Monster roster + nearest-target AI + barricade blocking
status: in-progress
depends_on: [001]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Targeting.cs, sim/GameSim.Tests/T02_TargetingTests.cs]
iterations: 0
test_files: []
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

_Filled in by the test-writer._

## Attempt log

- wave A: test-writer dispatched in worktree .tdd/worktrees/002 (branch tdd/002).
