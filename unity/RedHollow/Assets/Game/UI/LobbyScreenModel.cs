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
        public LobbyScreenModel(NetSession session, string localPeerId) =>
            throw new NotImplementedException("T-12 / R-60: the lobby screen");

        /// <summary>The code to share, read off the session (click-to-copy is presentation).</summary>
        public string JoinCode =>
            throw new NotImplementedException("T-12 / R-07: the join code");

        /// <summary>The player list, in join order, mirroring <see cref="NetSession.Seats"/>.</summary>
        public IReadOnlyList<LobbySeat> Seats =>
            throw new NotImplementedException("T-12: the player list");

        /// <summary>Wireframe state: waiting alone → hint text "share code".</summary>
        public bool WaitingAlone =>
            throw new NotImplementedException("T-12: waiting-alone hint");

        public int ReadyCount =>
            throw new NotImplementedException("T-12 / R-03: ready count");

        /// <summary>The denominator: connected players, not party capacity.</summary>
        public int ConnectedCount =>
            throw new NotImplementedException("T-12 / R-03: connected count");

        public bool AllReady =>
            throw new NotImplementedException("T-12: all connected players ready");

        /// <summary>Always true — duplicate classes are allowed, so no pick is ever blocked.</summary>
        public bool CanPick(string heroClass) =>
            throw new NotImplementedException("T-12 / R-31: duplicate classes allowed");

        /// <summary>The local player picks (or re-picks) a class.</summary>
        public void PickClass(string heroClass) =>
            throw new NotImplementedException("T-12 / R-31: pick a class");

        /// <summary>The local player toggles READY.</summary>
        public void SetReady(bool ready) =>
            throw new NotImplementedException("T-12: ready up");

        /// <summary>A replicated ready toggle from another seat.</summary>
        public void NotePeerReady(string peerId, bool ready) =>
            throw new NotImplementedException("T-12: replicated ready");

        /// <summary>
        /// Re-read the session (players joining/leaving mid-lobby update the list), and — on the
        /// host's model only — start the match once every connected player is ready.
        /// </summary>
        public void Update() =>
            throw new NotImplementedException("T-12: lobby refresh / all-ready start");
    }
}
