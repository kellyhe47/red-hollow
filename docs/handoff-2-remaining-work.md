# Implementation handoff #2 — The Red Hollow

You are taking over a TDD-orchestrated build that is **16 of 19 tickets green**. The simulation
is complete and the game runs end to end headlessly. Three tickets remain, all in the Unity
shell. Everything you need is in this repo — read this file, then `.tdd/config.md`, then the
board.

---

## 1. Verified state (re-derive this yourself before trusting it)

```bash
python3 eval/verify_claims.py && python3 eval/verify_fixtures.py   # validate-spec
dotnet test sim/RedHollow.slnx --nologo                            # 356/356, incl. 30/30 fixtures
python3 run/coverage_check.py                                      # 19 tickets, 51 reqs owned
```

Unity EditMode suite — **70/70** (check the lock first, see §6):

```bash
/Applications/Unity/Hub/Editor/6000.5.9f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath unity/RedHollow \
  -runTests -testPlatform EditMode -testResults /tmp/t.xml -logFile /tmp/t.log
```
Parse `total`/`passed`/`failed` off the NUnit3 XML root. **A full run takes 5–10 minutes.**

110 commits on `main`. **Nothing has been pushed and nothing should be** — pushing is the owner's.

---

## 2. Sources of truth, in precedence order

1. **`eval/golden/*.json`** — 30 fixtures, 19 behaviours (`eval/golden-manifest.json`). The
   acceptance contract. **Never edit a fixture's `expect`.** That is a spec change requiring PRD +
   manifest + provenance updates and is the owner's call. No fixture has been edited in this run.
2. **`docs/PRD.md`** — R-01..R-73, decision registry, evidence registry.
3. **`docs/ui-wireframes.html`** (S1–S7 + cross-cutting states, **all normative** — this is
   ticket 012's contract) and `docs/architecture.excalidraw` (recover with `check_diagram.py`).
4. **`.tdd/config.md`** — 11 orchestrator decisions (DEC-RUN-1..11) resolving ambiguities the PRD
   and fixtures left open. **Read all of them.** Several bind future work.

---

## 3. The invariant — check it yourself, always

**Every fixture-covered rule lives in a pure-C# `GameSim` assembly with zero UnityEngine
dependencies, executed only on the host.** This is now enforced four ways:

- `GameSim.asmdef` has `noEngineReferences: true`
- `sim/GameSim/GameSim.csproj` builds it with no Unity reference at all
- `Assets/Editor/ProjectVerify.cs` asserts the *loaded* assembly has zero `Unity*` references
- **`T10_HostLoopTests.No_MonoBehaviour_in_the_shell_writes_sim_world_state`** — a Mono.Cecil IL
  walk over every `MonoBehaviour`-derived type **and its compiler-generated nested types**
  (lambdas, local functions, iterators), flagging field stores, by-ref primitive loads, property
  setters, and mutating collection calls whose receiver came out of sim world state. The
  world-state type set is **derived** as the transitive closure from `MatchState` and
  `AccountProfile`, so new entity fields are covered without editing a list.

**This test bites.** It was verified by planting a violating MonoBehaviour (`Scrip += 50`,
`Monster.Hp -= 1`) and watching it fail with the exact method and field named. There is also an
**anti-vacuity guard** requiring the shell to contain at least one MonoBehaviour, so the invariant
cannot pass by having nothing to scan.

Practical consequences: **object initializers count** (`new Monster { … }` in a component trips
it); keep logic in plain C# classes; `MatchHostBehaviour` is a two-member pump and should stay
one. Command request types (`HotspotAttackRequest` etc.) and `SimConfig` are deliberately outside
the flagged closure — building a request is how you issue a command, and R-16 says the shell tunes
config.

---

## 4. What is done

| Ticket | Owns | What landed |
|---|---|---|
| 001 | R-51, R-54 | `GameSim` scaffold + the golden-fixture NUnit adapter; `MatchSim` split into per-area partials |
| 002 | R-16, R-17, R-18 | Nearest-target AI, lowest-ordinal tiebreak, barricade redirect via `IPathOracle`, Burrower carve-out, R-17 roster |
| 003 | R-10..R-13, R-72 | `ceil(dmg/10)` clamped civilian kills, colony-wide defeat, `ColonyMap.V1()` |
| 004 | R-01..R-05, R-14, R-19 | Match FSM, wave lifecycle, bounty, victory, wave table, R-05 leak-proof preview, planning timer |
| 005 | R-20..R-22, R-24, R-25 | Shared scrip, purchase/sell, server-side zone validation, R-23 cost table |
| 006 | R-23 | Spike/dynamite/turret effects, **barricade destruction**, **Med Station** |
| 007 | R-26, R-33..R-36 | Sawbones DR, death, **respawn execution**, no friendly fire, **out-of-combat regen** |
| 008 | R-31, R-32 | All six abilities, both remaining passives, cooldowns, rank scaling, kit catalog |
| 009 | R-40..R-44 | XP curve, skill points, `SaveProfilesAtMatchEnd`, `JsonProfileStore` |
| 010 | R-50, R-52 | `HostLoop`, `ISimHost`/`MatchSimHost`, `PartyRoster`, R-52 seams, **the Cecil invariant** |
| 011 | R-07, R-53, R-55 | `NetSession`, loopback transport, 10-wave match, rematch, disconnect, non-pausing ESC |
| 015 | R-18 | **Monster attack cadence** (`TryMonsterAttack`) |
| 016 | R-30 | Scene, tilted isometric camera (~60–70° down), WASD + mouse-aim input, placeholder visuals |
| 017 | — | **Wave spawning** (`SpawnWave`) |
| 018 | — | **Hero and monster movement** (`TickMonsterMovement`, `MoveHero`) |
| 019 | — | The playable bootstrap — spawn → target → move → gate → damage → defeat |

Tickets 015, 017, 018, 019 and the **bolded** items above did not exist in the original
decomposition. See §5.

---

## 5. ⚠️ The most important lesson: fixture coverage is not requirement coverage

The 30 fixtures grade **19 behaviours**; the PRD asks for **51 requirements**. Nine requirements
would have shipped unimplemented, each because a fixture graded a *neighbouring* behaviour:

| Requirement | The hole |
|---|---|
| R-33 respawn | The deadline was set; nothing ever revived anyone |
| R-35 regen | No seam at all |
| R-03 planning timer | Only all-ready ended planning; one AFK player hung the match forever |
| R-20 starting stake | `StartingScrip` declared, read nowhere — matches started broke |
| R-18 attack cadence | Nothing rate-limited attacks; 60fps meant 60× damage |
| R-23 barricade HP | **No operation damaged a placeable** — a 100-scrip wall was immortal |
| R-23 Med Station | Only a string constant existed |
| — spawning | **Nothing created a `Monster`** — a match could never contain one |
| — movement | **Nothing advanced a position** — defeat was unreachable; the lasso slowed nothing |

**The tell:** when a fixture's name carries a qualifier — *"schedules"* respawn, *"early"* start,
*"carries over"* — the unqualified case is usually unowned. When a config knob exists, grep for who
**reads** it.

**Do this before implementing each remaining ticket:** take its `owns_requirements`, read each one
in `docs/PRD.md` in full, and ask what code would have to exist. Do not assume a green neighbour
means the requirement is met. `run/coverage_check.py` proves ids are *owned*, not *implemented*.

---

## 6. Traps that cost time in this run

- **Unity takes an EXCLUSIVE project lock.** A headless run cannot start while the Editor GUI has
  the project open — it stalls silently right after "Successfully changed project path" and writes
  no results XML. **Always `pgrep -f "Unity.app/Contents/MacOS/Unity"` first.** Never delete
  `unity/RedHollow/Temp/UnityLockfile` without confirming no Unity process is alive: two instances
  writing `Library/` corrupts it. If the owner has the Editor open, ask them to close it.
- **Unity 6 pairs with Netcode for GameObjects 2.x** (2.13.2 here). Most tutorials target 1.x and
  have a different API surface.
- **`Assert.Multiple` does not exist** in Unity's bundled NUnit (it does in the dotnet suite).
- **`MatchStatus.InProgress` and `MatchPhase.Combat` are both the literal string `"combat"`.**
  Different fields. G-010 changes `phase`; G-011 changes `status`.
- **Deadlines are INCLUSIVE repo-wide** (`now >= deadline`). G-019 is the precedent — it expires a
  status effect at exactly `expires_at` and its `defends_against` names strict-greater-than drift.
  Respawn, cooldowns, the planning timer and Bulwark all follow it.
- **Adding a member to `ISimHost` breaks the locked T10 fake at compile time** and takes the whole
  suite red. `IMatchSimHost : ISimHost` is the established widening pattern.
- `Vec2` has no operators or `Zero`. `MatchSim` is sealed. `Monster` has no damage field —
  per-hit damage arrives on the request, which is what G-006 pins.

---

## 7. Process — the TDD loop you should keep running

The run used the `tdd-orchestrator` skill. State is on disk in `.tdd/tickets/*.md` (status
frontmatter, locked `test_files`, attempt log) and `run/tickets.json` (scope, acceptance criteria,
owned R-ids, graded G-ids — the id-coverage board asserted by `run/coverage_check.py`).

Per ticket: **a test-writer agent writes failing tests → you verify they are red for the right
reason → commit them (they are now LOCKED) → a separate implementation agent makes them pass →
you run the tests yourself → regression gate → checkpoint commit → mark green.**

Non-negotiables:
- **The test-author and the implementer must be different agents.** An agent that can edit its own
  tests can trivially "pass" them.
- **Never take an agent's word that tests pass.** Run them yourself and read the output.
- **Never weaken a locked test to get green.** A genuinely wrong test is fixed through the
  test-writer, deliberately and visibly.
- Verify a new test *bites* when it guards something structural — plant a violation, watch it fail,
  revert. A green-by-construction architecture test is worthless.

Sequential tickets commit straight to `main`. Parallel waves use `git worktree` per ticket; all
worktrees are currently removed and no `tdd/*` branches remain.

---

## 8. What is left

### Ticket 012 — UI screens S1–S7 and cross-cutting states (R-60..R-63)
**The largest remaining piece, and DoD item 2 depends on it.**

`docs/ui-wireframes.html` is **normative in full** — every screen *and* every cross-cutting state
listed there is a requirement: bad join-code error, greyed unaffordable shop items, dead-hero
spectate overlay, civilians-lost toast + red flash, lost-hotspot marking. Read the file; do not
work from the screen list alone.

Everything the HUD needs already exists on the sim:
- wave n/10 → `State.Wave.Number` / `.TotalWaves`
- monsters remaining → `State.Wave.LivingMonsterIds`
- per-hotspot civilians → `State.Hotspots[..].Civilians`
- shared scrip → `State.Team.Scrip`
- HP / level / XP / unspent points → `Hero` + `IProfileStore.Load(accountId)`
- cooldowns → `Hero.CooldownReadyAt` (absent key = ready)
- R-04 interstitial → `WaveSummary()` (bounty earned **this wave**, civilians remaining)
- R-05 planning preview → `PreviewUpcomingWave()` — returns **only** activating tunnel indices and
  carries no monster types or counts *by construction*. **Do not work around this to show
  composition; hiding it is the requirement (DEC-018).**

R-62: the level-up overlay is **non-blocking** — the sim never pauses for it, same as R-55's ESC
overlay (011 made that structural: `Step` never consults the overlay flag).

Rejections surface as `purchase_rejected` / `spend_rejected` events carrying a reason string.
`SellResult` has **no** reason field — a refused sale reports only `accepted: false`.

### Ticket 013 — asset seam and feel pass (R-15, R-64)
Art has arrived in volume: **50 textures, 53 character images, 78 icons**, plus `art/props/`,
`art/ui/`. Check `art/asset-log.csv` first — a Comfy agent may still be writing there, and
`art/characters-plan.md` and `art/asset-log.csv` have shown uncommitted edits during this run.
**Do not commit another agent's in-flight art work**; this run deliberately never touched `art/`.

The seam is already built and tested (016). `PlaceholderVisualResolver` **deliberately does not
probe** for an asset before falling back — a probe is a code path that can answer "absent", which
is where blocking-on-art creeps in. **Chain a real resolver in FRONT of it**; do not teach it to
look.

**R-15 / DEC-026 presentation:** art-to-scene means a **3D Lykos cavern** (stacked colony meshes,
rock walls, lift shaft), a **tilted isometric camera (~60–70° down)**, and **2D standing-card**
heroes/monsters — not a 2D tilemap, not a western main street, not sculpted character models.
Lantern Deep lighting still holds: dark warm ambient, amber **point** lights, volumetric fog,
rock-dome mesh as sky; **no sun / directional golden-hour**. **URP 17.5.0 is the active pipeline**
(adopted deliberately for exactly this; see the git log for 010). Read
`docs/comfy-prompts/00-shared-style.md` before touching visuals. Environment Comfy tiles are
albedo/normal/AO for those 3D meshes.

R-64 feel targets hang off these sim events: `monster_damaged`, `hero_damaged`, `hero_died`,
`hero_respawned`, `civilians_killed`, `hotspot_emptied`, `placeable_created`,
`placeable_triggered`, `placeable_broken` (a spent trap) vs `placeable_destroyed` (a wall
collapsing — deliberately distinct so they can have different effects), `turret_fired`,
`status_applied`, `status_expired`, `wave_spawned`, `wave_complete`, `combat_started`,
`match_victory`, `match_defeat`.

### Ticket 014 — wave-table tuning and non-goal guard (R-06, R-70, R-71, R-73)
`WaveTable.V1()` ships a playtestable first pass: one new archetype layered in per wave (ravagers
w2, spitters w4, behemoths w5, burrowers w6), the active breach set rotating every wave so the
previous wave's wall line is never simply reusable, wave 8 an all-four rehearsal, wave 9 trading
width for weight, wave 10 thirty mixed from all four. It is per-instance config — retune without
touching rule code.

R-06's 25–35 minute session is a **playtest criterion, not machine-testable**. R-70/71/73 are
non-goals to confirm nothing shipped: no PvP, no host migration, no mid-match join, no spectator
beyond dead-hero cam, no cross-match meta-economy, no second map, no boss, no difficulty settings.

Numbers most likely to need tuning, all config and none PRD-specified: ability damage (Whirl
4.0/35, Stampede 4.0/25, Fan the Hammer 12×6, Deadeye 60), placement radii (hotspot 4.0, tunnel
mouth 3.0, footprint 1.5), hero move speed (4.0), dynamite blast radius (3.0 — taken from G-029's
fixture inputs, **owner-confirmable**).

### Follow-up ticket — real NGO transport, Lobby and Relay
**011 is green but covers everything EXCEPT actual networking.** `LoopbackNetTransport` is
in-process by design. `INetTransport` is the swap point — implementing it over NGO 2.13.2 plus
Unity Lobby and Relay, and hand-verifying two machines, is genuinely outstanding work that is
**not** claimed as done.

The project is linked to cloud project `ac5dd937-4e73-44e8-8ac5-fb148787ce3b`, org `kellyqhe47`,
written into `ProjectSettings/ProjectSettings.asset`. Loopback needs no UGS id and must keep
working without one.

---

## 9. Definition of done

1. `validate-spec` and `test-golden` (all 30) pass from a clean checkout; full suite in CI.
   **Status: met locally.** CI is not set up — worth adding.
2. All PRD requirements met; every wireframe screen and state present. **Status: 012 and 013
   outstanding.** Apply §5 before declaring any requirement met.
3. A 2-player co-op session completes a full 10-wave match — victory, defeat and rematch all
   exercised. **Status: met over loopback, headlessly. Real transport outstanding.**
4. Placeholder-art build is shippable; generated art drops in as pure asset swaps. **Status: the
   seam is built and tested; the swap itself is 013.**

Open owner-level questions, all recorded in `.tdd/config.md`: the dynamite blast radius (not in
the PRD), DEC-RUN-7's multiplicative Bulwark stacking (balance), and a one-line PRD amendment for
DEC-RUN-3 (R-43's save-timing prose is narrower than G-025 requires — the fixture wins, the prose
should catch up).
