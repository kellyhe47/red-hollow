---
id: 005
title: Shared scrip economy, purchase and sell
status: in-progress
depends_on: [001, 004]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Economy.cs, sim/GameSim.Tests/T05_EconomyTests.cs]
iterations: 1
test_files: [sim/GameSim.Tests/T05_EconomyTests.cs]
branch: ""
board_id: T-05
owns_requirements: [R-20, R-21, R-22, R-24, R-25]
grades_fixtures: [G-013, G-014, G-015, G-022]
---

## Scope

purchase_placement (planning-phase only, valid zone, sufficient scrip; rejection reasons wrong_phase / insufficient_scrip; never negative), sell_placement (floor(cost*0.5) refund during planning). Starting stake 500, shared pool, any player may spend.

## Acceptance criteria

- [ ] G-013, G-014, G-015, G-022 pass
- [ ] rejections emit purchase_rejected and change no state
- [ ] placement zone validation rejects hotspot interiors, tunnel mouths, overlaps
- [ ] R-25: any player may spend from the shared pool - no ownership check, vote or lock rejects a purchase
- [ ] PlaceableCatalog carries the R-23 cost table (Barricade 100, Spike Trap 75, Dynamite 150, Turret 250, Med Station 200); 006 consumes its effect numbers

## Test plan

`T05_EconomyTests.cs` — 29 cases. R-24 zone validation (hotspot interior, tunnel mouth, overlap,
valid control, freed-by-sell); R-25 any-player-spends; R-23 cost table as config; pool boundary
at/just-under/just-over with exactly-equal landing on 0; odd-cost refund flooring; sell in combat;
duplicate and unknown sell; cost-vs-catalog invariant; DEC-RUN-9 starting stake. G-013/014/015/022
not re-encoded.

## Attempt log

- CRITERIA AMENDED pre-dispatch (DEC-RUN-4 audit): requirements this ticket owns that had
  neither a fixture nor an acceptance criterion, and would have shipped unimplemented.
- tests locked @ HEAD: 29 cases, 28 red; single labelled structural guard passes. Sequential ticket,
  main working directory, no worktree.
