using System;
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
        public UiRouter(NetSession session) =>
            throw new NotImplementedException("T-12 / R-60: the screen router");

        /// <summary>The screen the player is on, as of the last <see cref="Update"/>.</summary>
        public UiScreen Screen =>
            throw new NotImplementedException("T-12 / R-60: which screen");

        /// <summary>
        /// R-04 — how long S5 holds before falling back to S3. The wireframe says "~3s"; the exact
        /// value is this class's schedule decision, so it is exposed rather than guessed at by
        /// callers. Positive, and shorter than R-03's planning duration.
        /// </summary>
        public double InterstitialSeconds =>
            throw new NotImplementedException("T-12 / R-04: the S5 hold");

        /// <summary>
        /// R-53 / DEC-RUN-10 — the error S1 shows after the host left, or null when the player is
        /// on S1 for any ordinary reason. Copy is presentation; non-null-ness is the contract.
        /// </summary>
        public string TitleError =>
            throw new NotImplementedException("T-12 / R-53: host-disconnect error on S1");

        /// <summary>R-55 — whether the ESC menu overlay is up. Never a pause, never a screen.</summary>
        public bool EscMenuOpen =>
            throw new NotImplementedException("T-12 / R-55: the ESC overlay");

        /// <summary>Re-derive <see cref="Screen"/> from the session and the live match.</summary>
        public void Update() =>
            throw new NotImplementedException("T-12 / R-60: derive the screen");

        /// <summary>
        /// R-04 — the router listens for `wave_complete` to enter S5; every other transition is
        /// state-derived. Events it does not care about are ignored.
        /// </summary>
        public void OnSimEvent(SimEvent evt) =>
            throw new NotImplementedException("T-12 / R-04: the interstitial trigger");

        /// <summary>
        /// R-55 — open or close the ESC menu. Forwards to
        /// <see cref="NetSession.SetOverlayOpen"/>; it must never touch the sim, the clock or
        /// <c>UnityEngine.Time.timeScale</c>.
        /// </summary>
        public void SetEscMenuOpen(bool open) =>
            throw new NotImplementedException("T-12 / R-55: the non-pausing ESC menu");
    }
}
