using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RedHollow.Sim.Tests
{
    /// <summary>One `canonicalization` entry from eval/golden-manifest.json.</summary>
    internal sealed class SortRule
    {
        internal string Surface;
        internal IReadOnlyList<string> SortBy;
    }

    /// <summary>
    /// The manifest, read once. It — not the adapter — decides how observations are made comparable,
    /// so a manifest change is picked up without touching code, and a manifest that starts asking for
    /// something the adapter cannot do fails loudly here rather than quietly grading differently.
    /// </summary>
    internal static class GoldenManifest
    {
        private const string SupportedComparison = "deep_equal_after_canonicalization";

        /// <summary>The four observation surfaces `expect.exact` is written in terms of.</summary>
        private static readonly string[] Surfaces =
        {
            "result", "state_changes", "emitted_events", "external_calls",
        };

        internal static readonly IReadOnlyList<SortRule> SortRules = LoadSortRules();

        private static IReadOnlyList<SortRule> LoadSortRules()
        {
            var manifest = FixtureCatalog.LoadManifest();
            var comparison = Json.Str(manifest, "comparison");
            if (comparison != SupportedComparison)
            {
                throw new FixtureContractException(
                    "golden-manifest.json asks for comparison \"" + comparison
                    + "\"; this adapter implements \"" + SupportedComparison + "\"");
            }

            var rules = new List<SortRule>();
            foreach (var entry in Json.Arr(manifest, "canonicalization"))
            {
                var path = Json.Str(entry, "path");

                // The manifest writes the state-change surface as "result.state_changes": `result`
                // there means the observation record, not the record's own `result` field. Taking
                // the last segment resolves both spellings onto the surface being sorted.
                var surface = path.Split('.').Last();
                if (!Surfaces.Contains(surface))
                {
                    throw new FixtureContractException(
                        "golden-manifest.json canonicalizes \"" + path
                        + "\", which names no observation surface the adapter captures");
                }

                rules.Add(new SortRule
                {
                    Surface = surface,
                    SortBy = Json.Arr(entry, "sort_by").Select(n => n.GetValue<string>()).ToList(),
                });
            }

            return rules;
        }
    }

    /// <summary>
    /// Applies the manifest's canonicalization to an observation-shaped JSON object.
    ///
    /// Only the surfaces the manifest names are reordered. `external_calls` and any array nested
    /// inside a single result value keep their declared order — for those, order is the contract.
    /// </summary>
    internal static class GoldenCanonicalizer
    {
        /// <summary>Keeps ("a", "bc") from sorting as ("ab", "c"); no id contains this character.</summary>
        private const char Separator = '\u001F';

        internal static JsonNode Canonicalize(JsonNode observation)
        {
            var copy = observation?.DeepClone();
            if (!(copy is JsonObject obj))
            {
                return copy;
            }

            foreach (var rule in GoldenManifest.SortRules)
            {
                if (obj[rule.Surface] is JsonArray array)
                {
                    obj[rule.Surface] = Sorted(array, rule.SortBy);
                }
            }

            return copy;
        }

        private static JsonArray Sorted(JsonArray array, IReadOnlyList<string> sortBy)
        {
            // Elements must be detached from their parent before they can join a new array.
            var detached = array.Select(node => node?.DeepClone()).ToList();
            var ordered = detached
                .Select((node, index) => new { node, index })
                .OrderBy(item => SortKey(item.node, sortBy), StringComparer.Ordinal)
                .ThenBy(item => item.index)
                .Select(item => item.node)
                .ToArray();
            return new JsonArray(ordered);
        }

        private static string SortKey(JsonNode node, IReadOnlyList<string> sortBy)
        {
            var key = new StringBuilder();
            foreach (var field in sortBy)
            {
                key.Append(Scalar(Json.Node(node, field)));
                key.Append(Separator);
            }

            return key.ToString();
        }

        private static string Scalar(JsonNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            switch (node.GetValueKind())
            {
                case JsonValueKind.String:
                    return node.GetValue<string>();
                case JsonValueKind.Number:
                    return node.GetValue<double>().ToString("R", CultureInfo.InvariantCulture);
                default:
                    return node.ToJsonString();
            }
        }
    }

    /// <summary>The first place two observations disagree, described well enough to act on.</summary>
    internal sealed class JsonDifference
    {
        internal string Path;
        internal string Reason;
        internal string Expected;
        internal string Actual;
    }

    /// <summary>
    /// Deep-equality with the tolerances the contract actually needs: numbers compare within a small
    /// epsilon (distances come out of Math.Sqrt), everything else compares exactly, key sets must
    /// match exactly, and array lengths must match exactly. Returns the FIRST disagreement rather
    /// than a boolean, because every later ticket is debugged from this message.
    /// </summary>
    internal static class JsonComparison
    {
        private const double Epsilon = 1e-9;

        internal static JsonDifference FirstDifference(JsonNode expected, JsonNode actual) =>
            Compare(expected, actual, "$");

        private static JsonDifference Compare(JsonNode expected, JsonNode actual, string path)
        {
            var expectedKind = Kind(expected);
            var actualKind = Kind(actual);

            if (expectedKind != actualKind)
            {
                // true/false are distinct kinds; report those as a plain value mismatch.
                var reason = IsBoolean(expectedKind) && IsBoolean(actualKind)
                    ? "boolean value differs"
                    : "expected a JSON " + Describe(expectedKind) + " but got a JSON " + Describe(actualKind);
                return Difference(path, reason, expected, actual);
            }

            switch (expectedKind)
            {
                case JsonValueKind.Object:
                    return CompareObjects((JsonObject)expected, (JsonObject)actual, path);
                case JsonValueKind.Array:
                    return CompareArrays((JsonArray)expected, (JsonArray)actual, path);
                case JsonValueKind.String:
                    return expected.GetValue<string>() == actual.GetValue<string>()
                        ? null
                        : Difference(path, "string value differs", expected, actual);
                case JsonValueKind.Number:
                    return NumbersMatch(expected.GetValue<double>(), actual.GetValue<double>())
                        ? null
                        : Difference(path, "number differs by more than the 1e-9 tolerance", expected, actual);
                default:
                    // Null / True / False carry no payload beyond their kind, already compared.
                    return null;
            }
        }

        private static JsonDifference CompareObjects(JsonObject expected, JsonObject actual, string path)
        {
            var expectedKeys = expected.Select(kv => kv.Key).ToList();
            var actualKeys = actual.Select(kv => kv.Key).ToList();

            var missing = expectedKeys.Where(k => !actual.ContainsKey(k)).ToList();
            var extra = actualKeys.Where(k => !expected.ContainsKey(k)).ToList();
            if (missing.Count > 0 || extra.Count > 0)
            {
                var reason = "object keys differ"
                    + (missing.Count > 0 ? "; missing: " + string.Join(", ", missing) : string.Empty)
                    + (extra.Count > 0 ? "; unexpected: " + string.Join(", ", extra) : string.Empty);
                return new JsonDifference
                {
                    Path = path,
                    Reason = reason,
                    Expected = "keys [" + string.Join(", ", expectedKeys) + "]",
                    Actual = "keys [" + string.Join(", ", actualKeys) + "]",
                };
            }

            foreach (var key in expectedKeys)
            {
                var difference = Compare(expected[key], actual[key], path + "." + key);
                if (difference != null)
                {
                    return difference;
                }
            }

            return null;
        }

        private static JsonDifference CompareArrays(JsonArray expected, JsonArray actual, string path)
        {
            if (expected.Count != actual.Count)
            {
                return new JsonDifference
                {
                    Path = path,
                    Reason = "array length differs",
                    Expected = expected.Count + " element(s): " + expected.ToJsonString(),
                    Actual = actual.Count + " element(s): " + actual.ToJsonString(),
                };
            }

            for (var i = 0; i < expected.Count; i++)
            {
                var difference = Compare(expected[i], actual[i], path + "[" + i + "]");
                if (difference != null)
                {
                    return difference;
                }
            }

            return null;
        }

        private static bool NumbersMatch(double expected, double actual)
        {
            if (expected.Equals(actual))
            {
                return true;
            }

            var scale = Math.Max(1.0, Math.Max(Math.Abs(expected), Math.Abs(actual)));
            return Math.Abs(expected - actual) <= Epsilon * scale;
        }

        private static JsonDifference Difference(string path, string reason, JsonNode expected, JsonNode actual) =>
            new JsonDifference
            {
                Path = path,
                Reason = reason,
                Expected = Render(expected),
                Actual = Render(actual),
            };

        private static JsonValueKind Kind(JsonNode node) => node == null ? JsonValueKind.Null : node.GetValueKind();

        private static bool IsBoolean(JsonValueKind kind) => kind == JsonValueKind.True || kind == JsonValueKind.False;

        private static string Describe(JsonValueKind kind) => kind.ToString().ToLowerInvariant();

        internal static string Render(JsonNode node) => node == null ? "null" : node.ToJsonString();

        internal static string Pretty(JsonNode node) =>
            node == null ? "null" : node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
