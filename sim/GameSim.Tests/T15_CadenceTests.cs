using System;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Ticket 015 (T-15): the monster attack cadence half of R-18 — "monsters attack once per
    /// second". No fixture grades it, which is why the gap survived to a requirement walk:
    /// <see cref="SimConfig.MonsterAttackIntervalSeconds"/> has been declared since ticket 001 and
    /// nothing in the sim reads it, so nothing rate-limits how often a monster lands a hit. The
    /// host's combat loop drives <see cref="MatchSim.ApplyHotspotAttack"/>,
    /// <see cref="MatchSim.ApplyHeroDamage"/> and <see cref="MatchSim.ApplyPlaceableDamage"/> off
    /// its own tick, and at 60fps that is 60x the intended damage — wave 1 would empty the colony
    /// in its first second.
    ///
    /// R-18's other half (NavMesh movement; the Burrower path ignoring barricade obstacles) is not
    /// tested here: the pathing is Unity shell work, and the Burrower carve-out is already green in
    /// ticket 002 at the targeting level (G-005).
    ///
    /// <b>The seam these tests pin, and why it is a separate operation.</b>
    /// <see cref="MatchSim.TryMonsterAttack"/> is asked *before* a damage operation is called; the
    /// damage operations themselves are untouched. Six golden fixtures — G-006/007/008/009 on
    /// apply_hotspot_attack and G-020/021 on apply_hero_damage — call a damage entry point directly
    /// with no prior attack and no cadence state, and pin an exact `result`, `state_changes` and
    /// `emitted_events` for what is that monster's first hit. A gate folded into those operations
    /// would have to either refuse a first attack or record a cadence stamp as a delta, and either
    /// breaks all six. So the two properties that keep the acceptance contract intact are asserted
    /// here as first-class tests: a monster that has never attacked is permitted immediately, at
    /// any clock reading including 0
    /// (<see cref="A_monster_that_has_never_attacked_may_attack_immediately"/>), and a permitted
    /// attack adds nothing observable to the damage operation that follows it
    /// (<see cref="A_permitted_attack_adds_nothing_observable_to_the_damage_operation"/>).
    ///
    /// Where R-18 is silent — what the gate answers for a monster that is dead, unknown or unnamed
    /// — these tests assert shape and non-corruption only, never a guessed answer or error string.
    /// The repo's sad-path conventions are deliberately inconsistent (apply_hotspot_attack throws
    /// KeyNotFoundException, apply_hero_damage throws ArgumentException, trigger_placeable refuses),
    /// so pinning one of them here would ship this ticket's guess as spec.
    ///
    /// Scenarios are built from production types directly rather than through the fixture JSON
    /// loader: the loader is the adapter's contract with eval/golden, not a test fixture builder.
    /// </summary>
    [TestFixture]
    public class T15_CadenceTests
    {
        private const double Tolerance = 1e-9;

        /// <summary>
        /// One frame of a 64fps host loop. Chosen over 1/60 because 1/64 is exactly representable
        /// as a double: 64 of these advance the clock to exactly 1.0, so the inclusive-boundary
        /// frame is a real boundary rather than an accumulated-rounding near-miss.
        /// </summary>
        private const double FrameSeconds = 1.0 / 64.0;

        // ---- R-18: a monster that has never attacked is not gated ----------------------------------

        /// <summary>
        /// The fixture-safety property, stated first because everything else in this file depends on
        /// it: a monster with no cadence history must be able to attack the instant it is asked.
        ///
        /// G-006 through G-009, G-020 and G-021 all run at clock 0 (`"clock": {}`) and all call a
        /// damage operation for a monster's first hit of the scenario. An implementation that
        /// initialises "last attacked at" to 0 and then asks `now &lt; last + interval` refuses at
        /// t=0 — which is precisely the shape that would turn six green fixtures red. t=0 is
        /// therefore a case in its own right, not a rounding of "early in the match".
        /// </summary>
        [TestCase(0.0)]
        [TestCase(0.5)]
        [TestCase(250.0)]
        public void A_monster_that_has_never_attacked_may_attack_immediately(double now)
        {
            var sim = SimWith(out var state, new SimConfig(), new SimClock(now));
            AddMonster(state, "m1", MonsterType.Shambler, new Vec2(0.0, 0.0), hp: 60.0);

            Assert.That(sim.TryMonsterAttack("m1"), Is.True,
                "a monster that has never attacked is not on cooldown, whatever the clock reads; at "
                + now + " it must be allowed to swing — G-006..G-009 and G-020/G-021 are exactly "
                + "this arrangement and they are the acceptance contract");
        }

        // ---- R-18: the rate limit itself -----------------------------------------------------------

        /// <summary>
        /// R-18's whole sentence, driven the way the bug actually reaches production: a host loop
        /// that asks on every frame. One second of sim time at 64fps is 65 opportunities to swing
        /// (t=0 through t=1.0 inclusive) and R-18 allows exactly two of them — the opening hit and
        /// the one at exactly one second later.
        ///
        /// The damage operation is driven through the gate rather than counted in the abstract,
        /// because the gap this ticket closes is a damage gap: an ungated loop lands 65 ten-damage
        /// hits on an 8-civilian shelter, empties it, and loses the match inside wave 1's first
        /// second (R-02/R-13). The civilian count is the assertion that says so.
        /// </summary>
        [Test]
        public void A_host_loop_asking_every_frame_lands_one_hit_per_configured_second()
        {
            var clock = new SimClock(0.0);
            var sim = SimWith(out var state, new SimConfig(), clock);
            AddMonster(state, "m1", MonsterType.Shambler, new Vec2(0.0, 0.0), hp: 60.0);
            var saloon = AddHotspot(state, "hs_saloon", new Vec2(0.0, 1.0), civilians: 8);

            var permitted = 0;
            for (var frame = 0; frame <= 64; frame++)
            {
                if (frame > 0)
                {
                    clock.Advance(FrameSeconds);
                }

                if (sim.TryMonsterAttack("m1"))
                {
                    permitted++;
                    sim.ApplyHotspotAttack(Attack("m1", MonsterType.Shambler, 10.0, "hs_saloon"));
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(permitted, Is.EqualTo(2),
                    "R-18: over one second of sim time a monster attacks at t=0 and again at exactly "
                    + "t=1.0 — no more. An ungated loop would have landed 65 hits");
                Assert.That(saloon.Civilians, Is.EqualTo(6),
                    "two 10-damage hits kill two civilians (R-11). An ungated loop empties an "
                    + "8-civilian shelter in the first second of wave 1");
                Assert.That(state.IsOver, Is.False,
                    "the colony must still be standing one second into wave 1");
            });
        }

        /// <summary>
        /// The interval is a config value, and the deadline is inclusive.
        ///
        /// Both intervals are deliberately something other than R-18's one second, in both
        /// directions: at 0.25s a sim with a hardcoded second refuses an attack it should permit,
        /// and at 2.5s it permits one it should refuse. A constant cannot pass all six cases.
        ///
        /// Inclusive at exactly last + interval, pinned to this repo's boundary convention rather
        /// than invented here: G-019 expires a status effect at exactly its `expires_at` and names
        /// "expiry compared with strict greater-than drift" as the bug it defends against, and
        /// tickets 004, 007 and 008 all follow it. Both intervals are dyadic, so `last + interval`
        /// is exact and the boundary case is a genuine boundary.
        /// </summary>
        [TestCase(0.25, -0.001, false)]
        [TestCase(0.25, 0.0, true)]
        [TestCase(0.25, 0.001, true)]
        [TestCase(2.5, -0.001, false)]
        [TestCase(2.5, 0.0, true)]
        [TestCase(2.5, 0.001, true)]
        public void The_next_attack_is_permitted_at_exactly_the_configured_interval(
            double interval, double offset, bool expectedPermitted)
        {
            var config = new SimConfig { MonsterAttackIntervalSeconds = interval };
            var clock = new SimClock(100.0);
            var sim = SimWith(out var state, config, clock);
            AddMonster(state, "m1", MonsterType.Shambler, new Vec2(0.0, 0.0), hp: 60.0);

            Assert.That(sim.TryMonsterAttack("m1"), Is.True, "the opening attack is never gated");

            clock.Advance(interval + offset);

            Assert.That(sim.TryMonsterAttack("m1"), Is.EqualTo(expectedPermitted),
                "with MonsterAttackIntervalSeconds = " + interval + ", an attack at last+interval"
                + (offset < 0 ? offset.ToString() : "+" + offset) + " should be "
                + (expectedPermitted ? "permitted" : "refused"));
        }

        // ---- R-18: the cadence is per monster ------------------------------------------------------

        /// <summary>
        /// One monster's cooldown must never gate another's, the way R-32's cooldowns are per hero
        /// and per slot. A single shared timer would let one Shambler's swing silence a whole wave.
        ///
        /// The two are deliberately offset by half an interval and then walked forward together, so
        /// the test fails both ways a shared timer can fail: a single timer would refuse `b` at
        /// t=100.5 (it was `a` that just swung) and permit it at t=101.0 (it was `a`'s deadline,
        /// not `b`'s). Every instant here is dyadic, so the two boundary reads at 101.0 and 101.5
        /// are exact.
        /// </summary>
        [Test]
        public void Cadence_is_per_monster_and_two_monsters_interleave_at_their_own_pace()
        {
            var clock = new SimClock(100.0);
            var sim = SimWith(out var state, new SimConfig(), clock);
            AddMonster(state, "m_a", MonsterType.Shambler, new Vec2(0.0, 0.0), hp: 60.0);
            AddMonster(state, "m_b", MonsterType.Ravager, new Vec2(1.0, 0.0), hp: 60.0);

            var aOpening = sim.TryMonsterAttack("m_a");

            clock.Advance(0.5);
            var aTooSoon = sim.TryMonsterAttack("m_a");
            var bOpening = sim.TryMonsterAttack("m_b");

            clock.Advance(0.5);
            var aAtDeadline = sim.TryMonsterAttack("m_a");
            var bTooSoon = sim.TryMonsterAttack("m_b");

            clock.Advance(0.5);
            var bAtDeadline = sim.TryMonsterAttack("m_b");

            Assert.Multiple(() =>
            {
                Assert.That(aOpening, Is.True, "t=100.0: m_a has never attacked");
                Assert.That(aTooSoon, Is.False, "t=100.5: half a second after m_a's own swing");
                Assert.That(bOpening, Is.True,
                    "t=100.5: m_b has never attacked — m_a's swing must not put m_b on cooldown");
                Assert.That(aAtDeadline, Is.True, "t=101.0: exactly one second after m_a's swing");
                Assert.That(bTooSoon, Is.False,
                    "t=101.0: only half a second after m_b's own swing — m_a reaching its deadline "
                    + "must not release m_b");
                Assert.That(bAtDeadline, Is.True, "t=101.5: exactly one second after m_b's swing");
            });
        }

        // ---- the acceptance contract: the 30 fixtures must not move --------------------------------

        /// <summary>
        /// G-006's arrangement, rebuilt from production types and run through the gate first: the
        /// gate must add nothing the fixture would see.
        ///
        /// This is the test that fails if the cadence check is folded into
        /// <see cref="MatchSim.ApplyHotspotAttack"/> instead of living beside it. G-006 pins the
        /// operation's whole observation — one `result`, exactly one state change
        /// (`hs_saloon.civilians 8 -> 7`), exactly one event (`civilians_killed`), no external
        /// calls — so a cadence stamp recorded as a delta, or a refusal that swallowed the hit,
        /// shows up here as an extra row or a missing one. G-007/G-008/G-009 share the operation
        /// and G-020/G-021 share the pattern on apply_hero_damage.
        /// </summary>
        [Test]
        public void A_permitted_attack_adds_nothing_observable_to_the_damage_operation()
        {
            var sim = SimWith(out var state, new SimConfig(), new SimClock(0.0));
            AddMonster(state, "m1", MonsterType.Shambler, new Vec2(0.0, 0.0), hp: 60.0);
            var saloon = AddHotspot(state, "hs_saloon", new Vec2(0.0, 1.0), civilians: 8);
            AddHotspot(state, "hs_chapel", new Vec2(0.0, 5.0), civilians: 6);

            Assert.That(sim.TryMonsterAttack("m1"), Is.True,
                "G-006 is a first hit at clock 0; the gate must permit it");

            var result = sim.ApplyHotspotAttack(Attack("m1", MonsterType.Shambler, 10.0, "hs_saloon"));

            Assert.Multiple(() =>
            {
                Assert.That(result.HotspotId, Is.EqualTo("hs_saloon"));
                Assert.That(result.CiviliansKilled, Is.EqualTo(1), "G-006 result");
                Assert.That(result.CiviliansRemaining, Is.EqualTo(7), "G-006 result");
                Assert.That(result.TotalCiviliansRemaining, Is.EqualTo(13), "G-006 result");
                Assert.That(saloon.Civilians, Is.EqualTo(7));

                Assert.That(sim.LastObservation.StateChanges.Count, Is.EqualTo(1),
                    "G-006 pins exactly one state change; the gate must not add a cadence delta to "
                    + "the damage operation's observation");
                Assert.That(sim.LastObservation.StateChanges[0].Entity, Is.EqualTo("hs_saloon"));
                Assert.That(sim.LastObservation.StateChanges[0].Field, Is.EqualTo("civilians"));

                Assert.That(sim.LastObservation.EmittedEvents.Count, Is.EqualTo(1),
                    "G-006 pins exactly one emitted event; the gate must not announce itself here");
                Assert.That(sim.LastObservation.EmittedEvents[0].Type, Is.EqualTo("civilians_killed"));

                Assert.That(sim.LastObservation.ExternalCalls, Is.Empty, "G-006 pins no external calls");
            });
        }

        // ---- sad paths: shape and non-corruption only ----------------------------------------------

        /// <summary>
        /// A gate query for a monster the sim cannot act on: one that was never in the world, one
        /// that is already a corpse, and an unnamed one. What the gate *answers* for these is
        /// genuinely open — R-18 says nothing about them, and this repo answers such questions three
        /// different ways already (KeyNotFoundException, ArgumentException, a refusal) — so this
        /// test invents no answer. It pins the two things that are not open: the query must be
        /// defined rather than a stub, and it must corrupt nothing.
        ///
        /// "Corrupt nothing" includes the one failure mode that would be easy to ship: a bad id
        /// must not poison the cadence bookkeeping of a real monster standing beside it, so the
        /// living monster's own opening attack is still permitted afterwards.
        /// </summary>
        [TestCase("m_ghost")]
        [TestCase("m_dead")]
        [TestCase(null)]
        public void A_gate_query_for_a_missing_or_dead_monster_is_defined_and_corrupts_nothing(
            string monsterId)
        {
            var sim = SimWith(out var state, new SimConfig(), new SimClock(50.0));
            var living = AddMonster(state, "m_live", MonsterType.Ravager, new Vec2(0.0, 0.0), hp: 80.0);
            var corpse = AddMonster(state, "m_dead", MonsterType.Shambler, new Vec2(1.0, 0.0), hp: 0.0);
            corpse.Alive = false;
            var saloon = AddHotspot(state, "hs_saloon", new Vec2(0.0, 1.0), civilians: 8);

            Attempt(() => sim.TryMonsterAttack(monsterId), out var thrown);

            Assert.Multiple(() =>
            {
                Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                    "the cadence gate is still a stub, so '" + (monsterId ?? "null")
                    + "' has no defined behaviour yet");

                Assert.That(state.Monsters.Count, Is.EqualTo(2),
                    "asking about an id must never add a monster to the world");
                Assert.That(corpse.Alive, Is.False, "a cadence query must not resurrect anybody");
                Assert.That(corpse.Hp, Is.EqualTo(0.0).Within(Tolerance));
                Assert.That(living.Hp, Is.EqualTo(80.0).Within(Tolerance),
                    "a permission question deals no damage");
                Assert.That(saloon.Civilians, Is.EqualTo(8),
                    "a permission question must not land a hit of its own");

                Assert.That(sim.TryMonsterAttack("m_live"), Is.True,
                    "a query for '" + (monsterId ?? "null") + "' must not put an unrelated living "
                    + "monster on cooldown");
            });
        }

        // ---- scenario builders -----------------------------------------------------------------------

        private static MatchSim SimWith(out MatchState state, SimConfig config, IClock clock)
        {
            state = new MatchState();
            return new MatchSim(state, config, profileStore: null, clock: clock, pathOracle: null);
        }

        private static Monster AddMonster(MatchState state, string id, string type, Vec2 pos, double hp)
        {
            var monster = new Monster
            {
                Id = id,
                Type = type,
                Pos = pos,
                Hp = hp,
                Alive = true,
                BaseSpeed = 2.0,
                CurrentSpeed = 2.0,
            };

            state.Monsters[id] = monster;
            return monster;
        }

        private static Hotspot AddHotspot(MatchState state, string id, Vec2 pos, int civilians)
        {
            var hotspot = new Hotspot { Id = id, Pos = pos, Civilians = civilians };
            state.Hotspots[id] = hotspot;
            return hotspot;
        }

        private static HotspotAttackRequest Attack(
            string attackerId, string attackerType, double damage, string targetId)
        {
            return new HotspotAttackRequest
            {
                AttackerId = attackerId,
                AttackerType = attackerType,
                Damage = damage,
                TargetId = targetId,
            };
        }

        /// <summary>
        /// Runs a gate query that may legitimately throw, capturing the exception instead of
        /// failing. Used only where R-18 leaves the sad path open; the NotImplementedException
        /// guard at the call site is what keeps that test red until T-15 lands.
        /// </summary>
        private static bool? Attempt(Func<bool> query, out Exception thrown)
        {
            thrown = null;
            try
            {
                return query();
            }
            catch (Exception ex)
            {
                thrown = ex;
                return null;
            }
        }
    }
}
