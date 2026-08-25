using System;
using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 017 (T-17) owns this half of <see cref="MatchSim"/>: turning the wave table (R-19)
    /// and the map's entry tunnels (R-14) into live <see cref="Monster"/> entities.
    ///
    /// Nothing else in the sim creates a monster. <see cref="WaveTable"/> says what a wave is
    /// made of, <see cref="SimConfig.Monsters"/> says what each archetype is worth (R-17), and
    /// <see cref="ColonyMap.EntryTunnels"/> says where the breaches are — but until this file
    /// nothing assembled the three into <see cref="MatchState.Monsters"/>, so a match could never
    /// contain a monster and no wave could be fought. This file is the seam that closes that gap.
    ///
    /// It grades no fixture: G-010/G-011/G-012 grade what happens when a monster *dies*, and every
    /// fixture that needs a monster hands one to the loader ready-made. The contract therefore
    /// lives entirely in T17_SpawningTests.
    ///
    /// The shared core — fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>
        /// Prefix for ids minted by <see cref="SpawnWave"/>. Distinct from the `pl_` prefix
        /// <see cref="PurchasePlacement"/> uses for the same reason that one exists: entity ids
        /// share one namespace in a log and a spawned monster must never read as a placeable.
        /// </summary>
        private const string SpawnedMonsterIdPrefix = "mon_";

        /// <summary>
        /// How many ids <see cref="NextMonsterId"/> has minted for this match. Per instance and
        /// never reset per wave (R-54): a per-wave counter would name wave 2's first monster the
        /// same as wave 1's, and because <see cref="MatchState.Monsters"/> is keyed by id the
        /// second spawn would silently overwrite the first's entities. Per instance rather than
        /// static so two matches built the same way spawn the same wave identically.
        /// </summary>
        private int _monstersSpawned;

        /// <summary>
        /// R-19 / R-14 / R-17 / R-54 / B-013. Spawn one wave into the world.
        ///
        /// The composition comes from <see cref="WaveTable"/>, the stats from
        /// <see cref="SimConfig.Monsters"/> and the positions from the tunnels the wave marks
        /// active, resolved through <see cref="ColonyMap.EntryTunnels"/>. Every id created joins
        /// <see cref="WaveState.LivingMonsterIds"/>, which is the roster
        /// <see cref="RecordMonsterKill"/> counts down to complete the wave (R-02 / G-010) — a
        /// monster missing from it can never be killed off the roster, so the wave never completes
        /// and the match hangs in combat forever.
        ///
        /// The wave number is a parameter rather than a read of <see cref="WaveState.Number"/>:
        /// the host names the wave it is opening, and a spawn that trusted match state could not
        /// be asked about a wave the table does not define without first corrupting the match.
        /// Nothing here writes the wave counter either — <see cref="BeginPlanningPhase"/> owns it
        /// (R-03), and two places advancing it is two places to get it wrong.
        ///
        /// Determinism (R-54) needs no RNG and takes none: the table is walked in authored order,
        /// each group in turn, and the open breaches are dealt round-robin, so the same table, map
        /// and catalog always produce the same ids, archetypes and positions in the same order.
        /// <see cref="WaveSpawnResult.MonsterIds"/> is the ordered surface that states it —
        /// <see cref="MatchState.Monsters"/> is a dictionary and its enumeration order is not a
        /// promise.
        ///
        /// Nothing mutates until every lookup has succeeded, the same way
        /// <see cref="PurchasePlacement"/> passes all its gates before any money moves. A wave is
        /// therefore all-or-nothing: an archetype the R-17 catalog has no row for aborts the whole
        /// spawn rather than landing its valid groups, because a partial wave is a balance lie the
        /// team cannot see — it looks like a cleared-early wave, pays less bounty, and leaves the
        /// campaign's difficulty curve quietly wrong from that wave on.
        ///
        /// Sad paths, decided here:
        ///  - a match already won or lost spawns nothing and reports an empty wave. R-02 makes
        ///    defeat immediate, so a spawn already in flight from the host loop must not
        ///    repopulate a finished match — and on the final wave it must not manufacture a wave
        ///    11 out of a match that has already been won (R-01). Refused rather than thrown: a
        ///    host-loop command racing the end of the match is ordinary, not a bug;
        ///  - a wave the table does not define throws out of <see cref="WaveTable.For"/>, naming
        ///    the missing wave, exactly as <see cref="PreviewUpcomingWave"/> lets it. There is no
        ///    honest <see cref="WaveSpawnResult"/> for a wave that does not exist, and a silently
        ///    empty one would read as an instantly-cleared wave;
        ///  - an archetype with no R-17 row throws out of <see cref="MonsterCatalog.StatsFor"/>,
        ///    which is what that method exists to make loud: the alternative is a zero-HP monster
        ///    that dies to its own spawn and pays a bounty nobody tuned;
        ///  - a wave that opens no breach, or names one the map does not have, throws — R-14's
        ///    tunnels are the only place a monster can enter from, so there is nowhere to put it.
        /// </summary>
        /// <param name="waveNumber">The wave to spawn, matching <see cref="WaveSpec.Number"/>.</param>
        public WaveSpawnResult SpawnWave(int waveNumber)
        {
            BeginCommand();

            var result = new WaveSpawnResult { Wave = waveNumber };

            // R-01 / R-02 — a finished match fights no further wave.
            if (State.IsOver)
            {
                return Finish(result);
            }

            var spec = WaveTable.For(waveNumber);

            // Every lookup first, every mutation after. StatsFor throws for an archetype the R-17
            // catalog does not configure, and it throws here — before a single monster exists —
            // so the world is left exactly as it was found rather than half-populated.
            var plan = new List<(MonsterGroup Group, MonsterStats Stats)>();
            var headcount = 0;
            foreach (var group in spec.Groups)
            {
                if (group == null || group.Count <= 0)
                {
                    continue;
                }

                plan.Add((group, _config.Monsters.StatsFor(group.MonsterType)));
                headcount += group.Count;
            }

            if (headcount == 0)
            {
                // An authored wave that sends nobody. Nothing to place, so the breaches are never
                // resolved: a wave with no monsters has no need of a tunnel to fail on.
                return Finish(result);
            }

            var breaches = ResolveActiveBreaches(spec);

            var monstersBefore = State.Monsters.Count;
            var livingBefore = State.Wave.LivingMonsterIds.Count;

            foreach (var entry in plan)
            {
                for (var i = 0; i < entry.Group.Count; i++)
                {
                    // R-14 — dealt round-robin across the wave's open breaches, in spawn order.
                    // R-14 and R-19 say which breaches open and nothing about how a wave is split
                    // between them, so this is a decision rather than a rule: round-robin spreads
                    // every archetype across every open breach, which keeps a multi-breach wave
                    // from degenerating into one lane of Behemoths and three of Shamblers.
                    var monster = new Monster
                    {
                        Id = NextMonsterId(),
                        Type = entry.Group.MonsterType,
                        Pos = breaches[result.MonsterIds.Count % breaches.Count],

                        // R-17 / R-16 — every number comes off the catalog row. A literal written
                        // here would look right against the PRD and leave the shell's
                        // ScriptableObject with nothing to turn.
                        Hp = entry.Stats.MaxHp,
                        BaseSpeed = entry.Stats.MoveSpeed,

                        // R-31 / G-018 — a monster arrives moving at its base speed. The lasso
                        // *multiplies* CurrentSpeed and TickStatusEffects restores it to
                        // BaseSpeed, so one that spawned pre-slowed would speed up the first time
                        // it was lassoed and released.
                        CurrentSpeed = entry.Stats.MoveSpeed,
                        Alive = true,
                    };

                    State.Monsters[monster.Id] = monster;
                    State.Wave.LivingMonsterIds.Add(monster.Id);
                    result.MonsterIds.Add(monster.Id);
                }
            }

            // Replicated as populations rather than as one delta per monster, the way G-013 states
            // a placeable appearing: the shell learns *which* monsters from the result, which it
            // receives with the same observation.
            RecordChange("monsters", "count", monstersBefore, State.Monsters.Count);
            RecordChange("wave", "living_monsters", livingBefore, State.Wave.LivingMonsterIds.Count);

            Emit("wave_spawned", new Dictionary<string, object>
            {
                { "wave", waveNumber },
                { "monster_count", result.MonsterIds.Count },
            });

            return Finish(result);
        }

        // ---- helpers ---------------------------------------------------------------------------

        /// <summary>
        /// R-14 — the world positions this wave's monsters may enter from, in the order the table
        /// authored them.
        ///
        /// <see cref="WaveSpec.ActiveTunnels"/> holds 0-based indices into
        /// <see cref="ColonyMap.EntryTunnels"/> rather than positions: index is a tunnel's only
        /// identity, and resolving it against the match's own map is what keeps a spawn on the map
        /// the level was built from instead of on a set of coordinates rule code remembers.
        ///
        /// Both failure modes throw rather than silently narrowing the wave to the breaches that
        /// did resolve: a wave entering from fewer breaches than the planning preview promised
        /// (R-05 / DEC-018) sends the team to defend a breach nothing comes out of, which is worse
        /// than telling them nothing.
        /// </summary>
        private List<Vec2> ResolveActiveBreaches(WaveSpec spec)
        {
            var tunnels = ColonyMap.EntryTunnels;
            var breaches = new List<Vec2>();

            foreach (var index in spec.ActiveTunnels)
            {
                if (index < 0 || index >= tunnels.Count)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(spec),
                        "wave " + spec.Number + " activates entry tunnel " + index + ", but this "
                        + "colony has " + tunnels.Count + " (R-14); the wave table and the map "
                        + "this match is played on disagree");
                }

                breaches.Add(tunnels[index]);
            }

            if (breaches.Count == 0)
            {
                throw new InvalidOperationException(
                    "wave " + spec.Number + " sends monsters but opens no entry tunnel (R-14); "
                    + "there is nowhere on this map for them to come from");
            }

            return breaches;
        }

        /// <summary>
        /// A fresh id for a spawned monster. Ids are the host's to mint (R-51), and this one is
        /// minted from a per-match counter that never rewinds, so no two monsters in a match ever
        /// share a key however many waves it runs. The loop makes it collision-proof against ids a
        /// fixture or the shell authored, exactly as <see cref="NextPlaceableId"/> is.
        ///
        /// The format is deliberately nobody's contract: no fixture and no requirement names one,
        /// and G-010's `m1` is a fixture's own input rather than a scheme the sim owes anybody.
        /// </summary>
        private string NextMonsterId()
        {
            while (true)
            {
                _monstersSpawned++;
                var id = SpawnedMonsterIdPrefix + _monstersSpawned;
                if (!State.Monsters.ContainsKey(id))
                {
                    return id;
                }
            }
        }
    }
}
