# Shared style contract — all four Comfy Cloud pipelines

**Art direction: "Lantern Deep" palette/lighting (DEC-025) + 3D Lykos presentation (DEC-026, owner 2026-08-26).** DEC-025 still owns palette, sourced light, volumetric haze, and matte painterly semi-realism (owner-approved 2026-08-24; supersedes the original "stylized low-poly orange-and-teal flat shading" direction). DEC-026 overrides *what we ship in-engine*: fully 3D cavern colony (~70% Lykos habitat forms / ~30% western wood/brass/lantern wear on those forms; not a cowboy main street, not a flat 2D tilemap), tilted perspective follow-cam, 2D painted characters as standing cards. Not western-only-on-characters.

## Design language (the words; use these when describing, prompting, or reviewing any asset)

**One-line:** A vast enclosed cavern city rendered in a single warm monochrome, lit entirely from within — thousands of small artificial lights against immense dark rock, heavy atmospheric haze giving depth by silhouette.

**Palette**
- Near-monochromatic **burnt sienna / rust-amber**; shadows fall to warm near-black umber, highlights to pale dusty apricot — never white.
- No cool tones at all in the environment; contrast comes from *value* (light vs dark), not hue.
- Accent: tiny points of warm white-gold (windows, lanterns, arc-light towers) — the only saturation spikes.
- Gameplay-readability exception: hostile/status accents (e.g. Spitter acid-green, breach warnings) may break monochrome *sparingly* — they read as alarms precisely because nothing else does.

**Light & atmosphere**
- **Zero natural light**: no sun, no sky, no horizon. All light is artificial and sourced — window glow, floodlight masts, an illuminated lift shaft as a vertical landmark beacon.
- **Volumetric haze everywhere**: dust-fog scatters light into soft gradients; distance = brighter and hazier, foreground = darker and sharper (reversed aerial perspective — glow behind silhouette).
- Light pools and falls off fast; darkness is the default state and feels heavy overhead.

**Forms & materials**
- Immense scale contrast: cathedral-height rock walls and dome dwarf dense low blocky architecture; human figures as tiny silhouettes for scale.
- Architecture (~70%): stacked, flat-roofed, cubic **Lykos utilitarian colony blocks** — habitat masses, metal decking, carved rock, lantern masts, industrial gantries/cables. Western (~30%) is **wood/brass/lantern wear on those Mars forms**, not cowboy-main-street architecture. First/second-gen saloon-porch / hitching-post / chapel-steeple / ranch-porch LOOK is superseded (DEC-026).
- Rock: rough-hewn, massive, softly modeled by bounce light, not textured detail.
- Surfaces read matte and dusty; **no specular sparkle, no clean metal**.

**Rendering character**
- Semi-realistic painterly-matte (concept-art matte painting, not PBR-photoreal): soft edges in haze, crisp silhouette edges in foreground, detail suggested rather than drawn.
- Cinematic framing sensibility, deep Z-depth layering (foreground ridge → mid city → glowing far wall).

**In-engine presentation (DEC-026 — what we ship)**
- Environment = **fully 3D** Martian terraformed underground colony inspired by Lykos (Red Rising) / `seed-env.webp`. Mix **~70% colony / ~30% western frontier accents**. Stacked blocky settlement, real building height, volumetric haze, amber lanterns.
- Buildings are **Mars-type 3D kitbash modules** (Quaternius Modular Sci-Fi MegaKit Standard, CC0) retextured with Lykos URP Lit maps (hab-block-wall/cladding, hab-block-roof, colony-decking, cavern-ground, metal-floor-plate, colony-wall). Stacked habitat walls, 3D deck plates, roofs, doors, columns. **2D facade cards are retired.** Western is wood/brass/lantern wear on those forms — **not** western-only-on-characters, **not** a cowboy main street.
- **Zero natural light** (DEC-025, still in force): no sun, no sky, no directional golden-hour. Lanterns, not sun.
- Camera: **tilted perspective follow-cam (~58–62° down, FOV ~38°, street-scale)** so side walls, roof slabs and thick deck plates read. Not orthographic, not `Quaternion.LookRotation(Vector3.down)` bird's-eye, not a whole-map diorama. Reference language: r/DestroyMyGame 1hijaq4 trailer camera, but lanterns not sun.
- Heroes and monsters stay **2D painted western sheets** as standing cards/billboards in 3D space (2.5D). **v1 explicitly defers 3D sculpted/rigged characters** (hard lift).
- Comfy environment tiles = albedo/normal/AO for **3D mesh UVs**, not a flat 2D tilemap.
- Hotspot **names** Saloon, Chapel, Homestead remain sim/fixture IDs (R-10); their 3D meshes are Lykos colony blocks (kit modules + Lykos textures) with western wear. Camera is a **tilted perspective follow-cam**; characters stay 2.5D sheets planted on the 3D deck.

## Prompt contract

Every pipeline MUST use this exact style tail, stored once inside the workflow JSON (never retyped per prompt):

**STYLE TAIL (append to every positive prompt):**
> `cinematic subterranean Mars colony, colossal cavern interior, monochromatic burnt-sienna palette, dense blocky settlement lit by scattered warm artificial lights, volumetric dust haze, glowing distance behind dark silhouetted foreground, no sun, no sky, no horizon, matte painterly semi-realism, muted matte surfaces, no text, no watermark`

Pipeline-specific modifiers still apply on top and **override scene terms where they conflict** — e.g. the environment-tiles flatness modifier strips the scene/haze/silhouette language because tileable albedo must stay flat; icons/UI strip the settlement-vista language. The palette, matte-dusty material read, and no-sun rules are unconditional everywhere.

**NEGATIVE PROMPT (all pipelines):**
> `photo, photorealistic, blurry, text, watermark, signature, extra limbs, deformed, low quality, jpeg artifacts, sun, blue sky, daylight, teal, cool blue tones, oversaturated color`

**Locked sampler settings (per the deck):** seed = fixed (set `control_after_generate` to *fixed*, never randomize — do this before touching the prompt), steps = 25–30 (draft at 12–15, rerun keepers at 28), cfg = 7, sampler = `dpmpp_2m` + `karras` scheduler, denoise = 1.0 for txt2img.

## Where the style lives in-engine (Unity, not textures)

Most of Lantern Deep is a **scene-lighting** achievement, deliberately kept out of the albedo textures:
- Dark warm ambient (near-black umber), fog/volumetrics for the dust haze.
- All light sourced: amber point lights at lanterns/string lights/windows; one bright landmark (lift shaft / arc towers) as the "glowing distance".
- Cavern dome mesh textured with the sandstone/carved-rock wall tile; no skybox — the dome IS the sky.
- Semi-realistic depth comes from URP lighting + derived normal/AO maps over painterly albedo on **3D meshes** (see per-pipeline deliverables) — NOT from photoreal source textures, and **not from a flat 2D tilemap**.
- Camera is a tilted perspective follow-cam (~58–62° down, FOV ~38°). Heroes/monsters are 2D standing-card billboards in that 3D volume (DEC-026).

**Process rules (all agents):**
1. Start from a Comfy Cloud text-to-image template; get one image out before building anything.
2. One workflow file per asset class; the subject slot is an `{item}` token — the workflow owns the style, an items list owns the subjects. Fork per asset with the seed pinned.
3. Export **Save (API Format)** — the plain Save is only an editor file. Commit workflow JSON to `art/workflows/` in the game repo; roll back when a change makes things worse.
4. Log model + seed + steps + cfg + both prompts per delivered asset in `art/asset-log.csv`.
5. Draft at low steps and small sizes; spend credits only on keepers.
5b. **"Deliver" means committed to the repo.** Comfy Cloud storage is scratch space, not delivery. Every keeper gets downloaded (asset "..." menu → Download) and committed under `art/<class>/` (textures / characters / icons / ui) named `<subject-slug>_v<N>_<size|variant>.png` — e.g. `art/textures/street-dirt_v1_512.png`. Bump `_v<N+1>` on regeneration; never overwrite a committed version; never re-run a pipeline just to rename. An asset that exists only in Comfy Cloud is not done.
6. **Style-change rule:** any asset generated under the pre-DEC-025 tail must be regenerated before ship; never mix the two styles in one delivered set. DEC-026 is a *presentation* override (3D Lykos ~70/30 + 2.5D + tilted camera), not a palette regen. First/second-gen env art that leaned too western (saloon porch, hitching posts, chapel steeple, ranch porch) is superseded for the map look even if those files remain committed.
7. **Save hygiene (owner directive, 2026-08-25):** Comfy Cloud does not autosave, and console widget edits, queueing, and run progress all dirty the tab. **Never end a turn, report back, or go idle with a `*` (unsaved-changes dot) in your workflow tab title** — Cmd+S and verify the dot cleared (if it persists, click empty canvas to focus, Cmd+S again). Before that final save, restore the graph's contract nodes (framing/negative/prefixes) to match your committed `art/workflows/<pipeline>.json`, so the next run doesn't silently pick up a stale per-item experiment. The owner should never have to ping an agent to save its Comfy work. (Also in repo `CLAUDE.md` §1.)

Game: **The Red Hollow** — 1–4 player co-op wave defense, tilted perspective 3D Unity (DEC-026). Full spec in `docs/PRD.md`; wireframes in `docs/ui-wireframes.html`.
