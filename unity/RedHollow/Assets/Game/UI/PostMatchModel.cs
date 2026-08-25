using System;
using System.Collections.Generic;
using RedHollow.Game.Net;
using RedHollow.Sim;

namespace RedHollow.Game.UI
{
    /// <summary>
    /// Ticket 012 (T-12) — the S6/S7 stats table's data, accumulated from the event stream while
    /// the match runs. The sim keeps no per-player kill tally and no spend ledger, so the shell
    /// counts what the host broadcast: one `xp_awarded` per credited kill (R-40 awards the
    /// killer), one `placeable_created` per accepted purchase, priced off the R-23 catalog.
    ///
    /// Read-only over the world — it counts events and writes nothing.
    /// </summary>
    public sealed class MatchStatsTracker
    {
        private readonly PlaceableCatalog _catalog;

        private readonly Dictionary<string, int> _killsByHero = new Dictionary<string, int>();

        private int _scripSpent;

        public MatchStatsTracker(PlaceableCatalog catalog)
        {
            _catalog = catalog;
        }

        public void OnSimEvent(SimEvent evt)
        {
            if (evt == null || evt.Fields == null)
            {
                return;
            }

            switch (evt.Type)
            {
                case "xp_awarded":
                    // R-40 — one xp_awarded per credited kill, naming the killer.
                    if (evt.Fields.TryGetValue("hero_id", out var heroId) && heroId is string hero)
                    {
                        _killsByHero.TryGetValue(hero, out var kills);
                        _killsByHero[hero] = kills + 1;
                    }

                    break;

                case "placeable_created":
                    // R-23 — the event names the type; the catalog names the price.
                    if (evt.Fields.TryGetValue("placeable_type", out var typeValue)
                        && typeValue is string placeableType)
                    {
                        _scripSpent += _catalog.StatsFor(placeableType).Cost;
                    }

                    break;
            }
        }

        /// <summary>Kills credited to this hero (one per `xp_awarded` naming it).</summary>
        public int KillsBy(string heroId) =>
            !string.IsNullOrEmpty(heroId) && _killsByHero.TryGetValue(heroId, out var kills)
                ? kills
                : 0;

        /// <summary>Total scrip spent on placeables across the match.</summary>
        public int ScripSpent => _scripSpent;
    }

    /// <summary>
    /// Ticket 012 (T-12) — S6 Victory / S7 Defeat (R-60): the outcome, civilians saved X/Y,
    /// reached-wave, the stats table, and PLAY AGAIN / RETRY.
    ///
    /// The outcome keys off <see cref="MatchState.Status"/> and never off the phase — both fields
    /// read the literal "combat" during a live match, and a won match's phase stays "combat"
    /// forever (there is no eleventh planning phase).
    ///
    /// PLAY AGAIN / RETRY are host-only (R-07) and land on
    /// <see cref="NetSession.TryRematch"/>: the whole party returns to S2 with the same code and
    /// picks (DEC-RUN-11).
    /// </summary>
    public sealed class PostMatchModel
    {
        private readonly NetSession _session;

        private readonly string _localPeerId;

        private readonly MatchStatsTracker _stats;

        private readonly int _civiliansAtStart;

        public PostMatchModel(
            NetSession session, string localPeerId, MatchStatsTracker stats, int civiliansAtStart)
        {
            _session = session;
            _localPeerId = localPeerId;
            _stats = stats;
            _civiliansAtStart = civiliansAtStart;
        }

        /// <summary>"THE COLONY STANDS" vs "THE COLONY HAS FALLEN" — off the status field only.</summary>
        public bool IsVictory => _session.Match != null
                                 && _session.Match.State.Status == MatchStatus.Victory;

        public int CiviliansSaved => _session.Match == null
            ? 0
            : _session.Match.State.TotalCivilians;

        public int CiviliansAtStart => _civiliansAtStart;

        /// <summary>S7 — "reached wave N".</summary>
        public int ReachedWave => _session.Match == null
            ? 0
            : _session.Match.State.Wave.Number;

        public MatchStatsTracker Stats => _stats;

        /// <summary>R-07 — true only for the host; the button is disabled for everyone else.</summary>
        public bool CanRematch
        {
            get
            {
                foreach (var peer in _session.Seats)
                {
                    if (string.Equals(peer.PeerId, _localPeerId, StringComparison.Ordinal))
                    {
                        return peer.IsHost;
                    }
                }

                return false;
            }
        }

        /// <summary>PLAY AGAIN / RETRY → <see cref="NetSession.TryRematch"/>.</summary>
        public bool RequestRematch() => _session.TryRematch(_localPeerId);
    }
}
