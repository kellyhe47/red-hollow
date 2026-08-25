using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>Locates repo files without depending on the working directory the runner picked.</summary>
    internal static class Repo
    {
        internal static readonly string Root = FindRoot();

        internal static string GoldenDir => Path.Combine(Root, "eval", "golden");

        internal static string ManifestPath => Path.Combine(Root, "eval", "golden-manifest.json");

        private static string FindRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "eval", "golden")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "could not locate the repo root (no eval/golden above " + AppContext.BaseDirectory + ")");
        }
    }

    /// <summary>Small JsonNode readers, so the loader below stays about the domain, not about JSON.</summary>
    internal static class Json
    {
        internal static JsonNode Node(JsonNode parent, string key) => parent?[key];

        internal static bool Has(JsonNode parent, string key) =>
            parent is JsonObject obj && obj.ContainsKey(key) && obj[key] != null;

        internal static string Str(JsonNode parent, string key, string fallback = null) =>
            Has(parent, key) ? parent[key].GetValue<string>() : fallback;

        internal static double Num(JsonNode parent, string key, double fallback = 0.0) =>
            Has(parent, key) ? parent[key].GetValue<double>() : fallback;

        internal static int Int(JsonNode parent, string key, int fallback = 0) =>
            Has(parent, key) ? (int)Math.Round(parent[key].GetValue<double>()) : fallback;

        internal static bool Bool(JsonNode parent, string key, bool fallback = false) =>
            Has(parent, key) ? parent[key].GetValue<bool>() : fallback;

        internal static Vec2 Pos(JsonNode parent, string key)
        {
            if (!Has(parent, key))
            {
                return new Vec2(0, 0);
            }

            var arr = parent[key].AsArray();
            return new Vec2(arr[0].GetValue<double>(), arr[1].GetValue<double>());
        }

        internal static JsonArray Arr(JsonNode parent, string key) =>
            Has(parent, key) ? parent[key].AsArray() : new JsonArray();

        /// <summary>Key set of a JSON object, in declaration order. Empty when the node is absent.</summary>
        internal static IEnumerable<string> Keys(JsonNode parent) =>
            parent is JsonObject obj ? obj.Select(kv => kv.Key).ToList() : (IEnumerable<string>)new List<string>();

        /// <summary>Converts a sim observation's object graph into JsonNode for comparison.</summary>
        internal static JsonNode FromObject(object value)
        {
            switch (value)
            {
                case null:
                    return null;
                case string s:
                    return JsonValue.Create(s);
                case bool b:
                    return JsonValue.Create(b);
                case int i:
                    return JsonValue.Create((double)i);
                case long l:
                    return JsonValue.Create((double)l);
                case float f:
                    return JsonValue.Create((double)f);
                case double d:
                    return JsonValue.Create(d);
                case Vec2 v:
                    return new JsonArray(JsonValue.Create(v.X), JsonValue.Create(v.Y));
                case IDictionary<string, object> dict:
                {
                    var obj = new JsonObject();
                    foreach (var kv in dict)
                    {
                        obj[kv.Key] = FromObject(kv.Value);
                    }

                    return obj;
                }
                case System.Collections.IEnumerable seq:
                {
                    var arr = new JsonArray();
                    foreach (var item in seq)
                    {
                        arr.Add(FromObject(item));
                    }

                    return arr;
                }
                default:
                    return JsonValue.Create(value.ToString());
            }
        }
    }

    /// <summary>One golden fixture, parsed.</summary>
    internal sealed class Fixture
    {
        internal string Id;
        internal string Name;
        internal string FileName;
        internal JsonNode Root;

        internal JsonNode Given => Root["given"];

        internal JsonNode Inputs => Given?["inputs"];

        internal JsonNode Preexisting => Given?["preexisting_state"];

        internal JsonNode Configuration => Given?["configuration"];

        internal JsonNode Clock => Given?["clock"];

        internal string Operation => Root["when"]["operation"].GetValue<string>();

        internal JsonNode Expected => Root["expect"]["exact"];

        public override string ToString() => Id + " " + Name;
    }

    /// <summary>
    /// Enumerates and parses eval/golden at run time. Deliberately never a hardcoded list of 30:
    /// a fixture added by a later spec change must surface as a new NUnit case without the adapter
    /// being edited, and a fixture silently disappearing must show up as a count mismatch.
    ///
    /// Every read here is read-only. eval/ is the acceptance contract; the adapter grades against
    /// it and must never write to it.
    /// </summary>
    internal static class FixtureCatalog
    {
        /// <summary>Absolute paths of every golden fixture, ordered so case order is reproducible.</summary>
        internal static IReadOnlyList<string> Paths()
        {
            var paths = Directory.GetFiles(Repo.GoldenDir, "*.json");
            Array.Sort(paths, StringComparer.Ordinal);
            return paths;
        }

        internal static Fixture Load(string path)
        {
            JsonNode root;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                root = JsonNode.Parse(stream);
            }

            if (root == null)
            {
                throw new InvalidOperationException("fixture parsed to nothing: " + path);
            }

            return new Fixture
            {
                Id = Json.Str(root, "id"),
                Name = Json.Str(root, "name"),
                FileName = Path.GetFileName(path),
                Root = root,
            };
        }

        /// <summary>The manifest, parsed read-only. Its canonicalization rules are contract.</summary>
        internal static JsonNode LoadManifest()
        {
            using (var stream = new FileStream(Repo.ManifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return JsonNode.Parse(stream);
            }
        }
    }
}
