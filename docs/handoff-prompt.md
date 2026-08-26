# Implementation handoff — The Red Hollow

Build **The Red Hollow**: a 1–4 player co-op wave-defense game in **Unity** (tilted perspective 3D follow-cam, ~58–62° down, WASD + mouse-aim, LoL-style ability kits) defending a terraformed underground Mars colony (Lykos) across 10 monster waves. Western look is on the **heroes**, not the cavern. The spec is complete and review-verified; you need no artifact outside this repo.

## Sources of truth (precedence order; conflicts resolve upward)
1. **`eval/golden/*.json`** — 30 fixtures, 19 behaviors (`eval/golden-manifest.json`). The acceptance contract for every simulation rule.
2. **`docs/PRD.md`** — numbered requirements R-01..R-73, decision registry, evidence registry. Settled conflicts are recorded there (e.g. Burrower carve-outs in B-001/B-002 vs B-003; veteran accounts start with saved ability unlocks per R-31/R-43).
3. **`docs/ui-wireframes.html`** (S1–S7 + cross-cutting states, all normative) and **`docs/architecture.excalidraw`** (recover it with `check_diagram.py`, not by eyeballing).

## Acceptance contract — two commands, kept distinct
- **`validate-spec`** (exists): `python3 eval/verify_claims.py` — spec schema/arithmetic/traceability. Keep green.
- **`test-golden`** (you build this first): an NUnit adapter mapping each fixture's `when.operation` to the real product entry point, loading `given` through production boundaries, capturing the four observation surfaces (result, state_changes, emitted_events, external_calls), canonicalizing per the manifest, and deep-comparing to `expect.exact`. Every fixture must **fail for its intended reason before** the covered behavior is implemented, then pass. Relevant fixtures run per work unit; the full suite runs in CI. **Never edit a fixture's `expect` to make code pass** — that is a spec change requiring PRD + manifest + provenance updates.

## The invariant (check it yourself, always)
**Every fixture-covered rule lives in a pure-C# `GameSim` assembly with zero UnityEngine dependencies, executed only on the host.** Test: `GameSim` compiles and its NUnit suite runs with no Unity editor. Netcode, rendering, and input are shells that send commands in and replicate state out. If a game rule appears in a MonoBehaviour, it is misplaced.

## Dependency facts (typed seams, never TODOs)
- **Art arrives asynchronously** from four ComfyUI pipelines (`docs/comfy-prompts/`). All visuals load via swappable references; the game must be fully playable with primitive placeholders (cubes/unlit color). No code path may block on an asset existing — an implementation ticket "blocked on art" must be impossible by construction.
**Presentation (DEC-026):** the match is a **3D Lykos cavern** (Quaternius Sci-Fi kitbash modules retextured with Lykos maps + URP lanterns + fog) with a **tilted perspective follow-cam**; 2D facade cards are retired. Heroes/monsters are **camera-facing upright billboards** with a blob shadow (2.5D, lantern tint/haze) — not XZ-flat sprites, not 8-dir cycles, not sculpted character meshes. Environment tiles are mesh albedo, not a 2D tilemap. Preliminary assets have already started arriving (check `art/` and ask the owner for the latest drops). Use them to smoke-test the asset seam early, not as a dependency: wire in one representative asset per class (a mesh albedo tile, an icon, a UI frame, a character sheet) in the first days to validate Unity import settings, exact pixel sizes, alpha handling, and texture detail at the real camera — and report any asset that violates its pipeline contract (seams, wrong dimensions, halos) back to the owner so the Comfy workflow can be fixed while it's cheap. Hold bulk imports until an asset set is final; everything not yet covered by a real asset keeps its placeholder.
- **Unity Gaming Services** (Lobby + Relay) needs a UGS project id from the owner — inject it via config; local-loopback multiplayer must work without it.
- **Profile store** (R-43/44): an injected `IProfileStore` interface; fixture tests use a fixture-backed fake; production uses server-local SQLite/JSON keyed by callsign.
- **Wave table** (R-19) is deliberately unfixtured config — ship a first-pass table, tune by playtest.

## Before dispatching any sub-agent
Put the run state on disk: ticket board, per-ticket scope + acceptance criteria + owned R-IDs and fixture IDs, the two verification commands, and a resume procedure. If you produce a ticket decomposition, add a script asserting two-way coverage — every R-01..R-73 owned by ≥1 ticket (in a structured field, not prose), every G-001..G-030 graded by ≥1 ticket, no ticket citing an id that doesn't exist — reading ids from `docs/PRD.md` and `eval/golden/` at run time.

## Definition of done
1. `validate-spec` and `test-golden` (all 30) pass from a clean checkout; full suite in CI.
2. All PRD requirements met; every wireframe screen and state present.
3. A 2-player co-op session (Relay or local loopback) completes a full 10-wave match — victory, defeat, and rematch (R-07) paths all exercised.
4. Placeholder-art build is shippable; generated art drops in as pure asset swaps.
