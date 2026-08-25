using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Self-tests for the grading machinery.
    ///
    /// While every rule is unimplemented the 30 fixture cases throw before they ever reach the
    /// canonicalizer or the comparer, which would leave the part of the harness that decides
    /// pass/fail completely unexercised. These tests close that hole: they prove the comparer says
    /// "equal" for observations a correct sim would produce, says "different" for each way an
    /// implementation can be wrong, and absorbs exactly the reordering the manifest allows.
    ///
    /// They contain no game rules — the observations are transcribed from fixtures, not derived.
    /// </summary>
    [TestFixture]
    public class GoldenComparisonTests
    {
        // ---- the comparer accepts what a correct sim would emit -----------------------------------

        /// <summary>
        /// Every fixture's own expectation must canonicalize to something equal to itself. Cheap, but
        /// it runs the comparer over all 30 real shapes — nulls, nested arrays, mixed numerics.
        /// </summary>
        [Test]
        public void Every_expectation_is_equal_to_itself_after_canonicalization()
        {
            foreach (var path in FixtureCatalog.Paths())
            {
                var fixture = FixtureCatalog.Load(path);
                var left = GoldenCanonicalizer.Canonicalize(fixture.Expected);
                var right = GoldenCanonicalizer.Canonicalize(fixture.Expected);

                var difference = JsonComparison.FirstDifference(left, right);
                Assert.That(difference, Is.Null,
                    fixture.Id + " did not compare equal to itself at " + difference?.Path);
            }
        }

        /// <summary>
        /// End-to-end proof that the capture path grades a correct implementation as passing:
        /// an observation assembled from production types (SimObservation, StateChange, SimEvent,
        /// ExternalCall, the typed results) must match its fixture's expect.exact exactly.
        /// G-023 is the richest case — all four surfaces populated, including a profile save.
        /// </summary>
        [Test]
        public void A_correct_observation_matches_G_023()
        {
            var result = new XpAwardResult
            {
                HeroId = "hero_a",
                XpAwarded = 15,
                LifetimeXp = 105,
                Level = 2,
                LeveledUp = true,
                SkillPoints = 1,
                XpIntoLevel = 5,
                XpForNextLevel = 200,
            };

            var observation = new SimObservation();
            observation.StateChanges.Add(new StateChange("acct_kelly", "skill_points", 0, 1));
            observation.StateChanges.Add(new StateChange("acct_kelly", "lifetime_xp", 90.0, 105.0));
            observation.StateChanges.Add(new StateChange("acct_kelly", "level", 1, 2));
            observation.EmittedEvents.Add(new SimEvent("level_up", new Dictionary<string, object>
            {
                { "hero_id", "hero_a" }, { "new_level", 2 },
            }));
            observation.EmittedEvents.Add(new SimEvent("xp_awarded", new Dictionary<string, object>
            {
                { "hero_id", "hero_a" }, { "amount", 15 },
            }));
            observation.ExternalCalls.Add(new ExternalCall("profile_store", "save",
                new Dictionary<string, object> { { "account_id", "acct_kelly" } }));

            AssertMatchesFixture("G-023", observation, result);
        }

        /// <summary>Covers a Vec2 inside an event payload and an explicit null result field.</summary>
        [Test]
        public void A_correct_observation_matches_G_013()
        {
            var result = new PurchaseResult
            {
                Accepted = true,
                PlaceableType = PlaceableType.Turret,
                ScripAfter = 50,
                RejectionReason = null,
            };

            var observation = new SimObservation();
            observation.StateChanges.Add(new StateChange("team", "scrip", 300, 50));
            observation.StateChanges.Add(new StateChange("placeables", "count", 0, 1));
            observation.EmittedEvents.Add(new SimEvent("placeable_created", new Dictionary<string, object>
            {
                { "placeable_type", PlaceableType.Turret },
                { "pos", new Vec2(6, 3) },
                { "by", "hero_a" },
            }));

            AssertMatchesFixture("G-013", observation, result);
        }

        /// <summary>Covers a state change whose value is a list of objects (status effects).</summary>
        [Test]
        public void A_correct_observation_matches_G_018()
        {
            var result = new AbilityResult { TargetId = "m4", SpeedAfter = 2.5, SlowExpiresAt = 13.0 };

            var observation = new SimObservation();
            observation.StateChanges.Add(new StateChange("m4", "current_speed", 5.0, 2.5));
            observation.StateChanges.Add(new StateChange("m4", "status_effects",
                new List<IDictionary<string, object>>(),
                new List<IDictionary<string, object>> { new StatusEffect("lasso_slow", 13.0).ToFields() }));
            observation.EmittedEvents.Add(new SimEvent("status_applied", new Dictionary<string, object>
            {
                { "status", "lasso_slow" }, { "target_id", "m4" },
            }));

            AssertMatchesFixture("G-018", observation, result);
        }

        // ---- canonicalization absorbs exactly the reordering the manifest allows -------------------

        [Test]
        public void State_change_and_event_order_do_not_matter()
        {
            var expected = GoldenCanonicalizer.Canonicalize(Expectation("G-008"));
            var shuffled = GoldenCanonicalizer.Canonicalize(
                Reversed(Reversed(Expectation("G-008"), "state_changes"), "emitted_events"));

            Assert.That(JsonComparison.FirstDifference(expected, shuffled), Is.Null);
        }

        [Test]
        public void External_call_order_is_part_of_the_contract()
        {
            var first = Observed("{\"result\":null,\"state_changes\":[],\"emitted_events\":[],"
                                 + "\"external_calls\":[{\"service\":\"profile_store\",\"op\":\"save\"},"
                                 + "{\"service\":\"profile_store\",\"op\":\"load\"}]}");
            var second = Observed("{\"result\":null,\"state_changes\":[],\"emitted_events\":[],"
                                  + "\"external_calls\":[{\"service\":\"profile_store\",\"op\":\"load\"},"
                                  + "{\"service\":\"profile_store\",\"op\":\"save\"}]}");

            var difference = JsonComparison.FirstDifference(
                GoldenCanonicalizer.Canonicalize(first), GoldenCanonicalizer.Canonicalize(second));

            Assert.That(difference, Is.Not.Null, "reordered external_calls must not be treated as equal");
            Assert.That(difference.Path, Is.EqualTo("$.external_calls[0].op"));
        }

        // ---- the comparer rejects every way an implementation can be wrong -------------------------

        [Test]
        public void A_changed_scalar_is_reported_with_its_path()
        {
            var expected = GoldenCanonicalizer.Canonicalize(Expectation("G-001"));
            var actual = GoldenCanonicalizer.Canonicalize(Expectation("G-001"));
            actual["result"]["target_id"] = "hs_saloon";

            var difference = JsonComparison.FirstDifference(expected, actual);

            Assert.That(difference, Is.Not.Null);
            Assert.That(difference.Path, Is.EqualTo("$.result.target_id"));
            Assert.That(difference.Expected, Does.Contain("hero_a"));
            Assert.That(difference.Actual, Does.Contain("hs_saloon"));
        }

        [Test]
        public void A_missing_key_is_a_failure()
        {
            var expected = GoldenCanonicalizer.Canonicalize(Expectation("G-001"));
            var actual = GoldenCanonicalizer.Canonicalize(Expectation("G-001"));
            actual["result"].AsObject().Remove("distance");

            var difference = JsonComparison.FirstDifference(expected, actual);

            Assert.That(difference, Is.Not.Null);
            Assert.That(difference.Path, Is.EqualTo("$.result"));
            Assert.That(difference.Reason, Does.Contain("missing: distance"));
        }

        [Test]
        public void An_extra_key_is_a_failure()
        {
            var expected = GoldenCanonicalizer.Canonicalize(Expectation("G-001"));
            var actual = GoldenCanonicalizer.Canonicalize(Expectation("G-001"));
            actual["result"]["bonus"] = 1;

            var difference = JsonComparison.FirstDifference(expected, actual);

            Assert.That(difference, Is.Not.Null);
            Assert.That(difference.Reason, Does.Contain("unexpected: bonus"));
        }

        [Test]
        public void A_missing_state_change_is_a_failure()
        {
            var expected = GoldenCanonicalizer.Canonicalize(Expectation("G-008"));
            var actual = GoldenCanonicalizer.Canonicalize(Expectation("G-008"));
            actual["state_changes"].AsArray().RemoveAt(0);

            var difference = JsonComparison.FirstDifference(expected, actual);

            Assert.That(difference, Is.Not.Null);
            Assert.That(difference.Path, Is.EqualTo("$.state_changes"));
            Assert.That(difference.Reason, Does.Contain("array length differs"));
        }

        [Test]
        public void A_flipped_boolean_is_a_failure()
        {
            var expected = GoldenCanonicalizer.Canonicalize(Expectation("G-010"));
            var actual = GoldenCanonicalizer.Canonicalize(Expectation("G-010"));
            actual["result"]["map_victory"] = true;

            var difference = JsonComparison.FirstDifference(expected, actual);

            Assert.That(difference, Is.Not.Null);
            Assert.That(difference.Path, Is.EqualTo("$.result.map_victory"));
        }

        [Test]
        public void A_null_where_a_value_was_expected_is_a_failure()
        {
            var expected = GoldenCanonicalizer.Canonicalize(Expectation("G-014"));
            var actual = GoldenCanonicalizer.Canonicalize(Expectation("G-014"));
            actual["result"]["rejection_reason"] = null;

            var difference = JsonComparison.FirstDifference(expected, actual);

            Assert.That(difference, Is.Not.Null);
            Assert.That(difference.Path, Is.EqualTo("$.result.rejection_reason"));
        }

        // ---- numeric tolerance --------------------------------------------------------------------

        [Test]
        public void Float_noise_within_tolerance_still_matches()
        {
            // What Math.Sqrt(3*3 + 4*4) can plausibly land on instead of an exact 5.0.
            var expected = GoldenCanonicalizer.Canonicalize(Expectation("G-001"));
            var actual = GoldenCanonicalizer.Canonicalize(Expectation("G-001"));
            actual["result"]["distance"] = 5.0 + 1e-12;

            Assert.That(JsonComparison.FirstDifference(expected, actual), Is.Null);
        }

        [Test]
        public void A_real_numeric_error_is_still_caught()
        {
            var expected = GoldenCanonicalizer.Canonicalize(Expectation("G-001"));
            var actual = GoldenCanonicalizer.Canonicalize(Expectation("G-001"));
            actual["result"]["distance"] = 5.001;

            var difference = JsonComparison.FirstDifference(expected, actual);

            Assert.That(difference, Is.Not.Null);
            Assert.That(difference.Path, Is.EqualTo("$.result.distance"));
        }

        // ---- helpers ------------------------------------------------------------------------------

        private static void AssertMatchesFixture(string id, SimObservation observation, ISimResult result)
        {
            // Mirrors what MatchSim.Finish does: the typed result IS the `result` surface.
            var fields = observation.ToFields();
            fields["result"] = result.ToFields();

            var expected = GoldenCanonicalizer.Canonicalize(Expectation(id));
            var actual = GoldenCanonicalizer.Canonicalize(Json.FromObject(fields));

            var difference = JsonComparison.FirstDifference(expected, actual);
            Assert.That(difference, Is.Null,
                difference == null
                    ? string.Empty
                    : id + " mismatch at " + difference.Path + ": expected " + difference.Expected
                      + ", got " + difference.Actual);
        }

        private static JsonNode Expectation(string id)
        {
            foreach (var path in FixtureCatalog.Paths())
            {
                var fixture = FixtureCatalog.Load(path);
                if (fixture.Id == id)
                {
                    return fixture.Expected.DeepClone();
                }
            }

            throw new FixtureContractException("no fixture with id " + id);
        }

        private static JsonNode Observed(string json) => JsonNode.Parse(json);

        private static JsonNode Reversed(JsonNode observation, string surface)
        {
            var copy = observation.DeepClone().AsObject();
            var items = copy[surface].AsArray().Select(n => n.DeepClone()).Reverse().ToArray();
            copy[surface] = new JsonArray(items);
            return copy;
        }
    }
}
