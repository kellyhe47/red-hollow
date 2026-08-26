# Agent 4 — UI & props pipeline (Comfy Cloud)

## Mission
Build and commit a ComfyUI (cloud.comfy.org) pipeline producing **UI chrome and world prop art** for The Red Hollow. Read `docs/comfy-prompts/00-shared-style.md` first; obey its style tail ("Lantern Deep", DEC-025), sampler locks, and process rules. UI chrome reads as lantern-lit weathered wood/brass/riveted plate inside the burnt-sienna monochrome — matte, dusty, no specular sparkle, no clean metal; props (string lights, lantern post, antenna) are the in-world light sources that carry the style in Unity. Screens and HUD layout this art must serve: `docs/ui-wireframes.html` (S1–S7) and `docs/PRD.md` §10.

## Failure mode you own (from the art plan)
This is the **hardest class to get clean**: it needs **real transparency and exact pixel sizes**. Diffusion models fake alpha with checkerboards and halos. Your mandatory alpha workflow:
- **Alpha-recover technique:** render every transparent asset twice with the same fixed seed — once on pure white, once on pure black — and subtract to recover a true alpha matte (soft edges survive). Build this as a two-branch workflow, not a manual step.
- Deliver at the exact target pixel sizes below; UI cannot be "roughly" sized.

## Pipeline to build
1. Comfy Cloud txt2img → lock seed → first image.
2. Two-branch white/black background workflow + alpha subtraction (Comfy Cloud image-math nodes) → RGBA PNG out.
3. `{item}` token; UI framing prompt baked into workflow: `game UI element, weathered wood and brass western frame, riveted plate, clean edges, flat front-on view` + style tail.
4. Export Save (API Format) → `art/workflows/ui_props.json` (+ `ui_alpha.json`); commit; log per asset.

## Items list — UI chrome (RGBA, exact sizes)
1. HUD top-bar frame 1920×120 · 2. shop-bar panel 1200×160 · 3. ability slot frame 96×96 (+ locked-padlock variant) · 4. HP bar frame + fill 320×32 · 5. XP bar frame + fill 480×24 · 6. wooden button (normal/hover/pressed) 320×96 · 7. level-up choice card 360×480 · 8. dialog/panel background 800×600 (9-sliceable: keep detail in a 64px border, flat center) · 9. toast banner 640×96 · 10. wave banner scroll 800×200 (empty center — Unity renders "WAVE N") · 11. victory laurel banner frame 1000×400 (empty center — Unity renders "THE COLONY STANDS") · 12. defeat banner frame 1000×400 (empty center — Unity renders "THE COLONY HAS FALLEN") · 13. cursor + crosshair 64×64 · 14. team spawn banner/flag marker 256×256

## Items list — world props (RGBA where free-standing)
15. water tower · 16. string lights strand · 17. hay bale · 18. barrel cluster · 19. cactus-in-pot (terraform garden) · 20. hitching post · 21. colony antenna mast · 22. lantern post · 23. saloon sign blank (no text — Unity overlays text) · 24. tumbleweed · 25. SALOON facade (hotspot building front, weathered grandeur) · 26. CHAPEL facade (steeple, stained tin windows) · 27. HOMESTEAD facade (ranch house + porch) — facades are texture/reference art for the Unity greybox hotspot meshes (PRD R-15); deliver front-on, flat-lit, 1024²

## Verification (mandatory, per asset)
- Alpha check: composite over magenta AND over dark blue; no halos, no checkerboard remnants, no white fringe.
- Size check: delivered PNG dimensions exactly match the table; scripted, not eyeballed.
- Text rule: NO baked text anywhere (style tail already forbids it) — all labels are rendered by Unity.

## Control rung
Fixed seed + the dual-render alpha branch (rung 2 + technique). img2img off an approved frame to keep chrome consistent across the set. No LoRA.

## Pipeline-specific overrides (locked 2026-08-25, after first drafts)

- **Workflow:** Comfy Cloud "UI & Props". Canonical export: `art/workflows/ui_props.json` (single file — the two alpha branches live in it, so no separate `ui_alpha.json`).
- **Alpha, as built:** the mandated dual-render (white/black, shared seed) branch exists and its three outputs are saved every run (`ui/white`, `ui/black`, `ui/rgba`). But the two branches *diverge in interior detail* (same seed, different background conditioning), so the subtraction matte alone ships ~75% semi-alpha interiors — unusable directly. **Primary matte is a `BiRefNetRMBG` node** (model `BiRefNet-general`, `refine_foreground: true`, background Alpha) run on the black-branch decode → `ui/rgba2` output. Dual-render outputs are kept as the mandated cross-check and as raw material for local recovery on any asset where BiRefNet mis-judges subject-vs-background.
- **Broken on cloud:** `LayerMask: LoadBiRefNetModelV2` / `BiRefNetUltraV2` fail with an HF repo-id error (both "BiRefNet-General" and "RMBG-2.0"). Use `BiRefNetRMBG` (RMBG node pack) instead.
- **Framing contract (② node):** `game user interface element of the named subject, (a single isolated object, nothing else in frame:1.2), (flat front-on orthographic view:1.2), weathered dark wood and aged brass western frame style, riveted iron plate, crisp clean silhouette edges, centered composition, monochromatic burnt-sienna palette, warm white-gold lantern glow accents, matte painterly semi-realism, muted matte dusty surfaces, no text, no watermark` — scene tail stripped per 00-shared-style's icons/UI exemption.
- **Negative additions:** `checkerboard pattern, transparency grid` appended to the contract negative.
- **Sizes:** SDXL can't render the extreme UI aspect ratios (1920×120 etc). Generate at SDXL-safe sizes (table in `art/ui-props-plan.md`), deliver exact target sizes via scripted lanczos resize in `art/tools/ui_deliver.py` (which also runs the mandatory alpha-halo and size checks).
- Runs queue via `app.queuePrompt(0,1)` from the console (same as icons pipeline).

## Framing variants + bar fallback (locked after full set, 2026-08-25)

- **② FRAMING props variant** (used for items 15–24): `game environment prop of the named subject, (a single isolated object, nothing else in frame:1.2), (three-quarter view from slightly above:1.2), weathered frontier colony materials of wood iron brass and canvas, crisp clean silhouette edges, centered composition, monochromatic burnt-sienna palette, warm white-gold lantern glow accents, matte painterly semi-realism, muted matte dusty surfaces, no text, no watermark`
- **② FRAMING facade variant** (items 25–27, with both BG nodes set to `(the weathered building surface fills the entire frame:1.3)`): `flat front-on orthogonal elevation view of the named building facade, game environment texture reference art, the facade fills the frame edge to edge, evenly flat lit, weathered frontier colony architecture, monochromatic burnt-sienna palette, warm white-gold window glow accents, matte painterly semi-realism, muted matte dusty surfaces, no text, no watermark`. Facades render with painterly sky behind — delivered as the BiRefNet cutout (RGBA), which removes the sky and satisfies no-sky.
- **Full-bleed chrome ships opaque:** bars/panels that span their rect (hud-topbar, shop-bar, dialog, toast, slot frames, defeat panel) are delivered from the black-branch raw with `--opaque`; matting only shaped silhouettes.
- **Opaque-alpha escalation (ticket 013, resolved 2026-08-25):** the seam test flagged 11 `art/ui/` PNGs as RGBA-with-all-255-alpha. Reviewed against this contract: 10 of the 11 (hud-topbar, shop-bar, slot-frame, slot-frame-locked, toast-banner, defeat-banner, hp/xp frames and fills) are full-bleed chrome shipped opaque **by the rule above — working as intended**, not a bypassed matte step. The one real defect was `dialog-panel_v1` (panel art inset in a baked light-gray background); fixed as `dialog-panel_v2_800x600.png` by cropping the panel region from v1 and lanczos-resizing (file op, no re-run, no credits). Unity's ticket-013 representative UI asset was re-targeted to `button-normal_v1` (real alpha) — do not re-target it back to hp-bar-frame.
- **HP/XP bars cannot be generated at 10:1–20:1 aspect** — three attempts each produced pipes/garbage (SDXL). Locked fallback: frames are the toast-banner_v2 plaque art resized (file op, allowed); fills are a crop of the generated white-gold glow strip; HP fill is tinted red at runtime via Unity UI Image color. Do not spend more credits re-attempting thin bars.
