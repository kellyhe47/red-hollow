using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Ticket 009 (T-09) rule tests: R-40..R-44, the half of progression the golden fixtures leave
    /// under-covered.
    ///
    /// G-023..G-026 are graded by the locked golden adapter and are deliberately NOT re-encoded here.
    /// What this fixture owns instead is the *rule behind* those four samples:
    ///   - persistence timing measured with a counting store, not a single pinned observation;
    ///   - the level curve as a curve, including landing exactly on a threshold;
    ///   - lifetime XP monotonicity across waves and across matches;
    ///   - turret credit, free-choice spending, the rank ceiling, and banked points.
    ///
    /// Everything the PRD leaves open (rejection wording other than G-026's "no_skill_points", the
    /// number of writes inside one multi-level command) is asserted as shape or as a bound, never as
    /// a guessed string — an over-pinned test here would reject a correct implementation.
    /// </summary>
    [TestFixture]
    public class T09_ProgressionTests
    {
        // ---- test doubles and builders -----------------------------------------------------------

        /// <summary>
        /// A counting <see cref="IProfileStore"/>. The fixtures can only pin persistence at four
        /// moments; R-43 states a rule ("saves at each level-up and match end") that is only
        /// observable by counting writes over a sequence of commands, which is what this exists for.
        ///
        /// Each write is stored as a snapshot: the sim keeps mutating the live profile object, and a
        /// test asserting *what* was written must not have its history rewritten underneath it.
        /// </summary>
        private sealed class RecordingProfileStore : IProfileStore
        {
            private readonly Dictionary<string, AccountProfile> _profiles =
                new Dictionary<string, AccountProfile>(StringComparer.Ordinal);

            internal List<AccountProfile> Saves { get; } = new List<AccountProfile>();

            internal int SaveCount => Saves.Count;

            internal IEnumerable<string> SavedAccountIds => Saves.Select(p => p.AccountId);

            internal void Seed(AccountProfile profile)
            {
                _profiles[profile.AccountId] = profile;
            }

            public AccountProfile Load(string accountId)
            {
                if (_profiles.TryGetValue(accountId, out var profile))
                {
                    return profile;
                }

                // R-44: an unknown callsign is a fresh account, not a miss. Mirrors InMemoryProfileStore.
                var fresh = new AccountProfile { AccountId = accountId };
                _profiles[accountId] = fresh;
                return fresh;
            }

            public void Save(AccountProfile profile)
            {
                Saves.Add(profile.Clone());
                _profiles[profile.AccountId] = profile;
            }

            /// <summary>What a later match would load for this account right now.</summary>
            internal AccountProfile Current(string accountId) => Load(accountId);
        }

        private static AccountProfile Profile(
            string accountId, double lifetimeXp, int level, int skillPoints, int q = 0, int e = 0)
        {
            var profile = new AccountProfile
            {
                AccountId = accountId,
                LifetimeXp = lifetimeXp,
                Level = level,
                SkillPoints = skillPoints,
            };

            profile.Abilities["Q"] = q;
            profile.Abilities["E"] = e;
            return profile;
        }

        /// <summary>
        /// A match with the given heroes bound to the given accounts — the association the golden
        /// loader also builds from `given.inputs.profile.hero_id`.
        /// </summary>
        private static MatchSim SimWith(
            RecordingProfileStore store, params (string HeroId, string AccountId)[] heroes)
        {
            var state = new MatchState();
            foreach (var (heroId, accountId) in heroes)
            {
                state.Heroes[heroId] = new Hero
                {
                    Id = heroId,
                    HeroClass = HeroClass.Gunslinger,
                    AccountId = accountId,
                    Hp = 100.0,
                    MaxHp = 100.0,
                };

                state.Players.Add(new PlayerSlot
                {
                    Id = "p_" + heroId,
                    AccountId = accountId,
                    HeroClass = HeroClass.Gunslinger,
                });
            }

            return new MatchSim(state, new SimConfig(), store);
        }

        private static MonsterKillRequest Kill(
            int bounty, string killerHeroId, string monsterType = MonsterType.Shambler, string monsterId = "m1")
        {
            return new MonsterKillRequest
            {
                MonsterId = monsterId,
                MonsterType = monsterType,
                Bounty = bounty,
                KillerHeroId = killerHeroId,
            };
        }

        private static SpendSkillPointRequest Spend(string accountId, string heroId, string choice)
        {
            return new SpendSkillPointRequest
            {
                AccountId = accountId,
                HeroId = heroId,
                Choice = choice,
            };
        }

        // ---- R-43: where the profile store is written --------------------------------------------

        /// <summary>
        /// The failure G-024 defends against, generalised: every kill writing to the store would
        /// hammer persistence mid-combat. Four kills that never reach the level-2 threshold must
        /// produce no writes at all.
        /// </summary>
        [Test]
        public void Kills_below_the_next_threshold_never_write_to_the_profile_store()
        {
            var store = new RecordingProfileStore();
            store.Seed(Profile("acct_tex", 0.0, 1, 0));
            var sim = SimWith(store, ("hero_b", "acct_tex"));

            double running = 0.0;
            foreach (var bounty in new[] { 10, 10, 15, 20 })
            {
                var result = sim.AwardKillXp(Kill(bounty, "hero_b"), "acct_tex");
                running += bounty;

                Assert.That(result.LeveledUp, Is.False, "55 XP total never reaches the level-2 threshold of 100");
                Assert.That(result.LifetimeXp, Is.EqualTo(running));
                Assert.That(sim.LastObservation.ExternalCalls, Is.Empty,
                    "the observation must not claim a save the store never received");
            }

            Assert.That(store.SaveCount, Is.EqualTo(0),
                "R-43: profiles save at level-up and match end, not on every kill");
        }

        /// <summary>
        /// One level-up, one write — and the write carries the *post*-level-up profile. Saving a
        /// stale snapshot would leave the earned level and point on the floor at the next load.
        /// </summary>
        [Test]
        public void A_kill_that_crosses_a_threshold_writes_the_updated_profile_once()
        {
            var store = new RecordingProfileStore();
            store.Seed(Profile("acct_kelly", 90.0, 1, 0));
            var sim = SimWith(store, ("hero_a", "acct_kelly"));

            var result = sim.AwardKillXp(Kill(15, "hero_a", MonsterType.Ravager, "m4"), "acct_kelly");

            Assert.Multiple(() =>
            {
                Assert.That(result.LeveledUp, Is.True);
                Assert.That(store.SaveCount, Is.EqualTo(1));
                Assert.That(store.SavedAccountIds, Is.EqualTo(new[] { "acct_kelly" }));
                Assert.That(store.Saves[0].LifetimeXp, Is.EqualTo(105.0));
                Assert.That(store.Saves[0].Level, Is.EqualTo(2));
                Assert.That(store.Saves[0].SkillPoints, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// A single fat bounty from level 1 crosses three thresholds at once (100, 300, 600). The
        /// point arithmetic is exact — one per level gained — while the write count is only bounded,
        /// because R-43 ("at each level-up") permits three writes and G-023 permits one batched write
        /// per command. What must never happen is an unbounded number of writes.
        /// </summary>
        [Test]
        public void One_kill_crossing_three_thresholds_grants_one_point_per_level()
        {
            var store = new RecordingProfileStore();
            store.Seed(Profile("acct_kelly", 0.0, 1, 0));
            var sim = SimWith(store, ("hero_a", "acct_kelly"));

            var result = sim.AwardKillXp(Kill(600, "hero_a", MonsterType.BullBehemoth, "m9"), "acct_kelly");

            Assert.Multiple(() =>
            {
                Assert.That(result.LifetimeXp, Is.EqualTo(600.0));
                Assert.That(result.Level, Is.EqualTo(4), "cumulative thresholds 100 / 300 / 600 all cleared");
                Assert.That(result.LeveledUp, Is.True);
                Assert.That(result.SkillPoints, Is.EqualTo(3), "R-42: one point per level gained, 1 -> 4");
                Assert.That(result.XpIntoLevel, Is.EqualTo(0.0), "landing exactly on the level-4 threshold");
                Assert.That(result.XpForNextLevel, Is.EqualTo(400.0), "the level-4 band is 1000 - 600");
            });

            Assert.That(store.SaveCount, Is.InRange(1, 3),
                "at least one write (a level-up happened), at most one per level gained");
            Assert.That(store.Saves[store.SaveCount - 1].Level, Is.EqualTo(4));
            Assert.That(store.Saves[store.SaveCount - 1].SkillPoints, Is.EqualTo(3));
        }

        /// <summary>
        /// R-43 pins a save at match end and no fixture covers it: without it, every kill's XP after
        /// the last level-up is lost when the match tears down.
        ///
        /// NOTE FOR THE IMPLEMENTER: this seam does not exist yet. MatchSim has no match-end
        /// operation at all, so this test does not compile until one is added. Keep it minimal — the
        /// requirement is only "persist the players' profiles once the match is over".
        /// </summary>
        [Test]
        public void Match_end_writes_every_players_profile()
        {
            var store = new RecordingProfileStore();
            store.Seed(Profile("acct_kelly", 40.0, 1, 0));
            store.Seed(Profile("acct_tex", 10.0, 1, 0));
            var sim = SimWith(store, ("hero_a", "acct_kelly"), ("hero_b", "acct_tex"));

            sim.AwardKillXp(Kill(10, "hero_a"), "acct_kelly");
            Assert.That(store.SaveCount, Is.EqualTo(0), "below the threshold, nothing is written mid-match");

            sim.State.Status = MatchStatus.Victory;
            sim.SaveProfilesAtMatchEnd();

            Assert.That(store.SavedAccountIds, Is.EquivalentTo(new[] { "acct_kelly", "acct_tex" }),
                "R-43: every player's profile is persisted when the match ends");
            Assert.That(store.Current("acct_kelly").LifetimeXp, Is.EqualTo(50.0),
                "the XP earned since the last level-up survives the match");
        }

        // ---- R-41: the level curve, and XP that only grows ----------------------------------------

        /// <summary>
        /// The curve as a curve (R-41 / DEC-013). Cumulative thresholds are 0 / 100 / 300 / 600 /
        /// 1000; `xp_into_level` is lifetime minus the current level's threshold and
        /// `xp_for_next_level` is the *width* of the current band, not the next cumulative total.
        /// The exactly-on-a-threshold rows are the ones no fixture covers.
        /// </summary>
        [TestCase(0.0, 1, 99, 99.0, 1, false, 0, 99.0, 100.0, TestName = "one XP short of level 2")]
        [TestCase(99.0, 1, 1, 100.0, 2, true, 1, 0.0, 200.0, TestName = "exactly on the level-2 threshold")]
        [TestCase(100.0, 2, 199, 299.0, 2, false, 0, 199.0, 200.0, TestName = "one XP short of level 3")]
        [TestCase(299.0, 2, 1, 300.0, 3, true, 1, 0.0, 300.0, TestName = "exactly on the level-3 threshold")]
        [TestCase(300.0, 3, 299, 599.0, 3, false, 0, 299.0, 300.0, TestName = "one XP short of level 4")]
        [TestCase(599.0, 3, 1, 600.0, 4, true, 1, 0.0, 400.0, TestName = "exactly on the level-4 threshold")]
        [TestCase(999.0, 4, 1, 1000.0, 5, true, 1, 0.0, 500.0, TestName = "exactly on the level-5 threshold")]
        public void The_level_curve_reports_the_current_band(
            double startXp,
            int startLevel,
            int bounty,
            double expectedLifetime,
            int expectedLevel,
            bool expectedLeveledUp,
            int expectedPoints,
            double expectedIntoLevel,
            double expectedForNextLevel)
        {
            var store = new RecordingProfileStore();
            store.Seed(Profile("acct_kelly", startXp, startLevel, 0));
            var sim = SimWith(store, ("hero_a", "acct_kelly"));

            var result = sim.AwardKillXp(Kill(bounty, "hero_a"), "acct_kelly");

            Assert.Multiple(() =>
            {
                Assert.That(result.XpAwarded, Is.EqualTo((double)bounty), "R-40: XP awarded is the bounty");
                Assert.That(result.LifetimeXp, Is.EqualTo(expectedLifetime));
                Assert.That(result.Level, Is.EqualTo(expectedLevel));
                Assert.That(result.LeveledUp, Is.EqualTo(expectedLeveledUp));
                Assert.That(result.SkillPoints, Is.EqualTo(expectedPoints));
                Assert.That(result.XpIntoLevel, Is.EqualTo(expectedIntoLevel));
                Assert.That(result.XpForNextLevel, Is.EqualTo(expectedForNextLevel));
            });
        }

        /// <summary>R-41: lifetime XP never decreases and never resets — not at a wave boundary.</summary>
        [Test]
        public void Lifetime_xp_only_ever_grows_across_a_sequence_of_kills()
        {
            var store = new RecordingProfileStore();
            store.Seed(Profile("acct_kelly", 0.0, 1, 0));
            var sim = SimWith(store, ("hero_a", "acct_kelly"));

            double previous = 0.0;
            foreach (var bounty in new[] { 10, 25, 5, 60, 15 })
            {
                var result = sim.AwardKillXp(Kill(bounty, "hero_a"), "acct_kelly");

                Assert.That(result.LifetimeXp, Is.GreaterThanOrEqualTo(previous),
                    "R-41: lifetime XP never decreases");
                Assert.That(result.LifetimeXp, Is.EqualTo(previous + bounty));
                previous = result.LifetimeXp;

                // A wave boundary is not a reset (R-41); the level-up at 100 must not zero the total.
                sim.State.Wave.Number++;
            }

            Assert.That(previous, Is.EqualTo(115.0));
        }

        /// <summary>
        /// R-41's harder half: "not per match". A second MatchSim over the same store — a new match
        /// for the same callsign — continues from the persisted total, level and banked point.
        /// </summary>
        [Test]
        public void A_new_match_continues_the_persisted_level_and_banked_point()
        {
            var store = new RecordingProfileStore();
            store.Seed(Profile("acct_kelly", 90.0, 1, 0));

            var firstMatch = SimWith(store, ("hero_a", "acct_kelly"));
            firstMatch.AwardKillXp(Kill(15, "hero_a", MonsterType.Ravager, "m4"), "acct_kelly");

            var secondMatch = SimWith(store, ("hero_a", "acct_kelly"));
            var carried = secondMatch.AwardKillXp(Kill(10, "hero_a"), "acct_kelly");

            Assert.Multiple(() =>
            {
                Assert.That(carried.LifetimeXp, Is.EqualTo(115.0), "the match boundary is not a reset");
                Assert.That(carried.Level, Is.EqualTo(2));
                Assert.That(carried.LeveledUp, Is.False);
                Assert.That(carried.SkillPoints, Is.EqualTo(1), "the unspent point survived the match");
            });
        }

        // ---- R-40: who gets credited --------------------------------------------------------------

        /// <summary>
        /// R-40: a turret kill credits the placer, never the turret, and never the other player in
        /// the match. The shell decides who placed what, so the sim is handed the placer's hero and
        /// account — what this pins is that the XP lands there and nowhere else.
        /// </summary>
        [Test]
        public void A_turret_kill_credits_the_placer_and_nobody_else()
        {
            var store = new RecordingProfileStore();
            store.Seed(Profile("acct_kelly", 90.0, 1, 0));
            store.Seed(Profile("acct_tex", 40.0, 1, 0));
            var sim = SimWith(store, ("hero_a", "acct_kelly"), ("hero_b", "acct_tex"));

            sim.State.Placeables["t1"] = new Placeable
            {
                Id = "t1",
                Type = PlaceableType.Turret,
                Pos = new Vec2(0.0, 0.0),
                OwnerPlayerId = "p_hero_a",
                PurchaseCost = 150,
                Hp = 100.0,
            };

            var result = sim.AwardKillXp(Kill(15, "hero_a", MonsterType.Ravager, "m4"), "acct_kelly");

            Assert.Multiple(() =>
            {
                Assert.That(result.HeroId, Is.EqualTo("hero_a"), "the placer is credited, not the turret");
                Assert.That(result.LifetimeXp, Is.EqualTo(105.0));
                Assert.That(store.SavedAccountIds, Is.EqualTo(new[] { "acct_kelly" }),
                    "only the credited account is written");
                Assert.That(sim.LastObservation.StateChanges.Select(c => c.Entity), Has.No.Member("acct_tex"));
                Assert.That(store.Current("acct_tex").LifetimeXp, Is.EqualTo(40.0),
                    "the uninvolved player earns nothing from someone else's turret");
            });
        }

        // ---- R-42: spending points ----------------------------------------------------------------

        /// <summary>
        /// R-42 is free choice: unlock order is not forced, so unlocking Q with E already unlocked is
        /// legal and must leave E alone. G-025 only shows the E-first direction.
        /// </summary>
        [Test]
        public void Unlocking_Q_after_E_is_accepted_and_leaves_E_alone()
        {
            var store = new RecordingProfileStore();
            store.Seed(Profile("acct_kelly", 300.0, 3, 1, q: 0, e: 1));
            var sim = SimWith(store, ("hero_a", "acct_kelly"));

            var result = sim.SpendSkillPoint(Spend("acct_kelly", "hero_a", "unlock_Q"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.True, "R-42 does not force an unlock order");
                Assert.That(result.SkillPointsAfter, Is.EqualTo(0));
                Assert.That(result.Abilities["Q"], Is.EqualTo(1));
                Assert.That(result.Abilities["E"], Is.EqualTo(1), "spending on Q must not disturb E");
                Assert.That(store.SaveCount, Is.EqualTo(1), "an accepted spend is persisted (G-025)");
            });
        }

        /// <summary>
        /// R-42 / DEC-014: ranks climb to SimConfig.MaxAbilityRank and stop. The rejection *reason*
        /// for over-ranking is specified nowhere, so only its presence is asserted — the graded
        /// string "no_skill_points" belongs to G-026's case, not this one.
        /// </summary>
        [Test]
        public void Ranks_climb_to_the_configured_maximum_and_stop_there()
        {
            var store = new RecordingProfileStore();
            store.Seed(Profile("acct_kelly", 600.0, 4, 3, q: 0, e: 1));
            var sim = SimWith(store, ("hero_a", "acct_kelly"));
            Assert.That(sim.Config.MaxAbilityRank, Is.EqualTo(3), "the ceiling this test is about");

            var second = sim.SpendSkillPoint(Spend("acct_kelly", "hero_a", "rank_E"));
            var third = sim.SpendSkillPoint(Spend("acct_kelly", "hero_a", "rank_E"));
            var fourth = sim.SpendSkillPoint(Spend("acct_kelly", "hero_a", "rank_E"));

            Assert.Multiple(() =>
            {
                Assert.That(second.Accepted, Is.True);
                Assert.That(second.Abilities["E"], Is.EqualTo(2));
                Assert.That(second.SkillPointsAfter, Is.EqualTo(2));

                Assert.That(third.Accepted, Is.True);
                Assert.That(third.Abilities["E"], Is.EqualTo(3));
                Assert.That(third.SkillPointsAfter, Is.EqualTo(1));

                Assert.That(fourth.Accepted, Is.False, "rank 4 is past the maximum");
                Assert.That(fourth.SkillPointsAfter, Is.EqualTo(1), "a rejected spend keeps the point banked");
                Assert.That(fourth.RejectionReason, Is.Not.Null.And.Not.Empty, "shape only: the wording is unspecified");
            });

            Assert.That(sim.LastObservation.StateChanges, Is.Empty, "the rejected rank-up changed nothing");
            Assert.That(store.SaveCount, Is.EqualTo(2), "the two accepted spends wrote; the rejected one did not");
        }

        /// <summary>
        /// A rejected spend is inert everywhere: no state change, no event-side external call, no
        /// write. Only G-026's exact case pins a reason string, so these assert presence and shape.
        /// </summary>
        [TestCase(0, 0, 0, "unlock_Q", TestName = "unlock with no points banked is rejected")]
        [TestCase(0, 1, 0, "rank_Q", TestName = "rank-up with no points banked is rejected")]
        public void Rejected_spends_change_nothing_and_never_write(
            int skillPoints, int rankQ, int rankE, string choice)
        {
            var store = new RecordingProfileStore();
            store.Seed(Profile("acct_tex", 120.0, 2, skillPoints, q: rankQ, e: rankE));
            var sim = SimWith(store, ("hero_b", "acct_tex"));

            var result = sim.SpendSkillPoint(Spend("acct_tex", "hero_b", choice));

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.False);
                Assert.That(result.Choice, Is.EqualTo(choice), "the result echoes what was asked for");
                Assert.That(result.SkillPointsAfter, Is.EqualTo(skillPoints));
                Assert.That(result.RejectionReason, Is.Not.Null.And.Not.Empty);
                Assert.That(sim.LastObservation.StateChanges, Is.Empty);
                Assert.That(sim.LastObservation.ExternalCalls, Is.Empty);
                Assert.That(store.SaveCount, Is.EqualTo(0));
            });
        }

        /// <summary>
        /// Ranking up an ability that is still locked is described by neither the PRD nor a fixture.
        /// Rather than guess a verdict, this pins internal consistency: whichever way the sim rules,
        /// the point, the observation and the store must all agree with the answer it gave.
        /// </summary>
        [Test]
        public void Ranking_up_a_still_locked_ability_has_a_defined_outcome()
        {
            var store = new RecordingProfileStore();
            store.Seed(Profile("acct_kelly", 100.0, 2, 1, q: 0, e: 0));
            var sim = SimWith(store, ("hero_a", "acct_kelly"));

            var result = sim.SpendSkillPoint(Spend("acct_kelly", "hero_a", "rank_Q"));

            if (result.Accepted)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(result.SkillPointsAfter, Is.EqualTo(0), "an accepted spend consumes the point");
                    Assert.That(result.Abilities["Q"], Is.GreaterThan(0));
                    Assert.That(store.SaveCount, Is.EqualTo(1));
                });
            }
            else
            {
                Assert.Multiple(() =>
                {
                    Assert.That(result.SkillPointsAfter, Is.EqualTo(1), "a rejected spend keeps the point");
                    Assert.That(result.RejectionReason, Is.Not.Null.And.Not.Empty);
                    Assert.That(sim.LastObservation.StateChanges, Is.Empty);
                    Assert.That(store.SaveCount, Is.EqualTo(0));
                });
            }
        }

        /// <summary>R-42: points may be banked — two level-ups with no spend leave two points.</summary>
        [Test]
        public void Points_bank_across_level_ups_until_they_are_spent()
        {
            var store = new RecordingProfileStore();
            store.Seed(Profile("acct_kelly", 0.0, 1, 0));
            var sim = SimWith(store, ("hero_a", "acct_kelly"));

            var first = sim.AwardKillXp(Kill(100, "hero_a", MonsterType.Ravager, "m4"), "acct_kelly");
            var second = sim.AwardKillXp(Kill(200, "hero_a", MonsterType.BullBehemoth, "m9"), "acct_kelly");
            var spent = sim.SpendSkillPoint(Spend("acct_kelly", "hero_a", "unlock_E"));

            Assert.Multiple(() =>
            {
                Assert.That(first.Level, Is.EqualTo(2));
                Assert.That(first.SkillPoints, Is.EqualTo(1));
                Assert.That(second.Level, Is.EqualTo(3));
                Assert.That(second.SkillPoints, Is.EqualTo(2), "R-42: an unspent point banks");
                Assert.That(spent.Accepted, Is.True);
                Assert.That(spent.SkillPointsAfter, Is.EqualTo(1), "one spend consumes exactly one point");
            });
        }
    }

    /// <summary>
    /// R-44: the production profile store is server-local and keyed by callsign, with no password and
    /// no auth. These tests are about persistence itself — an in-memory dictionary would satisfy
    /// round-tripping, so the load-bearing case is the one that reads back through a second instance.
    ///
    /// Nothing here asserts the on-disk format: R-44 allows SQLite or JSON, and the layout inside the
    /// file is the implementer's business.
    /// </summary>
    [TestFixture]
    public class T09_JsonProfileStoreTests
    {
        private string _directory;
        private string _path;

        [SetUp]
        public void CreateTemporaryStoreDirectory()
        {
            // Never the repo: the store writes real files, so it gets a directory this test owns.
            _directory = Path.Combine(Path.GetTempPath(), "redhollow-t09-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "profiles.json");
        }

        [TearDown]
        public void DeleteTemporaryStoreDirectory()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        private static AccountProfile Profile(
            string accountId, double lifetimeXp, int level, int skillPoints, int q, int e)
        {
            var profile = new AccountProfile
            {
                AccountId = accountId,
                LifetimeXp = lifetimeXp,
                Level = level,
                SkillPoints = skillPoints,
            };

            profile.Abilities["Q"] = q;
            profile.Abilities["E"] = e;
            return profile;
        }

        [Test]
        public void Save_then_load_round_trips_every_progression_field()
        {
            var store = new JsonProfileStore(_path);
            store.Save(Profile("kelly", 275.0, 2, 3, q: 2, e: 1));

            var loaded = store.Load("kelly");

            Assert.Multiple(() =>
            {
                Assert.That(loaded.AccountId, Is.EqualTo("kelly"));
                Assert.That(loaded.LifetimeXp, Is.EqualTo(275.0));
                Assert.That(loaded.Level, Is.EqualTo(2));
                Assert.That(loaded.SkillPoints, Is.EqualTo(3));
                Assert.That(loaded.Abilities["Q"], Is.EqualTo(2));
                Assert.That(loaded.Abilities["E"], Is.EqualTo(1));
            });

            Assert.That(Directory.EnumerateFileSystemEntries(_directory), Is.Not.Empty,
                "R-44: the store is server-local — a save lands under the path it was given");
        }

        /// <summary>
        /// The point of "server-local" (R-44): the profile outlives the object that wrote it. A second
        /// store over the same backing file must see what the first one saved.
        /// </summary>
        [Test]
        public void A_second_store_over_the_same_file_reads_back_what_the_first_wrote()
        {
            new JsonProfileStore(_path).Save(Profile("kelly", 610.0, 4, 1, q: 3, e: 2));

            var reopened = new JsonProfileStore(_path).Load("kelly");

            Assert.Multiple(() =>
            {
                Assert.That(reopened.LifetimeXp, Is.EqualTo(610.0));
                Assert.That(reopened.Level, Is.EqualTo(4));
                Assert.That(reopened.SkillPoints, Is.EqualTo(1));
                Assert.That(reopened.Abilities["Q"], Is.EqualTo(3));
                Assert.That(reopened.Abilities["E"], Is.EqualTo(2));
            });
        }

        /// <summary>R-44: the key is the callsign, so two callsigns are two independent profiles.</summary>
        [Test]
        public void Two_callsigns_do_not_collide()
        {
            var store = new JsonProfileStore(_path);
            store.Save(Profile("kelly", 275.0, 2, 3, q: 2, e: 1));
            store.Save(Profile("tex", 60.0, 1, 0, q: 0, e: 0));

            Assert.Multiple(() =>
            {
                Assert.That(store.Load("kelly").LifetimeXp, Is.EqualTo(275.0));
                Assert.That(store.Load("kelly").Abilities["Q"], Is.EqualTo(2));
                Assert.That(store.Load("tex").LifetimeXp, Is.EqualTo(60.0));
                Assert.That(store.Load("tex").Abilities["Q"], Is.EqualTo(0));
            });
        }

        /// <summary>
        /// R-44 is trust-based and passwordless: an unrecognised callsign is a brand new player, not
        /// an error. Matches InMemoryProfileStore's semantics so the two stores are interchangeable.
        /// </summary>
        [Test]
        public void An_unknown_callsign_loads_a_fresh_account_rather_than_throwing()
        {
            var store = new JsonProfileStore(_path);

            var fresh = store.Load("never-seen-before");

            Assert.Multiple(() =>
            {
                Assert.That(fresh, Is.Not.Null);
                Assert.That(fresh.AccountId, Is.EqualTo("never-seen-before"));
                Assert.That(fresh.LifetimeXp, Is.EqualTo(0.0));
                Assert.That(fresh.Level, Is.EqualTo(1));
                Assert.That(fresh.SkillPoints, Is.EqualTo(0));
                Assert.That(fresh.Abilities["Q"], Is.EqualTo(0), "a fresh account is basic-attack only");
                Assert.That(fresh.Abilities["E"], Is.EqualTo(0));
            });
        }
    }
}
