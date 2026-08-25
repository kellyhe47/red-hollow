using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Ticket 006 (T-06): placeable combat effects — R-23, plus the R-16 half of R-23 that says a
    /// barricade blocks "until destroyed".
    ///
    /// G-027 (spike trap final trigger), G-028 (turret nearest-in-range) and G-029 (dynamite AoE)
    /// are already turned into cases by the locked golden adapter, so nothing here re-encodes them.
    /// What lives here is everything those three arrangements cannot see:
    ///
    ///   * <b>Barricade destruction.</b> R-23 gives a barricade 300 HP and R-16 makes it a
    ///     monster's target "until destroyed" — but nothing in the sim damages a placeable, so
    ///     today a barricade is immortal and a 100-scrip wall blocks a lane for the whole match.
    ///     G-004/G-005 only grade target *selection*, so no fixture can catch it. These tests
    ///     drive the destruction through the real <see cref="MatchSim.SelectTarget"/> so the
    ///     re-target is pinned as integration rather than as a private flag.
    ///   * <b>Med Station.</b> R-23 gives it 5 HP/s in radius 5 and R-35 says it stacks with
    ///     out-of-combat regen. Only the <see cref="PlaceableType.MedStation"/> string exists
    ///     today; nothing heals anybody.
    ///   * The <i>rules</i> behind the three fixtures: a trap's whole countdown rather than its
    ///     last step, the turret's range boundary / tie / empty-sky cases, the blast-radius
    ///     boundary, and R-26's no-friendly-fire promise applied to a placeable's own damage.
    ///
    /// Where the PRD is silent — the turret's tiebreak rule, whether non-barricade placeables are
    /// damageable, the arithmetic of "Med Station stacks", the error shape of a bad command — these
    /// tests assert direction, bound and non-corruption, never a guessed number or string. An
    /// over-pinned test would reject a correct implementation and ship this ticket's guesses as spec.
    ///
    /// Scenarios are built from production types directly rather than through the fixture JSON
    /// loader: the loader is the adapter's contract with eval/golden, not a test fixture builder.
    /// </summary>
    [TestFixture]
    public class T06_PlaceableTests
    {
        private const double Tolerance = 1e-9;

        // ---- R-23: the effect columns are configuration (DEC-RUN-1) --------------------------------

        /// <summary>
        /// The R-23 effect columns, verbatim from the PRD. Ticket 005 filled in the cost column and
        /// deliberately left these at their defaults, so a sim that hardcodes 300/30/10/20/8/5/5 in
        /// rule code would run correctly while the catalog still reported zeros — and the shell,
        /// which rebalances through <see cref="SimConfig.Placeables"/> and never through code, would
        /// have nothing to turn.
        ///
        /// Asserted on the catalog rather than through the sim for the same reason
        /// <see cref="T02TargetingTests.Configured_roster_matches_the_R17_table"/> is: the criterion
        /// is about where the numbers live.
        ///
        /// Dynamite's blast radius is the one number R-23 does NOT give — the PRD row says only
        /// "150 dmg AoE, single use", and 3.0 appears solely as an input inside G-029. It is
        /// therefore pinned as "a real radius exists", not as a value this test invented.
        /// </summary>
        [Test]
        public void Configured_placeable_effects_match_the_R23_table()
        {
            var catalog = new SimConfig().Placeables;

            Assert.Multiple(() =>
            {
                Assert.That(catalog.StatsFor(PlaceableType.Barricade).MaxHp, Is.EqualTo(300.0).Within(Tolerance),
                    "R-23: barricade is a 300 HP wall");

                Assert.That(catalog.StatsFor(PlaceableType.SpikeTrap).Damage, Is.EqualTo(30.0).Within(Tolerance),
                    "R-23: spike trap deals 30 dmg per monster crossing");
                Assert.That(catalog.StatsFor(PlaceableType.SpikeTrap).TriggerCount, Is.EqualTo(10),
                    "R-23: spike trap survives 10 triggers then breaks");

                Assert.That(catalog.StatsFor(PlaceableType.DynamiteTrap).Damage, Is.EqualTo(150.0).Within(Tolerance),
                    "R-23: dynamite deals 150 AoE damage");
                Assert.That(catalog.StatsFor(PlaceableType.DynamiteTrap).BlastRadius, Is.GreaterThan(0.0),
                    "R-23 gives dynamite no radius number, only that its damage is AoE — but a blast "
                    + "with radius 0 hits nobody but the monster standing on it");
                Assert.That(catalog.StatsFor(PlaceableType.DynamiteTrap).TriggerCount, Is.EqualTo(1),
                    "R-23: dynamite is single use");

                Assert.That(catalog.StatsFor(PlaceableType.Turret).Damage, Is.EqualTo(20.0).Within(Tolerance),
                    "R-23: turret does 20 DPS");
                Assert.That(catalog.StatsFor(PlaceableType.Turret).Range, Is.EqualTo(8.0).Within(Tolerance),
                    "R-23: turret range 8");

                Assert.That(catalog.StatsFor(PlaceableType.MedStation).HealPerSecond, Is.EqualTo(5.0).Within(Tolerance),
                    "R-23: med station heals 5 HP/s");
                Assert.That(catalog.StatsFor(PlaceableType.MedStation).Range, Is.EqualTo(5.0).Within(Tolerance),
                    "R-23: med station radius 5");
            });
        }

        /// <summary>
        /// R-23: "numeric stats config-tunable". Ticket 005 pinned that for the cost column; the
        /// effect columns need the same guarantee, because they are the ones a balance pass actually
        /// moves. A retune must stay on the config it was made against and must not leak into any
        /// other match's catalog (DEC-RUN-1: fresh rows per instance, never shared static state).
        /// </summary>
        [Test]
        public void Placeable_effect_numbers_are_overridable_per_config_instance()
        {
            var tuned = new SimConfig();
            tuned.Placeables.Set(PlaceableType.Barricade, new PlaceableStats { Cost = 100, MaxHp = 42.0 });

            Assert.Multiple(() =>
            {
                Assert.That(tuned.Placeables.StatsFor(PlaceableType.Barricade).MaxHp,
                    Is.EqualTo(42.0).Within(Tolerance), "a tuned barricade HP must stay tuned");
                Assert.That(new SimConfig().Placeables.StatsFor(PlaceableType.Barricade).MaxHp,
                    Is.EqualTo(300.0).Within(Tolerance),
                    "one config's override leaked into another; the catalog is shared static state, not config");
            });
        }

        // ---- R-23 / R-16: a barricade takes damage and is destroyed at 0 HP ------------------------

        /// <summary>
        /// The first half of the hole: a barricade must be able to lose HP at all. The delta is
        /// replicated the way every other HP delta in this sim is (`hp`, from -> to), because the
        /// host is authoritative and every client draws that wall's health bar from this stream.
        /// </summary>
        [Test]
        public void Barricade_takes_damage_and_replicates_the_hp_delta()
        {
            var sim = SimWith(out var state);
            var barricade = AddBarricade(state, "bar1", new Vec2(4.0, 0.0), hp: 300.0);

            var result = sim.ApplyPlaceableDamage(Hit("m1", MonsterType.Shambler, 120.0, "bar1"));

            var change = ChangeFor(sim, "bar1", "hp");

            Assert.Multiple(() =>
            {
                Assert.That(barricade.Hp, Is.EqualTo(180.0).Within(Tolerance), "300 HP wall hit for 120");
                Assert.That(barricade.Exists, Is.True, "a wall above 0 HP is still standing");
                Assert.That(result.PlaceableId, Is.EqualTo("bar1"));
                Assert.That(result.DamageTaken, Is.EqualTo(120.0).Within(Tolerance));
                Assert.That(result.HpAfter, Is.EqualTo(180.0).Within(Tolerance),
                    "the result must carry the same HP the entity does");
                Assert.That(result.Destroyed, Is.False);

                Assert.That(change, Is.Not.Null, "the wall's HP change must replicate to clients");
                Assert.That(Convert.ToDouble(change.From), Is.EqualTo(300.0).Within(Tolerance));
                Assert.That(Convert.ToDouble(change.To), Is.EqualTo(180.0).Within(Tolerance));
            });
        }

        /// <summary>
        /// The second half: 0 HP removes the wall from the world. HP floors at 0 rather than going
        /// negative — a negative wall would render as a negative health bar and, worse, any rule
        /// written as `Hp != 0` would keep treating it as standing.
        ///
        /// Three starting HP values, only one of them the catalog's 300, so an implementation that
        /// destroys at a hardcoded threshold instead of at the entity's own remaining HP cannot pass
        /// all three. The overkill case is the one that pins the floor.
        /// </summary>
        [TestCase(300.0, 300.0)]
        [TestCase(300.0, 475.0)]
        [TestCase(120.0, 999.0)]
        public void Barricade_is_destroyed_at_zero_hp_and_never_goes_negative(double startHp, double damage)
        {
            var sim = SimWith(out var state);
            var barricade = AddBarricade(state, "bar1", new Vec2(4.0, 0.0), startHp);

            var result = sim.ApplyPlaceableDamage(Hit("m1", MonsterType.Ravager, damage, "bar1"));

            var existsChange = ChangeFor(sim, "bar1", "exists");

            Assert.Multiple(() =>
            {
                Assert.That(barricade.Hp, Is.EqualTo(0.0).Within(Tolerance),
                    "a destroyed wall floors at 0 HP; " + damage + " into " + startHp + " must not go negative");
                Assert.That(barricade.Exists, Is.False, "R-16: the block is released when the wall dies");
                Assert.That(result.HpAfter, Is.EqualTo(0.0).Within(Tolerance));
                Assert.That(result.Destroyed, Is.True);

                Assert.That(existsChange, Is.Not.Null,
                    "a placeable leaving the world replicates as an `exists` delta (G-027/G-029 shape)");
                Assert.That(existsChange.To, Is.EqualTo(false));

                Assert.That(AnnouncesRemovalOf(sim, "bar1"), Is.True,
                    "destroying a placeable must emit an event naming it, the way a broken trap does");
            });
        }

        /// <summary>
        /// The point of the whole criterion, and the reason it is graded through the real targeting
        /// operation rather than through a flag: R-16/B-002 makes a barricade the monster's target
        /// "until destroyed", and ticket 002 already honours <see cref="Placeable.Exists"/> when it
        /// decides whether a declared blocker still redirects. Nothing tests the transition, because
        /// until this ticket nothing could produce it.
        ///
        /// Built on the real v1 colony map (R-10) with a real <see cref="DeclaredPathOracle"/>: the
        /// monster starts west of the Saloon with the wall between them, chews through the wall, and
        /// must then walk on to the shelter. A sim where the wall is immortal fails at the second
        /// selection — which is the live game-breaking bug this test exists to close.
        /// </summary>
        [Test]
        public void A_destroyed_barricade_stops_blocking_and_the_monster_retargets()
        {
            var config = new SimConfig();
            var state = ColonyMap.V1().CreateMatchState(config);

            var monster = AddMonster(state, "m1", MonsterType.Shambler, new Vec2(-20.0, 6.0), hp: 60.0);
            var barricade = AddBarricade(state, "bar1", new Vec2(-16.0, 6.0), hp: 300.0);

            var oracle = new DeclaredPathOracle();
            oracle.Declare(monster.Id, "hs_saloon", barricade.Id);
            var sim = new MatchSim(state, config, profileStore: null, clock: new SimClock(0.0), pathOracle: oracle);

            var blocked = sim.SelectTarget(monster.Id);
            Assert.That(blocked.TargetId, Is.EqualTo("bar1"),
                "B-002: a barricade in the way is the target while it stands");

            sim.ApplyPlaceableDamage(Hit(monster.Id, MonsterType.Shambler, 200.0, barricade.Id));
            Assert.That(sim.SelectTarget(monster.Id).TargetId, Is.EqualTo("bar1"),
                "a wall on 100 HP is still standing and still the target");

            sim.ApplyPlaceableDamage(Hit(monster.Id, MonsterType.Shambler, 100.0, barricade.Id));

            var afterBreak = sim.SelectTarget(monster.Id);

            Assert.Multiple(() =>
            {
                Assert.That(barricade.Exists, Is.False, "300 HP of damage destroys a 300 HP wall");
                Assert.That(afterBreak.TargetId, Is.EqualTo("hs_saloon"),
                    "R-16 'until destroyed': a dead wall must stop redirecting the monster, or a "
                    + "100-scrip barricade blocks its lane for the rest of the match");
                Assert.That(afterBreak.Distance, Is.EqualTo(8.0).Within(Tolerance),
                    "the reported distance is now the shelter's, not the rubble's");
                Assert.That(monster.TargetId, Is.EqualTo("hs_saloon"));
            });
        }

        /// <summary>
        /// A wall that is already rubble absorbs nothing. Whether the second hit refuses or throws
        /// is open — this sim does both elsewhere (<see cref="MatchSim.ApplyHotspotAttack"/> throws
        /// on an unknown shelter, <see cref="MatchSim.SelectTarget"/> answers emptily) — so only the
        /// effect is pinned: no HP moves, no second destruction is announced, and the hit is
        /// credited with nothing.
        /// </summary>
        [Test]
        public void Damage_to_a_destroyed_barricade_does_nothing()
        {
            var sim = SimWith(out var state);
            var barricade = AddBarricade(state, "bar1", new Vec2(4.0, 0.0), hp: 300.0);

            sim.ApplyPlaceableDamage(Hit("m1", MonsterType.Ravager, 400.0, "bar1"));
            Assert.That(barricade.Exists, Is.False, "the first hit is the ordinary destruction");

            var second = Attempt(
                () => sim.ApplyPlaceableDamage(Hit("m2", MonsterType.Ravager, 50.0, "bar1")), out var thrown);

            Assert.Multiple(() =>
            {
                Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                    "apply_placeable_damage is still a stub, so hitting rubble has no defined behaviour yet");
                Assert.That(barricade.Hp, Is.EqualTo(0.0).Within(Tolerance), "rubble does not take damage");
                Assert.That(barricade.Exists, Is.False);
                Assert.That(AnnouncesRemovalOf(sim, "bar1"), Is.False,
                    "a wall must not be destroyed twice — clients would play the collapse twice");

                if (thrown == null)
                {
                    Assert.That(second.DamageTaken, Is.EqualTo(0.0).Within(Tolerance),
                        "no damage is credited against a wall that is already gone");
                    Assert.That(second.HpAfter, Is.EqualTo(0.0).Within(Tolerance));
                }
            });
        }

        /// <summary>
        /// R-23 gives HP to the Barricade row and to no other, so whether a turret or a med station
        /// can be shot down is genuinely unspecified — this test invents no rule for it. It pins
        /// only that the command is *defined* for one: a monster that wanders into a turret must not
        /// crash the host or leave a placeable in an incoherent state (negative HP, or a result that
        /// disagrees with the entity it names).
        /// </summary>
        [Test]
        public void Damaging_a_non_barricade_placeable_is_defined_and_leaves_the_world_coherent()
        {
            var sim = SimWith(out var state);
            var turret = AddTurret(state, "t1", new Vec2(0.0, 0.0), damage: 20.0, range: 8.0);
            var bystander = AddMonster(state, "m1", MonsterType.Shambler, new Vec2(1.0, 0.0), hp: 60.0);

            var result = Attempt(
                () => sim.ApplyPlaceableDamage(Hit("m1", MonsterType.Shambler, 40.0, "t1")), out var thrown);

            Assert.Multiple(() =>
            {
                Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                    "apply_placeable_damage is still a stub");
                Assert.That(turret.Hp, Is.GreaterThanOrEqualTo(0.0), "no placeable may hold negative HP");
                Assert.That(bystander.Hp, Is.EqualTo(60.0).Within(Tolerance),
                    "hitting a placeable must not touch anything else");

                if (thrown == null)
                {
                    Assert.That(result.PlaceableId, Is.EqualTo("t1"), "the result names the placeable it was aimed at");
                    Assert.That(result.HpAfter, Is.EqualTo(turret.Hp).Within(Tolerance),
                        "the result must carry the same HP the entity does");
                }
            });
        }

        // ---- R-23 / R-35: the Med Station ----------------------------------------------------------

        /// <summary>
        /// R-23's one sentence: "heals heroes 5 HP/s in radius 5". Radius is the whole rule that
        /// makes a med station a *placement* decision rather than a team-wide buff, and no fixture
        /// covers it. Two identical heroes, one inside and one outside, one tick.
        ///
        /// The exact healed amount is deliberately not asserted: whether healing accrues
        /// continuously or in whole seconds is left open, exactly as ticket 007 left it for regen.
        /// Only "healed at all", "no more than rate x elapsed" and "not healed" are pinned.
        /// </summary>
        [Test]
        public void Med_station_heals_heroes_inside_its_radius_only()
        {
            const double Rate = 4.0;
            const double Radius = 5.0;
            const double Now = 10.0;

            var config = MedStationConfig(Rate, Radius);
            var sim = SimWith(out var state, config, new SimClock(Now));
            AddMedStation(state, config, "med1", new Vec2(0.0, 0.0));

            var inside = AddHero(state, "hero_in", HeroClass.Gunslinger, hp: 100.0, maxHp: 500.0, pos: new Vec2(3.0, 0.0));
            var outside = AddHero(state, "hero_out", HeroClass.Gunslinger, hp: 100.0, maxHp: 500.0, pos: new Vec2(5.5, 0.0));

            sim.TickMedStations();

            Assert.Multiple(() =>
            {
                Assert.That(inside.Hp, Is.GreaterThan(100.0), "a hero 3 units from a radius-5 station heals");
                Assert.That(inside.Hp, Is.LessThanOrEqualTo(100.0 + (Rate * Now) + Tolerance),
                    "a station cannot heal faster than its configured rate");
                Assert.That(outside.Hp, Is.EqualTo(100.0).Within(Tolerance),
                    "a hero 5.5 units from a radius-5 station is out of its reach");
            });
        }

        /// <summary>
        /// A med station tops heroes up; it does not overheal. The cap is each hero's own MaxHp —
        /// the three classes do not share one — and a long window must land exactly on it rather
        /// than sail past.
        /// </summary>
        [Test]
        public void Med_station_healing_never_exceeds_max_hp()
        {
            var config = MedStationConfig(ratePerSecond: 5.0, radius: 5.0);
            var sim = SimWith(out var state, config, new SimClock(1000.0));
            AddMedStation(state, config, "med1", new Vec2(0.0, 0.0));

            var hero = AddHero(state, "hero_a", HeroClass.Sawbones, hp: 195.0, maxHp: 200.0, pos: new Vec2(1.0, 0.0));

            sim.TickMedStations();

            Assert.That(hero.Hp, Is.EqualTo(200.0).Within(Tolerance),
                "med station healing clamps at MaxHp however long the station has been standing");
        }

        /// <summary>
        /// A corpse does not heal, consistent with R-33/R-35 and with how ticket 007 already treats
        /// regen: coming back is respawn, at full HP, and a med station must never be the thing that
        /// quietly resurrects a hero standing in its footprint.
        /// </summary>
        [Test]
        public void Dead_heroes_are_not_healed_by_a_med_station()
        {
            var config = MedStationConfig(ratePerSecond: 5.0, radius: 5.0);
            var sim = SimWith(out var state, config, new SimClock(60.0));
            AddMedStation(state, config, "med1", new Vec2(0.0, 0.0));

            var hero = AddHero(state, "hero_a", HeroClass.Rancher, hp: 0.0, maxHp: 200.0, pos: new Vec2(1.0, 0.0));
            hero.Alive = false;

            sim.TickMedStations();

            Assert.Multiple(() =>
            {
                Assert.That(hero.Hp, Is.EqualTo(0.0).Within(Tolerance), "a dead hero is not healed");
                Assert.That(hero.Alive, Is.False, "healing must not resurrect anyone");
            });
        }

        /// <summary>
        /// Both med station numbers are catalog rows, not constants. Deliberately tuned away from
        /// R-23's 5/5: the hero here stands 6 units out, so a hardcoded radius of 5 heals him not at
        /// all, and the two runs differ only in <see cref="PlaceableStats.HealPerSecond"/>, so a
        /// hardcoded rate heals him the same amount twice.
        ///
        /// Amounts are compared by direction and bound rather than pinned, for the same reason
        /// <see cref="T07_HeroTests.Regen_accrues_at_the_configured_rate"/> does it that way.
        /// </summary>
        [Test]
        public void Med_station_rate_and_radius_come_from_the_catalog()
        {
            const double Radius = 7.0;
            const double Now = 10.0;

            var slow = MedStationGain(ratePerSecond: 3.0, radius: Radius, heroPos: new Vec2(6.0, 0.0), now: Now);
            var fast = MedStationGain(ratePerSecond: 6.0, radius: Radius, heroPos: new Vec2(6.0, 0.0), now: Now);

            Assert.Multiple(() =>
            {
                Assert.That(slow, Is.GreaterThan(0.0),
                    "a hero 6 units from a radius-7 station must be healed — the radius is config, not 5");
                Assert.That(slow, Is.LessThanOrEqualTo((3.0 * Now) + Tolerance));
                Assert.That(fast, Is.LessThanOrEqualTo((6.0 * Now) + Tolerance));
                Assert.That(fast, Is.GreaterThan(slow),
                    "HealPerSecond must actually be read from the catalog");
            });
        }

        /// <summary>
        /// R-35: "Med Station stacks". The PRD gives no arithmetic for the word, so this pins the
        /// only two things it unambiguously means — direction and bound. Three identical heroes,
        /// one tick of each seam:
        ///
        ///   both  — inside the radius AND past the out-of-combat grace period,
        ///   regen — eligible for regen but standing outside the radius,
        ///   med   — inside the radius but hit one second ago, so still in combat.
        ///
        /// The two single-source heroes are placed at equal distance from the station so that the
        /// comparison cannot be decided by a distance falloff nobody specified. "Stacks" is then:
        /// the hero receiving both ends up strictly better off than either hero receiving one — and
        /// no better off than the two sources could pay out separately.
        /// </summary>
        [Test]
        public void Med_station_healing_stacks_with_out_of_combat_regen()
        {
            const double MedRate = 3.0;
            const double RegenRate = 2.0;
            const double Delay = 5.0;
            const double Now = 15.0;

            var config = MedStationConfig(MedRate, radius: 5.0);
            config.RegenHpPerSecond = RegenRate;
            config.RegenDelaySeconds = Delay;

            var sim = SimWith(out var state, config, new SimClock(Now));
            AddMedStation(state, config, "med1", new Vec2(0.0, 0.0));

            // Same distance from the station, so any radial rule pays these two the same med share.
            var both = AddHero(state, "hero_both", HeroClass.Gunslinger, hp: 100.0, maxHp: 1000.0, pos: new Vec2(2.0, 0.0));
            var medOnly = AddHero(state, "hero_med", HeroClass.Gunslinger, hp: 100.0, maxHp: 1000.0, pos: new Vec2(0.0, 2.0));
            var regenOnly = AddHero(state, "hero_regen", HeroClass.Gunslinger, hp: 100.0, maxHp: 1000.0, pos: new Vec2(9.0, 0.0));

            both.LastDamagedAt = 0.0;
            regenOnly.LastDamagedAt = 0.0;
            medOnly.LastDamagedAt = Now - 1.0;  // hit a second ago: in combat, so no regen

            sim.TickHeroRegen();
            sim.TickMedStations();

            var bothGain = both.Hp - 100.0;
            var medGain = medOnly.Hp - 100.0;
            var regenGain = regenOnly.Hp - 100.0;

            Assert.Multiple(() =>
            {
                Assert.That(regenGain, Is.GreaterThan(0.0), "R-35 regen still has to run on its own");
                Assert.That(medGain, Is.GreaterThan(0.0), "a med station heals a hero who is still in combat");
                Assert.That(bothGain, Is.GreaterThan(regenGain),
                    "R-35 'Med Station stacks': standing in the station must beat regen alone");
                Assert.That(bothGain, Is.GreaterThan(medGain),
                    "R-35 'Med Station stacks': the station must not replace regen, it adds to it");
                Assert.That(bothGain,
                    Is.LessThanOrEqualTo((RegenRate * (Now - Delay)) + (MedRate * Now) + Tolerance),
                    "stacking adds two sources; it does not multiply them");
            });
        }

        /// <summary>
        /// A med station that has been destroyed or sold is off the map. <see cref="Placeable.Exists"/>
        /// is the predicate every other placeable rule reads (R-16's blocker check, R-22's sell), and
        /// a heal aura that outlives its emitter would keep a refunded 200-scrip station healing.
        /// </summary>
        [Test]
        public void A_destroyed_med_station_heals_nobody()
        {
            var config = MedStationConfig(ratePerSecond: 5.0, radius: 5.0);
            var sim = SimWith(out var state, config, new SimClock(100.0));
            AddMedStation(state, config, "med1", new Vec2(0.0, 0.0), exists: false);

            var hero = AddHero(state, "hero_a", HeroClass.Sawbones, hp: 100.0, maxHp: 200.0, pos: new Vec2(1.0, 0.0));
            hero.LastDamagedAt = 100.0;

            sim.TickMedStations();

            Assert.That(hero.Hp, Is.EqualTo(100.0).Within(Tolerance),
                "a station that no longer exists must not keep healing");
        }

        // ---- R-23: the spike trap countdown as a rule ----------------------------------------------

        /// <summary>
        /// G-027 pins only the *last* crossing of a spike trap. The rule R-23 states is a countdown:
        /// "30 dmg per monster crossing; 10 triggers then breaks". Walking a trap all the way down
        /// is what separates a real counter from a trap that breaks on its first use, or from one
        /// that deals damage without ever decrementing (G-027's own `defends_against`: "infinite
        /// trap").
        ///
        /// Three triggers rather than ten so the test stays one screen; the count is seeded on the
        /// entity, so nothing here depends on the catalog's 10 (which the catalog test pins).
        /// </summary>
        [Test]
        public void Spike_trap_damages_and_decrements_on_every_crossing_until_it_breaks()
        {
            const int Triggers = 3;
            const double Damage = 30.0;
            const double StartHp = 500.0;

            var sim = SimWith(out var state);
            var trap = AddSpikeTrap(state, "trap1", new Vec2(0.0, 0.0), Damage, Triggers);
            var monster = AddMonster(state, "m1", MonsterType.BullBehemoth, new Vec2(0.0, 0.0), StartHp);

            for (var crossing = 1; crossing <= Triggers; crossing++)
            {
                var expectedRemaining = Triggers - crossing;
                var lastCrossing = expectedRemaining == 0;
                var expectedHp = StartHp - (Damage * crossing);

                var result = (TrapTriggerResult)sim.TriggerPlaceable("trap1", "m1");

                Assert.Multiple(() =>
                {
                    Assert.That(result.DamageDealt, Is.EqualTo(Damage).Within(Tolerance),
                        "crossing " + crossing + " must deal the trap's full damage");
                    Assert.That(monster.Hp, Is.EqualTo(expectedHp).Within(Tolerance),
                        "crossing " + crossing + " HP");
                    Assert.That(result.TriggersRemaining, Is.EqualTo(expectedRemaining),
                        "crossing " + crossing + " must decrement the counter");
                    Assert.That(trap.TriggersRemaining, Is.EqualTo(expectedRemaining),
                        "the entity must carry the same counter the result reported");
                    Assert.That(result.Broke, Is.EqualTo(lastCrossing),
                        "the trap breaks on the crossing that reaches 0, and on no earlier one");
                    Assert.That(trap.Exists, Is.EqualTo(!lastCrossing),
                        "the trap is removed from the world only once it has spent its last trigger");
                });
            }
        }

        /// <summary>
        /// The other end of the counter: once a trap is spent it is scenery. Whether a crossing of
        /// broken spikes refuses or throws is open, so only the effect is pinned — the monster walks
        /// away unhurt and nothing announces a trigger.
        /// </summary>
        [Test]
        public void A_broken_spike_trap_never_triggers_again()
        {
            var sim = SimWith(out var state);
            var trap = AddSpikeTrap(state, "trap1", new Vec2(0.0, 0.0), damage: 30.0, triggersRemaining: 0);
            trap.Exists = false;
            var monster = AddMonster(state, "m1", MonsterType.Shambler, new Vec2(0.0, 0.0), hp: 60.0);

            Attempt(() => sim.TriggerPlaceable("trap1", "m1"), out var thrown);

            Assert.Multiple(() =>
            {
                Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                    "trigger_placeable is still a stub, so a spent trap has no defined behaviour yet");
                Assert.That(monster.Hp, Is.EqualTo(60.0).Within(Tolerance), "spent spikes deal no damage");
                Assert.That(trap.TriggersRemaining, Is.EqualTo(0), "the counter must not go negative");
                Assert.That(FiredEvents(sim), Is.Empty, "a spent trap announces nothing");
            });
        }

        // ---- R-23: turret targeting, the axes G-028 cannot reach ------------------------------------

        /// <summary>
        /// G-028 shows one monster comfortably inside range 8 and one comfortably outside it, so it
        /// cannot say what happens *at* 8. This repo has one convention for range and deadline
        /// boundaries — inclusive — set by G-019 (a status effect expires at exactly its expires_at,
        /// with strict greater-than named as the bug) and followed by tickets 004, 007 and 008. A
        /// turret at exactly its stated range must therefore fire.
        ///
        /// The dead case rides along on the same arrangement: G-028's dead monster is also the
        /// *nearest*, so it cannot distinguish "skips the dead" from "skips whoever is nearest".
        /// </summary>
        [TestCase(7.5, true, true)]
        [TestCase(8.0, true, true)]
        [TestCase(8.000001, true, false)]
        [TestCase(12.0, true, false)]
        [TestCase(4.0, false, false)]
        public void Turret_range_is_inclusive_and_only_living_monsters_count(
            double distance, bool alive, bool expectFire)
        {
            var sim = SimWith(out var state);
            AddTurret(state, "t1", new Vec2(0.0, 0.0), damage: 20.0, range: 8.0);
            var monster = AddMonster(state, "m1", MonsterType.Shambler, new Vec2(distance, 0.0), hp: 60.0);
            monster.Alive = alive;

            var result = sim.TurretTick("t1");

            Assert.Multiple(() =>
            {
                if (expectFire)
                {
                    Assert.That(result.TargetId, Is.EqualTo("m1"),
                        "a living monster at " + distance + " is inside a range-8 turret's reach");
                    Assert.That(result.Distance, Is.EqualTo(distance).Within(1e-6));
                    Assert.That(monster.Hp, Is.EqualTo(40.0).Within(Tolerance));
                }
                else
                {
                    Assert.That(result.TargetId, Is.Null.Or.Empty,
                        "a turret with nothing valid to shoot names no target");
                    Assert.That(monster.Hp, Is.EqualTo(60.0).Within(Tolerance),
                        "a monster at " + distance + " (alive: " + alive + ") must not be shot");
                    Assert.That(result.DamageDealt, Is.EqualTo(0.0).Within(Tolerance));
                }
            });
        }

        /// <summary>
        /// Two living monsters at exactly the same distance. R-16 breaks monster-targeting ties on
        /// the lowest entity id (ordinal), but the PRD never says a turret follows the same rule, so
        /// this test does not guess which of the two wins. What it does pin is the property that
        /// actually matters for a host-authoritative, replicated sim (R-51): the answer must be a
        /// property of the world, not of dictionary insertion order, so the same arrangement built
        /// in either order must fire at the same monster — and at exactly one of them.
        /// </summary>
        [Test]
        public void Turret_tie_between_equidistant_monsters_resolves_deterministically()
        {
            var alphaFirst = TieTargetId(alphaFirst: true);
            var omegaFirst = TieTargetId(alphaFirst: false);

            Assert.Multiple(() =>
            {
                Assert.That(alphaFirst, Is.Not.Null.And.Not.Empty, "a turret with two valid targets fires at one");
                Assert.That(alphaFirst, Is.AnyOf("m_alpha", "m_omega"));
                Assert.That(omegaFirst, Is.EqualTo(alphaFirst),
                    "the tiebreak must not depend on the order the monsters were added to the world");
                Assert.That(TieTargetId(alphaFirst: true), Is.EqualTo(alphaFirst),
                    "the same arrangement must resolve the same way every time it is evaluated");
            });
        }

        /// <summary>
        /// G-028's `defends_against` names this exactly: a turret that "reuses hero-targeting rules
        /// that include hotspots". G-028 has no hero and no hotspot in the world at all, so it
        /// cannot catch it. Here both sit closer to the turret than the only monster does.
        /// </summary>
        [Test]
        public void Turret_never_fires_at_a_hero_or_a_hotspot()
        {
            var sim = SimWith(out var state);
            AddTurret(state, "t1", new Vec2(0.0, 0.0), damage: 20.0, range: 8.0);

            var ally = AddHero(state, "hero_a", HeroClass.Sawbones, hp: 200.0, maxHp: 200.0, pos: new Vec2(1.0, 0.0));
            var shelter = AddHotspot(state, "hs_saloon", new Vec2(0.0, 1.0), civilians: 8);
            var monster = AddMonster(state, "m1", MonsterType.Shambler, new Vec2(6.0, 0.0), hp: 60.0);

            var result = sim.TurretTick("t1");

            Assert.Multiple(() =>
            {
                Assert.That(result.TargetId, Is.EqualTo("m1"), "a turret shoots monsters and nothing else");
                Assert.That(monster.Hp, Is.EqualTo(40.0).Within(Tolerance));
                Assert.That(ally.Hp, Is.EqualTo(200.0).Within(Tolerance), "R-26: turrets do not shoot heroes");
                Assert.That(shelter.Civilians, Is.EqualTo(8), "a turret must never fire into a shelter");
            });
        }

        /// <summary>
        /// The empty-sky tick, which happens on most frames of a real match and which no fixture
        /// covers: a turret with nothing valid in range must be a defined no-op, not a crash and not
        /// a shot at whatever else is standing nearby. Everything in range here is ineligible — a
        /// corpse, an ally, a shelter — and the only living monster is well outside it.
        /// </summary>
        [Test]
        public void Turret_with_no_living_monster_in_range_holds_fire()
        {
            var sim = SimWith(out var state);
            AddTurret(state, "t1", new Vec2(0.0, 0.0), damage: 20.0, range: 8.0);

            var corpse = AddMonster(state, "m_dead", MonsterType.Shambler, new Vec2(1.0, 0.0), hp: 0.0);
            corpse.Alive = false;
            var ally = AddHero(state, "hero_a", HeroClass.Rancher, hp: 200.0, maxHp: 200.0, pos: new Vec2(2.0, 0.0));
            var shelter = AddHotspot(state, "hs_chapel", new Vec2(0.0, 3.0), civilians: 6);
            var distant = AddMonster(state, "m_far", MonsterType.Ravager, new Vec2(30.0, 0.0), hp: 40.0);

            var result = sim.TurretTick("t1");

            Assert.Multiple(() =>
            {
                Assert.That(result.TurretId, Is.EqualTo("t1"), "the tick still answers for the turret it ran on");
                Assert.That(result.TargetId, Is.Null.Or.Empty);
                Assert.That(result.DamageDealt, Is.EqualTo(0.0).Within(Tolerance));
                Assert.That(distant.Hp, Is.EqualTo(40.0).Within(Tolerance));
                Assert.That(corpse.Hp, Is.EqualTo(0.0).Within(Tolerance));
                Assert.That(ally.Hp, Is.EqualTo(200.0).Within(Tolerance));
                Assert.That(shelter.Civilians, Is.EqualTo(6));
                Assert.That(sim.LastObservation.StateChanges, Is.Empty, "an idle tick replicates nothing");
                Assert.That(FiredEvents(sim), Is.Empty, "an idle turret announces nothing");
            });
        }

        // ---- R-23: the dynamite blast --------------------------------------------------------------

        /// <summary>
        /// G-029's monsters sit at 0, 2 and 5 against a radius of 3, so the boundary itself is
        /// untested. Same inclusive convention as the turret's range: a monster standing exactly on
        /// the edge of the blast is caught by it.
        ///
        /// The corpse in the middle of the blast is the other axis — "every living monster in the
        /// radius" is the rule, and a blast that re-damages the dead would re-emit kill effects and
        /// (once R-40's kill accounting hangs off this) pay a second bounty.
        /// </summary>
        [Test]
        public void Dynamite_blast_radius_is_inclusive_and_hits_living_monsters_only()
        {
            const double Damage = 100.0;
            const double Radius = 3.0;

            var sim = SimWith(out var state);
            AddDynamite(state, "dyn1", new Vec2(0.0, 0.0), Damage, Radius);

            var trigger = AddMonster(state, "m_trigger", MonsterType.Shambler, new Vec2(0.0, 0.0), hp: 200.0);
            var onEdge = AddMonster(state, "m_edge", MonsterType.Ravager, new Vec2(3.0, 0.0), hp: 200.0);
            var justOutside = AddMonster(state, "m_out", MonsterType.Ravager, new Vec2(3.5, 0.0), hp: 200.0);
            var corpse = AddMonster(state, "m_dead", MonsterType.Spitter, new Vec2(1.0, 0.0), hp: 0.0);
            corpse.Alive = false;

            var result = (BlastTriggerResult)sim.TriggerPlaceable("dyn1", "m_trigger");

            Assert.Multiple(() =>
            {
                Assert.That(trigger.Hp, Is.EqualTo(100.0).Within(Tolerance), "the monster that set it off is in the blast");
                Assert.That(onEdge.Hp, Is.EqualTo(100.0).Within(Tolerance),
                    "a monster at exactly the blast radius is inside it (inclusive, per G-019's convention)");
                Assert.That(justOutside.Hp, Is.EqualTo(200.0).Within(Tolerance), "3.5 is outside a radius of 3");
                Assert.That(corpse.Hp, Is.EqualTo(0.0).Within(Tolerance), "a corpse is not damaged again");
                Assert.That(corpse.Alive, Is.False);

                Assert.That(result.MonstersHit, Does.Contain("m_trigger"));
                Assert.That(result.MonstersHit, Does.Contain("m_edge"));
                Assert.That(result.MonstersHit, Does.Not.Contain("m_out"));
                Assert.That(result.MonstersHit, Does.Not.Contain("m_dead"));
                Assert.That(result.DamageEach, Is.EqualTo(Damage).Within(Tolerance));
            });
        }

        /// <summary>
        /// R-26 / DEC-019 says hero attacks never damage heroes or placeables. Ticket 007 enforced
        /// that for basics and ticket 008 for abilities, both through an allowlist of what may be
        /// hit — but a dynamite blast is not a hero attack, it is an area sweep over whatever stands
        /// in a circle, and that is exactly where friendly fire creeps back in. No fixture covers
        /// it: G-029's blast contains monsters only.
        ///
        /// A team that cannot place dynamite near its own wall without demolishing it has a
        /// different game than the one R-23 describes.
        /// </summary>
        [Test]
        public void Dynamite_blast_never_damages_heroes_or_placeables()
        {
            var sim = SimWith(out var state);
            AddDynamite(state, "dyn1", new Vec2(0.0, 0.0), damage: 150.0, blastRadius: 4.0);

            var ally = AddHero(state, "hero_a", HeroClass.Gunslinger, hp: 200.0, maxHp: 200.0, pos: new Vec2(1.0, 0.0));
            var wall = AddBarricade(state, "bar1", new Vec2(1.5, 0.0), hp: 300.0);
            var station = AddMedStation(state, new SimConfig(), "med1", new Vec2(2.0, 0.0));
            var monster = AddMonster(state, "m1", MonsterType.Shambler, new Vec2(0.0, 0.0), hp: 400.0);

            sim.TriggerPlaceable("dyn1", "m1");

            Assert.Multiple(() =>
            {
                Assert.That(monster.Hp, Is.EqualTo(250.0).Within(Tolerance), "the blast still hits monsters");
                Assert.That(ally.Hp, Is.EqualTo(200.0).Within(Tolerance), "R-26: no friendly fire onto heroes");
                Assert.That(ally.Alive, Is.True);
                Assert.That(wall.Hp, Is.EqualTo(300.0).Within(Tolerance), "R-26: no friendly fire onto placeables");
                Assert.That(wall.Exists, Is.True, "the team's own barricade must survive its own dynamite");
                Assert.That(station.Exists, Is.True);
            });
        }

        // ---- sad paths: shape and non-corruption only ----------------------------------------------

        /// <summary>
        /// Commands that name something that cannot be triggered: a placeable id this match never
        /// had, and a placeable that is not a trap at all. The PRD describes neither, and this sim
        /// answers such things two different ways already (<see cref="MatchSim.SelectTarget"/>
        /// returns an empty selection, <see cref="MatchSim.ApplyHotspotAttack"/> throws), so the
        /// error shape is deliberately unpinned. What is pinned is that a bad command cannot damage
        /// anybody or announce an effect that did not happen.
        /// </summary>
        [TestCase("ghost1", TestName = "trigger_unknown_placeable_id")]
        [TestCase("t1", TestName = "trigger_a_turret_which_is_not_a_trap")]
        [TestCase("bar1", TestName = "trigger_a_barricade_which_is_not_a_trap")]
        public void Triggering_something_that_is_not_a_live_trap_changes_nothing(string placeableId)
        {
            var sim = SimWith(out var state);
            var turret = AddTurret(state, "t1", new Vec2(0.0, 0.0), damage: 20.0, range: 8.0);
            var wall = AddBarricade(state, "bar1", new Vec2(1.0, 0.0), hp: 300.0);
            var monster = AddMonster(state, "m1", MonsterType.Shambler, new Vec2(0.0, 0.0), hp: 60.0);

            Attempt(() => sim.TriggerPlaceable(placeableId, "m1"), out var thrown);

            Assert.Multiple(() =>
            {
                Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                    "trigger_placeable is still a stub, so this command has no defined behaviour yet");
                Assert.That(monster.Hp, Is.EqualTo(60.0).Within(Tolerance), "nothing was triggered, so nothing was hurt");
                Assert.That(turret.Exists, Is.True);
                Assert.That(wall.Exists, Is.True);
                Assert.That(wall.Hp, Is.EqualTo(300.0).Within(Tolerance));
                Assert.That(FiredEvents(sim), Is.Empty, "a command that did nothing must announce nothing");
            });
        }

        /// <summary>
        /// The same for the turret tick: an id this match never had, and a placeable that is not a
        /// turret. Shape and non-corruption only — a stray tick must not turn a barricade into a gun.
        /// </summary>
        [TestCase("ghost1", TestName = "turret_tick_unknown_id")]
        [TestCase("bar1", TestName = "turret_tick_on_a_barricade")]
        [TestCase("trap1", TestName = "turret_tick_on_a_spike_trap")]
        public void Turret_tick_on_something_that_is_not_a_turret_changes_nothing(string turretId)
        {
            var sim = SimWith(out var state);
            var wall = AddBarricade(state, "bar1", new Vec2(0.0, 0.0), hp: 300.0);
            var trap = AddSpikeTrap(state, "trap1", new Vec2(0.0, 0.0), damage: 30.0, triggersRemaining: 10);
            var monster = AddMonster(state, "m1", MonsterType.Shambler, new Vec2(1.0, 0.0), hp: 60.0);

            Attempt(() => sim.TurretTick(turretId), out var thrown);

            Assert.Multiple(() =>
            {
                Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                    "turret_tick is still a stub, so this command has no defined behaviour yet");
                Assert.That(monster.Hp, Is.EqualTo(60.0).Within(Tolerance), "only a turret shoots");
                Assert.That(wall.Hp, Is.EqualTo(300.0).Within(Tolerance));
                Assert.That(trap.TriggersRemaining, Is.EqualTo(10), "a turret tick must not spend a trap's triggers");
                Assert.That(FiredEvents(sim), Is.Empty);
            });
        }

        // ---- scenario helpers ------------------------------------------------------------------------

        private static MatchSim SimWith(out MatchState state, SimConfig config = null, IClock clock = null)
        {
            state = new MatchState();
            return new MatchSim(
                state, config ?? new SimConfig(), profileStore: null, clock: clock ?? new SimClock(0.0));
        }

        /// <summary>A config whose med station row is deliberately tuned away from R-23's 5 / 5.</summary>
        private static SimConfig MedStationConfig(double ratePerSecond, double radius)
        {
            var config = new SimConfig();
            config.Placeables.Set(PlaceableType.MedStation, new PlaceableStats
            {
                Cost = 200,
                HealPerSecond = ratePerSecond,
                Range = radius,
            });

            return config;
        }

        /// <summary>HP one hero gained from a single med station tick under the given tuning.</summary>
        private static double MedStationGain(double ratePerSecond, double radius, Vec2 heroPos, double now)
        {
            var config = MedStationConfig(ratePerSecond, radius);
            var sim = SimWith(out var state, config, new SimClock(now));
            AddMedStation(state, config, "med1", new Vec2(0.0, 0.0));

            var hero = AddHero(state, "hero_a", HeroClass.Gunslinger, hp: 100.0, maxHp: 1000.0, pos: heroPos);
            hero.LastDamagedAt = now;  // in combat, so nothing but the station can move this HP

            sim.TickMedStations();
            return hero.Hp - 100.0;
        }

        /// <summary>
        /// The turret's chosen target for two equidistant monsters, built with the two added to the
        /// world in the requested order so dictionary iteration order is the variable under test.
        /// </summary>
        private static string TieTargetId(bool alphaFirst)
        {
            var sim = SimWith(out var state);
            AddTurret(state, "t1", new Vec2(0.0, 0.0), damage: 20.0, range: 8.0);

            // Both exactly 5.0 from the turret.
            if (alphaFirst)
            {
                AddMonster(state, "m_alpha", MonsterType.Shambler, new Vec2(3.0, 4.0), hp: 60.0);
                AddMonster(state, "m_omega", MonsterType.Shambler, new Vec2(-3.0, -4.0), hp: 60.0);
            }
            else
            {
                AddMonster(state, "m_omega", MonsterType.Shambler, new Vec2(-3.0, -4.0), hp: 60.0);
                AddMonster(state, "m_alpha", MonsterType.Shambler, new Vec2(3.0, 4.0), hp: 60.0);
            }

            var result = sim.TurretTick("t1");

            Assert.That(state.Monsters.Values.Count(m => m.Hp < 60.0), Is.EqualTo(1),
                "one tick is one shot — a tie must not be resolved by shooting both");

            return result.TargetId;
        }

        // ---- entity builders -------------------------------------------------------------------------

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

        private static Hero AddHero(
            MatchState state, string id, string heroClass, double hp, double maxHp, Vec2 pos)
        {
            var hero = new Hero
            {
                Id = id,
                HeroClass = heroClass,
                AccountId = "acct_" + id,
                Pos = pos,
                Hp = hp,
                MaxHp = maxHp,
                Alive = true,
            };

            state.Heroes[id] = hero;
            return hero;
        }

        private static Hotspot AddHotspot(MatchState state, string id, Vec2 pos, int civilians)
        {
            var hotspot = new Hotspot { Id = id, Pos = pos, Civilians = civilians };
            state.Hotspots[id] = hotspot;
            return hotspot;
        }

        private static Placeable AddBarricade(MatchState state, string id, Vec2 pos, double hp)
        {
            return AddPlaceable(state, new Placeable
            {
                Id = id,
                Type = PlaceableType.Barricade,
                Pos = pos,
                PurchaseCost = 100,
                Hp = hp,
            });
        }

        private static Placeable AddSpikeTrap(
            MatchState state, string id, Vec2 pos, double damage, int triggersRemaining)
        {
            return AddPlaceable(state, new Placeable
            {
                Id = id,
                Type = PlaceableType.SpikeTrap,
                Pos = pos,
                PurchaseCost = 75,
                Damage = damage,
                TriggersRemaining = triggersRemaining,
            });
        }

        private static Placeable AddDynamite(
            MatchState state, string id, Vec2 pos, double damage, double blastRadius)
        {
            return AddPlaceable(state, new Placeable
            {
                Id = id,
                Type = PlaceableType.DynamiteTrap,
                Pos = pos,
                PurchaseCost = 150,
                Damage = damage,
                BlastRadius = blastRadius,
                TriggersRemaining = 1,
            });
        }

        private static Placeable AddTurret(MatchState state, string id, Vec2 pos, double damage, double range)
        {
            return AddPlaceable(state, new Placeable
            {
                Id = id,
                Type = PlaceableType.Turret,
                Pos = pos,
                PurchaseCost = 250,
                Damage = damage,
                Range = range,
            });
        }

        /// <summary>
        /// A med station built the way <see cref="MatchSim.PurchasePlacement"/> builds one: the whole
        /// catalog row copied onto the entity. That is deliberate — it leaves the implementation free
        /// to read the radius off the instance or off <see cref="SimConfig.Placeables"/>, because in
        /// a real match those two agree, and this test must not pick one of them as spec.
        /// </summary>
        private static Placeable AddMedStation(
            MatchState state, SimConfig config, string id, Vec2 pos, bool exists = true)
        {
            var stats = config.Placeables.StatsFor(PlaceableType.MedStation);

            return AddPlaceable(state, new Placeable
            {
                Id = id,
                Type = PlaceableType.MedStation,
                Pos = pos,
                PurchaseCost = stats.Cost,
                Exists = exists,
                Hp = stats.MaxHp,
                Damage = stats.Damage,
                TriggersRemaining = stats.TriggerCount,
                BlastRadius = stats.BlastRadius,
                Range = stats.Range,
            });
        }

        private static Placeable AddPlaceable(MatchState state, Placeable placeable)
        {
            placeable.OwnerPlayerId = placeable.OwnerPlayerId ?? "hero_a";
            state.Placeables[placeable.Id] = placeable;
            return placeable;
        }

        private static PlaceableDamageRequest Hit(
            string attackerId, string attackerType, double damage, string targetId)
        {
            return new PlaceableDamageRequest
            {
                AttackerId = attackerId,
                AttackerType = attackerType,
                Damage = damage,
                TargetId = targetId,
            };
        }

        // ---- observation helpers ---------------------------------------------------------------------

        /// <summary>The replicated delta for one entity field, or null when the command recorded none.</summary>
        private static StateChange ChangeFor(MatchSim sim, string entity, string field)
        {
            return sim.LastObservation.StateChanges
                .FirstOrDefault(c => c.Entity == entity && c.Field == field);
        }

        /// <summary>
        /// Whether the last command announced that this placeable left the world. Two spellings are
        /// accepted because R-23 only requires that destruction *is* announced: G-027/G-029 lock
        /// `placeable_broken` for a spent trap, and a destroyed wall may reasonably reuse that name
        /// or carry its own. Pinning one of them would be this test inventing vocabulary.
        /// </summary>
        private static bool AnnouncesRemovalOf(MatchSim sim, string placeableId)
        {
            return sim.LastObservation.EmittedEvents.Any(e =>
                (e.Type == "placeable_broken" || e.Type == "placeable_destroyed")
                && e.Fields.TryGetValue("placeable_id", out var id)
                && Equals(id, placeableId));
        }

        /// <summary>Every "something fired" event the last command emitted, whatever the source.</summary>
        private static IEnumerable<SimEvent> FiredEvents(MatchSim sim)
        {
            return sim.LastObservation.EmittedEvents.Where(e =>
                e.Type == "placeable_triggered" || e.Type == "placeable_broken"
                || e.Type == "placeable_destroyed" || e.Type == "turret_fired");
        }

        /// <summary>
        /// Runs a command that may legitimately throw, capturing the exception instead of failing.
        /// Used only where the PRD leaves the sad path open; the NotImplementedException guard at
        /// each call site is what keeps those tests red until T-06 lands.
        /// </summary>
        private static TResult Attempt<TResult>(Func<TResult> command, out Exception thrown)
            where TResult : class
        {
            thrown = null;
            try
            {
                return command();
            }
            catch (Exception ex)
            {
                thrown = ex;
                return null;
            }
        }
    }
}
