using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RedHollow.Game.Host;
using RedHollow.Sim;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 011 (T-11) — where a party is, from the lobby to the post-match screen.
    ///
    /// Distinct from <see cref="RedHollow.Sim.MatchPhase"/> and
    /// <see cref="RedHollow.Sim.MatchStatus"/> on purpose, and the distinction is R-53's: a host
    /// that leaves <b>ends the match</b> without anybody having lost it, and R-02 makes an emptied
    /// colony the only defeat there is. A session that reported an abandoned match as a defeat would
    /// be inventing a second loss rule and writing it onto the players' post-match screen.
    /// </summary>
    public enum NetSessionPhase
    {
        /// <summary>Nothing has been started yet.</summary>
        Offline,

        /// <summary>R-07 / R-50 — the party is seated, the join code is live, class picks are held.</summary>
        Lobby,

        /// <summary>A match is running. R-53: nobody new may arrive from here.</summary>
        InMatch,

        /// <summary>R-07 — victory or defeat is on screen; the host may PLAY AGAIN / RETRY.</summary>
        PostMatch,

        /// <summary>R-53 — the host left. No host migration in v1, so there is nothing to return to.</summary>
        Ended,
    }

    /// <summary>
    /// Ticket 011 (T-11) — what the session has to tell the players about (R-53's toast).
    ///
    /// A kind and a peer, plus text nothing may depend on: R-53 requires that a toast is shown and
    /// names no copy for it, so the copy is presentation and the kind is the contract.
    /// </summary>
    public enum SessionNoticeKind
    {
        /// <summary>R-53 — a player left mid-match; their hero despawned and the match continues.</summary>
        PlayerDisconnected,

        /// <summary>R-53 — the host left, which ends the match.</summary>
        HostDisconnected,

        /// <summary>R-53 — somebody tried to join a match already in progress.</summary>
        JoinRefused,

        /// <summary>R-07 — the host restarted, so everyone is back in the lobby.</summary>
        MatchRestarted,
    }

    /// <summary>One thing the session has to surface to the party (R-53). See <see cref="SessionNoticeKind"/>.</summary>
    public sealed class SessionNotice
    {
        public SessionNoticeKind Kind;

        /// <summary>Who it is about, when it is about somebody.</summary>
        public string PeerId;

        /// <summary>Toast copy. The PRD names none, so nothing may assert on this.</summary>
        public string Text;
    }

    /// <summary>
    /// Ticket 011 (T-11) — the host-side session: the lobby, the party, the live match, and every
    /// transition between them. Owns R-07 (rematch), R-53 (disconnects and mid-match joins) and
    /// R-55 (the non-pausing overlay).
    ///
    /// <b>Plain C#, and this is the ticket where that matters most.</b> Netcode for GameObjects is
    /// MonoBehaviour-shaped from top to bottom — <c>NetworkManager</c> is a component and
    /// <c>NetworkBehaviour</c> derives from <c>MonoBehaviour</c> — so the obvious way to write this
    /// ticket is a component that owns the match and pokes it on callbacks, which is precisely what
    /// T-10's Cecil scan rejects. Everything below therefore sits above
    /// <see cref="INetTransport"/>, and a component's whole job is to forward
    /// <c>OnClientDisconnect</c> into <see cref="Disconnect"/>.
    ///
    /// <b>It decides no game rule either.</b> The reset in <see cref="TryRematch"/> is a rebuild
    /// (<see cref="IHostedMatchFactory"/>), the despawn in <see cref="Disconnect"/> goes through the
    /// sim, and <see cref="Step"/> is a forward onto <see cref="RedHollow.Game.Host.MatchSession"/>.
    /// What this class contributes is *who is allowed to do what, and when* — which is the half of
    /// R-07 and R-53 that has no home in the sim at all.
    /// </summary>
    public sealed class NetSession
    {
        private readonly NetSessionConfig _config;
        private readonly INetTransport _transport;
        private readonly IHostedMatchFactory _matches;

        /// <summary>
        /// R-07 / R-50 — the party, in join order, and the one piece of state here that outlives a
        /// match. The class picks live on these peers precisely because a rematch discards the whole
        /// <see cref="MatchState"/>: a pick stored inside the match is a pick the reset destroys.
        /// </summary>
        private readonly List<NetPeer> _seats = new List<NetPeer>();

        private readonly ReadOnlyCollection<NetPeer> _seatsView;

        private readonly List<SessionNotice> _notices = new List<SessionNotice>();

        private readonly ReadOnlyCollection<SessionNotice> _noticesView;

        /// <summary>
        /// R-53 — who the host is, remembered at <see cref="StartHost"/> rather than read off
        /// <see cref="NetPeer.IsHost"/>: the flag arrives from the wire, and a client that could set
        /// it could restart or end the match out from under the party.
        /// </summary>
        private string _hostPeerId;

        private NetSessionPhase _phase = NetSessionPhase.Offline;

        private HostedMatch _match;

        private bool _overlayOpen;

        /// <param name="config">R-50 — the UGS project id, or none for loopback.</param>
        /// <param name="transport">R-50 — connectivity. Loopback in EditMode, NGO in a build.</param>
        /// <param name="matches">R-07 — where a fresh match comes from, every time.</param>
        public NetSession(
            NetSessionConfig config, INetTransport transport, IHostedMatchFactory matches)
        {
            if (transport == null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            if (matches == null)
            {
                throw new ArgumentNullException(nameof(matches));
            }

            // R-50 — a session with no config is the offline default (no project id, R-50's party
            // cap), not a failure: the loopback case must be the default rather than a special mode.
            _config = config ?? new NetSessionConfig();
            _transport = transport;
            _matches = matches;

            _seatsView = new ReadOnlyCollection<NetPeer>(_seats);
            _noticesView = new ReadOnlyCollection<SessionNotice>(_notices);
        }

        /// <summary>The configuration this session came up on (R-50).</summary>
        public NetSessionConfig Config => _config;

        /// <summary>The transport underneath (R-50).</summary>
        public INetTransport Transport => _transport;

        /// <summary>Where the party is (R-07 / R-53).</summary>
        public NetSessionPhase Phase => _phase;

        /// <summary>
        /// R-07 — the join code, retained across a rematch.
        ///
        /// Read straight off the transport rather than copied at <see cref="StartHost"/>: the code
        /// belongs to the lobby the transport holds open, and a cached copy is a code that keeps
        /// being shown after the lobby behind it has gone.
        /// </summary>
        public string JoinCode => _transport.JoinCode;

        /// <summary>
        /// R-07 / R-50 — the seated party in join order, class picks included. Survives a rematch;
        /// a mid-match disconnect frees the seat (R-53).
        /// </summary>
        public IReadOnlyList<NetPeer> Seats => _seatsView;

        /// <summary>
        /// The match the session is running, or the one it just finished, or null before the first
        /// has been started. Kept after the end because R-07's post-match screen reads it.
        /// </summary>
        public HostedMatch Match => _match;

        /// <summary>R-55 — whether the ESC overlay is up. It never pauses anything.</summary>
        public bool IsOverlayOpen => _overlayOpen;

        /// <summary>R-53 — what the party has to be told about, oldest first.</summary>
        public IReadOnlyList<SessionNotice> Notices => _noticesView;

        /// <summary>
        /// R-50 — come up as host and open the lobby. The host takes the first seat; solo is this
        /// and nothing more.
        ///
        /// The whole config goes to the transport, project id and all — carried rather than
        /// interpreted here, because whether an id is needed is the transport's answer
        /// (<see cref="INetTransport.RequiresUnityServices"/>) and loopback's is no.
        /// </summary>
        public void StartHost(NetPeer host)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (string.IsNullOrEmpty(host.PeerId))
            {
                throw new ArgumentException("a host peer needs an id", nameof(host));
            }

            if (_phase != NetSessionPhase.Offline)
            {
                // Hosting twice would leave two lobbies claiming one party. Loud rather than
                // ignored: unlike a join or a rematch, nothing on the wire can send this.
                throw new InvalidOperationException(
                    "this session is already '" + _phase + "'; a host starts one lobby (R-50)");
            }

            _transport.StartHost(_config);

            _hostPeerId = host.PeerId;
            _seats.Add(host);
            _transport.ConnectPeer(host);

            _phase = NetSessionPhase.Lobby;
        }

        /// <summary>
        /// R-50 / R-53 — seat a joining player. False when the party is full (R-50) and false while
        /// a match is running (R-53: no mid-match joins), which is a refusal rather than a throw
        /// because both are ordinary lobby outcomes.
        ///
        /// Every refusal below leaves the party exactly as it found it, and that is structural: the
        /// seat is added on the last line, after every gate, so a half-applied join — a hero
        /// standing in a match nobody is driving — cannot be written.
        /// </summary>
        public bool TryJoin(NetPeer peer)
        {
            if (peer == null || string.IsNullOrEmpty(peer.PeerId))
            {
                return false;
            }

            if (_phase != NetSessionPhase.Lobby)
            {
                // R-53 — no mid-match joins, and nothing to join before a host has opened a lobby or
                // after the host has left. Surfaced rather than silent: a joiner who was told
                // nothing is a player staring at a spinner.
                Notify(SessionNoticeKind.JoinRefused, peer.PeerId,
                    "the party is not accepting joins right now");
                return false;
            }

            if (_seats.Count >= MaxPlayers)
            {
                Notify(SessionNoticeKind.JoinRefused, peer.PeerId, "the party is full");
                return false;
            }

            if (SeatIndexOfPeer(peer.PeerId) >= 0 || SeatIndexOfAccount(peer.AccountId) >= 0)
            {
                // One account holds one slot, the rule PartyRoster.TryAdd already states: a
                // reconnect racing its own timeout must not take two seats.
                Notify(SessionNoticeKind.JoinRefused, peer.PeerId, "that player is already seated");
                return false;
            }

            _seats.Add(peer);
            _transport.ConnectPeer(peer);
            return true;
        }

        /// <summary>
        /// R-50 — the host starts the match: one fresh <see cref="HostedMatch"/> for the seated
        /// party, opened at its first wave. False for anyone who is not the host, and false when
        /// there is no lobby to start from.
        /// </summary>
        public bool TryStartMatch(string peerId)
        {
            if (_phase != NetSessionPhase.Lobby || !IsHost(peerId))
            {
                return false;
            }

            if (_seats.Count < PartyRoster.MinPlayers)
            {
                return false;
            }

            // A copy, not the live list: the factory reads the party while the session is free to
            // keep taking disconnects, and R-07 will hand it a different copy next time.
            _match = _matches.CreateMatch(new List<NetPeer>(_seats));
            if (_match == null)
            {
                return false;
            }

            // R-19 — the match's wave enters the colony. Ticket 019 owns the schedule from here.
            _match.Session.Start();

            _phase = NetSessionPhase.InMatch;
            return true;
        }

        /// <summary>
        /// R-07 — PLAY AGAIN / RETRY. The whole party returns to the same lobby, join code and class
        /// picks retained; every field of the finished match is discarded rather than reset in place
        /// (see <see cref="IHostedMatchFactory"/>); account profiles persist per R-43.
        ///
        /// False for anyone who is not the host: R-07 says "when the host clicks it", and a client
        /// that could restart the match could restart it out from under the party.
        ///
        /// <b>The reset is not performed here, and that is DEC-RUN-11.</b> R-07 asks for two things
        /// — "back to the same lobby" and "all match state resets fully" — and they resolve in
        /// sequence: this returns the party to the lobby and drops the finished match on the floor,
        /// and the reset is what the next <see cref="TryStartMatch"/> builds. Scrubbing fields on
        /// the way out would mean remembering every field the sim ever grows, and the first one
        /// missed is last match's barricade standing in the next one.
        /// </summary>
        public bool TryRematch(string peerId)
        {
            if (_phase != NetSessionPhase.PostMatch || !IsHost(peerId))
            {
                return false;
            }

            _match = null;
            _phase = NetSessionPhase.Lobby;

            Notify(SessionNoticeKind.MatchRestarted, peerId, "the host started a new match");
            return true;
        }

        /// <summary>
        /// R-53 — a peer left.
        ///
        /// A player: their hero despawns, monsters holding it retarget, the match continues and a
        /// toast is shown. The host: the match ends, because v1 has no host migration — and it ends
        /// without a defeat, since R-02 leaves the emptied colony the only way to lose.
        /// </summary>
        public void Disconnect(string peerId)
        {
            if (string.IsNullOrEmpty(peerId))
            {
                return;
            }

            var seatIndex = SeatIndexOfPeer(peerId);
            var peer = seatIndex >= 0 ? _seats[seatIndex] : null;

            if (seatIndex >= 0)
            {
                _seats.RemoveAt(seatIndex);
            }

            _transport.DisconnectPeer(peerId);

            if (IsHost(peerId))
            {
                EndSessionWithTheHost(peerId);
                return;
            }

            if (peer != null && _phase == NetSessionPhase.InMatch && _match != null)
            {
                DespawnTheLeaver(peer.AccountId);
            }

            Notify(SessionNoticeKind.PlayerDisconnected, peerId, "a player left the match");
        }

        /// <summary>
        /// R-55 — open or close the ESC overlay. It is an overlay and nothing else: it must not stop
        /// the sim, must not touch <c>Time.timeScale</c>, and must not be reachable as a pause,
        /// because multiplayer never pauses.
        ///
        /// One assignment, and the absence of everything else is the requirement. In a
        /// host-authoritative session (R-51) a pause is one player's host loop stopping while the
        /// rest of the party keeps playing, so there is nothing for this to switch off.
        /// </summary>
        public void SetOverlayOpen(bool open)
        {
            _overlayOpen = open;
        }

        /// <summary>
        /// One host step (R-51). Drives the live match and notices when it has ended; a no-op
        /// outside a live match, so a caller pumping a fixed-step loop needs no phase check of its
        /// own.
        ///
        /// <see cref="IsOverlayOpen"/> is deliberately not consulted (R-55).
        ///
        /// The end is checked on both sides of the step: a match that ended through a command the
        /// session never made — a shelter emptied by the shell, ticket 019's own drive — must still
        /// reach the post-match screen, and a finished match must never be stepped again (R-01: a
        /// won match spawns nothing further, and <see cref="MatchSim.BeginPlanningPhase"/> throws
        /// for one).
        /// </summary>
        public void Step(double deltaSeconds)
        {
            if (_phase != NetSessionPhase.InMatch || _match == null)
            {
                return;
            }

            if (_match.State.IsOver)
            {
                ConcludeMatch();
                return;
            }

            _match.Session.Step(deltaSeconds);

            if (_match.State.IsOver)
            {
                ConcludeMatch();
            }
        }

        // ---- transitions ----------------------------------------------------------------------

        /// <summary>
        /// R-07 / R-43 — the match is over, so the party goes to the post-match screen and every
        /// account's progression is written down.
        ///
        /// The save is the reason this is a method rather than an assignment: R-43 does not persist
        /// per kill (G-024), so the XP earned since each player's last level-up exists only in the
        /// live profile until <see cref="MatchSim.SaveProfilesAtMatchEnd"/> writes it. Without this
        /// the tail of every match is lost, and R-07's rematch would look like it reset progression.
        /// </summary>
        private void ConcludeMatch()
        {
            _match.Sim.SaveProfilesAtMatchEnd();
            _phase = NetSessionPhase.PostMatch;
        }

        /// <summary>
        /// R-53 / DEC-RUN-10 — the host left, so the session ends. No host migration in v1, so there
        /// is nothing to hand the match to and nothing to return to.
        ///
        /// <b><see cref="MatchState.Status"/> is deliberately left alone.</b> R-02 makes an emptied
        /// colony the only defeat in the game; writing one here would invent a second loss rule and
        /// put it on the players' post-match screen and into everything that reads the status
        /// afterwards. An abandoned match is not a lost match — the end-state lives on the session.
        ///
        /// Progression is still saved: the party played the match, and R-43's lifetime XP never
        /// resets, least of all because somebody's connection dropped.
        /// </summary>
        private void EndSessionWithTheHost(string hostPeerId)
        {
            if (_match != null && _phase == NetSessionPhase.InMatch)
            {
                _match.Sim.SaveProfilesAtMatchEnd();
            }

            _phase = NetSessionPhase.Ended;
            _transport.Shutdown();

            Notify(SessionNoticeKind.HostDisconnected, hostPeerId, "the host left; the match has ended");
        }

        /// <summary>
        /// R-53 — the leaver's hero comes off the field and their slot is marked disconnected.
        ///
        /// Two separate things, and both are load-bearing. The hero is <b>removed</b> rather than
        /// killed: a dead hero is R-33's business and respawns ten seconds later, which is a player
        /// who left walking back into the colony. The slot <b>stays</b>: R-03 judges readiness
        /// across connected players, so a deleted slot would renumber the party mid-match while a
        /// disconnected one simply stops being waited on.
        ///
        /// Nothing retargets anything here. R-16 is the sim's answer and
        /// <see cref="MatchSession.Step"/> already re-asks it for every monster whose target has
        /// left the field, which is exactly what this despawn just did to it — so the retarget rides
        /// the next host step through <see cref="MatchSim.SelectTarget"/> rather than through a
        /// second copy of the rule written here.
        /// </summary>
        private void DespawnTheLeaver(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return;
            }

            var state = _match.State;

            List<string> heroIds = null;
            foreach (var hero in state.Heroes.Values)
            {
                if (hero != null && string.Equals(hero.AccountId, accountId, StringComparison.Ordinal))
                {
                    heroIds = heroIds ?? new List<string>();
                    heroIds.Add(hero.Id);
                }
            }

            if (heroIds != null)
            {
                for (var i = 0; i < heroIds.Count; i++)
                {
                    state.Heroes.Remove(heroIds[i]);
                }
            }

            for (var i = 0; i < state.Players.Count; i++)
            {
                var player = state.Players[i];
                if (player != null && string.Equals(player.AccountId, accountId, StringComparison.Ordinal))
                {
                    player.Connected = false;

                    // R-03 — a player who has left cannot be waited on and must not count as a yes;
                    // clearing the flag keeps a stale ready from ending somebody else's planning.
                    player.Ready = false;
                }
            }
        }

        // ---- helpers --------------------------------------------------------------------------

        /// <summary>
        /// R-50 / DEC-020 — the party ceiling, never wider than the roster's own rule. Config may
        /// tighten it (a two-player playtest build); a config that tried to widen it past R-50's
        /// four, or that named nothing at all, gets the rule instead.
        /// </summary>
        private int MaxPlayers
        {
            get
            {
                var configured = _config.MaxPlayers;
                if (configured <= 0 || configured > PartyRoster.MaxPlayers)
                {
                    return PartyRoster.MaxPlayers;
                }

                return configured;
            }
        }

        private bool IsHost(string peerId) =>
            !string.IsNullOrEmpty(peerId) && string.Equals(peerId, _hostPeerId, StringComparison.Ordinal);

        private int SeatIndexOfPeer(string peerId)
        {
            if (string.IsNullOrEmpty(peerId))
            {
                return -1;
            }

            for (var i = 0; i < _seats.Count; i++)
            {
                if (string.Equals(_seats[i].PeerId, peerId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private int SeatIndexOfAccount(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return -1;
            }

            for (var i = 0; i < _seats.Count; i++)
            {
                if (string.Equals(_seats[i].AccountId, accountId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// R-53 — one thing the party has to be told, appended oldest-first. The kind and the peer
        /// are the contract; the text is presentation the PRD names none of, so nothing may read it.
        /// </summary>
        private void Notify(SessionNoticeKind kind, string peerId, string text)
        {
            _notices.Add(new SessionNotice
            {
                Kind = kind,
                PeerId = peerId,
                Text = text,
            });
        }
    }
}
