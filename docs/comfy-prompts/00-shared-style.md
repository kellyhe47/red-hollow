# Shared style contract — all four Comfy Cloud pipelines

**Art direction: "Lantern Deep"** (owner-approved 2026-08-24, DEC-025) **in a 3D Lykos underground colony** (owner override 2026-08-26, DEC-026; supersedes “interior western town” as the LOOK of the map). Palette, lighting, and matte-painterly rendering are DEC-025; setting, camera, and what is western vs not are DEC-026.

## Design language (the words; use these when describing, prompting, or reviewing any asset)

**One-line:** A vast enclosed Martian cavern city of stacked blocky colony architecture, rendered in a single warm monochrome, lit entirely from within — thousands of small artificial lights against immense dark rock, heavy atmospheric haze giving depth by silhouette. Western silhouettes belong on the **heroes and monsters**, not on the streets.

**Palette**
- Near-monochromatic **burnt sienna / rust-amber**; shadows fall to warm near-black umber, highlights to pale dusty apricot — never white.
- No cool tones at all in the environment; contrast comes from *value* (light vs dark), not hue.
- Accent: tiny points of warm white-gold (windows, lanterns, arc-light towers) — the only saturation spikes.
- Gameplay-readability exception: hostile/status accents (e.g. Spitter acid-green, breach warnings) may break monochrome *sparingly* — they read as alarms precisely because nothing else does.

**Light & atmosphere**
- **Zero natural light**: no sun, no sky, no horizon, no directional golden-hour. All light is artificial and sourced — window glow, floodlight masts, amber lanterns, an illuminated lift shaft as a vertical landmark beacon.
- **Volumetric haze everywhere**: dust-fog scatters light into soft gradients; distance = brighter and hazier, foreground = darker and sharper (reversed aerial perspective — glow behind silhouette).
- Light pools and falls off fast; darkness is the default state and feels heavy overhead.

**Forms & materials**
- Immense scale contrast: cathedral-height rock walls and dome dwarf dense low blocky architecture; human figures as tiny silhouettes for scale.
- Architecture: **Lykos stacked cubes** — flat-roofed, cubic vernacular masses, utilitarian, accreted, no ornament; industrial gantries, cables, scaffold masts threading through. **Not** a western town (no saloon porch, wagon-rut dirt, hitching posts, tumbleweed, hay bales, chapel steeple, ranch porch as the map look).
- Rock: rough-hewn, massive, softly modeled by bounce light, not textured detail.
- Surfaces read matte and dusty; **no specular sparkle, no clean metal**.

**Rendering character**
- Semi-realistic painterly-matte (concept-art matte painting, not PBR-photoreal): soft edges in haze, crisp silhouette edges in foreground, detail suggested rather than drawn.
- Cinematic framing sensibility, deep Z-depth layering (foreground ridge → mid city → glowing far wall).

## In-engine presentation (Unity)

This is a **tilted 3D top-down / isometric** scene (~60–70° down), not straight bird's-eye and not a 2D tilemap.

- **Environment:** simple 3D meshes (rock slabs, cavern walls, stacked colony blocks) with URP Lit materials, amber **point** lanterns, fog/haze, rock-dome as sky. Comfy environment tiles are **albedo / normal / AO for those mesh UVs** — they wrap 3D geometry; they are not laid as a flat tiled quad that *is* the world.
- **Camera:** steep top-down so building **side walls and roof edges** read. Bird's-eye flattens the colony into roofs and is the wrong target.
- **Characters (v1):** **camera-facing upright billboards** (world-up, yaw to the lens) with a **blob shadow** under the feet, in lantern tint and haze. One canon painted sheet per character. **Not** XZ-flat floor sprites (edge-on under the tilt). **Not** 8-direction walk cycles — do not over-invest; a later 3D hero **swaps the view mesh only**. Do **not** sculpt 3D character meshes for v1.
- **Western split:** gunslinger/rancher/sawbones (and monster) art may stay western. The cavern and colony buildings may not.

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
- All light sourced: amber point lights at lanterns/string lights/windows; one bright landmark (lift shaft / arc towers) as the "glowing distance". **No directional sun.**
- Cavern dome mesh textured with the sandstone wall tile; no skybox — the dome IS the sky.
- Semi-realistic depth comes from URP lighting + derived normal/AO maps over painterly albedo (see per-pipeline deliverables) — NOT from photoreal source textures, and NOT from a flat 2D tilemap.

**Process rules (all agents):**
1. Start from a Comfy Cloud text-to-image template; get one image out before building anything.
2. One workflow file per asset class; the subject slot is an `{item}` token — the workflow owns the style, an items list owns the subjects. Fork per asset with the seed pinned.
3. Export **Save (API Format)** — the plain Save is only an editor file. Commit workflow JSON to `art/workflows/` in the game repo; roll back when a change makes things worse.
4. Log model + seed + steps + cfg + both prompts per delivered asset in `art/asset-log.csv`.
5. Draft at low steps and small sizes; spend credits only on keepers.
5b. **"Deliver" means committed to the repo.** Comfy Cloud storage is scratch space, not delivery. Every keeper gets downloaded (asset "..." menu → Download) and committed under `art/<class>/` (textures / characters / icons / ui) named `<subject-slug>_v<N>_<size|variant>.png` — e.g. `art/textures/street-dirt_v1_512.png`. Bump `_v<N+1>` on regeneration; never overwrite a committed version; never re-run a pipeline just to rename. An asset that exists only in Comfy Cloud is not done.
6. **Style-change rule:** any asset generated under the pre-DEC-025 tail must be regenerated before ship; never mix the two styles in one delivered set.
7. **Save hygiene (owner directive, 2026-08-25):** Comfy Cloud does not autosave, and console widget edits, queueing, and run progress all dirty the tab. **Never end a turn, report back, or go idle with a `*` (unsaved-changes dot) in your workflow tab title** — Cmd+S and verify the dot cleared (if it persists, click empty canvas to focus, Cmd+S again). Before that final save, restore the graph's contract nodes (framing/negative/prefixes) to match your committed `art/workflows/<pipeline>.json`, so the next run doesn't silently pick up a stale per-item experiment. The owner should never have to ping an agent to save its Comfy work. (Also in repo `CLAUDE.md` §1.)

Game: **The Red Hollow** — 1–4 player co-op wave defense, **tilted top-down / isometric 3D** Unity (not bird's-eye, not a 2D tilemap). Full spec in `docs/PRD.md` (v1.3, DEC-026); wireframes in `docs/ui-wireframes.html`.
