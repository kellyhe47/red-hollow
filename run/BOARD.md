# The Red Hollow — implementation run state

Machine-readable board: `run/tickets.json` (scope, acceptance criteria, owned R-ids, graded G-ids, deps).
Coverage assertion: `python3 run/coverage_check.py` — reads R-ids from `docs/PRD.md` and G-ids from
`eval/golden/` at run time; fails if any requirement is unowned, any fixture ungraded, or any ticket
cites an id that does not exist.

## Verification commands

```bash
python3 eval/verify_claims.py && python3 eval/verify_fixtures.py   # validate-spec
dotnet test sim/RedHollow.slnx                                      # test-golden (all 30 fixtures)
python3 run/coverage_check.py                                       # board coverage
```

`dotnet` is Homebrew's bottled formula at `/opt/homebrew/bin/dotnet` (SDK 10.0.400) — no admin
install, no Unity editor required. That is the invariant test: GameSim builds and its NUnit suite
runs with no Unity present.

## Layout

| Path | What |
|---|---|
| `unity/RedHollow/Assets/GameSim/` | the pure-C# simulation — single source of truth for every fixture-covered rule |
| `unity/RedHollow/Assets/GameSim/GameSim.asmdef` | `noEngineReferences: true` — Unity mechanically enforces zero-UnityEngine |
| `sim/GameSim/GameSim.csproj` | globs those same sources so `dotnet build` works with no Unity |
| `sim/GameSim.Tests/` | the `test-golden` NUnit adapter |
| `run/` | this board |

The sim sources live inside `Assets/` so Unity and `dotnet` compile *the same files*. Never fork them.

## Ticket order

Dependency-ordered. T-01 first — it is the acceptance harness everything else is graded by.

```
T-01 sim scaffold + golden adapter
 ├── T-02 monster AI            G-001..G-005
 ├── T-03 hotspots + defeat     G-006..G-009
 │    └── T-04 match FSM/waves  G-010,011,012,016,017
 │         └── T-05 economy     G-013,014,015,022
 │              └── T-06 placeable effects  G-027,028,029
 ├── T-07 hero damage/respawn   G-020,021,030
 │    └── T-08 abilities        G-018,019
 ├── T-09 progression           G-023..G-026
 └── T-10 Unity shell
      ├── T-11 netcode + rematch
      ├── T-12 UI screens S1-S7
      └── T-13 asset seam + feel
           └── T-14 playtest tuning
```

## Rules that bind every ticket

1. **Never edit a fixture's `expect`** to make code pass. That is a spec change requiring PRD +
   manifest + provenance updates, and it is the owner's call.
2. Every fixture must **fail for its intended reason before** the covered behavior is implemented,
   then pass. Red first, always.
3. Fixture-covered rules live in `GameSim` only. A game rule in a MonoBehaviour is misplaced.
4. All visuals load through swappable references. No code path may block on an asset existing.

## Resume procedure

State lives on disk, not in any agent's context. To resume:

1. `git -C . status && git log --oneline -8` — see what actually landed.
2. Run the three verification commands above. The fixture pass count *is* the progress bar.
3. `python3 run/coverage_check.py` — confirm the board still matches the specs.
4. Find the first ticket in the order above whose graded fixtures are not all green; that is the
   work front. Re-derive from disk — never trust a summary over `dotnet test` output.

## Environment facts

- `.NET SDK 10.0.400` installed via `brew install dotnet` (bottled, no admin password).
- **Unity Editor is not installed on this machine** and needs the owner's Unity account/licence.
  T-01..T-09 (all 30 fixtures, the entire acceptance contract) need no Unity. T-10 onward do.
- **UGS project id** is not yet supplied. Per the handoff it is injected via config; local-loopback
  multiplayer must work without it, so T-11 is not blocked on the owner for its loopback path.
