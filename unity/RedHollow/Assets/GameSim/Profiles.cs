using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// Persistent per-account progression (R-43 / DEC-015). Lifetime XP never resets — not per wave,
    /// not per match (R-41) — so this outlives every MatchState.
    /// </summary>
    public sealed class AccountProfile
    {
        public string AccountId;
        public double LifetimeXp;
        public int Level = 1;
        public int SkillPoints;

        /// <summary>Ability rank per key ("Q", "E"); 0 means still locked (R-42).</summary>
        public readonly Dictionary<string, int> Abilities = new Dictionary<string, int>
        {
            { "Q", 0 },
            { "E", 0 },
        };

        public AccountProfile Clone()
        {
            var copy = new AccountProfile
            {
                AccountId = AccountId,
                LifetimeXp = LifetimeXp,
                Level = Level,
                SkillPoints = SkillPoints,
            };
            copy.Abilities.Clear();
            foreach (var kv in Abilities)
            {
                copy.Abilities[kv.Key] = kv.Value;
            }

            return copy;
        }
    }

    /// <summary>
    /// The injected persistence boundary (R-43/R-44). Production binds server-local SQLite/JSON keyed
    /// by callsign; the golden adapter binds a fixture-backed fake. The sim only ever sees this.
    /// </summary>
    public interface IProfileStore
    {
        AccountProfile Load(string accountId);

        void Save(AccountProfile profile);
    }

    /// <summary>In-memory store. Backs the fixture fake and editor play-throughs.</summary>
    public sealed class InMemoryProfileStore : IProfileStore
    {
        private readonly Dictionary<string, AccountProfile> _profiles =
            new Dictionary<string, AccountProfile>();

        public void Seed(AccountProfile profile)
        {
            _profiles[profile.AccountId] = profile;
        }

        public AccountProfile Load(string accountId)
        {
            if (_profiles.TryGetValue(accountId, out var profile))
            {
                return profile;
            }

            // R-44: an unknown callsign is simply a fresh account, basic-attack only.
            var fresh = new AccountProfile { AccountId = accountId };
            _profiles[accountId] = fresh;
            return fresh;
        }

        public void Save(AccountProfile profile)
        {
            _profiles[profile.AccountId] = profile;
        }
    }
}
