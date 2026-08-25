using System.Collections.Generic;
using System.Globalization;
using RedHollow.Game.Host;
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
        /// <summary>
        /// Prefix for the player slot ids this factory mints, matching how the sim prefixes the ids
        /// it mints itself (`mon_`, `pl_`): entity ids share one namespace in a log, so a slot must
        /// never read as a placeable or a monster.
        /// </summary>
        private const string PlayerSlotIdPrefix = "player_";

        /// <summary>Prefix for hero ids. No format is contract — R-31 names none.</summary>
        private const string HeroIdPrefix = "hero_";

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
            // Resolved once, here, rather than per call: R-07 rebuilds a match on every rematch, and
            // a factory that defaulted its profile store per match would hand the second match a
            // brand-new store — which is "lifetime XP resets when the host clicks PLAY AGAIN".
            _map = map ?? ColonyMap.V1();
            _config = config ?? new SimConfig();
            _profiles = profiles ?? new InMemoryProfileStore();
        }

        /// <summary>
        /// R-07 / R-10 / R-20 / R-31 / R-43 — a brand-new match for this party.
        ///
        /// Every field starts from <see cref="ColonyMap.CreateMatchState"/> rather than from a
        /// scrubbed previous match: that bridge already seeds the shelters (R-10) and R-20's opening
        /// stake, and building fresh is what makes R-07's "all match state resets fully" total by
        /// construction rather than a list of fields somebody has to keep up to date.
        ///
        /// The party arrives from outside for the same reason: R-07 retains class picks <i>across</i>
        /// the reset, so the picks cannot live in the thing being reset.
        ///
        /// The match opens in combat on the wave it is on, which is wave 1 for a fresh match —
        /// <see cref="MatchSession.Start"/> then puts that wave in the colony (R-19). The counter is
        /// never written here: <see cref="MatchSim.BeginPlanningPhase"/> owns it (G-016).
        /// </summary>
        public HostedMatch CreateMatch(IReadOnlyList<NetPeer> party)
        {
            var state = _map.CreateMatchState(_config);

            // R-01 — the campaign length is a tunable, and the wave table has to define every wave
            // of it. Copied across the same config-to-state bridge the stake crosses, so a retuned
            // campaign is one edit rather than two that can drift apart.
            state.Wave.TotalWaves = _config.TotalWaves;

            // A match that is already running, the same opening ticket 019's own drive uses: R-03's
            // lobby edge belongs to the lobby, and by the time a match exists the party has left it.
            state.Phase = MatchPhase.Combat;
            state.Status = MatchStatus.InProgress;

            var clock = new SimClock();
            var sim = new MatchSim(state, _config, _profiles, clock, null) { ColonyMap = _map };

            SeatTheParty(state, party);

            // R-43 / R-31 — a veteran starts the new match with the abilities their account already
            // paid for. Asked after the heroes exist and before the first step, because that is the
            // only window in which "match start" means anything.
            sim.ApplySavedAbilityAllocations();

            var host = new MatchSimHost(sim, clock);

            return new HostedMatch
            {
                State = state,
                Clock = clock,
                Sim = sim,
                Host = host,

                // Headless: no view binder and no hero intent source. R-51 makes rendering and input
                // the shell's business, and a session must run without either — a dedicated host and
                // an EditMode drive are the same code path.
                Session = new MatchSession(host),
            };
        }

        /// <summary>
        /// R-50 / R-31 / R-07 — one <see cref="PlayerSlot"/> and one <see cref="Hero"/> per seated
        /// peer, in join order, each carrying that peer's account (R-43) and lobby class pick.
        ///
        /// HP comes off the R-31 kit for the pick rather than from a number typed here, so a
        /// retuned class table retunes the match; a class the catalog has no row for throws out of
        /// <see cref="HeroKitCatalog.KitFor"/>, which is what that method exists to make loud — a
        /// hero spawned with 0 max HP dies on the first hit and reads as a combat bug.
        ///
        /// Heroes enter at the map's team spawn (R-10 / R-33), which is the same point they respawn
        /// to, so a match opens where it recovers.
        /// </summary>
        private void SeatTheParty(MatchState state, IReadOnlyList<NetPeer> party)
        {
            if (party == null)
            {
                return;
            }

            for (var i = 0; i < party.Count; i++)
            {
                var peer = party[i];
                if (peer == null)
                {
                    continue;
                }

                var ordinal = (state.Players.Count + 1).ToString(CultureInfo.InvariantCulture);
                var playerId = PlayerSlotIdPrefix + ordinal;
                var kit = _config.HeroKits.KitFor(peer.HeroClass);

                state.Players.Add(new PlayerSlot
                {
                    Id = playerId,
                    AccountId = peer.AccountId,
                    HeroClass = peer.HeroClass,
                    Ready = false,
                    Connected = true,
                });

                var hero = new Hero
                {
                    Id = HeroIdPrefix + ordinal,
                    HeroClass = peer.HeroClass,
                    AccountId = peer.AccountId,
                    Pos = _map.TeamSpawn,
                    Hp = kit.MaxHp,
                    MaxHp = kit.MaxHp,
                    Alive = true,
                };

                state.Heroes[hero.Id] = hero;
            }
        }
    }
}
