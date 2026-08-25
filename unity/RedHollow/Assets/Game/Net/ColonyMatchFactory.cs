using System;
using System.Collections.Generic;
using RedHollow.Sim;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 011 (T-11) — the one production <see cref="IHostedMatchFactory"/>: a match on the
    /// shipped v1 colony (R-10), against a profile store that outlives it (R-43).
    ///
    /// The store is held here rather than per match, and that is the whole R-07/R-43 interaction in
    /// one field: match state is rebuilt from nothing on every rematch, and account progression is
    /// the one thing that must survive being rebuilt around.
    /// </summary>
    public sealed class ColonyMatchFactory : IHostedMatchFactory
    {
        private readonly ColonyMap _map;
        private readonly SimConfig _config;
        private readonly IProfileStore _profiles;

        /// <param name="map">R-10 — the colony. Null means the shipped <see cref="ColonyMap.V1"/>.</param>
        /// <param name="config">Tunables. Null means the shipped defaults (R-20's 500 stake included).</param>
        /// <param name="profiles">
        /// R-43 — persistent account progression, shared across every match this factory builds. Null
        /// means an in-memory store, which is a match whose XP dies with the process.
        /// </param>
        public ColonyMatchFactory(
            ColonyMap map = null, SimConfig config = null, IProfileStore profiles = null)
        {
            _map = map;
            _config = config;
            _profiles = profiles;
        }

        public HostedMatch CreateMatch(IReadOnlyList<NetPeer> party) =>
            throw new NotImplementedException("T-11 / R-07: build a fresh match for this party");
    }
}
