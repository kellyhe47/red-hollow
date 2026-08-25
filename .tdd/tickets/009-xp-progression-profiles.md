---
id: 009
title: XP, leveling, skill points, persistent profiles
status: green
depends_on: [001]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Progression.cs, unity/RedHollow/Assets/GameSim/SqliteProfileStore.cs, sim/GameSim.Tests/T09_ProgressionTests.cs]
iterations: 1
test_files: [sim/GameSim.Tests/T09_ProgressionTests.cs]
branch: "tdd/009"
board_id: T-09
owns_requirements: [R-40, R-41, R-42, R-43, R-44]
grades_fixtures: [G-023, G-024, G-025, G-026]
---

## Scope

award_kill_xp (XP = bounty to the credited player; turret kills credit the placer; lifetime XP never decreases; level L threshold 100*L*(L-1)/2; one skill point per level gained) and spend_skill_point (unlock Q / unlock E / rank up, max rank 3; reject with no_skill_points). Injected IProfileStore; saves at each level-up and match end.

## Acceptance criteria

- [x] G-023, G-024, G-025, G-026 pass
- [x] profile_store.save external call fires exactly where fixtures pin it
- [x] fixture tests use a fixture-backed fake store; production uses server-local SQLite/JSON keyed by callsign

## Test plan

`T09_ProgressionTests.cs` — 24 cases in two fixtures. Save timing as a rule via a recording
fake (non-levelling kills -> 0 writes, threshold crossing -> 1, multi-level -> [1,3], rejected
spend -> 0, match end -> one per account); level curve incl. landing exactly on 100/300/600/1000
and one XP short; R-41 monotonicity across a match boundary; R-42 free-choice order, rank
ceiling, banking; R-40 turret credit; JsonProfileStore round-trip, callsign isolation, unknown
callsign, and persistence across two store instances. G-023..026 not re-encoded.
Stubs added: `JsonProfileStore.cs`, `MatchSim.SaveProfilesAtMatchEnd()`.

## Attempt log

- wave A: test-writer dispatched in worktree .tdd/worktrees/009 (branch tdd/009).
- tests locked on tdd/009 @ a534bca: 24 cases, all red, none passing. Orchestrator-verified
  in-worktree: 73 total, 54 failed (30 golden with original owning-ticket tags + 24 T09), 19 passed.
- First attempt left the whole test assembly non-compiling (no match-end seam existed anywhere in
  MatchSim). My prompt had put all MatchSim*.cs off-limits to test-writers, which was too blunt --
  a missing stub takes the golden adapter down with it. Stub requested and added; suite now builds.
- DEC-RUN-3 binds this implementer: persist on level-up, accepted spend AND match end.
- iter 1: implementer dispatched in worktree .tdd/worktrees/009.
- iter 1 GREEN in worktree: 26 failed / 47 passed / 73 total, exactly the target. Locked tests
  untouched. No csproj change (no NuGet package); JSON hand-rolled per the netstandard2.1 constraint.
- MERGED to main @ 9adbcc8. Post-merge full suite with 002+003: 17 failed / 92 passed / 109 total,
  13/30 fixtures green. T-09 tag gone; remaining tags unchanged. validate-spec and coverage green.
