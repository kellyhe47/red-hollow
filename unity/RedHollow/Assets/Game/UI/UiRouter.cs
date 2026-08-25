using RedHollow.Game.Net;
using RedHollow.Sim;

namespace RedHollow.Game.UI
{
    /// <summary>
    /// Ticket 012 (T-12) — which wireframe screen the player is looking at (R-60).
    ///
    /// One value per screen in docs/ui-wireframes.html, and nothing else: overlays (ESC menu,
    /// level-up picker, dead-hero spectate) are NOT screens — they sit on top of one and the sim
    /// never pauses for any of them (R-55 / R-62).
    /// </summary>
    public enum UiScreen
    {
        /// <summary>S1 — Title / Join.</summary>
        Title,

        /// <summary>S2 — Lobby: class pick + ready.</summary>
        Lobby,

        /// <summary>S3 — Planning phase.</summary>
        Planning,

        /// <summary>S4 — Combat phase.</summary>
        Combat,

        /// <summary>S5 — Wave complete interstitial (~3s hold, the shell's own R-04 schedule).</summary>
        WaveInterstitial,

        /// <summary>S6 — Victory.</summary>
        Victory,

        /// <summary>S7 — Defeat.</summary>
        Defeat,
    }

    /// <summary>
    /// Ticket 012 (T-12) — the screen router: S1 → S2 → S3 → S4 → S5(→ S3 …) → S6/S7, rematch back
    /// to S2 (DEC-RUN-11) and host-disconnect back to S1 with an error (DEC-RUN-10). Owns R-60's
    /// flow; every screen's *contents* live in the per-screen models beside this class.
    ///
    /// Plain C#, read-only over the session and the match: it derives the screen from
    /// <see cref="NetSession.Phase"/>, <see cref="RedHollow.Sim.MatchState.Phase"/> and
    /// <see cref="RedHollow.Sim.MatchState.Status"/> — two different fields that BOTH read the
    /// literal "combat" while a match runs, which is exactly why the mapping needs a test.
    /// </summary>
    public sealed class UiRouter
    {
        /// <summary>The wireframe's "~3s", declared once here (R-04).</summary>
        private const double InterstitialHoldSeconds = 3.0;

        private readonly NetSession _session;

        private UiScreen _screen = UiScreen.Title;

        private string _titleError;

        /// <summary>R-04 — whether S5 is holding, and until when (inclusive, in sim time).</summary>
        private bool _interstitialPending;

        private double _interstitialUntil;

        public UiRouter(NetSession session)
        {
            _session = session;
        }

        /// <summary>The screen the player is on, as of the last <see cref="Update"/>.</summary>
        public UiScreen Screen => _screen;

        /// <summary>
        /// R-04 — how long S5 holds before falling back to S3. The wireframe says "~3s"; the exact
        /// value is this class's schedule decision, so it is exposed rather than guessed at by
        /// callers. Positive, and shorter than R-03's planning duration.
        /// </summary>
        public double InterstitialSeconds => InterstitialHoldSeconds;

        /// <summary>
        /// R-53 / DEC-RUN-10 — the error S1 shows after the host left, or null when the player is
        /// on S1 for any ordinary reason. Copy is presentation; non-null-ness is the contract.
        /// </summary>
        public string TitleError => _titleError;

        /// <summary>R-55 — whether the ESC menu overlay is up. Never a pause, never a screen.</summary>
        public bool EscMenuOpen => _session.IsOverlayOpen;

        /// <summary>Re-derive <see cref="Screen"/> from the session and the live match.</summary>
        public void Update()
        {
            switch (_session.Phase)
            {
                case NetSessionPhase.Lobby:
                    _screen = UiScreen.Lobby;
                    _titleError = null;
                    _interstitialPending = false;
                    break;

                case NetSessionPhase.InMatch:
                    _screen = ScreenForLiveMatch();
                    _titleError = null;
                    break;

                case NetSessionPhase.PostMatch:
                    _screen = ScreenForFinishedMatch();
                    _titleError = null;
                    _interstitialPending = false;
                    break;

                case NetSessionPhase.Ended:
                    // DEC-RUN-10 — derived from the SESSION's end, never from the match status,
                    // which deliberately stays in-progress for an abandoned match.
                    _screen = UiScreen.Title;
                    _titleError = LatestHostDisconnectText();
                    _interstitialPending = false;
                    break;

                default:
                    _screen = UiScreen.Title;
                    _titleError = null;
                    _interstitialPending = false;
                    break;
            }
        }

        /// <summary>
        /// R-04 — the router listens for `wave_complete` to enter S5; every other transition is
        /// state-derived. Events it does not care about are ignored.
        /// </summary>
        public void OnSimEvent(SimEvent evt)
        {
            if (evt == null || evt.Type != "wave_complete")
            {
                return;
            }

            var match = _session.Match;
            if (match == null)
            {
                return;
            }

            _interstitialPending = true;
            _interstitialUntil = match.Clock.ElapsedSeconds + InterstitialHoldSeconds;
        }

        /// <summary>
        /// R-55 — open or close the ESC menu. Forwards to
        /// <see cref="NetSession.SetOverlayOpen"/>; it must never touch the sim, the clock or
        /// <c>UnityEngine.Time.timeScale</c>.
        /// </summary>
        public void SetEscMenuOpen(bool open)
        {
            _session.SetOverlayOpen(open);
        }

        // ---- helpers --------------------------------------------------------------------------

        private UiScreen ScreenForLiveMatch()
        {
            var match = _session.Match;
            if (match == null)
            {
                return UiScreen.Lobby;
            }

            if (_interstitialPending)
            {
                // Deadlines are inclusive repo-wide: at now >= until the hold is over.
                if (match.Clock.ElapsedSeconds >= _interstitialUntil)
                {
                    _interstitialPending = false;
                }
                else
                {
                    return UiScreen.WaveInterstitial;
                }
            }

            // Phase, never status: a live match's STATUS also spells "combat".
            return match.State.Phase == MatchPhase.Planning ? UiScreen.Planning : UiScreen.Combat;
        }

        private UiScreen ScreenForFinishedMatch()
        {
            var match = _session.Match;
            if (match == null)
            {
                return UiScreen.Lobby;
            }

            // Status, never phase: a won match's PHASE spells "combat" forever.
            return match.State.Status == MatchStatus.Victory ? UiScreen.Victory : UiScreen.Defeat;
        }

        private string LatestHostDisconnectText()
        {
            var notices = _session.Notices;
            for (var i = notices.Count - 1; i >= 0; i--)
            {
                if (notices[i].Kind == SessionNoticeKind.HostDisconnected
                    && !string.IsNullOrEmpty(notices[i].Text))
                {
                    return notices[i].Text;
                }
            }

            return "the host left; the match has ended";
        }
    }
}
