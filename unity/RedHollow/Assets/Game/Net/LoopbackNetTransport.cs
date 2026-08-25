using System;
using System.Collections.Generic;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 011 (T-11) — the in-memory <see cref="INetTransport"/>: a whole party on one process,
    /// no sockets, no Unity Gaming Services (R-50).
    ///
    /// It is not a test double. R-50 makes solo "a 1-player lobby" rather than a separate mode, so
    /// the offline path is a shipped path, and it must work on a machine with no cloud project
    /// linked — which is what <see cref="RequiresUnityServices"/> answers false to.
    ///
    /// Plain C#, like every other seam in this shell: T-10's IL invariant rejects a MonoBehaviour
    /// that touches sim state, and a transport is exactly where somebody would otherwise reach for a
    /// <c>NetworkBehaviour</c>.
    /// </summary>
    public sealed class LoopbackNetTransport : INetTransport
    {
        public bool IsRunning => throw new NotImplementedException("T-11: loopback transport lifecycle");

        public bool RequiresUnityServices =>
            throw new NotImplementedException("T-11 / R-50: loopback needs no UGS project id");

        public string ProjectId => throw new NotImplementedException("T-11 / R-50: carried project id");

        public string JoinCode => throw new NotImplementedException("T-11 / R-07: the party's join code");

        public IReadOnlyList<NetPeer> ConnectedPeers =>
            throw new NotImplementedException("T-11: connected peers");

        public void StartHost(NetSessionConfig config) =>
            throw new NotImplementedException("T-11 / R-50: start a host session");

        public void ConnectPeer(NetPeer peer) =>
            throw new NotImplementedException("T-11 / R-50: admit a peer");

        public void DisconnectPeer(string peerId) =>
            throw new NotImplementedException("T-11 / R-53: drop a peer");

        public void Shutdown() => throw new NotImplementedException("T-11: tear the transport down");
    }
}
