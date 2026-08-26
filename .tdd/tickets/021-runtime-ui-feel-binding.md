---
id: 021
title: Runtime binding — UI screens, feel router and art resolver into the playable shell
status: pending
depends_on: [012, 013, 019]
touches: [unity/RedHollow/Assets/Game/UI/, unity/RedHollow/Assets/Game/View/]
iterations: 0
test_files:
  - unity/RedHollow/Assets/Tests/EditMode/T21_ShellUiBindingTests.cs
  - unity/RedHollow/Assets/Tests/EditMode/T21_FeelArtBindingTests.cs
branch: ""
board_id: T-21
owns_requirements: []
grades_fixtures: []
---

## Scope

Found by the §5 audit (2026-08-25): 012's screen models, 013's `FeelRouter` and
`ArtVisualResolver` are implemented and locked-tested, but **nothing at runtime constructs
them**. No Canvas renders any wireframe screen; sim events never reach the feel layer;
`MatchViewBinder(visuals: null)` defaults to the placeholder resolver everywhere.
(`LanternDeepLighting` IS wired — `MatchSceneBuilder` applies it.)

Binds all three into the playable bootstrap so a launched build shows S1–S7, plays feel
effects, and resolves real art.

## Acceptance criteria

- [ ] a launched scene renders the UI screens through the 012 models — visible UI elements
      bound to model state, screen switching driven by `UiRouter`
- [ ] the host's sim event stream reaches `FeelRouter`; feel state applied to views each frame
- [ ] `MatchViewBinder` receives an `ArtVisualResolver` whose catalog registers the imported
      representative assets, chained over the placeholder
- [ ] the Cecil invariant still holds — UI/feel MonoBehaviours never write sim state

## Test plan

_(test-writer, 2026-08-25.)_ Two EditMode suites (10 tests) over a new composition-root contract,
plus throwing stubs. Everything is driven T-19-style: explicit `Pump` calls, no frames, no PlayMode.

### Contract defined (throwing stubs, `RedHollow.Game.UI`)

- `ShellBootstrap` (`Assets/Game/UI/ShellBootstrap.cs`) — the shell composition root. Plain C#.
  Owns: a `NetSession` whose matches are created view-bound (not 011's headless default), the
  `UiRouter` + models + built `ShellUi`, the `FeelRouter`, one `MatchViewBinder` over an
  `ArtVisualResolver`(catalog → placeholder). `Pump(dt)` is one presentation frame: collect
  not-yet-delivered sim events (including ones still in `LastObservation` from direct sim calls
  between pumps — collected BEFORE stepping), step the session, route events (UiRouter / HUD model /
  FeelRouter), `Feel.Tick(dt)`, refresh router+models+labels+screen activation, then `FeelRig.Apply`
  per monster view. `Pump(0)` = pure refresh (nothing moves — used to make "sim untouched" exact).
  Also `TearDown()` and `static LoadRepresentativeArt()`.
- `ShellBootstrapOptions` — transport/profiles/config/map/local peer+account/catalog, all defaulted
  (null catalog → the representative default).
- `ShellArtKeys` — the four registered spellings; the character key IS `HeroClass.Gunslinger`
  ("gunslinger"), because `MatchViewBinder` resolves heroes by class literal.
- `ShellUi` (`Assets/Game/UI/ShellUi.cs`) — handle object: `Root` (named "RedHollow_Shell"),
  `Canvas`, `ScreenRoot(UiScreen)` (one distinct container per S1–S7, switched by activation), and
  uGUI `Text` labels `WaveLabel`/`ScripLabel`/`HpLabel`/`MonstersRemainingLabel`/`HotspotLabels`.
- asmdef change: `UnityEngine.UI` added to `RedHollow.Game` and `RedHollow.Tests.EditMode`
  references (uGUI Text; the package is already resolved, builtin 2.5.0).

### T21_ShellUiBindingTests (AC1, AC2-UI-side, AC4)

1. `The_bootstrap_builds_one_canvas_with_a_distinct_per_screen` — Canvas + 7 distinct roots under
   "RedHollow_Shell", before any match exists.
2. `Exactly_the_routed_screens_root_is_active_and_follows_the_flow` — S2 → S4 → S5 → S3; the S5
   entry only happens if the sim's own `wave_complete` (last `RecordMonsterKill`) reaches
   `UiRouter.OnSimEvent` through the pump — no test calls it by hand.
3. `A_finished_match_lands_on_its_post_match_screen_root` (×2 cases) — victory (final-wave clear)
   and defeat (colony emptied) roots after one pump.
4. `The_hud_labels_show_the_models_values_after_a_pump` — wave/scrip/HP/monsters-remaining/per-
   hotspot civilian labels, values read off state (containment only; copy free), labels inside the
   hierarchy.
5. `A_state_change_reaches_the_labels_on_the_next_pump` — sentinel scrip 4917 / HP 73, checked
   absent first (anti-snapshot).
6. `A_civilians_killed_event_reaches_the_hud_model_through_the_pump` — R-13 red flash + toast off
   the sim's own `ApplyHotspotAttack`.
7. `The_binding_layer_is_plain_C_sharp…` — guard, GREEN on stubs by design (T-19 AC6 precedent);
   T10's Cecil scan stays the enforcement.

### T21_FeelArtBindingTests (AC2, AC3)

1. `A_monster_damaged_event_flashes_and_nudges_the_view_and_never_the_sim` — real
   `ResolveHeroAttack` → `Pump(0)` → view flashing, transform displaced by exactly the router's
   nudge, `monster.Pos` EXACTLY unchanged and `WorldPosition` still the sim's answer.
2. `The_flash_expires_and_the_nudge_decays_as_pumps_tick` — 1s of pumps (generous bound; duration
   is playtest's): not flashing, offset < 25% of initial, measured against `WorldPosition` so real
   movement can't fake decay.
3. `The_default_catalog_registers_the_four_representative_assets_as_real_art` —
   `LoadRepresentativeArt()`: four keys registered, each `IsPlaceholder == false` with an instance
   through a chained resolver; "shambler" honestly placeholder; character-key spelling pinned to
   the class literal.
4. `The_bootstraps_binder_dresses_a_hero_in_real_art_and_a_shambler_in_the_placeholder` — end to
   end through the bootstrap's own binder in a driven match, plus both directions through the
   exposed resolver.

### Red verification (2026-08-25, headless EditMode run)

154 total = 142 prior (all still green) + 12 new (10 methods; the post-match test is 2 cases).
143 passed / 11 failed — every failure is `NotImplementedException` out of the T21 stubs. The one
new green is the plain-C#-shape guard, green-on-stubs by design (T-19 AC6 precedent).

### Deliberately unpinned / notes for the implementer

- Label copy/format, layout, fonts, render mode, where labels sit (beyond "inside the shell root"),
  whether a screen deactivates itself or via parent.
- Nudge direction/magnitude, flash duration (only "present then gone" within 1s), audio.
- How `LoadRepresentativeArt` loads assets at runtime (Resources copy, serialized catalog — NOT
  AssetDatabase; and T-13's imported paths must stay where its locked tests read them).
- The scene build (`MatchSceneBuilder`/ground real art via a Ground artKey) is NOT pinned — the
  builder currently resolves Ground with a null key; wiring it to `ShellArtKeys.GroundTile` is
  implementer's choice, no test demands it.
- Event-feed caveat pinned honestly: out-of-band events survive only until a later command
  overwrites `LastObservation`; every test issues the emitting command last before pumping. The
  implementer will need a per-command tap on the host seam (bootstrap-created factory can wrap
  `HostedMatch`'s host/session) plus a pre-step drain; delivery is exactly-once in order.

## Attempt log

_(created 2026-08-25 by the handoff-2 orchestrator after the §5 recheck.)_
