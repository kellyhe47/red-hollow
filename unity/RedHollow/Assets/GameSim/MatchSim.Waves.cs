namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 004 (T-04) owns this half of <see cref="MatchSim"/>: the match FSM and wave
    /// lifecycle. Requirements R-01, R-02, R-03, R-04, R-05, R-14, R-19; graded by fixtures
    /// G-010, G-011, G-012, G-016, G-017.
    ///
    /// The operations below are stubs that throw until ticket 004 lands. The shared core —
    /// fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>
        /// R-19 / R-14 — the campaign this match is playing, as config. Per instance and settable so
        /// a match can be handed a tuned table without touching rule code; a match constructed
        /// without one plays the shipped <see cref="RedHollow.Sim.WaveTable.V1"/> campaign.
        ///
        /// It lives here rather than on <see cref="SimConfig"/> only because the table is wave-rule
        /// data that nothing else in the sim reads.
        /// </summary>
        public WaveTable WaveTable { get; set; }

        /// <summary>R-01, R-02, R-20 / B-006, B-007. A monster died.</summary>
        public MonsterKillResult RecordMonsterKill(MonsterKillRequest request)
        {
            BeginCommand();
            throw NotYet("T-04", "kill bounty into the shared pool, wave completion, and final-wave victory");
        }

        /// <summary>R-03, R-20 / B-009. Open the next wave's planning phase.</summary>
        public PlanningPhaseResult BeginPlanningPhase()
        {
            BeginCommand();
            throw NotYet("T-04", "planning phase start with full scrip carryover");
        }

        /// <summary>R-03 / B-010. A player toggled ready.</summary>
        public ReadyResult SetPlayerReady(string playerId)
        {
            BeginCommand();
            throw NotYet("T-04", "all-connected-players-ready early combat start");
        }

        /// <summary>
        /// R-03 / DEC-006. The host's planning-phase clock tick: planning runs for
        /// <see cref="SimConfig.PlanningDurationSeconds"/> measured from
        /// <see cref="MatchState.PlanningStartedAt"/>, and combat begins the moment that elapses.
        /// <see cref="SetPlayerReady"/> is the *early* exit from the same phase — this is the
        /// ordinary one, and without it a lobby holding one un-ready player never progresses.
        ///
        /// Host-loop shaped and returning nothing, the same as <see cref="TickHeroRegen"/> and
        /// <see cref="TickHeroRespawns"/>: the shell calls it every step, the observation carries
        /// whatever it did, and no fixture drives it.
        /// </summary>
        public void TickPlanningTimer()
        {
            BeginCommand();
            throw NotYet("T-04", "planning timer expiry starting combat at the configured duration");
        }

        /// <summary>
        /// R-05 / DEC-018. The partial preview of the wave the coming combat phase will fight:
        /// which breaches open, and nothing about what comes out of them.
        /// </summary>
        public WavePreviewResult PreviewUpcomingWave()
        {
            BeginCommand();
            throw NotYet("T-04", "the partial wave preview — active entry points only, no types or counts");
        }

        /// <summary>
        /// R-04. The wave-complete interstitial's data: what the wave just paid and how many
        /// civilians are left. The ~3s hold before planning is the shell's.
        /// </summary>
        public WaveSummaryResult WaveSummary()
        {
            BeginCommand();
            throw NotYet("T-04", "wave-complete interstitial data — bounty earned this wave and civilians remaining");
        }
    }
}
