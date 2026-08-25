---
id: 008
title: Hero kits, abilities, cooldowns, status effects
status: in-progress
depends_on: [001, 007]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Abilities.cs, sim/GameSim.Tests/T08_AbilityTests.cs]
iterations: 0
test_files: []
branch: "tdd/008"
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
- [ ] R-32: Q and E cooldowns (8s / 20s) are enforced - a cast while on cooldown is rejected and changes nothing
- [ ] R-32: ability ranks cap at 3 and each rank improves the ability's numbers by ~25%
- [ ] R-31: every class Q/E resolves through the sim - Gunslinger Fan the Hammer / Deadeye, Rancher Lasso / Stampede, Sawbones Whirl / Bulwark (60% DR for 2s) - and the class passives apply (Gunslinger every-4th-basic crit x2, Rancher basics hit up to 2 targets)

## Test plan

_Filled in by the test-writer._

## Attempt log

- CRITERIA AMENDED pre-dispatch (DEC-RUN-4 audit): requirements this ticket owns that had
  neither a fixture nor an acceptance criterion, and would have shipped unimplemented.
- wave B: test-writer dispatched in worktree .tdd/worktrees/008 (branch tdd/008).
