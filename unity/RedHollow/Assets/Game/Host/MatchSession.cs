using System;
using System.Collections.Generic;
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
    ///
    /// <b>It decides no rule either.</b> Every line below is either a <see cref="MatchSim"/>
    /// command or the question "does the sim still need to be asked?" — the wave counter moves in
    /// <see cref="IMatchSimHost.BeginPlanningPhase"/>, combat opens in
    /// <see cref="ISimHost.TickPlanningTimer"/>, and defeat fires inside
    /// <see cref="ISimHost.ApplyHotspotAttack"/>. What this class contributes is the *schedule*,
    /// which the PRD deliberately leaves unstated (R-04's interstitial sits somewhere in here) and
    /// which is therefore the one thing here that may be retuned without touching a rule.
    /// </summary>
    public sealed class MatchSession
    {
        private readonly IMatchSimHost _sim;
        private readonly MatchViewBinder _views;
        private readonly HostLoop _loop;

        /// <summary>
        /// Scratch list for the monsters that need a target this step, so the retarget pass does
        /// not allocate sixty times a second and does not mutate
        /// <see cref="MatchState.Monsters"/> while enumerating it.
        /// </summary>
        private readonly List<string> _needTarget = new List<string>();

        /// <summary>
        /// R-19 — the wave whose monsters this session has already put in the colony. Zero means
        /// "none yet". It is the only piece of state here, and it exists because
        /// <see cref="IMatchSimHost.SpawnWave"/> is not idempotent: the sim happily spawns wave 2
        /// twice, so *something* has to remember that it already asked, and the wave counter alone
        /// cannot say whether the monsters standing under it are this wave's or the last one's.
        /// </summary>
        private int _waveInTheColony;

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
            if (sim == null)
            {
                throw new ArgumentNullException(nameof(sim));
            }

            _sim = sim;
            _views = views;

            // R-18 — contact is geometry and the sim holds none, so the loop is given the shell's
            // answer to "who has arrived?". Owned here rather than injected because a session with
            // no attack source is a match whose monsters walk to the shelters and stand there
            // politely; nothing in the PRD describes that game.
            _loop = new HostLoop(sim, new ContactMonsterAttacks(), heroIntents);
        }

        /// <summary>
        /// R-19 — open the match: the wave the match is on enters the colony. On a fresh match
        /// (<see cref="WaveState.Number"/> = 1) that is wave 1, which is what "starting a match
        /// spawns wave 1" means; a session handed a match already on wave 10 opens wave 10.
        ///
        /// The counter is read, never written: <see cref="IMatchSimHost.BeginPlanningPhase"/> is
        /// the only thing that advances it (G-016), and a bootstrap that opened by advancing would
        /// silently skip wave 1 of every match it ever started.
        /// </summary>
        public void Start()
        {
            _waveInTheColony = _sim.State.Wave.Number;
            _sim.SpawnWave(_waveInTheColony);

            // The wave that just arrived is visible before the first frame is drawn, rather than
            // one step later (R-51).
            SyncViews();
        }

        /// <summary>
        /// One host step of a live match: the loop (R-51), wave progression (R-02/R-03) and the
        /// view set. Bounded by nothing here — the caller owns the cadence, so a fixed-step pump
        /// and a test loop drive exactly the same code.
        ///
        /// Targeting runs before the loop rather than after it, so a monster that was handed a
        /// target this step also walks that way this step: the other order costs every spawn one
        /// wasted tick and, worse, leaves a monster whose shelter was just emptied (R-12) walking
        /// one more step at a target it can no longer hurt.
        ///
        /// Wave progression runs after it, because the loop is what ends a planning phase
        /// (R-03) — asking first would always be reading the previous step's phase.
        /// </summary>
        public void Step(double deltaSeconds)
        {
            RetargetMonstersThatNeedOne();

            _loop.Step(deltaSeconds);

            AdvanceTheCampaign();

            SyncViews();
        }

        /// <summary>
        /// R-16 — keep every living monster pointed at something.
        ///
        /// The host has to keep asking, and the two reasons are both ordinary mid-match states
        /// rather than edge cases: <see cref="IMatchSimHost.SpawnWave"/> leaves
        /// <see cref="Monster.TargetId"/> null, so an unasked wave never leaves its breach, and
        /// R-12 invalidates a target the moment its shelter is emptied — which is exactly when the
        /// wave needs to be sent at the next one.
        ///
        /// Asked only for the monsters that need it, not for the whole roster every step. That is
        /// not a micro-optimisation: each command resets
        /// <see cref="ISimHost.LastObservation"/>, which netcode replicates from (R-51), so thirty
        /// re-selections a step would shred every other command's observation for answers that had
        /// not changed.
        /// </summary>
        private void RetargetMonstersThatNeedOne()
        {
            var state = _sim.State;

            // A finished match re-targets nobody: R-02 fires the moment the last civilian dies, and
            // with every shelter empty SelectTarget has no honest answer left to give.
            if (state.IsOver)
            {
                return;
            }

            _needTarget.Clear();

            foreach (var monster in state.Monsters.Values)
            {
                if (monster != null && monster.Alive && !HoldsAnAttackableTarget(state, monster))
                {
                    _needTarget.Add(monster.Id);
                }
            }

            for (var i = 0; i < _needTarget.Count; i++)
            {
                _sim.SelectTarget(_needTarget[i]);
            }
        }

        /// <summary>
        /// Whether this monster's current target is still something R-16 would have picked. The
        /// three readings are the sim's own (MatchSim.Targeting.cs): a dead hero and a destroyed
        /// placeable have left the field, and an emptied hotspot has stopped being a valid target
        /// (R-12) even though the building is still standing.
        /// </summary>
        private static bool HoldsAnAttackableTarget(MatchState state, Monster monster)
        {
            if (string.IsNullOrEmpty(monster.TargetId))
            {
                return false;
            }

            if (state.Heroes.TryGetValue(monster.TargetId, out var hero))
            {
                return hero.Alive;
            }

            if (state.Hotspots.TryGetValue(monster.TargetId, out var hotspot))
            {
                return hotspot.IsValidTarget;
            }

            if (state.Placeables.TryGetValue(monster.TargetId, out var placeable))
            {
                return placeable.Exists;
            }

            return false;
        }

        /// <summary>
        /// R-02 / R-03 / R-19 — the campaign moves on. Three sim commands share the job and the PRD
        /// orders none of the timing between them, so the schedule below is this class's decision
        /// and is stated as such:
        ///
        ///  * <see cref="ISimHost.ApplyHotspotAttack"/> / <c>RecordMonsterKill</c> return the phase
        ///    to planning when the wave is cleared (R-02), leaving the counter alone;
        ///  * <see cref="IMatchSimHost.BeginPlanningPhase"/> advances the counter (G-016) — asked
        ///    here on the first step after the clear, which puts R-04's interstitial at one host
        ///    step rather than at a number nothing in the PRD supports;
        ///  * <see cref="ISimHost.TickPlanningTimer"/> (already driven by the loop) opens combat
        ///    when R-03's 60 seconds elapse, and the wave is spawned into that combat phase.
        ///
        /// A finished match is left entirely alone. Both of the sim's own guards are there — spawn
        /// refuses, planning throws — but reaching either would mean this class had decided a won
        /// match still had a campaign to advance, and the one that throws would take the whole
        /// session down (R-01).
        /// </summary>
        private void AdvanceTheCampaign()
        {
            var state = _sim.State;

            if (state.IsOver)
            {
                return;
            }

            // The wave this session opened has been cleared and the phase has fallen back to
            // planning with the counter still on it. Nothing else advances the counter, so nothing
            // else can start the next wave.
            if (state.Phase == MatchPhase.Planning
                && state.Wave.Number == _waveInTheColony
                && state.Wave.LivingMonsterIds.Count == 0)
            {
                _sim.BeginPlanningPhase();
                return;
            }

            // Planning has ended (R-03) on a wave whose monsters are not in the colony yet.
            if (state.Phase == MatchPhase.Combat && state.Wave.Number != _waveInTheColony)
            {
                _waveInTheColony = state.Wave.Number;
                _sim.SpawnWave(_waveInTheColony);
            }
        }

        /// <summary>
        /// R-51 — the view set follows the world, every step. Null-checked rather than replaced by
        /// a no-op binder: a headless host must not build a <see cref="UnityEngine.GameObject"/>
        /// per monster for nobody to look at.
        /// </summary>
        private void SyncViews()
        {
            if (_views == null)
            {
                return;
            }

            _views.Sync(_sim.State);
        }
    }
}
