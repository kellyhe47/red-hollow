---
id: 022
title: Play-mode entry point — the scene actually boots the shell
status: green
depends_on: [021]
touches: [unity/RedHollow/Assets/Game/UI/, unity/RedHollow/Assets/Scenes/]
iterations: 1
test_files: [unity/RedHollow/Assets/Tests/EditMode/T22_PlayEntryTests.cs]
branch: ""
board_id: T-22
owns_requirements: []
grades_fixtures: []
---

## Scope

Found by the owner pressing Play (2026-08-26): `RedHollow.unity` contains **zero
MonoBehaviours** — nothing runs. `ShellBootstrap` (021) is only ever constructed by tests.
Adds a thin scene-resident entry behaviour that constructs `ShellBootstrap` on load, pumps
it every frame with the frame delta, feeds `HeroInput` from real devices, ensures a camera
and `EventSystem` exist, and is actually serialized into the committed scene asset.

## Acceptance criteria

- [x] the committed scene contains the entry behaviour; pressing Play boots the shell to S1
      (⚠️ hosting-to-combat by mouse needs T-23: the screens had zero interactive controls)
- [x] the entry behaviour is a thin pump — logic stays in `ShellBootstrap` (Cecil invariant)
- [x] input reaches the hero (WASD + mouse aim + SPACE/Q/E) in play mode

## Test plan

`Tests/EditMode/T22_PlayEntryTests.cs` — 7 tests, all EditMode, no play mode. Contract:
`GameEntryBehaviour : MonoBehaviour` (Game/UI/, sealed, [DisallowMultipleComponent]) with
`ShellBootstrap Shell` (readonly accessor), `Func<double> DeltaSource` (clock seam, default
`() => Time.deltaTime`), private Awake/Update/OnDestroy — driven reflectively by the tests.
Plus the input hook 021 left null: `ShellBootstrapOptions.InputSource : IInputSource` and
`ShellBootstrap.Input` accessor; the shell resolves samples through `DefaultHeroInputMap`
and feeds the LOCAL hero (AccountId == LocalAccountId) of the session's live match.

1. **Scene asset** — opens the committed `RedHollow.unity` (scene setup saved/restored):
   exactly one enabled `GameEntryBehaviour`; an enabled Camera still present (the entry
   relies on the SceneBuilder camera, creates none). RED until the .unity file is edited
   and saved — AddComponent-in-test cannot green this.
2. **Awake** — constructs loopback shell (Phase Offline, S1 the only active root; StartHost
   then succeeds with no UGS id); `Shell.Input` non-null (device source supplied);
   EventSystem ensured (created if absent, never duplicated — second test).
3. **Update** — a counting `DeltaSource` proves one Update = one sample = one pump (S2
   appears only after the first Update post-StartHost; 4 Updates = 4 samples); the sampled
   delta reaches `Pump` as frame time (60Hz scripted clock walks S4 → S5 → S3 through the
   router's own `InterstitialSeconds`).
4. **OnDestroy** — "RedHollow_Shell" root gone; second OnDestroy is a no-op.
5. **Input wiring** (R-30/DEC-017) — fake `IInputSource` via options: held W walks the own
   hero +Y through `Pump` alone, X unmoved despite cursor at (-9,-9); released keys hold
   ground; `shell.Input` is the same instance supplied.
6. **Thinness** — entry is a MonoBehaviour in the shell assembly (T10 Cecil scan covers
   it); ≤ 6 declared instance fields, none typed from `RedHollow.Sim`.

Deliberately unpinned: concrete device-source type (legacy vs Input System), camera
creation, exact move distance (sim's speed), any layout/copy. Device key reads are not
EditMode-testable — the wiring + T16's mapping tables are the provable whole.

Stubs (throwing): `Game/UI/GameEntryBehaviour.cs` (new); `ShellBootstrapOptions.InputSource`
field + `ShellBootstrap.Input` throwing accessor added to `Game/UI/ShellBootstrap.cs`.

## Attempt log

_(created 2026-08-26 by the orchestrator after the owner's Play test found the empty scene.)_

- 2026-08-26 green in 1 pass. Red 7/8 verified; green verified by orchestrator: EditMode
  173/173, dotnet 371/371. Closing audit found the next §5 hole — no Button/InputField
  exists anywhere in the shell UI — spun off as T-23 before telling the owner to press Play.

## Handoff notes

- Precedent for the thin-pump shape: `MatchHostBehaviour` (Game/Host) — owns one plain-C#
  object, pumps it, holds no rule. T10's Cecil scan enforces this mechanically.
- Input seam exists: `HeroInput`/`IInputSource` (Game/Input) — device reads belong behind
  `IInputSource`; a Unity-device implementation is part of this ticket if none exists.
- Scene edits are EditMode-testable: load the scene asset via
  `UnityEditor.SceneManagement.EditorSceneManager.OpenScene` and inspect components; the
  entry behaviour's lifecycle can be driven reflectively (Awake/Update) headlessly.
- `activeInputHandler: 2` (both backends) — old `Input.GetKey` works, Input System 1.20
  also installed.
