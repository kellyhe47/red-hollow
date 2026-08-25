---
id: 012
title: UI screens S1-S7 and cross-cutting states
status: green
depends_on: [010]
touches: [unity/RedHollow/Assets/Game/UI/]
iterations: 1
test_files:
  - unity/RedHollow/Assets/Tests/EditMode/T12_UiScreensTests.cs
  - unity/RedHollow/Assets/Tests/EditMode/T12_UiHudTests.cs
branch: ""
board_id: T-12
owns_requirements: [R-60, R-61, R-62, R-63]
grades_fixtures: []
---

## Scope

Every screen and state in docs/ui-wireframes.html: Title/Join, Lobby, Planning, Combat, Wave interstitial, Victory, Defeat, plus bad join code error, greyed unaffordable shop items, dead-hero spectate overlay, civilians-lost toast and red flash, lost-hotspot marking. Persistent combat HUD; non-blocking level-up overlay (hotkey L / badge click); planning shop bar with ghost preview, sell tooltip, pulsing entry points, ready N/4 and timer.

## Acceptance criteria

- [x] every wireframe screen and state is present and reachable
- [x] the sim never pauses for the level-up overlay

## Test plan

Contract stubs (throwing, T11/T16 convention) live in `unity/RedHollow/Assets/Game/UI/` —
`UiRouter` + `UiScreen`, `TitleScreenModel`, `LobbyScreenModel`, `PlanningScreenModel`,
`CombatHudModel`, `WaveInterstitialModel`, `PostMatchModel` + `MatchStatsTracker`. All plain C#
presenters, read-only over sim state (T-10 Cecil invariant); mutations are sim commands / session
calls only. MonoBehaviours may only mirror these models.

`T12_UiScreensTests.cs` — screens and flow:
- S1: callsign loads profile (lifetime level + XP); unknown callsign = fresh account, no error (R-44)
- S1: failed join → inline error, cleared on edit, router stays on Title (R-60)
- Router: Title → Lobby → phase-mapped match screen; phase/status "combat" literal never conflated
- S5: wave_complete → interstitial; declared hold (0 < hold < planning duration) → back to Planning (R-04)
- S5 data: wave, bounty earned THIS wave (not the pool), civilians remaining X/20 (WaveSummary)
- S6: victory keys off Status (phase still "combat"); civilians saved; rematch host-only → S2 same code (R-07/DEC-RUN-11)
- S7: defeat, reached wave N mid-campaign, 0 saved, RETRY same semantics
- Stats table: kills per player from xp_awarded, scrip spent from placeable_created at catalog prices
- S2: seats mirror party, join/leave updates, waiting-alone hint, join code shown
- S2: duplicate class picks allowed (R-31)
- S2: match starts only when ALL connected ready; solo needs only own ready (no force-start)
- Host disconnect → Title with error; match status stays in-progress (DEC-RUN-10)
- ESC menu: overlay not a screen, sim time/world advance, Time.timeScale untouched (R-55)

`T12_UiHudTests.cs` — S3 planning + S4 combat HUD:
- S3 top bar mirrors state: wave n/total, scrip, per-hotspot civilians (R-61)
- S3 timer counts down with sim time, clamps at 0, inclusive deadline; timer 0 → auto combat + router S4 (R-03)
- R-05/DEC-018: pulsing entries == PreviewUpcomingWave indices; reflection pin — presenter surface
  cannot involve WaveSpec/MonsterGroup/WaveTable (composition unexposable by construction)
- Shop bar == R-23 catalog at catalog prices; unaffordable flagged not hidden; buy anyway →
  refused, reason surfaced, pool untouched (G-014)
- Ghost: follows cursor, invalid-zone flag, invalid placement rejected (reason ≠ money), ghost
  stays for retry; valid placement buys at catalog price, placeable at ghost pos, ghost clears
- Sell: tooltip = SellRefundRatio × PurchaseCost; sale refunds into pool, removes placeable;
  refused sale = accepted:false only, no reason anywhere (SellResult pinned reason-free)
- Ready panel: denominator = connected players; last connected ready starts combat early; leaver
  drops out of denominator (R-03/R-53)
- S4 HUD mirrors: wave, LivingMonsterIds.Count, scrip (+ticks on kill), hotspot civs, HP/class
- Cooldowns: absent CooldownReadyAt key = ready; countdown; INCLUSIVE ready-at (R-32)
- Locked slots padlocked until unlock; level/XP/points/badge from IProfileStore (R-61)
- level_up event → toast; picker choices lawful: unlock for locked, rank for unlocked < MaxAbilityRank
- R-62: picker open → sim time advances by every delta, world moves, spend is a normal command,
  timeScale untouched
- spend_rejected reason surfaced verbatim (G-026)
- Dead hero: spectate overlay, respawn countdown from config (inclusive, clamped), camera target =
  living ally, overlay down on respawn (R-33)
- civilians_killed (count>0) → red flash + toast naming hotspot; count 0 → neither (R-13)
- hotspot_emptied → that hotspot Lost, others not (R-12)
- wave_spawned → entry flares at the previewed tunnels (event carries none — DEC-018)
- PlayerDisconnected notice → toast naming peer; match continues (R-53)

## Attempt log

- 2026-08-25 green in 1 implementation pass. Red verified: 39/39 NotImplementedException, 70 prior green. Green verified by orchestrator: EditMode 109/109, dotnet 356/356. Locked tests and sim untouched (git diff empty over Tests/, GameSim/, sim/, eval/, docs/).

- ~~BLOCKED (environment, pre-run)~~ RESOLVED 2026-08-25 — owner installed Unity. Original note: Unity Editor is not installed on this machine and needs the owner's Unity account/licence. `unity/RedHollow/` currently holds only `Assets/GameSim` — there is no Unity project (no ProjectSettings/, no Packages/). T-01..T-09 carry the entire 30-fixture acceptance contract and need no Unity.

## Handoff notes from the sim run (read before starting)

Everything the HUD (R-61) needs is already exposed:
- wave n/10 — `State.Wave.Number` / `State.Wave.TotalWaves`
- monsters remaining — `State.Wave.LivingMonsterIds`
- per-hotspot civilians — `State.Hotspots[..].Civilians`
- shared scrip — `State.Team.Scrip`
- own HP / level / XP / unspent points — `Hero` + `IProfileStore.Load(accountId)`
- cooldowns — `Hero.CooldownReadyAt` (absent key = ready)

- **R-04 interstitial**: `WaveSummary()` returns bounty earned *that wave* and civilians remaining.
- **R-05 planning preview**: `PreviewUpcomingWave()` returns only the activating entry-tunnel indices.
  It carries no monster types or counts **by construction** — do not work around this to show
  composition; hiding it is the requirement (DEC-018).
- **R-62** the level-up overlay must not pause the sim; `SpendSkillPoint` is a normal command.
- Rejections surface as `purchase_rejected` / `spend_rejected` events with a reason string.
  `SellResult` has **no** reason field — a refused sale reports only `accepted: false`.
- UNBLOCKED: Unity installed and the project scaffolds cleanly. Depends on 010's scene work.
