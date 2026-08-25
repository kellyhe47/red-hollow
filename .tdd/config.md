# tdd-orchestrator run config — The Red Hollow

## Working branch
`main` (checked out at invocation; sequential tickets commit here, parallel ticket
branches `tdd/<id>` merge back here). **Never pushed.**

## Verified commands (smoke-tested 2026-08-25 at Phase 0)

| Name | Command |
|---|---|
| validate-spec | `python3 eval/verify_claims.py && python3 eval/verify_fixtures.py` |
| test-golden (full) | `dotnet test sim/RedHollow.slnx --nologo` |
| test-golden (one fixture) | `dotnet test sim/RedHollow.slnx --nologo --filter "Name~G-0NN"` |
| coverage | `python3 run/coverage_check.py` |

Verified by probe: the runner discovers `[TestCase]` rows as individual cases, executes
them, reports failures non-zero, and `--filter "Name~..."` selects a subset. Probe deleted.

`dotnet` = Homebrew SDK 10.0.400 at `/opt/homebrew/bin/dotnet`. **No Unity editor on this
machine** — which is exactly the R-51 invariant test: GameSim + its NUnit suite build and
run with zero Unity present.

## Board of record
- `run/tickets.json` — machine-readable scope / acceptance / `owns_requirements` /
  `grades_fixtures` / `depends_on`. **The id-coverage source of truth.**
- `run/coverage_check.py` — asserts two-way coverage, reading R-ids from `docs/PRD.md`
  and G-ids from `eval/golden/` at run time. Currently green: 14 tickets, 51 requirements
  all owned, 30 fixtures all graded, no dangling ids.
- `.tdd/tickets/NNN-<slug>.md` — per-ticket **status carrier** (status, iterations,
  locked `test_files`, attempt log). Generated from `run/tickets.json`; ids match (T-01 → 001).

## Repo conventions that bind every agent
1. Sim sources live at `unity/RedHollow/Assets/GameSim/` so Unity and `dotnet` compile the
   *same files*. `sim/GameSim/GameSim.csproj` globs them. **Never fork these sources.**
2. `GameSim.asmdef` has `noEngineReferences: true`. Zero UnityEngine in GameSim, mechanically.
3. **Never edit a fixture's `expect`.** That is a spec change requiring PRD + manifest +
   provenance updates — the owner's call, not an agent's.
4. Concurrent art agents are live in this repo (`art/`, `docs/comfy-prompts/`).
   **No implementation agent may touch `art/`, `docs/`, or `eval/`.**

## Resume procedure
1. `git log --oneline -12 && git status --short` — see what actually landed.
2. Run validate-spec, test-golden, coverage. **The fixture pass count is the progress bar.**
3. Read `.tdd/tickets/*.md` frontmatter; first ticket not `green` is the work front.
   A ticket in `awaiting-merge` has already passed its gate — resume at merge-back, not
   at its implementation loop.
4. Re-derive from disk. Never trust a summary over `dotnet test` output.

## Orchestrator decisions (resolved during the run)

### DEC-RUN-1 — catalog defaults mirror the PRD (raised by 002's test-writer, 2026-08-25)
The 001 catalog seams shipped **empty** with a throwing lookup, but `SimConfig`'s own class
doc already establishes the opposite convention: *"Defaults mirror the PRD; the Unity shell
overrides them from ScriptableObjects so balance changes never require a code change
(R-16, R-17, R-23, R-31)"* — and every scalar tunable in the file follows it
(`StartingScrip = 500`, `TotalWaves = 10`, `SawbonesDamageReduction = 0.3`, …).

**Resolution:** the three catalogs follow the same convention as every other tunable —
`new SimConfig()` carries the PRD table, overridable per instance. `StatsFor`/`KitFor` keeps
throwing for a genuinely unknown key (a typo, or a custom catalog missing a row), which is
what the loud-failure design was actually for. "Config, not code" is satisfied by the values
being *per-instance overridable data*, not by the table being absent — there is no external
config file in this repo and no ScriptableObject asset yet, so an empty default would just
make the sim unusable outside the fixtures.

Each owning ticket populates only its own catalog and fixes only its own `Config.cs` doc line:
002 → `Monsters`, 005 → `Placeables`, 008 → `HeroKits`. `MonsterCatalog.cs`'s type-level
"Ships empty" comment is 002's to correct.

002's tests as written (`new SimConfig().Monsters.StatsFor(...)`) are **correct and stay**.
