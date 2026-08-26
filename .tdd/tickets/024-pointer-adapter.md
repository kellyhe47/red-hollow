---
id: 024
title: Play-mode pointer adapter — mouse ray to the S3 placement seam
status: pending
depends_on: [023]
touches: [unity/RedHollow/Assets/Game/UI/, unity/RedHollow/Assets/Game/Input/]
iterations: 0
test_files: []
branch: ""
board_id: T-24
owns_requirements: []
grades_fixtures: []
---

## Scope

023 pinned S3 placement at a pointer seam (`ShellControls.PointerAt/ClickGround/
ClickPlaceable`) driven with a caller-supplied `zoneValid`; nothing feeds it from a real
mouse. Adds:

1. Cursor ray through the camera onto the ground plane → `Vec2` (via `SimSpace.ToSim` —
   the same path `CursorGroundPoint` already documents for aim).
2. Placeable picking by proximity to the cursor's ground point.
3. A client-side **zone oracle** answering `zoneValid` for the red-tint UX. Advisory only —
   the sim's R-24 verdict stays authoritative (`ClientPrediction` is the precedent for
   client-side mirrors of sim rules).
4. Pump integration during planning; mouse-left drives ground/placeable clicks (R-30
   reserves mouse buttons for UI — placement IS UI, so this is compliant, but no gameplay
   intent may come from them).

## Acceptance criteria

- [ ] during planning, the real mouse moves the ghost; left-click places at the cursor's
      ground point; clicking a standing placeable sells it
- [ ] the zone oracle's answer agrees with the sim's accept/reject verdict across sampled
      positions (property-tested against the live sim)
- [ ] combat aim keeps working; no gameplay intent from mouse buttons (R-30)

## Test plan

_Filled in by the test-writer._

## Attempt log

_(created 2026-08-26 by the orchestrator at 023 close.)_

## Handoff notes

- The zone rule lives sim-side (R-24, ticket 005: hotspot/tunnel-mouth/footprint radii,
  config-tunable — find where the radii live and read them from config/state, never
  hardcode). Property-test the oracle against the sim's actual accept/reject on sampled
  grid positions so drift fails loudly.
- Device mouse reads stay declarative/untestable; test the ray math with a scripted camera
  and the oracle/picking as pure functions.
- `LegacyDeviceInputSource` already ray-casts for combat aim — reuse its plane-projection
  approach or its code path.
