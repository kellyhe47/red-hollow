# Agent 2 — Characters pipeline plan/board

Workflows (Comfy Cloud): "Characters pipeline" (canon+portrait+dead, d2acdf8a…) and "Characters IPAdapter" (turnarounds, a83d73d6…).
Model: sd_xl_base_1.0.safetensors · cfg 7 · dpmpp_2m/karras · keepers 28 steps, drafts 14.
Seeds: canon/portrait 20260824 (pinned). Turnarounds use documented per-view offsets (front 20260824, ¾ 20260825, back 20260826; sawbones rerolls 20260827/28) — offsets are pinned and logged, still reproducible.
Control ladder AS SHIPPED: fixed seed canon → IP-Adapter "PLUS (high strength)"; front frames weight 0.85/linear, back+¾ frames weight 1.0/"style transfer" (sawbones: "strong style transfer") → measured verification (canvas bbox baseline/scale). **ControlNet is silently broken on Comfy Cloud** (branch pruned server-side, no error) — see docs/comfy-prompts/02-characters-agent.md override. True 90° side profile unattainable without CN; third turnaround frame is a ¾ view (deviation documented; LoRA rung = owner decision).
NOTE: Comfy Cloud does NOT autosave — Cmd+S and verify the * clears (CLAUDE.md rule 1).

## Board
- [x] Build canon graph (item + fullbody & portrait branches + parked dead-variant branch) in "Characters pipeline"
- [x] Draft-pass, fix portrait framing (dedicated portrait negative)
- [x] Keeper canon runs 28 steps → characters/canon/<name> (8/8)
- [x] Portraits 512 → characters/portrait/<name> (8/8); hero dead variants via desaturate+darken branch (3/3)
- [x] Build "Characters IPAdapter" graph (IPAdapter; ControlNet removed — broken on cloud; openpose preprocessor parked as diagnostic)
- [x] Turnaround frames front/back/¾ per character (front batch + v2 back/¾ batch; sawbones v3 rerolls for identity drift)
- [x] Verify: identity spot-checks + canvas bbox measurement (feet baseline spread 0px, height spread ≤0.8% across frames)
- [x] Export Save (API Format) → art/workflows/character.json + character_ipadapter.json
- [x] Download all keepers → art/characters/ (43 PNGs + 8 normalized turnaround sheets, 51 files)
- [x] Append rows to art/asset-log.csv (51 rows)
- [x] Normalized composite sheets: feet baseline y=980 exact, uniform figure height per sheet (raw per-frame spreads before normalization: feet 0–69px, height 1.9–10.8% — normalization is the deterministic fix, logged)
- [ ] Commit — BLOCKED: repo has no .git (raised to owner; env agent also delivered uncommitted)
- Known deviations: (1) third turnaround frame is ¾ view not 90° side (SDXL+IPA without ControlNet cannot hold profiles; LoRA rung = owner call); (2) sawbones back frame is best-effort near-frontal (silhouette resisted view prompt across 5 seeds); (3) sawbones is _v2 (v1 canon drifted to hooded-assassin design, regenerated).

## Cast items (positive subject strings — the exact strings used)
gunslinger|lean human gunslinger hero, long charcoal duster coat with brass buckles, twin revolvers in low-slung holsters, wide low-brimmed hat, weathered western frontier outfit
rancher|stocky human rancher hero, double-barrel shotgun held ready, coiled rope lasso on hip, oxblood leather vest, rope-tan work clothes, heavy boots, western frontier outfit
sawbones|broad heavily-built human field surgeon hero, heavy bone-white leather apron armor with rust-stained metal fittings, huge bone-saw blade sword, western frontier outfit (v3 turnarounds: "thick leather butcher apron armor plated over the torso, giant serrated bone-saw blade carried like a sword" — anti-drift rewording)
shambler|shambling zombie mutated colonist, tattered settler clothes hanging in strips, sunken virus-blighted flesh, slack jaw, lurching stance
ravager|mutated cattle-dog monster, fast lean quadruped, mangy torn hide, exposed sinew, bared fangs, low predatory crouch
spitter|bloated mutated colonist monster, swollen distended belly, glowing acid-green throat sacs on neck, ragged settler clothes
burrower|mutated hog monster, huge digging claws on forelimbs, rock-crusted hide with embedded stone, tusked snout, low heavy body
bull_behemoth|massive mutated bull monster, exposed ribcage bones, broken jagged horns, towering muscular bulk, torn hide

Resume: cloud.comfy.org → Workflows; canon/portrait file hashes cached in browser localStorage (rh_canon, rh_portrait, rh_files).
