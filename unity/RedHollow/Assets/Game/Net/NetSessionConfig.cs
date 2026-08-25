using RedHollow.Game.Host;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 011 (T-11) — everything a session needs to come up, injected rather than compiled in
    /// (R-50).
    ///
    /// <see cref="UgsProjectId"/> is the whole reason this type exists. R-50 names Unity Lobby and
    /// Relay as the shipped transport, both of which need a cloud project id — and a project id
    /// baked into the shell is a build that cannot run on anybody else's account and a test that
    /// cannot run at all. It arrives here instead, and <b>null is a supported value</b>: a loopback
    /// session is the local case R-50 still has to serve (solo is a 1-player lobby), and it talks to
    /// no Unity service, so demanding an id for one would make the offline path depend on the online
    /// one.
    ///
    /// Plain mutable fields, shaped like the sim's own request types: this is authored data the
    /// shell fills from a ScriptableObject or from the command line, and it decides nothing.
    /// </summary>
    public sealed class NetSessionConfig
    {
        /// <summary>
        /// R-50 — the UGS cloud project id Lobby/Relay authenticate against, or null/empty for a
        /// loopback session, which needs none.
        /// </summary>
        public string UgsProjectId;

        /// <summary>R-50 / DEC-020 — party ceiling. Defaults to the roster's own rule, never a literal.</summary>
        public int MaxPlayers = PartyRoster.MaxPlayers;
    }

    /// <summary>
    /// Ticket 011 (T-11) — one connected participant, from the session's point of view: who they
    /// are on the wire (<see cref="PeerId"/>), who they are to the profile store
    /// (<see cref="AccountId"/>, R-43/R-44) and which hero they picked in the lobby
    /// (<see cref="HeroClass"/>, R-07).
    ///
    /// The class pick lives on the peer rather than on the match because R-07 makes it survive the
    /// match: a rematch resets every field of <see cref="RedHollow.Sim.MatchState"/> and must still
    /// put the same party back in the lobby with the same picks. Anything stored only inside the
    /// match state is, by construction, a pick that a full reset destroys.
    /// </summary>
    public sealed class NetPeer
    {
        /// <summary>Transport-level identity. Opaque: the PRD names no format.</summary>
        public string PeerId;

        /// <summary>R-43 / R-44 — the account this peer plays as; the key profiles persist under.</summary>
        public string AccountId;

        /// <summary>R-07 / R-31 — the lobby class pick, retained across a rematch.</summary>
        public string HeroClass;

        /// <summary>R-53 — the host, whose departure ends the match (no migration in v1).</summary>
        public bool IsHost;
    }
}
