using System.Collections.Generic;

namespace RedHollow.Game.Host
{
    /// <summary>
    /// R-50 / DEC-020 / DEC-022 — the party a match is played by: 1 to 4 players, solo being a
    /// one-player lobby rather than a separate mode.
    ///
    /// Transport (Netcode for GameObjects, Lobby join codes, Relay) is ticket 011; this is only the
    /// size rule, which is the half R-50 states as a number and the half a fifth joiner must bounce
    /// off regardless of how they arrived.
    /// </summary>
    public sealed class PartyRoster
    {
        /// <summary>R-50 — solo is a 1-player lobby, so a match is playable at one.</summary>
        public const int MinPlayers = 1;

        /// <summary>R-50 — 4-player co-op is the ceiling.</summary>
        public const int MaxPlayers = 4;

        /// <summary>
        /// A list rather than a set: join order is the slot order the lobby shows, and R-50 caps the
        /// party at four, so a linear membership check costs nothing worth optimising away.
        /// </summary>
        private readonly List<string> _accountIds = new List<string>(MaxPlayers);

        public int Count => _accountIds.Count;

        public IReadOnlyList<string> AccountIds => _accountIds.AsReadOnly();

        /// <summary>
        /// Adds a player; false when the party is already at <see cref="MaxPlayers"/>.
        ///
        /// Refuses rather than throws, and refusing must not grow the party: a fifth joiner is an
        /// ordinary lobby outcome (the party filled while they were connecting), not a fault in the
        /// host. An account already seated is refused for the same reason — one account holds one
        /// slot, so a reconnect that races its own timeout cannot take two.
        /// </summary>
        public bool TryAdd(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return false;
            }

            if (_accountIds.Count >= MaxPlayers)
            {
                return false;
            }

            if (_accountIds.Contains(accountId))
            {
                return false;
            }

            _accountIds.Add(accountId);
            return true;
        }

        /// <summary>R-53 — a mid-match disconnect frees the slot; the match carries on.</summary>
        public bool Remove(string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return false;
            }

            return _accountIds.Remove(accountId);
        }
    }
}
