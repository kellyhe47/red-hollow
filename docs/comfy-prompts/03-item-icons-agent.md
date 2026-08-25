# Agent 3 — Item icons pipeline (Comfy Cloud)

## Mission
Build and commit a ComfyUI (cloud.comfy.org) pipeline producing the full **icon set** for The Red Hollow's shop bar, ability slots, and HUD. Read `docs/comfy-prompts/00-shared-style.md` first; obey its style tail ("Lantern Deep", DEC-025), sampler locks, and process rules. Icons live in the burnt-sienna monochrome with warm white-gold rim/lantern light on the badge; no teal, no cool tones — status accents (breach warning, slow) may use the sanctioned alarm accents sparingly. Icon meanings: `docs/PRD.md` §6 (placeables), §7 (abilities), §8 (XP).

## Failure mode you own (from the art plan)
**~30 icons, all matching.** One icon is easy; a *set* that reads as one hand is the job. That demands ONE locked seed, ONE style tail in the workflow file, ONE model and size — change any of the three mid-run and the set stops matching. If you improve the style mid-set, regenerate the entire set from the forked workflows; never ship a mixed batch.

## Pipeline to build
1. Comfy Cloud txt2img template → lock seed → first image.
2. Icon framing baked into the workflow prompt (not per item): `game item icon, centered single object, 3/4 view, dark leather-and-brass circular badge background, subtle rim light, thick readable silhouette` + style tail. `{item}` token for the subject.
3. Generate 1024², deliver 256² and 128² downscales (icons must stay readable at 64px on the shop bar — squint test every one).
4. Fork one workflow per icon with the seed pinned (`workflow_kit fork` pattern from the art plan). Export Save (API Format) → `art/workflows/item_icon.json`; commit.

## Items list
Placeables (shop bar): 1. wooden barricade 2. spike trap 3. dynamite bundle trap 4. brass auto-turret 5. med station (red-cross lantern crate)
Economy/HUD: 6. scrip coin 7. sell/refund tag 8. skill point star badge 9. XP vial 10. civilian (huddled figures) 11. hotspot shield 12. wave skull-counter 13. entry-tunnel breach warning
Abilities (per hero: basic/Q/E): Gunslinger 14. revolver shot 15. fan-the-hammer 16. deadeye piercing bolt · Rancher 17. shotgun blast 18. lasso 19. stampede charge · Sawbones 20. saw-sword cleave 21. whirl spin 22. bulwark shield
States: 23. locked ability padlock 24. cooldown clock 25. slow (rope-wrapped boot) 26. respawn hourglass

## Verification
Set-consistency pass: contact-sheet all icons in one grid; identical background badge, palette, lighting direction, and object scale. Any outlier → regenerate from its fork, same seed, adjusted `{item}` wording only. Log model/seed/prompts per icon.

## Control rung
Fixed seed (rung 2) is the design center for this class. img2img off the badge template if the background drifts. No ControlNet/LoRA.

## Pipeline-specific overrides (locked 2026-08-25, after 5 drafts)

The doc's original framing line ("centered single object … circular badge background") **fails on SDXL**: the item either tiles the whole frame (no badge) or the badge wins and the item is lost. The locked contract uses prompt weighting and lives in the workflow's ② FRAMING node (canonical copy: `art/workflows/item_icon.json` node 3):

> `game item icon of the named object, the object is the main subject displayed centered on top of (a dark leather-and-brass circular badge medallion:1.2), (flat pitch-black dark background surrounding the badge:1.4), 3/4 view, thick readable silhouette, warm white-gold lantern rim light, monochromatic burnt-sienna palette, matte painterly semi-realism, muted matte surfaces, no text, no watermark`

- ITEM token format: `(<visual description of object>:1.2)` — describe what it *looks like*, not its game function; keep weight at 1.2 (1.35 kills the badge, 1.0 loses the item).
- Scene terms (cavern vista, volumetric haze, settlement) are stripped per 00-shared-style's icons/UI exemption; palette/matte/no-cool-tones rules kept.
- Full scene tail in the shared doc is NOT used here (vista terms leak architecture into the badge).
- Workflow: Comfy Cloud "Item Icons Pipeline". Runs queue reliably via `app.queuePrompt(0,1)` from the console; the Run button click sometimes doesn't register.
- Delivery: SaveImage ×3 (1024/256/128, `area` downscale). Draft prefix `icons/<sz>/<slug>_draft`, keeper prefix `icons/<sz>/<slug>_v<N>`.
