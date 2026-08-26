---
id: 025
title: Combat action routing — SPACE/Q/E intents reach the sim
status: pending
depends_on: [024]
touches: [unity/RedHollow/Assets/Game/UI/, unity/RedHollow/Assets/Game/Host/]
iterations: 0
test_files: [unity/RedHollow/Assets/Tests/EditMode/T25_CombatActionTests.cs]
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

19 EditMode cases in `unity/RedHollow/Assets/Tests/EditMode/T25_CombatActionTests.cs`, driven
through the locked shell pump with T-22's fake `IInputSource`. Stubs (throwing
`NotImplementedException`): `Game/Input/AimLine.cs`; real data stub `Game/UI/CombatActionConfig.cs`
(cadence + line length/width, defaults flagged to the owner below); additive members on
`ShellBootstrap` — `ShellBootstrapOptions.CombatActions`, `ShellBootstrap.CombatActions`
(throwing), `ShellBootstrap.LastAbilityOutcome` (throwing).

1. **Aim-line geometry** (`AimLine.EntitiesAlong(state, attackerId, origin, aimPoint, length,
   width)`, pure over hand-built `MatchState`): nearest-first ordering; corridor bounds (width /
   length / nothing behind the origin — boundary in/exclusivity deliberately unpinned); fixture
   kind spellings hero/hotspot/monster/barricade with Id+Pos carried; the attacker never on its
   own line; dead monsters and `Exists == false` placeables excluded (**verified necessary**:
   `MatchSim.FirstMonsterOnLine` resolves by id without re-checking `Alive`, and kills leave the
   corpse in `State.Monsters` — exclusion is the shell's job); zero-direction aim → empty list, no
   throw; tunables are config-shaped (defaults pinned only as positive; shell exposes the composed
   instance, `Input`-accessor pattern).
2. **Sim honoring** (real `ColonyMatchFactory` match, direct `ResolveHeroAttack` with the
   `AimLine` list): ally + hotspot + barricade nearer than a monster — all reported honestly,
   only the monster is hit for the catalog basic damage (R-34 is the sim's monster ALLOWLIST;
   hotspots/barricades/heroes on the line are ignored entirely by the sim).
3. **Basic attack routing**: press fires immediately (T-24's zero-delta pump-edge precedent);
   holding fires exactly one request per cadence window (composed 0.25 s test value), never one
   per pump; damage read from `HeroKits.KitFor(class).BasicAttackDamage`; SPACE during planning
   issues NOTHING — proven via the Gunslinger every-4th crit rhythm, which advances per issued
   request even on a miss; a monster at 0 HP **dies through the pump path** (`alive` flips, wave
   roster shrinks, catalog bounty paid exactly once — `ResolveHeroAttack` deliberately never
   kills, and nothing shipped calls `RecordMonsterKill`, so the routing must); Rancher `Damage`
   is the per-pellet kit value (DEC-RUN-8) with the sim-side 2-target spread riding the line.
4. **Ability routing**: Q press-edge → one accepted cast (burst damage = `kit.Q.Damage *
   kit.Q.Hits`, `Slot` = `AbilitySlot.Q`, `Ability` = kit name) surfaced on
   `ShellBootstrap.LastAbilityOutcome`; holding across pumps issues no second request (per-frame
   spam would overwrite the accepted outcome with `ability_cooling`); E → slot E, pierce hits
   both monsters on the cursor line (pins that the shell fills `EntitiesOnLine` from aim
   geometry); locked rejection (`ability_locked`, fresh account) surfaces without breaking the
   loop (movement works on the next pump); re-press inside cooldown surfaces `ability_cooling`;
   a second press after `kit.QCooldownSeconds` of pumped time casts again.
5. **No double-consumption**: W+SPACE in one snapshot — hero walks (X drift re-pinned zero,
   DEC-017) AND the monster takes damage; planning click still places with SPACE held
   (born-green regression pin).
6. **Thinness** (born-green): `AimLine` static plain C#, `CombatActionConfig` plain data, both in
   the scanned shell assembly (T-10's Cecil invariant covers the pump additions mechanically).

**Flagged to the owner**: shipped defaults chosen at test time — attack cadence 0.25 s (the 014
harness's number), aim line 30 long / 1.5 wide. All config, none pinned by value.

**Deliberately unpinned / ambiguities for the implementer**: kind spelling of non-barricade
placeables on a line (sim ignores every non-monster kind; fixtures only name barricade); whether
the shell also fills `HeroAbilityRequest.TargetId` / `AimDirection` (the sim's own
nearest-on-line fallback makes the line list sufficient for the shipped Gunslinger kit — dash
classes will need `AimDirection` when their routing is graded); ability presses during planning;
width/length boundary in/exclusivity; where inside the pump the routing lives (pump body vs
`LocalHeroIntentSource` vs host path — only pump-observable behavior is locked).

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
