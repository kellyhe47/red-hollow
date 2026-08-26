using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RedHollow.Game.Host;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 020 (T-20) — the real-networking <see cref="INetTransport"/> (R-50): Unity Lobby for
    /// the join code, Relay for reachability, Netcode for GameObjects for the wire — all of it
    /// reached only through the two seams it is built on, <see cref="IUgsServices"/> and
    /// <see cref="INetWire"/>.
    ///
    /// <b>This class is the half that is testable, and it holds ALL the decisions.</b>
    ///
    ///  * Host bring-up: sign in → allocate Relay → create the lobby carrying the relay join code
    ///    → bring the wire up at the allocation's endpoint. <see cref="JoinCode"/> is the LOBBY's
    ///    code — the one players share (R-07) — never the relay code, which is plumbing.
    ///  * Client bring-up (<see cref="TryJoinAsClient"/>): sign in → lobby by the typed code →
    ///    Relay join by the code the lobby carried → connect the wire. A bad or expired code is a
    ///    refusal (<c>false</c>), not a throw: it is an ordinary lobby outcome, surfaced the same
    ///    way S1's inline error already is (T-12), and it must leave the transport clean enough to
    ///    retry with a corrected code.
    ///  * A host-side auth/Relay/Lobby failure propagates as <see cref="UgsUnavailableException"/>
    ///    and leaves nothing half-started: no lobby without a wire, no heartbeat without a lobby.
    ///    That is structural below — every service answer lands in a local first, and this
    ///    object's own state is written only after the last call has succeeded.
    ///  * The lobby is heartbeated while hosting (<see cref="Tick"/>) and released at
    ///    <see cref="Shutdown"/>, after which nothing beats.
    ///  * Wire-reported drops surface as <see cref="PeerDisconnected"/>, carrying the session's
    ///    peer id; whether the match survives stays <see cref="NetSession"/>'s call (R-53).
    ///
    /// Plain C#, no MonoBehaviour and no NGO type anywhere in it — T-10's Cecil invariant scans
    /// every shell MonoBehaviour, and the whole point of the two seams is that this class never
    /// needs to be one. <see cref="NetSession"/> sits above, unchanged: the T-11 lifecycle must
    /// behave identically over this transport and over loopback.
    /// </summary>
    public sealed class NgoNetTransport : INetTransport
    {
        /// <summary>
        /// How much ticked time may pass between heartbeats. UGS idles a lobby out after ~30
        /// unbeaten seconds; a third of that keeps the lobby alive with room for a missed frame.
        /// The exact number is deliberately not contract — the tests pin only "at least one beat
        /// per 30 ticked seconds, none after teardown".
        /// </summary>
        private const double HeartbeatIntervalSeconds = 10.0;

        private readonly IUgsServices _services;
        private readonly INetWire _wire;

        private readonly List<NetPeer> _peers = new List<NetPeer>();

        /// <summary>Wrapped once rather than per read, exactly as loopback does.</summary>
        private readonly ReadOnlyCollection<NetPeer> _peersView;

        private bool _running;
        private string _projectId;
        private string _joinCode;

        /// <summary>The lobby this transport is hosting, or null. What heartbeats and teardown name.</summary>
        private string _hostedLobbyId;

        private double _secondsSinceHeartbeat;

        public NgoNetTransport(IUgsServices services, INetWire wire)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (wire == null)
            {
                throw new ArgumentNullException(nameof(wire));
            }

            _services = services;
            _wire = wire;
            _peersView = new ReadOnlyCollection<NetPeer>(_peers);

            // Passive: subscribing to the wire's event touches no service and opens no socket.
            _wire.PeerDisconnected += OnWirePeerDisconnected;
        }

        public bool IsRunning => _running;

        /// <summary>R-50 — true: Lobby and Relay authenticate against a UGS cloud project.</summary>
        public bool RequiresUnityServices => true;

        public string ProjectId => _projectId;

        /// <summary>R-07 — the LOBBY join code (the one players share), never the relay code.</summary>
        public string JoinCode => _joinCode;

        public IReadOnlyList<NetPeer> ConnectedPeers => _peersView;

        /// <summary>
        /// A peer's wire connection dropped (R-53), by session peer id. The shell forwards this to
        /// <see cref="NetSession.Disconnect"/> — the transport reports, the session decides.
        /// </summary>
        public event Action<string> PeerDisconnected;

        /// <summary>
        /// R-50 — come up as host: sign in (once per process — UGS auth is per player, not per
        /// lobby), allocate Relay for every non-host seat, create the lobby carrying the relay
        /// join code, and raise the wire at the very endpoint the allocation answered.
        ///
        /// Ordering is forced, not chosen: auth gates both services, and the lobby stores the
        /// relay code so it cannot exist before the allocation minted one. Failures propagate as
        /// <see cref="UgsUnavailableException"/> with NOTHING half-started — this object's own
        /// fields are assigned only after every call has succeeded.
        /// </summary>
        public void StartHost(NetSessionConfig config)
        {
            if (_running)
            {
                // A second start while up would mint a second lobby under the party's feet;
                // the one they hold already answers (R-07: same lobby, same code).
                return;
            }

            var projectId = config == null ? null : config.UgsProjectId;

            if (!_services.IsSignedIn)
            {
                _services.SignIn(projectId);
            }

            var allocation = _services.AllocateRelay(PartyRoster.MaxPlayers - 1);
            var lobby = _services.CreateLobby(PartyRoster.MaxPlayers, allocation.RelayJoinCode);

            _wire.StartHost(allocation.Endpoint);

            // ---- commit: everything answered, so the transport may now say it is up ----------
            _hostedLobbyId = lobby.LobbyId;
            _joinCode = lobby.JoinCode;
            _projectId = projectId;
            _secondsSinceHeartbeat = 0.0;
            _running = true;
        }

        /// <summary>
        /// Client — join a hosted party by the code a player typed (R-50). False on a bad or
        /// expired code (<see cref="UgsStep.JoinLobby"/>), leaving everything untouched so a
        /// corrected code can retry. Deliberately NOT on <see cref="INetTransport"/>: the session
        /// is host-side and must not grow a member only a joining machine can answer.
        /// </summary>
        public bool TryJoinAsClient(NetSessionConfig config, string joinCode)
        {
            var projectId = config == null ? null : config.UgsProjectId;

            if (!_services.IsSignedIn)
            {
                _services.SignIn(projectId);
            }

            LobbyTicket lobby;
            try
            {
                lobby = _services.JoinLobbyByCode(joinCode);
            }
            catch (UgsUnavailableException failure) when (failure.Step == UgsStep.JoinLobby)
            {
                // T-12 / R-53 — a bad or expired code is an ordinary lobby outcome: refuse,
                // touch nothing, and leave this same instance ready for the corrected code.
                return false;
            }

            var slot = _services.JoinRelay(lobby.RelayJoinCode);

            _wire.StartClient(slot.Endpoint);

            _projectId = projectId;
            _joinCode = lobby.JoinCode;
            _running = true;
            return true;
        }

        /// <summary>
        /// Admit a peer the session has already decided to accept (R-50, R-53). Recorded once:
        /// a peer id admitted twice is one connection, not two seats — loopback's rule verbatim.
        /// </summary>
        public void ConnectPeer(NetPeer peer)
        {
            if (peer == null || string.IsNullOrEmpty(peer.PeerId))
            {
                return;
            }

            if (IndexOf(peer.PeerId) >= 0)
            {
                return;
            }

            _peers.Add(peer);
        }

        /// <summary>
        /// R-53 — drop a peer. Whether the match survives is the session's call, not this one's;
        /// an unknown peer is a disconnect that raced its own timeout, and is ignored.
        /// </summary>
        public void DisconnectPeer(string peerId)
        {
            var index = IndexOf(peerId);
            if (index < 0)
            {
                return;
            }

            _peers.RemoveAt(index);
        }

        /// <summary>
        /// Drive time-based upkeep — the lobby heartbeat — from whatever loop the shell already
        /// pumps. Beats only while hosting; never after <see cref="Shutdown"/>.
        /// </summary>
        public void Tick(double deltaSeconds)
        {
            if (!_running || string.IsNullOrEmpty(_hostedLobbyId))
            {
                return;
            }

            _secondsSinceHeartbeat += deltaSeconds;
            if (_secondsSinceHeartbeat >= HeartbeatIntervalSeconds)
            {
                _secondsSinceHeartbeat = 0.0;
                _services.HeartbeatLobby(_hostedLobbyId);
            }
        }

        /// <summary>
        /// Tear everything down: release the hosted lobby (exactly once — a code that keeps
        /// resolving to a dead party is a joiner staring at a spinner), drop the wire, clear the
        /// join code. Idempotent, like loopback's — R-53 tears down from callbacks that can fire
        /// twice, and the lobby id is nulled BEFORE the release call so a re-entrant shutdown
        /// cannot leave it a second time.
        /// </summary>
        public void Shutdown()
        {
            var lobbyToRelease = _hostedLobbyId;
            _hostedLobbyId = null;

            _running = false;
            _joinCode = null;
            _projectId = null;
            _secondsSinceHeartbeat = 0.0;
            _peers.Clear();

            _wire.Shutdown();

            if (!string.IsNullOrEmpty(lobbyToRelease))
            {
                _services.LeaveLobby(lobbyToRelease);
            }
        }

        /// <summary>
        /// The wire says a peer's connection dropped: forward the session peer id, verbatim.
        /// Nothing is decided here — R-53's rules (despawn, seat, toast, host-ends-it) all live
        /// in <see cref="NetSession.Disconnect"/>, where loopback's tests already pinned them.
        /// </summary>
        private void OnWirePeerDisconnected(string peerId)
        {
            var handler = PeerDisconnected;
            if (handler != null)
            {
                handler(peerId);
            }
        }

        private int IndexOf(string peerId)
        {
            if (string.IsNullOrEmpty(peerId))
            {
                return -1;
            }

            for (var i = 0; i < _peers.Count; i++)
            {
                if (string.Equals(_peers[i].PeerId, peerId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
