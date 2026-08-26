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

        private readonly Dictionary<string, MonsterView> _monsterViews =
            new Dictionary<string, MonsterView>(System.StringComparer.Ordinal);

        private readonly Dictionary<string, HeroView> _heroViews =
            new Dictionary<string, HeroView>(System.StringComparer.Ordinal);

        private readonly Dictionary<string, PlaceableView> _placeableViews =
            new Dictionary<string, PlaceableView>(System.StringComparer.Ordinal);

        /// <summary>
        /// Scratch list for the ids leaving the binding this step. Reused because
        /// <see cref="Sync"/> runs sixty times a second, and because a dictionary cannot be edited
        /// while it is being walked.
        /// </summary>
        private readonly List<string> _released = new List<string>();

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
        public IReadOnlyCollection<string> BoundMonsterIds => _monsterViews.Keys;

        /// <summary>The hero ids that currently have a view.</summary>
        public IReadOnlyCollection<string> BoundHeroIds => _heroViews.Keys;

        /// <summary>
        /// T-26 — the standing placeable ids that currently have a view. Follows the world:
        /// one view per placeable with <see cref="Placeable.Exists"/>, released when it flips.
        /// </summary>
        public IReadOnlyCollection<string> BoundPlaceableIds => _placeableViews.Keys;

        /// <summary>
        /// T-26 — the R-23 catalog the barricade damage readout takes its full-HP denominator
        /// from. Null is allowed (a binder must never block on config any more than on art) and
        /// means no denominator is known.
        /// </summary>
        public PlaceableCatalog PlaceableCatalog { get; set; }

        public PlaceableView PlaceableViewFor(string placeableId)
        {
            if (placeableId == null)
            {
                return null;
            }

            return _placeableViews.TryGetValue(placeableId, out var view) ? view : null;
        }

        public MonsterView MonsterViewFor(string monsterId)
        {
            if (monsterId == null)
            {
                return null;
            }

            return _monsterViews.TryGetValue(monsterId, out var view) ? view : null;
        }

        public HeroView HeroViewFor(string heroId)
        {
            if (heroId == null)
            {
                return null;
            }

            return _heroViews.TryGetValue(heroId, out var view) ? view : null;
        }

        /// <summary>
        /// R-51 — reconcile the view set with <paramref name="state"/>: a view appears for every
        /// entity that is in the world and living, the view of an entity that died or left is
        /// released, and every surviving view then renders from this state.
        ///
        /// Idempotent by construction — the host calls it every step, and a binder that created a
        /// second view per step would leave a colony of stacked stand-ins.
        ///
        /// <b>Liveness is the sim's answer, not this class's.</b> Nothing here decides that a
        /// monster is dead; it reads <see cref="Monster.Alive"/>, which only
        /// <see cref="MatchSim"/> writes. That is what keeps the reconciliation a mirror rather
        /// than a second, quietly disagreeing copy of R-02.
        /// </summary>
        public void Sync(MatchState state)
        {
            if (state == null)
            {
                return;
            }

            EnsureRoot();

            SyncMonsters(state);
            SyncHeroes(state);
            SyncPlaceables(state);
        }

        // ---- reconciliation ----------------------------------------------------------------------

        /// <summary>
        /// One view per living monster. Bound first and released second so a wave that spawns and
        /// loses a monster in the same step still ends level with the world either way round.
        /// </summary>
        private void SyncMonsters(MatchState state)
        {
            foreach (var monster in state.Monsters.Values)
            {
                if (monster == null || string.IsNullOrEmpty(monster.Id) || !monster.Alive)
                {
                    continue;
                }

                if (!_monsterViews.ContainsKey(monster.Id))
                {
                    var view = NewView<MonsterView>("MonsterView_" + monster.Id);
                    view.Bind(monster.Id, _visuals.Resolve(VisualClass.Monster, monster.Type));
                    _monsterViews[monster.Id] = view;
                }
            }

            _released.Clear();
            foreach (var pair in _monsterViews)
            {
                // Left the world entirely (a rematch cleared the roster, R-07) or died in it
                // (R-02). Both release: a stand-in left standing in the colony is a monster the
                // players will keep shooting at.
                if (!state.Monsters.TryGetValue(pair.Key, out var monster) || monster == null || !monster.Alive)
                {
                    _released.Add(pair.Key);
                }
            }

            for (var i = 0; i < _released.Count; i++)
            {
                var id = _released[i];
                Release(_monsterViews[id] == null ? null : _monsterViews[id].gameObject);
                _monsterViews.Remove(id);
            }

            // Rendered after the set is settled, so nothing spends a frame reading a world it is
            // about to be removed from.
            foreach (var view in _monsterViews.Values)
            {
                view.RenderFrom(state);
            }
        }

        /// <summary>
        /// One view per living hero, on exactly the rule the monsters follow. A hero killed in a
        /// wave is off the field until R-33 respawns it, and a body left lying at the point of
        /// death is a target the team will try to revive.
        /// </summary>
        private void SyncHeroes(MatchState state)
        {
            foreach (var hero in state.Heroes.Values)
            {
                if (hero == null || string.IsNullOrEmpty(hero.Id) || !hero.Alive)
                {
                    continue;
                }

                if (!_heroViews.ContainsKey(hero.Id))
                {
                    var view = NewView<HeroView>("HeroView_" + hero.Id);
                    view.Bind(hero.Id, _visuals.Resolve(VisualClass.Hero, hero.HeroClass));
                    _heroViews[hero.Id] = view;
                }
            }

            _released.Clear();
            foreach (var pair in _heroViews)
            {
                if (!state.Heroes.TryGetValue(pair.Key, out var hero) || hero == null || !hero.Alive)
                {
                    _released.Add(pair.Key);
                }
            }

            for (var i = 0; i < _released.Count; i++)
            {
                var id = _released[i];
                Release(_heroViews[id] == null ? null : _heroViews[id].gameObject);
                _heroViews.Remove(id);
            }

            foreach (var view in _heroViews.Values)
            {
                view.RenderFrom(state);
            }
        }

        /// <summary>
        /// T-26 — one view per standing placeable, on exactly the monsters' rule with
        /// <see cref="Placeable.Exists"/> as the one predicate: the sim flips it for sold, broken
        /// and destroyed alike (R-22/R-23/R-16), and whatever flipped it, the next refresh
        /// releases the view. The art key IS the sim's <see cref="Placeable.Type"/> constant —
        /// the same key-equals-the-type rule the hero binding uses, so the art pipeline and the
        /// binder can never drift on spelling.
        /// </summary>
        private void SyncPlaceables(MatchState state)
        {
            foreach (var placeable in state.Placeables.Values)
            {
                if (placeable == null || string.IsNullOrEmpty(placeable.Id) || !placeable.Exists)
                {
                    continue;
                }

                if (!_placeableViews.ContainsKey(placeable.Id))
                {
                    var view = NewView<PlaceableView>("PlaceableView_" + placeable.Id);
                    view.Bind(
                        placeable.Id,
                        _visuals.Resolve(VisualClass.Placeable, placeable.Type),
                        FullHpFor(placeable.Type));
                    _placeableViews[placeable.Id] = view;
                }
            }

            _released.Clear();
            foreach (var pair in _placeableViews)
            {
                if (!state.Placeables.TryGetValue(pair.Key, out var placeable)
                    || placeable == null || !placeable.Exists)
                {
                    _released.Add(pair.Key);
                }
            }

            for (var i = 0; i < _released.Count; i++)
            {
                var id = _released[i];
                Release(_placeableViews[id] == null ? null : _placeableViews[id].gameObject);
                _placeableViews.Remove(id);
            }

            foreach (var view in _placeableViews.Values)
            {
                view.RenderFrom(state);
            }
        }

        /// <summary>
        /// The R-23 catalog MaxHp for one row — the same number the sim damages against, never a
        /// second copy typed here. No catalog (or no row) answers 0.0, which the view reads as
        /// "no denominator known" and shows no indicator over: a binder never blocks on config.
        /// </summary>
        private double FullHpFor(string placeableType)
        {
            if (PlaceableCatalog == null || string.IsNullOrEmpty(placeableType))
            {
                return 0.0;
            }

            var stats = PlaceableCatalog.TryGet(placeableType);
            return stats == null ? 0.0 : stats.MaxHp;
        }

        // ---- GameObject plumbing -----------------------------------------------------------------

        /// <summary>
        /// The parent everything this binder makes hangs from, built on first use rather than in
        /// the constructor: a binder constructed by a headless host and never synced must not put
        /// an empty object in the scene.
        /// </summary>
        private void EnsureRoot()
        {
            if (Root == null)
            {
                Root = new GameObject("RedHollow_MatchViews");
            }
        }

        private TView NewView<TView>(string name) where TView : MonoBehaviour
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root.transform, false);
            return go.AddComponent<TView>();
        }

        /// <summary>
        /// Destroy rather than pool. Whether a released view is recycled is not in the PRD, and a
        /// pool is a cache with an invalidation rule attached — worth writing when a profile says
        /// the churn costs something, not before.
        ///
        /// <see cref="Object.DestroyImmediate"/> outside play mode because
        /// <see cref="Object.Destroy"/> is deferred to the end of a frame that an EditMode test
        /// and an editor-time build never have: the stand-in would outlive the monster for the rest
        /// of the run.
        /// </summary>
        private static void Release(GameObject view)
        {
            if (view == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(view);
            }
            else
            {
                Object.DestroyImmediate(view);
            }
        }
    }
}
