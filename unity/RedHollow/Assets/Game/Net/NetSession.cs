using System;
using System.Collections.Generic;

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

        /// <param name="config">R-50 — the UGS project id, or none for loopback.</param>
        /// <param name="transport">R-50 — connectivity. Loopback in EditMode, NGO in a build.</param>
        /// <param name="matches">R-07 — where a fresh match comes from, every time.</param>
        public NetSession(
            NetSessionConfig config, INetTransport transport, IHostedMatchFactory matches)
        {
            _config = config;
            _transport = transport;
            _matches = matches;
        }

        /// <summary>The configuration this session came up on (R-50).</summary>
        public NetSessionConfig Config => _config;

        /// <summary>The transport underneath (R-50).</summary>
        public INetTransport Transport => _transport;

        /// <summary>Where the party is (R-07 / R-53).</summary>
        public NetSessionPhase Phase =>
            throw new NotImplementedException("T-11: session lifecycle");

        /// <summary>R-07 — the join code, retained across a rematch.</summary>
        public string JoinCode => throw new NotImplementedException("T-11 / R-07: the party's join code");

        /// <summary>
        /// R-07 / R-50 — the seated party in join order, class picks included. Survives a rematch;
        /// a mid-match disconnect frees the seat (R-53).
        /// </summary>
        public IReadOnlyList<NetPeer> Seats => throw new NotImplementedException("T-11: the party");

        /// <summary>
        /// The match the session is running, or the one it just finished, or null before the first
        /// has been started. Kept after the end because R-07's post-match screen reads it.
        /// </summary>
        public HostedMatch Match => throw new NotImplementedException("T-11: the hosted match");

        /// <summary>R-55 — whether the ESC overlay is up. It never pauses anything.</summary>
        public bool IsOverlayOpen => throw new NotImplementedException("T-11 / R-55: the ESC overlay");

        /// <summary>R-53 — what the party has to be told about, oldest first.</summary>
        public IReadOnlyList<SessionNotice> Notices =>
            throw new NotImplementedException("T-11 / R-53: session notices");

        /// <summary>
        /// R-50 — come up as host and open the lobby. The host takes the first seat; solo is this
        /// and nothing more.
        /// </summary>
        public void StartHost(NetPeer host) =>
            throw new NotImplementedException("T-11 / R-50: start hosting");

        /// <summary>
        /// R-50 / R-53 — seat a joining player. False when the party is full (R-50) and false while
        /// a match is running (R-53: no mid-match joins), which is a refusal rather than a throw
        /// because both are ordinary lobby outcomes.
        /// </summary>
        public bool TryJoin(NetPeer peer) =>
            throw new NotImplementedException("T-11 / R-53: seat or refuse a joiner");

        /// <summary>
        /// R-50 — the host starts the match: one fresh <see cref="HostedMatch"/> for the seated
        /// party, opened at its first wave. False for anyone who is not the host, and false when
        /// there is no lobby to start from.
        /// </summary>
        public bool TryStartMatch(string peerId) =>
            throw new NotImplementedException("T-11: start the match");

        /// <summary>
        /// R-07 — PLAY AGAIN / RETRY. The whole party returns to the same lobby, join code and class
        /// picks retained; every field of the finished match is discarded rather than reset in place
        /// (see <see cref="IHostedMatchFactory"/>); account profiles persist per R-43.
        ///
        /// False for anyone who is not the host: R-07 says "when the host clicks it", and a client
        /// that could restart the match could restart it out from under the party.
        /// </summary>
        public bool TryRematch(string peerId) =>
            throw new NotImplementedException("T-11 / R-07: rematch");

        /// <summary>
        /// R-53 — a peer left.
        ///
        /// A player: their hero despawns, monsters holding it retarget, the match continues and a
        /// toast is shown. The host: the match ends, because v1 has no host migration — and it ends
        /// without a defeat, since R-02 leaves the emptied colony the only way to lose.
        /// </summary>
        public void Disconnect(string peerId) =>
            throw new NotImplementedException("T-11 / R-53: handle a disconnect");

        /// <summary>
        /// R-55 — open or close the ESC overlay. It is an overlay and nothing else: it must not stop
        /// the sim, must not touch <c>Time.timeScale</c>, and must not be reachable as a pause,
        /// because multiplayer never pauses.
        /// </summary>
        public void SetOverlayOpen(bool open) =>
            throw new NotImplementedException("T-11 / R-55: the non-pausing overlay");

        /// <summary>
        /// One host step (R-51). Drives the live match and notices when it has ended; a no-op
        /// outside a live match, so a caller pumping a fixed-step loop needs no phase check of its
        /// own.
        /// </summary>
        public void Step(double deltaSeconds) =>
            throw new NotImplementedException("T-11: drive the session");
    }
}
