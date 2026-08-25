---
id: 019
title: Wire the shell to a running match — the playable bootstrap
status: green
depends_on: [010, 016, 017, 018]
touches: [unity/RedHollow/Assets/Game/Host/, unity/RedHollow/Assets/Game/View/, unity/RedHollow/Assets/Tests/EditMode/T19_BootstrapTests.cs]
iterations: 1
test_files: [unity/RedHollow/Assets/Tests/EditMode/T19_BootstrapTests.cs]
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

- [x] the host loop advances monster movement every step
- [x] a resolved move intent reaches the hero through `MoveHero`
- [x] starting a match spawns wave 1 and each cleared wave spawns the next
- [x] views appear for spawned entities and are released when they die
- [x] a driven session reaches defeat when monsters are left to reach the shelters
- [x] no game rule enters a MonoBehaviour — ticket 010's Cecil invariant stays green

## Test plan

`T19_BootstrapTests.cs` — 8 EditMode cases: movement driven every step, intent reaching MoveHero
with the direction intact, wave 1 on start, next wave after a clear, no eleventh wave, view
lifecycle following the world, and the end-to-end defeat run.

## Attempt log
- iter 1 GREEN @ fb2b283: EditMode 61/61, Cecil invariant green, dotnet sim 356/356.
- Defeat reached in 33.9 sim-seconds vs the 90s cap.
- ISimHost deliberately NOT widened; IMatchSimHost derives from it so T10 keeps compiling.
