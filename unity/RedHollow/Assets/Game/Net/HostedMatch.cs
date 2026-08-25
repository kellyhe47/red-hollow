using System.Collections.Generic;
using RedHollow.Game.Host;
using RedHollow.Sim;

namespace RedHollow.Game.Net
{
    /// <summary>
    /// Ticket 011 (T-11) — one live match, as the pieces the host holds while it runs (R-51).
    ///
    /// A holder rather than a wrapper: it decides nothing and adds no member of its own, because
    /// every rule already lives in <see cref="MatchSim"/> and every schedule already lives in
    /// <see cref="MatchSession"/>. What it contributes is a single object whose *identity* is the
    /// match, which is what makes R-07 checkable at all — "all match state resets fully" is the
    /// statement that a rematch produces a different one of these, not a scrubbed copy of the last.
    /// </summary>
    public sealed class HostedMatch
    {
        /// <summary>The host-authoritative world (R-51).</summary>
        public MatchState State;

        /// <summary>Sim time. The session advances it through <see cref="Session"/>.</summary>
        public SimClock Clock;

        /// <summary>The sim itself — the host holds one and clients never do (R-51).</summary>
        public MatchSim Sim;

        /// <summary>The seam the loop drives commands through.</summary>
        public IMatchSimHost Host;

        /// <summary>Ticket 019's driven session: <c>Start()</c> opens the wave, <c>Step()</c> runs it.</summary>
        public MatchSession Session;
    }

    /// <summary>
    /// Ticket 011 (T-11) — where a match comes from (R-07).
    ///
    /// A seam rather than a constructor call inside the session, because R-07's reset is exactly
    /// "build another one": a rematch that reached into <see cref="MatchState"/> and set fields back
    /// would have to remember every field the sim ever grows, and the first one it forgot would be a
    /// wave-3 barricade standing in a fresh match. Making creation the only way a match exists makes
    /// the reset total by construction.
    ///
    /// The party is passed in rather than read from anywhere, because the factory owns the
    /// config-to-state bridge for *players*: R-07 retains class picks across the reset, so the picks
    /// have to arrive from outside the thing being reset.
    /// </summary>
    public interface IHostedMatchFactory
    {
        /// <summary>
        /// A brand-new match for this party: a fresh <see cref="MatchState"/> on the colony map
        /// (R-10, R-20's stake seeded), one <see cref="PlayerSlot"/> and one <see cref="Hero"/> per
        /// seated peer carrying that peer's account and class pick (R-07 / R-31), and the persistent
        /// profiles applied on top (R-43).
        /// </summary>
        HostedMatch CreateMatch(IReadOnlyList<NetPeer> party);
    }
}
