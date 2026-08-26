using System;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 020 (T-20) — which Unity Gaming Services call a failure happened in, which is the
    /// only thing the orchestration layer is allowed to know about it. The SDK's own exception
    /// types stay inside the adapter: a transport that caught <c>LobbyServiceException</c> would be
    /// a transport that cannot be driven in EditMode at all.
    /// </summary>
    public enum UgsStep
    {
        /// <summary>Authentication sign-in (anonymous in v1).</summary>
        SignIn,

        /// <summary>Relay allocation on the host.</summary>
        AllocateRelay,

        /// <summary>Relay join on a client, by relay join code.</summary>
        JoinRelay,

        /// <summary>Lobby creation on the host.</summary>
        CreateLobby,

        /// <summary>Lobby join-by-code on a client — where a bad or expired code surfaces.</summary>
        JoinLobby,

        /// <summary>The periodic lobby heartbeat that keeps the lobby from idling out.</summary>
        Heartbeat,

        /// <summary>Leaving / deleting the lobby at teardown.</summary>
        LeaveLobby,
    }

    /// <summary>
    /// Ticket 020 (T-20) — a UGS call failed. One exception type for the whole seam, carrying the
    /// step it failed in, so the orchestration above can distinguish "bad join code" (an ordinary
    /// lobby outcome, surfaced the way S1 already surfaces it) from "auth is down" without ever
    /// naming an SDK type.
    /// </summary>
    public sealed class UgsUnavailableException : Exception
    {
        /// <summary>Which call failed.</summary>
        public readonly UgsStep Step;

        public UgsUnavailableException(UgsStep step, string message)
            : base(message)
        {
            Step = step;
        }
    }

    /// <summary>
    /// Ticket 020 (T-20) — opaque wire-level connection data, produced by the services seam and
    /// consumed by the wire seam, and by nothing else. Deliberately memberless at this level: the
    /// real adapter derives a type holding Relay's <c>RelayServerData</c>, and the orchestration
    /// layer's whole obligation is to hand the wire the same endpoint the allocation answered —
    /// which is exactly what the tests pin (by identity), and all they may pin.
    /// </summary>
    public class RelayEndpoint
    {
    }

    /// <summary>A host-side Relay allocation: the code peers join with, and where the wire binds.</summary>
    public sealed class RelayHostSlot
    {
        /// <summary>The RELAY join code — internal plumbing, stored in the lobby, never shown to a player.</summary>
        public string RelayJoinCode;

        /// <summary>Where the host's wire binds.</summary>
        public RelayEndpoint Endpoint;
    }

    /// <summary>A client-side Relay join: where the joining wire connects.</summary>
    public sealed class RelayJoinSlot
    {
        /// <summary>Where the client's wire connects.</summary>
        public RelayEndpoint Endpoint;
    }

    /// <summary>
    /// Ticket 020 (T-20) — one lobby, as the orchestration layer sees it. The LOBBY join code is
    /// the code players share (R-07's join code, the one on the S2 screen); the relay join code
    /// rides inside the lobby's data so a joiner can reach the host's allocation.
    /// </summary>
    public sealed class LobbyTicket
    {
        /// <summary>The lobby's service-side id — what heartbeats and teardown name.</summary>
        public string LobbyId;

        /// <summary>The code players type — R-07's join code. Format is the service's, never contract.</summary>
        public string JoinCode;

        /// <summary>The relay join code stored in the lobby's data, for the joiner's Relay step.</summary>
        public string RelayJoinCode;
    }

    /// <summary>
    /// Ticket 020 (T-20) — the seam onto Unity Gaming Services: Authentication, Lobby and Relay,
    /// one synchronous-looking surface (R-50).
    ///
    /// The same shape <see cref="INetTransport"/> already is, one layer down, and for the same
    /// reason: everything ticket 020 has to get RIGHT — the order of the host bring-up, a bad join
    /// code refused rather than crashed on, the heartbeat stopping at teardown, loopback never
    /// touching any of this — is decided ABOVE these calls, and none of it is verifiable in
    /// EditMode if it can only be reached through a cloud sign-in. The real implementation
    /// (<see cref="UnityGamingServices"/>) wraps the SDKs and is thin enough to have no branches
    /// worth testing; tests drive <see cref="NgoNetTransport"/> through a scripted fake.
    ///
    /// Failures are thrown as <see cref="UgsUnavailableException"/> naming their
    /// <see cref="UgsStep"/> — never as SDK exception types, which must not cross this seam.
    /// </summary>
    public interface IUgsServices
    {
        /// <summary>Whether sign-in has completed this session.</summary>
        bool IsSignedIn { get; }

        /// <summary>
        /// Authenticate against the given UGS project (anonymous auth in v1). The id is carried
        /// from <see cref="NetSessionConfig.UgsProjectId"/>, never invented (R-50).
        /// </summary>
        void SignIn(string projectId);

        /// <summary>
        /// Host — allocate a Relay slot for at least <paramref name="maxConnections"/> remote
        /// peers, answering the relay join code and the endpoint the host's wire binds to.
        /// </summary>
        RelayHostSlot AllocateRelay(int maxConnections);

        /// <summary>Client — join a host's Relay allocation by its relay join code.</summary>
        RelayJoinSlot JoinRelay(string relayJoinCode);

        /// <summary>
        /// Host — create the lobby players will join by code, with the relay join code stored in
        /// its data so joiners can complete the Relay step.
        /// </summary>
        LobbyTicket CreateLobby(int maxPlayers, string relayJoinCode);

        /// <summary>
        /// Client — join a lobby by the code a player typed on S1. A bad or expired code throws
        /// <see cref="UgsUnavailableException"/> with <see cref="UgsStep.JoinLobby"/>, which the
        /// transport turns into a refusal — the same inline-error surface S1 already shows (T-12).
        /// </summary>
        LobbyTicket JoinLobbyByCode(string joinCode);

        /// <summary>Keep the lobby alive. The service idles lobbies out; the transport schedules this.</summary>
        void HeartbeatLobby(string lobbyId);

        /// <summary>Release the lobby at teardown, so the join code stops resolving to a dead party.</summary>
        void LeaveLobby(string lobbyId);
    }
}
