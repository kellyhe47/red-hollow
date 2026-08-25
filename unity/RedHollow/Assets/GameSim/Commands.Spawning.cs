using System.Collections.Generic;
using System.Linq;

namespace RedHollow.Sim
{
    /// <summary>
    /// R-19 / R-14 — what one wave's spawn actually produced.
    ///
    /// Shaped like every other <see cref="ISimResult"/> here: the ids the command created, in the
    /// order it created them. The order is the point, not decoration — R-54 wants sim behaviour
    /// replayable, and <see cref="MatchState.Monsters"/> is a dictionary whose enumeration order is
    /// not a promise, so an ordered list is the only surface on which "the same wave spawns the same
    /// way twice" can be stated at all.
    ///
    /// Deliberately *not* the DEC-018 problem <see cref="WavePreviewResult"/> guards: that type
    /// exists so a client planning against a wave cannot read its composition. By the time this
    /// result exists the monsters are standing in the world and are replicated like any other
    /// entity, so naming them here reveals nothing the client is not about to see.
    ///
    /// Ticket 017 (T-17) declares the shape; nothing fills it in yet.
    /// </summary>
    public sealed class WaveSpawnResult : ISimResult
    {
        /// <summary>The wave that was spawned, matching <see cref="WaveSpec.Number"/>.</summary>
        public int Wave;

        /// <summary>
        /// Every monster id this spawn created, in spawn order. Also the ids that were added to
        /// <see cref="WaveState.LivingMonsterIds"/>, which is what lets
        /// <see cref="MatchSim.RecordMonsterKill"/> ever complete the wave (R-02 / G-010).
        /// </summary>
        public readonly List<string> MonsterIds = new List<string>();

        public IDictionary<string, object> ToFields() => new Dictionary<string, object>
        {
            { "wave", Wave },
            { "monster_ids", MonsterIds.Cast<object>().ToList() },
            { "spawned", MonsterIds.Count },
        };
    }
}
