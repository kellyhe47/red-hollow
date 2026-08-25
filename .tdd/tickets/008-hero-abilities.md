---
id: 008
title: Hero kits, abilities, cooldowns, status effects
status: green
depends_on: [001, 007]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Abilities.cs, sim/GameSim.Tests/T08_AbilityTests.cs]
iterations: 1
test_files: [sim/GameSim.Tests/T08_AbilityTests.cs]
branch: "tdd/008"
board_id: T-08
owns_requirements: [R-31, R-32]
grades_fixtures: [G-018, G-019]
---

## Scope

apply_ability (Rancher lasso: 50% slow for exactly 3.0s, expires_at recorded) and tick_status_effects (expiry at >= expires_at restores base speed). Three class kits with Q 8s / E 20s cooldowns, ranks 1-3 at ~+25%/rank, Q/E gated on unlocks from the account profile.

## Acceptance criteria

- [x] G-018, G-019 pass
- [x] kit numbers config-tunable
- [x] heroes start a match with saved ability allocations (R-31 + R-43)
- [x] R-32: Q and E cooldowns (8s / 20s) are enforced - a cast while on cooldown is rejected and changes nothing
- [x] R-32: ability ranks cap at 3 and each rank improves the ability's numbers by ~25%
- [x] R-31: every class Q/E resolves through the sim - Gunslinger Fan the Hammer / Deadeye, Rancher Lasso / Stampede, Sawbones Whirl / Bulwark (60% DR for 2s) - and the class passives apply (Gunslinger every-4th-basic crit x2, Rancher basics hit up to 2 targets)

## Test plan

`T08_AbilityTests.cs` — 46 cases. Kit table as config; saved allocations at match start;
cooldown enforcement incl. inert rejection, per-slot and per-hero independence, inclusive
ready boundary; rank cap and ~+25% as ratio ranges; all six abilities; both remaining passives;
sad paths. Cooldowns 4.0/11.5s, lasso 0.25/4.5s, Bulwark 2.5s — none matching the PRD or
fixture constants, so nothing passes against a hardcoded value. G-018/019 not re-encoded.

## Attempt log

- CRITERIA AMENDED pre-dispatch (DEC-RUN-4 audit): requirements this ticket owns that had
  neither a fixture nor an acceptance criterion, and would have shipped unimplemented.
- wave B: test-writer dispatched in worktree .tdd/worktrees/008 (branch tdd/008).
- iter 1 GREEN in worktree: 12 failed / 180 passed / 192 total, exactly the target. Zero T-08 stubs.
  Locked tests untouched. T07 37/37 and G-018/019/020/021/030 all still pass.
- DEVIATION (declared by the implementer, accepted): the brief allowed edits to ticket 007s
  MatchSim.Heroes.cs only to hook the two passives, but Bulwark is a hero-side timed reduction
  and IncomingDamageFor is the only seam through which it can reach incoming damage. A 5-line
  edit delegates to AfterTimedDamageReduction; the Sawbones branch is structurally intact and
  G-020 returns identity when no status effects are present. Verified: no 007 regression.
- MERGED to main @ 26b28d9. Post-merge full suite with 004: 7 failed / 235 passed / 242 total,
  23/30 fixtures green. Only T-05x4 T-06x3 remain. validate-spec, coverage, R-51 build all green.
