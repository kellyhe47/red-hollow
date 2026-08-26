---
id: 024
title: Play-mode pointer adapter — mouse ray to the S3 placement seam
status: green
depends_on: [023]
touches: [unity/RedHollow/Assets/Game/UI/, unity/RedHollow/Assets/Game/Input/]
iterations: 1
test_files: [unity/RedHollow/Assets/Tests/EditMode/T24_PointerTests.cs]
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

- [x] during planning, the real mouse moves the ghost; left-click places at the cursor's
      ground point; clicking a standing placeable sells it
- [x] the zone oracle's answer agrees with the sim's accept/reject verdict across sampled
      positions (property-tested against the live sim)
- [x] combat aim keeps working; no gameplay intent from mouse buttons (R-30)

## Test plan

`unity/RedHollow/Assets/Tests/EditMode/T24_PointerTests.cs` (Unity NUnit, EditMode). Stubs
(throwing `NotImplementedException`): `Game/Input/PointerProjection.cs`,
`Game/Input/PlaceablePicker.cs`, `Game/UI/PlacementZoneOracle.cs`. Pump wiring needs NO new
public seam — the tests drive the EXISTING `IInputSource` (cursor ground point + a held
`PlayerKey.MouseLeft`); implement the routing inside `ShellBootstrap.Pump`/planning refresh.

1. **Ray math (pure)** — `PointerProjection.TryScreenToGround(Camera, Vector2, out Vec2)`:
   round-trips sim points through the REAL `MatchSceneBuilder` top-down camera (given a
   RenderTexture so EditMode screen space has pixel dimensions); false (never throw) for a
   horizon-parallel ray, a ground point behind the camera, and a null camera.
2. **Picking (pure)** — `PlaceablePicker.Pick(MatchState, Vec2, double pickRadius)`: nearest
   standing wins; nothing beyond radius; `Exists=false` never picked (a farther standing one
   wins instead); boundary INCLUSIVE at the radius (matches the sim's edge-inclusive auras).
3. **Zone oracle (property-tested vs the real sim)** — `PlacementZoneOracle(ColonyMap)` with
   settable `HotspotBuildingRadius`/`EntryTunnelMouthRadius`/`PlaceableFootprintRadius`
   (defaults MUST mirror a fresh `MatchSim`'s — pinned by reading the sim's, not literals),
   `WouldAccept(MatchState, Vec2)`: 17×17 grid over ColonyMap.V1 plus edge-straddling samples
   at every hotspot/tunnel, fresh generously-funded planning-phase scratch sim per sample,
   verdict == `MatchSim.PurchasePlacement(...).Accepted` (with `ZoneValid:true` sent as a lie
   the sim must ignore); same around a deliberately placed standing obstacle + a sold one;
   retuned-radius test proves the radii are read, not hardcoded. Anti-vacuity: both verdicts.
4. **Pump integration (device faked)** — during planning the sampled cursor drives
   `PointerAt` with the oracle's answer (hover hotspot → GhostInvalid, hover clear → valid,
   hover inside a standing placeable's clearance → invalid = LIVE state); a fresh MouseLeft
   press is ONE click (held across pumps neither re-buys nor sells the fresh placement):
   ghost up → ClickGround at cursor (catalog-priced purchase / R-24 rejection keeps ghost);
   no ghost + cursor on a standing placeable → ClickPlaceable sells for the modeled refund;
   ghost up + cursor on a standing placeable → placement attempt (overlap-rejected), NEVER a
   sale (T23's ClickPlaceable-ignored-while-ghost precedence); no ghost + clear ground →
   nothing (LastSellRefused stays false — the click routed nowhere).
5. **Combat** — a combat-phase click over a standing placeable buys/sells nothing (planning
   pointer path is planning-only) and held W with MouseLeft held still walks the hero (T22's
   path untouched). Mouse-button no-gameplay-INTENT is already locked in T16 — not re-pinned.
6. **Thinness guard** — the three new types are plain C# in the scanned shell assembly
   (born-green, T-10 convention).

## Attempt log

_(created 2026-08-26 by the orchestrator at 023 close.)_

- 2026-08-26 green in 1 pass. Red 18/22 verified; green verified by orchestrator: EditMode
  218/218, dotnet 371/371. Final playability audit found T-25 (SPACE/Q/E intents consumed
  by nobody) — boarded before the owner ping.

## Handoff notes

- The zone rule lives sim-side (R-24, ticket 005: hotspot/tunnel-mouth/footprint radii,
  config-tunable — find where the radii live and read them from config/state, never
  hardcode). Property-test the oracle against the sim's actual accept/reject on sampled
  grid positions so drift fails loudly.
- Device mouse reads stay declarative/untestable; test the ray math with a scripted camera
  and the oracle/picking as pure functions.
- `LegacyDeviceInputSource` already ray-casts for combat aim — reuse its plane-projection
  approach or its code path.
