using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// The acceptance harness: one NUnit case per golden fixture, each one loading its `given`
    /// through production types, calling the real MatchSim entry point named by `when.operation`,
    /// and deep-comparing the resulting observation to `expect.exact` after canonicalization.
    ///
    /// Every later ticket is graded by this class, so it deliberately owns no game rules of its own.
    /// A case that fails with anything other than a mismatch report or the sim's own exception is an
    /// adapter bug, not a product bug.
    /// </summary>
    [TestFixture]
    public class GoldenFixtureTests
    {
        /// <summary>
        /// Discovered from disk, never hardcoded — a fixture added by a later spec change becomes a
        /// case with no edit here. Cases are named with the fixture id first so
        /// `--filter "Name~G-001"` selects exactly one fixture.
        /// </summary>
        public static IEnumerable<TestCaseData> FixtureCases()
        {
            foreach (var path in FixtureCatalog.Paths())
            {
                var fixture = FixtureCatalog.Load(path);
                yield return new TestCaseData(path)
                    .SetName(fixture.Id + " " + fixture.Name + " [" + fixture.FileName + "]");
            }
        }

        [TestCaseSource(nameof(FixtureCases))]
        public void Observation_matches_expect_exact(string fixturePath)
        {
            var fixture = FixtureCatalog.Load(fixturePath);
            var scenario = ScenarioLoader.Load(fixture);

            // Anything the entry point throws propagates: while a rule is unimplemented the case
            // must fail with the sim's own NotImplementedException, which is the whole point of
            // red-first. Catching it here would hide which ticket still owes work.
            OperationDispatch.Invoke(fixture, scenario);

            var expected = GoldenCanonicalizer.Canonicalize(fixture.Expected);
            var actual = GoldenCanonicalizer.Canonicalize(
                Json.FromObject(scenario.Sim.LastObservation.ToFields()));

            var difference = JsonComparison.FirstDifference(expected, actual);
            if (difference != null)
            {
                Assert.Fail(Report(fixture, difference, expected, actual));
            }
        }

        /// <summary>
        /// The message every downstream ticket debugs from: which fixture, where it diverged, what
        /// was wanted versus produced, and both full observations for context.
        /// </summary>
        private static string Report(
            Fixture fixture, JsonDifference difference, JsonNode expected, JsonNode actual)
        {
            var report = new StringBuilder();
            report.AppendLine(fixture.Id + " (" + fixture.Name + ", " + fixture.FileName + ") "
                              + "did not match expect.exact.");
            report.AppendLine("  operation : " + fixture.Operation);
            report.AppendLine("  path      : " + difference.Path);
            report.AppendLine("  reason    : " + difference.Reason);
            report.AppendLine("  expected  : " + difference.Expected);
            report.AppendLine("  actual    : " + difference.Actual);
            report.AppendLine();
            report.AppendLine("--- expect.exact (canonicalized) ---");
            report.AppendLine(JsonComparison.Pretty(expected));
            report.AppendLine("--- observed (canonicalized) ---");
            report.AppendLine(JsonComparison.Pretty(actual));
            return report.ToString();
        }
    }
}
