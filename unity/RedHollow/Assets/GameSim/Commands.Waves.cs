using System.Collections.Generic;
using System.Linq;

namespace RedHollow.Sim
{
    /// <summary>
    /// R-05 / DEC-018 — the **partial** wave preview shown during planning.
    ///
    /// This type is the enforcement point for the negative half of R-05, not just the positive one.
    /// The entry points that will open are replicated so the shell can pulse them (R-63); monster
    /// types and counts are deliberately absent, and must stay absent from every nested field —
    /// replicating the <see cref="WaveSpec"/> wholesale and letting the UI ignore the composition
    /// is exactly the leak DEC-018 rules out, because a client that receives it can read it.
    /// </summary>
    public sealed class WavePreviewResult : ISimResult
    {
        /// <summary>The wave this preview describes — the one the coming combat phase will fight.</summary>
        public int Wave;

        /// <summary>
        /// R-14 — indices into <see cref="ColonyMap.EntryTunnels"/> that activate this wave, and
        /// nothing else. See <see cref="WaveSpec.ActiveTunnels"/> for why tunnels are addressed
        /// positionally.
        /// </summary>
        public readonly List<int> ActiveEntryTunnels = new List<int>();

        /// <summary>
        /// Two fields, both safe to replicate. The indices are copied out as plain numbers rather
        /// than handing over the list the wave table owns, so a client holds a snapshot of the
        /// breaches and no route back to the <see cref="WaveSpec"/> they were read from (DEC-018).
        /// </summary>
        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "wave", Wave },
            { "active_entry_tunnels", ActiveEntryTunnels.Cast<object>().ToList() },
        };
    }

    /// <summary>
    /// R-04 — the data behind the wave-complete interstitial (S5): what the wave just paid and how
    /// many civilians are still alive. The ~3s hold and the banner are the shell's; the sim owns
    /// only these numbers.
    ///
    /// <see cref="BountyEarned"/> is the total paid by every kill *in that wave* — not the shared
    /// pool (which carries over, R-20/G-016) and not the last kill alone.
    /// </summary>
    public sealed class WaveSummaryResult : ISimResult
    {
        /// <summary>The wave that just cleared.</summary>
        public int Wave;

        /// <summary>R-04 — scrip earned from kills during this wave only.</summary>
        public int BountyEarned;

        /// <summary>R-04 / R-02 — civilians still alive across the whole colony.</summary>
        public int CiviliansRemaining;

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "wave", Wave },
            { "bounty_earned", BountyEarned },
            { "civilians_remaining", CiviliansRemaining },
        };
    }
}
