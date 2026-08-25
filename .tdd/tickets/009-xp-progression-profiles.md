---
id: 009
title: XP, leveling, skill points, persistent profiles
status: pending
depends_on: [001]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Progression.cs, unity/RedHollow/Assets/GameSim/SqliteProfileStore.cs, sim/GameSim.Tests/T09_ProgressionTests.cs]
iterations: 0
test_files: []
branch: ""
board_id: T-09
owns_requirements: [R-40, R-41, R-42, R-43, R-44]
grades_fixtures: [G-023, G-024, G-025, G-026]
---

## Scope

award_kill_xp (XP = bounty to the credited player; turret kills credit the placer; lifetime XP never decreases; level L threshold 100*L*(L-1)/2; one skill point per level gained) and spend_skill_point (unlock Q / unlock E / rank up, max rank 3; reject with no_skill_points). Injected IProfileStore; saves at each level-up and match end.

## Acceptance criteria

- [ ] G-023, G-024, G-025, G-026 pass
- [ ] profile_store.save external call fires exactly where fixtures pin it
- [ ] fixture tests use a fixture-backed fake store; production uses server-local SQLite/JSON keyed by callsign

## Test plan

_Filled in by the test-writer._

## Attempt log

