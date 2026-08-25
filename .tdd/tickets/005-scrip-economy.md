---
id: 005
title: Shared scrip economy, purchase and sell
status: pending
depends_on: [001, 004]
touches: [unity/RedHollow/Assets/GameSim/MatchSim.Economy.cs, sim/GameSim.Tests/T05_EconomyTests.cs]
iterations: 0
test_files: []
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

## Test plan

_Filled in by the test-writer._

## Attempt log

