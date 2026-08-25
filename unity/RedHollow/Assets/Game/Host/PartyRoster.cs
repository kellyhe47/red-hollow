using System;
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
    ///
    /// SHAPE ONLY (ticket 010, TDD stub) — implementation belongs to the implementing agent.
    /// </summary>
    public sealed class PartyRoster
    {
        /// <summary>R-50 — solo is a 1-player lobby, so a match is playable at one.</summary>
        public const int MinPlayers = 1;

        /// <summary>R-50 — 4-player co-op is the ceiling.</summary>
        public const int MaxPlayers = 4;

        public int Count => throw NotYet(nameof(Count));

        public IReadOnlyList<string> AccountIds => throw NotYet(nameof(AccountIds));

        /// <summary>Adds a player; false when the party is already at <see cref="MaxPlayers"/>.</summary>
        public bool TryAdd(string accountId) => throw NotYet(nameof(TryAdd));

        /// <summary>R-53 — a mid-match disconnect frees the slot; the match carries on.</summary>
        public bool Remove(string accountId) => throw NotYet(nameof(Remove));

        private static NotImplementedException NotYet(string member) =>
            new NotImplementedException("T-10 not implemented: PartyRoster." + member);
    }
}
