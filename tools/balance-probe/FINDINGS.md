# Balance probe findings — shipped numbers, 2026-08-26 (updated after ticket 029)

`dotnet run --project tools/balance-probe/BalanceProbe.csproj` (.NET 10). Every run drives the
REAL `MatchSim` (shipped `SimConfig`, `WaveTable.V1`, `ColonyMap.V1`, the production
`BarricadePathOracle`, and — since ticket 029 — the Spitter's PRD-stated "ranged acid, range 10")
through a faithful mirror of the Unity shell's host schedule. The scripted players have perfect
aim and uptime but no human movement finesse; treat them as a competent player, not the human
ceiling in either direction.

## Outcomes at SHIPPED numbers (Spitter range live)

| policy | outcome | died on | civilians |
|---|---|---|---|
| solo, basics only, no purchases | defeat | wave 6 | 0/20 |
| solo + turrets (+ spikes, + walls) | defeat | wave 6 | 0/20 |
| solo, full kit, nearest-monster aim | defeat | wave 6 | 0/20 |
| **solo, full kit + THREAT-priority aim** | **victory** | — | **4/20** |
| **two players, full kit** | **victory** | — | **6/20** |

Earlier snapshots (before the Spitter's range existed) had every solo policy losing in waves
8–10 and the duo winning 20/20. The ranged acid moved both ends: it punishes naive play four
waves earlier, and it is the pressure that finally makes shelter-defence a real job for the duo.

## Reading

1. **Spitters are the difficulty curve now.** From wave 4 they stand at their 10-unit line and
   drain shelters (12 dmg/s each) while walkers soak attention. A player who keeps shooting the
   nearest monster loses ten civilians in wave 4 alone and the colony by wave 6 — with any
   amount of hardware on the ground, because turret nests parked by the shelters cannot reach a
   spitter's line reliably (range 8 vs a line 10 from the shelter's centre).
2. **Threat-priority targeting flips solo from unwinnable to a 4/20 win at shipped numbers.**
   Kill what is about to land damage — spitters on their line, walkers at the door — and the
   campaign closes. This is skill expression, not stat inflation: the difference between the
   losing and winning solo runs is only which monster the reticle prefers.
3. **The duo remains the comfortable way to play** (6/20, two-minute campaign), and spitters are
   what keeps a duo honest — pre-029 they cleared 20/20 without noticing the roster.
4. **No tuning recommendation stands.** The pre-029 candidates (waves 6–9 trims) are stale and
   re-measured as unnecessary: solo is winnable at shipped numbers by exactly the player the
   game is trying to teach. If the owner wants naive solo runs to survive past wave 6, the
   measured lever is spitter count in waves 4–6 — but the current cliff reads as the game
   working as designed.

## Provenance

Placeable combat (turrets 1 Hz, trap edge-triggering, kill reaping), the barricade path oracle,
and the Spitter's attack range were all implemented on this branch; each probe update re-measured
the same policies over the same shipped numbers. History of snapshots lives in this file's git
log — the current table is the branch's live behaviour.
