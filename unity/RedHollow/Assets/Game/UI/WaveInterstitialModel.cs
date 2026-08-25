using System;
using RedHollow.Game.Net;

namespace RedHollow.Game.UI
{
    /// <summary>
    /// Ticket 012 (T-12) — S5 Wave Complete (R-04 / R-60): "WAVE n CLEARED", bounty earned THIS
    /// wave, civilians remaining X/Y. Exactly <see cref="RedHollow.Sim.MatchSim.WaveSummary"/>'s
    /// answer and nothing more — the summary is a pure read, so the interstitial may refresh as
    /// often as it likes; the ~3s hold and the fall back to S3 belong to <see cref="UiRouter"/>.
    /// </summary>
    public sealed class WaveInterstitialModel
    {
        /// <param name="civiliansAtStart">
        /// The banner's denominator ("18/20") — the colony's full population, captured at match
        /// start because the sim keeps no record of it once civilians die.
        /// </param>
        public WaveInterstitialModel(HostedMatch match, int civiliansAtStart) =>
            throw new NotImplementedException("T-12 / R-04: the interstitial");

        public int Wave =>
            throw new NotImplementedException("T-12 / R-04: which wave cleared");

        /// <summary>This wave's takings — not the last kill and not the shared pool.</summary>
        public int BountyEarned =>
            throw new NotImplementedException("T-12 / R-04: bounty earned this wave");

        public int CiviliansRemaining =>
            throw new NotImplementedException("T-12 / R-04: civilians remaining");

        public int CiviliansAtStart =>
            throw new NotImplementedException("T-12 / R-04: the denominator");

        /// <summary>Re-read <see cref="RedHollow.Sim.MatchSim.WaveSummary"/>.</summary>
        public void Refresh() =>
            throw new NotImplementedException("T-12 / R-04: refresh the summary");
    }
}
