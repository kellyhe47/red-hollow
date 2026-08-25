---
id: 007
title: Hero damage, death/respawn, no friendly fire
status: in-progress
depends_on: [001]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Heroes.cs, sim/GameSim.Tests/T07_HeroTests.cs]
iterations: 0
test_files: []
branch: "tdd/007"
board_id: T-07
owns_requirements: [R-26, R-33, R-34, R-35, R-36]
grades_fixtures: [G-020, G-021, G-030]
---

## Scope

apply_hero_damage (Sawbones flat 30% DR, floor applied; 0 HP -> dies instantly, respawn_at = now + 10s, untargetable while dead), resolve_hero_attack (hero attacks pass through heroes and placeables, damage monsters only). No mana; out-of-combat regen 2 HP/s after 5s.

## Acceptance criteria

- [ ] G-020, G-021, G-030 pass
- [ ] all heroes dead is not defeat
- [ ] dead heroes excluded from monster target candidates (feeds T-02)

## Test plan

_Filled in by the test-writer._

## Attempt log

- wave A: test-writer dispatched in worktree .tdd/worktrees/007 (branch tdd/007).
