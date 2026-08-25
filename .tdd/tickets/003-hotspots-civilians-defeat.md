---
id: 003
title: Colony, hotspots, civilian pool, defeat rule
status: awaiting-merge
depends_on: [001]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Hotspots.cs, sim/GameSim.Tests/T03_HotspotTests.cs]
iterations: 1
test_files: [sim/GameSim.Tests/T03_HotspotTests.cs]
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

`T03_HotspotTests.cs` — 24 tests. R-10 map config (counts, sum-to-20, 4 tunnels + 1 spawn,
overridability) and the map->MatchState bridge; R-72 structural guards; R-11 clamping as a
9-row parametrized rule; R-12/R-13 emptied-stays-lost; colony-wide edge-exact defeat;
sad paths for unknown hotspot id and non-positive damage. G-006..009 not re-encoded.
Stub added: `unity/RedHollow/Assets/GameSim/ColonyMap.cs` (shape only).

## Attempt log

- wave A: test-writer dispatched in worktree .tdd/worktrees/003 (branch tdd/003).
- tests locked on tdd/003 @ 13075a6: 22 red / 2 passing. Orchestrator-verified the 2 passers are
  exactly the labelled structural R-72 guards (`Civilians_are_a_count...`,
  `MatchState_total_civilians_is_the_sum...`), which cannot be red without first introducing the
  violation they defend against. Every new-behaviour criterion has a genuinely red test.
- Open question deferred to 004: `ColonyMap.EntryTunnels` is `List<Vec2>`, but R-19's wave table
  selects *active* entry points per wave, which may need stable tunnel ids. 004 depends on 003 and
  branches after it merges, so extending ColonyMap then is safe and non-concurrent.
- Test-writer's reading accepted: `hotspot_emptied` is a transition event, not a level — not
  re-emitted when an already-empty hotspot is hit. No fixture covers this; consistent with R-13.
- iter 1: implementer dispatched in worktree .tdd/worktrees/003.
- iter 1 GREEN in worktree: 26 failed / 47 passed / 73 total, exactly the target. Locked tests
  untouched; both R-72 structural guards still pass. Verified it writes State.Status and records
  field "status", never Phase.
