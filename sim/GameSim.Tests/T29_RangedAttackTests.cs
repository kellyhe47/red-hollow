using NUnit.Framework;
using RedHollow.Sim;

namespace GameSim.Tests
{
    /// <summary>
    /// Ticket 029 — the R-17 attack-range column. The PRD's roster row for the Spitter reads
    /// "ranged acid, range 10", and nothing implemented it: <see cref="MonsterStats"/> carried no
    /// reach, movement walked every archetype into hugging distance, and the shell's contact
    /// source derived reach from the arrival clamp alone — so a Spitter played as a slow
    /// Shambler. The column is data (R-16: roster tunable in config), zero means melee, and the
    /// zero path is pinned BIT-IDENTICAL to what always shipped so no fixture or locked test can
    /// move.
    /// </summary>
    [TestFixture]
    public class T29_RangedAttackTests
    {
        private const double Tolerance = 1e-9;

        /// <summary>One host step at the usual fixed timestep.</summary>
        private const double Step = 1.0 / 60.0;

        // ==========================================================================================
        //  the roster ships the PRD's reaches
        // ==========================================================================================

        [Test]
        public void The_spitter_row_ships_the_prd_range_and_every_other_row_is_melee()
        {
            var catalog = new MonsterCatalog();

            Assert.That(catalog.StatsFor(MonsterType.Spitter).AttackRange, Is.EqualTo(10.0),
                "R-17: 'ranged acid, range 10' is the Spitter's PRD row");

            foreach (var melee in new[]
                     {
                         MonsterType.Shambler, MonsterType.Ravager, MonsterType.Burrower,
                         MonsterType.BullBehemoth,
                     })
            {
                Assert.That(catalog.StatsFor(melee).AttackRange, Is.EqualTo(0.0),
                    "R-17: the PRD names no reach for '" + melee + "' — zero keeps its arrival "
                    + "behaviour bit-identical to what shipped before the column existed");
            }
        }

        [Test]
        public void Spawn_copies_the_reach_onto_the_entity()
        {
            var match = NewMatch();
            match.Sim.WaveTable = TableOf(MonsterType.Spitter, 1);

            var spawned = match.Sim.SpawnWave(1);

            Assert.That(spawned.MonsterIds, Has.Count.EqualTo(1), "sanity: one spitter spawned");
            Assert.That(match.State.Monsters[spawned.MonsterIds[0]].AttackRange, Is.EqualTo(10.0),
                "R-17: the reach rides onto the entity at spawn like Hp and BaseSpeed do");
        }

        // ==========================================================================================
        //  movement holds a ranged monster at its reach — and melee exactly where it always stood
        // ==========================================================================================

        [Test]
        public void A_spitter_walks_to_its_reach_and_holds_there()
        {
            var match = NewMatch();
            var spitter = Seed(match.State, MonsterType.Spitter, new Vec2(0.0, 30.0), "hs_target");

            // Walk far longer than the 10 units of ground there is to cover (20 gap - 10 reach).
            for (var i = 0; i < 20 * 60; i++)
            {
                match.Sim.TickMonsterMovement(Step);
            }

            var gap = spitter.Pos.DistanceTo(match.State.Hotspots["hs_target"].Pos);
            Assert.That(gap, Is.EqualTo(10.0).Within(Tolerance),
                "R-17: the Spitter stops at exactly its acid range — the movement clamp lands it "
                + "ON the line, and holding is arrival's own rule");

            var held = spitter.Pos;
            match.Sim.TickMonsterMovement(Step);
            Assert.That(spitter.Pos, Is.EqualTo(held),
                "a monster at its reach is stable forever, exactly as an arrived melee one is");
        }

        [Test]
        public void A_melee_monster_still_arrives_exactly_on_its_target()
        {
            var match = NewMatch();
            var shambler = Seed(match.State, MonsterType.Shambler, new Vec2(0.0, 30.0), "hs_target");

            for (var i = 0; i < 30 * 60; i++)
            {
                match.Sim.TickMonsterMovement(Step);
            }

            Assert.That(shambler.Pos.DistanceTo(match.State.Hotspots["hs_target"].Pos),
                Is.EqualTo(0.0).Within(Tolerance),
                "R-18: melee reach is still the arrival clamp — range 0 must be bit-identical to "
                + "the pre-column behaviour every fixture pinned");
        }

        [Test]
        public void A_lassoed_spitter_is_slower_but_its_reach_does_not_move()
        {
            var match = NewMatch();
            var spitter = Seed(match.State, MonsterType.Spitter, new Vec2(0.0, 30.0), "hs_target");
            spitter.CurrentSpeed = spitter.BaseSpeed * 0.5; // DEC-008's slow, applied directly

            for (var i = 0; i < 40 * 60; i++)
            {
                match.Sim.TickMonsterMovement(Step);
            }

            Assert.That(spitter.Pos.DistanceTo(match.State.Hotspots["hs_target"].Pos),
                Is.EqualTo(10.0).Within(Tolerance),
                "R-31 slows the walk, never the weapon: a lassoed Spitter takes longer to reach "
                + "its line and stops on the same line");
        }

        [Test]
        public void A_spitter_closes_again_when_its_target_retreats()
        {
            var match = NewMatch();
            match.State.Heroes["h1"] = new Hero
            {
                Id = "h1",
                HeroClass = HeroClass.Gunslinger,
                AccountId = "acc_t29",
                Pos = new Vec2(0.0, 10.0),
                Hp = 100.0,
                MaxHp = 100.0,
                Alive = true,
            };
            var spitter = Seed(match.State, MonsterType.Spitter, new Vec2(0.0, 0.0), "h1");

            match.Sim.TickMonsterMovement(Step);
            var held = spitter.Pos;
            Assert.That(held, Is.EqualTo(new Vec2(0.0, 0.0)),
                "sanity: already on its line (gap 10), so it holds");

            // The hero backs off; the gap opens to 14 and the Spitter closes it back to 10.
            match.State.Heroes["h1"].Pos = new Vec2(0.0, 14.0);
            for (var i = 0; i < 5 * 60; i++)
            {
                match.Sim.TickMonsterMovement(Step);
            }

            Assert.That(spitter.Pos.DistanceTo(match.State.Heroes["h1"].Pos),
                Is.EqualTo(10.0).Within(Tolerance),
                "R-17: the reach is a pursuit distance, not a leash — a kiting hero is followed "
                + "at acid range");
        }

        // ==========================================================================================
        //  scenario builders
        // ==========================================================================================

        private sealed class Match
        {
            public MatchState State;
            public MatchSim Sim;
        }

        private static Match NewMatch()
        {
            var state = new MatchState
            {
                Phase = MatchPhase.Combat,
                Status = MatchStatus.InProgress,
            };
            state.Hotspots["hs_target"] = new Hotspot
            {
                Id = "hs_target",
                Pos = new Vec2(0.0, 0.0),
                Civilians = 5,
            };

            return new Match
            {
                State = state,
                Sim = new MatchSim(state, new SimConfig(), null, new SimClock(), null),
            };
        }

        /// <summary>One monster of the archetype, reach copied off the shipped catalog row.</summary>
        private static Monster Seed(MatchState state, string type, Vec2 pos, string targetId)
        {
            var stats = new SimConfig().Monsters.StatsFor(type);
            var monster = new Monster
            {
                Id = "m_" + type,
                Type = type,
                Pos = pos,
                Hp = stats.MaxHp,
                BaseSpeed = stats.MoveSpeed,
                CurrentSpeed = stats.MoveSpeed,
                AttackRange = stats.AttackRange,
                Alive = true,
                TargetId = targetId,
            };

            state.Monsters[monster.Id] = monster;
            return monster;
        }

        /// <summary>A one-wave table sending exactly <paramref name="count"/> of one archetype.</summary>
        private static WaveTable TableOf(string type, int count)
        {
            var table = new WaveTable();
            var spec = new WaveSpec { Number = 1 };
            spec.Groups.Add(new MonsterGroup { MonsterType = type, Count = count });
            spec.ActiveTunnels.Add(0);
            table.Waves.Add(spec);
            return table;
        }
    }
}
