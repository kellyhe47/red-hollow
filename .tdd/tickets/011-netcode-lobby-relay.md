---
id: 011
title: Netcode: Lobby, Relay, loopback, disconnects, rematch
status: blocked
depends_on: [010]
touches: [unity/RedHollow/Assets/Game/Net/]
iterations: 0
test_files: []
branch: ""
board_id: T-11
owns_requirements: [R-07, R-53, R-55]
grades_fixtures: []
---

## Scope

Netcode for GameObjects host-authoritative; Unity Lobby join codes + Relay with the UGS project id injected via config; local-loopback multiplayer must work with no UGS id. Mid-match disconnect despawns the hero and retargets monsters; host disconnect ends the match; no mid-match joins. ESC is a non-pausing overlay. Rematch returns the party to the same lobby with join code and class picks retained, full match-state reset, profiles persisted.

## Acceptance criteria

- [ ] 2-player loopback session completes a 10-wave match
- [ ] victory, defeat and rematch paths all exercised
- [ ] no UGS id required for loopback

## Test plan

_Filled in by the test-writer._

## Attempt log

- BLOCKED (environment, pre-run): Unity Editor is not installed on this machine and needs the owner's Unity account/licence. `unity/RedHollow/` currently holds only `Assets/GameSim` — there is no Unity project (no ProjectSettings/, no Packages/). T-01..T-09 carry the entire 30-fixture acceptance contract and need no Unity.

## Handoff notes from the sim run (read before starting)

- All fixture-covered rules are in `GameSim`, host-only (R-51). Clients send commands, receive
  replicated state. `SimObservation` (`Result` / `StateChanges` / `EmittedEvents` / `ExternalCalls`)
  is the replication payload — one per command.
- **`LastObservation` is overwritten by every command.** Replicate it before issuing the next one.
- R-53 disconnect: `PlayerSlot.Connected` already gates the all-ready early start (ticket 004), so a
  disconnected player cannot hold planning hostage. Nothing else reads it yet.
- R-07 rematch: `MatchState` is a plain object — build a fresh one from
  `ColonyMap.V1().CreateMatchState(config)` to reset scrip, waves, placeables and civilians. Profiles
  persist separately through `IProfileStore`, so `SaveProfilesAtMatchEnd()` must run before the reset.
- UGS project id is still needed from the owner for Relay; local loopback needs none.
