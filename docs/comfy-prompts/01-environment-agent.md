# Agent 1 — Environment: tiles & textures pipeline (Comfy Cloud)

## Mission
Build and commit a ComfyUI (cloud.comfy.org) pipeline that produces **seamlessly tileable ground/wall textures** for The Red Hollow, a tilted-isometric 3D Unity co-op wave-defense game set in a fully 3D Martian terraformed underground colony inspired by Lykos (Red Rising) / `seed-env.webp`. Read `docs/comfy-prompts/00-shared-style.md` first and obey its style tail ("Lantern Deep" palette/lighting, DEC-025; presentation override DEC-026), sampler locks, and process rules.

**These tiles are albedo/normal/AO for 3D mesh UVs**, not a flat 2D tilemap. Tiles carry only the palette (burnt-sienna monochrome, warm near-black shadows, dusty apricot highlights) and the matte-dusty material read — the scene language (haze, silhouettes, settlement vistas) belongs to Unity lighting, never to a tile. Mix is **~70% Mars colony materials / ~30% western wood/brass/lantern wear on those forms** (DEC-026) — not a cowboy main street. Tile *subjects* are cavern floor, colony metal decking, rusted plate, carved-rock wall, hab cladding. Western wear may weather those surfaces; do not generate saloon-porch / wagon-rut / hitching-post / chapel-steeple / ranch-porch as the subject (first/second-gen leaned too western; superseded).

**TEXTURE-CORE TAIL (this pipeline uses this instead of the full scene tail — verified 2026-08-24: the full tail's scene terms overpower the flatness modifier and produce canyon vistas instead of tiles):**
> `monochromatic burnt-sienna palette, warm near-black umber shadows, pale dusty apricot highlights, warm artificial light color temperature, matte painterly semi-realism, muted matte dusty surfaces, no specular sparkle, no text, no watermark`

## Failure mode you own (from the art plan)
Tiles **must repeat with no seam**. Generate FLAT albedo — no baked directional lighting, no vignette, no hotspot glow; Unity lights the scene. A tiled wall that reads as a checkerboard even though the bricks line up means the *lighting* didn't tile — that is your primary defect class.

## Pipeline to build
1. Comfy Cloud txt2img template → lock seed → verify first image.
2. Add **seamless tiling**: use a tiling-capable node path (e.g. asymmetric tiling / circular-padding conv node available on Comfy Cloud, or generate 1024² then offset-and-inpaint the cross-seam with an img2img pass at denoise ~0.35).
3. `{item}` token in the positive prompt; style tail lives once in the workflow.
4. Output 1024×1024 PNG, downscale to 512² for engine import.
5. Export Save (API Format) → `art/workflows/env_tile.json`; commit.

## Verification (mandatory, per texture)
- **Seam check:** offset the image 50% in x and y and inspect the cross; script it (`seam_check` style: tile 2×2 and diff edge rows). Reject any visible seam or lighting gradient.
- Flatness check: no corner-to-corner luminance gradient > ~5%.

## Deliverables (items list)
Prompts = `{item}` + style tail. Needed set (DEC-026 intended subjects — Lykos cavern / colony materials for 3D mesh UVs):
1. red-rock cavern floor, packed martian dust
2. colony metal decking / grated walkway (hab catwalk)
3. stacked hab-block cladding, utilitarian colony plate (building sides/roofs)
4. rusted metal colony floor plate, hex-riveted
5. carved-rock cavern wall, rough-hewn
6. corrugated metal + rusted-plate hab-block wall (colony building)
7. cracked dry martian soil with frost veins
8. gravel + scrap border ground (entry tunnel mouths)

Deliver: seamless 1024² PNGs + workflow JSON + asset log rows. Draft at low steps, pick winners, rerun keepers at 28 steps with the same seed.

**Superseded subjects (DEC-026 — first/second-gen Comfy env art leaned too western; do not use on the map; do not delete committed files):**
- ~~dusty main-street dirt with wagon ruts~~ — committed as `art/textures/street-dirt_*`. Historical. Not the ship look.
- ~~weathered wooden planking (saloon porch)~~ — committed as `art/textures/saloon-planking_*`. Historical. Not the ship look.

Keepers already in-repo that match the intended set stay valid: `cavern-ground_*`, `metal-floor-plate_*`, `sandstone-wall_*`, `colony-wall_*`, `cracked-soil_*`, `gravel-border_*`.

**Pipeline-specific negative (on top of the shared negative):** `wagon ruts, saloon porch, wooden boardwalk, hitching post, tumbleweed, chapel steeple, ranch porch, western main street`

**Semi-realistic depth pass (DEC-025):** for each approved albedo, derive **normal + ambient-occlusion maps** (ComfyUI normal-from-image nodes, or Materialize/equivalent) and deliver them alongside — URP Lit materials use albedo+normal+AO so Unity lighting carries the Lantern Deep look on **3D meshes**. Albedo stays painterly-matte and flat; realism comes from the lighting response, not photoreal source pixels.

## Control rung
Fixed seed + tiling nodes should suffice (rung 2). Climb to img2img (rung 3) only if palette drifts between textures — anchor on your best-approved texture as reference. Do NOT use LoRA.
