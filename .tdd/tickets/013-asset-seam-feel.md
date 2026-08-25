---
id: 013
title: Asset seam validation and feel pass
status: pending
depends_on: [010]
touches: [unity/RedHollow/Assets/Game/Art/, unity/RedHollow/Assets/Game/Scenes/]
iterations: 0
test_files:
  - unity/RedHollow/Assets/Tests/EditMode/T13_ArtSeamTests.cs
  - unity/RedHollow/Assets/Tests/EditMode/T13_LightingTests.cs
  - unity/RedHollow/Assets/Tests/EditMode/T13_FeelTests.cs
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

Three EditMode suites (locked), plus throwing contract stubs so the assembly compiles and fails
red with `NotImplementedException`. Asset-existence tests fail naturally until the four
representative files are copied in.

**Contract stubs (implementer fills these in):**
- `Assets/Game/Art/ArtCatalog.cs` — artKey→factory table, pure data (`Register`, `Contains`,
  `Keys`, `TryInstantiate`).
- `Assets/Game/Art/ArtVisualResolver.cs` — `IVisualResolver` chained IN FRONT of the ticket-016
  fallback (which deliberately never probes). Known key → real art, `IsPlaceholder=false`;
  unknown/null/empty → the fallback's own handle. Never null, never throws.
- `Assets/Game/Art/LanternDeepLighting.cs` — `Apply(MatchScene)`: R-15 RenderSettings + sourced
  lights + cavern dome. (May also be folded into `MatchSceneBuilder.Build`; Apply must still work.)
- `Assets/Game/View/Feel.cs` — `FeelRouter` / `FeelCue` / `EntityFeelState` / `FeelRig`, all plain
  C# (T10 Cecil invariant keeps them out of MonoBehaviours).
- `MatchScene.CavernDome` field added (null until lit).

**T13_ArtSeamTests** (AC: shippable placeholder build / nothing blocks on assets / pure asset swap)
- registered key → real art, `IsPlaceholder=false`, fallback never consulted
- unknown/null/empty key → delegates to fallback, returns its very handle
- totality: hostile keys × all VisualClasses — never throws, never null
- `Register` at runtime flips a key placeholder→real through an UNCHANGED resolver ("pure asset
  swap", pinned behaviorally — an IL no-branching scan was rejected as over-pinning)
- catalog mapping inspectable (`Keys`, `Contains`, `TryInstantiate` absence≠error)
- structural: art/feel layer is plain C#, no MonoBehaviours
- 4 imported representative assets via `AssetDatabase` (exact sources below): tile 1024² + wrap
  Repeat; character 512²; icon 256²; UI frame exact 320x32 (NPOT preserved) + alpha survives import

**T13_LightingTests** (R-15 Lantern Deep — bounds, not exact numbers; painterly values → playtest)
- ambient: Flat mode, near-black (max channel ≤0.25, >0), warm (r≥g≥b, r>b)
- fog on, fog color warm (r≥b)
- no skybox material, no `RenderSettings.sun`, no enabled directional light under the scene root
  (retires the pre-013 placeholder KeyLight)
- ≥1 enabled warm point light (r>b, intensity>0)
- `CavernDome`: rendered geometry under Root, top above ground plane, bounds span the play area
- RenderSettings saved/restored per test (global editor state)

**T13_FeelTests** (R-64 — binding/cue-key layer; audio actually playing = playtest)
- all 17 handoff events have bindings; missing/renamed event fails loudly by name
- every binding a distinct EffectKey; `placeable_broken` vs `placeable_destroyed` explicitly
  distinct, cue TargetId from `placeable_id`
- wave start/end distinct AudioKeys; victory/defeat distinct
- `monster_damaged` → target flashes (observable `EntityFeelState`) + nonzero nudge; bystanders
  neutral
- nudge is presentation-only: `FeelRig.Apply` puts transform at WorldPosition+offset;
  `Monster.Pos` and `view.WorldPosition` untouched (R-51)
- feel decays: after 15s of `Tick` flash off, nudge sprung back, view exactly authoritative again
- totality: unbound/null/malformed events and null views never throw; unbound → null cue

**Representative source assets (copy EXACTLY these, a file operation — never re-run a pipeline):**
- `art/textures/cavern-ground_v1_1024.png` → `unity/RedHollow/Assets/Game/Art/Textures/cavern-ground_v1_1024.png`
- `art/characters/gunslinger-portrait_v1_512.png` → `.../Art/Characters/gunslinger-portrait_v1_512.png`
- `art/icons/gs-revolver-shot_v1_256.png` → `.../Art/Icons/gs-revolver-shot_v1_256.png`
- `art/ui/button-normal_v1_320x96.png` → `.../Art/UI/button-normal_v1_320x96.png` (verified real
  alpha, min 0 / max 255; the icon/texture/portrait sources carry none — alpha is asserted on the
  UI class only). Originally `hp-bar-frame_v1_320x32.png`; re-targeted, see attempt log.

**Ambiguities flagged:**
- Handoff's R-64 list says `wave_complete`/`combat_started`; sim also emits `wave_spawned`,
  `monster_killed`, `xp_awarded`, `level_up`, `planning_started`, rejection events — NOT pinned as
  feel (explicitly asserted unbound-is-fine, not unbound-is-required, except `xp_awarded` pinned
  as having no binding to make "the list is the contract" concrete).
- Nudge direction/magnitude, flash duration/color, cue-key spellings: free (playtest).
- Icons ship without alpha (full-bleed 1024-derived); if set-consistency review wants cut-out
  icons that's an art-pipeline change, not this ticket's.

## Attempt log

- 2026-08-25 (test-writer, orchestrator-directed) — re-targeted the representative UI asset from
  `art/ui/hp-bar-frame_v1_320x32.png` to `art/ui/button-normal_v1_320x96.png`. The original is
  defective at source: RGBA format but every alpha pixel is 255 (10 of 20 art/ui PNGs are all-opaque
  — defeat-banner_v3, dialog-panel_v1, hp-bar-fill_v1, hp-bar-frame_v1, hud-topbar_v1, shop-bar_v1,
  slot-frame-locked_v1, slot-frame_v2, toast-banner_v2, xp-bar-fill_v1, xp-bar-frame_v1), so Unity's
  content-based `DoesSourceTextureHaveAlpha` honestly answers false and the locked alpha pin could
  never pass. The test CONTRACT (alpha survives import, exact NPOT size preserved) is unchanged and
  correct — the seam test catching a defective delivered asset is this ticket doing its job.
  Replacement verified: 320x96 (still NPOT), alpha min 0 / max 255. Regenerating the ten flat-alpha
  UI assets is the UI-props pipeline's job, escalated separately; 013 does not block on it.

- ~~BLOCKED (environment, pre-run)~~ RESOLVED 2026-08-25 — owner installed Unity. Original note: Unity Editor is not installed on this machine and needs the owner's Unity account/licence. `unity/RedHollow/` currently holds only `Assets/GameSim` — there is no Unity project (no ProjectSettings/, no Packages/). T-01..T-09 carry the entire 30-fixture acceptance contract and need no Unity.

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
- UNBLOCKED: Unity installed. art/textures and art/characters already hold real assets to smoke-test
  the seam against; check art/asset-log.csv first, a Comfy agent is still writing there.
