# Agent 2 — Characters pipeline plan/board

Workflow (Comfy Cloud): "Characters pipeline" (tab d2acdf8a-d2b8-4ce5-93a5-0edb78fcde2a)
Model: sd_xl_base_1.0.safetensors · seed 20260824 fixed · steps 28 (draft 14) · cfg 7 · dpmpp_2m/karras
Control: canon = fixed seed txt2img; poses = IPAdapter (PLUS, canon ref) + ControlNet controlnet-openpose-sdxl-1.0 (front skeleton reused for side/back to lock scale+baseline). LoRA only if heroes drift.

## Board
- [ ] Build canon graph (item + fullbody & portrait branches) in "Characters pipeline"
- [ ] Draft-pass 8 characters at 14 steps; approve silhouettes
- [ ] Keeper canon runs at 28 steps → characters/canon/<name>
- [ ] Portraits 512 (downscale from 1024) → characters/portrait/<name>; hero dead variants (greyed prompt mod)
- [ ] Build character_ipadapter graph (IPAdapter+openpose CN)
- [ ] Turnarounds front/side/back per character → characters/turnaround/<name>_<view>
- [ ] Verify: download frames, measure feet baseline (<few px) + scale (<3%) via Python bbox
- [ ] Export Save (API Format) → art/workflows/character.json + character_ipadapter.json; commit
- [ ] Log to art/asset-log.csv (model,seed,steps,cfg,pos,neg per asset)

## Cast items (positive subject strings)
gunslinger|lean human gunslinger hero, long charcoal duster coat with brass buckles, twin revolvers in low-slung holsters, wide low-brimmed hat, weathered western frontier outfit
rancher|stocky human rancher hero, double-barrel shotgun held ready, coiled rope lasso on hip, oxblood leather vest, rope-tan work clothes, heavy boots, western frontier outfit
sawbones|broad heavily-built human field surgeon hero, heavy bone-white leather apron armor with rust-stained metal fittings, huge bone-saw blade sword, western frontier outfit
shambler|shambling zombie mutated colonist, tattered settler clothes hanging in strips, sunken virus-blighted flesh, slack jaw, lurching stance
ravager|mutated cattle-dog monster, fast lean quadruped, mangy torn hide, exposed sinew, bared fangs, low predatory crouch
spitter|bloated mutated colonist monster, swollen distended belly, glowing acid-green throat sacs on neck, ragged settler clothes
burrower|mutated hog monster, huge digging claws on forelimbs, rock-crusted hide with embedded stone, tusked snout, low heavy body
bull_behemoth|massive mutated bull monster, exposed ribcage bones, broken jagged horns, towering muscular bulk, torn hide

Resume: reopen cloud.comfy.org → Workflows → Characters pipeline; graph state autosaves. Outputs in Comfy Cloud assets panel.
