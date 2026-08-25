using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Ticket 017 (T-17): wave spawning — the missing bridge between the three pieces that already
    /// exist and a match that can actually be played.
    ///
    /// Nothing in the sim creates a <see cref="Monster"/>. <see cref="WaveTable"/> describes each
    /// wave's composition (R-19), <see cref="MonsterCatalog"/> holds the R-17 stats, and
    /// <see cref="ColonyMap.EntryTunnels"/> holds the breach positions (R-14) — but no code
    /// assembles the three into <see cref="MatchState.Monsters"/>. Every golden fixture hands the
    /// loader a ready-made monster, so no fixture can see the hole: G-010/G-011/G-012 grade what
    /// happens when a monster *dies*, and ticket 004 covered the wave table only as *config*.
    ///
    /// This ticket therefore grades no fixture and the whole contract lives here. Because the PRD
    /// is silent on most of the mechanism, these tests assert relationships rather than values:
    ///
    ///   * composition is read back out of the <see cref="WaveSpec"/> under test, never written as
    ///     a literal — R-19 says the table is playtest-tuned and explicitly not fixture-locked, so
    ///     a test that hardcoded "wave 1 spawns 6" would freeze balance data as contract;
    ///   * stats are proven to come from <see cref="SimConfig.Monsters"/> by tuning a row away from
    ///     its PRD value and demanding the spawn follow it (R-16: rebalancing edits config, never
    ///     rule code);
    ///   * placement asserts *membership* in the wave's active tunnels, not distribution across
    ///     them — R-14 says which breaches open, nothing about how a wave is split between them;
    ///   * ids assert uniqueness and roster membership, never a naming scheme;
    ///   * determinism (R-54) is asserted as "two identical sims agree", with no RNG pinned — this
    ///     ticket added no seed seam, because the property is expressible without one and inventing
    ///     one would ship a guess about the implementation as spec;
    ///   * the observation surface asserts presence and shape only. No fixture pins a spawn event's
    ///     wording or its state-change list, so neither is named here;
    ///   * sad paths assert non-corruption, not error strings — this repo already answers unknown
    ///     entities three different ways (throw, refuse, silent no-op) and T-17 is not the ticket
    ///     that unifies them.
    ///
    /// Scenarios are built straight from production types; the fixture JSON loader is the golden
    /// adapter's contract with eval/golden, not a test fixture builder.
    /// </summary>
    [TestFixture]
    public class T17_SpawningTests
    {
        private const double Tolerance = 1e-9;

        // ---- R-19: the wave that gets spawned is the wave the table describes ----------------------

        /// <summary>
        /// The core of the ticket: spawning wave N produces exactly the monsters its
        /// <see cref="WaveSpec"/> lists — the right total, the right count per archetype, and no
        /// archetype the wave never asked for.
        ///
        /// Expectations are derived from the spec the sim itself is holding rather than written
        /// down here, so this stays true across a rebalance (R-19 is explicitly tunable) while
        /// still failing any implementation that spawns a fixed number, spawns one group and stops,
        /// or spawns the first wave regardless of which was asked for.
        ///
        /// The opener and the finale are the two rows because they differ in every way that matters:
        /// one group versus five, one tunnel versus four.
        /// </summary>
        [TestCase(1, TestName = "spawn_wave_1_the_single_archetype_opener")]
        [TestCase(5, TestName = "spawn_wave_5_the_first_behemoth_wave")]
        [TestCase(10, TestName = "spawn_wave_10_the_all_archetype_finale")]
        public void Spawning_a_wave_creates_exactly_the_monsters_its_spec_describes(int wave)
        {
            var sim = SpawnSim(out var state);
            var spec = sim.WaveTable.For(wave);
            var expectedTotal = spec.Groups.Sum(g => g.Count);

            var result = sim.SpawnWave(wave);

            Assert.Multiple(() =>
            {
                Assert.That(result.Wave, Is.EqualTo(wave), "the result answers for the wave it was asked for");
                Assert.That(result.MonsterIds, Has.Count.EqualTo(expectedTotal),
                    "R-19: wave " + wave + " is " + expectedTotal + " monsters");
                Assert.That(state.Monsters, Has.Count.EqualTo(expectedTotal),
                    "every monster the wave sends must exist in the world, not just in the result");

                foreach (var group in spec.Groups)
                {
                    Assert.That(state.Monsters.Values.Count(m => m.Type == group.MonsterType),
                        Is.EqualTo(group.Count),
                        "R-19: wave " + wave + " sends " + group.Count + " x " + group.MonsterType);
                }

                Assert.That(state.Monsters.Values.Select(m => m.Type).Distinct(),
                    Is.EquivalentTo(spec.Groups.Select(g => g.MonsterType).Distinct()),
                    "an archetype the wave table never named must not turn up in the world");
            });
        }

        /// <summary>
        /// The same rule against a table nobody shipped. R-19 puts the campaign in config precisely
        /// so it can be replaced — the Unity shell authors it from a ScriptableObject — so an
        /// implementation that reads <see cref="WaveTable.V1"/> directly, or that knows the shipped
        /// curve, must fail here while passing the case above.
        ///
        /// The composition is deliberately unlike anything V1 ships: a wave whose largest group is
        /// Burrowers, at a wave number the shipped table gives an entirely different shape.
        /// </summary>
        [Test]
        public void Spawning_reads_the_wave_table_the_match_was_given()
        {
            var table = TableWith(
                Spec(7, new[] { 0 }, (MonsterType.Burrower, 5), (MonsterType.Spitter, 2)));

            var sim = SpawnSim(out var state, table: table);
            var result = sim.SpawnWave(7);

            Assert.Multiple(() =>
            {
                Assert.That(result.MonsterIds, Has.Count.EqualTo(7), "5 Burrowers + 2 Spitters");
                Assert.That(state.Monsters.Values.Count(m => m.Type == MonsterType.Burrower), Is.EqualTo(5));
                Assert.That(state.Monsters.Values.Count(m => m.Type == MonsterType.Spitter), Is.EqualTo(2));
                Assert.That(state.Monsters.Values.Any(m => m.Type == MonsterType.Shambler), Is.False,
                    "the shipped wave 7 is Shambler-heavy; this match is not playing the shipped table");
            });
        }

        // ---- R-17 / R-16: a spawned monster carries its configured stats ---------------------------

        /// <summary>
        /// R-17's stats must come from <see cref="SimConfig.Monsters"/>, and R-16 requires that
        /// rebalancing them is a config edit rather than a code edit. A sim that writes 60 HP and
        /// speed 2.0 into a Shambler from rule code would look perfect against the PRD and would
        /// leave the shell's ScriptableObject with nothing to turn — so the catalog row here is
        /// tuned to values the PRD never mentions, and the spawn has to follow it.
        ///
        /// Two invariants ride along that the criteria call out by name:
        ///  - <see cref="Monster.Alive"/> starts true. A wave of corpses clears itself;
        ///  - <see cref="Monster.CurrentSpeed"/> starts equal to <see cref="Monster.BaseSpeed"/>.
        ///    G-018's lasso *multiplies* CurrentSpeed and
        ///    <see cref="MatchSim.TickStatusEffects"/> restores it back to BaseSpeed, so a monster
        ///    that spawned pre-slowed would speed up the first time it was lassoed and released.
        /// </summary>
        [Test]
        public void Spawned_monsters_carry_their_configured_catalog_stats()
        {
            var config = TunedShamblerConfig();
            var table = TableWith(Spec(1, new[] { 0 }, (MonsterType.Shambler, 3)));
            var sim = SpawnSim(out var state, config, table, MapWithTunnels(new Vec2(-101.0, 17.0)), totalWaves: 5);

            sim.SpawnWave(1);

            Assert.That(state.Monsters, Is.Not.Empty, "nothing spawned, so there is nothing to check stats on");
            Assert.Multiple(() =>
            {
                foreach (var monster in state.Monsters.Values)
                {
                    Assert.That(monster.Hp, Is.EqualTo(TunedMaxHp).Within(Tolerance),
                        "R-17: a monster spawns on its configured MaxHp, not a number written into rule code");
                    Assert.That(monster.BaseSpeed, Is.EqualTo(TunedMoveSpeed).Within(Tolerance),
                        "R-17: base speed comes from the catalog row");
                    Assert.That(monster.CurrentSpeed, Is.EqualTo(monster.BaseSpeed).Within(Tolerance),
                        "R-31/G-018: a monster spawns moving at its base speed — spawning pre-slowed "
                        + "would make the first lasso release a speed *boost*");
                    Assert.That(monster.Alive, Is.True, "a spawned monster is alive");
                    Assert.That(monster.StatusEffects, Is.Empty, "a monster spawns carrying no effects");
                    Assert.That(monster.Type, Is.EqualTo(MonsterType.Shambler));
                }
            });
        }

        /// <summary>
        /// The other half of "not invented numbers", and the only way this ticket can speak about
        /// R-17's *damage* column at all: <see cref="Monster"/> has no damage field — attack damage
        /// arrives per-hit on the request (<see cref="HotspotAttackRequest.Damage"/>) — so there is
        /// nothing to read back. What can still be checked is that the spawn does not smuggle the
        /// PRD's Shambler row in anywhere while the catalog says something else.
        ///
        /// Every number reachable from the spawned monsters and from the spawn's own observation is
        /// swept for the PRD defaults this config replaced (60 HP, 10 damage, speed 2.0, bounty 10).
        /// The scenario is built so none of those can appear innocently: 3 monsters, wave 1, a
        /// 5-wave campaign and a tunnel at (-101, 17).
        /// </summary>
        [Test]
        public void Spawning_never_falls_back_to_the_PRD_stat_row_the_config_replaced()
        {
            var config = TunedShamblerConfig();
            var table = TableWith(Spec(1, new[] { 0 }, (MonsterType.Shambler, 3)));
            var sim = SpawnSim(out var state, config, table, MapWithTunnels(new Vec2(-101.0, 17.0)), totalWaves: 5);

            sim.SpawnWave(1);

            var numbers = NumbersIn(state.Monsters.Values.ToList())
                .Concat(NumbersIn(sim.LastObservation))
                .ToList();

            Assert.Multiple(() =>
            {
                foreach (var abandoned in new[] { 60.0, 10.0, 2.0 })
                {
                    var leaks = numbers.Where(n => Math.Abs(n.Value - abandoned) < Tolerance).ToList();
                    Assert.That(leaks, Is.Empty,
                        "R-16/R-17: the Shambler row is tuned away from the PRD's "
                        + "60 hp / 10 dmg / 2.0 speed / 10 bounty, but " + abandoned
                        + " still appears at " + string.Join(", ", leaks.Select(l => l.Key))
                        + " — a stat is being read from somewhere other than SimConfig.Monsters");
                }
            });
        }

        // ---- R-14: monsters come out of the breaches the wave opens --------------------------------

        /// <summary>
        /// R-14: entry tunnels are fixed map features and the wave table picks which subset opens.
        /// A wave that activates tunnels [1, 2] must put every monster at tunnel 1 or tunnel 2, and
        /// none at 0 or 3 — otherwise the planning-phase preview (R-05 / DEC-018) tells the team to
        /// defend breaches the wave will not use, which is worse than telling them nothing.
        ///
        /// Only membership is asserted. Whether a wave is split evenly across its open breaches,
        /// round-robins, or pours everything out of the first one is not something R-14 or R-19
        /// says, and pinning a distribution here would ship this test's guess as spec.
        ///
        /// The indices are resolved through <see cref="MatchSim.ColonyMap"/>, so the four tunnels
        /// are given deliberately un-V1 coordinates: an implementation that hardcoded the shipped
        /// map's positions would place monsters nowhere near this match's breaches.
        /// </summary>
        [TestCase("0", TestName = "one_open_breach")]
        [TestCase("1,2", TestName = "two_open_breaches_neither_of_them_the_first")]
        [TestCase("3", TestName = "one_open_breach_the_last_one")]
        [TestCase("0,1,2,3", TestName = "all_four_breaches_open")]
        public void Spawned_monsters_stand_only_at_the_tunnels_the_wave_activates(string activeTunnels)
        {
            var active = activeTunnels.Split(',').Select(int.Parse).ToArray();
            var map = MapWithTunnels(
                new Vec2(-101.0, 0.0), new Vec2(0.0, 101.0), new Vec2(101.0, 0.0), new Vec2(0.0, -101.0));
            var table = TableWith(Spec(1, active, (MonsterType.Shambler, 8)));

            var sim = SpawnSim(out var state, table: table, map: map);
            sim.SpawnWave(1);

            var open = active.Select(i => map.EntryTunnels[i]).ToList();
            var shut = Enumerable.Range(0, map.EntryTunnels.Count).Except(active)
                .Select(i => map.EntryTunnels[i]).ToList();

            Assert.That(state.Monsters, Has.Count.EqualTo(8), "the wave has to actually spawn to be placed");
            Assert.Multiple(() =>
            {
                foreach (var monster in state.Monsters.Values)
                {
                    Assert.That(open, Does.Contain(monster.Pos),
                        "R-14: " + monster.Id + " spawned at " + monster.Pos + ", which is not one of "
                        + "this wave's open breaches (" + string.Join(", ", open) + ")");
                    Assert.That(shut, Does.Not.Contain(monster.Pos),
                        "R-14: " + monster.Id + " came out of a breach this wave leaves closed");
                }
            });
        }

        // ---- ids and the living roster -------------------------------------------------------------

        /// <summary>
        /// Two things the wave lifecycle depends on absolutely, and neither is about naming:
        ///  - ids are unique, or two monsters share one dictionary slot and the world quietly loses
        ///    a monster it charged the wave for;
        ///  - every spawned id lands in <see cref="WaveState.LivingMonsterIds"/>, which is the
        ///    roster <see cref="MatchSim.RecordMonsterKill"/> counts down (R-02 / G-010). A monster
        ///    missing from it can never be killed off the roster, so the wave never completes and
        ///    the match hangs in combat forever.
        ///
        /// The id *format* is deliberately unasserted — nothing in the PRD or the fixtures names
        /// one, and G-010's `m1` is a fixture's own input, not a scheme the sim owes anybody.
        /// </summary>
        [Test]
        public void Spawned_ids_are_unique_and_join_the_living_roster()
        {
            var table = TableWith(
                Spec(1, new[] { 0, 1 }, (MonsterType.Shambler, 4), (MonsterType.Ravager, 2)));
            var sim = SpawnSim(out var state, table: table);

            var result = sim.SpawnWave(1);

            Assert.Multiple(() =>
            {
                Assert.That(result.MonsterIds, Has.Count.EqualTo(6));
                Assert.That(result.MonsterIds, Is.Unique, "two monsters may not share an id");
                Assert.That(result.MonsterIds, Has.None.Null.And.None.Empty,
                    "a monster with no id cannot be killed, targeted or replicated");

                foreach (var id in result.MonsterIds)
                {
                    Assert.That(state.Monsters.ContainsKey(id), Is.True,
                        "the spawn reported id '" + id + "' that is not in the world");
                    Assert.That(state.Monsters[id].Id, Is.EqualTo(id),
                        "a monster's key and its own Id must agree — every lookup in this sim assumes it");
                    Assert.That(state.Wave.LivingMonsterIds, Does.Contain(id),
                        "R-02/G-010: '" + id + "' is not on the living roster, so killing it can never "
                        + "complete the wave");
                }

                Assert.That(state.Wave.LivingMonsterIds, Has.Count.EqualTo(6),
                    "the roster is the wave's headcount — no phantom entries, no missing ones");
            });
        }

        /// <summary>
        /// Spawning twice in one match must not reuse an id. This is the failure a per-wave counter
        /// walks straight into: wave 2's first monster is named the same as wave 1's, and because
        /// <see cref="MatchState.Monsters"/> is keyed by id, wave 2's spawn silently overwrites the
        /// corpse still sitting in the dictionary — or, worse, a surviving monster.
        ///
        /// Both spawns are done on the same sim, with the wave counter advanced between them the way
        /// a real match does it.
        /// </summary>
        [Test]
        public void Spawning_a_second_wave_never_reuses_an_id_from_the_first()
        {
            var table = TableWith(
                Spec(1, new[] { 0 }, (MonsterType.Shambler, 4)),
                Spec(2, new[] { 0, 1 }, (MonsterType.Shambler, 4), (MonsterType.Ravager, 2)));

            var sim = SpawnSim(out var state, table: table);

            var first = sim.SpawnWave(1).MonsterIds.ToList();
            state.Wave.Number = 2;
            var second = sim.SpawnWave(2).MonsterIds.ToList();

            Assert.Multiple(() =>
            {
                Assert.That(first, Has.Count.EqualTo(4));
                Assert.That(second, Has.Count.EqualTo(6));
                Assert.That(second.Intersect(first), Is.Empty,
                    "wave 2 reused an id from wave 1; the second spawn overwrites the first's monsters");
                Assert.That(first.Concat(second), Is.Unique);
                Assert.That(state.Monsters, Has.Count.EqualTo(10),
                    "ten distinct monsters were spawned, so ten must exist");
                foreach (var id in first.Concat(second))
                {
                    Assert.That(state.Wave.LivingMonsterIds, Does.Contain(id));
                }
            });
        }

        /// <summary>
        /// The reason the roster matters, driven end to end: a spawned wave must be clearable
        /// through the real <see cref="MatchSim.RecordMonsterKill"/>. Ticket 004's wave completion
        /// has only ever been exercised against rosters a test or a fixture wrote by hand; if
        /// spawning populates the roster differently — ids that do not match the monster keys, or a
        /// roster left empty — the match reaches combat and never leaves it.
        ///
        /// Also pins the negative: no kill before the last one may complete the wave.
        /// </summary>
        [Test]
        public void A_spawned_wave_can_be_cleared_kill_by_kill()
        {
            var table = TableWith(Spec(1, new[] { 0 }, (MonsterType.Shambler, 3)));
            var sim = SpawnSim(out var state, table: table);

            var ids = sim.SpawnWave(1).MonsterIds.ToList();
            Assert.That(ids, Has.Count.EqualTo(3), "the wave must spawn before it can be cleared");

            var completions = ids.Select(id => sim.RecordMonsterKill(Kill(id))).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(completions.Take(2).Any(r => r.WaveComplete), Is.False,
                    "R-02: the wave is not clear while monsters are still alive");
                Assert.That(completions.Last().WaveComplete, Is.True,
                    "R-02/G-010: killing the last spawned monster clears the wave");
                Assert.That(completions.Last().MapVictory, Is.False, "wave 1 of 10 is not the map");
                Assert.That(state.Wave.LivingMonsterIds, Is.Empty);
                Assert.That(state.Phase, Is.EqualTo(MatchPhase.Planning),
                    "R-02/G-010: a cleared wave returns the phase to planning");
            });
        }

        // ---- R-54: the same wave spawns the same way -----------------------------------------------

        /// <summary>
        /// R-54: the sim is host-only, so cross-client lockstep is not required — determinism exists
        /// here so behaviour is replayable and testable. Two matches built from the same
        /// configuration must therefore agree completely on what wave N is: the same ids, of the
        /// same archetypes, at the same positions, in the same order.
        ///
        /// Order is compared through <see cref="WaveSpawnResult.MonsterIds"/> rather than through
        /// <see cref="MatchState.Monsters"/>, because dictionary enumeration order is not a promise
        /// and comparing it would test the runtime rather than the sim.
        ///
        /// No RNG is named and no seed is required: a spawn that reads the table, the catalog and
        /// the map in a fixed order is already deterministic, and this test is equally satisfied by
        /// an implementation that seeds one. What it does fail is an unseeded <c>new Random()</c>,
        /// a <c>Guid.NewGuid()</c> id, or an id counter living in static state shared between
        /// matches.
        /// </summary>
        [Test]
        public void Two_identical_matches_spawn_the_same_wave_identically()
        {
            var alpha = SpawnSim(out var alphaState);
            var beta = SpawnSim(out var betaState);

            var alphaIds = alpha.SpawnWave(5).MonsterIds.ToList();
            var betaIds = beta.SpawnWave(5).MonsterIds.ToList();

            Assert.That(alphaIds, Is.Not.Empty, "wave 5 has to spawn something to be compared");
            Assert.Multiple(() =>
            {
                Assert.That(betaIds, Is.EqualTo(alphaIds).AsCollection,
                    "R-54: the same wave from the same configuration must produce the same ids in the "
                    + "same order — a replay that renames its monsters is not a replay");

                for (var i = 0; i < alphaIds.Count && i < betaIds.Count; i++)
                {
                    var a = alphaState.Monsters[alphaIds[i]];
                    var b = betaState.Monsters[betaIds[i]];
                    Assert.That(b.Type, Is.EqualTo(a.Type), "R-54: spawn #" + i + " is a different archetype");
                    Assert.That(b.Pos, Is.EqualTo(a.Pos), "R-54: spawn #" + i + " came out of a different breach");
                    Assert.That(b.Hp, Is.EqualTo(a.Hp).Within(Tolerance));
                    Assert.That(b.BaseSpeed, Is.EqualTo(a.BaseSpeed).Within(Tolerance));
                }
            });
        }

        // ---- the observation surface -----------------------------------------------------------------

        /// <summary>
        /// Every command in this sim records what it did into <see cref="MatchSim.LastObservation"/>
        /// — that stream is what the host replicates from, so a spawn that is invisible on it puts a
        /// wave of monsters into the host's world and none onto any client's screen.
        ///
        /// Presence and shape only. No fixture pins a spawn's event name or its state-change list
        /// (this ticket grades none), so naming either here would invent vocabulary the way
        /// <c>wave_complete</c> and <c>planning_started</c> were *not* invented — those are pinned
        /// by G-010 and G-016.
        /// </summary>
        [Test]
        public void Spawning_records_a_result_an_event_and_what_materially_changed()
        {
            var table = TableWith(Spec(1, new[] { 0 }, (MonsterType.Shambler, 3)));
            var sim = SpawnSim(out _, table: table);

            sim.SpawnWave(1);
            var observation = sim.LastObservation;

            Assert.Multiple(() =>
            {
                Assert.That(observation.Result, Is.Not.Null,
                    "the command's result must be recorded for replication, like every other command's");
                Assert.That(observation.EmittedEvents, Is.Not.Empty,
                    "a wave arriving is the loudest thing that happens in a match; it must be announced");
                Assert.That(observation.StateChanges, Is.Not.Empty,
                    "three monsters appeared in the world — something materially changed and must replicate");
            });
        }

        // ---- sad paths: shape and non-corruption only -------------------------------------------------

        /// <summary>
        /// A wave number the table does not define. <see cref="WaveTable.For"/> already throws
        /// naming the missing wave, and this ticket does not decide whether spawning should let that
        /// through or refuse ahead of it — the repo answers such things inconsistently on purpose
        /// (<see cref="MatchSim.BeginPlanningPhase"/> throws, <see cref="MatchSim.RecordMonsterKill"/>
        /// ignores).
        ///
        /// What is not negotiable: a wave that does not exist puts nothing into the world and
        /// nothing onto the roster. A half-spawn here would leave a match holding living ids for
        /// monsters that were never created, which no kill can ever clear.
        /// </summary>
        [Test]
        public void Spawning_a_wave_the_table_does_not_define_creates_nothing()
        {
            var sim = SpawnSim(out var state);

            var thrown = Attempt(() => sim.SpawnWave(99));

            Assert.Multiple(() =>
            {
                AssertDefined(thrown);
                Assert.That(state.Monsters, Is.Empty, "wave 99 does not exist, so it sends no monsters");
                Assert.That(state.Wave.LivingMonsterIds, Is.Empty,
                    "a roster entry with no monster behind it can never be killed off");
            });
        }

        /// <summary>
        /// A wave whose composition names an archetype the catalog has no row for — the exact case
        /// <see cref="MonsterCatalog.StatsFor"/> was written to make loud, since the alternative is
        /// a zero-HP monster that dies to its own spawn.
        ///
        /// Whether spawning lets that throw or refuses the wave up front is genuinely open, and so
        /// is whether the wave's *valid* groups still spawn, so neither is asserted. The invariants
        /// that hold either way are: no monster of the unconfigured type exists, every monster in
        /// the world has a catalog row behind it, and the living roster and the monster table still
        /// describe the same set of monsters.
        /// </summary>
        [Test]
        public void Spawning_a_wave_naming_an_unconfigured_archetype_leaves_the_world_consistent()
        {
            const string Unconfigured = "ghoul";

            var config = new SimConfig();
            var table = TableWith(
                Spec(1, new[] { 0 }, (MonsterType.Shambler, 2), (Unconfigured, 3)));
            var sim = SpawnSim(out var state, config, table);

            var thrown = Attempt(() => sim.SpawnWave(1));

            Assert.Multiple(() =>
            {
                AssertDefined(thrown);
                Assert.That(state.Monsters.Values.Any(m => m.Type == Unconfigured), Is.False,
                    "R-17: '" + Unconfigured + "' has no stat row, so a monster of that type would carry "
                    + "invented numbers");

                foreach (var monster in state.Monsters.Values)
                {
                    Assert.That(config.Monsters.Contains(monster.Type), Is.True,
                        "monster '" + monster.Id + "' spawned as an archetype the catalog does not configure");
                }

                foreach (var id in state.Wave.LivingMonsterIds)
                {
                    Assert.That(state.Monsters.ContainsKey(id), Is.True,
                        "the living roster holds '" + id + "', which is not a monster in this world");
                }

                Assert.That(state.Wave.LivingMonsterIds, Is.Unique);
                Assert.That(state.Monsters.Count, Is.EqualTo(state.Wave.LivingMonsterIds.Count),
                    "every spawned monster is alive and on the roster, or the wave can never complete");
            });
        }

        /// <summary>
        /// Spawning into a finished match (R-01 victory or R-02 defeat). R-02 makes defeat immediate
        /// — the colony emptied mid-wave — so a spawn command already in flight from the host loop
        /// must not repopulate a match that is over, and on the final wave it must not manufacture a
        /// wave 11 out of a match that has already been won.
        ///
        /// As above: refusing and throwing are both acceptable; putting monsters into a finished
        /// match is not.
        /// </summary>
        [TestCase(MatchStatus.Victory, TestName = "spawn_after_the_map_was_won")]
        [TestCase(MatchStatus.Defeat, TestName = "spawn_after_the_colony_fell")]
        public void Spawning_into_a_finished_match_creates_nothing(string status)
        {
            var sim = SpawnSim(out var state);
            state.Status = status;

            var thrown = Attempt(() => sim.SpawnWave(1));

            Assert.Multiple(() =>
            {
                AssertDefined(thrown);
                Assert.That(state.Monsters, Is.Empty, "R-01/R-02: a finished match fights no further wave");
                Assert.That(state.Wave.LivingMonsterIds, Is.Empty);
                Assert.That(state.Status, Is.EqualTo(status), "a refused spawn must not move the match status");
                Assert.That(state.IsOver, Is.True);
            });
        }

        // ---- scenario helpers --------------------------------------------------------------------------

        /// <summary>
        /// A match in combat on wave 1 with an empty world, wired to whichever map, table and config
        /// the test wants. The map is passed to <see cref="MatchSim.ColonyMap"/> *and* used to build
        /// the state, so the tunnels a test asserts against are the tunnels the sim resolves through.
        /// </summary>
        private static MatchSim SpawnSim(
            out MatchState state,
            SimConfig config = null,
            WaveTable table = null,
            ColonyMap map = null,
            int totalWaves = 10)
        {
            var tunables = config ?? new SimConfig();
            var colony = map ?? ColonyMap.V1();

            state = colony.CreateMatchState(tunables);
            state.Phase = MatchPhase.Combat;
            state.Wave.Number = 1;
            state.Wave.TotalWaves = totalWaves;

            var sim = new MatchSim(state, tunables, null, new SimClock(0.0), null)
            {
                ColonyMap = colony,
            };

            if (table != null)
            {
                sim.WaveTable = table;
            }

            return sim;
        }

        private const double TunedMaxHp = 7.25;
        private const double TunedAttackDamage = 3.5;
        private const double TunedMoveSpeed = 0.125;
        private const int TunedBounty = 3;

        /// <summary>
        /// A config whose Shambler row is tuned away from R-17's 60 / 10 / 2.0 / 10 in every column,
        /// to values that cannot be confused with a wave number, a headcount or a coordinate.
        /// </summary>
        private static SimConfig TunedShamblerConfig()
        {
            var config = new SimConfig();
            config.Monsters.Set(MonsterType.Shambler, new MonsterStats
            {
                MaxHp = TunedMaxHp,
                AttackDamage = TunedAttackDamage,
                MoveSpeed = TunedMoveSpeed,
                Bounty = TunedBounty,
            });

            return config;
        }

        /// <summary>The v1 colony with its breaches replaced, so no shipped coordinate can pass by luck.</summary>
        private static ColonyMap MapWithTunnels(params Vec2[] tunnels)
        {
            var map = ColonyMap.V1();
            map.EntryTunnels.Clear();
            map.EntryTunnels.AddRange(tunnels);
            return map;
        }

        private static WaveTable TableWith(params WaveSpec[] specs)
        {
            var table = new WaveTable();
            table.Waves.AddRange(specs);
            return table;
        }

        private static WaveSpec Spec(int number, int[] activeTunnels, params (string Type, int Count)[] groups)
        {
            var spec = new WaveSpec { Number = number };
            spec.ActiveTunnels.AddRange(activeTunnels);
            foreach (var group in groups)
            {
                spec.Groups.Add(new MonsterGroup { MonsterType = group.Type, Count = group.Count });
            }

            return spec;
        }

        private static MonsterKillRequest Kill(string monsterId, int bounty = 10) => new MonsterKillRequest
        {
            MonsterId = monsterId,
            MonsterType = MonsterType.Shambler,
            Bounty = bounty,
        };

        private static Exception Attempt(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>Rejecting is fine, throwing is fine, no-op is fine; "the rule does not exist" is not.</summary>
        private static void AssertDefined(Exception thrown) =>
            Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                "the sad path must have a decided behaviour, not an unimplemented one: " + thrown);

        // ---- observation helpers -----------------------------------------------------------------------

        /// <summary>
        /// Every number reachable from an object, with the path that reached it. Walks dictionaries,
        /// sequences and the public members of sim types, so a stat read from the wrong place is
        /// found however deep it is buried rather than hidden by a shallow field check.
        /// </summary>
        private static List<KeyValuePair<string, double>> NumbersIn(object root)
        {
            var found = new List<KeyValuePair<string, double>>();
            Walk(string.Empty, root, found, 0);
            return found;
        }

        private static void Walk(string path, object node, List<KeyValuePair<string, double>> found, int depth)
        {
            if (node == null || depth > 6)
            {
                return;
            }

            switch (node)
            {
                case string _:
                case bool _:
                    return;
                case double d:
                    found.Add(new KeyValuePair<string, double>(path, d));
                    return;
                case float f:
                    found.Add(new KeyValuePair<string, double>(path, f));
                    return;
                case decimal m:
                    found.Add(new KeyValuePair<string, double>(path, (double)m));
                    return;
                case int i:
                    found.Add(new KeyValuePair<string, double>(path, i));
                    return;
                case long l:
                    found.Add(new KeyValuePair<string, double>(path, l));
                    return;
                case short s:
                    found.Add(new KeyValuePair<string, double>(path, s));
                    return;
            }

            var type = node.GetType();
            if (type.IsPrimitive || type.IsEnum)
            {
                return;
            }

            if (node is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    Walk(path + "/" + entry.Key, entry.Value, found, depth + 1);
                }

                return;
            }

            if (node is IEnumerable sequence)
            {
                var index = 0;
                foreach (var item in sequence)
                {
                    Walk(path + "[" + index + "]", item, found, depth + 1);
                    index++;
                }

                return;
            }

            if (type.Namespace != "RedHollow.Sim")
            {
                return;
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Walk(path + "/" + field.Name, field.GetValue(node), found, depth + 1);
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.GetIndexParameters().Length == 0))
            {
                Walk(path + "/" + property.Name, property.GetValue(node), found, depth + 1);
            }
        }
    }
}
