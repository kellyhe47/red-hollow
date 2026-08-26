---
id: 020
title: Real networking — NGO transport, Unity Lobby and Relay
status: pending
depends_on: [011]
touches: [unity/RedHollow/Assets/Game/Net/]
iterations: 0
test_files: []
branch: ""
board_id: T-20
owns_requirements: []
grades_fixtures: []
---

## Scope

Ticket 011 is green but covers everything EXCEPT actual networking: `LoopbackNetTransport`
is in-process by design. Implement `INetTransport` over Netcode for GameObjects 2.13.2 plus
Unity Lobby (join codes) and Relay, behind the existing seam. Loopback must keep working
with no UGS id configured.

## Acceptance criteria

- [ ] `INetTransport` implemented over NGO 2.13.2 + Lobby + Relay behind the existing seam
- [ ] loopback transport still works with no UGS id configured
- [ ] a 2-player co-op session across two real machines completes a 10-wave match — victory,
      defeat and rematch exercised (hand-verified; DoD item 3's real-transport half)

## Test plan

_Filled in by the test-writer._

## Attempt log

_(created 2026-08-25 by the handoff-2 orchestrator; the two-machine hand-verification
needs the owner — flag before starting.)_

## Handoff notes

- The swap point is `INetTransport` (unity/RedHollow/Assets/Game/Net/INetTransport.cs);
  `NetSession` and everything above it must not change.
- UGS: cloud project `ac5dd937-4e73-44e8-8ac5-fb148787ce3b`, org `kellyqhe47`, linked in
  `ProjectSettings/ProjectSettings.asset`. Packages already installed: NGO 2.13.2,
  Transport 6.5.0, Lobby 1.3.0, Relay 1.2.0, Authentication 3.7.4.
- **Unity 6 pairs with NGO 2.x** — most tutorials target 1.x and have a different API surface.
- The T10 Cecil invariant scans all shell MonoBehaviours; NGO NetworkBehaviours count.
  Sim state is written host-side only, through the sim's command surface.
