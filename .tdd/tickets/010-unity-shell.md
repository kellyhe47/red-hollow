---
id: 010
title: Sim host loop + shell architecture invariant
status: in-progress
depends_on: [001]
touches: [unity/RedHollow/Assets/Game/Host/, unity/RedHollow/Assets/Tests/EditMode/]
iterations: 1
test_files: []
branch: ""
board_id: T-10
owns_requirements: [R-50, R-52]
grades_fixtures: []
---

## Scope

Unity project that references GameSim via asmdef (noEngineReferences on GameSim mechanically enforces the invariant). Top-down camera, WASD move with W movement-only, hero faces cursor, SPACE basic, Q/E abilities; own-hero local prediction with host reconciliation; remote entity interpolation. MonoBehaviours send commands and render replicated state, never hold game rules.

## Acceptance criteria

- [ ] a playable solo session runs with primitive placeholder art
- [ ] no game rule appears in a MonoBehaviour
- [ ] GameSim asmdef has noEngineReferences: true

## Test plan

_Filled in by the test-writer._

## Attempt log

- ~~BLOCKED (environment, pre-run)~~ RESOLVED 2026-08-25 — owner installed Unity. Original note: Unity Editor is not installed on this machine and needs the owner's Unity account/licence. `unity/RedHollow/` currently holds only `Assets/GameSim` — there is no Unity project (no ProjectSettings/, no Packages/). T-01..T-09 carry the entire 30-fixture acceptance contract and need no Unity.

## Handoff notes from the sim run (read before starting)

The whole simulation is green and callable. `MatchSim` is the only object the shell needs.

**The host loop must drive these ticks — none of them self-schedule:**
- `TickPlanningTimer()` — R-03's 60s planning expiry (without it, planning only ends when every connected player readies)
- `TickStatusEffects()` — lasso/Bulwark expiry
- `TickHeroRegen()` — R-35 out-of-combat regen
- `TickHeroRespawns()` — R-33 revives; without it dead heroes never come back
- `TickMedStations()` — R-23 healing

**⚠️ `TryMonsterAttack(monsterId)` is ADVISORY and the sim cannot enforce it.** The host must
call it and get `true` *before* calling `ApplyHotspotAttack` / `ApplyHeroDamage` /
`ApplyPlaceableDamage` for that monster. Skipping it applies damage every frame instead of once
per second (R-18) and the colony falls in the first second of wave 1. This design was chosen
deliberately so the six first-attack golden fixtures keep their exact observations — the cost is
that the discipline lives in the shell.

Gate **before** the damage op, never after: each command resets `LastObservation`, which is what
netcode replicates from.

**Settable config seams on `MatchSim`:** `WaveTable` (defaults to `WaveTable.V1()`), `ColonyMap`
(defaults to `ColonyMap.V1()`), and the R-24 placement radii (`HotspotBuildingRadius`,
`EntryTunnelMouthRadius`, `PlaceableFootprintRadius`).

Build a match with `ColonyMap.V1().CreateMatchState(config)` — it seeds hotspots and the R-20
starting stake.

- UNBLOCKED: Unity 6000.5.9f1 installed with Mac Standalone + WebGL; licence verified working via
  headless batchmode. Project initialised in place — ProjectSettings, Packages, 27 .meta files.
- VERIFIED: Unity compiled GameSim into its own assembly with `noEngineReferences: true` honoured
  and an empty reference list. R-51 now proven under the engine compiler, not just dotnet.
- Transport stack resolved by Package Manager API (not hand-pinned): NGO 2.13.2, Transport 6.5.0,
  Lobby 1.3.0, Relay 1.2.0, Authentication 3.7.4, Core 1.18.0, Input System 1.20.0.
  packages-lock.json committed for reproducibility.
- REMAINING for this ticket: scene, top-down camera, WASD+mouse-aim input (R-30), the host loop
  driving the sim ticks, and local prediction / remote interpolation (R-52).
- SPLIT 2026-08-25: scene, camera, input and placeholder visuals moved to ticket 016 (R-30).
  This ticket keeps the host loop and the no-rules-in-a-MonoBehaviour invariant — the spine
  everything downstream depends on, and the part that is pure EditMode testable.
