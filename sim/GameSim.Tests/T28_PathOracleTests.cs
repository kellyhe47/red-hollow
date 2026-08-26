using NUnit.Framework;
using RedHollow.Sim;

namespace GameSim.Tests
{
    /// <summary>
    /// Ticket 028 — the production path oracle (R-16 / B-002). G-004 locks the sim-side rule ("a
    /// declared blocker becomes the target until destroyed") through <see cref="DeclaredPathOracle"/>;
    /// what nothing locked is a production ANSWERER, so every factory-built match ran on
    /// <see cref="OpenPathOracle"/> and no barricade ever blocked anything in the shipped game.
    ///
    /// <see cref="BarricadePathOracle"/> is that answerer: a standing barricade whose footprint the
    /// mover's straight walk crosses. These tests pin the geometry (corridor in/out, behind/beyond,
    /// inclusive boundary), the allowlist (barricades only — a turret blocker would park a wave
    /// chewing an indestructible box), the determinism (first along the path, ordinal ties), and
    /// the integration: the REAL <see cref="MatchSim.SelectTarget"/> redirecting through this
    /// oracle exactly as it does through G-004's declared one, Burrower carve-out included.
    /// </summary>
    [TestFixture]
    public class T28_PathOracleTests
    {
        // ==========================================================================================
        //  geometry — what crosses the walk and what does not
        // ==========================================================================================

        [Test]
        public void A_standing_barricade_on_the_straight_walk_blocks_it()
        {
            var state = StateWith(
                Monster("m1", 0.0, 0.0, target: "hs_1"),
                Hotspot("hs_1", 20.0, 0.0),
                Barricade("wall", 10.0, 0.0));

            var oracle = new BarricadePathOracle(state);

            Assert.That(oracle.BlockerBetween("m1", "hs_1"), Is.EqualTo("wall"),
                "R-16/B-002: a wall dead on the lane blocks the walk");
        }

        [Test]
        public void A_barricade_outside_the_corridor_does_not_block()
        {
            var state = StateWith(
                Monster("m1", 0.0, 0.0, target: "hs_1"),
                Hotspot("hs_1", 20.0, 0.0),
                Barricade("wall", 10.0, 5.0));

            var oracle = new BarricadePathOracle(state);

            Assert.That(oracle.BlockerBetween("m1", "hs_1"), Is.Null,
                "a wall 5.0 off a 1.5-radius lane is not in anybody's way");
        }

        [Test]
        public void The_corridor_boundary_is_inclusive()
        {
            var state = StateWith(
                Monster("m1", 0.0, 0.0, target: "hs_1"),
                Hotspot("hs_1", 20.0, 0.0),
                Barricade("wall", 10.0, 1.5));

            var oracle = new BarricadePathOracle(state);

            Assert.That(oracle.BlockerBetween("m1", "hs_1"), Is.EqualTo("wall"),
                "G-019's convention: a wall at exactly the blocking radius is on the lane");
        }

        [Test]
        public void Walls_behind_the_mover_or_beyond_the_target_do_not_block()
        {
            var state = StateWith(
                Monster("m1", 0.0, 0.0, target: "hs_1"),
                Hotspot("hs_1", 20.0, 0.0),
                Barricade("wall_behind", -5.0, 0.0),
                Barricade("wall_beyond", 25.0, 0.0));

            var oracle = new BarricadePathOracle(state);

            Assert.That(oracle.BlockerBetween("m1", "hs_1"), Is.Null,
                "the walk runs from the mover to the target; ground outside that segment is not "
                + "on the path");
        }

        [Test]
        public void A_destroyed_barricade_blocks_nothing()
        {
            var state = StateWith(
                Monster("m1", 0.0, 0.0, target: "hs_1"),
                Hotspot("hs_1", 20.0, 0.0),
                Barricade("rubble", 10.0, 0.0, exists: false));

            var oracle = new BarricadePathOracle(state);

            Assert.That(oracle.BlockerBetween("m1", "hs_1"), Is.Null,
                "R-16 'until destroyed': rubble has released the lane");
        }

        [Test]
        public void Only_barricades_block_the_walk()
        {
            var state = StateWith(
                Monster("m1", 0.0, 0.0, target: "hs_1"),
                Hotspot("hs_1", 20.0, 0.0));
            state.Placeables["turret"] = new Placeable
            {
                Id = "turret",
                Type = PlaceableType.Turret,
                Pos = new Vec2(8.0, 0.0),
                Exists = true,
            };
            state.Placeables["spikes"] = new Placeable
            {
                Id = "spikes",
                Type = PlaceableType.SpikeTrap,
                Pos = new Vec2(12.0, 0.0),
                Exists = true,
            };

            var oracle = new BarricadePathOracle(state);

            Assert.That(oracle.BlockerBetween("m1", "hs_1"), Is.Null,
                "a turret has no HP column (ApplyPlaceableDamage no-ops on it) — returning it "
                + "would park the wave chewing an indestructible box; traps are meant to be "
                + "walked over, that is what triggers them");
        }

        [Test]
        public void The_target_barricade_is_never_its_own_blocker()
        {
            var state = StateWith(
                Monster("m1", 0.0, 0.0, target: "wall"),
                Barricade("wall", 10.0, 0.0));

            var oracle = new BarricadePathOracle(state);

            Assert.That(oracle.BlockerBetween("m1", "wall"), Is.Null,
                "a monster already sent at a wall keeps walking at it — re-answering the same "
                + "wall forever would re-run the redirect every retarget");
        }

        [Test]
        public void A_second_wall_between_the_mover_and_its_wall_target_still_blocks()
        {
            var state = StateWith(
                Monster("m1", 0.0, 0.0, target: "far_wall"),
                Barricade("far_wall", 20.0, 0.0),
                Barricade("near_wall", 10.0, 0.0));

            var oracle = new BarricadePathOracle(state);

            Assert.That(oracle.BlockerBetween("m1", "far_wall"), Is.EqualTo("near_wall"),
                "walls layer: the first wall on the walk is the one the monster reaches");
        }

        // ==========================================================================================
        //  determinism — first along the path, ordinal ties (R-51)
        // ==========================================================================================

        [Test]
        public void The_first_wall_along_the_walk_wins()
        {
            var state = StateWith(
                Monster("m1", 0.0, 0.0, target: "hs_1"),
                Hotspot("hs_1", 20.0, 0.0),
                Barricade("wall_far", 15.0, 0.0),
                Barricade("wall_near", 5.0, 0.0));

            var oracle = new BarricadePathOracle(state);

            Assert.That(oracle.BlockerBetween("m1", "hs_1"), Is.EqualTo("wall_near"),
                "the monster meets the near wall first; answering the far one would teleport its "
                + "attention through a standing wall");
        }

        [Test]
        public void Equidistant_walls_tie_to_the_lowest_ordinal_id()
        {
            var state = StateWith(
                Monster("m1", 0.0, 0.0, target: "hs_1"),
                Hotspot("hs_1", 20.0, 0.0));

            // Same projection along the walk, opposite sides of the lane, both inside the radius.
            state.Placeables["wall_b"] = BarricadeEntity("wall_b", 10.0, 1.0);
            state.Placeables["wall_a"] = BarricadeEntity("wall_a", 10.0, -1.0);

            var oracle = new BarricadePathOracle(state);

            Assert.That(oracle.BlockerBetween("m1", "hs_1"), Is.EqualTo("wall_a"),
                "R-51: ties break by ordinal id (R-16's own tiebreak), never by dictionary "
                + "iteration order — a host and a rebuilt world must answer alike");
        }

        [Test]
        public void An_unknown_mover_or_target_answers_no_blocker()
        {
            var state = StateWith(
                Monster("m1", 0.0, 0.0, target: "hs_1"),
                Hotspot("hs_1", 20.0, 0.0),
                Barricade("wall", 10.0, 0.0));

            var oracle = new BarricadePathOracle(state);

            Assert.That(oracle.BlockerBetween("m_ghost", "hs_1"), Is.Null,
                "a mover the match does not hold has no path to ask about");
            Assert.That(oracle.BlockerBetween("m1", "hs_ghost"), Is.Null,
                "a target the match does not hold has no path to it");
            Assert.That(oracle.BlockerBetween(null, null), Is.Null,
                "nulls are an ordinary non-answer, never a throw mid-step");
        }

        // ==========================================================================================
        //  integration — the REAL SelectTarget redirects through this oracle (G-004's rule, live)
        // ==========================================================================================

        [Test]
        public void Select_target_redirects_a_monster_onto_the_wall_across_its_lane()
        {
            var state = StateWith(
                Monster("m1", 0.0, 0.0, target: null),
                Hotspot("hs_1", 20.0, 0.0),
                Barricade("wall", 10.0, 0.0));

            var sim = new MatchSim(state, new SimConfig(), null, new SimClock(),
                new BarricadePathOracle(state));

            var result = sim.SelectTarget("m1");

            Assert.That(result.TargetId, Is.EqualTo("wall"),
                "R-16/B-002 live: the wall across the lane IS the target, at its own distance — "
                + "this is the rule G-004 locks, answered by production geometry instead of a "
                + "declared fixture relation");
            Assert.That(result.Distance, Is.EqualTo(10.0).Within(1e-9),
                "the distance reported is to the wall the monster now walks at");
        }

        [Test]
        public void A_burrower_tunnels_under_the_same_wall()
        {
            var state = StateWith(
                Hotspot("hs_1", 20.0, 0.0),
                Barricade("wall", 10.0, 0.0));
            state.Monsters["m1"] = new Monster
            {
                Id = "m1",
                Type = MonsterType.Burrower,
                Pos = new Vec2(0.0, 0.0),
                Hp = 80.0,
                Alive = true,
            };

            var sim = new MatchSim(state, new SimConfig(), null, new SimClock(),
                new BarricadePathOracle(state));

            var result = sim.SelectTarget("m1");

            Assert.That(result.TargetId, Is.EqualTo("hs_1"),
                "DEC-007/B-003: a Burrower tunnels — the sim never consults the oracle for it, so "
                + "the wall is invisible and the shelter behind it is the target");
        }

        [Test]
        public void Destroying_the_wall_releases_the_lane_on_the_next_retarget()
        {
            var state = StateWith(
                Monster("m1", 0.0, 0.0, target: null),
                Hotspot("hs_1", 20.0, 0.0),
                Barricade("wall", 10.0, 0.0));

            var sim = new MatchSim(state, new SimConfig(), null, new SimClock(),
                new BarricadePathOracle(state));

            Assert.That(sim.SelectTarget("m1").TargetId, Is.EqualTo("wall"), "sanity: blocked");

            // Chew the wall down (R-16 'until destroyed' — ApplyPlaceableDamage owns the collapse).
            var down = sim.ApplyPlaceableDamage(new PlaceableDamageRequest
            {
                AttackerId = "m1",
                AttackerType = MonsterType.Shambler,
                Damage = 9999.0,
                TargetId = "wall",
            });
            Assert.That(down.Destroyed, Is.True, "sanity: the wall collapsed");

            Assert.That(sim.SelectTarget("m1").TargetId, Is.EqualTo("hs_1"),
                "the lane is open again: the next retarget walks the monster at the shelter");
        }

        // ==========================================================================================
        //  scenario builders
        // ==========================================================================================

        private static MatchState StateWith(params object[] entities)
        {
            var state = new MatchState();
            foreach (var entity in entities)
            {
                switch (entity)
                {
                    case Monster monster:
                        state.Monsters[monster.Id] = monster;
                        break;
                    case Hotspot hotspot:
                        state.Hotspots[hotspot.Id] = hotspot;
                        break;
                    case Placeable placeable:
                        state.Placeables[placeable.Id] = placeable;
                        break;
                }
            }

            return state;
        }

        private static Monster Monster(string id, double x, double y, string target)
        {
            return new Monster
            {
                Id = id,
                Type = MonsterType.Shambler,
                Pos = new Vec2(x, y),
                Hp = 60.0,
                Alive = true,
                TargetId = target,
            };
        }

        private static Hotspot Hotspot(string id, double x, double y)
        {
            return new Hotspot { Id = id, Pos = new Vec2(x, y), Civilians = 5 };
        }

        private static Placeable Barricade(string id, double x, double y, bool exists = true)
        {
            var wall = BarricadeEntity(id, x, y);
            wall.Exists = exists;
            return wall;
        }

        private static Placeable BarricadeEntity(string id, double x, double y)
        {
            return new Placeable
            {
                Id = id,
                Type = PlaceableType.Barricade,
                Pos = new Vec2(x, y),
                PurchaseCost = 100,
                Hp = 300.0,
                Exists = true,
            };
        }
    }
}
