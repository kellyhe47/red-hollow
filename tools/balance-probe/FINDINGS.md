# Balance probe findings — shipped numbers, 2026-08-26

`dotnet run --project tools/balance-probe/BalanceProbe.csproj` (.NET 10). Every run drives the
REAL `MatchSim` (shipped `SimConfig`, `WaveTable.V1`, `ColonyMap.V1`, the production
`BarricadePathOracle`) through a faithful mirror of the Unity shell's host schedule. The scripted
players have perfect aim and perfect uptime but no human finesse; treat them as a competent
player, not an expert one.

## Outcomes

| policy | outcome | died on | civilians |
|---|---|---|---|
| solo, basics only, no purchases | defeat | wave 8 | 0/20 |
| solo + turrets | defeat | wave 9 | 0/20 |
| solo + turrets + spikes | defeat | wave 10 | 0/20 |
| solo + walls + turrets + spikes | defeat | wave 10 | 0/20 |
| solo, full kit + abilities + repositioning | defeat | wave 9 | 0/20 |
| solo, full kit + threat-priority aim | defeat | wave 9 | 0/20 |
| **two players, full kit** | **victory** | — | **20/20, zero downs** |

## Reading

1. **Waves 1–6 are safe solo under every policy** — zero civilian losses even with no purchases.
   The early game is comfortable at shipped numbers.
2. **The solo campaign dies in waves 7–10**, always by civilian bleed, never by hero deaths. The
   structural cause: those waves open 3–4 breaches at once, and one gunslinger (~125 DPS with the
   crit rhythm) plus an economy-capped turret grid (~8 × 20 DPS but range-8-local) cannot clear
   ~1600–2300 HP of wave before enough of it reaches shelters. 20 civilians is the budget for the
   WHOLE match; waves 8–9 alone eat 10–16 of them.
3. **Two players win with a perfect score and a 2× margin** (63 s of combined combat vs ~100 s of
   solo attempts). The shipped table reads as tuned for co-op; the solo cliff between wave 6 and
   wave 7 is the sharpest edge in the campaign.
4. Barricades (now that the production path oracle exists) and spike lines are worth roughly one
   extra wave of survival each; abilities and repositioning as scripted here moved little — the
   binding constraint is total DPS across simultaneous lanes, not micro.

## Recommendation (owner decision — R-19 makes these numbers playtest-tunable)

If solo is meant to be *winnable* rather than merely playable, the smallest levers are:

- trim the late-table headcounts (waves 7–10) by ~25–30%, or
- raise the civilian budget (R-10's 20) — it is the match's real HP bar, or
- scale wave composition by party size (note: that is a NEW sim rule, not a config tweak, and
  would need a PRD decision first).

No tuning was applied on this branch: the shipped defaults are untouched, and the probe exists so
the retune can be measured instead of guessed.
