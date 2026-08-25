---
id: 019
title: Wire the shell to a running match — the playable bootstrap
status: pending
depends_on: [010, 016, 017, 018]
touches: [unity/RedHollow/Assets/Game/Host/, unity/RedHollow/Assets/Game/View/, unity/RedHollow/Assets/Tests/EditMode/T19_BootstrapTests.cs]
iterations: 0
test_files: []
branch: ""
board_id: T-19
owns_requirements: []
grades_fixtures: []
---

## Scope

Every piece of a playable session now exists and **nothing connects them**:
- `HostLoop.Step` does not call `TickMonsterMovement` or `MoveHero`. T-10's tick test derives
  its required set from `MatchSim`'s **parameterless** `Tick*` methods, and both of these take
  arguments, so they fell outside the net.
- Nothing calls `SpawnWave`, so a running match has no monsters.
- No view is bound to a live entity, so nothing renders what the sim is doing.

This is the ticket that turns 356 green sim tests into something you can watch.

## Acceptance criteria

- [ ] the host loop advances monster movement every step
- [ ] a resolved move intent reaches the hero through `MoveHero`
- [ ] starting a match spawns wave 1 and each cleared wave spawns the next
- [ ] views appear for spawned entities and are released when they die
- [ ] a driven session reaches defeat when monsters are left to reach the shelters
- [ ] no game rule enters a MonoBehaviour — ticket 010's Cecil invariant stays green

## Test plan

_Filled in by the test-writer._

## Attempt log
