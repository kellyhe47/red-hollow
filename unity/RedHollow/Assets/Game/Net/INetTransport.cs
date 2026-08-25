using System.Collections.Generic;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 011 (T-11) — the session's seam onto connectivity (R-50).
    ///
    /// The same shape <see cref="RedHollow.Sim.IPathOracle"/> and
    /// <see cref="RedHollow.Sim.IProfileStore"/> already establish in this codebase, and for the
    /// same reason: everything R-07, R-53 and R-55 actually require — a full reset, a despawn and a
    /// retarget, a refused join, a non-pausing overlay — is decided *above* the wire and is
    /// unverifiable if it can only be reached through one. Put the wire behind a seam and the rules
    /// are drivable headlessly; leave it in front and the whole ticket is a manual test.
    ///
    /// <see cref="LoopbackNetTransport"/> is the in-memory implementation. The Netcode for
    /// GameObjects + Lobby + Relay implementation is a swap behind this interface, and it is what a
    /// later ticket verifies by hand — nothing here should grow a member that only a real socket can
    /// answer.
    ///
    /// Deliberately NGO-free, and that is load-bearing rather than tidy: <c>NetworkBehaviour</c>
    /// derives from <c>MonoBehaviour</c>, so an NGO type reachable from the session is an NGO type
    /// inside T-10's Cecil scan, which is the invariant this ticket is likeliest to trip.
    /// </summary>
    public interface INetTransport
    {
        /// <summary>Whether a host has been started and not shut down.</summary>
        bool IsRunning { get; }

        /// <summary>
        /// R-50 — whether this transport needs a UGS project id (and therefore Authentication,
        /// Lobby and Relay) to come up at all. False for loopback, which is the acceptance criterion
        /// "no UGS id is required for loopback" stated as a property rather than as an absence.
        /// </summary>
        bool RequiresUnityServices { get; }

        /// <summary>
        /// R-50 — the UGS project id this transport was started with, or null when it was started
        /// without one. Carried, never invented: a transport that defaulted a missing id would make
        /// the loopback case indistinguishable from a misconfigured Relay one.
        /// </summary>
        string ProjectId { get; }

        /// <summary>
        /// R-07 / R-50 — the join code the party shares. Real Lobby mints one; loopback stands one
        /// in so the lobby screen and the rematch path have the same shape offline. The PRD names no
        /// format, so nothing may depend on what it looks like — only that it exists and is stable
        /// while the session is.
        /// </summary>
        string JoinCode { get; }

        /// <summary>Who is currently connected, host included.</summary>
        IReadOnlyList<NetPeer> ConnectedPeers { get; }

        /// <summary>R-50 — come up as host. Host-authoritative: there is no other kind of session.</summary>
        void StartHost(NetSessionConfig config);

        /// <summary>Admit a peer the session has already decided to accept (R-50, R-53).</summary>
        void ConnectPeer(NetPeer peer);

        /// <summary>R-53 — drop a peer. Whether the match survives is the session's call, not this one's.</summary>
        void DisconnectPeer(string peerId);

        /// <summary>Tear the transport down. Idempotent.</summary>
        void Shutdown();
    }
}
