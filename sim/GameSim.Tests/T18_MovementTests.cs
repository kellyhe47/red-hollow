using System;
using System.Collections.Generic;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Ticket 018 (T-18): movement — the missing verb between every position the sim writes and
    /// every rule that reads one.
    ///
    /// Nothing in the sim advanced a position over time. Positions were only ever *set*: at spawn
    /// (<see cref="MatchSim.SpawnWave"/>), at respawn, by Stampede's knockback and by placement. So
    /// monsters never walked to a hotspot, never arrived, never attacked one, and R-02's defeat
    /// condition — the only loss rule in the game — was unreachable in a real match.
    /// <see cref="Monster.CurrentSpeed"/> was written at spawn and multiplied by the lasso and then
    /// read by nothing that moved anything, so DEC-008's 50% slow affected nothing and R-17's Speed
    /// column was inert. G-018 grades the slow being applied and G-019 grades it expiring; the
    /// behaviour those two fixtures bracket did not exist between them.
    ///
    /// This ticket therefore grades no fixture and the whole contract lives here. Where the PRD is
    /// silent these tests assert relationships and shape rather than values:
    ///
    ///   * distances are asserted as <c>speed * time</c> against speeds the test itself configures,
    ///     never against R-17's table — the stat rows are explicitly tunable (R-16), and a test that
    ///     hardcoded "a Shambler covers 2.0" would freeze balance data as contract. Speeds and
    ///     intervals are chosen so their product is exact in binary (2.0 over 0.5s), so no assertion
    ///     here is a floating-point argument;
    ///   * the lasso is asserted as a *ratio* against <see cref="SimConfig.LassoSlowMultiplier"/>,
    ///     which is the fixture-locked name DEC-008 lives under;
    ///   * arrival asserts that no ground is covered beyond the gap and that the position then stops
    ///     changing — "clamp exactly onto the target" is the natural reading but it is one specific
    ///     implementation, and a stopping radius would satisfy the requirement equally;
    ///   * hero speed has no PRD number at all (R-31's class table is HP/basic/Q/E/passive), so
    ///     these tests configure one and assert the sim follows it. The default is never asserted;
    ///   * diagonal normalisation is *decided* here, not read — see
    ///     <see cref="Hero_moves_in_the_commanded_direction_at_its_configured_speed"/>;
    ///   * the replication granularity of a per-tick position change is left open: no fixture pins
    ///     it, and forcing one delta row per monster per tick would flood the netcode layer 60 times
    ///     a second. Only "a tick that moved nobody replicates nothing" is asserted;
    ///   * undefined inputs (no target, a stale target id) assert non-corruption and the speed
    ///     bound, never a particular answer.
    ///
    /// Scenarios are built straight from production types; the fixture JSON loader is the golden
    /// adapter's contract with eval/golden, not a test fixture builder.
    /// </summary>
    [TestFixture]
    public class T18_MovementTests
    {
        private const double Tolerance = 1e-9;

        /// <summary>Ids used across the fixtures below; nothing here asserts a naming scheme.</summary>
        private const string Mover = "m1";

        private const string Shelter = "hs_saloon";

        // ---- R-17 / R-18: a monster closes on the thing it is targeting ---------------------------

        /// <summary>
        /// The core of the ticket. A monster with a target covers <c>CurrentSpeed * deltaSeconds</c>
        /// of ground toward it — 2.0 units per second over half a second is exactly 1.0, so the
        /// assertion is arithmetic rather than a tolerance argument.
        ///
        /// Table-driven over the three things R-16 lets a monster target, because "closes distance
        /// to *it*" is the criterion and a mover that can only resolve hotspot positions would walk
        /// nowhere when it is chasing a hero or chewing on the barricade in its way (G-004). The
        /// three rows are otherwise identical: same start, same speed, same interval, same distance.
        ///
        /// Speed is configured per monster rather than read from <see cref="SimConfig.Monsters"/>:
        /// <see cref="Monster.CurrentSpeed"/> is the field the rule must read, and it is exactly the
        /// field the lasso rewrites. A mover that consulted the catalog row instead would pass this
        /// test and fail every DEC-008 one below.
        /// </summary>
        [TestCase(TargetHotspot, TestName = "closes_on_a_hotspot")]
        [TestCase(TargetHero, TestName = "closes_on_a_hero")]
        [TestCase(TargetBarricade, TestName = "closes_on_the_barricade_blocking_it")]
        public void A_monster_closes_distance_to_its_target_at_its_current_speed(string targetKind)
        {
            var sim = MoveSim(out var state);
            var target = PlaceTarget(state, targetKind, new Vec2(10.0, 0.0));
            var monster = MonsterAt(state, Mover, new Vec2(0.0, 0.0), speed: 2.0, targetId: target);

            sim.TickMonsterMovement(0.5);

            Assert.Multiple(() =>
            {
                Assert.That(monster.Pos.DistanceTo(new Vec2(0.0, 0.0)), Is.EqualTo(1.0).Within(Tolerance),
                    "R-17/R-18: 2.0 units per second for 0.5s is 1.0 unit of ground");
                Assert.That(monster.Pos.DistanceTo(new Vec2(10.0, 0.0)), Is.EqualTo(9.0).Within(Tolerance),
                    "the ground has to be covered *toward* the target, not in some other direction");
                Assert.That(monster.Pos.Y, Is.EqualTo(0.0).Within(Tolerance),
                    "the default straight-line oracle sends it down the axis it started on");
            });
        }

        /// <summary>
        /// The reason this ticket exists, end to end: a monster picks a target through the real
        /// <see cref="MatchSim.SelectTarget"/> (R-16, green since ticket 002) and then actually
        /// reaches it. Until movement existed this loop could not close, so R-02's defeat condition
        /// — the only loss rule in the game — could never fire in a real match however long it ran.
        ///
        /// The tick loop is bounded and generous (20 units of travel for a 10-unit gap) so the test
        /// fails on "never arrives", not on an off-by-one in how many ticks arrival takes.
        /// </summary>
        [Test]
        public void A_monster_can_now_reach_the_hotspot_it_selected()
        {
            var sim = MoveSim(out var state);
            state.Hotspots[Shelter] = new Hotspot { Id = Shelter, Pos = new Vec2(10.0, 0.0), Civilians = 8 };
            var monster = MonsterAt(state, Mover, new Vec2(0.0, 0.0), speed: 2.0);

            var selection = sim.SelectTarget(Mover);
            Assert.That(selection.TargetId, Is.EqualTo(Shelter), "R-16: the only target in the world");

            for (var tick = 0; tick < 100; tick++)
            {
                sim.TickMonsterMovement(0.1);
            }

            Assert.That(monster.Pos.DistanceTo(state.Hotspots[Shelter].Pos), Is.LessThan(1.0),
                "R-02/R-18: a monster that selects a shelter must be able to get to it — until it "
                + "can, nothing can ever attack a hotspot and defeat is unreachable");
        }

        // ---- DEC-008: the lasso finally means something --------------------------------------------

        /// <summary>
        /// The sharpest assertion in the ticket, and the first time DEC-008 has consequences.
        ///
        /// Two identical monsters, same start, same target, same speed, moved over the same
        /// interval. One is lassoed through the real <see cref="MatchSim.ApplyAbility"/> (G-018,
        /// green) and must cover exactly half the ground of the other — not "less", not
        /// "approximately half": <see cref="SimConfig.LassoSlowMultiplier"/> is fixture-locked at
        /// 0.5 and the ratio is asserted against the config field rather than the literal, so a
        /// rebalance moves the test with it.
        ///
        /// Then the clock is advanced past the effect's deadline and
        /// <see cref="MatchSim.TickStatusEffects"/> (G-019, green) restores
        /// <see cref="Monster.CurrentSpeed"/>. The recovered monster must cover the *same* ground
        /// per tick as the one that was never slowed — a mover that captured its speed once, or
        /// that read <see cref="Monster.BaseSpeed"/> instead of CurrentSpeed, fails one half of this
        /// or the other.
        ///
        /// The shelter is far enough away (100 units) that neither monster arrives, so nothing here
        /// is contaminated by the arrival rule below.
        /// </summary>
        [Test]
        public void A_lassoed_monster_covers_half_the_ground_and_recovers_when_the_slow_expires()
        {
            var config = new SimConfig();
            var clock = new SimClock(0.0);
            var sim = MoveSim(out var state, config, clock);
            state.Hotspots[Shelter] = new Hotspot { Id = Shelter, Pos = new Vec2(100.0, 0.0), Civilians = 8 };

            var free = MonsterAt(state, "m_free", new Vec2(0.0, 0.0), speed: 2.0, targetId: Shelter);
            var slowed = MonsterAt(state, "m_slow", new Vec2(0.0, 0.0), speed: 2.0, targetId: Shelter);

            sim.ApplyAbility(new AbilityCastRequest
            {
                CasterId = "hero_rancher",
                Ability = AbilityName.Lasso,
                TargetId = slowed.Id,
            });

            var freeStart = free.Pos;
            var slowedStart = slowed.Pos;
            sim.TickMonsterMovement(1.0);

            var freeStep = free.Pos.DistanceTo(freeStart);
            var slowedStep = slowed.Pos.DistanceTo(slowedStart);

            Assert.Multiple(() =>
            {
                Assert.That(freeStep, Is.EqualTo(2.0).Within(Tolerance),
                    "the control monster covers speed * time with no effect on it");
                Assert.That(slowedStep, Is.EqualTo(freeStep * config.LassoSlowMultiplier).Within(Tolerance),
                    "R-31/DEC-008: the lasso is a 50% slow, so the lassoed monster covers exactly "
                    + "half the ground of an identical one over the same interval");
            });

            // R-31/G-019 — past the deadline, the effect lifts and base speed is restored.
            clock.Advance(config.LassoDurationSeconds + 0.5);
            sim.TickStatusEffects();
            Assert.That(slowed.CurrentSpeed, Is.EqualTo(slowed.BaseSpeed).Within(Tolerance),
                "precondition (G-019): expiry restores CurrentSpeed before movement is asked again");

            freeStart = free.Pos;
            slowedStart = slowed.Pos;
            sim.TickMonsterMovement(1.0);

            Assert.That(slowed.Pos.DistanceTo(slowedStart),
                Is.EqualTo(free.Pos.DistanceTo(freeStart)).Within(Tolerance),
                "R-31/G-019: once the slow expires the monster is back to full pace — a mover that "
                + "cached its speed, or that never re-read CurrentSpeed, leaves a permanent residue");
        }

        // ---- arrival ------------------------------------------------------------------------------

        /// <summary>
        /// A monster that reaches its target stops there.
        ///
        /// The step is deliberately enormous — 100 units of travel for a 10-unit gap — so a mover
        /// that simply adds <c>speed * dt</c> along the direction sails 90 units past the shelter
        /// and, with the direction recomputed next tick, spends the rest of the match oscillating
        /// across it. That is the bug this test exists for, and it is invisible at small step sizes.
        ///
        /// Two properties, neither of them a clamp: no more ground is covered than there was ground
        /// to cover (so it cannot end up on the far side), and stepping again five more times moves
        /// it nowhere (so it neither oscillates nor orbits). Whether the implementation lands
        /// exactly on the target or halts at a stopping radius is not asserted — the PRD names no
        /// melee reach, so pinning one here would ship this test's guess as spec.
        /// </summary>
        [Test]
        public void A_monster_that_reaches_its_target_stops_and_stays_stopped()
        {
            var sim = MoveSim(out var state);
            state.Hotspots[Shelter] = new Hotspot { Id = Shelter, Pos = new Vec2(10.0, 0.0), Civilians = 8 };
            var monster = MonsterAt(state, Mover, new Vec2(0.0, 0.0), speed: 100.0, targetId: Shelter);

            var start = monster.Pos;
            var gap = start.DistanceTo(state.Hotspots[Shelter].Pos);

            sim.TickMonsterMovement(1.0);

            var arrived = monster.Pos;
            var travelled = arrived.DistanceTo(start);
            var remaining = arrived.DistanceTo(state.Hotspots[Shelter].Pos);

            Assert.Multiple(() =>
            {
                Assert.That(travelled, Is.LessThanOrEqualTo(gap + Tolerance),
                    "R-18: a 100-unit step across a 10-unit gap must not carry the monster past its "
                    + "target — it covered " + travelled + " of ground to close " + gap);
                Assert.That(remaining, Is.LessThan(gap),
                    "the step has to make progress; " + remaining + " is no closer than " + gap);
            });

            for (var tick = 0; tick < 5; tick++)
            {
                sim.TickMonsterMovement(1.0);
            }

            Assert.That(monster.Pos, Is.EqualTo(arrived),
                "an arrived monster holds still: five more steps moved it from " + arrived + " to "
                + monster.Pos + ", which is an orbit or an oscillation, not an arrival");
        }

        // ---- the dead, and the targetless ------------------------------------------------------------

        /// <summary>
        /// A dead monster does not move, however alive its target is. Corpses linger in
        /// <see cref="MatchState.Monsters"/> until the roster is cleared, so a mover that walks the
        /// dictionary without checking <see cref="Monster.Alive"/> marches the whole graveyard at
        /// the shelters.
        ///
        /// The observation is asserted alongside it, and this is the one tick where its content is
        /// unambiguous: nothing moved, so nothing materially changed, so nothing replicates. What
        /// this ticket deliberately does *not* pin is the opposite case — whether a tick that did
        /// move monsters emits one delta row per monster is left to the implementer, because at 60
        /// ticks a second against a 30-monster wave (R-19) that choice is a netcode decision and no
        /// fixture makes it. G-013 replicating <c>placeables.count</c> is the precedent either way.
        /// </summary>
        [Test]
        public void A_dead_monster_does_not_move()
        {
            var sim = MoveSim(out var state);
            state.Hotspots[Shelter] = new Hotspot { Id = Shelter, Pos = new Vec2(10.0, 0.0), Civilians = 8 };
            var corpse = MonsterAt(state, Mover, new Vec2(3.0, 4.0), speed: 2.0, targetId: Shelter, alive: false);

            var result = sim.TickMonsterMovement(1.0);

            Assert.Multiple(() =>
            {
                Assert.That(corpse.Pos, Is.EqualTo(new Vec2(3.0, 4.0)), "R-18: the dead do not walk");
                Assert.That(result.MonstersMoved, Is.EqualTo(0));
                Assert.That(sim.LastObservation.Result, Is.Not.Null,
                    "every command records its result for replication, including one that did nothing");
                Assert.That(sim.LastObservation.StateChanges, Is.Empty,
                    "nothing moved, so nothing materially changed and nothing may replicate");
            });
        }

        /// <summary>
        /// A monster with nowhere to go: no target at all, or a target id that no longer resolves —
        /// the hero it was chasing died and left <see cref="MatchState.Heroes"/>, or the barricade
        /// it was chewing was sold in planning (R-22).
        ///
        /// Both are ordinary mid-match states, not caller bugs, and neither has a specified
        /// behaviour: holding position is the obvious reading, but wandering, drifting to the
        /// nearest breach or standing still are all defensible and the PRD chooses none. So this
        /// asserts only what must hold regardless — the tick does not throw an unimplemented rule at
        /// the host loop, the monster stays in the world and stays alive, its position stays a real
        /// number, and whatever it does it cannot outrun its own speed.
        /// </summary>
        [TestCase(null, TestName = "no_target_at_all")]
        [TestCase("hs_demolished", TestName = "a_target_id_that_no_longer_resolves")]
        public void A_monster_with_no_reachable_target_behaves_in_a_defined_way(string targetId)
        {
            var sim = MoveSim(out var state);
            var monster = MonsterAt(state, Mover, new Vec2(2.0, -3.0), speed: 2.0, targetId: targetId);
            var start = monster.Pos;

            var thrown = Attempt(() => sim.TickMonsterMovement(0.5));

            Assert.Multiple(() =>
            {
                AssertDefined(thrown);
                Assert.That(state.Monsters.ContainsKey(Mover), Is.True,
                    "a monster with no target is still a monster");
                Assert.That(monster.Alive, Is.True, "having nowhere to go does not kill it");
                Assert.That(double.IsNaN(monster.Pos.X) || double.IsNaN(monster.Pos.Y), Is.False,
                    "a normalised direction toward nothing is the classic NaN: it poisons the "
                    + "position, every distance computed from it, and every target selection after");
                Assert.That(double.IsInfinity(monster.Pos.X) || double.IsInfinity(monster.Pos.Y), Is.False);
                Assert.That(monster.Pos.DistanceTo(start), Is.LessThanOrEqualTo(1.0 + Tolerance),
                    "whatever it does with no target, the speed rule still binds it: 2.0/s for 0.5s "
                    + "is 1.0 unit, and no undefined case may buy free ground");
            });
        }

        // ---- R-18 / R-51: direction comes from the injected seam ---------------------------------------

        /// <summary>
        /// The seam itself. R-18 says monster movement uses NavMesh paths, which is UnityEngine and
        /// can never live in GameSim (R-51) — so direction is asked of an injected
        /// <see cref="IDirectionOracle"/> exactly as blocking is asked of an injected
        /// <see cref="IPathOracle"/>, and the sim applies its own distance to the answer.
        ///
        /// The oracle here answers with a direction perpendicular to the target and three units
        /// long, which pins both halves at once: the monster must travel the way the *oracle* said
        /// rather than the way the target lies (so a straight line is a default, not a hardcoded
        /// rule), and it must travel <c>speed * dt</c> rather than three times that (so the oracle
        /// supplies a direction and never a distance — the sim owns speed, which is what makes
        /// DEC-008 and R-17 enforceable at all).
        ///
        /// The query is asserted too, because it is the shell's whole interface: a NavMesh
        /// implementation needs the mover's identity to choose an agent type — R-18's parenthetical
        /// is that a Burrower's path ignores barricade obstacles — and both endpoints to path
        /// between.
        /// </summary>
        [Test]
        public void Monster_direction_comes_from_the_injected_oracle_and_never_its_magnitude()
        {
            var oracle = new ScriptedDirectionOracle(new Vec2(0.0, 3.0));
            var sim = MoveSim(out var state, directions: oracle);
            state.Hotspots[Shelter] = new Hotspot { Id = Shelter, Pos = new Vec2(10.0, 0.0), Civilians = 8 };
            var monster = MonsterAt(state, Mover, new Vec2(0.0, 0.0), speed: 2.0, targetId: Shelter);

            sim.TickMonsterMovement(0.5);

            Assert.Multiple(() =>
            {
                Assert.That(monster.Pos.DistanceTo(new Vec2(0.0, 0.0)), Is.EqualTo(1.0).Within(Tolerance),
                    "R-17: the sim owns how far — 2.0/s for 0.5s is 1.0 unit however long the "
                    + "oracle's vector is, or an oracle could hand a monster free speed the lasso "
                    + "cannot slow");
                Assert.That(monster.Pos.Y, Is.GreaterThan(0.0),
                    "R-18: it must travel the way the oracle pointed, not the way the target lies");
                Assert.That(monster.Pos.X, Is.EqualTo(0.0).Within(Tolerance),
                    "the oracle pointed straight up; nothing may bend that back toward the target");

                Assert.That(oracle.Queries, Has.Count.GreaterThanOrEqualTo(1),
                    "the oracle must actually be asked — a sim that never calls it has kept pathing");
                Assert.That(oracle.Queries[0].MoverId, Is.EqualTo(Mover),
                    "R-18: the shell needs to know who is moving to path a Burrower differently");
                Assert.That(oracle.Queries[0].From, Is.EqualTo(new Vec2(0.0, 0.0)),
                    "it is asked from where the mover actually stands");
                Assert.That(oracle.Queries[0].To, Is.EqualTo(new Vec2(10.0, 0.0)),
                    "and toward where its target actually is");
            });
        }

        /// <summary>
        /// The other half of the seam: an oracle that answers with a zero vector is saying "no step"
        /// — a NavMesh reporting no path, which is how the shell declines without the sim knowing
        /// what a NavMesh is. The monster must hold its ground rather than falling back to a
        /// straight line through whatever the navigation data was avoiding.
        /// </summary>
        [Test]
        public void A_mover_the_oracle_gives_no_direction_for_holds_its_ground()
        {
            var oracle = new ScriptedDirectionOracle(new Vec2(0.0, 0.0));
            var sim = MoveSim(out var state, directions: oracle);
            state.Hotspots[Shelter] = new Hotspot { Id = Shelter, Pos = new Vec2(10.0, 0.0), Civilians = 8 };
            var monster = MonsterAt(state, Mover, new Vec2(-4.0, 1.0), speed: 2.0, targetId: Shelter);

            sim.TickMonsterMovement(1.0);

            Assert.That(monster.Pos, Is.EqualTo(new Vec2(-4.0, 1.0)),
                "R-18: no path means no step — falling back to a straight line would walk the "
                + "monster through the geometry the shell's navigation data exists to route around");
        }

        // ---- R-30: heroes move on a commanded direction ------------------------------------------------

        /// <summary>
        /// R-30. WASD is a command: the shell resolves the keys into a direction (ticket 016 ships
        /// <c>HeroIntent.MoveDirection</c>) and the sim applies its own speed to it. At 3.0 units a
        /// second for 2.0 seconds every commanded direction covers exactly 6.0 units of ground, and
        /// the movement is parallel to what was commanded.
        ///
        /// **Diagonal normalisation is decided here, not read.** The PRD does not say whether a
        /// diagonal is normalised: R-30 names the controls and nothing states a movement vector's
        /// length. But a raw WASD diagonal has both components at 1, and applying it unnormalised
        /// makes a hero travel 1.41x faster on the diagonal than on the cardinals — a real and
        /// well-known movement bug that turns "hold W and D" into the fastest way to cross the map.
        /// So this ticket pins normalisation: magnitude carries no meaning, only direction does, and
        /// the (1,1) and (3,0) rows exist to enforce exactly that.
        ///
        /// The zero row is the same rule at its limit — a player touching nothing gets no ground and
        /// no NaN out of normalising a zero vector.
        /// </summary>
        [TestCase(1.0, 0.0, 6.0, TestName = "east_a_cardinal")]
        [TestCase(0.0, -1.0, 6.0, TestName = "south_a_cardinal")]
        [TestCase(1.0, 1.0, 6.0, TestName = "a_raw_wasd_diagonal_is_not_a_141_percent_sprint")]
        [TestCase(3.0, 0.0, 6.0, TestName = "an_over_long_vector_buys_no_extra_ground")]
        [TestCase(0.0, 0.0, 0.0, TestName = "no_keys_held_is_no_movement")]
        public void Hero_moves_in_the_commanded_direction_at_its_configured_speed(
            double dirX, double dirY, double expectedDistance)
        {
            var sim = MoveSim(out var state);
            SetHeroSpeed(sim, 3.0);
            var hero = HeroAt(state, "h1", HeroClass.Gunslinger, new Vec2(0.0, 0.0));

            var result = sim.MoveHero(new HeroMoveRequest
            {
                HeroId = hero.Id,
                Direction = new Vec2(dirX, dirY),
                DeltaSeconds = 2.0,
            });

            var moved = hero.Pos;
            Assert.Multiple(() =>
            {
                Assert.That(moved.DistanceTo(new Vec2(0.0, 0.0)), Is.EqualTo(expectedDistance).Within(Tolerance),
                    "R-30: 3.0 units a second for 2.0s is " + expectedDistance + " units of ground for "
                    + "direction (" + dirX + ", " + dirY + ") — magnitude is not speed");
                Assert.That(result.DistanceMoved, Is.EqualTo(expectedDistance).Within(Tolerance),
                    "the result the host replicates must agree with the position it wrote");

                if (expectedDistance > 0.0)
                {
                    Assert.That((moved.X * dirY) - (moved.Y * dirX), Is.EqualTo(0.0).Within(Tolerance),
                        "R-30: the hero walks parallel to the commanded direction");
                    Assert.That((moved.X * dirX) + (moved.Y * dirY), Is.GreaterThan(0.0),
                        "R-30: and along it, not backwards up it");
                }
            });
        }

        /// <summary>
        /// R-30 — hero pace is configuration, not a constant in rule code.
        ///
        /// The PRD gives no hero move speed anywhere: R-31's class table is HP, basic attack, Q, E
        /// and passive, and R-30 covers the controls rather than the pace. Ticket 018 therefore put
        /// the number in <see cref="MatchSim.HeroMovement"/> — a default plus per-class overrides,
        /// keyed by the <see cref="HeroClass"/> constants exactly as <see cref="HeroKitCatalog"/> is
        /// — for the same reason <see cref="MatchSim.WaveTable"/> lives there: it is movement-rule
        /// data nothing else in the sim reads.
        ///
        /// Both readings are pinned in one match, because they are one rule: a class the config
        /// names moves at its own speed, a class it does not moves at the default. The numbers are
        /// chosen to appear nowhere in the PRD — 3.0 and 7.0 are neither a monster speed (1.5, 2.0,
        /// 2.5, 5.0) nor any other tunable — so a hardcoded constant cannot pass by luck.
        /// </summary>
        [Test]
        public void Hero_move_speed_is_read_from_configuration()
        {
            var sim = MoveSim(out var state);
            SetHeroSpeed(sim, 3.0);
            SetHeroSpeed(sim, HeroClass.Rancher, 7.0);

            var byDefault = HeroAt(state, "h_gun", HeroClass.Gunslinger, new Vec2(0.0, 0.0));
            var tuned = HeroAt(state, "h_ranch", HeroClass.Rancher, new Vec2(0.0, 0.0));

            sim.MoveHero(Step(byDefault.Id));
            sim.MoveHero(Step(tuned.Id));

            Assert.Multiple(() =>
            {
                Assert.That(byDefault.Pos.DistanceTo(new Vec2(0.0, 0.0)), Is.EqualTo(3.0).Within(Tolerance),
                    "R-30: a class with no configured speed of its own walks at the default");
                Assert.That(tuned.Pos.DistanceTo(new Vec2(0.0, 0.0)), Is.EqualTo(7.0).Within(Tolerance),
                    "R-30: a class the config names walks at its own speed — a number written into "
                    + "rule code would leave the shell's ScriptableObject with nothing to turn");
            });
        }

        /// <summary>
        /// R-33. A dead hero does not move: they are untargetable and spectating a living ally, and
        /// their body is not on the field to be walked around. Their client is still running and
        /// still sending input, so a mover that trusts the command rather than checking
        /// <see cref="Hero.Alive"/> lets a corpse tour the colony until it respawns ten seconds
        /// later (DEC-010) — and respawn writes a position of its own, which that ghost walk would
        /// silently be fighting with.
        /// </summary>
        [Test]
        public void A_dead_hero_does_not_move()
        {
            var sim = MoveSim(out var state);
            SetHeroSpeed(sim, 3.0);
            var corpse = HeroAt(state, "h1", HeroClass.Sawbones, new Vec2(5.0, -2.0), alive: false);

            var result = sim.MoveHero(Step(corpse.Id));

            Assert.Multiple(() =>
            {
                Assert.That(corpse.Pos, Is.EqualTo(new Vec2(5.0, -2.0)),
                    "R-33: a dead hero spectates; it does not walk");
                Assert.That(result.DistanceMoved, Is.EqualTo(0.0).Within(Tolerance));
                Assert.That(sim.LastObservation.Result, Is.Not.Null,
                    "a refused step is still a command and still reports");
                Assert.That(sim.LastObservation.StateChanges, Is.Empty,
                    "nothing moved, so nothing may replicate");
            });
        }

        // ---- scenario helpers ----------------------------------------------------------------------------

        private const string TargetHotspot = "hotspot";

        private const string TargetHero = "hero";

        private const string TargetBarricade = "barricade";

        /// <summary>
        /// A match in combat with an empty world, wired to whichever config, clock and direction
        /// oracle the test wants. Built from production types directly: the golden loader is the
        /// adapter's contract with eval/golden, not a fixture builder.
        /// </summary>
        private static MatchSim MoveSim(
            out MatchState state,
            SimConfig config = null,
            SimClock clock = null,
            IDirectionOracle directions = null)
        {
            var tunables = config ?? new SimConfig();

            state = new MatchState();
            state.Phase = MatchPhase.Combat;
            state.Wave.Number = 1;

            var sim = new MatchSim(state, tunables, null, clock ?? new SimClock(0.0), null);
            if (directions != null)
            {
                sim.Directions = directions;
            }

            return sim;
        }

        /// <summary>
        /// The single place a hero's speed is configured. Ticket 018 put the number on
        /// <see cref="MatchSim.HeroMovement"/> because the PRD supplies none and the two obvious
        /// homes — <see cref="SimConfig"/> and <see cref="HeroKit"/> — were outside the ticket's
        /// file scope; if it later moves onto the kit catalog beside <see cref="HeroKit.MaxHp"/>,
        /// these two lines are the whole migration.
        /// </summary>
        private static void SetHeroSpeed(MatchSim sim, double speed) =>
            sim.HeroMovement.DefaultMoveSpeed = speed;

        private static void SetHeroSpeed(MatchSim sim, string heroClass, double speed) =>
            sim.HeroMovement.MoveSpeedByClass[heroClass] = speed;

        /// <summary>
        /// A monster standing somewhere at a speed the test chose. <see cref="Monster.BaseSpeed"/>
        /// and <see cref="Monster.CurrentSpeed"/> start equal, exactly as
        /// <see cref="MatchSim.SpawnWave"/> leaves them (R-31/G-018).
        /// </summary>
        private static Monster MonsterAt(
            MatchState state, string id, Vec2 pos, double speed, string targetId = null, bool alive = true)
        {
            var monster = new Monster
            {
                Id = id,
                Type = MonsterType.Shambler,
                Pos = pos,
                Hp = 60.0,
                Alive = alive,
                BaseSpeed = speed,
                CurrentSpeed = speed,
                TargetId = targetId,
            };

            state.Monsters[id] = monster;
            return monster;
        }

        private static Hero HeroAt(
            MatchState state, string id, string heroClass, Vec2 pos, bool alive = true)
        {
            var hero = new Hero
            {
                Id = id,
                HeroClass = heroClass,
                AccountId = "acct_" + id,
                Pos = pos,
                Hp = alive ? 100.0 : 0.0,
                MaxHp = 100.0,
                Alive = alive,
            };

            state.Heroes[id] = hero;
            return hero;
        }

        /// <summary>One second of walking east — the shape of every hero step these tests take.</summary>
        private static HeroMoveRequest Step(string heroId) => new HeroMoveRequest
        {
            HeroId = heroId,
            Direction = new Vec2(1.0, 0.0),
            DeltaSeconds = 1.0,
        };

        /// <summary>Puts one of R-16's three target kinds into the world and answers its id.</summary>
        private static string PlaceTarget(MatchState state, string kind, Vec2 pos)
        {
            switch (kind)
            {
                case TargetHotspot:
                    state.Hotspots[Shelter] = new Hotspot { Id = Shelter, Pos = pos, Civilians = 8 };
                    return Shelter;

                case TargetHero:
                    return HeroAt(state, "h_target", HeroClass.Gunslinger, pos).Id;

                case TargetBarricade:
                    state.Placeables["pl_wall"] = new Placeable
                    {
                        Id = "pl_wall",
                        Type = PlaceableType.Barricade,
                        Pos = pos,
                        OwnerPlayerId = "p1",
                        PurchaseCost = 100,
                        Hp = 300.0,
                        Exists = true,
                    };
                    return "pl_wall";

                default:
                    throw new ArgumentException("unknown target kind '" + kind + "'", nameof(kind));
            }
        }

        private static Exception Attempt(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        /// <summary>Holding position is fine, refusing is fine, throwing is fine; "the rule does not exist" is not.</summary>
        private static void AssertDefined(Exception thrown) =>
            Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                "the undefined case must have a decided behaviour, not an unimplemented one: " + thrown);

        /// <summary>
        /// A stand-in for the shell's NavMesh: it answers every query with one direction and records
        /// what it was asked, which is how these tests prove the sim consults the seam at all rather
        /// than quietly keeping a straight line of its own.
        /// </summary>
        private sealed class ScriptedDirectionOracle : IDirectionOracle
        {
            private readonly Vec2 _direction;

            public ScriptedDirectionOracle(Vec2 direction)
            {
                _direction = direction;
            }

            public readonly List<DirectionQuery> Queries = new List<DirectionQuery>();

            public Vec2 DirectionFor(string moverId, Vec2 from, Vec2 to)
            {
                Queries.Add(new DirectionQuery { MoverId = moverId, From = from, To = to });
                return _direction;
            }
        }

        private sealed class DirectionQuery
        {
            public string MoverId;
            public Vec2 From;
            public Vec2 To;
        }
    }
}
