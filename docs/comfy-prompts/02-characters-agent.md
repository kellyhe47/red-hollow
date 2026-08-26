# Agent 2 — Characters pipeline (Comfy Cloud)

## Mission
Build and commit a ComfyUI (cloud.comfy.org) pipeline producing **character art for The Red Hollow**: the 3 hero classes and 5 monster types — lobby/HUD portraits and full-body turnaround sheets. Read `docs/comfy-prompts/00-shared-style.md` first; obey its style tail ("Lantern Deep" palette/lighting, DEC-025; presentation override DEC-026), sampler locks, and process rules. Characters render as lantern-lit figures in the burnt-sienna monochrome: color keys below are *value/material* distinctions within that palette (charcoal, brass, oxblood, bone all sit inside it); the Spitter's acid-green is the sanctioned gameplay-alarm accent. Full character specs: `docs/PRD.md` §7 (heroes) and §5 (monsters).

**In-engine use (DEC-026):** these are **2D painted sheets** used as standing cards/billboards in 3D space (2.5D). v1 is **not** 3D sculpted/rigged character meshes — that is explicitly deferred (hard lift). Western silhouettes stay on these 2.5D standing cards. Environment western is **wear on Lykos forms** (~30%), not a cowboy main street — see `00-shared-style.md`.

## Failure mode you own (from the art plan)
**Same face in every image.** Identity drift across poses/angles is your defect class — measured on the walk-cycle demo as 13.9% scale drift and 11px baseline wander from prompt-only generation. Prompt-only is NOT acceptable here; you must climb the control ladder.

## Control rung (this is the one class allowed to climb high)
1. Fixed seed + `{item}` workflow → one approved **canon image** per character.
2. **IP-Adapter** with the canon image as reference for every additional pose/angle — locks the look without a training run.
3. **ControlNet** (openpose or depth) to lock pose per turnaround frame: front / side / back at matching scale and baseline.
4. **LoRA only if the 3 heroes still drift after IP-Adapter + ControlNet** (half-day cost; heroes only, never monsters).

## Cast (items list)
Heroes (human, **western silhouettes** — keep; these sheets become 2D standing cards, DEC-026 — distinct color keys):
1. **Gunslinger** — lean duster coat, twin revolvers, low hat; color key: charcoal + brass
2. **Rancher** — stocky, shotgun, coiled lasso on hip; color key: oxblood + rope-tan
3. **Sawbones** — broad tank build, heavy apron-armor, bone-saw sword; color key: bone-white + rust

Monsters (virus-mutated colonists/livestock, zombie-western horror, readable as standing cards at tilted-isometric distance):
4. **Shambler** — mutated colonist, tattered settler clothes
5. **Ravager** — mutated cattle-dog, fast quadruped
6. **Spitter** — bloated colonist, acid-green throat sacs
7. **Burrower** — mutated hog, digging claws, rock-crusted hide
8. **Bull Behemoth** — massive mutated bull, exposed ribs, broken horns

## Deliverables
Per character: 1 canon full-body (1024², front 3/4, neutral pose, plain dark background) — this is the **standing-card / billboard** sheet in the 3D cavern — 1 turnaround sheet (front/side/back, matched scale; extra card angles + marketing, not v1 3D modeling reference), 1 head-and-shoulders portrait 512² (heroes also get a greyed "dead/respawning" variant). Verify each set: consistent silhouette, colors, and gear across all frames — measure, don't eyeball (overlay frames; feet baseline within a few px, scale within ~3%).

Export Save (API Format) → `art/workflows/character.json` (+ `character_ipadapter.json`); commit; log model/seed/prompts per asset.

## Pipeline-specific override — ControlNet unavailable on Comfy Cloud (2026-08-25)

Verified during build: any prompt whose sampler chain includes `ControlNetLoader` (tried
`controlnet-openpose-sdxl-1.0`, `controlnet-union-sdxl-1.0`, and `ControlNetApply` +
`ControlNetApplyAdvanced`) is **silently pruned server-side** — the job reports
`execution_success` in ~1s but plans/executes only the non-CN output nodes. No node_errors
returned; the model names appear in `object_info` but the loader never validates. The
`OpenposePreprocessor` itself works (kept parked in the graph as a diagnostic — it proves a
clean skeleton extracts from each canon).

**Effective control ladder for this pipeline** (rung 3 replaced):
1. Fixed seed 20260824 + `{item}` canon (txt2img, `character.json`).
2. IP-Adapter (`IPAdapterUnifiedLoader` preset "PLUS (high strength)", weight 0.85,
   end_at 0.9) with the canon as reference for every extra view (`character_ipadapter.json`).
   Verified: identity holds (gunslinger draft indistinguishable from canon in gear/face/palette).
3. ~~ControlNet openpose~~ → **measured post-verification**: in-page canvas bbox of the figure
   per frame; feet-baseline and height-scale compared across front/side/back. Frames outside
   tolerance (baseline > ~8px, scale > ~3%) get rerolled (bump view prompt / weight), and
   final normalization (uniform scale + translate to common baseline) is deterministic
   post-processing, not generation.
4. LoRA fallback unchanged (heroes only) if IP-Adapter + reroll can't hold identity.

Gotchas for future runs (cost half a day):
- Setting a combo/image widget via JS: must also push the value into `widget.options.values`
  and call `widget.callback(value)`, or the frontend flags "Missing Inputs".
- `LoadImageOutput` ignores `characters/canon/<hash>.png [output]` refs on cloud and falls
  back to the *latest* output (we got a bull behemoth as the gunslinger ref). Use
  `/api/upload/image` to copy the canon into the input space and a plain `LoadImage` instead.
- `/api/history` display_names always say `_00001_`; resolve real per-run files via
  `/api/jobs/{id}` (needs `app.api.fetchApi`, not raw fetch).

## Sawbones back-frame follow-up (2026-08-25, session 2)
Recipe that finally produced a true back view without ControlNet — **3-stage pose transplant**:
1. img2img: gunslinger's good back frame as latent init, IPA "style transfer", denoise 0.5 → back pose kept, but gunslinger's hat/coat survive (denoise ≥0.62 flips the pose back to frontal — the IPA frontal reference is a strong attractor).
2. img2img refine at 0.6 WITHOUT IPAdapter (init already carries the palette) → clean back view, hat still baked into pixels.
3. Masked head inpaint (SolidMask 340px column + FeatherMask 40 + SetLatentNoiseMask, denoise 0.85, "back of a bald head, no headwear" + anti-hat negatives) → bald back view.
Delivered as art/characters/sawbones-turnaround_v3_back.png + sheet_v3. **Known mismatch: build reads leaner than the canon tank** — pose transplant inherits the donor's silhouette. A faithful back view for bulky silhouettes still needs the LoRA rung (or ControlNet, if Comfy Cloud ever fixes it). 12 draft attempts across pure-prompt seeds/weights all stayed frontal; logged so nobody repeats them.
