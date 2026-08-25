---
id: 018
title: Movement — advance hero and monster positions over time
status: pending
depends_on: [017, 002, 008]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Movement.cs, sim/GameSim.Tests/T18_MovementTests.cs]
iterations: 0
test_files: []
branch: ""
board_id: T-18
owns_requirements: []
grades_fixtures: []
---

## Scope

Found while scoping 016. **Nothing in the sim advances a position over time.** Positions are
only ever *set* — at spawn, respawn, Stampede knockback, and placement. Verified by grep.

Consequences, all of them live bugs:
- monsters never walk to a hotspot, so they can never attack it, so **defeat is unreachable**
- `CurrentSpeed` is written at spawn and multiplied by the lasso, then read by nothing that
  moves anything — DEC-008's 50% slow currently affects nothing at all
- R-17's Speed column is inert

Eighth instance of the pattern: G-018 grades the slow being *applied* and G-019 grades it
*expiring*, so the fixtures bracket a behaviour that does not exist between them.

**The seam question this ticket answers:** the sim owns *how far* (speed x delta, honouring
slows and death); the shell owns *which way* (NavMesh, R-18). That mirrors `IPathOracle`,
which already lets the shell answer a geometry question while the sim keeps the rule — and it
keeps movement out of the shell, where ticket 010's Cecil invariant forbids it.

## Acceptance criteria

- [ ] a monster with a target closes distance to it at its `CurrentSpeed`
- [ ] a lassoed monster covers exactly half the ground of an unslowed one over the same interval (DEC-008)
- [ ] when the slow expires the monster returns to full pace (G-019 restores `CurrentSpeed`)
- [ ] a monster that reaches its target stops rather than overshooting or orbiting
- [ ] heroes move on a commanded direction at their configured speed (R-30)
- [ ] dead heroes and dead monsters do not move
- [ ] direction comes from an injected seam, so NavMesh pathing stays in the shell (R-18)
- [ ] the existing 30 golden fixtures still pass unchanged

## Test plan

_Filled in by the test-writer._

## Attempt log
