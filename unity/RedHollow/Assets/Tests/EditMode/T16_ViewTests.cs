using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RedHollow.Game.Host;
using RedHollow.Game.Input;
using RedHollow.Game.View;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 016 (T-16): the playable shell — scene, top-down camera, R-30 controls, and
    /// placeholder visuals driven from replicated sim state. Owns R-30 (DEC-016 / DEC-017). Grades
    /// no golden fixture: nothing here is a game rule, and every game rule is already green in
    /// <see cref="MatchSim"/>.
    ///
    /// Five things are pinned and nothing else:
    ///
    ///  1. <b>The R-30 mapping.</b> WASD moves, <b>W is movement only</b>, SPACE is the basic
    ///     attack, Q and E are the two abilities, and the mouse buttons produce nothing at all
    ///     because they belong to the UI. The clause that needs a test is "W is movement only": in
    ///     League Q/W/E/R are all spells, so W-as-an-ability is the muscle-memory mistake this
    ///     ticket exists to make impossible. DEC-017's rejection of click-to-move is the same shape
    ///     of failure from the other side — a cursor position must never become a step.
    ///
    ///  2. <b>Facing follows the cursor, not the feet.</b> The discriminating case is walking one
    ///     way with the cursor somewhere else. Direction only: the PRD names no turn rate and no
    ///     rotation representation, so a snap and a smooth turn must both pass.
    ///
    ///  3. <b>Nothing blocks on an asset existing.</b> <see cref="IVisualResolver"/> is total — a
    ///     null or unknown art key resolves to a visible primitive rather than null, an exception or
    ///     a skipped render. Covered across four visual classes so it is a rule and not one case.
    ///     Ticket 013 wires the real art in <c>art/</c>; this ticket pins only the fallback, which
    ///     is what keeps a gameplay ticket from ever being blocked on the art pipeline.
    ///
    ///  4. <b>Views mirror replicated sim state and hold no rule.</b> T10's IL invariant covers the
    ///     write direction (no MonoBehaviour may touch sim state); these cover the read direction —
    ///     a view shows what the sim says, follows it when it changes, and does not re-derive it.
    ///
    ///  5. <b>A solo session actually runs</b> on primitive art: real <see cref="MatchSim"/>, real
    ///     wave 1, real <see cref="HostLoop"/> steps, world evolving, nothing thrown.
    ///
    /// <b>Two conventions this ticket establishes</b>, because the PRD is silent and the tests need
    /// somewhere to stand. Both are stated rather than assumed:
    ///  * <see cref="HeroIntent.MoveDirection"/> and <see cref="InputSnapshot.CursorGroundPoint"/>
    ///    are ground-space, x = right and y = forward, so "W is forward" has a meaning.
    ///  * <see cref="SimSpace"/> lays the colony on one horizontal plane with Unity's Y as up.
    ///    The match camera looks down at that plane from a steep tilt (owner: ~60–70° from the
    ///    horizon), not straight down the vertical axis — bird's-eye flattens the 3D cavern.
    ///
    /// <b>What is deliberately NOT asserted</b>, because the PRD states none of it and a guessed
    /// number would ship as spec: move speed, turn rate, camera height and field of view, camera
    /// projection, placeholder colours and shapes, marker size, and the vertical offset of any
    /// visual (a capsule sitting half its height above the ground is presentation, not placement —
    /// so every position assertion here is horizontal only).
    ///
    /// EditMode throughout, on purpose. Everything above is reachable without entering play mode:
    /// the input map is pure data in and out, the views are components driven by an explicit
    /// <c>RenderFrom</c> rather than by <c>Update</c>, and the scene is composed by a plain
    /// function. Nothing here needs a frame to elapse, so nothing here needs PlayMode.
    /// </summary>
    [TestFixture]
    public class T16_ViewTests
    {
        /// <summary>Sim-time step, matching T10. Nothing depends on the value.</summary>
        private const double Step60Hz = 1.0 / 60.0;

        /// <summary>Positions cross a double-to-float boundary, so they are compared loosely.</summary>
        private const float PositionTolerance = 1e-3f;

        /// <summary>An axis reading below this counts as "not moving".</summary>
        private const float AxisTolerance = 1e-4f;

        private const double SimTolerance = 1e-9;

        /// <summary>Everything a test put in the editor's scene, torn down after it.</summary>
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void DestroyEverythingThisTestBuilt()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
        }

        // ==========================================================================================
        //  AC — R-30 input mapping: WASD move, W is movement only, SPACE basic, Q/E abilities
        // ==========================================================================================

        /// <summary>
        /// R-30 / DEC-017. Each movement key moves on its own axis and casts nothing.
        ///
        /// The <b>W row is the point of this ticket</b>: in League W is a spell, so the failure this
        /// catches is W bound to an ability. The row asserts both halves — W moves forward, and W
        /// produces no ability and no basic attack.
        ///
        /// The cursor sits at (5, 5) for every row, which is a direction no row expects: an
        /// implementation that derived movement from the cursor (click-to-move, DEC-017) fails every
        /// case here rather than sneaking through the ones that happen to agree.
        ///
        /// Only the sign of each axis is asserted. Move speed is not in the PRD.
        /// </summary>
        [TestCase(PlayerKey.W, 0f, 1f, TestName = "W moves forward and only forward")]
        [TestCase(PlayerKey.S, 0f, -1f, TestName = "S moves back")]
        [TestCase(PlayerKey.A, -1f, 0f, TestName = "A moves left")]
        [TestCase(PlayerKey.D, 1f, 0f, TestName = "D moves right")]
        public void Each_movement_key_moves_on_its_own_axis_and_casts_nothing(
            PlayerKey key, float expectedX, float expectedY)
        {
            var intent = new DefaultHeroInputMap().Resolve(Snapshot(new Vector2(5f, 5f), key));

            Assert.That(intent, Is.Not.Null, "R-30: every frame of input must resolve to an intent");

            AssertAxis(intent.MoveDirection.x, expectedX, "x", "R-30: " + key + " movement");
            AssertAxis(intent.MoveDirection.y, expectedY, "y", "R-30: " + key + " movement");

            Assert.That(intent.Ability, Is.Null,
                "R-30: " + key + " is movement only — it must never cast an ability (in League, W is a spell; "
                + "here it walks forward and does nothing else)");
            Assert.That(intent.BasicAttack, Is.False,
                "R-30: " + key + " is movement only — SPACE is the basic attack, not a movement key");
        }

        /// <summary>
        /// R-30. SPACE is the basic attack and is not an ability: it occupies no
        /// <see cref="AbilitySlot"/>, so nothing downstream can charge it a cooldown (R-32) or
        /// refuse it as locked (R-31). It is also not a step.
        /// </summary>
        [Test]
        public void Space_is_the_basic_attack_and_never_an_ability()
        {
            var cursor = new Vector2(3f, -4f);
            var intent = new DefaultHeroInputMap().Resolve(Snapshot(cursor, PlayerKey.Space));

            Assert.That(intent.BasicAttack, Is.True, "R-30: SPACE = basic attack");
            Assert.That(intent.Ability, Is.Null,
                "R-30: the basic attack is not an ability and holds no Q/E slot");
            AssertNoMovement(intent, "R-30: SPACE attacks, it does not walk");
            AssertAimsAt(intent, cursor, "R-30: SPACE is the basic attack *toward the cursor*");
        }

        /// <summary>
        /// R-30 / R-31. Q and E each cast their own ability — distinct from each other, and distinct
        /// from the basic attack. The two rows together are what catch a mapping that fires the same
        /// slot for both keys, or that routes an ability through the basic-attack channel.
        ///
        /// The slot spelling is the sim's own <see cref="AbilitySlot"/> constant, so an accepted
        /// intent feeds <see cref="HeroAbilityRequest.Slot"/> with no translation table in between.
        /// </summary>
        [TestCase(PlayerKey.Q, AbilitySlot.Q)]
        [TestCase(PlayerKey.E, AbilitySlot.E)]
        public void Q_and_E_each_cast_their_own_ability_and_not_the_basic_attack(
            PlayerKey key, string expectedSlot)
        {
            var intent = new DefaultHeroInputMap().Resolve(Snapshot(new Vector2(2f, 2f), key));

            Assert.That(intent.Ability, Is.EqualTo(expectedSlot),
                "R-30: " + key + " casts the " + expectedSlot + " ability");
            Assert.That(intent.BasicAttack, Is.False,
                "R-30: an ability is not the basic attack — SPACE is");
            AssertNoMovement(intent, "R-30: " + key + " casts, it does not walk");
        }

        /// <summary>
        /// R-30. <b>Mouse buttons stay free for UI</b>, so a held mouse button — over a point far
        /// from the hero, which is exactly the click-to-move gesture DEC-017 rejects — must produce
        /// no gameplay intent whatsoever. This is the test that fails if left-click is ever bound to
        /// attack or to move.
        ///
        /// Aim is the one thing the mouse still does, and it comes from the cursor's *position*,
        /// never from a button.
        /// </summary>
        [TestCase(PlayerKey.MouseLeft)]
        [TestCase(PlayerKey.MouseRight)]
        [TestCase(PlayerKey.MouseMiddle)]
        public void A_mouse_button_produces_no_gameplay_intent_at_all(PlayerKey button)
        {
            var distantPoint = new Vector2(18f, -23f);
            var intent = new DefaultHeroInputMap().Resolve(Snapshot(distantPoint, button));

            AssertNoMovement(intent,
                "R-30 / DEC-017: no click-to-move — clicking a distant point must never walk the hero");
            Assert.That(intent.BasicAttack, Is.False,
                "R-30: mouse buttons stay free for UI; " + button + " must not be the basic attack");
            Assert.That(intent.Ability, Is.Null,
                "R-30: mouse buttons stay free for UI; " + button + " must not cast an ability");
            AssertAimsAt(intent, distantPoint, "aiming is the only thing the cursor does");
        }

        /// <summary>
        /// R-30 / DEC-017, the other half of the click-to-move rejection: with no key and no button
        /// held, moving the cursor anywhere at all is still not a movement command. LoL contributes
        /// kit structure, cooldowns and skillshots — not click-to-move.
        /// </summary>
        [Test]
        public void A_cursor_position_alone_never_becomes_movement()
        {
            var map = new DefaultHeroInputMap();

            AssertNoMovement(map.Resolve(Snapshot(new Vector2(1f, 0f))),
                "DEC-017: a cursor just next to the hero is not a step");
            AssertNoMovement(map.Resolve(Snapshot(new Vector2(-40f, 25f))),
                "DEC-017: a cursor across the colony is not a run order either");
        }

        // ==========================================================================================
        //  AC — the hero faces the mouse cursor rather than turning toward movement
        // ==========================================================================================

        /// <summary>
        /// R-30. The discriminating case: the hero walks forward-and-right while the cursor sits
        /// straight behind it. Facing must follow the cursor.
        ///
        /// Asserted as a <i>direction</i>, not a rotation: the PRD pins no turn rate, no snapping
        /// rule and no rotation representation, so the only contract is which way the hero ends up
        /// pointed. The final dot-product assertion is what separates "faces the cursor" from
        /// "faces where it is going" — a movement-facing implementation lands on a direction with a
        /// positive dot against <see cref="HeroIntent.MoveDirection"/>, and this one is negative.
        /// </summary>
        [Test]
        public void The_hero_faces_the_cursor_rather_than_the_direction_it_is_walking()
        {
            var state = SoloState();
            var cursorBehindTheHero = new Vector2(0f, -5f);

            var intent = new DefaultHeroInputMap()
                .Resolve(Snapshot(cursorBehindTheHero, PlayerKey.W, PlayerKey.D));

            // Walking forward and to the right...
            Assert.That(intent.MoveDirection.y, Is.GreaterThan(AxisTolerance), "R-30: W walks forward");
            Assert.That(intent.MoveDirection.x, Is.GreaterThan(AxisTolerance), "R-30: D walks right");
            AssertAimsAt(intent, cursorBehindTheHero, "R-30: the aim point is the cursor");

            // ...while looking the other way.
            var hero = NewView<HeroView>("hero");
            hero.Bind("h1", Placeholder(VisualClass.Hero));
            hero.RenderFrom(state);
            hero.Apply(intent);

            Assert.That(hero.Facing.magnitude, Is.EqualTo(1f).Within(PositionTolerance),
                "facing is a unit direction; the PRD pins no rotation representation");
            Assert.That(hero.Facing.y, Is.LessThan(-AxisTolerance),
                "R-30: the cursor is behind the hero, so the hero faces backwards");
            Assert.That(hero.Facing.x, Is.EqualTo(0f).Within(PositionTolerance),
                "R-30: the cursor is straight behind, so there is no sideways component to face");
            Assert.That(Vector2.Dot(hero.Facing, intent.MoveDirection.normalized), Is.LessThan(0f),
                "R-30: the hero faces the mouse cursor, NOT the direction it is moving");
        }

        // ==========================================================================================
        //  AC — no code path blocks on an asset existing
        // ==========================================================================================

        /// <summary>
        /// R-30's delivery constraint, and the ticket's hardest architectural one: an implementation
        /// ticket blocked on art must be impossible by construction. So the asset seam is total —
        /// for every visual class, a null art key and an art key naming a file that does not exist
        /// both resolve to a visible primitive placeholder.
        ///
        /// "Visible" is asserted as a <see cref="Renderer"/> rather than as a particular mesh:
        /// nothing in the PRD says a placeholder is a capsule rather than a quad, and pinning the
        /// shape would reject a correct implementation. What matters is that something renders —
        /// null, an exception and a silently skipped render are the three failures this closes.
        ///
        /// Four classes rather than one so this reads as a rule. Real art lives in <c>art/</c>
        /// already; wiring it is ticket 013.
        /// </summary>
        [TestCase(VisualClass.Hero, null)]
        [TestCase(VisualClass.Hero, "art/characters/not-generated-yet_v1.png")]
        [TestCase(VisualClass.Monster, null)]
        [TestCase(VisualClass.Monster, "art/characters/not-generated-yet_v1.png")]
        [TestCase(VisualClass.Placeable, null)]
        [TestCase(VisualClass.Placeable, "art/props/not-generated-yet_v1.png")]
        [TestCase(VisualClass.Ground, null)]
        [TestCase(VisualClass.Ground, "art/textures/not-generated-yet_512.png")]
        public void A_visual_with_no_art_resolves_to_a_primitive_placeholder(
            VisualClass visualClass, string absentArtKey)
        {
            var resolver = new PlaceholderVisualResolver();

            VisualHandle handle = null;
            Assert.That(() => { handle = resolver.Resolve(visualClass, absentArtKey); }, Throws.Nothing,
                "no code path may block on an asset existing: resolving absent art must not throw");

            Assert.That(handle, Is.Not.Null,
                "the asset seam is total — it must never answer null for " + visualClass);
            Assert.That(handle.Instance, Is.Not.Null,
                "a resolved visual must be something that exists in the scene, not a null hole");

            Track(handle.Instance);

            Assert.That(handle.IsPlaceholder, Is.True,
                "absent art must resolve to the stand-in, and must say so — a silent difference is "
                + "how a missing asset becomes an invisible entity");
            Assert.That(handle.Class, Is.EqualTo(visualClass),
                "the placeholder must stand in for the class that was asked for");

            var renderer = handle.Instance.GetComponentInChildren<Renderer>();
            Assert.That(renderer, Is.Not.Null,
                "a placeholder must actually render; a skipped render is the failure mode this seam "
                + "exists to prevent");
        }

        // ==========================================================================================
        //  AC — visuals render from replicated sim state, never from locally recomputed rules
        // ==========================================================================================

        /// <summary>
        /// R-51. A view shows the sim's numbers and follows them when the sim moves. T10's IL
        /// invariant proves the shell never writes sim state; this proves it actually reads it —
        /// a view rendering from a local cache diverges from the host the first time replication
        /// corrects it.
        /// </summary>
        [Test]
        public void A_monster_view_shows_what_the_sim_says_and_follows_it_when_the_sim_changes()
        {
            var state = SoloState();
            var monster = state.Monsters["m1"];

            var view = NewView<MonsterView>("monster");
            view.Bind("m1", Placeholder(VisualClass.Monster));
            view.RenderFrom(state);

            AssertStandsAt(view.WorldPosition, monster.Pos, "the monster view at its replicated position");
            Assert.That(view.DisplayedHp, Is.EqualTo(monster.Hp).Within(SimTolerance),
                "R-51: HP is whatever the sim says it is");
            Assert.That(view.DisplayedAlive, Is.EqualTo(monster.Alive),
                "R-51: liveness is whatever the sim says it is");

            // The host replicates a new state. The view must follow it, not its own last frame.
            monster.Pos = new Vec2(-7.5, 4.25);
            monster.Hp = 12.0;
            view.RenderFrom(state);

            AssertStandsAt(view.WorldPosition, monster.Pos, "the monster view after the sim moved it");
            Assert.That(view.DisplayedHp, Is.EqualTo(12.0).Within(SimTolerance),
                "R-51: a replicated HP change must reach the view");
        }

        /// <summary>
        /// R-51. The read-direction complement of T10's invariant: a view must not re-derive a rule
        /// the sim owns.
        ///
        /// The state given here is one the sim's own rules never produce — zero HP while still
        /// alive. Only <see cref="MatchSim"/> decides death (R-51), so a view that showed this
        /// monster as dead would be applying a death rule of its own, and would disagree with the
        /// host the moment the rule is retuned. Mirroring an "impossible" state is the correct
        /// behaviour precisely because deciding otherwise is not the view's call.
        /// </summary>
        [Test]
        public void A_view_reports_the_sims_answer_rather_than_recomputing_the_rule()
        {
            var state = SoloState();
            var monster = state.Monsters["m1"];
            monster.Hp = 0.0;
            monster.Alive = true;

            var view = NewView<MonsterView>("monster");
            view.Bind("m1", Placeholder(VisualClass.Monster));
            view.RenderFrom(state);

            Assert.That(view.DisplayedAlive, Is.True,
                "R-51: death is the sim's ruling; a view must not derive it from HP");
            Assert.That(view.DisplayedHp, Is.EqualTo(0.0).Within(SimTolerance),
                "R-51: HP is shown as replicated, not clamped or re-derived");
        }

        // ==========================================================================================
        //  AC — the scene: top-down camera, ground, team spawn, hotspot markers
        // ==========================================================================================

        /// <summary>
        /// The sim/world boundary the rest of the scene assertions stand on (R-51: the sim is
        /// engine-free and carries its own <see cref="Vec2"/>).
        ///
        /// Two properties only, because the PRD picks no axis convention: the colony lands on a
        /// single horizontal plane — which is what a top-down camera can look down at — and the
        /// conversion is an isometry, so the map is not stretched, mirrored into a different shape,
        /// or collapsed to a point. Scale, origin and handedness are deliberately free.
        /// </summary>
        [Test]
        public void The_sim_to_world_conversion_lays_the_colony_flat_without_distorting_it()
        {
            var saloon = new Vec2(-12.0, 6.0);
            var chapel = new Vec2(11.0, 9.0);

            var a = SimSpace.ToWorld(saloon);
            var b = SimSpace.ToWorld(chapel);

            Assert.That(a.y, Is.EqualTo(b.y).Within(PositionTolerance),
                "the colony must lie on one horizontal plane; Unity's Y is the vertical axis the "
                + "top-down camera looks down");
            Assert.That(Vector3.Distance(a, b), Is.EqualTo((float)saloon.DistanceTo(chapel)).Within(1e-2f),
                "the conversion must preserve distance — a shell that stretched or collapsed the map "
                + "would put every R-16 range check somewhere the sim did not mean");

            var roundTripped = SimSpace.ToGround(a);
            Assert.That(roundTripped.X, Is.EqualTo(saloon.X).Within(1e-3),
                "world-to-ground must invert ground-to-world; the cursor is read back through it");
            Assert.That(roundTripped.Y, Is.EqualTo(saloon.Y).Within(1e-3),
                "world-to-ground must invert ground-to-world");
        }

        /// <summary>
        /// The camera sits above the colony and looks down at it from a steep tilt (~60–70°
        /// from the horizon, looking north). Owner override 2026-08-26: bird's-eye (straight
        /// −Y) flattens the Lykos cavern into roofs; the tilt is what shows building sides
        /// and roof edges. Still over the play area, still looking down — not a horizon shot.
        /// Height, field of view and projection stay free.
        /// </summary>
        [Test]
        public void The_built_scene_looks_down_at_the_play_area_from_a_steep_tilt()
        {
            var map = ColonyMap.V1();
            var scene = Track(MatchSceneBuilder.Build(map, new PlaceholderVisualResolver()));

            Assert.That(scene, Is.Not.Null, "the builder must hand back the scene it composed");
            Assert.That(scene.Camera, Is.Not.Null, "R-30: a top-down game needs a camera");

            var forward = scene.Camera.transform.forward.normalized;
            var downDot = Vector3.Dot(forward, Vector3.down);
            // sin(60°)≈0.866, sin(70°)≈0.940 — the Lykos pitch band.
            Assert.That(downDot, Is.GreaterThan(0.84f).And.LessThan(0.95f),
                "the camera must look down at 60–70° from the horizon (building sides visible); "
                + "bird's-eye (dot≈1) flattens the cavern, a horizon shot (dot≈0) is not top-down; "
                + "got " + downDot);

            Assert.That(forward.z, Is.GreaterThan(0.2f),
                "the look is into the cavern (+Z / north), so the south ridge sits in the foreground");

            var eye = scene.Camera.transform.position;
            var ground = SimSpace.ToWorld(map.TeamSpawn);
            Assert.That(eye.y, Is.GreaterThan(ground.y),
                "the camera must be above the colony, not under it");

            var play = PlayAreaBounds(map);
            Assert.That(eye.x, Is.InRange(play.min.x, play.max.x),
                "the camera must be over the play area, not off the side of it");
            Assert.That(eye.z, Is.InRange(play.min.z, play.max.z),
                "the camera must be over the play area, not off the side of it");
        }

        /// <summary>
        /// R-10 / R-33. The scene the headless builder produces contains the ground, the team spawn
        /// where heroes enter and respawn, and one marker per shelter standing at the position
        /// <see cref="ColonyMap.V1"/> gives it — derived from the map rather than hardcoded, so a
        /// retuned layout moves the scene with it instead of silently disagreeing.
        ///
        /// Horizontal placement only: how far a marker floats above the ground is presentation.
        /// </summary>
        [Test]
        public void The_built_scene_has_ground_a_team_spawn_and_a_marker_on_every_hotspot()
        {
            var map = ColonyMap.V1();
            var scene = Track(MatchSceneBuilder.Build(map, new PlaceholderVisualResolver()));

            Assert.That(scene.Ground, Is.Not.Null,
                "the colony needs something to stand on for the session to be playable");
            Assert.That(scene.TeamSpawn, Is.Not.Null,
                "R-10 / R-33: heroes enter and respawn at the team spawn, so the scene must have one");
            AssertStandsAt(scene.TeamSpawn.transform.position, map.TeamSpawn, "the team spawn");

            Assert.That(scene.HotspotMarkers.Keys, Is.EquivalentTo(map.Hotspots.Select(h => h.Id)),
                "R-10: one marker per shelter — no more, no fewer, and named by the sim's own ids");

            foreach (var spec in map.Hotspots)
            {
                Assert.That(scene.HotspotMarkers[spec.Id], Is.Not.Null, "marker for " + spec.Id);
                AssertStandsAt(
                    scene.HotspotMarkers[spec.Id].transform.position, spec.Pos, "marker for " + spec.Id);
            }
        }

        /// <summary>
        /// The scene must be reproducible from a command line, because there is no GUI here and
        /// because a hand-authored .unity file cannot be reviewed in a diff.
        ///
        /// <b>Reflection rather than a direct reference</b> is not a stylistic choice: editor
        /// scripts under <c>Assets/Editor</c> with no asmdef compile into the predefined
        /// <c>Assembly-CSharp-Editor</c>, and an asmdef-based assembly (this one) cannot reference a
        /// predefined assembly at all. The alternative — giving <c>Assets/Editor</c> an asmdef —
        /// would drag <c>PackageBootstrap</c> and <c>ProjectVerify</c> into it, which is not this
        /// ticket's to do.
        ///
        /// <b>This is a structural guard and is expected to be GREEN as soon as the stub compiles.</b>
        /// It has no behaviour to fail on; it fails only if the headless entry point is deleted or
        /// given a signature <c>-executeMethod</c> cannot invoke. The scene's *contents* are pinned
        /// by the two tests above, against <see cref="MatchSceneBuilder"/>, which is the runtime
        /// function this entry point exists to call.
        /// </summary>
        [Test]
        public void The_scene_is_built_by_a_headless_editor_entry_point()
        {
            var builder = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(TypesIn)
                .FirstOrDefault(t => t.FullName == "RedHollow.EditorTools.SceneBuilder");

            Assert.That(builder, Is.Not.Null,
                "there is no GUI here, so the scene must be built by an editor script: expected "
                + "RedHollow.EditorTools.SceneBuilder in Assets/Editor/SceneBuilder.cs");

            var entry = builder.GetMethod(
                "Build", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);

            Assert.That(entry, Is.Not.Null,
                "Unity -executeMethod can only invoke a public static parameterless method; "
                + "SceneBuilder.Build must be one");
            Assert.That(entry.ReturnType, Is.EqualTo(typeof(void)),
                "-executeMethod ignores a return value; the entry point returns void");
        }

        // ==========================================================================================
        //  AC — a solo session is playable with primitive placeholder art
        // ==========================================================================================

        /// <summary>
        /// R-30 / R-50, end to end and as close to "playable" as EditMode can get: a one-player
        /// lobby (R-50, solo is a party of one), the real <see cref="ColonyMap.V1"/>, the real
        /// <see cref="MatchSim"/>, real wave 1 (R-19), the real <see cref="HostLoop"/> from ticket
        /// 010, and every visual in it a primitive placeholder.
        ///
        /// "Progresses" is asserted four ways, because any one alone can pass on a frozen world:
        /// time advances, the wave is still populated, the colony has actually taken damage (R-11 /
        /// R-18 — the world evolved rather than merely ticked), and the match has not fallen over.
        /// Then every monster's view is checked against the sim position it should be standing on,
        /// which is what makes this a *session* rather than a headless sim run.
        ///
        /// No assertion here depends on an asset existing — that is the criterion, stated as a test.
        /// </summary>
        [Test]
        public void A_solo_session_spawns_wave_one_and_keeps_running_on_placeholder_art()
        {
            var map = ColonyMap.V1();
            var config = new SimConfig();
            var state = map.CreateMatchState(config);

            // R-50 / DEC-020 — solo is a one-player lobby, not a special mode.
            var roster = new PartyRoster();
            Assert.That(roster.TryAdd("acc_solo"), Is.True, "R-50: a party of one is a valid party");

            state.Players.Add(new PlayerSlot
            {
                Id = "p1",
                AccountId = "acc_solo",
                HeroClass = HeroClass.Gunslinger,
                Connected = true,
            });

            state.Heroes["h1"] = new Hero
            {
                Id = "h1",
                HeroClass = HeroClass.Gunslinger,
                AccountId = "acc_solo",
                Pos = map.TeamSpawn,
                Hp = 100.0,
                MaxHp = 100.0,
                Alive = true,
            };

            var clock = new SimClock();
            var sim = new MatchSim(state, config, null, clock, null) { ColonyMap = map };
            var host = new MatchSimHost(sim, clock);

            var spawned = sim.SpawnWave(1);
            Assert.That(spawned.MonsterIds, Is.Not.Empty, "R-19: wave 1 puts monsters in the colony");

            // Everything the player will see, on placeholder art alone.
            var visuals = new PlaceholderVisualResolver();
            var scene = Track(MatchSceneBuilder.Build(map, visuals));
            Assert.That(scene, Is.Not.Null, "a session needs a scene");

            var heroView = NewView<HeroView>("hero");
            heroView.Bind("h1", visuals.Resolve(VisualClass.Hero, null));

            var monsterViews = new List<MonsterView>();
            foreach (var id in spawned.MonsterIds)
            {
                var view = NewView<MonsterView>("monster_" + id);
                view.Bind(id, visuals.Resolve(VisualClass.Monster, null));
                monsterViews.Add(view);
            }

            // One monster leaning on a shelter, so the world has somewhere to go. The damage comes
            // off the R-17 catalog, never from a number typed here.
            var attacker = state.Monsters[spawned.MonsterIds[0]];
            var attacks = new ScriptedAttackSource(new MonsterAttackIntent
            {
                MonsterId = attacker.Id,
                MonsterType = attacker.Type,
                TargetId = map.Hotspots[0].Id,
                TargetKind = TargetKind.Hotspot,
                Damage = config.Monsters.StatsFor(attacker.Type).AttackDamage,
            });

            var loop = new HostLoop(host, attacks);
            var civiliansAtStart = state.TotalCivilians;

            // Three seconds of the session actually running.
            for (var i = 0; i < 180; i++)
            {
                loop.Step(Step60Hz);

                heroView.RenderFrom(state);
                foreach (var view in monsterViews)
                {
                    view.RenderFrom(state);
                }
            }

            Assert.That(clock.ElapsedSeconds, Is.GreaterThan(2.9), "the session's clock advances");
            Assert.That(state.Monsters, Is.Not.Empty, "R-19: the wave is still in the world");
            Assert.That(state.TotalCivilians, Is.LessThan(civiliansAtStart),
                "R-11 / R-18: the world evolves while the session runs — the colony is under attack");
            Assert.That(state.Status, Is.EqualTo(MatchStatus.InProgress),
                "three seconds of wave 1 must not end the match");

            Assert.That(heroView.Visual.IsPlaceholder, Is.True,
                "the session is playable before any art exists");

            foreach (var view in monsterViews)
            {
                Assert.That(view.Visual.IsPlaceholder, Is.True,
                    "every monster is drawn with a primitive stand-in");
                AssertStandsAt(
                    view.WorldPosition, state.Monsters[view.MonsterId].Pos, "monster " + view.MonsterId);
            }
        }

        // ==========================================================================================
        //  scenario builders and assertions
        // ==========================================================================================

        /// <summary>One frame of input, spelled without a device.</summary>
        private static InputSnapshot Snapshot(Vector2 cursorGroundPoint, params PlayerKey[] pressed)
        {
            var snapshot = new InputSnapshot { CursorGroundPoint = cursorGroundPoint };
            foreach (var key in pressed)
            {
                snapshot.Pressed.Add(key);
            }

            return snapshot;
        }

        /// <summary>
        /// A minimal live solo match: the hero at the origin and one shambler nearby. Built from
        /// production types directly, as T10 does.
        /// </summary>
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

        /// <summary>A placeholder visual for a view to wear, registered for teardown.</summary>
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

        private MatchScene Track(MatchScene scene)
        {
            if (scene != null)
            {
                Track(scene.Root);
            }

            return scene;
        }

        /// <summary>
        /// The world-space box the colony occupies: every shelter and every breach (R-10 / R-14).
        /// Derived from the map so a retuned layout retunes the assertion with it.
        /// </summary>
        private static Bounds PlayAreaBounds(ColonyMap map)
        {
            var bounds = new Bounds(SimSpace.ToWorld(map.TeamSpawn), Vector3.zero);

            foreach (var spec in map.Hotspots)
            {
                bounds.Encapsulate(SimSpace.ToWorld(spec.Pos));
            }

            foreach (var tunnel in map.EntryTunnels)
            {
                bounds.Encapsulate(SimSpace.ToWorld(tunnel));
            }

            return bounds;
        }

        /// <summary>
        /// Horizontal placement only. How high above the ground a visual floats is presentation
        /// (a capsule sits half its height up), and the PRD says nothing about it.
        /// </summary>
        private static void AssertStandsAt(Vector3 actualWorld, Vec2 expectedGround, string what)
        {
            var expected = SimSpace.ToWorld(expectedGround);

            Assert.That(actualWorld.x, Is.EqualTo(expected.x).Within(PositionTolerance),
                what + ": x must match the sim position " + expectedGround);
            Assert.That(actualWorld.z, Is.EqualTo(expected.z).Within(PositionTolerance),
                what + ": z must match the sim position " + expectedGround);
        }

        private static void AssertNoMovement(HeroIntent intent, string because)
        {
            Assert.That(intent, Is.Not.Null, "R-30: every frame of input must resolve to an intent");
            Assert.That(intent.MoveDirection.magnitude, Is.LessThan(AxisTolerance), because);
        }

        private static void AssertAimsAt(HeroIntent intent, Vector2 cursor, string because)
        {
            Assert.That(intent.AimPoint.x, Is.EqualTo(cursor.x).Within(PositionTolerance), because);
            Assert.That(intent.AimPoint.y, Is.EqualTo(cursor.y).Within(PositionTolerance), because);
        }

        /// <summary>
        /// Sign, not magnitude: <paramref name="expectedSign"/> of 0 means "must not move on this
        /// axis", anything else means "must move that way". Move speed is not in the PRD.
        /// </summary>
        private static void AssertAxis(float actual, float expectedSign, string axis, string because)
        {
            if (Mathf.Approximately(expectedSign, 0f))
            {
                Assert.That(actual, Is.EqualTo(0f).Within(AxisTolerance),
                    because + ": nothing may move it on the " + axis + " axis");
                return;
            }

            Assert.That(actual * expectedSign, Is.GreaterThan(AxisTolerance),
                because + ": must move " + (expectedSign > 0f ? "positive" : "negative")
                + " on the " + axis + " axis, got " + actual);
        }

        private static IEnumerable<Type> TypesIn(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }

        // ==========================================================================================
        //  test doubles
        // ==========================================================================================

        /// <summary>
        /// This step's monster attack candidates, scripted — the same shape T10 uses. The shell's
        /// real movement layer is what produces these in a running session; here they are fixed so
        /// the world has a known way to evolve.
        /// </summary>
        private sealed class ScriptedAttackSource : IMonsterAttackSource
        {
            private readonly MonsterAttackIntent[] _intents;

            public ScriptedAttackSource(params MonsterAttackIntent[] intents)
            {
                _intents = intents ?? new MonsterAttackIntent[0];
            }

            public IReadOnlyList<MonsterAttackIntent> AttacksReadyThisStep(ISimHost sim, double deltaSeconds)
            {
                return _intents;
            }
        }
    }
}
