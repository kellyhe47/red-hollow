---
id: 007
title: Hero damage, death/respawn, no friendly fire
status: awaiting-merge
depends_on: [001]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Heroes.cs, unity/RedHollow/Assets/GameSim/Entities.cs, sim/GameSim.Tests/T07_HeroTests.cs]
iterations: 2
test_files: [sim/GameSim.Tests/T07_HeroTests.cs]
branch: "tdd/007"
board_id: T-07
owns_requirements: [R-26, R-33, R-34, R-35, R-36]
grades_fixtures: [G-020, G-021, G-030]
---

## Scope

apply_hero_damage (Sawbones flat 30% DR, floor applied; 0 HP -> dies instantly, respawn_at = now + 10s, untargetable while dead), resolve_hero_attack (hero attacks pass through heroes and placeables, damage monsters only). No mana; out-of-combat regen 2 HP/s after 5s.

## Acceptance criteria

- [ ] G-020, G-021, G-030 pass
- [ ] all heroes dead is not defeat
- [ ] dead heroes excluded from monster target candidates (feeds T-02)

## Test plan

`T07_HeroTests.cs` — 29 cases. R-33 all-heroes-dead-is-not-defeat (party of 1 and last-of-3);
dead-hero candidate exclusion (state side only — SelectTarget belongs to 002); DEC-009
class-conditional reduction + flooring rule; clock-derived respawn at values other than
G-021's so 55.0 cannot be hardcoded; overkill ordering; no-friendly-fire parametrized over
ally/barricade/placeable orderings + the no-monster boundary; R-34 no-mana structural guard;
R-35 regen (delay, rate-is-read, MaxHp cap, damage resets clock, dead heroes excluded).
G-020/021/030 not re-encoded.

## Attempt log

- wave A: test-writer dispatched in worktree .tdd/worktrees/007 (branch tdd/007).
- tests locked on tdd/007 @ d8bda71: 29 cases, 28 red. The single passer is the labelled R-34
  structural guard. Orchestrator-verified in-worktree: 78 total = 30 golden + 19 harness + 29 T07.
- R-35 was initially skipped by the test-writer as untestable (no seam). Sent back and covered:
  absence of a seam specifies the seam, it does not excuse the requirement. Shape stubs added:
  `Hero.LastDamagedAt` (Entities.cs) and `MatchSim.TickHeroRegen()` (MatchSim.Heroes.cs).
  Entities.cs is touched by no other wave-A ticket, so this is safe.
- `TickHeroRegen()` returns void, not an ISimResult: no fixture grades regen, and keeping it void
  avoids editing the shared Commands.cs. Accepted.
- DEC-RUN-2 binds this ticket's implementer: reduced damage is
  `Math.Floor(damage * (1.0 - reduction) + 1e-9)`.
- iter 1: implementer dispatched in worktree .tdd/worktrees/007.
- iter 2: R-33 respawn execution (DEC-RUN-4). Test-writer added TickHeroRespawns() seam + 8 cases
  incl. the inclusive-deadline boundary grounded in G-019 precedent; implementer closed it.
- GREEN in worktree: 27 failed / 59 passed / 86 total. 37/37 T07 pass, zero T-07 stubs remain,
  locked tests untouched.
