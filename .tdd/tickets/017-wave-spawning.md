---
id: 017
title: Wave spawning — turn the wave table into live monsters
status: pending
depends_on: [004, 002]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Spawning.cs, sim/GameSim.Tests/T17_SpawningTests.cs]
iterations: 0
test_files: []
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

- [ ] spawning wave N creates exactly the monsters its `WaveSpec` describes
- [ ] each spawned monster carries its R-17 catalog stats - hp, speed, damage - not invented numbers
- [ ] monsters are placed at the entry tunnels the wave table marks active for that wave (R-14)
- [ ] spawned ids are unique and land in `WaveState.LivingMonsterIds` so wave completion works
- [ ] spawning is deterministic - same wave and seed yields the same result (R-54)
- [ ] the existing 30 golden fixtures still pass unchanged

## Test plan

_Filled in by the test-writer._

## Attempt log
