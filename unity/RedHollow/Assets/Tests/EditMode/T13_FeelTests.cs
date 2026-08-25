using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Game.View;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 013 (T-13) — R-64, the feel pass: "basic attacks land with hit-flash + knockback
    /// nudge; wave start/end stingers; western-twang UI audio". R-64 itself says "not
    /// fixture-testable; playtest criteria" — so these tests pin the layer UNDER the feel, which is
    /// mechanical: every sim event on the feel list has a registered binding, the bindings are
    /// distinct, monster_damaged drives an observable flash + a presentation-only nudge, and
    /// nothing feel does can ever touch sim state or take the session down. Whether the twang
    /// twangs is playtest's question; whether the event that should trigger it still reaches a
    /// binding is this file's.
    ///
    /// Three rules carried throughout:
    ///
    ///  1. <b>The event list is the contract.</b> <see cref="R64FeelEvents"/> is spelled out here,
    ///     in the sim's own event-name strings, so a renamed or dropped event fails THIS suite
    ///     loudly instead of silently losing its juice in a playtest six waves later.
    ///
    ///  2. <b>Nudges are presentation.</b> The knockback offset lives on
    ///     <see cref="EntityFeelState"/> and lands on the view's TRANSFORM via
    ///     <see cref="FeelRig"/>; the sim-authoritative position — <see cref="Monster.Pos"/> and
    ///     the <see cref="MonsterView.WorldPosition"/> that mirrors it — never moves. A feel layer
    ///     that nudged the sim would be a client writing world state (R-51; T10's IL invariant
    ///     covers the MonoBehaviour side, this covers the seam it might be tempted through).
    ///
    ///  3. <b>Feel is total and harmless.</b> Unknown events, null events, unhit entities: never a
    ///     throw, never a null feel state. Juice must not be able to crash the game it garnishes.
    ///
    /// Deliberately NOT pinned: nudge direction and magnitude, flash duration and color, cue-key
    /// spellings, which audio asset a key names, easing curves. All playtest.
    /// </summary>
    [TestFixture]
    public class T13_FeelTests
    {
        private const float PositionTolerance = 1e-3f;

        /// <summary>
        /// R-64 — the full feel-event list from the sim handoff, in the sim's own spellings
        /// (placeable_broken is a spent trap, placeable_destroyed a wall collapsing — the sim keeps
        /// them distinct deliberately, and the feel layer must not merge them back together).
        /// </summary>
        private static readonly string[] R64FeelEvents =
        {
            "monster_damaged",
            "hero_damaged",
            "hero_died",
            "hero_respawned",
            "civilians_killed",
            "hotspot_emptied",
            "placeable_created",
            "placeable_triggered",
            "placeable_broken",
            "placeable_destroyed",
            "turret_fired",
            "status_applied",
            "status_expired",
            "wave_complete",
            "combat_started",
            "match_victory",
            "match_defeat",
        };

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void DestroyEverythingThisTestBuilt()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
        }

        // ==========================================================================================
        //  AC — every R-64 event has a registered binding; a renamed event fails loudly
        // ==========================================================================================

        /// <summary>
        /// Every event on the list resolves to a binding and routes to a real cue. This is the
        /// loud-failure test: rename an event in the sim, or forget one when wiring the router, and
        /// the missing name is printed here rather than discovered as a silent dead spot in feel.
        /// </summary>
        [Test]
        public void Every_R64_event_has_a_registered_feel_binding()
        {
            var router = new FeelRouter();

            var unbound = R64FeelEvents.Where(name => !router.HasBindingFor(name)).ToList();
            Assert.That(unbound, Is.Empty,
                "R-64: every feel event must have a binding — a renamed or missed event loses its "
                + "juice silently otherwise. Unbound: " + string.Join(", ", unbound));

            foreach (var name in R64FeelEvents)
            {
                var cue = router.Route(new SimEvent(name));
                Assert.That(cue, Is.Not.Null, "R-64: routing a bound event answers a cue: " + name);
                Assert.That(cue.EventType, Is.EqualTo(name), "the cue names the event it answered");
                Assert.That(string.IsNullOrEmpty(cue.EffectKey), Is.False,
                    "R-64: a binding without an effect key is not a binding: " + name);
            }
        }

        /// <summary>
        /// Every binding is its own effect. The concrete pair this protects is broken-vs-destroyed
        /// (asserted again by name below), but the rule is general: seventeen events funneled into
        /// a handful of effects quietly erases distinctions the sim was designed to keep.
        /// </summary>
        [Test]
        public void Each_bound_event_maps_to_its_own_distinct_effect()
        {
            var router = new FeelRouter();

            var effectKeys = R64FeelEvents
                .Select(name => router.Route(new SimEvent(name)).EffectKey)
                .ToList();

            Assert.That(effectKeys, Is.Unique,
                "R-64: each event carries its own effect key — two events sharing one effect is "
                + "how placeable_broken and placeable_destroyed stop being distinguishable");
        }

        /// <summary>
        /// The pair the sim distinguishes DELIBERATELY (handoff note): a spent trap
        /// (placeable_broken) and a collapsing wall (placeable_destroyed) are different moments and
        /// must stay different effects. Both cues also name the placeable they land on, off the
        /// event's own placeable_id field.
        /// </summary>
        [Test]
        public void A_spent_trap_and_a_collapsing_wall_are_distinct_effects()
        {
            var router = new FeelRouter();

            var broken = router.Route(new SimEvent("placeable_broken",
                new Dictionary<string, object> { { "placeable_id", "pl_trap" } }));
            var destroyed = router.Route(new SimEvent("placeable_destroyed",
                new Dictionary<string, object> { { "placeable_id", "pl_wall" }, { "by", "m1" } }));

            Assert.That(broken.EffectKey, Is.Not.EqualTo(destroyed.EffectKey),
                "the sim splits broken (spent trap) from destroyed (collapsed wall) deliberately; "
                + "the feel layer must not merge them back");
            Assert.That(broken.TargetId, Is.EqualTo("pl_trap"),
                "the effect lands on the placeable the event names");
            Assert.That(destroyed.TargetId, Is.EqualTo("pl_wall"),
                "the effect lands on the placeable the event names");
        }

        /// <summary>
        /// R-64: "wave start/end stingers". combat_started and wave_complete each carry an audio
        /// cue key, and not the same one — a start fanfare on a wave clear is the wrong music.
        /// Which asset the key names, and whether it twangs, is playtest.
        /// </summary>
        [Test]
        public void Wave_start_and_end_route_to_distinct_stinger_cues()
        {
            var router = new FeelRouter();

            var start = router.Route(new SimEvent("combat_started",
                new Dictionary<string, object> { { "wave", 3 }, { "trigger", "all_ready" } }));
            var end = router.Route(new SimEvent("wave_complete",
                new Dictionary<string, object> { { "wave", 3 } }));

            Assert.That(string.IsNullOrEmpty(start.AudioKey), Is.False,
                "R-64: a wave start carries a stinger cue");
            Assert.That(string.IsNullOrEmpty(end.AudioKey), Is.False,
                "R-64: a wave end carries a stinger cue");
            Assert.That(start.AudioKey, Is.Not.EqualTo(end.AudioKey),
                "R-64: start and end are different stingers");
        }

        /// <summary>Victory and defeat both carry audio, and never the same audio.</summary>
        [Test]
        public void Victory_and_defeat_route_to_distinct_cues()
        {
            var router = new FeelRouter();

            var victory = router.Route(new SimEvent("match_victory"));
            var defeat = router.Route(new SimEvent("match_defeat",
                new Dictionary<string, object> { { "reason", "all_civilians_dead" } }));

            Assert.That(string.IsNullOrEmpty(victory.AudioKey), Is.False, "victory has a cue");
            Assert.That(string.IsNullOrEmpty(defeat.AudioKey), Is.False, "defeat has a cue");
            Assert.That(victory.AudioKey, Is.Not.EqualTo(defeat.AudioKey),
                "playing the victory sting over the defeat screen is the bug this line is about");
        }

        // ==========================================================================================
        //  AC — monster_damaged: hit flash + knockback nudge, presentation only
        // ==========================================================================================

        /// <summary>
        /// R-64: "basic attacks land with hit-flash + knockback nudge". Routing monster_damaged
        /// flashes the named target and gives it a nonzero presentation nudge; the cue names the
        /// same target so the view layer knows where to look. Magnitude, direction and duration
        /// are free — landing at all is the contract.
        /// </summary>
        [Test]
        public void Monster_damaged_flashes_and_nudges_the_named_target()
        {
            var router = new FeelRouter();

            var cue = router.Route(new SimEvent("monster_damaged", new Dictionary<string, object>
            {
                { "monster_id", "m1" },
                { "amount", 12.0 },
                { "by", "h1" },
            }));

            Assert.That(cue, Is.Not.Null, "R-64: a landed basic attack is a feel event");
            Assert.That(cue.TargetId, Is.EqualTo("m1"),
                "the effect lands on the monster the event names, off its monster_id field");

            var feel = router.FeelFor("m1");
            Assert.That(feel, Is.Not.Null, "feel state is total");
            Assert.That(feel.IsFlashing, Is.True,
                "R-64: a hit flashes the target — the flash is observable state, not a hope");
            Assert.That(feel.FlashSecondsRemaining, Is.GreaterThan(0.0),
                "a flash has duration left the moment it starts");
            Assert.That(feel.NudgeOffset.magnitude, Is.GreaterThan(0f),
                "R-64: a hit nudges the target — zero offset is no knockback at all");

            var bystander = router.FeelFor("m2");
            Assert.That(bystander, Is.Not.Null, "feel state is total for the unhit too");
            Assert.That(bystander.IsFlashing, Is.False, "only the named target flashes");
            Assert.That(bystander.NudgeOffset.magnitude, Is.LessThan(PositionTolerance),
                "only the named target is nudged");
        }

        /// <summary>
        /// The R-51 boundary, stated on the nudge: applying feel moves the view's TRANSFORM off its
        /// authoritative position by exactly the presentation offset — and moves NOTHING else. The
        /// sim's Monster.Pos and the view's mirrored WorldPosition both stay put. A knockback that
        /// moved the sim position would be the client authoring world state.
        /// </summary>
        [Test]
        public void The_nudge_offsets_the_view_transform_and_never_the_sim_position()
        {
            var state = SoloState();
            var simPosBefore = new Vec2(state.Monsters["m1"].Pos.X, state.Monsters["m1"].Pos.Y);

            var view = NewView<MonsterView>("monster");
            view.Bind("m1", Placeholder(VisualClass.Monster));
            view.RenderFrom(state);

            var router = new FeelRouter();
            router.Route(new SimEvent("monster_damaged", new Dictionary<string, object>
            {
                { "monster_id", "m1" },
                { "amount", 12.0 },
                { "by", "h1" },
            }));

            var feel = router.FeelFor("m1");
            FeelRig.Apply(view, feel);

            var authoritative = SimSpace.ToWorld(state.Monsters["m1"].Pos);
            var expected = authoritative + feel.NudgeOffset;

            Assert.That(Vector3.Distance(view.transform.position, expected),
                Is.LessThan(PositionTolerance),
                "the nudge rides on the transform: authoritative position plus the presentation "
                + "offset, nothing cleverer");
            Assert.That(Vector3.Distance(view.transform.position, authoritative),
                Is.GreaterThan(0f),
                "with a live nudge the transform is visibly off its authoritative spot");

            Assert.That(view.WorldPosition, Is.EqualTo(authoritative),
                "WorldPosition mirrors the sim and only the sim — feel never edits the mirror");
            Assert.That(state.Monsters["m1"].Pos.X, Is.EqualTo(simPosBefore.X),
                "R-51: the sim-authoritative position never moves for a feel effect");
            Assert.That(state.Monsters["m1"].Pos.Y, Is.EqualTo(simPosBefore.Y),
                "R-51: the sim-authoritative position never moves for a feel effect");
        }

        /// <summary>
        /// Feel decays: after ample ticked time the flash is over, the nudge has sprung back, and
        /// applying the now-neutral feel parks the view exactly on its authoritative position
        /// again. Fifteen seconds is deliberately generous — any sane flash/nudge duration fits —
        /// so this pins "temporary", not a duration.
        /// </summary>
        [Test]
        public void Feel_decays_and_the_view_returns_to_its_authoritative_position()
        {
            var state = SoloState();
            var view = NewView<MonsterView>("monster");
            view.Bind("m1", Placeholder(VisualClass.Monster));
            view.RenderFrom(state);

            var router = new FeelRouter();
            router.Route(new SimEvent("monster_damaged", new Dictionary<string, object>
            {
                { "monster_id", "m1" },
                { "amount", 12.0 },
                { "by", "h1" },
            }));

            for (var i = 0; i < 60; i++)
            {
                router.Tick(0.25);
            }

            var feel = router.FeelFor("m1");
            Assert.That(feel.IsFlashing, Is.False,
                "R-64: a hit FLASH — fifteen seconds later it is long over");
            Assert.That(feel.NudgeOffset.magnitude, Is.LessThan(PositionTolerance),
                "R-64: a knockback NUDGE springs back; a permanent offset is a desynced view");

            view.RenderFrom(state);
            FeelRig.Apply(view, feel);

            Assert.That(Vector3.Distance(view.transform.position, view.WorldPosition),
                Is.LessThan(PositionTolerance),
                "with feel decayed the view stands exactly where the sim says again");
        }

        // ==========================================================================================
        //  AC — feel is total and harmless: no event can crash it, no code path blocks on it
        // ==========================================================================================

        /// <summary>
        /// Feel must never be able to take the session down. Events the router has no binding for
        /// (the sim emits more than the R-64 list — xp_awarded, monster_killed and future ones),
        /// null events, events missing their fields: no throw, and an honest "no binding" answer.
        /// </summary>
        [Test]
        public void Unbound_null_and_malformed_events_are_harmless()
        {
            var router = new FeelRouter();

            Assert.That(router.HasBindingFor("xp_awarded"), Is.False,
                "the R-64 list is the feel contract; other sim events simply have no binding");
            Assert.That(router.HasBindingFor("some_future_event"), Is.False);
            Assert.That(router.HasBindingFor(null), Is.False, "null is not a bound event");

            FeelCue cue = null;
            Assert.That(() => { cue = router.Route(new SimEvent("xp_awarded")); }, Throws.Nothing,
                "an unbound event routes to nothing, never to an exception");
            Assert.That(cue, Is.Null, "no binding, no cue — an honest nothing");

            Assert.That(() => router.Route(null), Throws.Nothing, "a null event is harmless");
            Assert.That(router.Route(null), Is.Null);

            Assert.That(() => router.Route(new SimEvent("monster_damaged")), Throws.Nothing,
                "a bound event missing its fields still must not throw — feel degrades, it never "
                + "crashes");

            Assert.That(() => router.Tick(0.0), Throws.Nothing, "an idle tick is fine");
            Assert.That(() => FeelRig.Apply(null, new EntityFeelState()), Throws.Nothing,
                "FeelRig on a missing view is a no-op, same contract as ViewRig");
        }

        // ==========================================================================================
        //  scenario builders
        // ==========================================================================================

        /// <summary>A minimal live solo match, same shape as T16's.</summary>
        private static MatchState SoloState()
        {
            var state = new MatchState
            {
                Phase = MatchPhase.Combat,
                Status = MatchStatus.InProgress,
            };

            state.Heroes["h1"] = new Hero
            {
                Id = "h1",
                HeroClass = HeroClass.Gunslinger,
                AccountId = "acc_solo",
                Pos = new Vec2(0.0, 0.0),
                Hp = 100.0,
                MaxHp = 100.0,
                Alive = true,
            };

            state.Monsters["m1"] = new Monster
            {
                Id = "m1",
                Type = MonsterType.Shambler,
                Pos = new Vec2(3.0, -2.0),
                Hp = 60.0,
                Alive = true,
                BaseSpeed = 2.0,
                CurrentSpeed = 2.0,
            };

            state.Wave.LivingMonsterIds.Add("m1");

            return state;
        }

        private VisualHandle Placeholder(VisualClass visualClass)
        {
            var handle = new PlaceholderVisualResolver().Resolve(visualClass, null);
            if (handle != null)
            {
                Track(handle.Instance);
            }

            return handle;
        }

        private T NewView<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        private GameObject Track(GameObject go)
        {
            if (go != null)
            {
                _spawned.Add(go);
            }

            return go;
        }
    }
}
