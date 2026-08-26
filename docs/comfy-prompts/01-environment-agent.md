# Agent 1 — Environment: tiles & textures pipeline (Comfy Cloud)

## Mission
Build and commit a ComfyUI (cloud.comfy.org) pipeline that produces **seamlessly tileable albedo (and derived normal/AO) textures for 3D mesh UVs** in The Red Hollow. The game is a **tilted isometric 3D** Unity co-op wave-defense set in a terraformed underground Mars colony (**Lykos**; DEC-026). Mix **~70% Martian / ~30% western**: tiles are **rusted metal, packed martian dust, carved sandstone** — not saloon porch or wagon-rut dirt. Wood/brass is a wear accent on metal plate, not the material of the town. Read `docs/comfy-prompts/00-shared-style.md` first.

These tiles wrap **3D meshes** (cavern floor slabs, rock walls, colony block faces). They are **not** a 2D tilemap and they are **not** a single textured quad that *is* the world. Tiles carry only the palette (burnt-sienna monochrome, warm near-black shadows, dusty apricot highlights) and the matte-dusty material read — the scene language (haze, silhouettes, settlement vistas, camera tilt) belongs to Unity lighting and meshes, never to a tile.

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
Prompts = `{item}` + style tail. Needed set (all for **3D mesh UVs**):

**Keep / generate**
1. red-rock cavern ground, packed martian dust *(floor slabs)*
4. rusted metal colony floor plate, hex-riveted *(colony decking / interior floors)*
5. red sandstone cavern wall, rough-hewn *(perimeter cliffs, dome)*
6. rusted metal colony habitat hull (utilitarian Lykos block, **wood/brass wear only as accent** — not a western storefront)
7. cracked dry martian soil with frost veins
8. gravel + scrap border ground (entry tunnel mouths)

**Superseded — do not generate more western-town ground** (DEC-026). Already-committed files stay in the repo; do not delete them; do not ship them as the match floor.
2. ~~dusty main-street dirt with wagon ruts~~ — superseded; use (1) cavern ground instead
3. ~~weathered wooden planking (saloon porch)~~ — superseded; use (4) colony metal plate / (6) colony block instead

If a new ground/decking tile is needed, generate **cavern floor**, **colony decking**, or **rusted metal plate** — not porch planks or wagon-rut dirt.

Deliver: seamless 1024² PNGs + workflow JSON + asset log rows for the keep set. Draft at low steps, pick winners, rerun keepers at 28 steps with the same seed.

**Semi-realistic depth pass (DEC-025):** for each approved albedo, derive **normal + ambient-occlusion maps** (ComfyUI normal-from-image nodes, or Materialize/equivalent) and deliver them alongside — URP Lit materials use albedo+normal+AO so Unity lighting carries the Lantern Deep look. Albedo stays painterly-matte and flat; realism comes from the lighting response, not photoreal source pixels.

## Control rung
Fixed seed + tiling nodes should suffice (rung 2). Climb to img2img (rung 3) only if palette drifts between textures — anchor on your best-approved texture as reference. Do NOT use LoRA.
