using System;
using RedHollow.Game.View;
using RedHollow.Sim;

namespace RedHollow.Game.Host
{
    /// <summary>
    /// Ticket 019 (T-19) — the playable bootstrap: the plain C# object that assembles a match and
    /// drives it. Every piece it needs already existed and nothing connected them.
    ///
    /// It owns the wiring the PRD's loop implies but no single class held: opening the match with
    /// a wave (R-19), keeping monsters targeted (R-16) and moving (R-17/R-18), letting the R-18
    /// gate turn contact into damage, moving the campaign on when a wave is cleared (R-02/R-03),
    /// and keeping the view set level with the world (R-51).
    ///
    /// Plain C# rather than a MonoBehaviour, and that is the whole architecture of the shell:
    /// <see cref="MatchHostBehaviour"/> stays a two-member pump that holds one of these and calls
    /// <see cref="Step"/>, so no game rule ever enters a component (T-10's IL invariant).
    /// </summary>
    public sealed class MatchSession
    {
        private readonly IMatchSimHost _sim;
        private readonly IHeroIntentSource _heroIntents;
        private readonly MatchViewBinder _views;

        /// <param name="sim">The widened sim seam (R-51). Everything below goes through it.</param>
        /// <param name="heroIntents">R-30 — this client's / the host's resolved hero input, or null.</param>
        /// <param name="views">
        /// R-51 — the view set to keep level with the world, or null for a headless session (a
        /// dedicated host, or a test). A session must run without one: rendering is not a rule.
        /// </param>
        public MatchSession(
            IMatchSimHost sim,
            IHeroIntentSource heroIntents = null,
            MatchViewBinder views = null)
        {
            _sim = sim;
            _heroIntents = heroIntents;
            _views = views;
        }

        /// <summary>
        /// R-19 — open the match: the wave the match is on enters the colony. On a fresh match
        /// (<see cref="WaveState.Number"/> = 1) that is wave 1, which is what "starting a match
        /// spawns wave 1" means; a session handed a match already on wave 10 opens wave 10.
        /// </summary>
        public void Start()
        {
            throw new NotImplementedException(
                "ticket 019: starting a match must put the current wave's monsters in the colony "
                + "(R-19) — nothing calls MatchSim.SpawnWave yet");
        }

        /// <summary>
        /// One host step of a live match: the loop (R-51), wave progression (R-02/R-03) and the
        /// view set. Bounded by nothing here — the caller owns the cadence, so a fixed-step pump
        /// and a test loop drive exactly the same code.
        /// </summary>
        public void Step(double deltaSeconds)
        {
            throw new NotImplementedException(
                "ticket 019: a driven session must move, target, gate and progress the match "
                + "(R-02 / R-03 / R-16 / R-18 / R-19)");
        }
    }
}
