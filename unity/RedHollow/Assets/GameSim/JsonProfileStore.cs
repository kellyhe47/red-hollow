using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RedHollow.Sim
{
    /// <summary>
    /// R-44 / DEC-015: the production profile store. Accounts are callsign strings with no password
    /// and no auth, so persistence is nothing more than a server-local document keyed by callsign.
    /// The trust-based design is deliberate and documented as spoofable for v1.
    ///
    /// Deliberately dependency-free: GameSim targets netstandard2.1 and is compiled by Unity too, so
    /// System.Text.Json is not available here without a package reference the Unity build would not
    /// have. The reader and writer below are hand-rolled instead — a profile is four scalars and two
    /// ability ranks, and the file is small enough to rewrite whole on every save.
    ///
    /// The document is re-read on every operation rather than cached, because "server-local" (R-44)
    /// means a second store over the same file must see what the first one wrote.
    /// </summary>
    public sealed class JsonProfileStore : IProfileStore
    {
        private const string SchemaVersionKey = "schema_version";
        private const string ProfilesKey = "profiles";
        private const string LifetimeXpKey = "lifetime_xp";
        private const string LevelKey = "level";
        private const string SkillPointsKey = "skill_points";
        private const string AbilitiesKey = "abilities";
        private const int SchemaVersion = 1;

        /// <param name="filePath">
        /// Absolute path of the JSON document holding every callsign's profile. The store owns this
        /// file: it must create it on first save and read it back on construction or on load, which
        /// is what makes "server-local" (R-44) survive a process restart.
        /// </param>
        public JsonProfileStore(string filePath)
        {
            FilePath = filePath;
        }

        public string FilePath { get; }

        public AccountProfile Load(string accountId)
        {
            AccountProfile stored;
            if (ReadAll().TryGetValue(accountId ?? string.Empty, out stored))
            {
                return stored;
            }

            // R-44 is passwordless and trust-based: an unrecognised callsign is a brand new player,
            // not an error. Mirrors InMemoryProfileStore so the two are interchangeable.
            return new AccountProfile { AccountId = accountId };
        }

        public void Save(AccountProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            // Read-modify-write the whole document: the file is one record per callsign and saving
            // one account must not drop the others (R-44 — two callsigns never collide).
            var profiles = ReadAll();
            profiles[profile.AccountId ?? string.Empty] = profile;
            WriteAll(profiles);
        }

        // ---- document i/o -------------------------------------------------------------------------

        private Dictionary<string, AccountProfile> ReadAll()
        {
            var profiles = new Dictionary<string, AccountProfile>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            {
                return profiles;
            }

            var text = File.ReadAllText(FilePath, Encoding.UTF8);
            if (string.IsNullOrEmpty(text.Trim()))
            {
                return profiles;
            }

            var cursor = new JsonCursor(text);
            cursor.Expect('{');
            if (!cursor.TryConsume('}'))
            {
                do
                {
                    var key = cursor.ReadString();
                    cursor.Expect(':');
                    if (key == ProfilesKey)
                    {
                        ReadProfiles(cursor, profiles);
                    }
                    else
                    {
                        // Forwards compatible: a key written by a newer schema is skipped, not fatal.
                        cursor.SkipValue();
                    }
                }
                while (cursor.TryConsume(','));

                cursor.Expect('}');
            }

            return profiles;
        }

        private static void ReadProfiles(JsonCursor cursor, IDictionary<string, AccountProfile> profiles)
        {
            cursor.Expect('{');
            if (cursor.TryConsume('}'))
            {
                return;
            }

            do
            {
                var accountId = cursor.ReadString();
                cursor.Expect(':');
                profiles[accountId] = ReadProfile(cursor, accountId);
            }
            while (cursor.TryConsume(','));

            cursor.Expect('}');
        }

        private static AccountProfile ReadProfile(JsonCursor cursor, string accountId)
        {
            // Starts from the fresh-account defaults, so a field absent from the document reads back
            // as the value a new player would have rather than as zero-level nonsense.
            var profile = new AccountProfile { AccountId = accountId };

            cursor.Expect('{');
            if (cursor.TryConsume('}'))
            {
                return profile;
            }

            do
            {
                var field = cursor.ReadString();
                cursor.Expect(':');
                switch (field)
                {
                    case LifetimeXpKey:
                        profile.LifetimeXp = cursor.ReadNumber();
                        break;
                    case LevelKey:
                        profile.Level = (int)cursor.ReadNumber();
                        break;
                    case SkillPointsKey:
                        profile.SkillPoints = (int)cursor.ReadNumber();
                        break;
                    case AbilitiesKey:
                        ReadAbilities(cursor, profile);
                        break;
                    default:
                        cursor.SkipValue();
                        break;
                }
            }
            while (cursor.TryConsume(','));

            cursor.Expect('}');
            return profile;
        }

        private static void ReadAbilities(JsonCursor cursor, AccountProfile profile)
        {
            cursor.Expect('{');
            if (cursor.TryConsume('}'))
            {
                return;
            }

            do
            {
                var ability = cursor.ReadString();
                cursor.Expect(':');
                profile.Abilities[ability] = (int)cursor.ReadNumber();
            }
            while (cursor.TryConsume(','));

            cursor.Expect('}');
        }

        private void WriteAll(IDictionary<string, AccountProfile> profiles)
        {
            var builder = new StringBuilder();
            builder.Append("{\"").Append(SchemaVersionKey).Append("\":")
                .Append(SchemaVersion.ToString(CultureInfo.InvariantCulture))
                .Append(",\"").Append(ProfilesKey).Append("\":{");

            var first = true;
            foreach (var entry in profiles)
            {
                builder.Append(first ? "\n    " : ",\n    ");
                first = false;
                AppendString(builder, entry.Key);
                AppendProfile(builder, entry.Value);
            }

            builder.Append(first ? "}}\n" : "\n  }\n}\n");

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // UTF-8 with no BOM: the reader above is byte-order-mark agnostic, but the file is also
            // meant to be readable by an operator with `cat`.
            File.WriteAllText(FilePath, builder.ToString(), new UTF8Encoding(false));
        }

        private static void AppendProfile(StringBuilder builder, AccountProfile profile)
        {
            builder.Append(":{\"").Append(LifetimeXpKey).Append("\":").Append(Number(profile.LifetimeXp));
            builder.Append(",\"").Append(LevelKey).Append("\":")
                .Append(profile.Level.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"").Append(SkillPointsKey).Append("\":")
                .Append(profile.SkillPoints.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"").Append(AbilitiesKey).Append("\":{");

            var firstAbility = true;
            foreach (var ability in profile.Abilities)
            {
                if (!firstAbility)
                {
                    builder.Append(',');
                }

                firstAbility = false;
                AppendString(builder, ability.Key);
                builder.Append(':').Append(ability.Value.ToString(CultureInfo.InvariantCulture));
            }

            builder.Append("}}");
        }

        /// <summary>Round-trippable and always a valid JSON number.</summary>
        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>
        /// Writes a JSON string. Callsigns are user-supplied under R-44's no-auth model, so quotes,
        /// backslashes and control characters are escaped rather than trusted.
        /// </summary>
        private static void AppendString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (var c in value ?? string.Empty)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        /// <summary>
        /// A minimal forward-only JSON scanner — enough to read the document this class writes, with
        /// full string unescaping so a callsign survives the round trip verbatim.
        /// </summary>
        private sealed class JsonCursor
        {
            private readonly string _text;
            private int _index;

            internal JsonCursor(string text)
            {
                _text = text;
                _index = 0;
            }

            internal char Peek()
            {
                SkipWhitespace();
                return _index < _text.Length ? _text[_index] : '\0';
            }

            internal bool TryConsume(char expected)
            {
                SkipWhitespace();
                if (_index >= _text.Length || _text[_index] != expected)
                {
                    return false;
                }

                _index++;
                return true;
            }

            internal void Expect(char expected)
            {
                if (!TryConsume(expected))
                {
                    throw new FormatException(
                        "profile document is malformed: expected '" + expected + "' at offset " + _index);
                }
            }

            internal string ReadString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (_index < _text.Length)
                {
                    var c = _text[_index++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (_index >= _text.Length)
                    {
                        break;
                    }

                    var escape = _text[_index++];
                    switch (escape)
                    {
                        case '"':
                            builder.Append('"');
                            break;
                        case '\\':
                            builder.Append('\\');
                            break;
                        case '/':
                            builder.Append('/');
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            builder.Append((char)ushort.Parse(
                                _text.Substring(_index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _index += 4;
                            break;
                        default:
                            throw new FormatException(
                                "profile document is malformed: unsupported escape \\" + escape);
                    }
                }

                throw new FormatException("profile document is malformed: unterminated string");
            }

            internal double ReadNumber()
            {
                SkipWhitespace();
                var start = _index;
                while (_index < _text.Length && "+-.eE0123456789".IndexOf(_text[_index]) >= 0)
                {
                    _index++;
                }

                if (_index == start)
                {
                    throw new FormatException(
                        "profile document is malformed: expected a number at offset " + _index);
                }

                return double.Parse(
                    _text.Substring(start, _index - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            /// <summary>Steps over one value of any kind, so unknown keys cost nothing.</summary>
            internal void SkipValue()
            {
                switch (Peek())
                {
                    case '{':
                        SkipStructure('{', '}');
                        break;
                    case '[':
                        SkipStructure('[', ']');
                        break;
                    case '"':
                        ReadString();
                        break;
                    default:
                        while (_index < _text.Length
                               && ",}]".IndexOf(_text[_index]) < 0
                               && !char.IsWhiteSpace(_text[_index]))
                        {
                            _index++;
                        }

                        break;
                }
            }

            private void SkipStructure(char open, char close)
            {
                Expect(open);
                var depth = 1;
                while (_index < _text.Length && depth > 0)
                {
                    var c = _text[_index];
                    if (c == '"')
                    {
                        ReadString();
                        continue;
                    }

                    _index++;
                    if (c == open)
                    {
                        depth++;
                    }
                    else if (c == close)
                    {
                        depth--;
                    }
                }
            }

            private void SkipWhitespace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                {
                    _index++;
                }
            }
        }
    }
}
