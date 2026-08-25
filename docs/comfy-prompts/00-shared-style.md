# Shared style contract — all four Comfy Cloud pipelines

**Art direction: "Lantern Deep" — cinematic subterranean colonial Mars** (owner-approved 2026-08-24, DEC-025; supersedes the original "stylized low-poly orange-and-teal flat shading" direction).

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
- Architecture: stacked, flat-roofed, cubic vernacular masses — utilitarian, accreted, no ornament; industrial gantries, cables, scaffold masts threading through.
- Rock: rough-hewn, massive, softly modeled by bounce light, not textured detail.
- Surfaces read matte and dusty; **no specular sparkle, no clean metal**.

**Rendering character**
- Semi-realistic painterly-matte (concept-art matte painting, not PBR-photoreal): soft edges in haze, crisp silhouette edges in foreground, detail suggested rather than drawn.
- Cinematic framing sensibility, deep Z-depth layering (foreground ridge → mid city → glowing far wall).

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
- Cavern dome mesh textured with the sandstone wall tile; no skybox — the dome IS the sky.
- Semi-realistic depth comes from URP lighting + derived normal/AO maps over painterly albedo (see per-pipeline deliverables) — NOT from photoreal source textures.

**Process rules (all agents):**
1. Start from a Comfy Cloud text-to-image template; get one image out before building anything.
2. One workflow file per asset class; the subject slot is an `{item}` token — the workflow owns the style, an items list owns the subjects. Fork per asset with the seed pinned.
3. Export **Save (API Format)** — the plain Save is only an editor file. Commit workflow JSON to `art/workflows/` in the game repo; roll back when a change makes things worse.
4. Log model + seed + steps + cfg + both prompts per delivered asset in `art/asset-log.csv`.
5. Draft at low steps and small sizes; spend credits only on keepers.
5b. **"Deliver" means committed to the repo.** Comfy Cloud storage is scratch space, not delivery. Every keeper gets downloaded (asset "..." menu → Download) and committed under `art/<class>/` (textures / characters / icons / ui) named `<subject-slug>_v<N>_<size|variant>.png` — e.g. `art/textures/street-dirt_v1_512.png`. Bump `_v<N+1>` on regeneration; never overwrite a committed version; never re-run a pipeline just to rename. An asset that exists only in Comfy Cloud is not done.
6. **Style-change rule:** any asset generated under the pre-DEC-025 tail must be regenerated before ship; never mix the two styles in one delivered set.

Game: **The Red Hollow** — 1–4 player co-op wave defense, top-down Unity. Full spec in `docs/PRD.md`; wireframes in `docs/ui-wireframes.html`.
