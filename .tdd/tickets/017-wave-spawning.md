---
id: 017
title: Wave spawning — turn the wave table into live monsters
status: green
depends_on: [004, 002]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Spawning.cs, sim/GameSim.Tests/T17_SpawningTests.cs]
iterations: 1
test_files: [sim/GameSim.Tests/T17_SpawningTests.cs]
branch: ""
board_id: T-17
owns_requirements: []
grades_fixtures: []
---

## Scope

Found while scoping 016. **Nothing in the sim creates a `Monster`.** `WaveTable` has the
composition, `MonsterCatalog` the stats, `ColonyMap.EntryTunnels` the positions — every
ingredient exists, but nothing assembles them, so a match can never actually contain a
monster and no wave can be fought.

Seventh instance of the fixture-grades-a-neighbouring-behaviour pattern: G-010/011/012
grade what happens when a monster *dies*, and T-04's criteria covered the wave table as
*config*, so nothing ever required turning that table into live entities.

Sim-side, not shell-side: how many monsters of what type exist is a game rule, and ticket
010's invariant forbids a MonoBehaviour authoring sim entities.

## Acceptance criteria

- [x] spawning wave N creates exactly the monsters its `WaveSpec` describes
- [x] each spawned monster carries its R-17 catalog stats - hp, speed, damage - not invented numbers
- [x] monsters are placed at the entry tunnels the wave table marks active for that wave (R-14)
- [x] spawned ids are unique and land in `WaveState.LivingMonsterIds` so wave completion works
- [x] spawning is deterministic - same wave and seed yields the same result (R-54)
- [x] the existing 30 golden fixtures still pass unchanged

## Test plan

`T17_SpawningTests.cs` — 19 cases covering composition, catalog-not-constants (from both
sides), R-14 tunnel membership, id uniqueness within and across spawns, a full spawned wave
cleared kill-by-kill to WaveComplete, R-54 determinism via ordered result ids, and three sad paths.

## Attempt log
- iter 1 GREEN @ 8533ad4: full suite 338/338, fixtures 30/30, zero NotYet call sites.
- All-or-nothing partial spawn; per-match id counter; round-robin breaches keyed on whole-wave index.
- NOTE: MatchSim.cs:85 still declares the now-unreferenced NotYet helper. Harmless, one-line cleanup.
