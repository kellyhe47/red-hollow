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
