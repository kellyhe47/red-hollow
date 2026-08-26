using System;
using System.Collections.Generic;

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
        private readonly IUgsServices _services;
        private readonly INetWire _wire;

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
        }

        public bool IsRunning => throw new NotImplementedException("ticket 020");

        /// <summary>R-50 — true: Lobby and Relay authenticate against a UGS cloud project.</summary>
        public bool RequiresUnityServices => throw new NotImplementedException("ticket 020");

        public string ProjectId => throw new NotImplementedException("ticket 020");

        /// <summary>R-07 — the LOBBY join code (the one players share), never the relay code.</summary>
        public string JoinCode => throw new NotImplementedException("ticket 020");

        public IReadOnlyList<NetPeer> ConnectedPeers =>
            throw new NotImplementedException("ticket 020");

        public void StartHost(NetSessionConfig config) =>
            throw new NotImplementedException("ticket 020");

        /// <summary>
        /// Client — join a hosted party by the code a player typed (R-50). False on a bad or
        /// expired code (<see cref="UgsStep.JoinLobby"/>), leaving everything untouched so a
        /// corrected code can retry. Deliberately NOT on <see cref="INetTransport"/>: the session
        /// is host-side and must not grow a member only a joining machine can answer.
        /// </summary>
        public bool TryJoinAsClient(NetSessionConfig config, string joinCode) =>
            throw new NotImplementedException("ticket 020");

        public void ConnectPeer(NetPeer peer) =>
            throw new NotImplementedException("ticket 020");

        public void DisconnectPeer(string peerId) =>
            throw new NotImplementedException("ticket 020");

        /// <summary>
        /// Drive time-based upkeep — the lobby heartbeat — from whatever loop the shell already
        /// pumps. Beats only while hosting; never after <see cref="Shutdown"/>.
        /// </summary>
        public void Tick(double deltaSeconds) =>
            throw new NotImplementedException("ticket 020");

        public void Shutdown() => throw new NotImplementedException("ticket 020");

        /// <summary>
        /// A peer's wire connection dropped (R-53), by session peer id. The shell forwards this to
        /// <see cref="NetSession.Disconnect"/> — the transport reports, the session decides.
        /// </summary>
        public event Action<string> PeerDisconnected
        {
            add { throw new NotImplementedException("ticket 020"); }
            remove { throw new NotImplementedException("ticket 020"); }
        }
    }
}
