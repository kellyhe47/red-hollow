using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 020 (T-20) — the seam onto the wire itself: Netcode for GameObjects'
    /// <c>NetworkManager</c> + UnityTransport, reduced to the four things the orchestration layer
    /// actually asks of it. Start hosting at a Relay endpoint, connect to one, tear down, and say
    /// when a peer's connection dropped.
    ///
    /// Peer identity crosses this seam as the session's own peer id (an opaque string), never as
    /// an NGO <c>ulong</c> client id: the mapping between the two is established by the connection
    /// payload and lives inside the adapter, because a transport that reasoned about client ids
    /// would be a transport no EditMode test can drive.
    /// </summary>
    public interface INetWire
    {
        /// <summary>Whether the wire is up (hosting or connected).</summary>
        bool IsUp { get; }

        /// <summary>Host — bring the wire up at the endpoint the Relay allocation answered.</summary>
        void StartHost(RelayEndpoint endpoint);

        /// <summary>Client — connect to the host through the endpoint the Relay join answered.</summary>
        void StartClient(RelayEndpoint endpoint);

        /// <summary>Tear the wire down. Idempotent, like everything else on the teardown path.</summary>
        void Shutdown();

        /// <summary>
        /// A peer's connection dropped, named by session peer id. The transport forwards this to
        /// whoever subscribed (the shell hands it to <see cref="NetSession.Disconnect"/> — R-53).
        /// </summary>
        event Action<string> PeerDisconnected;
    }

    /// <summary>
    /// Ticket 020 (T-20) — the real <see cref="INetWire"/>: a thin adapter over NGO 2.13's
    /// <c>NetworkManager</c> and its UnityTransport, configured with Relay connection data.
    ///
    /// Untestable by construction, and kept that way ON PURPOSE: no policy, no state beyond the
    /// clientId↔peerId map the connection payload establishes. Anything this class starts
    /// deciding belongs in <see cref="NgoNetTransport"/>, where a fake wire can reach it.
    /// Verified by the ticket's two-machine hand check (the owner's step), not by EditMode.
    ///
    /// Plain C# holding a <c>NetworkManager</c> reference rather than a NetworkBehaviour — T-10's
    /// Cecil invariant scans every shell MonoBehaviour, and nothing on the wire path may be one.
    /// </summary>
    public sealed class NgoWire : INetWire
    {
        private readonly NetworkManager _networkManager;

        /// <summary>
        /// clientId → session peer id, established by the connection payload (the shell's
        /// connection-approval hook calls <see cref="MapPeer"/>). Deliberately unasserted by any
        /// test: the mapping is adapter business, and the seam only ever speaks peer ids.
        /// </summary>
        private readonly Dictionary<ulong, string> _peerIdsByClientId =
            new Dictionary<ulong, string>();

        private bool _disconnectHooked;

        /// <summary>
        /// This machine's session peer id — what the connection payload carries (client) and what
        /// the host's own client id maps to (host). Set by the shell via
        /// <see cref="SetLocalPeerId"/> before starting either side.
        /// </summary>
        private string _localPeerId;

        public NgoWire(NetworkManager networkManager)
        {
            if (networkManager == null)
            {
                throw new ArgumentNullException(nameof(networkManager));
            }

            _networkManager = networkManager;
        }

        public bool IsUp => _networkManager.IsListening;

        public event Action<string> PeerDisconnected;

        public void StartHost(RelayEndpoint endpoint)
        {
            ApplyRelayData(endpoint, asHost: true);
            HookDisconnects();

            // The clientId↔peerId mapping starts at the door: every joiner's connection payload
            // carries its session peer id (see StartClient), and connection approval is where NGO
            // hands the host that payload next to the client id it minted.
            _networkManager.NetworkConfig.ConnectionApproval = true;
            if (_networkManager.ConnectionApprovalCallback == null)
            {
                // NGO allows exactly one approval callback; a shell that registered its own
                // (admission policy is the session's business anyway) keeps it, and then owns
                // calling MapPeer from it.
                _networkManager.ConnectionApprovalCallback = OnConnectionApproval;
            }

            _networkManager.StartHost();

            // The host is a client of itself; its own drop never fires through this map, but the
            // roster's language is peer ids, so the local id speaks it too.
            if (!string.IsNullOrEmpty(_localPeerId))
            {
                _peerIdsByClientId[_networkManager.LocalClientId] = _localPeerId;
            }
        }

        public void StartClient(RelayEndpoint endpoint)
        {
            ApplyRelayData(endpoint, asHost: false);
            HookDisconnects();

            // The payload is how this machine's session peer id reaches the host's approval
            // callback — the only moment the two identities are ever in the same hand.
            _networkManager.NetworkConfig.ConnectionData = string.IsNullOrEmpty(_localPeerId)
                ? new byte[0]
                : Encoding.UTF8.GetBytes(_localPeerId);

            _networkManager.StartClient();
        }

        public void Shutdown()
        {
            UnhookDisconnects();
            _peerIdsByClientId.Clear();

            // Release the approval callback only when it is ours (delegate value equality:
            // same target, same method) — a shell-owned policy callback stays registered.
            Action<NetworkManager.ConnectionApprovalRequest,
                NetworkManager.ConnectionApprovalResponse> mine = OnConnectionApproval;
            if (Equals(_networkManager.ConnectionApprovalCallback, mine))
            {
                _networkManager.ConnectionApprovalCallback = null;
            }

            if (_networkManager.IsListening)
            {
                _networkManager.Shutdown();
            }
        }

        /// <summary>
        /// Tell the wire who this machine is in the session's language. The shell calls it once,
        /// before <see cref="StartHost"/> / <see cref="StartClient"/>: the client sends it as the
        /// connection payload; the host maps its own client id with it.
        /// </summary>
        public void SetLocalPeerId(string peerId)
        {
            _localPeerId = peerId;
        }

        /// <summary>
        /// Register which session peer an NGO client id speaks for — called by the shell's
        /// connection-approval hook once it has read the peer id out of the connection payload.
        /// </summary>
        public void MapPeer(ulong clientId, string peerId)
        {
            if (string.IsNullOrEmpty(peerId))
            {
                return;
            }

            _peerIdsByClientId[clientId] = peerId;
        }

        private void HookDisconnects()
        {
            if (_disconnectHooked)
            {
                return;
            }

            _networkManager.OnClientDisconnectCallback += OnClientDisconnect;
            _disconnectHooked = true;
        }

        private void UnhookDisconnects()
        {
            if (!_disconnectHooked)
            {
                return;
            }

            _networkManager.OnClientDisconnectCallback -= OnClientDisconnect;
            _disconnectHooked = false;
        }

        /// <summary>
        /// The host's door: NGO hands over the joiner's connection payload (the session peer id
        /// the client's <see cref="StartClient"/> put there) next to the client id it minted —
        /// the one moment both identities are in the same hand, so the map is written here.
        ///
        /// Admission is NOT decided here: who may join is <see cref="NetSession.TryJoin"/>'s
        /// business (party cap, no mid-match joins), and it already refuses at the session layer.
        /// The wire approves and merely records who arrived.
        /// </summary>
        private void OnConnectionApproval(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            if (request.Payload != null && request.Payload.Length > 0)
            {
                MapPeer(request.ClientNetworkId, Encoding.UTF8.GetString(request.Payload));
            }

            response.Approved = true;

            // No NGO player object: sim state is host-side plain C# (T-10); nothing spawns.
            response.CreatePlayerObject = false;
        }

        /// <summary>NGO's disconnect, translated to the seam's language: a session peer id.</summary>
        private void OnClientDisconnect(ulong clientId)
        {
            string peerId;
            if (!_peerIdsByClientId.TryGetValue(clientId, out peerId))
            {
                // A connection that never completed its payload handshake has no session
                // identity to report; the session never seated it either.
                return;
            }

            _peerIdsByClientId.Remove(clientId);

            var handler = PeerDisconnected;
            if (handler != null)
            {
                handler(peerId);
            }
        }

        /// <summary>
        /// Hand UnityTransport the Relay connection data the allocation answered. The endpoint is
        /// opaque above this seam; only here may it be opened back up into its fields.
        /// </summary>
        private void ApplyRelayData(RelayEndpoint endpoint, bool asHost)
        {
            var relay = endpoint as UgsRelayEndpoint;
            if (relay == null)
            {
                throw new ArgumentException(
                    "NgoWire needs the UgsRelayEndpoint the real services adapter mints; got "
                    + (endpoint == null ? "null" : endpoint.GetType().Name),
                    nameof(endpoint));
            }

            var transport = _networkManager.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport == null)
            {
                throw new InvalidOperationException(
                    "the NetworkManager's transport must be UnityTransport to carry Relay data");
            }

            if (asHost)
            {
                transport.SetHostRelayData(
                    relay.Host, relay.Port, relay.AllocationIdBytes, relay.Key,
                    relay.ConnectionData, relay.IsSecure);
            }
            else
            {
                transport.SetClientRelayData(
                    relay.Host, relay.Port, relay.AllocationIdBytes, relay.Key,
                    relay.ConnectionData, relay.HostConnectionData, relay.IsSecure);
            }
        }
    }
}
