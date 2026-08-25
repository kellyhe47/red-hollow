using System;
using System.Collections.Generic;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Ticket 019 (T-19) — the thing that makes views follow the world (R-51).
    ///
    /// Ticket 016 built <see cref="MonsterView"/> and <see cref="HeroView"/> and pinned that
    /// <c>RenderFrom</c> mirrors replicated state, but nothing ever *created* one for a live
    /// entity: a wave spawned by <see cref="MatchSim.SpawnWave"/> was invisible, and a monster
    /// killed by <see cref="MatchSim.RecordMonsterKill"/> left its stand-in standing in the colony.
    /// This is the one place that reconciles the set of views with the set of entities.
    ///
    /// Plain C# and not a MonoBehaviour, on purpose: T-10's IL invariant flags any component that
    /// writes sim state, and a binder is exactly where somebody reaches for one. It reads the
    /// world and writes only <see cref="GameObject"/>s.
    /// </summary>
    public sealed class MatchViewBinder
    {
        private readonly IVisualResolver _visuals;

        /// <param name="visuals">
        /// The asset seam. Null falls back to <see cref="PlaceholderVisualResolver"/> — ticket 016's
        /// rule that no gameplay path may block on art (R-30's delivery constraint).
        /// </param>
        public MatchViewBinder(IVisualResolver visuals = null)
        {
            _visuals = visuals ?? new PlaceholderVisualResolver();
        }

        /// <summary>
        /// Everything this binder created, parented here so a session tears down in one call —
        /// the same shape <see cref="MatchScene.Root"/> uses.
        /// </summary>
        public GameObject Root { get; private set; }

        /// <summary>The monster ids that currently have a view. Follows the world, not the spawn log.</summary>
        public IReadOnlyCollection<string> BoundMonsterIds => new string[0];

        /// <summary>The hero ids that currently have a view.</summary>
        public IReadOnlyCollection<string> BoundHeroIds => new string[0];

        public MonsterView MonsterViewFor(string monsterId) => null;

        public HeroView HeroViewFor(string heroId) => null;

        /// <summary>
        /// R-51 — reconcile the view set with <paramref name="state"/>: a view appears for every
        /// entity that is in the world and living, the view of an entity that died or left is
        /// released, and every surviving view then renders from this state.
        ///
        /// Idempotent by construction — the host calls it every step, and a binder that created a
        /// second view per step would leave a colony of stacked stand-ins.
        /// </summary>
        public void Sync(MatchState state)
        {
            throw new NotImplementedException(
                "ticket 019: a view must appear for every living entity in the match and be "
                + "released when that entity dies (R-51)");
        }
    }
}
