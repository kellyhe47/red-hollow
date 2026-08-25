---
id: 001
title: GameSim assembly scaffold + golden-fixture NUnit adapter
status: in-progress
depends_on: []
touches: [unity/RedHollow/Assets/GameSim/MatchSim.cs, unity/RedHollow/Assets/GameSim/MatchSim.*.cs (created, stubs only), sim/GameSim.Tests/]
iterations: 0
test_files: [sim/GameSim.Tests/GoldenFixtureTests.cs, sim/GameSim.Tests/GoldenComparison.cs, sim/GameSim.Tests/GoldenComparisonTests.cs, sim/GameSim.Tests/GoldenHarnessGuards.cs, sim/GameSim.Tests/OperationDispatch.cs, sim/GameSim.Tests/ScenarioLoader.cs, sim/GameSim.Tests/FixtureLoading.cs]
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

Adapter is the test set: `GoldenFixtureTests` yields one case per `eval/golden/*.json`
(criteria 2+3), `GoldenHarnessGuards` covers no-Unity-reference / count+uniqueness /
eval-tree-SHA-unchanged (criteria 1+4), `GoldenComparisonTests` self-tests the
canonicalizer and deep-compare. Verified by orchestrator: 30 failed / 19 passed,
every failure `NotImplementedException: T-0N`, counts match run/tickets.json exactly.

## Attempt log

