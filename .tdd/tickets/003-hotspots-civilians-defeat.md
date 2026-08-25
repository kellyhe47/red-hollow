---
id: 003
title: Colony, hotspots, civilian pool, defeat rule
status: in-progress
depends_on: [001]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Hotspots.cs, sim/GameSim.Tests/T03_HotspotTests.cs]
iterations: 0
test_files: []
branch: "tdd/003"
board_id: T-03
owns_requirements: [R-10, R-11, R-12, R-13, R-72]
grades_fixtures: [G-006, G-007, G-008, G-009]
---

## Scope

apply_hotspot_attack: ceil(damage/10) civilians killed, clamped at 0; hotspot_emptied event; defeat exactly when total civilians across all hotspots reaches 0; emptied hotspots stay lost and stop being valid targets.

## Acceptance criteria

- [ ] G-006..G-009 pass
- [ ] 3 hotspots 8/6/6 = 20 civilians in map config
- [ ] no civilian agent simulation

## Test plan

_Filled in by the test-writer._

## Attempt log

- wave A: test-writer dispatched in worktree .tdd/worktrees/003 (branch tdd/003).
