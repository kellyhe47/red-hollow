using System;
using System.Collections.Generic;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Ticket 002 (T-02), the parts the golden fixtures do not grade.
    ///
    /// G-001..G-005 already pin five concrete targeting arrangements through the locked golden
    /// adapter, so nothing here re-encodes them. What the fixtures cannot see is (a) the R-17 roster
    /// table, which no fixture supplies or reads, and (b) whether the B-003 Burrower carve-out is a
    /// *rule* or just special-casing of the one arrangement G-005 happens to describe. Those two
    /// acceptance criteria live here.
    ///
    /// Scenarios are built from production types directly rather than through the fixture JSON
    /// loader: the loader is the adapter's contract with eval/golden, not a test fixture builder.
    /// </summary>
    [TestFixture]
    public class T02TargetingTests
    {
        private const double Tolerance = 1e-9;

        // ---- R-17: the roster is configuration -----------------------------------------------------

        /// <summary>
        /// The R-17 roster table, verbatim from the PRD. Bounty is one number with two uses — scrip
        /// paid out (R-20) and XP granted (R-40) — so a single column pins both.
        /// </summary>
        private static IEnumerable<TestCaseData> RosterTable()
        {
            yield return new TestCaseData(MonsterType.Shambler, 60.0, 10.0, 2.0, 10).SetName("roster_shambler");
            yield return new TestCaseData(MonsterType.Ravager, 40.0, 8.0, 5.0, 15).SetName("roster_ravager");
            yield return new TestCaseData(MonsterType.Spitter, 50.0, 12.0, 2.0, 20).SetName("roster_spitter");
            yield return new TestCaseData(MonsterType.Burrower, 80.0, 15.0, 2.5, 30).SetName("roster_burrower");
            yield return new TestCaseData(MonsterType.BullBehemoth, 400.0, 40.0, 1.5, 50).SetName("roster_bull_behemoth");
        }

        /// <summary>
        /// R-17: every archetype's stats come out of <see cref="SimConfig.Monsters"/>. Asserting on the
        /// catalog rather than on the sim is deliberate — the criterion is about where the numbers
        /// live, and a sim-level assertion would pass just as happily against constants in code.
        /// </summary>
        [TestCaseSource(nameof(RosterTable))]
        public void Configured_roster_matches_the_R17_table(
            string monsterType, double maxHp, double attackDamage, double moveSpeed, int bounty)
        {
            var stats = new SimConfig().Monsters.StatsFor(monsterType);

            Assert.Multiple(() =>
            {
                Assert.That(stats.MaxHp, Is.EqualTo(maxHp).Within(Tolerance), monsterType + " max_hp");
                Assert.That(stats.AttackDamage, Is.EqualTo(attackDamage).Within(Tolerance), monsterType + " attack_damage");
                Assert.That(stats.MoveSpeed, Is.EqualTo(moveSpeed).Within(Tolerance), monsterType + " move_speed");
                Assert.That(stats.Bounty, Is.EqualTo(bounty), monsterType + " bounty (= XP, R-40)");
            });
        }

        /// <summary>
        /// R-17 names exactly five archetypes. A sixth row would be an unspecified monster the wave
        /// table could spawn; a missing row is a KeyNotFoundException at spawn time.
        /// </summary>
        [Test]
        public void Roster_holds_exactly_the_five_R17_archetypes()
        {
            var roster = new SimConfig().Monsters;

            Assert.That(roster.Types, Is.EquivalentTo(new[]
            {
                MonsterType.Shambler,
                MonsterType.Ravager,
                MonsterType.Spitter,
                MonsterType.Burrower,
                MonsterType.BullBehemoth,
            }));
            Assert.That(roster.Count, Is.EqualTo(5));
        }

        /// <summary>
        /// R-16: "tunable in config without code changes". The roster only counts as configuration if
        /// a caller can override a stat on its own <see cref="SimConfig"/> and have the change stay
        /// there — and stay out of every other config. A hardcoded constant, or defaults shared
        /// through static state, fails one half of this or the other.
        /// </summary>
        [Test]
        public void Roster_stats_are_overridable_per_config_instance()
        {
            var tuned = new SimConfig();
            tuned.Monsters.Set(MonsterType.Shambler, new MonsterStats
            {
                MaxHp = 999.0,
                AttackDamage = 1.0,
                MoveSpeed = 0.25,
                Bounty = 7,
            });

            var tunedShambler = tuned.Monsters.StatsFor(MonsterType.Shambler);
            Assert.Multiple(() =>
            {
                Assert.That(tunedShambler.MaxHp, Is.EqualTo(999.0).Within(Tolerance));
                Assert.That(tunedShambler.AttackDamage, Is.EqualTo(1.0).Within(Tolerance));
                Assert.That(tunedShambler.MoveSpeed, Is.EqualTo(0.25).Within(Tolerance));
                Assert.That(tunedShambler.Bounty, Is.EqualTo(7));
            });

            Assert.That(new SimConfig().Monsters.StatsFor(MonsterType.Shambler).MaxHp,
                Is.EqualTo(60.0).Within(Tolerance),
                "one config's override leaked into another; the roster is shared static state, not config");
        }

        // ---- B-003: the Burrower carve-out is a rule, not one arrangement ---------------------------

        /// <summary>
        /// B-003 over B-001. The carve-out removes heroes from consideration entirely and does not
        /// suspend the R-12 empty-hotspot rule: the Burrower here walks past a living hero at
        /// distance 1 and an emptied chapel at distance 2 to reach the nearest hotspot that still
        /// holds civilians, and picks the nearer of the two populated ones.
        /// </summary>
        [Test]
        public void Burrower_takes_the_nearest_populated_hotspot_over_a_nearer_hero_or_empty_hotspot()
        {
            var state = new MatchState();
            AddMonster(state, "m9", MonsterType.Burrower, 0, 0);
            AddHero(state, "hero_a", 1, 0, alive: true);
            AddHotspot(state, "hs_chapel", 2, 0, civilians: 0);
            AddHotspot(state, "hs_homestead", 3, 4, civilians: 2);
            AddHotspot(state, "hs_saloon", 12, 0, civilians: 6);

            var result = new MatchSim(state).SelectTarget("m9");

            Assert.That(result.TargetId, Is.EqualTo("hs_homestead"));
            Assert.That(result.Distance, Is.EqualTo(5.0).Within(Tolerance));
        }

        /// <summary>
        /// The carve-out is conditional on the archetype, not on the arrangement. One world — a
        /// living hero close in, a populated hotspot far out, and a barricade declared across the
        /// path to that hotspot — read by two monster types.
        ///
        /// The Burrower ignores both the hero (B-003 over B-001) and the interposed barricade
        /// (B-003 over B-002) and commits to the hotspot at its true distance. The Shambler in the
        /// identical world takes the hero, which is what makes this a control rather than a second
        /// spelling of the general rule.
        /// </summary>
        [TestCase(MonsterType.Burrower, "hs_saloon", 10.0, TestName = "carve_out_burrower_reaches_hotspot")]
        [TestCase(MonsterType.Shambler, "hero_a", 1.0, TestName = "carve_out_control_shambler_takes_hero")]
        public void Same_arrangement_targets_differently_by_monster_type(
            string monsterType, string expectedTargetId, double expectedDistance)
        {
            var state = new MatchState();
            AddMonster(state, "m1", monsterType, 0, 0);
            AddHero(state, "hero_a", 1, 0, alive: true);
            AddHotspot(state, "hs_saloon", 10, 0, civilians: 8);
            AddBarricade(state, "bar_1", 5, 0, hp: 300);

            var oracle = new DeclaredPathOracle();
            oracle.Declare("m1", "hs_saloon", "bar_1");

            var result = new MatchSim(state, pathOracle: oracle).SelectTarget("m1");

            Assert.That(result.TargetId, Is.EqualTo(expectedTargetId));
            Assert.That(result.Distance, Is.EqualTo(expectedDistance).Within(Tolerance));
        }

        // ---- BarricadePathOracle: live geometry, not a declared pair --------------------------------

        /// <summary>
        /// G-004's arrangement, answered from positions rather than from a declared pair: a wall
        /// sitting on the walk from the shambler to the saloon becomes the target. This is the
        /// production oracle the Unity factory injects; goldens still use DeclaredPathOracle so
        /// a fixture that names `blocks_path_between` is not rewritten as geometry.
        /// </summary>
        [Test]
        public void A_standing_barricade_across_the_walk_becomes_the_target()
        {
            var state = new MatchState();
            AddMonster(state, "m1", MonsterType.Shambler, 0, 0);
            AddHotspot(state, "hs_saloon", 10, 0, civilians: 8);
            AddBarricade(state, "bar_1", 5, 0, hp: 300);

            var result = new MatchSim(state, pathOracle: new BarricadePathOracle(state)).SelectTarget("m1");

            Assert.That(result.TargetId, Is.EqualTo("bar_1"));
            Assert.That(result.Distance, Is.EqualTo(5.0).Within(Tolerance));
        }

        /// <summary>
        /// Off the walk is not "in the way". A wall five units beside the lane must not steal the
        /// hotspot — otherwise every barricade on the map is every monster's target.
        /// </summary>
        [Test]
        public void A_barricade_off_the_walk_does_not_redirect()
        {
            var state = new MatchState();
            AddMonster(state, "m1", MonsterType.Shambler, 0, 0);
            AddHotspot(state, "hs_saloon", 10, 0, civilians: 8);
            AddBarricade(state, "bar_1", 5, 5, hp: 300);

            var result = new MatchSim(state, pathOracle: new BarricadePathOracle(state)).SelectTarget("m1");

            Assert.That(result.TargetId, Is.EqualTo("hs_saloon"));
        }

        /// <summary>
        /// "First" is nearest along the walk, not cheapest id: the wall the monster hits first is
        /// the one it chews, even if a later wall sorts earlier.
        /// </summary>
        [Test]
        public void The_first_standing_barricade_along_the_walk_wins()
        {
            var state = new MatchState();
            AddMonster(state, "m1", MonsterType.Shambler, 0, 0);
            AddHotspot(state, "hs_saloon", 10, 0, civilians: 8);
            AddBarricade(state, "bar_z", 3, 0, hp: 300);
            AddBarricade(state, "bar_a", 7, 0, hp: 300);

            var result = new MatchSim(state, pathOracle: new BarricadePathOracle(state)).SelectTarget("m1");

            Assert.That(result.TargetId, Is.EqualTo("bar_z"),
                "the nearer wall is first on the walk even though bar_a sorts earlier");
        }

        /// <summary>
        /// B-003 in the oracle itself: a Burrower walking the same line as the shambler above
        /// ignores the wall (and the hero) and commits to the hotspot at its true distance.
        /// </summary>
        [Test]
        public void A_burrower_tunnels_past_a_wall_the_oracle_can_see()
        {
            var state = new MatchState();
            AddMonster(state, "m9", MonsterType.Burrower, 0, 0);
            AddHero(state, "hero_a", 1, 0, alive: true);
            AddHotspot(state, "hs_saloon", 10, 0, civilians: 8);
            AddBarricade(state, "bar_1", 5, 0, hp: 300);

            var result = new MatchSim(state, pathOracle: new BarricadePathOracle(state)).SelectTarget("m9");

            Assert.That(result.TargetId, Is.EqualTo("hs_saloon"));
            Assert.That(result.Distance, Is.EqualTo(10.0).Within(Tolerance));
        }

        /// <summary>
        /// Exists is the standing predicate. A sold or destroyed wall is ground again and must
        /// not redirect, matching G-004's "until destroyed" and T06's retarget after the break.
        /// </summary>
        [Test]
        public void A_destroyed_barricade_is_not_across_the_walk()
        {
            var state = new MatchState();
            AddMonster(state, "m1", MonsterType.Shambler, 0, 0);
            AddHotspot(state, "hs_saloon", 10, 0, civilians: 8);
            AddBarricade(state, "bar_1", 5, 0, hp: 0);
            state.Placeables["bar_1"].Exists = false;

            var result = new MatchSim(state, pathOracle: new BarricadePathOracle(state)).SelectTarget("m1");

            Assert.That(result.TargetId, Is.EqualTo("hs_saloon"));
        }

        // ---- sad paths -----------------------------------------------------------------------------

        /// <summary>
        /// Every hotspot emptied and every hero down leaves the candidate set empty. The PRD does not
        /// say what a monster does then (in a real match R-11's defeat rule has already fired), so
        /// this pins only that the answer is defined and non-fatal: a result naming no target, rather
        /// than an exception out of an empty min().
        /// </summary>
        [Test]
        public void No_available_target_yields_a_result_naming_no_target()
        {
            var state = new MatchState();
            AddMonster(state, "m1", MonsterType.Shambler, 0, 0);
            AddHero(state, "hero_a", 1, 0, alive: false);
            AddHotspot(state, "hs_chapel", 2, 0, civilians: 0);

            var sim = new MatchSim(state);

            TargetSelectionResult result = null;
            Assert.That(() => result = sim.SelectTarget("m1"), Throws.Nothing);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.MonsterId, Is.EqualTo("m1"));
            Assert.That(result.TargetId, Is.Null, "there was nothing available to target");
        }

        /// <summary>
        /// A monster id the match does not hold is a caller bug. Whether the sim throws or returns an
        /// empty selection is open, so this pins only the part that is not: it must never answer with
        /// a target. The NotImplementedException guard is what keeps this red until T-02 lands —
        /// without it the unimplemented stub would satisfy "does not answer with a target" trivially.
        /// </summary>
        [Test]
        public void Unknown_monster_id_never_yields_a_target()
        {
            var state = new MatchState();
            AddMonster(state, "m1", MonsterType.Shambler, 0, 0);
            AddHotspot(state, "hs_saloon", 10, 0, civilians: 8);

            var sim = new MatchSim(state);

            TargetSelectionResult result = null;
            Exception thrown = null;
            try
            {
                result = sim.SelectTarget("m_not_in_this_match");
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                "select_target is still a stub, so an unknown monster id has no defined behaviour yet");

            if (thrown == null)
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(result.TargetId, Is.Null,
                    "a monster that is not in the match must not come back holding a target");
            }
        }

        // ---- scenario builders ---------------------------------------------------------------------

        private static void AddMonster(MatchState state, string id, string type, double x, double y)
        {
            state.Monsters[id] = new Monster
            {
                Id = id,
                Type = type,
                Pos = new Vec2(x, y),
                Hp = 60.0,
                Alive = true,
                BaseSpeed = 2.0,
                CurrentSpeed = 2.0,
            };
        }

        private static void AddHero(MatchState state, string id, double x, double y, bool alive)
        {
            state.Heroes[id] = new Hero
            {
                Id = id,
                HeroClass = HeroClass.Gunslinger,
                AccountId = "acct_" + id,
                Pos = new Vec2(x, y),
                Hp = alive ? 100.0 : 0.0,
                MaxHp = 100.0,
                Alive = alive,
            };
        }

        private static void AddHotspot(MatchState state, string id, double x, double y, int civilians)
        {
            state.Hotspots[id] = new Hotspot
            {
                Id = id,
                Pos = new Vec2(x, y),
                Civilians = civilians,
            };
        }

        private static void AddBarricade(MatchState state, string id, double x, double y, double hp)
        {
            state.Placeables[id] = new Placeable
            {
                Id = id,
                Type = PlaceableType.Barricade,
                Pos = new Vec2(x, y),
                PurchaseCost = 100,
                Hp = hp,
                Exists = true,
            };
        }
    }
}
