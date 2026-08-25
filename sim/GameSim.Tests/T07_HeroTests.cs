using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Ticket 007 (T-07): hero damage, death/respawn and the no-friendly-fire rule —
    /// R-26, R-33, R-34, R-35, R-36.
    ///
    /// The three fixtures that grade this area (G-020, G-021, G-030) are already turned into
    /// cases by the locked golden adapter, so nothing here re-encodes them. What lives here is
    /// the *rule* behind each fixture — the axes a single fixture pins only one point on:
    ///
    ///   * G-020 pins one Sawbones hit. The rule is that the reduction is class-conditional
    ///     (R-31 / DEC-009) and that it floors, for every damage value — not just 15.
    ///   * G-021 pins death at sim_elapsed 45.0. The rule is that respawn_at is read from the
    ///     injected clock plus config, so these tests use clocks that are deliberately *not* 45.0
    ///     and delays that are deliberately not 10.0 — an implementation that returns 55.0 from a
    ///     literal cannot pass both.
    ///   * G-030 pins one ally + one barricade in front of one monster. The rule is that hero
    ///     attacks damage monsters only (R-26/R-36), whatever else the aim line crossed.
    ///
    /// Plus the two acceptance criteria no fixture covers at all: all heroes dead is not defeat,
    /// and a dead hero drops out of the living-hero set that monster targeting reads (R-33).
    ///
    /// Scenarios are built from production types directly rather than through the fixture JSON
    /// loader — the loader is the adapter's contract, not this ticket's.
    /// </summary>
    [TestFixture]
    public class T07_HeroTests
    {
        private const double Tolerance = 1e-9;

        // ---- R-31 / DEC-009: the Sawbones reduction is class-conditional --------------------------

        /// <summary>
        /// G-020 shows a Sawbones taking floor(damage * 0.7). It cannot show what the other two
        /// classes do, so it cannot catch a reduction wired to "all heroes" instead of the Sawbones
        /// passive. Same hit, same HP, three classes: only the Sawbones is reduced.
        /// </summary>
        [TestCase(HeroClass.Gunslinger, 15.0, 15.0)]
        [TestCase(HeroClass.Rancher, 15.0, 15.0)]
        [TestCase(HeroClass.Sawbones, 15.0, 10.0)]
        public void Damage_reduction_applies_to_sawbones_only(
            string heroClass, double damage, double expectedTaken)
        {
            var sim = SimWith(out var state);
            var hero = AddHero(state, "hero_x", heroClass, hp: 200, maxHp: 200);

            var result = sim.ApplyHeroDamage(Hit("m_atk", MonsterType.Shambler, damage, hero.Id));

            Assert.Multiple(() =>
            {
                Assert.That(result.DamageTaken, Is.EqualTo(expectedTaken).Within(Tolerance),
                    heroClass + " should take " + expectedTaken + " from a " + damage + " hit");
                Assert.That(result.HpAfter, Is.EqualTo(200.0 - expectedTaken).Within(Tolerance));
                Assert.That(hero.Hp, Is.EqualTo(200.0 - expectedTaken).Within(Tolerance),
                    "the entity must carry the same HP the result reported");
                Assert.That(result.Downed, Is.False);
                Assert.That(hero.Alive, Is.True);
            });
        }

        /// <summary>
        /// Flooring is a rule, not the single arithmetic case G-020 happens to exercise. Damage
        /// values that divide evenly (20 -> 14) and values that do not (7 -> 4.9, 15 -> 10.5,
        /// 25 -> 17.5, 33 -> 23.1) must all land on a whole number, because fractional hero HP is
        /// exactly the state desync G-020's `defends_against` names.
        /// </summary>
        [TestCase(7.0, 4.0)]
        [TestCase(15.0, 10.0)]
        [TestCase(20.0, 14.0)]
        [TestCase(25.0, 17.0)]
        [TestCase(33.0, 23.0)]
        public void Sawbones_reduction_floors_to_whole_hp(double damage, double expectedTaken)
        {
            var sim = SimWith(out var state);
            var hero = AddHero(state, "hero_saw", HeroClass.Sawbones, hp: 200, maxHp: 200);

            var result = sim.ApplyHeroDamage(Hit("m_atk", MonsterType.Burrower, damage, hero.Id));

            Assert.Multiple(() =>
            {
                Assert.That(result.DamageTaken, Is.EqualTo(expectedTaken).Within(Tolerance),
                    "expected floor(" + damage + " * 0.7)");
                Assert.That(result.HpAfter, Is.EqualTo(200.0 - expectedTaken).Within(Tolerance));
                Assert.That(result.DamageTaken, Is.EqualTo(Math.Floor(result.DamageTaken)).Within(Tolerance),
                    "damage taken must never be fractional");
                Assert.That(hero.Hp, Is.EqualTo(Math.Floor(hero.Hp)).Within(Tolerance),
                    "hero HP must never land on a fraction");
            });
        }

        // ---- R-33: death is instant, HP clamps at 0, respawn comes off the injected clock ---------

        /// <summary>
        /// G-021 dies at sim_elapsed 45.0 with a 10s delay, so 55.0 is reachable by a hardcode.
        /// These clocks and delays are chosen so it is not: respawn_at must be
        /// clock.ElapsedSeconds + SimConfig.RespawnDelaySeconds, read at the moment of death.
        /// </summary>
        [TestCase(123.5, 10.0, 133.5)]
        [TestCase(12.25, 7.5, 19.75)]
        [TestCase(0.0, 10.0, 10.0)]
        public void Death_schedules_respawn_from_the_clock_and_config(
            double elapsed, double respawnDelay, double expectedRespawnAt)
        {
            var config = new SimConfig { RespawnDelaySeconds = respawnDelay };
            var sim = SimWith(out var state, config, new SimClock(elapsed));
            var hero = AddHero(state, "hero_gun", HeroClass.Gunslinger, hp: 25, maxHp: 100);

            var result = sim.ApplyHeroDamage(Hit("m5", MonsterType.BullBehemoth, 40.0, hero.Id));

            Assert.Multiple(() =>
            {
                Assert.That(result.Downed, Is.True, "0 HP kills instantly — there is no downed state");
                Assert.That(result.RespawnAt, Is.Not.Null, "a downed hero must carry a respawn clock");
                Assert.That(result.RespawnAt.Value, Is.EqualTo(expectedRespawnAt).Within(Tolerance));
                Assert.That(hero.Alive, Is.False);
                Assert.That(hero.Hp, Is.EqualTo(0.0).Within(Tolerance),
                    "HP clamps at 0 — a 40-damage hit on 25 HP must not go negative");
                Assert.That(result.HpAfter, Is.EqualTo(0.0).Within(Tolerance));
                Assert.That(result.DamageTaken, Is.EqualTo(40.0).Within(Tolerance),
                    "reported damage is the incoming amount, not the remaining HP it consumed");

                // The death event carries the same clock the result did (fixtures pin the pairing).
                var died = sim.LastObservation.EmittedEvents.FirstOrDefault(e => e.Type == "hero_died");
                Assert.That(died, Is.Not.Null, "death must emit a hero_died event");
                Assert.That(died.Fields.ContainsKey("respawn_at"), Is.True);
                Assert.That(Convert.ToDouble(died.Fields["respawn_at"]),
                    Is.EqualTo(expectedRespawnAt).Within(Tolerance));
            });
        }

        /// <summary>
        /// Ordering guard from G-020's `defends_against`: the reduction is applied *before* the
        /// death check, and the reported damage is the reduced amount — not the raw hit, and not
        /// clamped down to the HP that was actually left. 5 HP hit for 100 as a Sawbones:
        /// floor(100 * 0.7) = 70 reported, HP floors at 0, hero dies.
        /// </summary>
        [Test]
        public void Overkill_reports_reduced_damage_and_still_kills()
        {
            var sim = SimWith(out var state, clock: new SimClock(200.0));
            var hero = AddHero(state, "hero_saw", HeroClass.Sawbones, hp: 5, maxHp: 200);

            var result = sim.ApplyHeroDamage(Hit("m5", MonsterType.BullBehemoth, 100.0, hero.Id));

            Assert.Multiple(() =>
            {
                Assert.That(result.DamageTaken, Is.EqualTo(70.0).Within(Tolerance));
                Assert.That(result.HpAfter, Is.EqualTo(0.0).Within(Tolerance));
                Assert.That(result.Downed, Is.True);
                Assert.That(hero.Hp, Is.EqualTo(0.0).Within(Tolerance));
                Assert.That(hero.Alive, Is.False);
            });
        }

        // ---- R-33: all heroes dead is not defeat -------------------------------------------------

        /// <summary>
        /// R-02 is the only loss rule: defeat fires exactly when total civilians reaches 0. R-33
        /// says a wiped party is not a loss — the monsters simply move on to the civilians. Killing
        /// the *last* living hero is the tempting place to special-case a game over, so it is tested
        /// both for a solo lobby and for the last survivor of a three-hero party.
        /// </summary>
        [TestCase(1)]
        [TestCase(3)]
        public void All_heroes_dead_is_not_defeat(int partySize)
        {
            var sim = SimWith(out var state, clock: new SimClock(60.0));
            AddHotspot(state, "hs_saloon", civilians: 8);

            // Everyone but the last is already down; this command wipes the party.
            for (var i = 0; i < partySize - 1; i++)
            {
                var corpse = AddHero(state, "hero_" + i, HeroClass.Rancher, hp: 0, maxHp: 120);
                corpse.Alive = false;
            }

            var last = AddHero(state, "hero_last", HeroClass.Gunslinger, hp: 10, maxHp: 100);

            var result = sim.ApplyHeroDamage(Hit("m5", MonsterType.BullBehemoth, 40.0, last.Id));

            Assert.Multiple(() =>
            {
                Assert.That(result.Downed, Is.True, "premise: the last hero really did die");
                Assert.That(state.Heroes.Values.Any(h => h.Alive), Is.False,
                    "premise: the whole party is now dead");

                Assert.That(state.Status, Is.EqualTo(MatchStatus.InProgress),
                    "a wiped party must leave the match in progress (R-33)");
                Assert.That(state.IsOver, Is.False);
                Assert.That(state.TotalCivilians, Is.EqualTo(8),
                    "civilians are untouched by hero deaths — R-02 is the only loss rule");
                Assert.That(sim.LastObservation.EmittedEvents.Select(e => e.Type),
                    Does.Not.Contain("match_defeat"),
                    "no defeat event may be emitted for a party wipe");
                Assert.That(sim.LastObservation.StateChanges.Any(
                        c => c.Entity == "match" && c.Field == "status"),
                    Is.False,
                    "match status must not change when heroes die");
            });
        }

        // ---- R-33: a dead hero leaves the target-candidate set ------------------------------------

        /// <summary>
        /// R-16 picks the nearest of {living hero, hotspot with >= 1 civilian}. The selection
        /// algorithm belongs to ticket 002; what belongs here is the state it reads — after death
        /// the hero is still in the world (it has a respawn clock to serve) but is no longer a
        /// living-hero candidate, and its still-standing ally is.
        /// </summary>
        [Test]
        public void Dead_hero_drops_out_of_the_living_target_candidates()
        {
            var sim = SimWith(out var state, clock: new SimClock(30.0));
            var doomed = AddHero(state, "hero_gun", HeroClass.Gunslinger, hp: 15, maxHp: 100);
            var survivor = AddHero(state, "hero_saw", HeroClass.Sawbones, hp: 200, maxHp: 200);

            sim.ApplyHeroDamage(Hit("m5", MonsterType.BullBehemoth, 40.0, doomed.Id));

            Assert.Multiple(() =>
            {
                Assert.That(state.Heroes.ContainsKey(doomed.Id), Is.True,
                    "a dead hero stays in the world — it respawns, it is not deleted");
                Assert.That(doomed.Alive, Is.False, "Alive is the untargetable predicate (R-33)");

                Assert.That(LivingHeroIds(state), Does.Not.Contain(doomed.Id),
                    "a dead hero must not be offered as a monster target candidate");
                Assert.That(LivingHeroIds(state), Does.Contain(survivor.Id),
                    "the surviving ally must remain a candidate");
            });
        }

        // ---- R-26 / R-36 / DEC-019: no friendly fire ---------------------------------------------

        /// <summary>
        /// G-030 covers one shape of aim line (ally, barricade, monster). The rule is broader: no
        /// matter what the shell's raycast crossed — an ally, a barricade, a non-barricade placeable,
        /// several of each — the shot damages the first *monster* and nothing else. The last case
        /// adds a second monster behind the first: a piercing basic attack is not the contract here.
        /// </summary>
        [TestCase("hero|monster")]
        [TestCase("barricade|monster")]
        [TestCase("turret|monster")]
        [TestCase("hero|barricade|turret|hero|monster")]
        [TestCase("hero|monster|monster")]
        public void Hero_attack_damages_only_the_first_monster_on_the_line(string kinds)
        {
            var sim = SimWith(out var state);
            var attacker = AddHero(state, "hero_gun", HeroClass.Gunslinger, hp: 100, maxHp: 100);
            var line = BuildLine(state, kinds);
            var hpBefore = SnapshotHp(state);

            var expectedHitId = line.First(e => e.Kind == "monster").Id;

            var result = sim.ResolveHeroAttack(new HeroAttackRequest
            {
                AttackerId = attacker.Id,
                AttackerClass = HeroClass.Gunslinger,
                Damage = 25.0,
                EntitiesOnLine = line,
            });

            var hpAfter = SnapshotHp(state);

            Assert.Multiple(() =>
            {
                Assert.That(result.HitId, Is.EqualTo(expectedHitId),
                    "only the nearest monster on the line may be hit");
                Assert.That(result.DamageDealt, Is.EqualTo(25.0).Within(Tolerance));
                Assert.That(result.TargetHpAfter, Is.EqualTo(35.0).Within(Tolerance));

                foreach (var id in hpBefore.Keys)
                {
                    var expected = id == expectedHitId ? 35.0 : hpBefore[id];
                    Assert.That(hpAfter[id], Is.EqualTo(expected).Within(Tolerance),
                        id + " HP changed when it should not have (kinds: " + kinds + ")");
                }

                // The replicated delta stream must agree: one field on one entity moved.
                var changes = sim.LastObservation.StateChanges;
                Assert.That(changes.Count, Is.EqualTo(1),
                    "exactly one state change — the monster's HP");
                Assert.That(changes[0].Entity, Is.EqualTo(expectedHitId));
                Assert.That(changes[0].Field, Is.EqualTo("hp"));

                // Nothing the shot passed through may be named by any emitted event.
                AssertNoEventMentions(sim, hpBefore.Keys.Where(id => id != expectedHitId), attacker.Id);
            });
        }

        /// <summary>
        /// The boundary G-030 cannot show: an aim line that crossed only friendlies. The PRD does
        /// not say what a whiff returns, so this pins shape only — it resolves without throwing and
        /// nothing takes damage. Asserting an exact result or error string here would invent a
        /// contract the spec has not made.
        /// </summary>
        [Test]
        public void Hero_attack_with_no_monster_on_the_line_damages_nothing()
        {
            var sim = SimWith(out var state);
            var attacker = AddHero(state, "hero_gun", HeroClass.Gunslinger, hp: 100, maxHp: 100);
            var line = BuildLine(state, "hero|barricade|turret");
            var hpBefore = SnapshotHp(state);

            HeroAttackResult result = null;
            Assert.DoesNotThrow(() =>
            {
                result = sim.ResolveHeroAttack(new HeroAttackRequest
                {
                    AttackerId = attacker.Id,
                    AttackerClass = HeroClass.Gunslinger,
                    Damage = 25.0,
                    EntitiesOnLine = line,
                });
            }, "a shot that crossed only friendlies must resolve, not crash");

            var hpAfter = SnapshotHp(state);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(result.HitId, Is.Null.Or.Empty, "nothing was hit");
                Assert.That(result.DamageDealt, Is.EqualTo(0.0).Within(Tolerance));

                foreach (var id in hpBefore.Keys)
                {
                    Assert.That(hpAfter[id], Is.EqualTo(hpBefore[id]).Within(Tolerance),
                        id + " took damage from a shot that hit no monster");
                }

                Assert.That(sim.LastObservation.StateChanges, Is.Empty,
                    "a whiff changes no replicated state");
                AssertNoEventMentions(sim, hpBefore.Keys, attacker.Id);
            });
        }

        // ---- R-35: out-of-combat regen (hero half only) -------------------------------------------

        /// <summary>
        /// R-35 has no fixture and one sentence of spec — "2 HP/s after 5s untouched" — so these
        /// tests pin the four things that sentence actually says and nothing more. Whether regen
        /// accrues continuously or in discrete ticks, and how it rounds, are deliberately left open:
        /// the assertions below are bounds and directions, never an exact healed amount.
        ///
        /// "Med Station stacks" is the other half of R-35 and belongs to the placeable ticket, so
        /// it is not tested here.
        /// </summary>
        [TestCase(0.0)]
        [TestCase(2.0)]
        [TestCase(4.9)]
        public void Regen_does_not_start_before_the_delay_elapses(double secondsSinceDamage)
        {
            var sim = RegenSim(out var state, ratePerSecond: 2.0, delaySeconds: 5.0, now: secondsSinceDamage);
            var hero = AddHero(state, "hero_saw", HeroClass.Sawbones, hp: 120, maxHp: 200);
            hero.LastDamagedAt = 0.0;

            sim.TickHeroRegen();

            Assert.That(hero.Hp, Is.EqualTo(120.0).Within(Tolerance),
                "a hero hit " + secondsSinceDamage + "s ago is still in combat — no regen yet");
        }

        /// <summary>
        /// The rate is config, not a constant in the code. Doubling RegenHpPerSecond must heal
        /// strictly more over the same window, and neither run may out-heal rate x (time spent
        /// eligible) — the 5s grace period is not healing time.
        /// </summary>
        [Test]
        public void Regen_accrues_at_the_configured_rate()
        {
            const double Delay = 5.0;
            const double Now = 15.0;
            const double Eligible = Now - Delay;

            var slow = RegenGain(ratePerSecond: 2.0, delaySeconds: Delay, now: Now);
            var fast = RegenGain(ratePerSecond: 4.0, delaySeconds: Delay, now: Now);

            Assert.Multiple(() =>
            {
                Assert.That(slow, Is.GreaterThan(0.0), "an untouched hero must heal at all");
                Assert.That(slow, Is.LessThanOrEqualTo((2.0 * Eligible) + Tolerance),
                    "healing may not accrue during the 5s grace window");
                Assert.That(fast, Is.LessThanOrEqualTo((4.0 * Eligible) + Tolerance));
                Assert.That(fast, Is.GreaterThan(slow),
                    "RegenHpPerSecond must actually be read from config");
            });
        }

        /// <summary>Regen tops a hero up, it does not overheal. The cap is MaxHp, exactly.</summary>
        [Test]
        public void Regen_never_exceeds_max_hp()
        {
            var sim = RegenSim(out var state, ratePerSecond: 2.0, delaySeconds: 5.0, now: 1000.0);
            var hero = AddHero(state, "hero_saw", HeroClass.Sawbones, hp: 195, maxHp: 200);
            hero.LastDamagedAt = 0.0;

            sim.TickHeroRegen();

            Assert.That(hero.Hp, Is.EqualTo(200.0).Within(Tolerance),
                "regen clamps at MaxHp no matter how long the hero was left alone");
        }

        /// <summary>
        /// "Untouched" is measured from the last hit taken, so being hit restarts the countdown.
        /// The hero here is eligible at t=8, gets hit, and is ticked at t=11 — only 3s untouched.
        /// </summary>
        [Test]
        public void Taking_damage_restarts_the_regen_delay()
        {
            var config = new SimConfig { RegenHpPerSecond = 2.0, RegenDelaySeconds = 5.0 };
            var clock = new SimClock(8.0);
            var sim = SimWith(out var state, config, clock);
            var hero = AddHero(state, "hero_gun", HeroClass.Gunslinger, hp: 60, maxHp: 100);
            hero.LastDamagedAt = 0.0;

            sim.ApplyHeroDamage(Hit("m1", MonsterType.Shambler, 10.0, hero.Id));
            var hpAfterHit = hero.Hp;

            clock.Advance(3.0);
            sim.TickHeroRegen();

            Assert.That(hero.Hp, Is.EqualTo(hpAfterHit).Within(Tolerance),
                "the hit at t=8 restarted the 5s clock, so t=11 is still in combat");
        }

        /// <summary>
        /// A corpse does not heal — coming back is R-33's respawn (full HP at respawn_at), not
        /// regen. Ticked at t=8: past the 5s regen delay, but still short of the 10s respawn, so
        /// the only thing that could move this hero's HP is regen, and it must not.
        /// </summary>
        [Test]
        public void Dead_heroes_do_not_regenerate()
        {
            var config = new SimConfig
            {
                RegenHpPerSecond = 2.0,
                RegenDelaySeconds = 5.0,
                RespawnDelaySeconds = 10.0,
            };
            var clock = new SimClock(0.0);
            var sim = SimWith(out var state, config, clock);
            var hero = AddHero(state, "hero_gun", HeroClass.Gunslinger, hp: 20, maxHp: 100);

            sim.ApplyHeroDamage(Hit("m5", MonsterType.BullBehemoth, 40.0, hero.Id));

            clock.Advance(8.0);
            sim.TickHeroRegen();

            Assert.Multiple(() =>
            {
                Assert.That(hero.Hp, Is.EqualTo(0.0).Within(Tolerance), "a dead hero does not regen");
                Assert.That(hero.Alive, Is.False, "regen must not resurrect anyone");
            });
        }

        // ---- R-33: respawn EXECUTION (G-021 pins only the scheduling half) ------------------------

        /// <summary>
        /// The boundary the whole rule turns on. G-021 pins that a death at t=45 with a 10s delay
        /// records respawn_at 55.0; it cannot show whether t=55 is early enough to be back.
        ///
        /// Pinned as inclusive — the deadline instant revives — to match how this sim already
        /// treats deadlines: G-019 is a boundary fixture that expires a status effect at exactly
        /// its expires_at, and its `defends_against` names strict greater-than as the bug. A
        /// respawn deadline is the same kind of timestamp, so "after 10s" means the hero is back
        /// at now &gt;= RespawnAt, not one tick later.
        /// </summary>
        [TestCase(-2.0, false)]
        [TestCase(-0.1, false)]
        [TestCase(0.0, true)]
        [TestCase(0.1, true)]
        public void Respawn_happens_at_or_after_the_deadline_and_never_before(
            double offsetFromDeadline, bool expectedBack)
        {
            var clock = new SimClock(45.0);
            var sim = SimWith(out var state, RespawnConfig(), clock);
            var hero = AddHero(state, "hero_gun", HeroClass.Gunslinger, hp: 10, maxHp: 100);
            hero.Pos = new Vec2(42, 42);

            var death = sim.ApplyHeroDamage(Hit("m5", MonsterType.BullBehemoth, 40.0, hero.Id));
            Assert.That(death.Downed, Is.True, "premise: the hero died and has a deadline");

            clock.Advance(death.RespawnAt.Value - clock.ElapsedSeconds + offsetFromDeadline);
            sim.TickHeroRespawns();

            if (expectedBack)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(hero.Alive, Is.True, "the deadline has been reached — the hero is back");
                    Assert.That(hero.Hp, Is.EqualTo(100.0).Within(Tolerance));
                });
            }
            else
            {
                Assert.Multiple(() =>
                {
                    Assert.That(hero.Alive, Is.False, "still inside the respawn delay");
                    Assert.That(hero.Hp, Is.EqualTo(0.0).Within(Tolerance));
                    Assert.That(hero.Pos, Is.EqualTo(new Vec2(42, 42)),
                        "a hero that has not respawned has not moved");
                    Assert.That(sim.LastObservation.StateChanges, Is.Empty,
                        "an early tick replicates nothing");
                });
            }
        }

        /// <summary>
        /// R-33 in full: "respawns at the team spawn at full HP after 10s". The spawn point is
        /// deliberately not the (0,0) default, so an implementation that teleports to the origin
        /// cannot pass. Event wording is unpinned by any fixture, so only presence and shape are
        /// asserted — some event must name the hero that came back.
        /// </summary>
        [Test]
        public void Respawn_restores_full_hp_at_the_configured_spawn_point()
        {
            var clock = new SimClock(45.0);
            var sim = SimWith(out var state, RespawnConfig(), clock);
            var hero = AddHero(state, "hero_gun", HeroClass.Gunslinger, hp: 10, maxHp: 100);
            hero.Pos = new Vec2(42, 42);

            sim.ApplyHeroDamage(Hit("m5", MonsterType.BullBehemoth, 40.0, hero.Id));
            clock.Advance(10.0);
            sim.TickHeroRespawns();

            var changes = sim.LastObservation.StateChanges;

            Assert.Multiple(() =>
            {
                Assert.That(hero.Alive, Is.True);
                Assert.That(hero.Hp, Is.EqualTo(hero.MaxHp).Within(Tolerance),
                    "respawn restores full HP, not a partial heal");
                Assert.That(hero.Pos, Is.EqualTo(RespawnPoint),
                    "the hero comes back at SimConfig.RespawnPoint, not where it fell");

                // Alive is the predicate R-16 targeting reads, so the revive must replicate it.
                Assert.That(LivingHeroIds(state), Does.Contain(hero.Id),
                    "a respawned hero is a monster target candidate again");

                Assert.That(changes.Any(c => c.Entity == hero.Id && c.Field == "hp"
                        && Convert.ToDouble(c.To) == 100.0),
                    Is.True, "the HP restore must replicate as a delta");
                Assert.That(changes.Any(c => c.Entity == hero.Id && c.Field == "alive"
                        && Equals(c.To, true)),
                    Is.True, "the alive flag must replicate as a delta");

                Assert.That(sim.LastObservation.EmittedEvents, Is.Not.Empty,
                    "coming back is worth an event; its wording is not pinned here");
                Assert.That(
                    sim.LastObservation.EmittedEvents.Any(
                        e => e.Fields.Values.OfType<string>().Contains(hero.Id)),
                    Is.True, "some emitted event must name the hero that respawned");
            });
        }

        /// <summary>
        /// Deadlines are per hero, not one match-wide timer. Three heroes die three seconds apart
        /// and the tick lands on the second one's exact deadline: the first two are back, the
        /// third — still inside its delay — is not.
        /// </summary>
        [Test]
        public void Respawns_resolve_independently_per_hero()
        {
            var clock = new SimClock(0.0);
            var sim = SimWith(out var state, RespawnConfig(), clock);

            var early = AddHero(state, "hero_early", HeroClass.Gunslinger, hp: 10, maxHp: 100);
            var mid = AddHero(state, "hero_mid", HeroClass.Rancher, hp: 10, maxHp: 120);
            var late = AddHero(state, "hero_late", HeroClass.Sawbones, hp: 10, maxHp: 200);

            sim.ApplyHeroDamage(Hit("m5", MonsterType.BullBehemoth, 40.0, early.Id));   // due at 10
            clock.Advance(3.0);
            sim.ApplyHeroDamage(Hit("m5", MonsterType.BullBehemoth, 40.0, mid.Id));     // due at 13
            clock.Advance(5.0);
            sim.ApplyHeroDamage(Hit("m5", MonsterType.BullBehemoth, 40.0, late.Id));    // due at 18

            clock.Advance(5.0); // t = 13.0 — exactly mid's deadline
            sim.TickHeroRespawns();

            Assert.Multiple(() =>
            {
                Assert.That(early.Alive, Is.True, "past its deadline");
                Assert.That(early.Hp, Is.EqualTo(100.0).Within(Tolerance));
                Assert.That(mid.Alive, Is.True, "exactly on its deadline");
                Assert.That(mid.Hp, Is.EqualTo(120.0).Within(Tolerance),
                    "each hero returns at its own MaxHp");
                Assert.That(late.Alive, Is.False, "still inside its 10s delay");
                Assert.That(late.Hp, Is.EqualTo(0.0).Within(Tolerance));
                Assert.That(LivingHeroIds(state), Does.Not.Contain(late.Id));
            });
        }

        /// <summary>A hero that never died is not the respawn tick's business — it must not be healed or moved.</summary>
        [Test]
        public void Respawn_tick_leaves_living_heroes_alone()
        {
            var sim = SimWith(out var state, RespawnConfig(), new SimClock(500.0));
            var hero = AddHero(state, "hero_saw", HeroClass.Sawbones, hp: 60, maxHp: 200);
            hero.Pos = new Vec2(42, 42);

            sim.TickHeroRespawns();

            Assert.Multiple(() =>
            {
                Assert.That(hero.Hp, Is.EqualTo(60.0).Within(Tolerance),
                    "respawn is not a heal for the living — that is regen's job (R-35)");
                Assert.That(hero.Pos, Is.EqualTo(new Vec2(42, 42)),
                    "a living hero is not teleported to the spawn point");
                Assert.That(hero.Alive, Is.True);
                Assert.That(sim.LastObservation.StateChanges, Is.Empty);
            });
        }

        /// <summary>
        /// The two ticks must not fight. A hero comes back at full HP, so regen has nothing to add
        /// (R-35 heals only up to MaxHp), and a second respawn tick must be a no-op rather than
        /// re-reviving an already-living hero.
        /// </summary>
        [Test]
        public void Respawned_hero_is_stable_under_further_ticks()
        {
            var clock = new SimClock(45.0);
            var sim = SimWith(out var state, RespawnConfig(), clock);
            var hero = AddHero(state, "hero_gun", HeroClass.Gunslinger, hp: 10, maxHp: 100);

            sim.ApplyHeroDamage(Hit("m5", MonsterType.BullBehemoth, 40.0, hero.Id));
            clock.Advance(10.0);
            sim.TickHeroRespawns();

            clock.Advance(30.0);
            sim.TickHeroRespawns();
            var respawnChanges = sim.LastObservation.StateChanges.Count;

            sim.TickHeroRegen();
            var regenChanges = sim.LastObservation.StateChanges.Count;

            Assert.Multiple(() =>
            {
                Assert.That(hero.Hp, Is.EqualTo(100.0).Within(Tolerance),
                    "a hero already at MaxHp cannot be healed further");
                Assert.That(hero.Alive, Is.True);
                Assert.That(respawnChanges, Is.Zero, "reviving a living hero replicates nothing");
                Assert.That(regenChanges, Is.Zero, "regen has nothing to do for a full-HP hero");
            });
        }

        // ---- R-34: no mana ------------------------------------------------------------------------

        /// <summary>
        /// REGRESSION GUARD — this one passes today, and is meant to. R-34 ("heroes have no mana;
        /// cooldowns are the only cast limit") is a structural claim about the hero entity rather
        /// than a behaviour, so it holds against the current empty <see cref="Hero"/> and its job is
        /// to keep holding once ticket 008 wires abilities up. It is red-by-construction only if
        /// someone adds a resource pool.
        /// </summary>
        [Test]
        public void Hero_carries_no_mana_or_other_cast_resource()
        {
            string[] banned = { "mana", "energy", "resource", "stamina", "rage", "focus" };

            var memberNames = typeof(Hero)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property)
                .Select(m => m.Name)
                .ToList();

            foreach (var name in memberNames)
            {
                foreach (var word in banned)
                {
                    Assert.That(name.ToLowerInvariant(), Does.Not.Contain(word),
                        "Hero." + name + " looks like a cast resource; R-34 says cooldowns are the "
                        + "only cast limit");
                }
            }

            // Sanity: the reflection actually saw the type, so an empty result cannot pass vacuously.
            Assert.That(memberNames, Does.Contain("Hp"));
            Assert.That(memberNames, Does.Contain("Alive"));
        }

        // ---- scenario helpers ---------------------------------------------------------------------

        private static MatchSim SimWith(out MatchState state, SimConfig config = null, IClock clock = null)
        {
            state = new MatchState();
            return new MatchSim(state, config ?? new SimConfig(), profileStore: null, clock: clock ?? new SimClock(0.0));
        }

        private static Hero AddHero(MatchState state, string id, string heroClass, double hp, double maxHp)
        {
            var hero = new Hero
            {
                Id = id,
                HeroClass = heroClass,
                AccountId = "acct_" + id,
                Pos = new Vec2(0, 0),
                Hp = hp,
                MaxHp = maxHp,
                Alive = true,
            };

            state.Heroes[id] = hero;
            return hero;
        }

        private static void AddHotspot(MatchState state, string id, int civilians)
        {
            state.Hotspots[id] = new Hotspot { Id = id, Pos = new Vec2(5, 5), Civilians = civilians };
        }

        private static HeroDamageRequest Hit(string attackerId, string attackerType, double damage, string targetId)
        {
            return new HeroDamageRequest
            {
                AttackerId = attackerId,
                AttackerType = attackerType,
                Damage = damage,
                TargetId = targetId,
            };
        }

        /// <summary>
        /// The candidate predicate R-16 reads for the hero half of its target set. Expressed here as
        /// the entity's own <see cref="Hero.Alive"/> flag rather than by calling SelectTarget — the
        /// selection algorithm is ticket 002's, the state it reads is this ticket's.
        /// </summary>
        private static List<string> LivingHeroIds(MatchState state)
        {
            return state.Heroes.Values.Where(h => h.Alive).Select(h => h.Id).ToList();
        }

        /// <summary>
        /// Turns a "kind|kind|kind" spec into entities in the world *and* the nearest-first line the
        /// shell's raycast would hand the sim. Positions advance down +X so the ordering is real.
        /// </summary>
        private static List<LineEntity> BuildLine(MatchState state, string kinds)
        {
            var line = new List<LineEntity>();
            var parts = kinds.Split('|');

            for (var i = 0; i < parts.Length; i++)
            {
                var pos = new Vec2(i + 1, 0);
                string id;
                string lineKind;

                switch (parts[i])
                {
                    case "hero":
                        id = "ally_" + i;
                        lineKind = "hero";
                        AddHero(state, id, HeroClass.Sawbones, hp: 200, maxHp: 200).Pos = pos;
                        break;

                    case "barricade":
                        id = "bar_" + i;
                        lineKind = "barricade";
                        state.Placeables[id] = new Placeable
                        {
                            Id = id, Type = PlaceableType.Barricade, Pos = pos, Hp = 300,
                        };
                        break;

                    case "turret":
                        id = "turret_" + i;
                        lineKind = PlaceableType.Turret;
                        state.Placeables[id] = new Placeable
                        {
                            Id = id, Type = PlaceableType.Turret, Pos = pos, Hp = 150,
                        };
                        break;

                    case "monster":
                        id = "m_" + i;
                        lineKind = "monster";
                        state.Monsters[id] = new Monster
                        {
                            Id = id, Type = MonsterType.Shambler, Pos = pos, Hp = 60, Alive = true,
                        };
                        break;

                    default:
                        throw new ArgumentException("unknown line kind '" + parts[i] + "'", nameof(kinds));
                }

                line.Add(new LineEntity { Id = id, Kind = lineKind, Pos = pos });
            }

            return line;
        }

        /// <summary>Deliberately not the (0,0) default, so a hardcoded origin cannot pass (R-33).</summary>
        private static readonly Vec2 RespawnPoint = new Vec2(7, -3);

        /// <summary>Respawn tuning with a spawn point that is distinguishable from the default.</summary>
        private static SimConfig RespawnConfig()
        {
            return new SimConfig
            {
                RespawnDelaySeconds = 10.0,
                RespawnPoint = RespawnPoint,
                RegenHpPerSecond = 2.0,
                RegenDelaySeconds = 5.0,
            };
        }

        /// <summary>A world whose only interesting axis is regen timing (R-35).</summary>
        private static MatchSim RegenSim(out MatchState state, double ratePerSecond, double delaySeconds, double now)
        {
            var config = new SimConfig
            {
                RegenHpPerSecond = ratePerSecond,
                RegenDelaySeconds = delaySeconds,
            };

            return SimWith(out state, config, new SimClock(now));
        }

        /// <summary>HP a mid-health hero gained from one regen tick. Headroom is large so the cap never binds.</summary>
        private static double RegenGain(double ratePerSecond, double delaySeconds, double now)
        {
            var sim = RegenSim(out var state, ratePerSecond, delaySeconds, now);
            var hero = AddHero(state, "hero_saw", HeroClass.Sawbones, hp: 40, maxHp: 200);
            hero.LastDamagedAt = 0.0;

            sim.TickHeroRegen();

            return hero.Hp - 40.0;
        }

        /// <summary>Every damageable entity's HP, so "nothing else changed" can be asserted whole.</summary>
        private static Dictionary<string, double> SnapshotHp(MatchState state)
        {
            var snapshot = new Dictionary<string, double>();
            foreach (var hero in state.Heroes.Values)
            {
                snapshot[hero.Id] = hero.Hp;
            }

            foreach (var monster in state.Monsters.Values)
            {
                snapshot[monster.Id] = monster.Hp;
            }

            foreach (var placeable in state.Placeables.Values)
            {
                snapshot[placeable.Id] = placeable.Hp;
            }

            return snapshot;
        }

        /// <summary>
        /// Shape assertion, not wording: whatever the events are called, none of them may name an
        /// entity the shot was supposed to pass through. The attacker is excluded — G-030 puts it in
        /// `monster_damaged.by`.
        /// </summary>
        private static void AssertNoEventMentions(MatchSim sim, IEnumerable<string> forbiddenIds, string attackerId)
        {
            var forbidden = forbiddenIds.Where(id => id != attackerId).ToList();

            foreach (var evt in sim.LastObservation.EmittedEvents)
            {
                foreach (var field in evt.Fields)
                {
                    if (field.Value is string value)
                    {
                        Assert.That(forbidden, Does.Not.Contain(value),
                            "event '" + evt.Type + "' names " + value + ", which the shot passed through");
                    }
                }
            }
        }
    }
}
