---
id: 020
title: Real networking — NGO transport, Unity Lobby and Relay
status: pending
depends_on: [011]
touches: [unity/RedHollow/Assets/Game/Net/]
iterations: 0
test_files: [unity/RedHollow/Assets/Tests/EditMode/T20_NgoTransportTests.cs]
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

**Reality constraint the design answers:** EditMode cannot run live NGO connections, UGS
sign-in or Relay allocations. So every DECISION is pulled into an EditMode-testable
orchestration layer driven through two seams, and the real SDK wraps stay thin, declarative
and branch-free (untestable by construction; the two-machine hand check is the owner's step
and stays an unchecked criterion).

**Contract (stubs, all throwing `NotImplementedException`):**

- `Game/Net/UgsServices.cs` — `IUgsServices` seam (SignIn / AllocateRelay / JoinRelay /
  CreateLobby / JoinLobbyByCode / HeartbeatLobby / LeaveLobby) + DTOs (`RelayEndpoint` —
  opaque, carried by identity; `RelayHostSlot`, `RelayJoinSlot`, `LobbyTicket` with distinct
  lobby vs relay join codes) + `UgsUnavailableException(UgsStep)` — SDK exceptions never
  cross the seam.
- `Game/Net/NgoWire.cs` — `INetWire` seam (StartHost/StartClient at a `RelayEndpoint`,
  Shutdown, `PeerDisconnected` event carrying SESSION peer ids, never NGO client ids) +
  `NgoWire` real-adapter skeleton over `NetworkManager` (plain C#, not a NetworkBehaviour —
  T10's Cecil scan).
- `Game/Net/NgoNetTransport.cs` — `NgoNetTransport : INetTransport`, all orchestration:
  host bring-up order, `TryJoinAsClient(config, code)` (client path; deliberately NOT on
  `INetTransport`), `Tick` (lobby heartbeat), `PeerDisconnected` event, teardown.
- `Game/Net/NetTransportFactory.cs` — the config gate: no UGS id → loopback, id → NGO.
- `Game/Net/UnityGamingServices.cs` — real `IUgsServices` adapter skeleton
  (Unity.Services.* usings compile; asmdef now references Unity.Netcode.Runtime +
  Unity.Services.{Core,Authentication,Lobbies,Relay}).

**T20_NgoTransportTests.cs — 10 cases:**

1. `No_ugs_id_selects_loopback_and_the_services_seam_is_never_touched` — factory returns
   loopback for id-less/null config; a whole hosted loopback match runs with the fake
   recording ZERO service calls and the wire untouched (the acceptance criterion).
2. `A_ugs_id_selects_the_ngo_transport_and_construction_touches_nothing` — id → NGO
   transport, `RequiresUnityServices` true, construction passive.
3. `A_host_start_signs_in_allocates_relay_creates_the_lobby_and_raises_the_wire` — exact
   order SignIn→AllocateRelay→CreateLobby; project id carried; allocation ≥ MaxPlayers-1;
   lobby carries the relay code; wire raised at the allocation's endpoint (same object);
   surfaced JoinCode is the LOBBY code, not the relay code.
4. `The_lobby_is_heartbeated_while_hosting_and_released_exactly_once_at_shutdown` — beats
   while ticked (≥1 per 30s window, cadence not pinned), all naming the held lobby; shutdown
   → LeaveLobby once + wire down + code cleared; no beats after; idempotent shutdown.
5. `A_client_join_signs_in_finds_the_lobby_joins_relay_and_connects_the_wire` — order
   SignIn→JoinLobby→JoinRelay; relay join uses the code the LOBBY carried; wire connected at
   the join's endpoint (same object).
6. `A_bad_join_code_is_a_refusal_that_leaves_everything_retryable` — bad code → false (not
   a throw), no relay join, no wire, transport down; feeds T12's S1 inline-error surface
   (`TitleScreenModel.NoteJoinFailed`); corrected code retries on the same instance.
7. `An_auth_or_relay_failure_during_host_start_leaves_nothing_half_started` — SignIn /
   AllocateRelay scripted failures propagate as `UgsUnavailableException` naming the step;
   nothing downstream ran (no lobby, no wire, no heartbeats); recovery → StartHost works.
8. `A_failed_host_start_leaves_the_session_offline_and_retryable` — same through
   `NetSession`: phase stays Offline, no seats; recovered service → same session hosts.
9. `The_t11_lifecycle_runs_identically_over_the_ngo_transport` — T11's drive over
   NgoNetTransport+fakes with `NetSession` unchanged: 2 players, 10 waves, victory,
   post-match, rematch to the SAME lobby (CreateLobby exactly once, join code unchanged,
   no LeaveLobby), second match from wave 1.
10. `A_wire_reported_guest_drop_reaches_the_session_as_r53` +
    `A_host_drop_ends_the_session_and_releases_the_lobby_without_a_defeat` — wire fires
    `PeerDisconnected(peerId)`, forwarded one-line into `NetSession.Disconnect`: guest →
    despawn/slot-disconnected/seat-freed/toast/match continues; host → Ended, status stays
    InProgress (DEC-RUN-10, no invented defeat), lobby released, wire down, beats stop.

**Deliberately not asserted:** join-code formats, heartbeat cadence, lobby names, relay
regions, clientId↔peerId mapping (adapter/connection-payload business), reconnection, host
migration. Cecil shape: all new orchestration types are plain C#; no NetworkBehaviour is
demanded anywhere.

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
