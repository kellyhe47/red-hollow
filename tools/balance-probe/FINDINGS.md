# Balance probe findings — shipped numbers, 2026-08-26

`dotnet run --project tools/balance-probe/BalanceProbe.csproj` (.NET 10). Every run drives the
REAL `MatchSim` (shipped `SimConfig`, `WaveTable.V1`, `ColonyMap.V1`, the production
`BarricadePathOracle`) through a faithful mirror of the Unity shell's host schedule. The scripted
players have perfect aim and perfect uptime but no human movement finesse; treat them as a
competent player, not the human ceiling in either direction.

## Outcomes at SHIPPED numbers

| policy | outcome | died on | civilians |
|---|---|---|---|
| solo, basics only, no purchases | defeat | wave 8 | 0/20 |
| solo + turrets | defeat | wave 9 | 0/20 |
| solo + turrets + spikes | defeat | wave 10 | 0/20 |
| solo + walls + turrets + spikes | defeat | wave 10 | 0/20 |
| solo, full kit + abilities + threat-priority aim | defeat | wave 9 | 0/20 |
| solo, full kit + spendthrift economy | defeat | wave 9 | 0/20 |
| **two players, full kit** | **victory** | — | **20/20, zero downs** |

## Outcomes with waves 6–9 trimmed (probe-only config; defaults untouched)

R-19's fixed points survive every candidate: wave 1 stays ~6 shamblers from one breach, the
first Behemoth still lands at wave 5, and wave 10 ships exactly as authored (~30 mixed, all four
breaches).

| policy | outcome | civilians |
|---|---|---|
| solo skilled, 25% trim, lean economy | defeat on wave 10 | 20 → 0 in the finale alone |
| solo skilled, 40% trim, lean economy | defeat on wave 10 | 20 → 0 in the finale alone |
| **solo skilled, 25% trim + spendthrift economy** | **victory** | **6/20** |
| duo, 25% trim (regression check) | victory | 20/20 |

## Reading

1. **Waves 1–6 are safe solo under every policy** — zero losses even buying nothing.
2. **At shipped numbers the solo campaign dies in waves 7–10, always by civilian bleed** —
   3–4 simultaneous breaches outrun one hero's ~125 DPS plus an economy-capped turret grid.
   20 civilians is the whole match's budget; waves 8–9 alone eat 10–16 of them.
3. **The finale is the real wall.** Given a clean 20-civilian runway (any midgame trim), lean
   solo play still loses wave 10 outright. What closes it is the ECONOMY: re-laying dynamite
   every planning and buying turrets until the pool runs dry turns the trimmed finale into a
   6/20 win. Solo winnability = midgame trim + spending discipline, together.
4. **Two players win everything comfortably** (20/20 at shipped numbers, 2× time margin). The
   shipped table reads as co-op-tuned.
5. Barricades (now that the production path oracle exists) and spike lines are each worth about
   one extra wave; abilities and repositioning moved little — the binding constraint is total
   DPS across simultaneous lanes.

## Recommendation (owner decision — R-19 makes these numbers playtest-tunable)

If solo should be winnable at expert level: **trim waves 6–9 headcounts ~25%** (one config edit,
fixed points untouched, duo stays a win). If solo should stay a co-op recruiting pitch, ship as
is — the probe shows a skilled solo player reaching wave 8–10 before the colony falls, which is
a real (if losing) run. Party-size wave scaling would close the gap cleanly but is a NEW sim
rule needing a PRD decision first.

No tuning was applied on this branch; the probe exists so any retune is measured, not guessed.
