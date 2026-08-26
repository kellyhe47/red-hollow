using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 020 (T-20) — the wire-level Relay connection data, opened up only inside the real
    /// adapters: minted here from an allocation, consumed by <see cref="NgoWire"/>, and carried as
    /// an opaque <see cref="RelayEndpoint"/> everywhere in between. No <c>Unity.Services</c> or
    /// Transport type appears on it, so it can cross the seam without dragging an SDK with it.
    /// </summary>
    public sealed class UgsRelayEndpoint : RelayEndpoint
    {
        public string Host;
        public ushort Port;
        public byte[] AllocationIdBytes;
        public byte[] Key;
        public byte[] ConnectionData;

        /// <summary>Client side only: the host's connection data blob, from the join allocation.</summary>
        public byte[] HostConnectionData;

        public bool IsSecure;
    }

    /// <summary>
    /// Ticket 020 (T-20) — the real <see cref="IUgsServices"/>: Unity Services Core initialization,
    /// anonymous Authentication sign-in, Lobby 1.3 create/join-by-code/heartbeat/leave, and Relay
    /// 1.2 allocation + join, each call a straight wrap of the SDK.
    ///
    /// Untestable by construction, deliberately: no retries, no policy — every decision (call
    /// order, failure surfaces, teardown, heartbeat cadence, loopback never getting here at all)
    /// lives in <see cref="NgoNetTransport"/> where the fake can reach it. SDK exceptions are
    /// translated at this boundary into <see cref="UgsUnavailableException"/> naming their
    /// <see cref="UgsStep"/>; no <c>Unity.Services</c> type crosses the seam. Verified by the
    /// ticket's two-machine hand check (the owner's step), not by EditMode.
    ///
    /// The seam is synchronous-looking on purpose (it is what makes the orchestration drivable in
    /// EditMode), so each wrap blocks on the SDK's task. That is acceptable for the shell's
    /// explicit "HOST A PARTY" / "JOIN" button presses on desktop; it would deadlock on WebGL,
    /// which v1 does not target.
    /// </summary>
    public sealed class UnityGamingServices : IUgsServices
    {
        /// <summary>The lobby-data key the relay join code rides under. Both sides of this class use it; nothing else may.</summary>
        private const string RelayJoinCodeKey = "relayJoinCode";

        /// <summary>Lobby names are not contract anywhere; the service demands one.</summary>
        private const string LobbyName = "red-hollow";

        public bool IsSignedIn =>
            UnityServices.State == ServicesInitializationState.Initialized
            && AuthenticationService.Instance.IsSignedIn;

        public void SignIn(string projectId)
        {
            // The project id is baked into the build by ProjectSettings (the linked cloud
            // project); Authentication takes none. It arrives here anyway so the seam carries
            // what the config named (R-50) — the fake asserts on it, the SDK does not need it.
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    Wait(UnityServices.InitializeAsync());
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    Wait(AuthenticationService.Instance.SignInAnonymouslyAsync());
                }
            }
            catch (Exception failure)
            {
                throw Wrap(UgsStep.SignIn, failure);
            }
        }

        public RelayHostSlot AllocateRelay(int maxConnections)
        {
            try
            {
                var allocation = Wait(RelayService.Instance.CreateAllocationAsync(maxConnections));
                var relayJoinCode =
                    Wait(RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId));
                var endpoint = PickEndpoint(allocation.ServerEndpoints);

                return new RelayHostSlot
                {
                    RelayJoinCode = relayJoinCode,
                    Endpoint = new UgsRelayEndpoint
                    {
                        Host = endpoint.Host,
                        Port = (ushort)endpoint.Port,
                        AllocationIdBytes = allocation.AllocationIdBytes,
                        Key = allocation.Key,
                        ConnectionData = allocation.ConnectionData,
                        IsSecure = endpoint.Secure,
                    },
                };
            }
            catch (Exception failure)
            {
                throw Wrap(UgsStep.AllocateRelay, failure);
            }
        }

        public RelayJoinSlot JoinRelay(string relayJoinCode)
        {
            try
            {
                var join = Wait(RelayService.Instance.JoinAllocationAsync(relayJoinCode));
                var endpoint = PickEndpoint(join.ServerEndpoints);

                return new RelayJoinSlot
                {
                    Endpoint = new UgsRelayEndpoint
                    {
                        Host = endpoint.Host,
                        Port = (ushort)endpoint.Port,
                        AllocationIdBytes = join.AllocationIdBytes,
                        Key = join.Key,
                        ConnectionData = join.ConnectionData,
                        HostConnectionData = join.HostConnectionData,
                        IsSecure = endpoint.Secure,
                    },
                };
            }
            catch (Exception failure)
            {
                throw Wrap(UgsStep.JoinRelay, failure);
            }
        }

        public LobbyTicket CreateLobby(int maxPlayers, string relayJoinCode)
        {
            try
            {
                var options = new CreateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject>
                    {
                        {
                            RelayJoinCodeKey,
                            new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode)
                        },
                    },
                };

                var lobby = Wait(
                    LobbyService.Instance.CreateLobbyAsync(LobbyName, maxPlayers, options));

                return new LobbyTicket
                {
                    LobbyId = lobby.Id,
                    JoinCode = lobby.LobbyCode,
                    RelayJoinCode = relayJoinCode,
                };
            }
            catch (Exception failure)
            {
                throw Wrap(UgsStep.CreateLobby, failure);
            }
        }

        public LobbyTicket JoinLobbyByCode(string joinCode)
        {
            try
            {
                var lobby = Wait(LobbyService.Instance.JoinLobbyByCodeAsync(joinCode));

                DataObject relayCode = null;
                if (lobby.Data != null)
                {
                    lobby.Data.TryGetValue(RelayJoinCodeKey, out relayCode);
                }

                return new LobbyTicket
                {
                    LobbyId = lobby.Id,
                    JoinCode = lobby.LobbyCode,
                    RelayJoinCode = relayCode == null ? null : relayCode.Value,
                };
            }
            catch (Exception failure)
            {
                throw Wrap(UgsStep.JoinLobby, failure);
            }
        }

        public void HeartbeatLobby(string lobbyId)
        {
            try
            {
                Wait(LobbyService.Instance.SendHeartbeatPingAsync(lobbyId));
            }
            catch (Exception failure)
            {
                throw Wrap(UgsStep.Heartbeat, failure);
            }
        }

        public void LeaveLobby(string lobbyId)
        {
            try
            {
                // The seam's caller is the hosting transport, and the host releasing the lobby
                // deletes it — a lobby whose host is gone must stop resolving its code (R-53).
                Wait(LobbyService.Instance.DeleteLobbyAsync(lobbyId));
            }
            catch (Exception failure)
            {
                throw Wrap(UgsStep.LeaveLobby, failure);
            }
        }

        /// <summary>
        /// The endpoint UnityTransport should carry: dtls when the allocation offers it (it
        /// always does today), otherwise whatever came first.
        /// </summary>
        private static RelayServerEndpoint PickEndpoint(List<RelayServerEndpoint> endpoints)
        {
            if (endpoints == null || endpoints.Count == 0)
            {
                throw new InvalidOperationException("the Relay allocation answered no endpoints");
            }

            for (var i = 0; i < endpoints.Count; i++)
            {
                if (string.Equals(
                        endpoints[i].ConnectionType,
                        RelayServerEndpoint.ConnectionTypeDtls,
                        StringComparison.Ordinal))
                {
                    return endpoints[i];
                }
            }

            return endpoints[0];
        }

        private static void Wait(Task task) => task.GetAwaiter().GetResult();

        private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

        private static UgsUnavailableException Wrap(UgsStep step, Exception failure)
        {
            var inner = failure as UgsUnavailableException;
            if (inner != null)
            {
                return inner;
            }

            return new UgsUnavailableException(step, step + " failed: " + failure.Message);
        }
    }
}
