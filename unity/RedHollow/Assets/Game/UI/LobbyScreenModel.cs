using System;
using System.Collections.Generic;
using RedHollow.Game.Net;

namespace RedHollow.Game.UI
{
    /// <summary>One row of S2's player list: name · class picked · ready ✓/✗.</summary>
    public sealed class LobbySeat
    {
        public string PeerId;

        /// <summary>The callsign shown as the player's name (R-44).</summary>
        public string AccountId;

        /// <summary>The class pick, or null while unpicked. Duplicates are ALLOWED (R-31).</summary>
        public string HeroClass;

        public bool Ready;
    }

    /// <summary>
    /// Ticket 012 (T-12) — S2 Lobby (R-60): join code to share, three class cards, the player
    /// list, and READY.
    ///
    /// Lobby readiness lives here and not in the sim: <see cref="RedHollow.Sim.MatchState"/> does
    /// not exist before the match starts, and <see cref="NetPeer"/> deliberately outlives one
    /// (R-07). The wireframe's start rule is the contract: the match starts when ALL connected
    /// players are ready — a solo lobby needs only your own ready, and there is no host
    /// force-start. The start itself still goes through <see cref="NetSession.TryStartMatch"/>,
    /// issued by the host's model once everyone is ready.
    /// </summary>
    public sealed class LobbyScreenModel
    {
        private readonly NetSession _session;

        private readonly string _localPeerId;

        /// <summary>Lobby ready flags per peer — pre-match state, so it has no home in the sim.</summary>
        private readonly Dictionary<string, bool> _ready = new Dictionary<string, bool>();

        private readonly List<LobbySeat> _seats = new List<LobbySeat>();

        public LobbyScreenModel(NetSession session, string localPeerId)
        {
            _session = session;
            _localPeerId = localPeerId;
        }

        /// <summary>The code to share, read off the session (click-to-copy is presentation).</summary>
        public string JoinCode => _session.JoinCode;

        /// <summary>The player list, in join order, mirroring <see cref="NetSession.Seats"/>.</summary>
        public IReadOnlyList<LobbySeat> Seats => _seats;

        /// <summary>Wireframe state: waiting alone → hint text "share code".</summary>
        public bool WaitingAlone => _session.Seats.Count == 1;

        public int ReadyCount
        {
            get
            {
                var count = 0;
                foreach (var peer in _session.Seats)
                {
                    if (IsReady(peer.PeerId))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>The denominator: connected players, not party capacity.</summary>
        public int ConnectedCount => _session.Seats.Count;

        public bool AllReady => ConnectedCount > 0 && ReadyCount == ConnectedCount;

        /// <summary>Always true — duplicate classes are allowed, so no pick is ever blocked.</summary>
        public bool CanPick(string heroClass) => true;

        /// <summary>The local player picks (or re-picks) a class.</summary>
        public void PickClass(string heroClass)
        {
            var seat = LocalPeer();
            if (seat != null)
            {
                seat.HeroClass = heroClass;
            }
        }

        /// <summary>The local player toggles READY.</summary>
        public void SetReady(bool ready)
        {
            _ready[_localPeerId] = ready;
        }

        /// <summary>A replicated ready toggle from another seat.</summary>
        public void NotePeerReady(string peerId, bool ready)
        {
            if (!string.IsNullOrEmpty(peerId))
            {
                _ready[peerId] = ready;
            }
        }

        /// <summary>
        /// Re-read the session (players joining/leaving mid-lobby update the list), and — on the
        /// host's model only — start the match once every connected player is ready.
        /// </summary>
        public void Update()
        {
            _seats.Clear();
            foreach (var peer in _session.Seats)
            {
                _seats.Add(new LobbySeat
                {
                    PeerId = peer.PeerId,
                    AccountId = peer.AccountId,
                    HeroClass = peer.HeroClass,
                    Ready = IsReady(peer.PeerId),
                });
            }

            // No host force-start: the ONLY start is everyone-connected-ready. The session refuses
            // the call for anyone who is not the host, so only the host's model ever lands it.
            if (_session.Phase == NetSessionPhase.Lobby && AllReady)
            {
                _session.TryStartMatch(_localPeerId);
            }
        }

        // ---- helpers --------------------------------------------------------------------------

        private bool IsReady(string peerId) =>
            !string.IsNullOrEmpty(peerId) && _ready.TryGetValue(peerId, out var ready) && ready;

        private NetPeer LocalPeer()
        {
            foreach (var peer in _session.Seats)
            {
                if (string.Equals(peer.PeerId, _localPeerId, StringComparison.Ordinal))
                {
                    return peer;
                }
            }

            return null;
        }
    }
}
