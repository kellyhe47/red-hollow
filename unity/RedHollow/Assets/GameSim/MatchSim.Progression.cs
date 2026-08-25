using System;
using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 009 (T-09) owns this half of <see cref="MatchSim"/>: XP, leveling, skill points
    /// and persistent profiles. Requirements R-40, R-41, R-42, R-43, R-44; graded by fixtures
    /// G-023 through G-026.
    ///
    /// Persistence policy (R-43, reconciled with the fixtures, which outrank the prose): the store
    /// is written on any profile mutation that has to survive the moment it happened — a level-up
    /// (G-023), an accepted spend (G-025) and match end. A kill that only moves the running total
    /// writes nothing (G-024) and a rejected spend writes nothing (G-026); that asymmetry is what
    /// keeps R-43's intent, which is not hammering the store once per kill mid-combat.
    ///
    /// The shared core — fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>The injected persistence boundary, as the fixtures name it (R-43 / R-44).</summary>
        private const string ProfileStoreService = "profile_store";

        private const string SaveOperation = "save";

        private const string UnlockPrefix = "unlock_";

        private const string RankPrefix = "rank_";

        /// <summary>R-40, R-41, R-43 / B-015, B-017. Credit a kill's XP to a player.</summary>
        /// <param name="kill">The kill being scored; R-40 makes its bounty the XP awarded.</param>
        /// <param name="accountId">
        /// The credited account, resolved by the caller — R-40's "turret kills credit the placer" is
        /// a question about who owns the turret, which the shell answers before the sim is called.
        /// </param>
        public XpAwardResult AwardKillXp(MonsterKillRequest kill, string accountId)
        {
            BeginCommand();

            var profile = _profileStore.Load(accountId);
            var awarded = kill == null ? 0.0 : kill.Bounty;
            var heroId = kill != null && !string.IsNullOrEmpty(kill.KillerHeroId)
                ? kill.KillerHeroId
                : HeroForAccount(accountId);

            var previousXp = profile.LifetimeXp;
            var previousLevel = profile.Level;
            var previousPoints = profile.SkillPoints;

            // R-41 / DEC-013: lifetime XP is a running total that never resets — not per wave, not
            // per match — so the level is read off the cumulative curve rather than accumulated.
            var lifetimeXp = previousXp + awarded;
            var level = LevelForXp(lifetimeXp);
            if (level < previousLevel)
            {
                // Defensive: a profile seeded above its curve keeps the level it was given. Levels,
                // like lifetime XP, only ever move forwards.
                level = previousLevel;
            }

            var levelsGained = level - previousLevel;

            // R-42 / DEC-014: one skill point per level gained, and unspent points bank.
            var skillPoints = previousPoints + levelsGained;

            profile.LifetimeXp = lifetimeXp;
            profile.Level = level;
            profile.SkillPoints = skillPoints;

            // State changes are replicated per ACCOUNT (the profile is the thing that changed);
            // events carry the hero, because that is what the client draws the bar and stinger on.
            RecordChange(accountId, "lifetime_xp", previousXp, lifetimeXp);
            RecordChange(accountId, "level", previousLevel, level);
            RecordChange(accountId, "skill_points", previousPoints, skillPoints);

            Emit("xp_awarded", new Dictionary<string, object>
            {
                { "hero_id", heroId },
                { "amount", awarded },
            });

            // One event per level crossed: a single fat bounty can clear several thresholds at once
            // and each of them is a level the player earned.
            for (var gained = 1; gained <= levelsGained; gained++)
            {
                Emit("level_up", new Dictionary<string, object>
                {
                    { "hero_id", heroId },
                    { "new_level", previousLevel + gained },
                });
            }

            if (levelsGained > 0)
            {
                // R-43: the level and its point must survive; one write carries every level the
                // command crossed.
                SaveProfile(profile);
            }

            var currentThreshold = _config.CumulativeXpForLevel(level);
            return Finish(new XpAwardResult
            {
                HeroId = heroId,
                XpAwarded = awarded,
                LifetimeXp = lifetimeXp,
                Level = level,
                LeveledUp = levelsGained > 0,
                SkillPoints = skillPoints,

                // Progress through the CURRENT band: how far past its floor, and how wide it is.
                // Both are derived, never stored — the lifetime total is the only source of truth.
                XpIntoLevel = lifetimeXp - currentThreshold,
                XpForNextLevel = _config.CumulativeXpForLevel(level + 1) - currentThreshold,
            });
        }

        /// <summary>R-42 / B-016. Spend a banked skill point.</summary>
        public SpendSkillPointResult SpendSkillPoint(SpendSkillPointRequest request)
        {
            BeginCommand();

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var accountId = request.AccountId;
            var profile = _profileStore.Load(accountId);

            string ability;
            bool unlocking;
            if (!TryParseChoice(request.Choice, out ability, out unlocking))
            {
                return RejectSpend(request, profile, "invalid_choice");
            }

            int rank;
            if (!profile.Abilities.TryGetValue(ability, out rank))
            {
                rank = 0;
            }

            // Checked first, and fixture-locked by G-026: an unaffordable spend is rejected whatever
            // it asked for. R-42 makes this server-side so a client cannot rank everything at level 1.
            if (profile.SkillPoints <= 0)
            {
                return RejectSpend(request, profile, "no_skill_points");
            }

            if (unlocking && rank > 0)
            {
                return RejectSpend(request, profile, "already_unlocked");
            }

            if (!unlocking && rank <= 0)
            {
                // R-42 defines ranking up only for an ability that is already unlocked; the point
                // stays banked so the player can spend it on the unlock instead.
                return RejectSpend(request, profile, "ability_locked");
            }

            if (rank >= _config.MaxAbilityRank)
            {
                return RejectSpend(request, profile, "max_rank");
            }

            var previousPoints = profile.SkillPoints;
            var newRank = rank + 1;
            profile.Abilities[ability] = newRank;
            profile.SkillPoints = previousPoints - 1;

            // The dotted path is how the fixtures address one ability inside the profile (G-025).
            RecordChange(accountId, "abilities." + ability, rank, newRank);
            RecordChange(accountId, "skill_points", previousPoints, profile.SkillPoints);

            Emit(newRank == 1 ? "ability_unlocked" : "ability_ranked_up", new Dictionary<string, object>
            {
                { "hero_id", request.HeroId },
                { "ability", ability },
                { "rank", newRank },
            });

            // R-43: an accepted allocation is a mutation that must survive the match (G-025).
            SaveProfile(profile);

            return Finish(new SpendSkillPointResult
            {
                Accepted = true,
                Choice = request.Choice,
                SkillPointsAfter = profile.SkillPoints,
                Abilities = SnapshotAbilities(profile),
            });
        }

        /// <summary>R-43 / B-017. Persist every player's profile once the match is over.</summary>
        public void SaveProfilesAtMatchEnd()
        {
            BeginCommand();

            // XP earned since the last level-up has not been written yet (R-43 does not save per
            // kill), so without this the tail of every match would be lost at teardown.
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var player in State.Players)
            {
                var accountId = player.AccountId;
                if (string.IsNullOrEmpty(accountId) || !written.Add(accountId))
                {
                    continue;
                }

                SaveProfile(_profileStore.Load(accountId));
            }
        }

        // ---- helpers ------------------------------------------------------------------------------

        /// <summary>
        /// Writes a profile across the injected boundary and records the call, so the observation
        /// never claims a save the store did not receive (R-43).
        /// </summary>
        private void SaveProfile(AccountProfile profile)
        {
            _profileStore.Save(profile);
            RecordExternalCall(ProfileStoreService, SaveOperation, new Dictionary<string, object>
            {
                { "account_id", profile.AccountId },
            });
        }

        /// <summary>
        /// The highest level whose cumulative threshold <paramref name="lifetimeXp"/> has reached
        /// (R-41 / DEC-013). Walking the curve keeps the one implementation of it in SimConfig.
        /// </summary>
        private int LevelForXp(double lifetimeXp)
        {
            var level = 1;
            var threshold = _config.CumulativeXpForLevel(level);
            while (true)
            {
                var next = _config.CumulativeXpForLevel(level + 1);

                // A non-increasing curve (a zero or negative coefficient) would never terminate.
                if (next <= threshold || lifetimeXp < next)
                {
                    return level;
                }

                level++;
                threshold = next;
            }
        }

        /// <summary>
        /// The hero an account is playing, used only when the caller did not name a killer. Lowest
        /// id wins so the answer never depends on dictionary ordering.
        /// </summary>
        private string HeroForAccount(string accountId)
        {
            string best = null;
            foreach (var hero in State.Heroes.Values)
            {
                if (hero.AccountId != accountId)
                {
                    continue;
                }

                if (best == null || string.CompareOrdinal(hero.Id, best) < 0)
                {
                    best = hero.Id;
                }
            }

            return best;
        }

        /// <summary>
        /// Splits "unlock_Q" / "rank_E" into the ability and what is being bought (R-42). Free
        /// choice: no unlock order is forced, so the only thing parsed is which key was named.
        /// </summary>
        private static bool TryParseChoice(string choice, out string ability, out bool unlocking)
        {
            ability = null;
            unlocking = false;

            if (string.IsNullOrEmpty(choice))
            {
                return false;
            }

            if (choice.StartsWith(UnlockPrefix, StringComparison.Ordinal))
            {
                unlocking = true;
                ability = choice.Substring(UnlockPrefix.Length);
            }
            else if (choice.StartsWith(RankPrefix, StringComparison.Ordinal))
            {
                ability = choice.Substring(RankPrefix.Length);
            }
            else
            {
                return false;
            }

            return ability == "Q" || ability == "E";
        }

        /// <summary>
        /// A rejected spend is inert: the point stays banked, nothing changes, nothing is written
        /// (G-026). Only the event tells anyone it happened.
        /// </summary>
        private SpendSkillPointResult RejectSpend(
            SpendSkillPointRequest request, AccountProfile profile, string reason)
        {
            Emit("spend_rejected", new Dictionary<string, object>
            {
                { "reason", reason },
                { "account_id", request.AccountId },
            });

            return Finish(new SpendSkillPointResult
            {
                Accepted = false,
                Choice = request.Choice,
                SkillPointsAfter = profile.SkillPoints,
                RejectionReason = reason,
                Abilities = SnapshotAbilities(profile),
            });
        }

        /// <summary>
        /// A detached copy of the ability ranks: the result is replicated after the command returns,
        /// and must not keep mutating with the live profile.
        /// </summary>
        private static IDictionary<string, int> SnapshotAbilities(AccountProfile profile)
        {
            var snapshot = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var ability in profile.Abilities)
            {
                snapshot[ability.Key] = ability.Value;
            }

            return snapshot;
        }
    }
}
