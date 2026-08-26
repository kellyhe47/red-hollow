using System;
using Unity.Netcode;

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
    /// <c>NetworkManager</c> and its UnityTransport, configured with Relay server data.
    ///
    /// Untestable by construction, and kept that way ON PURPOSE: no branching, no policy, no state
    /// beyond what NGO itself holds. Anything this class starts deciding belongs in
    /// <see cref="NgoNetTransport"/>, where a fake wire can reach it. Verified by the ticket's
    /// two-machine hand check (the owner's step), not by EditMode.
    ///
    /// Plain C# holding a <c>NetworkManager</c> reference rather than a NetworkBehaviour — T-10's
    /// Cecil invariant scans every shell MonoBehaviour, and nothing on the wire path may be one.
    /// </summary>
    public sealed class NgoWire : INetWire
    {
        private readonly NetworkManager _networkManager;

        public NgoWire(NetworkManager networkManager)
        {
            if (networkManager == null)
            {
                throw new ArgumentNullException(nameof(networkManager));
            }

            _networkManager = networkManager;
        }

        public bool IsUp => throw new NotImplementedException("ticket 020");

        public void StartHost(RelayEndpoint endpoint) =>
            throw new NotImplementedException("ticket 020");

        public void StartClient(RelayEndpoint endpoint) =>
            throw new NotImplementedException("ticket 020");

        public void Shutdown() => throw new NotImplementedException("ticket 020");

        public event Action<string> PeerDisconnected
        {
            add { throw new NotImplementedException("ticket 020"); }
            remove { throw new NotImplementedException("ticket 020"); }
        }
    }
}
