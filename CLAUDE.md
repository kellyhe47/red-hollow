# The Red Hollow — agent operating guide

Week-8 game project. Spec: `docs/PRD.md` (v1.3, DEC-026 owner art override). Fixtures: `eval/golden/` (acceptance contract). Art contracts: `docs/comfy-prompts/`.

## ⚠️ Multiple agents are working this repo concurrently

Several Claude (Desktop/Code) agents run in parallel, one per Comfy Cloud art pipeline (environment / characters / item icons / UI & props) plus possibly engine work. Assume you are NOT alone. That means:

### 1. Save your work as you go — every time
- Comfy Cloud does NOT autosave the workflow graph. After **any** graph or widget change: **Cmd+S**, then **verify the `*` (unsaved-changes dot) disappeared from the workflow tab title**. A Cmd+S that lands mid-focus elsewhere silently does nothing — we have already lost work this way once.
- A page refresh loads the last *saved* state and looks empty for a few seconds while loading. Don't panic-rebuild; wait for the graph to render before concluding anything is lost.
- After any *structural* workflow change (nodes/wiring/prompt contracts — not per-item token swaps), re-export **Save (API Format)** → commit to `art/workflows/<pipeline>.json`. That file is the rollback point.
- **Never end a turn (or pause to report/wait) with a dirty workflow.** Setting widgets from the console, queueing runs, and even execution progress all mark the tab dirty (`*`). Before every stopping point — end of a work burst, handing back to the owner, going idle while a queue drains — Cmd+S and confirm the `*` is gone; if it persists, click empty canvas to focus and Cmd+S again. The owner should never have to ping an agent to save its Comfy work.
- Bonus hygiene before that final save: leave the graph in its *canonical contract state* (framing/negative/prefix nodes matching the committed `art/workflows/<pipeline>.json`), not whatever the last per-item experiment left behind — the next run in a stale graph silently uses the wrong contract (e.g. a props framing left in place would mis-style the next UI item).

### 2. Only touch YOUR browser tab / workflow
- Each pipeline has its own Comfy Cloud workflow ("Environment Pipeline", characters, icons, UI/props). Work **only** in the Chrome tab with your pipeline open. Never edit, run, save, or rename another pipeline's workflow — another agent may have unsaved state there.
- Before acting, confirm the tab title matches your pipeline. If you find yourself in the wrong workflow, stop — do not Cmd+S there.
- Media Assets (Generated tab) is shared account-wide: you will see other pipelines' outputs. Never delete assets you didn't create.
- **Preview-overlay gotcha:** selecting a job/asset in the shared panel paints that job's images onto the open graph's nodes *by node ID* — if workflows were cloned from each other, another pipeline's characters can appear "inside" your graph. It's display-only; deselect (✕ on "N selected") to clear. Verify contamination by reading node values (item token, SaveImage prefixes), never by thumbnails.
- When creating your pipeline, **build or duplicate then immediately retitle** it (e.g. "Characters Pipeline") and change all SaveImage `filename_prefix` values to your class prefix (`characters/…`, `icons/…`, `ui/…`) BEFORE the first run — output prefixes are how everyone attributes assets in the shared feed.

### 3. Shared contracts — docs are the source of truth
- Art direction is **"Lantern Deep" (DEC-025)** plus **DEC-026** (3D Lykos cavern, tilted isometric camera, western on characters only) — read `docs/comfy-prompts/00-shared-style.md` before generating anything. The Comfy prompt nodes are deployed *copies* of that doc; if style must change, change the doc first (and tell the owner — style is an owner-level decision), then mirror to nodes.
- Environment pipeline uses the **texture-core tail** documented in `01-environment-agent.md`, not the full scene tail (scene terms break tileability — verified). Tiles are **albedo/normal/AO for 3D meshes**, not a 2D tilemap. Do not generate more western-town ground (wagon-rut dirt, saloon porch).
- Never mix pre- and post-DEC-025 assets in a delivered set; regenerate old-style assets. Do not mix western-town scenery into the Lykos environment (DEC-026).

### 4. Comfy pipeline discipline (all pipelines)
- Seed stays pinned (20260824, `control_after_generate: fixed`). Steps 28 for keepers, 12–15 for drafts. cfg 7, dpmpp_2m/karras.
- Per-asset changes go through the **① ITEM token node only**. Style/negative/sampler nodes are contract — don't edit ad hoc.
- Runs cost credits/quota. Draft cheap, spend on keepers. Log every delivered asset (model, seed, steps, cfg, both prompts) in `art/asset-log.csv`.
- **Asset naming & delivery (owner directive, 2026-08-25):** Comfy Cloud cannot rename generated assets, and its counters (`…_00001_`) are ambiguous. Therefore: (a) set your SaveImage `filename_prefix` to a **descriptive slug per asset** (e.g. `env_tile/1024/cavern-ground_v1`) *before* each keeper run when practical; (b) regardless, the canonical named copy lives in the **repo** — download each keeper (asset "..." menu → Download, unzip) and commit it under `art/<class>/` named `<subject-slug>_v<N>_<size|variant>.png` (e.g. `art/textures/street-dirt_v1_512.png`, `art/characters/gunslinger-canon_v1.png`). Bump `_v2` on regeneration instead of overwriting; never re-run a pipeline just to rename (pinned seed makes reruns identical — renaming is a file operation, not a generation).
- Verify outputs against your pipeline doc's mandatory checks (seam/flatness for tiles, identity for characters, set-consistency for icons, alpha/size for UI) before calling anything done.

### 5. Practical browser-automation notes (learned the hard way)
- The ComfyUI graph is scriptable from the page console: `(window.app||window.comfyAPI.app.app).graph`, `getNodeById(n)`, widget `.value` + `.callback(v)`, `app.graphToPrompt()` for API-format export. Far more reliable than clicking nodes at low zoom.
- After setting a widget via JS, call its `callback` and then save; verify by reading the value back.
- Uploading a reference image: Media Assets → Imported accepts direct file-input upload.
- The environment workflow has a parked (disconnected) Load Image → VAE Encode pair for optional img2img anchoring. Leave it disconnected unless escalating per the control-rung rules in your pipeline doc.

### 6. Coordination
- Don't rename shared things (workflows, file prefixes, doc files) without noting it in your handoff/summary — renames have already caused confusion once ("Environment Agent" → "Environment Pipeline").
- If you discover a contract problem (style term breaking your asset class, sampler setting fighting your deliverable), document the pipeline-specific override in YOUR `docs/comfy-prompts/0N-*.md` file — as `01` did with the texture-core tail — rather than editing the shared contract.
- Progress state: check `art/asset-log.csv` and Media Assets before regenerating — another agent (or an earlier you) may already have a keeper.
