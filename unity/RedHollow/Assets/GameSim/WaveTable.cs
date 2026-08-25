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
    /// </summary>
    public sealed class WaveTable
    {
        /// <summary>Every defined wave, ordered by <see cref="WaveSpec.Number"/>.</summary>
        public readonly List<WaveSpec> Waves = new List<WaveSpec>();

        /// <summary>
        /// The spec for one wave. Throws naming the missing wave rather than returning a default —
        /// a silently empty wave would look like an instantly-cleared one.
        /// </summary>
        public WaveSpec For(int waveNumber)
        {
            foreach (var wave in Waves)
            {
                if (wave.Number == waveNumber)
                {
                    return wave;
                }
            }

            throw new KeyNotFoundException(
                "no wave " + waveNumber + " in this wave table (R-19); the campaign defines "
                + Waves.Count + " wave(s)");
        }

        /// <summary>
        /// The shipped campaign for the v1 colony: <see cref="SimConfig.TotalWaves"/> waves ramping
        /// from a small Shambler-only opener through Behemoths from wave 5 to a final wave pouring
        /// out of all four breaches (R-19 / R-14).
        ///
        /// The numbers below are a playtest first pass, not contract — R-19 says so explicitly, and
        /// the only fixed points are the ones the PRD states: wave 1 is ~6 Shamblers from a single
        /// breach, Behemoths first appear at wave 5, and wave 10 is ~30 mixed monsters through all
        /// four tunnels. Between those the curve grows the headcount, layers one new archetype in at
        /// a time (Ravagers at 2, Spitters at 4, Behemoths at 5, Burrowers at 6) and rotates which
        /// breaches open so R-14's "varies per wave" is a real decision the team has to re-read each
        /// planning phase rather than a constant four-way siege.
        ///
        /// Built fresh on every call, never handed out from a static: a caller tuning the table it
        /// was given must not move any other match's numbers (the bug <see cref="MonsterCatalog"/>
        /// avoids by seeding per instance).
        /// </summary>
        public static WaveTable V1()
        {
            var table = new WaveTable();

            // Wave 1 — the tutorial breach: one tunnel, one archetype, six of them (R-19).
            table.Add(1, new[] { 0 }, (MonsterType.Shambler, 6));

            // Ravagers arrive: fast, fragile, and now from two directions at once.
            table.Add(2, new[] { 0, 1 }, (MonsterType.Shambler, 8), (MonsterType.Ravager, 2));

            // Same shape, more Ravagers, and the pair of breaches moves — the team cannot re-use
            // wave 2's barricade line unchanged (R-14).
            table.Add(3, new[] { 1, 2 }, (MonsterType.Shambler, 8), (MonsterType.Ravager, 4));

            // Spitters join and the front widens to three, which is the last wave before Behemoths.
            table.Add(4, new[] { 0, 2, 3 },
                (MonsterType.Shambler, 8), (MonsterType.Ravager, 4), (MonsterType.Spitter, 2));

            // R-19 — the first Bull Behemoth. One is enough: at 400 HP it is the wave.
            table.Add(5, new[] { 0, 1, 2 },
                (MonsterType.Shambler, 8), (MonsterType.Ravager, 4), (MonsterType.Spitter, 3),
                (MonsterType.BullBehemoth, 1));

            // Burrowers arrive (DEC-007: they tunnel past barricades), so the wall stops being a
            // complete answer exactly when the team has learned to rely on it.
            table.Add(6, new[] { 1, 2, 3 },
                (MonsterType.Shambler, 10), (MonsterType.Ravager, 5), (MonsterType.Spitter, 3),
                (MonsterType.Burrower, 1), (MonsterType.BullBehemoth, 1));

            table.Add(7, new[] { 0, 1, 3 },
                (MonsterType.Shambler, 10), (MonsterType.Ravager, 6), (MonsterType.Spitter, 4),
                (MonsterType.Burrower, 2), (MonsterType.BullBehemoth, 1));

            // First all-four-breach wave — a rehearsal for the finale while the counts are still
            // survivable.
            table.Add(8, new[] { 0, 1, 2, 3 },
                (MonsterType.Shambler, 10), (MonsterType.Ravager, 6), (MonsterType.Spitter, 4),
                (MonsterType.Burrower, 3), (MonsterType.BullBehemoth, 1));

            // Back to three breaches but two Behemoths: pressure moves from width to weight.
            table.Add(9, new[] { 0, 2, 3 },
                (MonsterType.Shambler, 12), (MonsterType.Ravager, 7), (MonsterType.Spitter, 5),
                (MonsterType.Burrower, 3), (MonsterType.BullBehemoth, 2));

            // R-19 — the finale: 30 monsters of every archetype, out of all four tunnels.
            table.Add(10, new[] { 0, 1, 2, 3 },
                (MonsterType.Shambler, 12), (MonsterType.Ravager, 8), (MonsterType.Spitter, 5),
                (MonsterType.Burrower, 3), (MonsterType.BullBehemoth, 2));

            return table;
        }

        /// <summary>
        /// Appends one authored row. Private because it exists only to keep <see cref="V1"/>
        /// readable as a balance table — callers build a <see cref="WaveSpec"/> directly.
        /// </summary>
        private void Add(int number, int[] activeTunnels, params (string Type, int Count)[] groups)
        {
            var spec = new WaveSpec { Number = number };
            spec.ActiveTunnels.AddRange(activeTunnels);
            foreach (var group in groups)
            {
                spec.Groups.Add(new MonsterGroup { MonsterType = group.Type, Count = group.Count });
            }

            Waves.Add(spec);
        }
    }
}
