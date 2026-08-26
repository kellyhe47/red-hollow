# The Red Hollow — PRD

*Co-op wave defense in a fully 3D Lykos-inspired Martian underground colony (~70% colony forms / ~30% western frontier wear). Not a cowboy main street.*
*Version 1.3 — 2026-08-26. Status: **Approved** (owner accepted all author proposals wholesale, DEC-024; art direction reset to "Lantern Deep" palette/lighting, DEC-025; presentation override to 3D Lykos (~70/30 colony + western wear) + world-facing painted character volumes + tilted camera, DEC-026).*

## 0. Sources of truth

Precedence order when artifacts conflict:

1. **Golden fixtures** — `eval/golden/*.json` (30 cases, 19 behaviors, manifest in `eval/golden-manifest.json`). These are the acceptance contract for all simulation rules. `python3 eval/verify_fixtures.py` and the structural validator must both pass; expectation changes are spec changes.
2. **This PRD** — the why, the numbered requirements, and everything fixtures can't express (feel, art, netcode).
3. **Wireframes** — `docs/ui-wireframes.html` (7 screens + states), `docs/architecture.excalidraw` (system diagram, checker-verified).

Line tags: `[source]` = from the owner's brief; `[decided]` = owner's explicit call (DEC registry, §12); `[decided]` = author's call, pending approval.

## 1. Product summary

`[source]` 1–4 players are gunslinger heroes defending an underground Mars colony from waves of zombie-like monsters — former colonists and livestock mutated by a genetic virus — pouring in through breached entry tunnels. Civilians shelter in hotspots; heroes place defenses in a planning phase, then fight in real time. Survive all waves to win; lose every civilian and the colony falls.

Combat feel is modeled on League of Legends (`[decided]` DEC-016): cooldown-gated ability kits, skillshots, level-ups and skill points — but with WASD movement and mouse aim (`[decided]` DEC-017), not click-to-move.

## 2. Requirements index

| ID | Area | ID | Area |
|---|---|---|---|
| R-01..R-07 | Match structure & phases | R-30..R-36 | Heroes & combat |
| R-10..R-15 | Colony, hotspots, civilians | R-40..R-44 | XP, leveling, accounts |
| R-16..R-19 | Monsters & AI | R-50..R-55 | Multiplayer & netcode |
| R-20..R-26 | Economy & placeables | R-60..R-64 | UX screens & feedback |
|  |  | R-70..R-73 | Non-goals (v1) |

## 3. Match structure & phases

- **R-01** `[source]` A match is one map with **10 waves** (`[decided]` DEC-004). Clearing wave 10 = victory (fixture G-011).
- **R-02** `[source]` A wave is cleared when its last living monster dies (G-010). Defeat occurs the instant total civilians across all hotspots reaches 0, at any point (G-008); defeat mid-wave ends the match immediately.
- **R-03** `[decided]` DEC-006 — Match FSM: `lobby → (planning → combat)×10 → victory`, with a `combat → defeat` edge available in every wave (R-02). Each wave begins with a **60-second planning phase**; combat starts early when all **connected** players ready up (G-017) — a 1-player lobby needs only that player's ready. There is no separate host force-start.
- **R-04** `[decided]` Wave-complete interstitial (~3s) shows bounty earned and civilians remaining, then auto-advances to planning.
- **R-05** `[decided]` DEC-018 — **Partial wave preview**: during planning, entry points that will activate next wave are highlighted; monster types and counts are hidden.
- **R-06** `[decided]` Session length goal 25–35 minutes (AUD-4) — playtest criterion, like R-64; not machine-testable.
- **R-07** `[decided]` Rematch: on Victory/Defeat, PLAY AGAIN / RETRY returns the **whole party to the same lobby** (join code and class picks retained) when the host clicks it; all match state (scrip, waves, placeables, civilians) resets fully; account profiles persist per R-43. Non-host players see the lobby when the host restarts; anyone may instead leave to Title.

## 4. Colony, hotspots, civilians

- **R-10** `[decided]` One map (v1): a cavern colony with **3 hotspots** — Saloon (8 civilians), Chapel (6), Homestead (6) — total **20 civilians**, **4 breach entry tunnels** at the cavern edges, and one marked **team spawn point** near the map center where heroes enter at wave 1 and respawn (R-33). **Names are sim/fixture IDs** (Saloon/Chapel/Homestead stay as strings for R-10, G-ids, and golden fixtures). **Meshes are Lykos utilitarian colony blocks** with western wood/brass/lantern wear (~70/30) — not a cowboy main street (DEC-026).
- **R-11** `[decided]` DEC-002 — Hotspot HP *is* its civilian count. A monster hit on a hotspot kills `ceil(damage/10)` civilians, clamped at 0 (G-006, G-007). Civilians are not simulated agents and cannot be healed, moved, or restored.
- **R-12** `[source]` Losing one hotspot does **not** end the match while civilians survive elsewhere (G-009). An emptied hotspot is visually marked lost and is no longer a valid monster target (G-002).
- **R-13** `[decided]` Emptied hotspots stay lost for the rest of the match; there is no recapture.
- **R-14** `[decided]` Entry tunnels are fixed map features; which subset activates varies per wave via the wave table (R-19).
- **R-15** `[decided]` DEC-025 + DEC-026 — Theme & art direction. **Palette & lighting (DEC-025, "Lantern Deep", still in force):** cinematic subterranean monochrome burnt-sienna/rust-amber with warm near-black shadows; **zero natural light** — no sun, sky, horizon, or directional golden-hour; all light artificial and sourced (amber lanterns, string lights, window glow, an illuminated lift-shaft landmark); volumetric dust haze; matte painterly semi-realism (not photoreal, not flat-shaded low-poly). Carried primarily by **Unity scene lighting** (dark warm ambient, amber point lights, fog, rock-dome mesh as the sky) over painterly-matte albedo + derived normal/AO maps. **Presentation (DEC-026, owner override 2026-08-26 — what we ship):** the environment is a **fully 3D Martian terraformed underground colony** inspired by Lykos (Red Rising) / `seed-env.webp` — stacked blocky settlement, real building height, volumetric haze, amber lanterns. Camera is a **tilted perspective follow-cam (~58–62° down, FOV ~38°, street-scale)** so side walls, roof slabs and deck thickness read; not orthographic, not `Quaternion.LookRotation(Vector3.down)` bird's-eye, not a whole-map diorama (reference language: r/DestroyMyGame 1hijaq4 trailer camera, lanterns not sun). Mix is **~70% Martian terraformed underground colony / ~30% western frontier accents** — not western-only-on-characters, not a cowboy main street. Buildings are Mars-type: **3D kitbash modules** (Quaternius Modular Sci-Fi MegaKit, Standard, CC0) retextured with Lykos URP Lit maps (`hab-block-wall` / cladding, `hab-block-roof`, `colony-decking`, `cavern-ground`, `metal-floor-plate`, `colony-wall`) — stacked habitat walls, 3D deck plates, roofs, doors, columns — not primitive cubes, not 2D facade cards (those are retired). Western is **wood/brass/lantern wear on those forms**, plus **painted hero/monster sheets** as world-facing Lit volumes (capsule/hat thickness, yaw from aim/walk — not camera-facing 2.5D cards; UI audio twang ok per R-64). First/second-gen Comfy environment art leaned too western (saloon porch, hitching posts, chapel steeple, ranch porch) — superseded for the environment look; do not delete committed files. Hotspot **names** Saloon, Chapel, Homestead remain for sim/fixtures/R-10; their **3D meshes** are Lykos utilitarian colony blocks with that western wear. Comfy environment tiles are albedo/normal/AO for 3D mesh UVs, not a flat 2D tilemap. **v1 still defers Mixamo/rigged character meshes** (hard lift). Full design language in `docs/comfy-prompts/00-shared-style.md`; facade art in `docs/comfy-prompts/04-ui-props-agent.md`.

## 5. Monsters & AI

- **R-16** `[source]` Monsters are data-configured (stats in ScriptableObjects); baseline AI: **auto-attack the nearest available target** — nearest of {living hero, hotspot with ≥1 civilian} by distance, ties to lowest entity id (G-001..G-003; DEC-003). A barricade blocking the path becomes the target until destroyed (G-004).
- **R-17** `[decided]` DEC-007 + AUD-5 — Roster (5 types):

| Type | HP | Dmg | Speed | Bounty=XP | Note |
|---|---|---|---|---|---|
| Shambler (ex-colonist) | 60 | 10 | 2.0 | 10 | baseline swarm |
| Ravager (mutant cattle-dog) | 40 | 8 | 5.0 | 15 | fast |
| Spitter (ex-colonist) | 50 | 12 | 2.0 | 20 | ranged acid, range 10 |
| Burrower (mutant hog) | 80 | 15 | 2.5 | 30 | ignores barricades & heroes; tunnels to nearest civilian hotspot (G-005) |
| Bull Behemoth (ex-livestock) | 400 | 40 | 1.5 | 50 | tank; one hit kills up to 4 civilians — ceil(40/10), clamped by remaining count (G-007) |

  All stats `[decided]`, tunable in config without code changes; bounty table is fixture-locked (verify_fixtures BOUNTIES).
- **R-18** `[decided]` Monsters attack once per second; movement uses NavMesh paths (Burrower path ignores barricade obstacles).
- **R-19** `[decided]` Wave table (per-wave composition, counts, active entry points) lives in config. Difficulty ramps: wave 1 ≈ 6 Shamblers; Behemoths appear from wave 5; wave 10 ≈ 30 mixed monsters from all 4 tunnels. Exact table is an implementation-time config, playtested; not fixture-locked.

## 6. Economy & placeables

- **R-20** `[decided]` DEC-005 — One **shared team scrip pool**. Starting stake 500 `[decided]`. Every kill adds its bounty to the pool (G-012); unspent scrip carries over between waves in full (G-016).
- **R-21** `[source]` Placement is **planning-phase only**; server rejects purchases in combat (`wrong_phase`, G-015) or beyond the pool (`insufficient_scrip`, G-014, no negative balance). Successful purchase decrements pool and spawns the placeable (G-013).
- **R-22** `[decided]` DEC-011 — During planning, placeables sell for `floor(cost/2)` refunded to the pool (G-022).
- **R-23** `[decided]` DEC-023 — Catalog; *mechanics* are fixture-locked (spike-trap trigger count & break G-027, turret nearest-in-range G-028, dynamite single-use AoE G-029), numeric stats config-tunable:

| Placeable | Cost | Effect |
|---|---|---|
| Barricade | 100 | 300 HP wall; blocks monster paths (not Burrowers) |
| Spike Trap | 75 | 30 dmg per monster crossing; 10 triggers then breaks |
| Dynamite Trap | 150 | 150 dmg AoE, single use |
| Turret | 250 | 20 DPS, range 8, targets nearest monster |
| Med Station | 200 | heals heroes 5 HP/s in radius 5 |

- **R-24** `[decided]` Placement zones: anywhere on colony ground except inside hotspot buildings, on entry tunnel mouths, or overlapping other placeables. Invalid ghost placement is visibly rejected.
- **R-25** `[decided]` Any player may spend from the shared pool; no votes or locks (co-op negotiation is verbal).
- **R-26** `[decided]` DEC-019 — **No friendly fire**: hero attacks never damage heroes or placeables (G-030; the placeable half is `[decided]`, extending the owner's decision).

## 7. Heroes & combat

- **R-30** `[decided]` DEC-016/DEC-017 — LoL-modeled combat; controls: **WASD move (W is movement only), hero faces mouse cursor, SPACE = basic attack toward cursor, Q and E = abilities**. Mouse buttons stay free for UI. LoL contributes kit structure, cooldowns, and skillshots — not click-to-move.
- **R-31** `[decided]` DEC-001/DEC-008/DEC-009 — Three classes, duplicates allowed in a lobby:

| Class | HP | Basic (SPACE) | Q | E | Passive |
|---|---|---|---|---|---|
| Gunslinger | 100 | long-range shot, 25 dmg | Fan the Hammer: 6-shot burst | Deadeye: piercing line skillshot | every 4th basic crits ×2 |
| Rancher | 120 | cone shotgun, 12×5 pellets | Lasso: 50% slow, 3.0s (G-018/G-019) | Stampede: dash + knockback | basics hit up to 2 targets |
| Sawbones | 200 | melee cleave, 40 dmg | Whirl: AoE spin | Bulwark: 60% DR for 2s | **flat 30% damage reduction** (G-020) |

  Kit numbers beyond fixture-locked values are `[decided]`, config-tunable. Q/E are locked until unlocked with skill points (R-42); at match start each hero applies the **saved ability allocations from the player's account profile** (R-43), so veteran accounts begin with previously unlocked abilities and fresh accounts begin basic-attack-only.
- **R-32** `[decided]` Ability cooldowns: Q 8s, E 20s (per class tuning allowed). Rank-ups (max 3) improve numbers ~+25%/rank.
- **R-33** `[decided]` DEC-010 — Hero at 0 HP dies instantly and **respawns at the team spawn at full HP after 10s** (G-021); dead heroes are untargetable and spectate a living ally. All heroes dead ≠ defeat — monsters keep attacking civilians (R-02 is the only loss rule).
- **R-34** `[decided]` Heroes have no mana; cooldowns are the only cast limit.
- **R-35** `[decided]` Out-of-combat regen: 2 HP/s after 5s untouched; Med Station stacks.
- **R-36** `[decided]` Hero basic attacks and abilities damage monsters only (R-26).

## 8. XP, leveling, accounts

- **R-40** `[decided]` DEC-012 — Kills grant **XP equal to the monster's bounty to the killing player only**. Turret kills credit the placer `[decided]`.
- **R-41** `[decided]` DEC-013 — **Lifetime XP never resets** — not per wave, not per match. Level L requires cumulative XP `100·L·(L-1)/2` (level 2 at 100, 3 at 300, 4 at 600 …); the visible bar shows progress within the current level and each level's requirement grows (G-023/G-024).
- **R-42** `[decided]` DEC-014 — Each level-up grants **1 skill point**. Player freely chooses: unlock Q, unlock E, or rank up an unlocked ability (max rank 3) (G-025); spend with no points is server-rejected (G-026). Points may be banked.
- **R-43** `[decided]` DEC-015 — **Account progression is persistent server-side**, keyed by account id: lifetime XP, level, skill points, ability allocations per class. Profile saves at each level-up and match end (G-023/G-024 pin save timing).
- **R-44** `[decided]` v1 account = callsign string, no password/auth (trust-based; fine for a week-8 project). Profile store = server-local SQLite or JSON keyed by callsign. **Consequence accepted:** veterans start matches with abilities already unlocked; PvE co-op tolerates power gaps.

## 9. Multiplayer & netcode

- **R-50** `[decided]` DEC-020/DEC-022 — Unity engine; **1–4 player co-op required**, solo = 1-player lobby. `[decided]` Transport stack: Netcode for GameObjects, host-authoritative, Unity Lobby (join codes) + Relay.
- **R-51** `[decided]` All fixture-covered logic (targeting, damage, economy, XP, phase FSM) lives in a **pure-C# `GameSim` assembly** executed only on the host; clients send commands, receive replicated state. This is the seam the golden fixture NUnit adapter drives.
- **R-52** `[decided]` Client-side: interpolation for remote entities; own-hero movement locally predicted, host-reconciled.
- **R-53** `[decided]` Mid-match disconnect: hero despawns, monsters retarget, match continues; toast shown. **Host disconnect ends the match** (no host migration in v1). No mid-match joins.
- **R-54** `[decided]` Determinism: sim runs on the host only, so cross-client lockstep determinism is NOT required; the tiebreak rule (G-003) exists for replayable, testable sim behavior.
- **R-55** `[decided]` ESC = non-pausing overlay menu; multiplayer never pauses.

## 10. UX screens & feedback

Authoritative sketch: `docs/ui-wireframes.html` (S1–S7 + cross-cutting states). Highlights:

- **R-60** `[decided]` Screens: Title/Join (S1) → Lobby with class pick + ready (S2) → Planning (S3) → Combat (S4) → Wave interstitial (S5) → Victory (S6) / Defeat (S7). All states listed in the wireframe file are requirements, including: bad join code error, greyed unaffordable shop items, dead-hero spectate overlay, civilians-lost toast + red flash, lost-hotspot marking.
- **R-61** `[decided]` Persistent HUD (combat): wave n/10, monsters remaining, per-hotspot civilian counts, shared scrip, own HP/cooldowns/XP/level, unspent-skill-point badge.
- **R-62** `[decided]` Level-up choice UI is a **non-blocking overlay** (hotkey L or badge click); the sim never pauses for it.
- **R-63** `[decided]` Planning UI: shop bar with ghost-preview placement, sell-on-click (50% tooltip), active entry points pulse red, ready 2/4 indicator + timer.
- **R-64** `[decided]` Feel targets: basic attacks land with hit-flash + knockback nudge; wave start/end stingers; western-twang UI audio (UI/feel; sits with DEC-026 western wear on colony forms). (Not fixture-testable; playtest criteria.)

## 11. Non-goals (v1)

- **R-70** No PvP, no host migration, no mid-match join, no spectator slots beyond dead-hero cam.
- **R-71** No meta-economy across matches (scrip resets every match; only XP/levels persist).
- **R-72** No civilian simulation (agents, rescue, relocation) — hotspot counters only.
- **R-73** No second map, no boss monster, no difficulty settings (candidates for post-v1; boss was explicitly deferred, DEC-021).

## 12. Decision registry

| ID | Decision (owner, 2026-08-24) |
|---|---|
| DEC-001 | 3 classes: Gunslinger (single-target DPS), Rancher (crowd control), Sawbones (melee tank w/ sword; owner redefined from healer) |
| DEC-002 | Hotspot HP = civilian count; `ceil(dmg/10)` kill rule |
| DEC-003 | Nearest-target AI, lowest-id tiebreak |
| DEC-004 | Fixed 10-wave campaign per map |
| DEC-005 | Shared scrip pool + kill bounties + full carryover |
| DEC-006 | 60s planning timer + all-ready early start |
| DEC-007 | 5-monster roster |
| DEC-008 | Lasso = 50% slow, 3s |
| DEC-009 | Sawbones = 30% flat damage reduction |
| DEC-010 | Instant 10s-timer respawn (owner chose over downed/revive) |
| DEC-011 | Sell placeables at 50% during planning |
| DEC-012 | Kills grant personal XP (= bounty) |
| DEC-013 | Lifetime XP never resets; escalating per-level requirement |
| DEC-014 | Level-up = 1 skill point, free-choice unlock/rank |
| DEC-015 | Persistent server-side accounts |
| DEC-016 | Combat modeled on League of Legends (kits, cooldowns, skillshots, leveling) |
| DEC-017 | WASD move (W reserved), mouse aim, SPACE basic, Q/E abilities |
| DEC-018 | Partial wave preview (entry points only) |
| DEC-019 | No friendly fire |
| DEC-020 | Unity engine (owner chose over web 3D) |
| DEC-021 | No boss in v1 (5-type roster chosen over 5+boss) |
| DEC-022 | 2–4 player co-op required from v1 (owner, platform question); solo supported as a 1-player lobby |
| DEC-023 | Placeable combat-effects catalog (author proposal accepted at spec review; fixture-locked G-027..G-029) |
| DEC-025 | "Lantern Deep" palette & lighting (owner, 2026-08-24, from reference image): cinematic subterranean monochrome burnt-sienna, all-artificial sourced light, volumetric haze, matte painterly semi-realism; supersedes the low-poly orange-and-teal flat-shaded direction. Still in force for palette/lighting. Presentation (3D Lykos colony ~70/30 + western wear, 2.5D standing-card characters, tilted camera) is DEC-026. Contract in `docs/comfy-prompts/00-shared-style.md` |
| DEC-026 | Presentation override (owner, 2026-08-26): environment = fully 3D Lykos-inspired Martian terraformed underground colony (stacked blocky settlement, real building height, volumetric haze, amber lanterns; zero natural light). Camera = tilted perspective follow-cam ~58–62° down, FOV ~38°, street-scale (not ortho, not bird's-eye, not whole-map). Heroes/monsters remain 2D painted sheets as standing cards/billboards (2.5D); 3D sculpted/rigged characters deferred for v1. Environment habs are **3D kitbash modules** (Quaternius Sci-Fi MegaKit Standard, CC0) retextured with Lykos maps; 2D facade cards are retired. Mix ~70% colony forms / ~30% western wood/brass/lantern wear on those forms, plus 2.5D western characters — not western-only-on-characters, not a cowboy main street. First/second-gen too-western env art (saloon porch, hitching post, chapel steeple, ranch porch) is superseded. Hotspot string IDs Saloon/Chapel/Homestead stay; meshes are utilitarian colony blocks. Comfy env tiles = albedo/normal/AO for 3D mesh UVs, not a 2D tilemap. |
| DEC-024 | Owner accepted ALL remaining author proposals wholesale (2026-08-24): rematch R-07, map/civilians R-10, stat tables, cooldowns, netcode stack R-50, GameSim seam R-51, passwordless callsign accounts R-44, UX §10, and all other former [proposal] lines |

### Evidence registry (referenced by fixtures' `traces_to` and requirement tags)

**Brief clauses (owner's original brief, 2026-08-24):**
- BRIEF-1 — win = kill all monsters in every wave
- BRIEF-2 — lose = all civilians die
- BRIEF-3 — enemies are virus-mutated colonists and livestock (realized as the R-17 roster's identities and the character art spec)
- BRIEF-4 — civilians shelter in hotspots that must be protected
- BRIEF-5 — per-wave planning phase with finite offensive/defensive resources
- BRIEF-6 — monsters are individually configurable; baseline AI auto-attacks the nearest available target

**Comparable audit (Orcs Must Die! / Sanctum 2 pattern, from product knowledge — no formal market study):**
- AUD-1 — comps separate a build phase from real-time hero combat; per-kill currency with carryover is the core loop
- AUD-2 — comps show incoming wave composition during planning (owner chose partial preview instead, DEC-018)
- AUD-3 — comps use pooled HP rather than per-entity civilian sim (basis for DEC-002)
- AUD-4 — comps run 2–4 players, 5–12 waves, 20–40 min sessions
- AUD-5 — 4–6 enemy archetypes (swarm/fast/tank/ranged/special) suffice
- AUD-6 — real-time netcode is the dominant engineering cost; host-authoritative is the standard mitigation

## 13. Risks

1. **Netcode is the schedule risk** (AUD-6). Mitigation: host-authoritative + pure-C# sim keeps multiplayer a transport problem, not a logic problem; build sim + solo first, wire Netcode against the same command API.
2. Wave-table balance (R-19) is deliberately unfixtured; requires playtesting time in the final days.
3. Trust-based accounts (R-44) are spoofable by design; acceptable for scope, documented.
