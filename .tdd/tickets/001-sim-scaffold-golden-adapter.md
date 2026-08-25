---
id: 001
title: GameSim assembly scaffold + golden-fixture NUnit adapter
status: pending
depends_on: []
touches: [unity/RedHollow/Assets/GameSim/MatchSim.cs, unity/RedHollow/Assets/GameSim/MatchSim.*.cs (created, stubs only), sim/GameSim.Tests/]
iterations: 0
test_files: []
branch: ""
board_id: T-01
owns_requirements: [R-51, R-54]
grades_fixtures: []
---

## Scope

Pure-C# GameSim assembly (netstandard2.1, zero UnityEngine) + the test-golden adapter: maps each fixture when.operation to a real sim entry point, loads given through production boundaries, captures the four observation surfaces, canonicalizes per eval/golden-manifest.json, deep-compares to expect.exact.

## Acceptance criteria

- [ ] dotnet test runs with no Unity editor installed
- [ ] all 30 fixtures are discovered as individual NUnit cases
- [ ] every fixture fails for its intended reason (NotImplemented) before behavior lands
- [ ] adapter never mutates eval/golden

## Test plan

_Filled in by the test-writer._

## Attempt log

