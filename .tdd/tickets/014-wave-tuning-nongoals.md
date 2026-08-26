---
id: 014
title: Wave-table playtest tuning and non-goal guard
status: blocked-owner-playtest (guards green; R-06 window is an owner tuning decision)
depends_on: [004, 011, 012]
touches: [unity/RedHollow/Assets/GameSim/WaveTable.cs, unity/RedHollow/Assets/Game/Config/]
iterations: 0
test_files: [sim/GameSim.Tests/T14_NonGoalGuardTests.cs, sim/GameSim.Tests/T14_WaveTablePinTests.cs, unity/RedHollow/Assets/Tests/EditMode/T14_NonGoalTests.cs, unity/RedHollow/Assets/Tests/EditMode/T14_TimingTests.cs]
branch: ""
board_id: T-14
owns_requirements: [R-06, R-70, R-71, R-73]
grades_fixtures: []
---

## Scope

Playtest the wave table to a 25-35 minute session (R-19 is deliberately unfixtured config). Confirm the v1 non-goals hold: no PvP, no host migration, no mid-match join, no spectator beyond dead-hero cam, no cross-match meta-economy, no second map, no boss, no difficulty settings.

## Acceptance criteria

- [ ] a full match lands in the 25-35 minute window
- [x] no non-goal feature shipped (21 locked guard tests, non-vacuity plant-verified)

## Test plan

Four files, 22 tests, ALL EXPECTED GREEN ON ARRIVAL — the non-goal guards assert the ABSENCE of
unshipped features (that is the point: they fail on the first commit that ships one), the R-19 pins
assert the already-shipped table's decided shape, and the R-06 harness always passes by design.

**sim/GameSim.Tests/T14_NonGoalGuardTests.cs** (9 tests, dotnet) — sim-side non-goals:
- R-70 no PvP: a hero attack whose line crosses another hero FIRST damages only the monster behind
  (anti-vacuity: the monster IS hit); a hero-only line is a clean miss (kills the
  "first-monster-else-first-hero" fallback); `FriendlyFire` ships false; name scan of
  MatchSim/SimConfig for pvp/duel operations.
- R-71: `AccountProfile` carries XP/level/points/abilities and nothing currency-shaped (scan
  self-tests against "Scrip"/"Currency"/... before certifying absence); `CreateMatchState`'s
  parameter list is pinned to exactly one `SimConfig` — the channel a carried pool would arrive by.
- R-73: `ColonyMap`'s static factories == exactly `{V1}`; MonsterType vocabulary AND default
  catalog rows == exactly the R-17 five (a boss must land in one of them to be spawnable);
  no difficulty-named member on SimConfig/WaveTable/ColonyMap.

**sim/GameSim.Tests/T14_WaveTablePinTests.cs** (6 tests, dotnet) — R-19's DECIDED facts only,
range-pinned so tuning never unlocks a test: waves numbered exactly 1..TotalWaves; every row
spawnable (real archetype, positive count, valid distinct tunnel indices); wave 1 = Shamblers only,
4–8 total, single breach; zero Behemoths in waves 1–4 and ≥1 at wave 5; wave 10 total 25–35, >1
archetype ("mixed"), all four tunnels; V1() builds per-instance (tuning one table moves no other).

**unity/.../EditMode/T14_NonGoalTests.cs** (6 tests) — session-side non-goals (NetSession is not
visible to the dotnet suite):
- R-70 no host migration: name scan (self-tested against MigrateHost/PromoteToHost/...), plus the
  behavioural arm T11 leaves open — an Ended session refuses EVERY verb (guest TryStartMatch/
  TryRematch, new TryJoin, and re-StartHost throws), each anti-vacuous against the live path.
- R-70 no mid-match join: the post-match-screen refusal (T11 pins the in-match one), anti-vacuous
  via the same peer being seated the moment rematch reopens the lobby.
- R-70 no spectator: name scan over NetSession/NetPeer/NetSessionConfig/PartyRoster/PlayerSlot and
  both session enums.
- R-71: rematch after a match whose pool was driven to 1500 by real kills opens on StartingScrip
  (two-sided: == stake AND != old pool), while the host's lifetime XP survives the same reset.
- R-73: no difficulty-named member on the shell config surface (NetSessionConfig et al.).

**unity/.../EditMode/T14_TimingTests.cs** (1 test) — the R-06 harness. R-06 is a playtest
criterion (PRD says so), so the test ALWAYS PASSES and its product is the report written to
TestContext + Unity log: a real NetSession/loopback/MatchSession 10-wave match driven by 2
scripted bots (instant ready-up, perfect-aim basic attacks each 0.25s through ResolveHeroAttack +
RecordMonsterKill), per-wave planning/combat sim-seconds, and the session length projected for
both an early-ready party and the never-ready 60s-ceiling party (+10 min planning). Measured
2026-08-25: combat 58.7s, victory, projections 1.4 min / 11.4 min — far below the 25–35 min
window even as a floor; tuning is the owner's call (see attempt log).

## Attempt log

- 2026-08-25 non-goal guards + R-19 structure pins + R-06 timing harness locked and green:
  dotnet 371/371, EditMode 142/142 (both orchestrator-verified). Guard bite verified by
  planting a CarryoverScrip field on AccountProfile — exactly one guard failed, reverted.
- R-06 measurement (bot loopback, sim time): combat total 58.7s across 10 waves; projected
  session 1.4 min (instant ready) to 11.4 min (never ready; the 60s planning ceiling alone
  is 10 min). Target window 25-35 min. Even as a floor (perfect-aim bots), the shipped
  WaveTable.V1() is an order of magnitude light. Closing this needs real playtest + owner
  retuning of the wave table / combat pacing — balance, not machine-testable (PRD line 39).
  The harness prints the full per-wave breakdown on every EditMode run for retuning loops.

- ~~BLOCKED (environment, pre-run)~~ RESOLVED 2026-08-25 — owner installed Unity. Original note: Unity Editor is not installed on this machine and needs the owner's Unity account/licence. `unity/RedHollow/` currently holds only `Assets/GameSim` — there is no Unity project (no ProjectSettings/, no Packages/). T-01..T-09 carry the entire 30-fixture acceptance contract and need no Unity.

## Handoff notes from the sim run (read before starting)

`WaveTable.V1()` ships a first-pass 10-wave table (R-19, deliberately unfixtured). Design intent:
one new archetype layered in at a time — ravagers w2, spitters w4, behemoths w5, burrowers w6 —
with the active breach set rotating every wave so the previous wave's wall line is never simply
reusable. Wave 8 is an all-four rehearsal, wave 9 trades width for weight (two behemoths from three
breaches), wave 10 is 30 mixed from all four.

It is per-instance config: retune without touching rule code. Numbers most likely to need it are
listed in the final report — the ability damage values and the R-24 placement radii are guesses the
PRD never specified.
- UNBLOCKED: Unity installed. Gated on 011/012 for a playable session to tune against.
