---
id: 025
title: Combat action routing — SPACE/Q/E intents reach the sim
status: pending
depends_on: [024]
touches: [unity/RedHollow/Assets/Game/UI/, unity/RedHollow/Assets/Game/Host/]
iterations: 0
test_files: []
branch: ""
board_id: T-25
owns_requirements: []
grades_fixtures: []
---

## Scope

Final playability audit find (2026-08-26): `DefaultHeroInputMap` produces
`HeroCommandIntent.BasicAttack` / `.Ability` but **nothing consumes them** — `HostLoop` and
`LocalHeroIntentSource` route movement only. In Play, SPACE/Q/E do nothing; the player
cannot kill a monster.

Adds shell-side combat action routing:

- Consume the intents in the pump / host path.
- Basic attack: build `HeroAttackRequest` — `EntitiesOnLine` ordered nearest-first along
  the cursor aim line, derived from **sim state geometry only** (the request doc:
  "Physics decides who is on it; the sim decides who is hit"; DEC-RUN-8 makes pellet
  connection the shell's answer). `Damage` from the hero-kit catalog
  (`SimConfig.HeroKits`, per-pellet for Rancher per DEC-RUN-8).
- Attack cadence: unspecced by the PRD (the 014 harness modeled 0.25 s and printed it as a
  parameter). Shell config value; **flag the chosen number to the owner.**
- Q/E: `HeroAbilityRequest` for the mapped slot; cooldowns, locks and ranks stay sim-side —
  the shell only issues the command and surfaces rejection.
- Press/hold semantics: holding SPACE re-fires at the cadence; abilities fire on press-edge
  (holding Q must not spam commands every frame — the sim would reject on cooldown but the
  spam is still wrong).

## Acceptance criteria

- [ ] holding SPACE in combat attacks along the cursor aim line at the configured cadence;
      monsters take catalog damage and die
- [ ] Q/E issue `HeroAbilityRequest` for the mapped slot on press-edge; sim-side rejections
      surface; nothing double-fires
- [ ] aim-line entity ordering is nearest-first from sim state only; the Cecil invariant
      holds

## Test plan

_Filled in by the test-writer._

## Attempt log

_(created 2026-08-26 by the orchestrator at 024 close.)_

## Handoff notes

- `HeroAttackRequest` shape: `AttackerId`, `AttackerClass`, `Damage`, `EntitiesOnLine`
  (ordered nearest-first `LineEntity {Id, Kind, Pos}`) — Commands.cs:66. The fixture loader
  understands kinds hero/hotspot/monster/barricade.
- `HeroAbilityRequest` is in Commands.Abilities.cs. `AbilityResult`'s shape is fixture-
  pinned (G-018) — read-only to you.
- Aim comes from `InputSnapshot.CursorGroundPoint` → direction from the hero's position.
  Line geometry (width/length/cone for Rancher) is shell policy — keep it config-shaped and
  document choices; nothing PRD-specified.
- The press-edge pattern precedent is 024's `_pointerMouseWasDown`.
- G-030 (crit sequence) lives in `ResolveHeroAttack` — the sim owns crits; just issue
  requests.
