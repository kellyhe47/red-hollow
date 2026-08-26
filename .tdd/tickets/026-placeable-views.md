---
id: 026
title: Placeable views — placed defenses render and show damage
status: green
depends_on: [025]
touches: [unity/RedHollow/Assets/Game/View/, unity/RedHollow/Assets/Game/UI/]
iterations: 1
test_files: [unity/RedHollow/Assets/Tests/EditMode/T26_PlaceableViewTests.cs]
branch: ""
board_id: T-26
owns_requirements: []
grades_fixtures: []
---

## Scope

Final render audit (2026-08-26): `MatchViewBinder` binds hero and monster views;
`MatchSceneBuilder` places hotspot/spawn markers; **nothing renders a placeable**. A
purchased barricade/trap/turret/med station is invisible, and sells/breaks/destroys change
nothing on screen. Wireframe S3: "Existing placeables shown"; S4: "Barricades show HP bars
when damaged".

Adds placeable view binding through the existing resolver seam:

- Create a view when a placeable exists (state-driven, like monsters — read how the binder
  tracks monster appear/despawn and follow it).
- Remove on sold / broken / destroyed (`Exists` flip; the distinct `placeable_broken` vs
  `placeable_destroyed` events stay distinct for the feel layer — already wired in 013).
- Damage readout for barricades driven by sim state (Hp vs full) — presentation shape
  (scale/color/bar) is free; the *presence when damaged* is the wireframe requirement.

## Acceptance criteria

- [x] a purchased placeable appears at its position through the resolver seam
- [x] sold, broken and destroyed placeables disappear
- [x] a damaged barricade shows a damage readout driven by sim state; Cecil invariant holds

## Test plan

Scope was extended mid-ticket by the orchestrator (verified gap): MatchSceneBuilder creates NO
entry-tunnel markers, so the wireframe's S3 pulse / S4 flare states had no world anchor, and
hotspot markers had no lost/dark state. All in `T26_PlaceableViewTests.cs` (EditMode).

**Placeable view lifecycle (binder, T16/T19 pattern):**
1. `A_placeable_in_state_gets_one_view_at_its_position_through_the_resolver_seam` — one view per
   standing placeable via `VisualClass.Placeable`; art key == the sim's `PlaceableType` constant
   (`"barricade"`); parented under the binder root.
2. `Repeated_syncs_never_duplicate_a_placeable_view` — idempotent Sync, one resolver call, same view.
3. `A_placeable_view_follows_the_entity_position_across_refreshes`.
4. `A_placeable_that_stops_existing_loses_its_view_on_the_next_refresh` — the `Exists` flip is the
   one predicate (sold/broken/destroyed); 013's feel events deliberately NOT re-pinned.
5. `A_barricade_destroyed_by_sim_damage_disappears_on_the_next_refresh` — real
   `ApplyPlaceableDamage` to 0 HP.

**Barricade damage readout (S4):**
6. `A_placeable_view_mirrors_the_sims_hp_and_position` — RenderFrom mirrors, never decides.
7. `A_barricade_at_full_catalog_hp_shows_no_damage_indicator` — denominator is the R-23 catalog
   MaxHp, never a literal.
8. `A_damaged_barricade_shows_an_indicator_whose_fraction_falls_with_hp` — presence + monotone
   `HpFraction` in [0,1]; shape/values unpinned. **Heal case skipped:** the sim has no barricade
   heal path (`TickMedStations` heals HEROES only; nothing raises a placeable's Hp).
9. `The_binder_wires_the_catalog_denominator_and_the_indicator_follows_state` — state-driven via
   `MatchViewBinder.PlaceableCatalog`.

**Scene marker anchors (extension):**
10. `The_built_scene_has_a_marker_on_every_entry_tunnel` — `MatchScene.EntryTunnelMarkers`
    keyed by tunnel index (mirrors HotspotMarkers), resolver-supplied visual,
    `EntryTunnelMarkerView` state component defaulting quiet.
11. `Hotspot_markers_carry_an_identifying_lost_state_component` — `HotspotMarkerView`, not lost
    at build.

**Pump-driven marker states (extension; via `ShellBootstrap.AttachScene`):**
12. `Planning_pulses_exactly_the_previewed_entry_tunnel_markers` — follows
    `PlanningScreenModel.PulsingEntryTunnels`; animation unpinned.
13. `A_wave_spawn_flares_the_previewed_markers_and_the_flare_eventually_clears` — follows
    `CombatHudModel.EntryFlares` (forces the shell to wire `SetExpectedEntryTunnels`); clears by
    the NEXT planning screen at the latest (timing unpinned); planning pulse does not leak into combat.
14. `An_emptied_hotspot_darkens_its_marker_and_only_its_marker` — driven by state (Civilians == 0).

**End to end (T21/T24 pump recipe):**
15. `A_purchase_through_the_shell_yields_a_view_and_a_sell_removes_it` — PlanningScreenModel
    purchase → pump → view; sell → pump → gone.

**New surface (throwing stubs):** `Game/View/PlaceableView.cs`; `Game/View/MarkerViews.cs`
(`EntryTunnelMarkerView`, `HotspotMarkerView`); additive on `MatchViewBinder`
(`BoundPlaceableIds`, `PlaceableViewFor`, `PlaceableCatalog`); additive `MatchScene.
EntryTunnelMarkers` dictionary; additive `ShellBootstrap.Scene` + `AttachScene`. All locked
T16/T19/T21 tests untouched and green at red time.

## Attempt log

_(created 2026-08-26 by the orchestrator at 025 close.)_

- 2026-08-26 green in 1 pass. Red 15/15 verified; green verified by orchestrator: EditMode
  252/252, dotnet 371/371. Play rebuilds the colony through the real art resolver at Awake
  (baked scene copy superseded). Flare decay is phase-gated; marker/damage presentation is
  state-only — animation/tint is feel-layer polish, deliberately unpinned.

## Handoff notes

- `VisualClass.Placeable` already exists on the resolver seam; art keys can follow the
  placeable type string (catalog icons exist under Assets/Game/Art for one representative).
- Binder precedent: monster views appear/despawn from state each refresh (T16/T19/T21 pin
  the pattern) — placeables should ride the same refresh, not a new pump hook.
- `Placeable` entity carries `Exists`, `Hp`, `PurchaseCost`, `OwnerPlayerId`, position;
  full HP for the bar denominator comes from the R-23 catalog.
