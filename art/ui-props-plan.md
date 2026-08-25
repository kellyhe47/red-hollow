# UI & Props pipeline — working board (Agent 4)

State file for the run. Update statuses as work lands. Resume procedure at bottom.

## Contracts (locked)
- Style: DEC-025 Lantern Deep, tail + negative from `docs/comfy-prompts/00-shared-style.md`.
- Sampler: seed 20260824 fixed, cfg 7, dpmpp_2m/karras, steps 12–15 draft / 28 keeper.
- Workflow: Comfy Cloud, **"UI & Props Pipeline"** (mine only). SaveImage prefixes `ui/...`.
- Framing prompt (② node, adapted from icons contract): weighted subject + `flat front-on view, game UI element` core; per-branch background token `(pure white background:1.4)` / `(pure black background:1.4)`.
- Alpha recovery: two KSampler branches, same seed, white/black bg → ImageBlend(difference) → invert → mask → JoinImageWithAlpha → RGBA SaveImage. Un-premultiply + exact-size resize done locally at delivery (scripted).
- Export API JSON → `art/workflows/ui_props.json` (single file; the two branches ARE the alpha workflow).

## Generation sizes (SDXL-safe) → delivered exact sizes
| item | gen size | deliver |
|---|---|---|
| 1 HUD top-bar frame | 1536×128 | 1920×120 |
| 2 shop-bar panel | 1344×192 | 1200×160 |
| 3 ability slot frame (+locked) | 1024² | 96×96 |
| 4 HP bar frame+fill | 1280×128 | 320×32 |
| 5 XP bar frame+fill | 1280×64 | 480×24 |
| 6 wooden button ×3 states | 1024×320 | 320×96 |
| 7 level-up card | 768×1024 | 360×480 |
| 8 dialog panel 9-slice | 1024×768 | 800×600 |
| 9 toast banner | 1280×192 | 640×96 |
| 10 wave banner scroll | 1024×256 | 800×200 |
| 11 victory laurel frame | 1280×512 | 1000×400 |
| 12 defeat banner frame | 1280×512 | 1000×400 |
| 13 cursor + crosshair (2 assets) | 1024² | 64×64 |
| 14 team spawn banner | 1024² | 256×256 |
| 15–24 props (RGBA) | 1024² | 1024² (or trimmed) |
| 25–27 facades (opaque, no alpha branch) | 1024² | 1024² |

## Status
- [ ] Workflow built in Comfy Cloud + first draft image
- [ ] Alpha branch verified (magenta/blue composite check on a draft)
- [ ] ui_props.json exported + committed
- [ ] Items 1–14 UI chrome (draft→keeper→download→verify→commit `art/ui/`)
- [ ] Items 15–24 props (`art/ui/props/` … actually `art/props/`? use `art/ui/` for chrome, `art/props/` for world props)
- [ ] Items 25–27 facades (opaque branch, `art/props/`)
- [ ] asset-log.csv rows per keeper

## Local delivery script
`art/tools/ui_deliver.py` (to write): input W/B renders or Comfy RGBA → un-premultiply → exact resize (lanczos) → size assert → magenta/blue composite halo check → write `art/ui/<slug>_v<N>_<WxH>.png`.

## Resume procedure
1. Read this file + `art/asset-log.csv` + `ls art/ui art/props`.
2. Chrome tab "UI & Props Pipeline" on cloud.comfy.org — only that tab.
3. Set ① ITEM node text per item, queue via `app.queuePrompt(0,1)` from console (Run button flaky).
4. Cmd+S after every graph change; verify `*` gone. Structural change → re-export API JSON → commit.
