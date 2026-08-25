using System;
using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 004 (T-04) owns this half of <see cref="MatchSim"/>: the match FSM and wave
    /// lifecycle. Requirements R-01, R-02, R-03, R-04, R-05, R-14, R-19; graded by fixtures
    /// G-010, G-011, G-012, G-016, G-017.
    ///
    /// The FSM R-03 describes is `lobby -> (planning -> combat) x N -> victory`, with a
    /// `combat -> defeat` edge available in every wave (R-02, owned by ticket 003). Two fields
    /// carry it and they are NOT interchangeable: <see cref="MatchState.Phase"/> swings between
    /// planning and combat every wave, while <see cref="MatchState.Status"/> moves once, at the
    /// end, to victory or defeat. Both spell the running match "combat"
    /// (<see cref="MatchStatus.InProgress"/> == <see cref="MatchPhase.Combat"/>), so a wave clear
    /// (G-010, phase) and a map win (G-011, status) look identical until you check which field
    /// moved. Every transition below names the one it means.
    ///
    /// The shared core — fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>
        /// R-03 / G-017 — the early exit: every connected player readied up before the timer ran
        /// out. Pinned by the fixture, so it is contract rather than taste.
        /// </summary>
        private const string TriggerAllReady = "all_ready";

        /// <summary>
        /// R-03 / DEC-006 — the ordinary exit: the planning phase simply ended. Distinct from
        /// <see cref="TriggerAllReady"/> because the two want different stingers and toasts (R-64)
        /// and a client cannot tell them apart from the phase change alone.
        /// </summary>
        private const string TriggerPlanningElapsed = "planning_elapsed";

        private WaveTable _waveTable;

        /// <summary>R-04 — the wave <see cref="_bountyEarnedThisWave"/> was accumulated for.</summary>
        private int _bountyEarnedForWave;

        /// <summary>R-04 — scrip paid by kills during that wave, reset by moving to another one.</summary>
        private int _bountyEarnedThisWave;

        /// <summary>
        /// R-19 / R-14 — the campaign this match is playing, as config. Per instance and settable so
        /// a match can be handed a tuned table without touching rule code; a match constructed
        /// without one plays the shipped <see cref="RedHollow.Sim.WaveTable.V1"/> campaign.
        ///
        /// It lives here rather than on <see cref="SimConfig"/> only because the table is wave-rule
        /// data that nothing else in the sim reads.
        /// </summary>
        public WaveTable WaveTable
        {
            // Built on first read rather than in the constructor: the shipped table is only needed
            // by matches that actually ask for a preview, and a caller assigning its own tuned
            // table must not pay for a discarded default.
            get => _waveTable ?? (_waveTable = RedHollow.Sim.WaveTable.V1());
            set => _waveTable = value;
        }

        /// <summary>
        /// R-01, R-02, R-20 / B-006, B-007. A monster died.
        ///
        /// Three things happen in order, and only for a monster that was genuinely on this wave's
        /// living roster: the bounty is paid into the single shared pool (R-20 / DEC-005 — there
        /// are no private wallets), the wave completes if that was the last one alive (R-02 —
        /// counted from the living roster, never from a spawned total), and clearing the wave the
        /// *match state* calls last wins the map (R-01).
        ///
        /// <see cref="WaveState.TotalWaves"/> decides the final wave, not
        /// <see cref="SimConfig.TotalWaves"/> (DEC-RUN-5): config seeds the match, state is what
        /// the match is actually playing, and every fixture states the state value.
        ///
        /// Sad paths, decided here:
        ///  - a kill for an id that is not on the living roster — an id the match never had, or a
        ///    second report for a monster already dead because a turret and a hero both claimed the
        ///    last hit — pays nothing and completes nothing. A bounty is paid once, on death (R-20),
        ///    and a duplicate that cleared the wave would end it with monsters still walking;
        ///  - a match that is already over ignores the kill outright. R-02 makes defeat immediate,
        ///    so a kill still in flight when the colony emptied must not resurrect the match — and
        ///    on the final wave it would otherwise arrive as a *victory*.
        /// </summary>
        public MonsterKillResult RecordMonsterKill(MonsterKillRequest request)
        {
            BeginCommand();

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var result = new MonsterKillResult
            {
                MonsterId = request.MonsterId,
                BountyAwarded = 0,
                ScripAfter = State.Team.Scrip,
                LivingMonstersRemaining = State.Wave.LivingMonsterIds.Count,
                WaveComplete = false,
                MapVictory = false,
            };

            if (State.IsOver || !State.Wave.LivingMonsterIds.Remove(request.MonsterId))
            {
                return Finish(result);
            }

            // `alive` is the predicate R-16 targets on, so it is a replicated delta. The roster is
            // the wave's own bookkeeping and is not replicated field-by-field; clients derive it
            // from the kill events and the result's living count.
            if (State.Monsters.TryGetValue(request.MonsterId, out var monster))
            {
                var aliveBefore = monster.Alive;
                monster.Alive = false;
                RecordChange(monster.Id, "alive", aliveBefore, false);
            }

            // R-20 / DEC-005 — one shared pool, credited on death regardless of who landed the hit.
            // R-40's XP for the *killer* is ticket 009's separate award; bounty is not split.
            var scripBefore = State.Team.Scrip;
            State.Team.Scrip = scripBefore + request.Bounty;
            RecordChange("team", "scrip", scripBefore, State.Team.Scrip);
            AccrueWaveBounty(request.Bounty);

            Emit("monster_killed", new Dictionary<string, object>
            {
                { "monster_id", request.MonsterId },
                { "bounty", request.Bounty },
            });

            result.BountyAwarded = request.Bounty;
            result.ScripAfter = State.Team.Scrip;
            result.LivingMonstersRemaining = State.Wave.LivingMonsterIds.Count;

            if (result.LivingMonstersRemaining > 0)
            {
                return Finish(result);
            }

            // R-02 — the last living monster died, so the wave is cleared.
            result.WaveComplete = true;
            Emit("wave_complete", new Dictionary<string, object>
            {
                { "wave", State.Wave.Number },
            });

            if (State.Wave.Number >= State.Wave.TotalWaves)
            {
                // R-01 / G-011 — the map is won. This moves the match STATUS and deliberately
                // leaves the phase reading "combat": there is no eleventh planning phase, and the
                // post-match screen keys off status.
                result.MapVictory = true;
                var statusBefore = State.Status;
                State.Status = MatchStatus.Victory;
                RecordChange("match", "status", statusBefore, MatchStatus.Victory);
                Emit("match_victory");
                return Finish(result);
            }

            // R-02 / R-04 / G-010 — a non-final clear moves the PHASE back to planning. The ~3s
            // interstitial (R-04) is the shell's hold on top of this; BeginPlanningPhase is what
            // then advances the wave counter.
            var phaseBefore = State.Phase;
            State.Phase = MatchPhase.Planning;
            RecordChange("match", "phase", phaseBefore, MatchPhase.Planning);
            EnterPlanningPhaseAt(_clock.ElapsedSeconds);

            return Finish(result);
        }

        /// <summary>
        /// R-03, R-20 / B-009. Open the next wave's planning phase.
        ///
        /// The wave counter advances here and nowhere else, and only from a planning phase that a
        /// wave clear already opened (R-02) — which is why G-016's `given` is already
        /// `phase: planning`. The lobby edge is the exception R-03 leaves implicit: the *first*
        /// planning phase is wave 1's, so opening planning out of the lobby starts the campaign
        /// rather than skipping to wave 2.
        ///
        /// R-20 / DEC-005 / G-016: unspent scrip carries over untouched. That is not a copy or a
        /// no-op write — the pool is simply not addressed here, so there is no scrip delta to
        /// replicate at all, which is exactly what the fixture defends.
        ///
        /// Sad paths, decided here — both throw, because there is no honest
        /// <see cref="PlanningPhaseResult"/> to return for a wave that is not going to happen and a
        /// silent no-op would leave the host believing it had advanced:
        ///  - a finished match has no next wave (R-01);
        ///  - combat re-enters planning through wave completion and nothing else (R-03), so asking
        ///    while monsters are still walking must not skip the rest of the wave.
        /// </summary>
        public PlanningPhaseResult BeginPlanningPhase()
        {
            BeginCommand();

            if (State.IsOver)
            {
                throw new InvalidOperationException(
                    "the match is over (" + State.Status + "); a finished match opens no further "
                    + "planning phase (R-01)");
            }

            if (State.Phase != MatchPhase.Lobby && State.Phase != MatchPhase.Planning)
            {
                throw new InvalidOperationException(
                    "cannot open a planning phase from phase '" + State.Phase + "' with "
                    + State.Wave.LivingMonsterIds.Count + " monster(s) still alive; combat returns "
                    + "to planning by clearing the wave (R-03)");
            }

            // Out of the lobby this IS wave 1's planning phase; out of a cleared wave it is the
            // next wave's.
            var waveBefore = State.Wave.Number;
            var wave = State.Phase == MatchPhase.Lobby ? waveBefore : waveBefore + 1;
            State.Wave.Number = wave;
            RecordChange("wave", "number", waveBefore, wave);

            var phaseBefore = State.Phase;
            State.Phase = MatchPhase.Planning;
            RecordChange("match", "phase", phaseBefore, MatchPhase.Planning);

            EnterPlanningPhaseAt(_clock.ElapsedSeconds);
            ClearReadyFlags();

            Emit("planning_started", new Dictionary<string, object>
            {
                { "wave", wave },
                { "duration_seconds", _config.PlanningDurationSeconds },
            });

            return Finish(new PlanningPhaseResult
            {
                Wave = wave,
                Scrip = State.Team.Scrip,
                PlanningSeconds = _config.PlanningDurationSeconds,
            });
        }

        /// <summary>
        /// R-03 / B-010. A player readied up.
        ///
        /// Ready is a flag, not a toggle — R-03 has no un-ready — so this is idempotent, and an
        /// already-ready player replicates no delta because <see cref="RecordChange"/> drops
        /// non-deltas.
        ///
        /// Readiness is judged across **connected** players only (R-03, and R-53: a mid-match
        /// disconnect leaves the match running). A player who has left cannot be waited on, so a
        /// disconnected slot neither holds planning open nor counts as a yes.
        ///
        /// Sad paths, decided here:
        ///  - an id the match does not have throws, matching how every other unknown entity is
        ///    handled here — it must never conjure a slot or ready somebody else's;
        ///  - a ready arriving outside a live planning phase (the message was in flight when the
        ///    timer fired, or the match is already over) is ignored: combat starts once.
        /// </summary>
        public ReadyResult SetPlayerReady(string playerId)
        {
            BeginCommand();

            var player = FindPlayer(playerId);
            if (player == null)
            {
                throw new ArgumentException(
                    "no player '" + playerId + "' in this match", nameof(playerId));
            }

            var result = new ReadyResult
            {
                AllReady = false,
                CombatStarted = false,
                // R-03 — measured from when THIS planning phase opened, never from match start:
                // the 60 seconds are per wave.
                PlanningElapsed = _clock.ElapsedSeconds - State.PlanningStartedAt,
            };

            if (State.IsOver || State.Phase != MatchPhase.Planning)
            {
                return Finish(result);
            }

            var readyBefore = player.Ready;
            player.Ready = true;
            RecordChange(player.Id, "ready", readyBefore, true);

            if (!EveryConnectedPlayerIsReady())
            {
                return Finish(result);
            }

            result.AllReady = true;
            result.CombatStarted = true;
            StartCombat(TriggerAllReady);

            return Finish(result);
        }

        /// <summary>
        /// R-03 / DEC-006. The host's planning-phase clock tick: planning runs for
        /// <see cref="SimConfig.PlanningDurationSeconds"/> measured from
        /// <see cref="MatchState.PlanningStartedAt"/>, and combat begins the moment that elapses.
        /// <see cref="SetPlayerReady"/> is the *early* exit from the same phase — this is the
        /// ordinary one, and without it a lobby holding one un-ready player never progresses.
        ///
        /// The deadline is inclusive, matching how this sim already treats deadlines: G-019 expires
        /// a status effect at exactly its `expires_at` and names strict greater-than as the bug it
        /// guards, and R-33's respawn follows the same rule. At the duration the phase is over, not
        /// still running.
        ///
        /// Host-loop shaped and returning nothing, the same as <see cref="TickHeroRegen"/> and
        /// <see cref="TickHeroRespawns"/>: the shell calls it every step, the observation carries
        /// whatever it did, and no fixture drives it. It is therefore inert everywhere except a
        /// live planning phase — during combat, in the lobby, and in a match already won or lost —
        /// and firing consumes the phase, so a later tick has nothing left to end.
        /// </summary>
        public void TickPlanningTimer()
        {
            BeginCommand();

            if (State.IsOver || State.Phase != MatchPhase.Planning)
            {
                return;
            }

            if (_clock.ElapsedSeconds - State.PlanningStartedAt < _config.PlanningDurationSeconds)
            {
                return;
            }

            StartCombat(TriggerPlanningElapsed);
        }

        /// <summary>
        /// R-05 / DEC-018. The partial preview of the wave the coming combat phase will fight:
        /// which breaches open, and nothing about what comes out of them.
        ///
        /// The projection is the requirement. Only <see cref="WaveSpec.ActiveTunnels"/> is copied
        /// out — by value, into a result type that has no field able to hold a
        /// <see cref="WaveSpec"/>, a <see cref="MonsterGroup"/> or a
        /// <see cref="RedHollow.Sim.WaveTable"/>. Handing back the spec and trusting the UI to
        /// ignore the composition would look identical on screen and hand every client the answer,
        /// which is precisely what DEC-018 rules out.
        /// </summary>
        public WavePreviewResult PreviewUpcomingWave()
        {
            BeginCommand();

            var spec = WaveTable.For(State.Wave.Number);
            var result = new WavePreviewResult { Wave = spec.Number };
            result.ActiveEntryTunnels.AddRange(spec.ActiveTunnels);

            return Finish(result);
        }

        /// <summary>
        /// R-04. The wave-complete interstitial's data: what the wave just paid and how many
        /// civilians are left. The ~3s hold before planning is the shell's.
        ///
        /// "Bounty earned" is the sum across the wave — not the last kill and not the shared pool,
        /// which carries over from earlier waves (R-20) and is spent on placeables in between. It
        /// is accumulated per wave and reset by moving to another one, so wave 4's banner never
        /// shows wave 3's takings.
        ///
        /// A pure read: nothing here moves the pool, which is why the interstitial can be rendered
        /// as often as the shell likes.
        /// </summary>
        public WaveSummaryResult WaveSummary()
        {
            BeginCommand();

            return Finish(new WaveSummaryResult
            {
                Wave = State.Wave.Number,
                BountyEarned = BountyEarnedThisWave(),
                CiviliansRemaining = State.TotalCivilians,
            });
        }

        // ---- helpers ---------------------------------------------------------------------------

        /// <summary>
        /// R-03 — the one door into combat, used by both exits from planning so they cannot drift
        /// apart. <paramref name="trigger"/> is how a client tells the early start from the timer
        /// running out (R-64); everything else about the two is identical.
        /// </summary>
        private void StartCombat(string trigger)
        {
            var phaseBefore = State.Phase;
            State.Phase = MatchPhase.Combat;
            RecordChange("match", "phase", phaseBefore, MatchPhase.Combat);
            Emit("combat_started", new Dictionary<string, object>
            {
                { "wave", State.Wave.Number },
                { "trigger", trigger },
            });
        }

        /// <summary>
        /// R-03 — anchors the planning countdown. Deliberately not a replicated delta: clients
        /// render the countdown from the `planning_started` event's duration, and G-016 pins the
        /// wave number as the only state change a planning phase produces.
        /// </summary>
        private void EnterPlanningPhaseAt(double now)
        {
            State.PlanningStartedAt = now;
        }

        /// <summary>
        /// R-03 — each wave's planning phase is readied up for on its own. Without this the flags
        /// left over from the previous wave would make the next phase start already unanimous.
        /// </summary>
        private void ClearReadyFlags()
        {
            foreach (var player in State.Players)
            {
                if (!player.Ready)
                {
                    continue;
                }

                player.Ready = false;
                RecordChange(player.Id, "ready", true, false);
            }
        }

        /// <summary>
        /// R-03 / R-53 — every connected player has readied. Disconnected slots are excluded on
        /// both sides: they cannot block the start, and they cannot supply the only yes either.
        /// </summary>
        private bool EveryConnectedPlayerIsReady()
        {
            var connected = 0;
            foreach (var player in State.Players)
            {
                if (!player.Connected)
                {
                    continue;
                }

                if (!player.Ready)
                {
                    return false;
                }

                connected++;
            }

            return connected > 0;
        }

        private PlayerSlot FindPlayer(string playerId)
        {
            if (playerId == null)
            {
                return null;
            }

            foreach (var player in State.Players)
            {
                if (player.Id == playerId)
                {
                    return player;
                }
            }

            return null;
        }

        /// <summary>
        /// R-04 — adds to the running total for the wave currently being fought, starting a fresh
        /// total whenever the match has moved on to a different wave. Keying the accumulator by
        /// wave number rather than resetting it on a transition means no path into the next wave
        /// can forget to clear it.
        /// </summary>
        private void AccrueWaveBounty(int bounty)
        {
            if (_bountyEarnedForWave != State.Wave.Number)
            {
                _bountyEarnedForWave = State.Wave.Number;
                _bountyEarnedThisWave = 0;
            }

            _bountyEarnedThisWave += bounty;
        }

        /// <summary>R-04 — the current wave's takings; zero for a wave that has paid nothing yet.</summary>
        private int BountyEarnedThisWave() =>
            _bountyEarnedForWave == State.Wave.Number ? _bountyEarnedThisWave : 0;
    }
}
