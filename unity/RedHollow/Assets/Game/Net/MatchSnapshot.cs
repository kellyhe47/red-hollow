using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RedHollow.Sim;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 030 — the replication payload for a remote party member (R-50 / R-51 / R-52): one
    /// self-contained capture of everything a CLIENT renders, and the apply that rebuilds a
    /// mirror <see cref="MatchState"/> from it on the other machine.
    ///
    /// The host stays the only authority (R-51): a mirror built by <see cref="Apply"/> is a
    /// picture, never a sim — no <see cref="MatchSim"/> holds it, no command writes it, and the
    /// next snapshot replaces whatever a client thought it knew. Full-state capture rather than
    /// deltas on purpose: the party cap is four (R-50), a state this size is a few kilobytes,
    /// and a client that joins mid-wave (or drops a packet) is correct again one snapshot later
    /// with no journal to replay.
    ///
    /// Hand-rolled JSON for the same reason <see cref="RedHollow.Sim.JsonProfileStore"/> hand
    /// rolls it: netstandard2.1 under Unity has no System.Text.Json without a package the build
    /// does not carry. Doubles travel in round-trip format, invariant culture; ids and callsigns
    /// are escaped (a callsign is user-typed text).
    ///
    /// What is deliberately NOT carried: sim tunables and catalogs (config is compiled into both
    /// builds), hotspot/tunnel geometry (both sides build the colony from the same
    /// <see cref="ColonyMap"/>), per-monster targeting and status internals (rendering reads
    /// pos/hp/alive), and events (a v1 client renders state; feel stingers degrade gracefully).
    /// The planning countdown crosses as a REMAINING number so the client never needs the host's
    /// clock or config to draw the R-63 timer.
    /// </summary>
    public static class MatchSnapshot
    {
        /// <summary>Capture everything a client renders, as one self-contained JSON document.</summary>
        /// <param name="state">The host-authoritative world.</param>
        /// <param name="planningRemainingSeconds">
        /// The R-63 countdown as the host computes it (authoritative clock + config); zero
        /// outside planning.
        /// </param>
        public static string Capture(MatchState state, double planningRemainingSeconds)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var sb = new StringBuilder(4096);
            sb.Append('{');

            Str(sb, "phase", state.Phase);
            sb.Append(',');
            Str(sb, "status", state.Status);
            sb.Append(',');
            Num(sb, "planning_remaining", planningRemainingSeconds);
            sb.Append(',');

            sb.Append("\"wave\":{");
            Num(sb, "number", state.Wave.Number);
            sb.Append(',');
            Num(sb, "total", state.Wave.TotalWaves);
            sb.Append(',');
            sb.Append("\"living\":[");
            for (var i = 0; i < state.Wave.LivingMonsterIds.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                Quoted(sb, state.Wave.LivingMonsterIds[i]);
            }

            sb.Append("]},");

            Num(sb, "scrip", state.Team.Scrip);
            sb.Append(',');

            sb.Append("\"players\":[");
            var first = true;
            foreach (var player in state.Players)
            {
                if (player == null)
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                sb.Append('{');
                Str(sb, "id", player.Id);
                sb.Append(',');
                Str(sb, "account", player.AccountId);
                sb.Append(',');
                Str(sb, "class", player.HeroClass);
                sb.Append(',');
                Bool(sb, "ready", player.Ready);
                sb.Append(',');
                Bool(sb, "connected", player.Connected);
                sb.Append('}');
            }

            sb.Append("],");

            sb.Append("\"heroes\":[");
            first = true;
            foreach (var hero in state.Heroes.Values)
            {
                if (hero == null)
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                sb.Append('{');
                Str(sb, "id", hero.Id);
                sb.Append(',');
                Str(sb, "class", hero.HeroClass);
                sb.Append(',');
                Str(sb, "account", hero.AccountId);
                sb.Append(',');
                Num(sb, "x", hero.Pos.X);
                sb.Append(',');
                Num(sb, "y", hero.Pos.Y);
                sb.Append(',');
                Num(sb, "hp", hero.Hp);
                sb.Append(',');
                Num(sb, "max_hp", hero.MaxHp);
                sb.Append(',');
                Bool(sb, "alive", hero.Alive);
                if (hero.RespawnAt.HasValue)
                {
                    sb.Append(',');
                    Num(sb, "respawn_at", hero.RespawnAt.Value);
                }

                sb.Append('}');
            }

            sb.Append("],");

            sb.Append("\"monsters\":[");
            first = true;
            foreach (var monster in state.Monsters.Values)
            {
                if (monster == null)
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                sb.Append('{');
                Str(sb, "id", monster.Id);
                sb.Append(',');
                Str(sb, "type", monster.Type);
                sb.Append(',');
                Num(sb, "x", monster.Pos.X);
                sb.Append(',');
                Num(sb, "y", monster.Pos.Y);
                sb.Append(',');
                Num(sb, "hp", monster.Hp);
                sb.Append(',');
                Bool(sb, "alive", monster.Alive);
                sb.Append('}');
            }

            sb.Append("],");

            sb.Append("\"hotspots\":[");
            first = true;
            foreach (var hotspot in state.Hotspots.Values)
            {
                if (hotspot == null)
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                sb.Append('{');
                Str(sb, "id", hotspot.Id);
                sb.Append(',');
                Num(sb, "civilians", hotspot.Civilians);
                sb.Append('}');
            }

            sb.Append("],");

            sb.Append("\"placeables\":[");
            first = true;
            foreach (var placeable in state.Placeables.Values)
            {
                if (placeable == null)
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                sb.Append('{');
                Str(sb, "id", placeable.Id);
                sb.Append(',');
                Str(sb, "type", placeable.Type);
                sb.Append(',');
                Str(sb, "owner", placeable.OwnerPlayerId);
                sb.Append(',');
                Num(sb, "x", placeable.Pos.X);
                sb.Append(',');
                Num(sb, "y", placeable.Pos.Y);
                sb.Append(',');
                Num(sb, "hp", placeable.Hp);
                sb.Append(',');
                Num(sb, "cost", placeable.PurchaseCost);
                sb.Append(',');
                Num(sb, "triggers", placeable.TriggersRemaining);
                sb.Append(',');
                Bool(sb, "exists", placeable.Exists);
                sb.Append('}');
            }

            sb.Append("]}");

            return sb.ToString();
        }

        /// <summary>
        /// Rebuild <paramref name="into"/> from one captured document. Replace-all semantics per
        /// collection: the snapshot is the whole truth, so an entity the host no longer carries
        /// vanishes from the mirror with it, and a mid-match joiner is complete after one apply.
        /// Returns the host's planning-remaining figure for the R-63 label.
        /// </summary>
        public static double Apply(string snapshot, MatchState into)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            var root = SnapshotJson.Parse(snapshot);

            into.Phase = SnapshotJson.Str(root, "phase") ?? into.Phase;
            into.Status = SnapshotJson.Str(root, "status") ?? into.Status;

            var wave = SnapshotJson.Node(root, "wave");
            if (wave != null)
            {
                into.Wave.Number = (int)SnapshotJson.Num(wave, "number", into.Wave.Number);
                into.Wave.TotalWaves = (int)SnapshotJson.Num(wave, "total", into.Wave.TotalWaves);
                into.Wave.LivingMonsterIds.Clear();
                foreach (var id in SnapshotJson.StrList(wave, "living"))
                {
                    into.Wave.LivingMonsterIds.Add(id);
                }
            }

            into.Team.Scrip = (int)SnapshotJson.Num(root, "scrip", into.Team.Scrip);

            into.Players.Clear();
            foreach (var node in SnapshotJson.NodeList(root, "players"))
            {
                into.Players.Add(new PlayerSlot
                {
                    Id = SnapshotJson.Str(node, "id"),
                    AccountId = SnapshotJson.Str(node, "account"),
                    HeroClass = SnapshotJson.Str(node, "class"),
                    Ready = SnapshotJson.Bool(node, "ready"),
                    Connected = SnapshotJson.Bool(node, "connected"),
                });
            }

            into.Heroes.Clear();
            foreach (var node in SnapshotJson.NodeList(root, "heroes"))
            {
                var hero = new Hero
                {
                    Id = SnapshotJson.Str(node, "id"),
                    HeroClass = SnapshotJson.Str(node, "class"),
                    AccountId = SnapshotJson.Str(node, "account"),
                    Pos = new Vec2(SnapshotJson.Num(node, "x", 0.0), SnapshotJson.Num(node, "y", 0.0)),
                    Hp = SnapshotJson.Num(node, "hp", 0.0),
                    MaxHp = SnapshotJson.Num(node, "max_hp", 0.0),
                    Alive = SnapshotJson.Bool(node, "alive"),
                };
                if (SnapshotJson.Has(node, "respawn_at"))
                {
                    hero.RespawnAt = SnapshotJson.Num(node, "respawn_at", 0.0);
                }

                if (hero.Id != null)
                {
                    into.Heroes[hero.Id] = hero;
                }
            }

            into.Monsters.Clear();
            foreach (var node in SnapshotJson.NodeList(root, "monsters"))
            {
                var monster = new Monster
                {
                    Id = SnapshotJson.Str(node, "id"),
                    Type = SnapshotJson.Str(node, "type"),
                    Pos = new Vec2(SnapshotJson.Num(node, "x", 0.0), SnapshotJson.Num(node, "y", 0.0)),
                    Hp = SnapshotJson.Num(node, "hp", 0.0),
                    Alive = SnapshotJson.Bool(node, "alive"),
                };

                if (monster.Id != null)
                {
                    into.Monsters[monster.Id] = monster;
                }
            }

            // Hotspots keep their map-built positions; only the civilian count is host truth.
            foreach (var node in SnapshotJson.NodeList(root, "hotspots"))
            {
                var id = SnapshotJson.Str(node, "id");
                if (id == null)
                {
                    continue;
                }

                if (!into.Hotspots.TryGetValue(id, out var hotspot) || hotspot == null)
                {
                    hotspot = new Hotspot { Id = id };
                    into.Hotspots[id] = hotspot;
                }

                hotspot.Civilians = (int)SnapshotJson.Num(node, "civilians", hotspot.Civilians);
            }

            into.Placeables.Clear();
            foreach (var node in SnapshotJson.NodeList(root, "placeables"))
            {
                var placeable = new Placeable
                {
                    Id = SnapshotJson.Str(node, "id"),
                    Type = SnapshotJson.Str(node, "type"),
                    OwnerPlayerId = SnapshotJson.Str(node, "owner"),
                    Pos = new Vec2(SnapshotJson.Num(node, "x", 0.0), SnapshotJson.Num(node, "y", 0.0)),
                    Hp = SnapshotJson.Num(node, "hp", 0.0),
                    PurchaseCost = (int)SnapshotJson.Num(node, "cost", 0.0),
                    TriggersRemaining = (int)SnapshotJson.Num(node, "triggers", 0.0),
                    Exists = SnapshotJson.Bool(node, "exists"),
                };

                if (placeable.Id != null)
                {
                    into.Placeables[placeable.Id] = placeable;
                }
            }

            return SnapshotJson.Num(root, "planning_remaining", 0.0);
        }

        // ---- writer helpers ----------------------------------------------------------------------

        private static void Str(StringBuilder sb, string key, string value)
        {
            Quoted(sb, key);
            sb.Append(':');
            if (value == null)
            {
                sb.Append("null");
            }
            else
            {
                Quoted(sb, value);
            }
        }

        private static void Num(StringBuilder sb, string key, double value)
        {
            Quoted(sb, key);
            sb.Append(':');
            sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void Bool(StringBuilder sb, string key, bool value)
        {
            Quoted(sb, key);
            sb.Append(':');
            sb.Append(value ? "true" : "false");
        }

        private static void Quoted(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
        }
    }

    /// <summary>
    /// The minimal JSON reader <see cref="MatchSnapshot.Apply"/> parses with: objects become
    /// dictionaries, arrays become lists, scalars become string/double/bool/null. Small and
    /// recursive; a snapshot is a few kilobytes. Internal to the Net assembly on purpose — this
    /// is a wire-format detail, not a general-purpose parser anybody should reach for.
    /// </summary>
    internal static class SnapshotJson
    {
        public static Dictionary<string, object> Parse(string text)
        {
            var index = 0;
            var value = ParseValue(text, ref index);
            return value as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        public static Dictionary<string, object> Node(Dictionary<string, object> node, string key)
        {
            return node != null && node.TryGetValue(key, out var value)
                ? value as Dictionary<string, object>
                : null;
        }

        public static IEnumerable<Dictionary<string, object>> NodeList(
            Dictionary<string, object> node, string key)
        {
            if (node != null && node.TryGetValue(key, out var value) && value is List<object> list)
            {
                foreach (var entry in list)
                {
                    if (entry is Dictionary<string, object> child)
                    {
                        yield return child;
                    }
                }
            }
        }

        public static IEnumerable<string> StrList(Dictionary<string, object> node, string key)
        {
            if (node != null && node.TryGetValue(key, out var value) && value is List<object> list)
            {
                foreach (var entry in list)
                {
                    if (entry is string s)
                    {
                        yield return s;
                    }
                }
            }
        }

        public static string Str(Dictionary<string, object> node, string key)
        {
            return node != null && node.TryGetValue(key, out var value) ? value as string : null;
        }

        public static double Num(Dictionary<string, object> node, string key, double fallback)
        {
            return node != null && node.TryGetValue(key, out var value) && value is double d
                ? d
                : fallback;
        }

        public static bool Bool(Dictionary<string, object> node, string key)
        {
            return node != null && node.TryGetValue(key, out var value) && value is bool b && b;
        }

        public static bool Has(Dictionary<string, object> node, string key)
        {
            return node != null && node.ContainsKey(key);
        }

        // ---- the parser ------------------------------------------------------------------------

        private static object ParseValue(string text, ref int index)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                return null;
            }

            var c = text[index];
            if (c == '{')
            {
                return ParseObject(text, ref index);
            }

            if (c == '[')
            {
                return ParseArray(text, ref index);
            }

            if (c == '"')
            {
                return ParseString(text, ref index);
            }

            if (c == 't' || c == 'f')
            {
                return ParseKeyword(text, ref index);
            }

            if (c == 'n')
            {
                Expect(text, ref index, "null");
                return null;
            }

            return ParseNumber(text, ref index);
        }

        private static Dictionary<string, object> ParseObject(string text, ref int index)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            index++; // '{'
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == '}')
            {
                index++;
                return result;
            }

            while (index < text.Length)
            {
                SkipWhitespace(text, ref index);
                var key = ParseString(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ':')
                {
                    throw new FormatException("snapshot object: expected ':' after key '" + key + "'");
                }

                index++;
                result[key] = ParseValue(text, ref index);
                SkipWhitespace(text, ref index);

                if (index < text.Length && text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (index < text.Length && text[index] == '}')
                {
                    index++;
                    return result;
                }

                throw new FormatException("snapshot object: expected ',' or '}'");
            }

            throw new FormatException("snapshot object: unterminated");
        }

        private static List<object> ParseArray(string text, ref int index)
        {
            var result = new List<object>();
            index++; // '['
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == ']')
            {
                index++;
                return result;
            }

            while (index < text.Length)
            {
                result.Add(ParseValue(text, ref index));
                SkipWhitespace(text, ref index);

                if (index < text.Length && text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (index < text.Length && text[index] == ']')
                {
                    index++;
                    return result;
                }

                throw new FormatException("snapshot array: expected ',' or ']'");
            }

            throw new FormatException("snapshot array: unterminated");
        }

        private static string ParseString(string text, ref int index)
        {
            if (index >= text.Length || text[index] != '"')
            {
                throw new FormatException("snapshot string: expected '\"' at " + index);
            }

            index++;
            var sb = new StringBuilder();
            while (index < text.Length)
            {
                var c = text[index++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (index >= text.Length)
                {
                    break;
                }

                var escape = text[index++];
                switch (escape)
                {
                    case '"':
                        sb.Append('"');
                        break;
                    case '\\':
                        sb.Append('\\');
                        break;
                    case '/':
                        sb.Append('/');
                        break;
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    case 'u':
                        if (index + 4 <= text.Length)
                        {
                            sb.Append((char)int.Parse(
                                text.Substring(index, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture));
                            index += 4;
                        }

                        break;
                    default:
                        sb.Append(escape);
                        break;
                }
            }

            throw new FormatException("snapshot string: unterminated");
        }

        private static object ParseKeyword(string text, ref int index)
        {
            if (text[index] == 't')
            {
                Expect(text, ref index, "true");
                return true;
            }

            Expect(text, ref index, "false");
            return false;
        }

        private static object ParseNumber(string text, ref int index)
        {
            var start = index;
            while (index < text.Length)
            {
                var c = text[index];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                {
                    index++;
                }
                else
                {
                    break;
                }
            }

            return double.Parse(
                text.Substring(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static void Expect(string text, ref int index, string keyword)
        {
            if (index + keyword.Length > text.Length
                || string.CompareOrdinal(text, index, keyword, 0, keyword.Length) != 0)
            {
                throw new FormatException("snapshot: expected '" + keyword + "' at " + index);
            }

            index += keyword.Length;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }
    }
}
