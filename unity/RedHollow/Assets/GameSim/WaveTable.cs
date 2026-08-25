using System;
using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// One archetype's contribution to a wave (R-19). Composition is authored as
    /// {archetype, how many} rows rather than a flat monster list so the table stays readable and
    /// tunable by hand — the shell overrides these from a ScriptableObject, exactly as it does the
    /// R-17 stat rows, so rebalancing never edits rule code.
    /// </summary>
    public sealed class MonsterGroup
    {
        /// <summary>A <see cref="MonsterType"/> key. Must have a row in <see cref="MonsterCatalog"/>.</summary>
        public string MonsterType;

        /// <summary>How many of that archetype this wave sends.</summary>
        public int Count;
    }

    /// <summary>
    /// One wave of the campaign as *data* (R-19): what it sends and which breaches it sends them
    /// through (R-14).
    ///
    /// Composition and tunnels are deliberately separate lists. R-05 (DEC-018) replicates the
    /// tunnels to clients during planning and must never replicate the composition, so keeping the
    /// two apart makes the safe half addressable on its own instead of something that has to be
    /// carved out of a combined structure.
    /// </summary>
    public sealed class WaveSpec
    {
        /// <summary>1-based wave number, matching <see cref="WaveState.Number"/>.</summary>
        public int Number;

        /// <summary>R-19 — per-archetype counts. Host-only; never leaves the sim (R-05).</summary>
        public readonly List<MonsterGroup> Groups = new List<MonsterGroup>();

        /// <summary>
        /// R-14 — which of the map's fixed breaches are open this wave, as **indices into
        /// <see cref="ColonyMap.EntryTunnels"/>**. Index is the tunnel's identity: the map's tunnel
        /// list has no ids of its own, and the wave table is the only thing that needs to name one,
        /// so the table addresses them positionally rather than the map growing an id type it has no
        /// other use for. The shell resolves an index back to a world position through the same map
        /// it built the level from.
        /// </summary>
        public readonly List<int> ActiveTunnels = new List<int>();
    }

    /// <summary>
    /// The wave table (R-19): the whole campaign's composition, counts and active entry points, as
    /// config rather than as constants inside the wave rules. R-19 is explicitly playtest-tuned and
    /// deliberately unfixtured — the *shape* is contract, the numbers are taste.
    ///
    /// Mutable instance data for the same reason <see cref="ColonyMap"/> and
    /// <see cref="MonsterCatalog"/> are: a caller may edit the table it was handed, and every
    /// derived figure follows from the edit, so one match's tuning can never move another's.
    ///
    /// Ticket 004 (T-04) implements this; everything here is shape only.
    /// </summary>
    public sealed class WaveTable
    {
        /// <summary>Every defined wave, ordered by <see cref="WaveSpec.Number"/>.</summary>
        public readonly List<WaveSpec> Waves = new List<WaveSpec>();

        /// <summary>
        /// The spec for one wave. Throws naming the missing wave rather than returning a default —
        /// a silently empty wave would look like an instantly-cleared one.
        /// </summary>
        public WaveSpec For(int waveNumber) =>
            throw NotYet("wave-table lookup by wave number (R-19)");

        /// <summary>
        /// The shipped campaign for the v1 colony: <see cref="SimConfig.TotalWaves"/> waves ramping
        /// from a small Shambler-only opener through Behemoths from wave 5 to a final wave pouring
        /// out of all four breaches (R-19 / R-14).
        /// </summary>
        public static WaveTable V1() =>
            throw NotYet("the shipped 10-wave campaign table (R-19)");

        private static NotImplementedException NotYet(string behavior) =>
            new NotImplementedException("T-04 not implemented: " + behavior);
    }
}
