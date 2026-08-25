---
id: 013
title: Asset seam validation and feel pass
status: blocked
depends_on: [010]
touches: [unity/RedHollow/Assets/Game/Art/, unity/RedHollow/Assets/Game/Scenes/]
iterations: 0
test_files: []
branch: ""
board_id: T-13
owns_requirements: [R-15, R-64]
grades_fixtures: []
---

## Scope

Swappable asset references throughout; wire one representative asset per class (tile, icon, UI frame, character) early to validate Unity import settings, exact pixel sizes, alpha handling and detail at real camera height. Report contract violations back to the owner. Lantern Deep scene lighting (dark warm ambient, amber point lights, fog, rock-dome sky). Hit flash, knockback nudge, wave stingers, western-twang UI audio.

## Acceptance criteria

- [ ] placeholder-art build is shippable
- [ ] no code path blocks on an asset existing
- [ ] generated art drops in as a pure asset swap

## Test plan

_Filled in by the test-writer._

## Attempt log

- BLOCKED (environment, pre-run): Unity Editor is not installed on this machine and needs the owner's Unity account/licence. `unity/RedHollow/` currently holds only `Assets/GameSim` — there is no Unity project (no ProjectSettings/, no Packages/). T-01..T-09 carry the entire 30-fixture acceptance contract and need no Unity.

## Handoff notes from the sim run (read before starting)

`GameSim` has zero UnityEngine references and no asset knowledge whatsoever — every visual is the
shell's, so nothing here can block on an asset existing.

Events to hang feel on (R-64): `monster_damaged`, `hero_damaged`, `hero_died`, `hero_respawned`,
`civilians_killed`, `hotspot_emptied`, `placeable_created`, `placeable_triggered`,
`placeable_broken` (a spent trap) vs `placeable_destroyed` (a wall collapsing — deliberately
distinct so they can have different effects), `turret_fired`, `status_applied`, `status_expired`,
`wave_complete`, `combat_started`, `match_victory`, `match_defeat`.

Art already in the repo: `art/textures/` (8 tile sets, 512/1024 + normal/AO + seam checks) and
`art/characters/` — a Comfy agent is still writing there, so check `art/asset-log.csv` before
importing in bulk.
