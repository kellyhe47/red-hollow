using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;

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
        /// <summary>
        /// R-07 — how many loopback join codes this process has minted. Static and interlocked so
        /// two sessions in one editor run (a rematch harness, two tests in a row) never read the
        /// same code off the screen; per-process rather than random so a code is reproducible in a
        /// log. No format is contract — the PRD names none, and nothing may parse this.
        /// </summary>
        private static int _codesMinted;

        private readonly List<NetPeer> _peers = new List<NetPeer>();

        /// <summary>
        /// Wrapped once rather than per read: <see cref="ConnectedPeers"/> is polled from the
        /// session's own hot paths, and a fresh wrapper per call would allocate for an answer that
        /// has not changed.
        /// </summary>
        private readonly ReadOnlyCollection<NetPeer> _peersView;

        private bool _running;
        private string _projectId;
        private string _joinCode;

        public LoopbackNetTransport()
        {
            _peersView = new ReadOnlyCollection<NetPeer>(_peers);
        }

        public bool IsRunning => _running;

        /// <summary>
        /// R-50 — always false, and this constant is the acceptance criterion "no UGS id is required
        /// for loopback" stated as a property. Nothing in this file authenticates, allocates a Relay
        /// or reaches a network, so nothing here can need a cloud project.
        /// </summary>
        public bool RequiresUnityServices => false;

        public string ProjectId => _projectId;

        public string JoinCode => _joinCode;

        public IReadOnlyList<NetPeer> ConnectedPeers => _peersView;

        /// <summary>
        /// R-50 — come up as host.
        ///
        /// The project id is <b>carried, never invented</b>: a loopback session started with no id
        /// keeps none, so "offline" and "misconfigured Relay" stay distinguishable. The join code is
        /// minted once and then held for the life of the transport, because R-07's rematch returns
        /// the party to the <i>same</i> lobby and a code that changed underneath them would send a
        /// party member reading it off the screen to a lobby that no longer exists.
        /// </summary>
        public void StartHost(NetSessionConfig config)
        {
            _running = true;
            _projectId = config == null ? null : config.UgsProjectId;

            if (string.IsNullOrEmpty(_joinCode))
            {
                _joinCode = MintJoinCode();
            }
        }

        /// <summary>
        /// Admit a peer the session has already decided to accept (R-50, R-53). Who is allowed in is
        /// the session's call — this only records the connection, and records it once: a peer id
        /// admitted twice is one connection, not two seats.
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
        /// R-53 — drop a peer. Whether the match survives is the session's call, not this one's, so
        /// nothing here looks at the match: an unknown peer is simply a disconnect that raced its
        /// own timeout and is ignored.
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
        /// Tear the transport down. Idempotent — R-53 ends a session from a callback that may well
        /// fire twice, and a teardown that threw the second time would take the shell down with it.
        /// The join code goes with it: the lobby it named is gone.
        /// </summary>
        public void Shutdown()
        {
            _running = false;
            _peers.Clear();
            _joinCode = null;
            _projectId = null;
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

        /// <summary>
        /// R-07 / R-50 — a stand-in for the code Unity Lobby mints, so the lobby screen and the
        /// rematch path have one shape offline and online. Readable on purpose (it is shown to a
        /// human) and contract-free: no format is specified anywhere, so nothing may parse it.
        /// </summary>
        private static string MintJoinCode()
        {
            var minted = Interlocked.Increment(ref _codesMinted);
            return "LOCAL-" + minted.ToString("D4", CultureInfo.InvariantCulture);
        }
    }
}
