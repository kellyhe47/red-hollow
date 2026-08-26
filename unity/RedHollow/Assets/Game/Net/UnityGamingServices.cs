using System;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Relay;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 020 (T-20) — the real <see cref="IUgsServices"/>: Unity Services Core initialization,
    /// anonymous Authentication sign-in, Lobby 1.3 create/join-by-code/heartbeat/leave, and Relay
    /// 1.2 allocation + join, each call a straight wrap of the SDK.
    ///
    /// Untestable by construction, deliberately: no branching, no retries, no policy — every
    /// decision (call order, failure surfaces, teardown, heartbeat cadence, loopback never getting
    /// here at all) lives in <see cref="NgoNetTransport"/> where the fake can reach it. SDK
    /// exceptions are translated at this boundary into <see cref="UgsUnavailableException"/>
    /// naming their <see cref="UgsStep"/>; no <c>Unity.Services</c> type crosses the seam.
    /// Verified by the ticket's two-machine hand check (the owner's step), not by EditMode.
    /// </summary>
    public sealed class UnityGamingServices : IUgsServices
    {
        public bool IsSignedIn => throw new NotImplementedException("ticket 020");

        public void SignIn(string projectId) => throw new NotImplementedException("ticket 020");

        public RelayHostSlot AllocateRelay(int maxConnections) =>
            throw new NotImplementedException("ticket 020");

        public RelayJoinSlot JoinRelay(string relayJoinCode) =>
            throw new NotImplementedException("ticket 020");

        public LobbyTicket CreateLobby(int maxPlayers, string relayJoinCode) =>
            throw new NotImplementedException("ticket 020");

        public LobbyTicket JoinLobbyByCode(string joinCode) =>
            throw new NotImplementedException("ticket 020");

        public void HeartbeatLobby(string lobbyId) =>
            throw new NotImplementedException("ticket 020");

        public void LeaveLobby(string lobbyId) =>
            throw new NotImplementedException("ticket 020");
    }
}
