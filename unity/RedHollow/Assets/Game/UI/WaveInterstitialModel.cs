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
        private readonly HostedMatch _match;

        private readonly int _civiliansAtStart;

        private int _wave;

        private int _bountyEarned;

        private int _civiliansRemaining;

        /// <param name="civiliansAtStart">
        /// The banner's denominator ("18/20") — the colony's full population, captured at match
        /// start because the sim keeps no record of it once civilians die.
        /// </param>
        public WaveInterstitialModel(HostedMatch match, int civiliansAtStart)
        {
            _match = match;
            _civiliansAtStart = civiliansAtStart;
        }

        public int Wave => _wave;

        /// <summary>This wave's takings — not the last kill and not the shared pool.</summary>
        public int BountyEarned => _bountyEarned;

        public int CiviliansRemaining => _civiliansRemaining;

        public int CiviliansAtStart => _civiliansAtStart;

        /// <summary>Re-read <see cref="RedHollow.Sim.MatchSim.WaveSummary"/>.</summary>
        public void Refresh()
        {
            var summary = _match.Sim.WaveSummary();
            _wave = summary.Wave;
            _bountyEarned = summary.BountyEarned;
            _civiliansRemaining = summary.CiviliansRemaining;
        }
    }
}
