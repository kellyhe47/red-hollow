using System;
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
        public MatchStatsTracker(PlaceableCatalog catalog) =>
            throw new NotImplementedException("T-12: the stats tracker");

        public void OnSimEvent(SimEvent evt) =>
            throw new NotImplementedException("T-12: count the match");

        /// <summary>Kills credited to this hero (one per `xp_awarded` naming it).</summary>
        public int KillsBy(string heroId) =>
            throw new NotImplementedException("T-12: kills per player");

        /// <summary>Total scrip spent on placeables across the match.</summary>
        public int ScripSpent =>
            throw new NotImplementedException("T-12: scrip spent");
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
        public PostMatchModel(
            NetSession session, string localPeerId, MatchStatsTracker stats, int civiliansAtStart) =>
            throw new NotImplementedException("T-12 / R-60: the post-match screen");

        /// <summary>"THE COLONY STANDS" vs "THE COLONY HAS FALLEN" — off the status field only.</summary>
        public bool IsVictory =>
            throw new NotImplementedException("T-12 / R-01 / R-02: the outcome");

        public int CiviliansSaved =>
            throw new NotImplementedException("T-12: civilians saved");

        public int CiviliansAtStart =>
            throw new NotImplementedException("T-12: the denominator");

        /// <summary>S7 — "reached wave N".</summary>
        public int ReachedWave =>
            throw new NotImplementedException("T-12: reached wave");

        public MatchStatsTracker Stats =>
            throw new NotImplementedException("T-12: the stats table");

        /// <summary>R-07 — true only for the host; the button is disabled for everyone else.</summary>
        public bool CanRematch =>
            throw new NotImplementedException("T-12 / R-07: host-only rematch");

        /// <summary>PLAY AGAIN / RETRY → <see cref="NetSession.TryRematch"/>.</summary>
        public bool RequestRematch() =>
            throw new NotImplementedException("T-12 / R-07: rematch");
    }
}
