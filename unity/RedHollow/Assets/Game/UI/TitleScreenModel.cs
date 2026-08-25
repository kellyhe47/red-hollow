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
        private readonly IProfileStore _profiles;

        private string _callsign;

        private AccountProfile _profile;

        private string _joinCodeInput;

        private string _joinError;

        public TitleScreenModel(IProfileStore profiles)
        {
            _profiles = profiles;
        }

        /// <summary>The callsign as typed. It doubles as the account id (R-44).</summary>
        public string Callsign => _callsign;

        /// <summary>Whether a profile has been loaded for the current callsign.</summary>
        public bool ProfileLoaded => _profile != null;

        /// <summary>R-41 — the loaded account's lifetime level (1 for a fresh callsign).</summary>
        public int Level => _profile == null ? 1 : _profile.Level;

        /// <summary>R-40 — the loaded account's lifetime XP (0 for a fresh callsign).</summary>
        public double LifetimeXp => _profile == null ? 0.0 : _profile.LifetimeXp;

        /// <summary>The join-code input as typed.</summary>
        public string JoinCodeInput => _joinCodeInput;

        /// <summary>
        /// The inline error under the code input, or null. Non-null after a failed join; cleared
        /// the moment the player edits the code. Copy is presentation and is not contract.
        /// </summary>
        public string JoinError => _joinError;

        /// <summary>Type a callsign: loads (or freshly creates, R-44) the profile behind it.</summary>
        public void SetCallsign(string callsign)
        {
            _callsign = callsign;

            // R-44 — the store answers a fresh account for an unknown callsign; there is no
            // "wrong callsign" outcome for this to error on.
            _profile = string.IsNullOrEmpty(callsign) ? null : _profiles.Load(callsign);
        }

        /// <summary>Edit the join code. Editing clears <see cref="JoinError"/>.</summary>
        public void SetJoinCodeInput(string code)
        {
            _joinCodeInput = code;

            // A stale error under a corrected code blames the wrong input.
            _joinError = null;
        }

        /// <summary>
        /// The adapter reports that the attempted join failed (bad/expired code, refused by the
        /// host). The model raises <see cref="JoinError"/>; the router stays on S1.
        /// </summary>
        public void NoteJoinFailed()
        {
            _joinError = "could not join with that code";
        }
    }
}
