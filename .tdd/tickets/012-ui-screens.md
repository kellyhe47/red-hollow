---
id: 012
title: UI screens S1-S7 and cross-cutting states
status: blocked
depends_on: [010]
touches: [unity/RedHollow/Assets/Game/UI/]
iterations: 0
test_files: []
branch: ""
board_id: T-12
owns_requirements: [R-60, R-61, R-62, R-63]
grades_fixtures: []
---

## Scope

Every screen and state in docs/ui-wireframes.html: Title/Join, Lobby, Planning, Combat, Wave interstitial, Victory, Defeat, plus bad join code error, greyed unaffordable shop items, dead-hero spectate overlay, civilians-lost toast and red flash, lost-hotspot marking. Persistent combat HUD; non-blocking level-up overlay (hotkey L / badge click); planning shop bar with ghost preview, sell tooltip, pulsing entry points, ready N/4 and timer.

## Acceptance criteria

- [ ] every wireframe screen and state is present and reachable
- [ ] the sim never pauses for the level-up overlay

## Test plan

_Filled in by the test-writer._

## Attempt log

- BLOCKED (environment, pre-run): Unity Editor is not installed on this machine and needs the owner's Unity account/licence. `unity/RedHollow/` currently holds only `Assets/GameSim` — there is no Unity project (no ProjectSettings/, no Packages/). T-01..T-09 carry the entire 30-fixture acceptance contract and need no Unity.

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
