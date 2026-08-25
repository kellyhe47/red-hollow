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

### DEC-RUN-2 — damage-reduction arithmetic must not inherit the IEEE straddle (raised by 007's test-writer, 2026-08-25)
`90 * 0.7 == 62.99999999999999` in IEEE double, so a naive `Math.Floor(damage * 0.7)` yields **62**
where R-31/DEC-009's `floor(damage * 0.7)` means **63**. Verified: 16 integer damage values below
1000 disagree — 90, 170, 180, 330, 340, 350, 360, 650, 660, 670, 680, 690, 700, 710, 720, 730.

No fixture catches this (G-020 pins 15 → 10, which is stable either way), and 007's test-writer
deliberately steered its parametrized values clear of every straddling value rather than ossifying
the artifact into the suite. That was the right call — but it means the suite will not catch a
wrong choice here, so the decision is recorded rather than left to the implementer's taste.

**Resolution:** reduced damage is `Math.Floor(damage * (1.0 - reduction) + 1e-9)`. The epsilon
guard restores exact-arithmetic agreement on all 16 straddling values while leaving genuinely
fractional results alone (`15 * 0.7 = 10.5` still floors to 10, matching G-020; even divides are
untouched). This is a real correctness matter, not a style preference: the sim is host-authoritative
and replicated (R-51), so an off-by-one in reduced damage propagates to every client's HP bar.

Applies wherever a fractional multiplier is floored — today Sawbones' 30% DR (R-31), and the same
guard belongs on the lasso slow (R-31/008) and the sell refund (R-22/005) if either ever floors.
`floor(cost * 0.5)` is exact in binary and needs no guard.

### DEC-RUN-3 — profile save timing: the fixture is broader than the PRD prose (raised by 009's test-writer, 2026-08-25)
R-43 states profiles persist *"at each level-up and match end"*. But **G-025 pins a
`profile_store.save` external call on an accepted skill-point spend**, which is neither of those.
G-026 (rejected spend) and G-024 (kill below threshold) both pin **no** save, so the fixtures are
internally consistent — the PRD sentence is simply narrower than the behaviour the fixtures require.

`eval/golden/*.json` outranks `docs/PRD.md` in this repo's precedence order, so the fixture wins.

**Resolution:** the operative rule is *"persist on any profile mutation that must survive"* —
level-up, accepted spend, and match end. Rejections and non-levelling kills write nothing, which is
what G-024/G-026 defend against (R-43's stated intent was to avoid hammering the store mid-combat,
and that intent is preserved).

This is a **PRD wording gap, not a spec violation** — no fixture, manifest or requirement changes,
and nothing was edited under `eval/`. Worth a one-line PRD amendment at the owner's convenience;
not a blocker and not an implementer's call.

### Noted, deliberately not acted on
- **R-40 turret credit is resolved by the shell, not the sim.** `AwardKillXp(kill, accountId)`
  receives the account as an argument, so "turret kills credit the placer" is the caller's mapping
  (placer → hero → account). Making the sim *enforce* it would need `MonsterKillRequest` to carry
  the placeable id so the sim could read `Placeable.OwnerPlayerId` — a seam change, not an
  implementation detail. 009's tests pin only that XP lands on the credited account and never on
  another player, which is all the current API can guarantee.
- **`IProfileStore.Load` reference semantics are unspecified** — mutate-in-place (what
  `InMemoryProfileStore` does) versus detached-copy-written-back. Both satisfy every fixture;
  009's tests deliberately never depend on which.
- **`Hero` has no `IsValidTarget` predicate** where `Hotspot` does (`Civilians >= 1`). A real
  asymmetry in the entity contract, but `Alive` serves, and closing it means editing shared
  `Entities.cs` mid-wave. Candidate tidy once wave A merges.
- **`LineEntity.Kind` has no closed vocabulary.** The fixture loader understands
  hero/hotspot/monster/barricade and throws otherwise, but the production field is a free string.
  007's tests assume an allowlist (`== "monster"` damages) rather than a denylist.

### DEC-RUN-4 — R-33's respawn *execution* was unowned (found by 007's implementer, 2026-08-25)
R-33: *"Hero at 0 HP dies instantly and respawns at the team spawn at full HP after 10s."*
Only the **scheduling** half existed — `Hp` → 0, `Alive` → false, `RespawnAt` set, `hero_died`
carrying `respawn_at`. **Nothing ever revived the hero.** Verified by grep across the whole
assembly: no respawn tick, and `SimConfig.RespawnPoint` was read nowhere outside its own
declaration. As shipped, a dead hero stays dead for the rest of the match.

G-021 pins only that the deadline is *set*, so no fixture catches this, and it was not among
ticket 007's written acceptance criteria — it fell in the gap between "what the fixtures grade"
and "what the requirement promises". Ticket 007 owns R-33, so it owns the revive.

**Resolution:** routed back through 007's test-writer for a `TickHeroRespawns`-style seam and
tests (including the exact-deadline boundary, which "after exactly 10 seconds" leaves ambiguous),
then re-locked and implemented. Same handling as the R-35 regen gap.

**Pattern worth noting for the remaining tickets:** twice now a requirement owned by a ticket had
no fixture *and* no acceptance criterion, and was therefore about to ship unimplemented. Fixture
coverage is not requirement coverage. When dispatching 004/005/006/008, check each ticket's
`owns_requirements` against what its criteria actually exercise, not just against its fixtures.

### Pre-dispatch requirement audit (2026-08-25, after DEC-RUN-4)
Applying the DEC-RUN-4 lesson to every not-yet-dispatched ticket: checked each owned requirement
against what its criteria actually exercise, not just against its fixture list. Found three more
requirements that would have shipped unimplemented, all invisible to the golden suite:

| Ticket | Requirement | Why it would have been missed |
|---|---|---|
| 004 | **R-04** wave interstitial | no fixture, no criterion — sim must expose bounty-this-wave + civilians remaining and auto-advance |
| 004 | **R-05** partial wave preview | no fixture, no criterion — and it is a *negative* requirement: replicated state must expose active entry points WITHOUT leaking monster types or counts |
| 004 | **R-14** per-wave tunnel subset | folded into "wave table is config" but never stated |
| 005 | **R-25** any player may spend | trivially true today, but nothing pinned it against a future ownership check |
| 006 | **R-23 barricade destruction** | **no operation damages a placeable at all.** B-002 makes a blocking barricade the monster's target "until destroyed", and 002 already honours `Exists` — but nothing can ever clear it. A targeted barricade is immortal and blocks its lane for the whole match. |
| 006 | **R-23 Med Station** | only the `PlaceableType.MedStation` string constant exists; the 5 HP/s heal in radius 5 is entirely absent |
| 008 | **R-32 cooldowns** | G-018 grades the lasso's *effect*; nothing grades that Q/E are gated at 8s/20s at all |
| 008 | **R-32 rank scaling** | max rank 3 and ~+25%/rank unpinned |
| 008 | **R-31 the other five abilities** | only Lasso is fixtured; Fan the Hammer, Deadeye, Stampede, Whirl, Bulwark and the two class passives are real sim rules with no coverage |

All added as explicit acceptance criteria in **both** `run/tickets.json` and `.tdd/tickets/*.md`
before dispatch, so the decomposition is amended while it is still editable rather than discovered
mid-implementation. Coverage gate re-run green.

**Ownership note:** DEC-RUN-1 assigned `PlaceableCatalog` to 005, but R-23 belongs to 006. They are
sequential (006 depends on 005), so there is no concurrency hazard: 005 populates the catalog
because it needs purchase costs, and 006 consumes the effect numbers already in it.

### Wave B file allocation (2026-08-25)
004 and 008 could both plausibly need `Entities.cs`, which would make them non-parallel.
Resolved by allocation rather than serialisation:
- **008 owns `Entities.cs`** this wave — it genuinely needs a home for per-hero ability
  cooldown/rank state.
- **004 keeps per-wave data in its own `WaveTable.cs`.** Which tunnels are active for wave N is
  wave-table *data*, not mutable match state, so this is the better design regardless of the
  scheduling constraint.
- New request/result types go in per-ticket files (`Commands.Waves.cs`, `Commands.Abilities.cs`)
  so neither edits the shared `Commands.cs`. `AbilityResult`'s shape is pinned by G-018 and must
  not change.

### DEC-RUN-5 — `WaveState.TotalWaves` is authoritative; `SimConfig.TotalWaves` seeds it (raised by 004's test-writer, 2026-08-25)
Both exist and nothing in the PRD or fixtures says which the rules read. All five T-04 fixtures set
them to the same 10, so the acceptance contract cannot disambiguate — a live trap, since balance
tuning edits `SimConfig` while a rule might read `State.Wave`.

**Resolution:** `State.Wave.TotalWaves` is authoritative at runtime; `SimConfig.TotalWaves` is the
tuning surface that seeds it at match creation. The fixture loader populates
`given.preexisting_state.wave.total_waves`, so state is what the acceptance contract actually
drives, and it matches the `ColonyMap` → `CreateMatchState()` pattern already established: config is
authored, state is live.

### DEC-RUN-6 — R-03's 60-second planning timer had no seam (found by 004's test-writer, 2026-08-25)
R-03: *"Each wave begins with a 60-second planning phase; combat starts **early** when all connected
players ready up."* "Early" presupposes a normal timeout path. Confirmed by grep: **nothing in the
assembly reads `SimConfig.PlanningDurationSeconds`.** The only planning → combat edge in the sim's
surface is `SetPlayerReady`, so a lobby where one player never readies — AFK, or disconnected while
un-ready — sits in planning forever and the match cannot progress.

G-017 grades only the all-ready *early* start, which is why no fixture catches it.

**Resolution:** routed back to 004's test-writer for an expiry seam plus tests, including the
inclusive boundary (G-019 convention) and a `trigger` distinguishable from G-017's `"all_ready"`.

**Fourth instance of the same pattern** — after R-35 regen, R-33 respawn execution, and the
pre-dispatch audit's nine. Every one was a requirement whose fixture graded a *neighbouring*
behaviour, leaving the requirement itself unenforced. The tell is a requirement whose fixture pins
an exception, an override, or an "early" path: the *ordinary* path is the one nobody grades.
