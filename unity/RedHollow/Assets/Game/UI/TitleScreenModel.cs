using System;
using RedHollow.Sim;

namespace RedHollow.Game.UI
{
    /// <summary>
    /// Ticket 012 (T-12) — S1 Title / Join (R-60).
    ///
    /// The callsign IS the account (R-44: v1, no password): entering one loads the server-side
    /// profile keyed to it and shows lifetime level + XP; an unknown callsign is simply a fresh
    /// account, never an error. The join-code path's one state is the wireframe's: a bad or
    /// expired code puts an inline error under the input and stays on S1.
    /// </summary>
    public sealed class TitleScreenModel
    {
        public TitleScreenModel(IProfileStore profiles) =>
            throw new NotImplementedException("T-12 / R-60: the title screen");

        /// <summary>The callsign as typed. It doubles as the account id (R-44).</summary>
        public string Callsign =>
            throw new NotImplementedException("T-12 / R-44: the callsign input");

        /// <summary>Whether a profile has been loaded for the current callsign.</summary>
        public bool ProfileLoaded =>
            throw new NotImplementedException("T-12 / R-44: profile loaded");

        /// <summary>R-41 — the loaded account's lifetime level (1 for a fresh callsign).</summary>
        public int Level =>
            throw new NotImplementedException("T-12 / R-41: lifetime level");

        /// <summary>R-40 — the loaded account's lifetime XP (0 for a fresh callsign).</summary>
        public double LifetimeXp =>
            throw new NotImplementedException("T-12 / R-40: lifetime XP");

        /// <summary>The join-code input as typed.</summary>
        public string JoinCodeInput =>
            throw new NotImplementedException("T-12: the join-code input");

        /// <summary>
        /// The inline error under the code input, or null. Non-null after a failed join; cleared
        /// the moment the player edits the code. Copy is presentation and is not contract.
        /// </summary>
        public string JoinError =>
            throw new NotImplementedException("T-12: bad-join-code inline error");

        /// <summary>Type a callsign: loads (or freshly creates, R-44) the profile behind it.</summary>
        public void SetCallsign(string callsign) =>
            throw new NotImplementedException("T-12 / R-44: set the callsign");

        /// <summary>Edit the join code. Editing clears <see cref="JoinError"/>.</summary>
        public void SetJoinCodeInput(string code) =>
            throw new NotImplementedException("T-12: edit the join code");

        /// <summary>
        /// The adapter reports that the attempted join failed (bad/expired code, refused by the
        /// host). The model raises <see cref="JoinError"/>; the router stays on S1.
        /// </summary>
        public void NoteJoinFailed() =>
            throw new NotImplementedException("T-12: join failed, stay on S1");
    }
}
