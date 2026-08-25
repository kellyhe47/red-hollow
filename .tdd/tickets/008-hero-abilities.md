---
id: 008
title: Hero kits, abilities, cooldowns, status effects
status: pending
depends_on: [001, 007]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Abilities.cs, sim/GameSim.Tests/T08_AbilityTests.cs]
iterations: 0
test_files: []
branch: ""
board_id: T-08
owns_requirements: [R-31, R-32]
grades_fixtures: [G-018, G-019]
---

## Scope

apply_ability (Rancher lasso: 50% slow for exactly 3.0s, expires_at recorded) and tick_status_effects (expiry at >= expires_at restores base speed). Three class kits with Q 8s / E 20s cooldowns, ranks 1-3 at ~+25%/rank, Q/E gated on unlocks from the account profile.

## Acceptance criteria

- [ ] G-018, G-019 pass
- [ ] kit numbers config-tunable
- [ ] heroes start a match with saved ability allocations (R-31 + R-43)

## Test plan

_Filled in by the test-writer._

## Attempt log

