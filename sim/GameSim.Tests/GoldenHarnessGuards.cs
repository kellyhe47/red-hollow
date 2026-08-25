using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Guards on the harness itself. Cheap insurance against the failure modes that would make the
    /// whole acceptance suite lie: fixtures silently not discovered, duplicate ids collapsing cases,
    /// the sim acquiring a Unity dependency, or the adapter writing to the spec it grades against.
    /// </summary>
    [TestFixture]
    public class GoldenHarnessGuards
    {
        /// <summary>
        /// Under-discovery is invisible in a green run: 0 cases pass just as loudly as 30. Pin the
        /// count to the files on disk so a fixture that stops being picked up fails here.
        /// </summary>
        [Test]
        public void Every_fixture_file_becomes_a_case()
        {
            var files = Directory.GetFiles(Repo.GoldenDir, "*.json");
            var cases = GoldenFixtureTests.FixtureCases().Count();

            Assert.That(cases, Is.EqualTo(files.Length),
                "eval/golden holds " + files.Length + " fixture file(s) but the adapter discovered "
                + cases + " case(s)");
            Assert.That(cases, Is.GreaterThan(0), "no golden fixtures were discovered at all");
        }

        /// <summary>Two fixtures sharing an id would make `--filter Name~G-0NN` ambiguous.</summary>
        [Test]
        public void Fixture_ids_are_unique_and_present()
        {
            var ids = new List<string>();
            foreach (var path in FixtureCatalog.Paths())
            {
                var fixture = FixtureCatalog.Load(path);
                Assert.That(fixture.Id, Is.Not.Null.And.Not.Empty, path + " declares no id");
                ids.Add(fixture.Id);
            }

            var duplicates = ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.That(duplicates, Is.Empty, "duplicate fixture ids: " + string.Join(", ", duplicates));
        }

        /// <summary>
        /// R-51: the sim is pure C#. This suite runs with no Unity editor on the machine, and the
        /// only way that stays true is the sim assembly referencing nothing from Unity.
        /// </summary>
        [Test]
        public void Sim_assembly_has_no_unity_dependency()
        {
            var unityReferences = typeof(MatchSim).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(name => name != null && name.StartsWith("Unity", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.That(unityReferences, Is.Empty,
                "GameSim referenced " + string.Join(", ", unityReferences) + "; R-51 requires none");
        }

        /// <summary>
        /// eval/ is the spec. Running the whole adapter over every fixture must leave it byte-identical
        /// — the harness reads the contract, it never edits it.
        /// </summary>
        [Test]
        public void Running_every_fixture_leaves_eval_golden_untouched()
        {
            var before = HashEvalTree();

            foreach (var path in FixtureCatalog.Paths())
            {
                var fixture = FixtureCatalog.Load(path);
                try
                {
                    var scenario = ScenarioLoader.Load(fixture);
                    OperationDispatch.Invoke(fixture, scenario);
                    GoldenCanonicalizer.Canonicalize(
                        Json.FromObject(scenario.Sim.LastObservation.ToFields()));
                }
                catch (Exception)
                {
                    // Whether a rule is implemented is the fixture cases' business, not this guard's.
                }

                GoldenCanonicalizer.Canonicalize(fixture.Expected);
            }

            var after = HashEvalTree();

            Assert.That(after.Keys, Is.EquivalentTo(before.Keys), "the set of files under eval/ changed");
            foreach (var file in before.Keys)
            {
                Assert.That(after[file], Is.EqualTo(before[file]), file + " was modified by the adapter");
            }
        }

        /// <summary>
        /// The loader can only be "wrong but quiet" in one interesting way: parsing a fixture without
        /// actually materialising the world its expectations talk about. So every entity named by an
        /// `expect.exact.state_changes` row must exist in the loaded MatchState (or the seeded profile
        /// store) before the operation runs. Without this, a `given` shape the loader silently drops
        /// would not surface until the ticket that implements the rule.
        /// </summary>
        [Test]
        public void Every_entity_the_expectations_name_is_present_after_loading()
        {
            // Aggregates, not entities: these name the match/team/wave records and the placeable
            // count, all of which MatchState exposes as derived or singleton state.
            var aggregates = new HashSet<string>(StringComparer.Ordinal)
            {
                "match", "team", "wave", "placeables",
            };

            var missing = new List<string>();
            foreach (var path in FixtureCatalog.Paths())
            {
                var fixture = FixtureCatalog.Load(path);
                var scenario = ScenarioLoader.Load(fixture);

                foreach (var change in Json.Arr(fixture.Expected, "state_changes"))
                {
                    var entity = Json.Str(change, "entity");
                    if (entity == null || aggregates.Contains(entity) || Resolves(scenario, entity))
                    {
                        continue;
                    }

                    missing.Add(fixture.Id + " expects a change to '" + entity
                                + "', which its `given` did not load into the sim");
                }
            }

            Assert.That(missing, Is.Empty, string.Join("\n", missing));
        }

        private static bool Resolves(Scenario scenario, string entity) =>
            scenario.State.Monsters.ContainsKey(entity)
            || scenario.State.Heroes.ContainsKey(entity)
            || scenario.State.Hotspots.ContainsKey(entity)
            || scenario.State.Placeables.ContainsKey(entity)
            || scenario.State.Players.Any(p => p.Id == entity)
            || scenario.SeededAccounts.Contains(entity);

        private static Dictionary<string, string> HashEvalTree()
        {
            var root = Path.Combine(Repo.Root, "eval");
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                using (var sha = SHA256.Create())
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    hashes[path.Substring(root.Length + 1)] = Convert.ToBase64String(sha.ComputeHash(stream));
                }
            }

            return hashes;
        }
    }
}
