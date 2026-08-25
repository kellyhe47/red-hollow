using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Ticket 008 (T-08): hero kits, abilities, cooldowns and status effects — R-31, R-32.
    ///
    /// G-018 (lasso applied) and G-019 (lasso expires) are already turned into cases by the
    /// locked golden adapter, so nothing here re-encodes them. Between them they grade exactly
    /// one of the six abilities and none of the cooldown system, which is what this fixture is
    /// for:
    ///
    ///   * R-32 cooldowns — ungraded entirely. Q/E gate independently, per hero, from config,
    ///     and a refused cast must be inert rather than restarting the timer.
    ///   * R-32 rank scaling — ranks cap at <see cref="SimConfig.MaxAbilityRank"/> and each rank
    ///     improves the numbers by "~+25%". The PRD's own wording is approximate, so these tests
    ///     pin direction and bounds, never an exact damage number.
    ///   * R-31 the other five abilities — Fan the Hammer, Deadeye, Stampede, Whirl, Bulwark —
    ///     and the two remaining class passives. Sawbones' flat 30% reduction is ticket 007's and
    ///     is not retested here.
    ///   * R-31/R-43 saved allocations applied at match start.
    ///
    /// Two seam facts that shape everything below:
    ///
    /// 1. <see cref="MatchSim.ApplyAbility"/> is the lasso *effect* and G-018 pins its result to
    ///    three lasso-shaped fields. G-018's caster ("hero_rancher") is not in the fixture's
    ///    match state at all, so that entry point cannot check an unlock or a cooldown for it.
    ///    The gate is therefore <see cref="MatchSim.CastAbility"/>, which every test here uses,
    ///    and every caster in these scenarios is a real hero in <see cref="MatchState.Heroes"/>.
    /// 2. The lasso's numbers stay on <see cref="SimConfig.LassoSlowMultiplier"/> /
    ///    <see cref="SimConfig.LassoDurationSeconds"/> because the fixtures supply them by those
    ///    names; the other five abilities' numbers live in the kit catalog.
    ///
    /// Scenarios are built from production types directly rather than through the fixture JSON
    /// loader — the loader is the adapter's contract with eval/golden, not a test-fixture builder.
    /// Every tunable a test depends on is set to a value that is deliberately NOT the shipped
    /// default, so nothing here can pass against a hardcoded constant.
    /// </summary>
    [TestFixture]
    public class T08_AbilityTests
    {
        private const double Tolerance = 1e-9;

        // ---- R-31 / R-32: the kit table is configuration --------------------------------------

        /// <summary>
        /// The R-31 class table plus R-32's cooldowns, verbatim from the PRD.
        ///
        /// The Rancher's basic is written "12x5 pellets": 12 is the damage quantum the sim is
        /// handed per pellet hit, and the 5-pellet cone is spread the shell resolves before it
        /// calls into <see cref="MatchSim.ResolveHeroAttack"/>. If the implementer reads that row
        /// as 60 damage per trigger-pull instead, that is a spec ambiguity to escalate — not a
        /// number to quietly change on either side.
        /// </summary>
        private static IEnumerable<TestCaseData> KitTable()
        {
            yield return new TestCaseData(
                HeroClass.Gunslinger, 100.0, 25.0, 8.0, 20.0,
                AbilityName.FanTheHammer, AbilityName.Deadeye).SetName("kit_gunslinger");
            yield return new TestCaseData(
                HeroClass.Rancher, 120.0, 12.0, 8.0, 20.0,
                AbilityName.Lasso, AbilityName.Stampede).SetName("kit_rancher");
            yield return new TestCaseData(
                HeroClass.Sawbones, 200.0, 40.0, 8.0, 20.0,
                AbilityName.Whirl, AbilityName.Bulwark).SetName("kit_sawbones");
        }

        /// <summary>
        /// R-31 / R-32: every kit number comes out of <see cref="SimConfig.HeroKits"/>. Asserted
        /// on the catalog rather than through the sim on purpose — the criterion is about where
        /// the numbers live, and a sim-level assertion would pass just as happily against
        /// constants in the ability code. Mirrors T-02's roster test for the R-17 monster table.
        /// </summary>
        [TestCaseSource(nameof(KitTable))]
        public void Configured_hero_kits_match_the_R31_R32_table(
            string heroClass,
            double maxHp,
            double basicDamage,
            double qCooldown,
            double eCooldown,
            string qAbility,
            string eAbility)
        {
            var kit = new SimConfig().HeroKits.KitFor(heroClass);

            Assert.Multiple(() =>
            {
                Assert.That(kit.MaxHp, Is.EqualTo(maxHp).Within(Tolerance), heroClass + " max_hp");
                Assert.That(kit.BasicAttackDamage, Is.EqualTo(basicDamage).Within(Tolerance),
                    heroClass + " basic attack damage");
                Assert.That(kit.QCooldownSeconds, Is.EqualTo(qCooldown).Within(Tolerance),
                    heroClass + " Q cooldown (R-32)");
                Assert.That(kit.ECooldownSeconds, Is.EqualTo(eCooldown).Within(Tolerance),
                    heroClass + " E cooldown (R-32)");
                Assert.That(kit.Q.Name, Is.EqualTo(qAbility), heroClass + " Q ability");
                Assert.That(kit.E.Name, Is.EqualTo(eAbility), heroClass + " E ability");
            });
        }

        /// <summary>R-31 names exactly three classes; a fourth kit is an unspecified hero.</summary>
        [Test]
        public void Hero_kits_hold_exactly_the_three_R31_classes()
        {
            var kits = new SimConfig().HeroKits;

            Assert.That(kits.Classes, Is.EquivalentTo(new[]
            {
                HeroClass.Gunslinger,
                HeroClass.Rancher,
                HeroClass.Sawbones,
            }));
            Assert.That(kits.Count, Is.EqualTo(3));
        }

        /// <summary>
        /// The three ability numbers the PRD states outright, beyond the fixture-locked lasso:
        /// Fan the Hammer's 6-shot burst and Bulwark's 60% reduction for 2s. Everything else
        /// (dash distance, Whirl radius, per-shot damage) the PRD leaves to balance, so nothing
        /// here pins it.
        /// </summary>
        [Test]
        public void The_ability_numbers_the_PRD_states_ship_in_the_kit()
        {
            var kits = new SimConfig().HeroKits;

            Assert.Multiple(() =>
            {
                Assert.That(kits.KitFor(HeroClass.Gunslinger).Q.Hits, Is.EqualTo(6),
                    "Fan the Hammer is a 6-shot burst (R-31)");
                Assert.That(kits.KitFor(HeroClass.Sawbones).E.Magnitude, Is.EqualTo(0.6).Within(Tolerance),
                    "Bulwark is 60% damage reduction (R-31)");
                Assert.That(kits.KitFor(HeroClass.Sawbones).E.DurationSeconds, Is.EqualTo(2.0).Within(Tolerance),
                    "Bulwark lasts 2s (R-31)");
            });
        }

        /// <summary>
        /// R-31's "kit numbers beyond fixture-locked values are config-tunable". The kit only
        /// counts as configuration if a caller can override a number on its own
        /// <see cref="SimConfig"/> and have the change stay there — and stay out of every other
        /// config. A hardcoded constant, or defaults shared through static state, fails one half
        /// of this or the other.
        /// </summary>
        [Test]
        public void Kit_numbers_are_overridable_per_config_instance()
        {
            var tuned = new SimConfig();
            tuned.HeroKits.Set(HeroClass.Sawbones, new HeroKit
            {
                MaxHp = 999.0,
                BasicAttackDamage = 1.0,
                QCooldownSeconds = 0.5,
                ECooldownSeconds = 2.5,
            });

            var tunedKit = tuned.HeroKits.KitFor(HeroClass.Sawbones);
            Assert.Multiple(() =>
            {
                Assert.That(tunedKit.MaxHp, Is.EqualTo(999.0).Within(Tolerance));
                Assert.That(tunedKit.QCooldownSeconds, Is.EqualTo(0.5).Within(Tolerance));
                Assert.That(tunedKit.ECooldownSeconds, Is.EqualTo(2.5).Within(Tolerance));
            });

            Assert.That(new SimConfig().HeroKits.KitFor(HeroClass.Sawbones).MaxHp,
                Is.EqualTo(200.0).Within(Tolerance),
                "one config's override leaked into another; the catalog is shared static state, not config");
        }

        // ---- R-31 + R-43: heroes start a match with their saved allocations --------------------

        /// <summary>
        /// R-31 / R-43. At match start each hero adopts the ranks saved on its player's profile.
        /// Two heroes of the same class, two accounts: the veteran's allocations arrive, the
        /// fresh account's do not exist and stay at zero (R-44 makes an unknown callsign a fresh
        /// account rather than an error).
        /// </summary>
        [Test]
        public void Match_start_applies_each_heros_saved_ability_allocations()
        {
            var store = new InMemoryProfileStore();
            Seed(store, "acct_vet", q: 2, e: 1);

            var sim = SimWith(out var state, PlayableConfig(), new SimClock(0.0), store);
            var veteran = AddHero(state, "hero_vet", HeroClass.Gunslinger, new Vec2(0, 0), "acct_vet");
            var rookie = AddHero(state, "hero_new", HeroClass.Gunslinger, new Vec2(5, 0), "acct_rookie");

            sim.ApplySavedAbilityAllocations();

            Assert.Multiple(() =>
            {
                Assert.That(veteran.Abilities[AbilitySlot.Q], Is.EqualTo(2));
                Assert.That(veteran.Abilities[AbilitySlot.E], Is.EqualTo(1));
                Assert.That(rookie.Abilities[AbilitySlot.Q], Is.EqualTo(0),
                    "a fresh account is basic-attack only (R-31/R-44)");
                Assert.That(rookie.Abilities[AbilitySlot.E], Is.EqualTo(0));
            });
        }

        /// <summary>
        /// R-31: Q/E are locked until unlocked. The observable consequence of the allocations
        /// above is who can actually cast — a veteran's unlocked slot resolves, and the same slot
        /// on a fresh account is refused without touching the world.
        /// </summary>
        [TestCase("acct_vet", "hero_vet", true)]
        [TestCase("acct_rookie", "hero_new", false)]
        public void Only_an_unlocked_slot_casts(string accountId, string heroId, bool expectedAccepted)
        {
            var store = new InMemoryProfileStore();
            Seed(store, "acct_vet", q: 1, e: 0);

            var sim = SimWith(out var state, PlayableConfig(), new SimClock(0.0), store);
            AddHero(state, "hero_vet", HeroClass.Gunslinger, new Vec2(0, 0), "acct_vet");
            AddHero(state, "hero_new", HeroClass.Gunslinger, new Vec2(5, 0), "acct_rookie");
            var monster = AddMonster(state, "m1", new Vec2(1, 0));
            sim.ApplySavedAbilityAllocations();

            var hpBefore = monster.Hp;
            var before = Snapshot(state);
            var outcome = sim.CastAbility(Cast(heroId, AbilitySlot.Q, targetId: monster.Id));

            Assert.That(outcome.Accepted, Is.EqualTo(expectedAccepted));
            if (expectedAccepted)
            {
                Assert.That(monster.Hp, Is.LessThan(hpBefore),
                    "an unlocked Fan the Hammer must actually resolve");
            }
            else
            {
                Assert.That(outcome.RejectionReason, Is.Not.Null.And.Not.Empty,
                    "a refused cast must say why");
                AssertUnchanged(state, before);
                Assert.That(sim.LastObservation.StateChanges, Is.Empty);
            }
        }

        /// <summary>
        /// R-31 versus R-32: "you never learned this" and "it is not ready yet" are different
        /// refusals and the client draws them differently. The exact wording is nobody's
        /// contract, so only their distinctness is pinned.
        /// </summary>
        [Test]
        public void A_locked_slot_and_a_cooling_slot_are_refused_for_different_reasons()
        {
            var store = new InMemoryProfileStore();
            Seed(store, "acct_vet", q: 1, e: 0);

            var sim = SimWith(out var state, PlayableConfig(), new SimClock(0.0), store);
            AddHero(state, "hero_vet", HeroClass.Gunslinger, new Vec2(0, 0), "acct_vet");
            var monster = AddMonster(state, "m1", new Vec2(1, 0));
            sim.ApplySavedAbilityAllocations();

            var locked = sim.CastAbility(Cast("hero_vet", AbilitySlot.E, targetId: monster.Id));
            sim.CastAbility(Cast("hero_vet", AbilitySlot.Q, targetId: monster.Id));
            var cooling = sim.CastAbility(Cast("hero_vet", AbilitySlot.Q, targetId: monster.Id));

            Assert.Multiple(() =>
            {
                Assert.That(locked.Accepted, Is.False, "E was never unlocked");
                Assert.That(cooling.Accepted, Is.False, "Q was just cast");
                Assert.That(locked.RejectionReason, Is.Not.Null.And.Not.Empty);
                Assert.That(cooling.RejectionReason, Is.Not.Null.And.Not.Empty);
                Assert.That(locked.RejectionReason, Is.Not.EqualTo(cooling.RejectionReason),
                    "a locked ability and a cooling ability are different refusals");
            });
        }

        // ---- R-32: cooldowns -------------------------------------------------------------------

        /// <summary>
        /// R-32. A ready slot casts and starts its own cooldown, read from the kit rather than a
        /// constant: these durations are deliberately not the shipped 8s/20s, so an
        /// implementation with the PRD numbers baked in cannot pass.
        /// </summary>
        [TestCase(AbilitySlot.Q, 4.0)]
        [TestCase(AbilitySlot.E, 11.5)]
        public void A_cast_on_a_ready_slot_starts_that_slots_configured_cooldown(
            string slot, double cooldown)
        {
            var clock = new SimClock(100.0);
            var sim = CooldownSim(out _, clock, qCooldown: 4.0, eCooldown: 11.5);

            var outcome = sim.CastAbility(Cast("hero_saw", slot, targetId: "m1"));

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Accepted, Is.True);
                Assert.That(outcome.Slot, Is.EqualTo(slot));
                Assert.That(outcome.CooldownReadyAt, Is.EqualTo(100.0 + cooldown).Within(Tolerance),
                    "cooldown runs from the injected clock plus the kit's duration");
            });
        }

        /// <summary>
        /// R-32. A cast while the slot is cooling is refused and the world is untouched — no
        /// damage, no status, no replicated delta. R-34 makes this the *only* cast limit, so if
        /// it leaks the ability has no cost at all.
        /// </summary>
        [TestCase(AbilitySlot.Q, 4.0)]
        [TestCase(AbilitySlot.E, 11.5)]
        public void A_cast_while_the_slot_is_cooling_is_refused_and_changes_nothing(
            string slot, double cooldown)
        {
            var clock = new SimClock(100.0);
            var sim = CooldownSim(out var state, clock, qCooldown: 4.0, eCooldown: 11.5);
            sim.CastAbility(Cast("hero_saw", slot, targetId: "m1"));

            clock.Advance(cooldown / 2.0);
            var before = Snapshot(state);
            var refused = sim.CastAbility(Cast("hero_saw", slot, targetId: "m1"));

            Assert.Multiple(() =>
            {
                Assert.That(refused.Accepted, Is.False);
                Assert.That(refused.RejectionReason, Is.Not.Null.And.Not.Empty);
                Assert.That(refused.TotalDamage, Is.EqualTo(0.0).Within(Tolerance));
                Assert.That(refused.MonstersAffected, Is.Empty);
                Assert.That(sim.LastObservation.StateChanges, Is.Empty,
                    "a refused cast replicates nothing");
                Assert.That(sim.LastObservation.ExternalCalls, Is.Empty);
            });

            AssertUnchanged(state, before);
        }

        /// <summary>
        /// R-32. A refused cast must not restart or extend the timer it was refused by — spam the
        /// key and the ability comes back no later than if you had waited quietly. The clock here
        /// lands exactly on the original deadline after the refusal, so an implementation that
        /// re-armed the cooldown on rejection refuses this second cast too.
        /// </summary>
        [TestCase(AbilitySlot.Q, 4.0)]
        [TestCase(AbilitySlot.E, 11.5)]
        public void A_refused_cast_leaves_the_running_cooldown_untouched(string slot, double cooldown)
        {
            var clock = new SimClock(100.0);
            var sim = CooldownSim(out _, clock, qCooldown: 4.0, eCooldown: 11.5);
            var first = sim.CastAbility(Cast("hero_saw", slot, targetId: "m1"));

            clock.Advance(cooldown - 1.0);
            var refused = sim.CastAbility(Cast("hero_saw", slot, targetId: "m1"));
            clock.Advance(1.0);
            var afterWaiting = sim.CastAbility(Cast("hero_saw", slot, targetId: "m1"));

            Assert.Multiple(() =>
            {
                Assert.That(refused.Accepted, Is.False);
                Assert.That(refused.CooldownReadyAt, Is.EqualTo(first.CooldownReadyAt).Within(Tolerance),
                    "a refusal reports the deadline that is already running, not a fresh one");
                Assert.That(afterWaiting.Accepted, Is.True,
                    "spamming the key must not push the ability further away");
            });
        }

        /// <summary>
        /// R-32 boundary, pinned to this repo's deadline convention. G-019 expires a status at
        /// exactly its expires_at and names strict greater-than as the bug it defends against;
        /// ticket 007's respawn tick was pinned the same way. So a cast at exactly
        /// cooldown_ready_at is allowed, and the tick before it is not.
        /// </summary>
        [TestCase(AbilitySlot.Q, 4.0, -0.001, false)]
        [TestCase(AbilitySlot.Q, 4.0, 0.0, true)]
        [TestCase(AbilitySlot.E, 11.5, -0.001, false)]
        [TestCase(AbilitySlot.E, 11.5, 0.0, true)]
        public void The_slot_is_castable_again_at_exactly_the_ready_time(
            string slot, double cooldown, double offset, bool expectedAccepted)
        {
            var clock = new SimClock(100.0);
            var sim = CooldownSim(out _, clock, qCooldown: 4.0, eCooldown: 11.5);
            sim.CastAbility(Cast("hero_saw", slot, targetId: "m1"));

            clock.Advance(cooldown + offset);
            var outcome = sim.CastAbility(Cast("hero_saw", slot, targetId: "m1"));

            Assert.That(outcome.Accepted, Is.EqualTo(expectedAccepted),
                "at ready_at" + (offset < 0 ? offset.ToString() : "+0") + " the cast should be "
                + (expectedAccepted ? "allowed" : "refused"));
        }

        /// <summary>
        /// R-32. Two slots, two timers. Casting Q must not put E on cooldown, or the 8s ability
        /// would silently gate the 20s one.
        /// </summary>
        [Test]
        public void Q_and_E_cooldowns_run_independently()
        {
            var clock = new SimClock(100.0);
            var sim = CooldownSim(out _, clock, qCooldown: 4.0, eCooldown: 11.5);

            var q = sim.CastAbility(Cast("hero_saw", AbilitySlot.Q, targetId: "m1"));
            var e = sim.CastAbility(Cast("hero_saw", AbilitySlot.E, targetId: "m1"));

            clock.Advance(4.0);
            var qAgain = sim.CastAbility(Cast("hero_saw", AbilitySlot.Q, targetId: "m1"));
            var eStillCooling = sim.CastAbility(Cast("hero_saw", AbilitySlot.E, targetId: "m1"));

            Assert.Multiple(() =>
            {
                Assert.That(q.Accepted, Is.True);
                Assert.That(e.Accepted, Is.True, "casting Q must not put E on cooldown");
                Assert.That(qAgain.Accepted, Is.True, "Q's shorter cooldown has elapsed");
                Assert.That(eStillCooling.Accepted, Is.False, "E's longer cooldown has not");
            });
        }

        /// <summary>
        /// R-32, the multiplayer-relevant half: cooldowns are per hero. Two Sawbones in the same
        /// lobby (R-31 allows duplicate classes) each own their own timer, so one player casting
        /// must never grey out another player's key.
        /// </summary>
        [Test]
        public void One_heros_cooldown_does_not_gate_another_hero()
        {
            var clock = new SimClock(100.0);
            var sim = CooldownSim(out var state, clock, qCooldown: 4.0, eCooldown: 11.5);

            // Same class, same lobby (R-31 allows duplicates), its own account and its own timer.
            AddHero(state, "hero_saw_2", HeroClass.Sawbones, new Vec2(0, 1), "acct_hero_saw_2");
            sim.ApplySavedAbilityAllocations();

            var first = sim.CastAbility(Cast("hero_saw", AbilitySlot.Q, targetId: "m1"));
            var second = sim.CastAbility(Cast("hero_saw_2", AbilitySlot.Q, targetId: "m1"));
            var firstAgain = sim.CastAbility(Cast("hero_saw", AbilitySlot.Q, targetId: "m1"));

            Assert.Multiple(() =>
            {
                Assert.That(first.Accepted, Is.True);
                Assert.That(second.Accepted, Is.True,
                    "a second hero's Q is its own timer, not the first hero's");
                Assert.That(firstAgain.Accepted, Is.False, "the first hero is still cooling");
            });
        }

        // ---- R-32: rank scaling ----------------------------------------------------------------

        /// <summary>
        /// R-32: "rank-ups (max 3) improve numbers ~+25%/rank". The PRD says *approximately*, so
        /// this pins the relationship — strictly increasing, each step in a band around a quarter,
        /// and the whole rank 1 -> 3 climb bounded — rather than a damage number that would encode
        /// one arithmetic reading of "~" as spec.
        /// </summary>
        [Test]
        public void Each_rank_improves_the_ability_by_roughly_a_quarter()
        {
            var rank1 = FanTheHammer(1).TotalDamage;
            var rank2 = FanTheHammer(2).TotalDamage;
            var rank3 = FanTheHammer(3).TotalDamage;

            Assert.Multiple(() =>
            {
                Assert.That(rank1, Is.GreaterThan(0.0), "rank 1 is the unlocked baseline");
                Assert.That(rank2, Is.GreaterThan(rank1), "rank 2 must be stronger than rank 1");
                Assert.That(rank3, Is.GreaterThan(rank2), "rank 3 must be stronger than rank 2");
                Assert.That(rank2 / rank1, Is.InRange(1.1, 1.5), "rank 1 -> 2 is ~+25%");
                Assert.That(rank3 / rank2, Is.InRange(1.1, 1.5), "rank 2 -> 3 is ~+25%");
                Assert.That(rank3 / rank1, Is.InRange(1.2, 2.0), "rank 1 -> 3 is ~two quarters");
            });
        }

        /// <summary>
        /// R-32 rank scaling is balance data, not arithmetic in the ability code. Two configs
        /// with different curves must produce different steps; the assertion is that the steeper
        /// config steps harder, never that either equals a particular number.
        /// </summary>
        [Test]
        public void Rank_scaling_is_driven_by_config_not_a_hardcoded_multiplier()
        {
            var gentleStep = FanTheHammer(2, rankScaling: 0.10).TotalDamage
                / FanTheHammer(1, rankScaling: 0.10).TotalDamage;
            var steepStep = FanTheHammer(2, rankScaling: 0.50).TotalDamage
                / FanTheHammer(1, rankScaling: 0.50).TotalDamage;

            Assert.That(steepStep, Is.GreaterThan(gentleStep),
                "a steeper configured curve must produce a bigger rank step");
        }

        /// <summary>
        /// R-32 / DEC-014: rank is capped at <see cref="SimConfig.MaxAbilityRank"/>. T-09 owns
        /// refusing the *spend* past the cap; what this pins is the ceiling in effect terms — a
        /// profile carrying a rank above the cap (seeded directly, as a migrated or tampered
        /// profile would be) casts no harder than rank 3.
        /// </summary>
        [Test]
        public void Ability_numbers_stop_improving_at_the_max_rank()
        {
            var atCap = FanTheHammer(3);
            var beyondCap = FanTheHammer(5);

            Assert.Multiple(() =>
            {
                Assert.That(beyondCap.TotalDamage, Is.EqualTo(atCap.TotalDamage).Within(Tolerance),
                    "rank 5 must resolve as rank 3");
                Assert.That(beyondCap.Rank, Is.EqualTo(new SimConfig().MaxAbilityRank));
            });
        }

        // ---- R-31: the six abilities resolve through the sim -----------------------------------

        /// <summary>
        /// R-31 Gunslinger Q. "6-shot burst" is a damage rule in the sim, not an animation: one
        /// cast resolves the kit's <see cref="AbilitySpec.Hits"/> shots' worth of damage on the
        /// target. Per-shot damage is config, which is why it is set here rather than assumed.
        /// </summary>
        [TestCase(10.0, 60.0)]
        [TestCase(7.0, 42.0)]
        public void Fan_the_hammer_resolves_six_shots_worth_of_damage(double perShot, double expectedTotal)
        {
            var config = PlayableConfig();
            var kit = config.HeroKits.KitFor(HeroClass.Gunslinger);
            kit.Q.Damage = perShot;
            kit.Q.Hits = 6;

            var sim = AbilitySim(out var state, config, HeroClass.Gunslinger, "hero_gun", q: 1, e: 1);
            var monster = AddMonster(state, "m1", new Vec2(3, 0), hp: 500.0);

            var outcome = sim.CastAbility(Cast("hero_gun", AbilitySlot.Q, targetId: "m1",
                aim: new Vec2(1, 0), line: Line(monster)));

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Accepted, Is.True);
                Assert.That(outcome.Ability, Is.EqualTo(AbilityName.FanTheHammer));
                Assert.That(outcome.TotalDamage, Is.EqualTo(expectedTotal).Within(Tolerance),
                    "six shots at " + perShot + " each");
                Assert.That(monster.Hp, Is.EqualTo(500.0 - expectedTotal).Within(Tolerance));
                Assert.That(outcome.MonstersAffected, Is.EquivalentTo(new[] { "m1" }));
            });
        }

        /// <summary>
        /// R-31 Gunslinger E. Deadeye is a *piercing* line: every monster the line crossed takes
        /// the hit, where a basic stops at the first (that contrast is asserted here on an
        /// identical line, because "pierces" is only meaningful against the thing it differs
        /// from). R-26/R-36: the ally and the barricade on the same line take nothing — a new
        /// piercing ability is exactly where friendly fire creeps back in.
        /// </summary>
        [Test]
        public void Deadeye_pierces_every_monster_on_the_line_and_damages_nothing_else()
        {
            var config = PlayableConfig();
            config.HeroKits.KitFor(HeroClass.Gunslinger).E.Damage = 33.0;

            var sim = AbilitySim(out var state, config, HeroClass.Gunslinger, "hero_gun", q: 1, e: 1);
            var near = AddMonster(state, "m_near", new Vec2(2, 0), hp: 500.0);
            var ally = AddHero(state, "hero_ally", HeroClass.Sawbones, new Vec2(3, 0), "acct_ally");
            var barricade = AddBarricade(state, "bar_1", new Vec2(4, 0));
            var mid = AddMonster(state, "m_mid", new Vec2(5, 0), hp: 500.0);
            var far = AddMonster(state, "m_far", new Vec2(7, 0), hp: 500.0);

            var line = new List<LineEntity>
            {
                Entity(near.Id, "monster", near.Pos),
                Entity(ally.Id, "hero", ally.Pos),
                Entity(barricade.Id, "barricade", barricade.Pos),
                Entity(mid.Id, "monster", mid.Pos),
                Entity(far.Id, "monster", far.Pos),
            };

            var outcome = sim.CastAbility(Cast("hero_gun", AbilitySlot.E, aim: new Vec2(1, 0), line: line));

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Accepted, Is.True);
                Assert.That(outcome.Ability, Is.EqualTo(AbilityName.Deadeye));
                Assert.That(outcome.MonstersAffected,
                    Is.EquivalentTo(new[] { "m_near", "m_mid", "m_far" }),
                    "a piercing line does not stop at the first monster");
                Assert.That(near.Hp, Is.EqualTo(467.0).Within(Tolerance));
                Assert.That(mid.Hp, Is.EqualTo(467.0).Within(Tolerance));
                Assert.That(far.Hp, Is.EqualTo(467.0).Within(Tolerance));
                Assert.That(ally.Hp, Is.EqualTo(1000.0).Within(Tolerance), "R-26: no friendly fire");
                Assert.That(barricade.Hp, Is.EqualTo(300.0).Within(Tolerance), "R-26: no friendly fire");
                Assert.That(outcome.MonstersAffected, Does.Not.Contain(ally.Id));
                Assert.That(outcome.MonstersAffected, Does.Not.Contain(barricade.Id));
            });

            // The same line fired as a basic stops at the first monster — which is what makes
            // Deadeye's piercing a distinct rule rather than the default behaviour.
            var basicSim = AbilitySim(out var basicState, PlayableConfig(), HeroClass.Gunslinger,
                "hero_gun", q: 1, e: 1);
            var first = AddMonster(basicState, "m_near", new Vec2(2, 0), hp: 500.0);
            var second = AddMonster(basicState, "m_mid", new Vec2(5, 0), hp: 500.0);
            basicSim.ResolveHeroAttack(new HeroAttackRequest
            {
                AttackerId = "hero_gun",
                AttackerClass = HeroClass.Gunslinger,
                Damage = 25.0,
                EntitiesOnLine = new List<LineEntity>
                {
                    Entity(first.Id, "monster", first.Pos),
                    Entity(second.Id, "monster", second.Pos),
                },
            });

            Assert.That(second.Hp, Is.EqualTo(500.0).Within(Tolerance),
                "a Gunslinger basic stops at the first monster; only Deadeye pierces");
        }

        /// <summary>
        /// R-31 Rancher Q, reached through the gate. G-018 grades the lasso's arithmetic, so this
        /// asserts only that <see cref="MatchSim.CastAbility"/> routes to that same config-driven
        /// effect — with a multiplier and a duration that are deliberately NOT the fixture's, so
        /// the two tests cannot be satisfied by the same hardcoded pair of numbers.
        /// </summary>
        [Test]
        public void A_gated_rancher_Q_resolves_the_lasso_effect()
        {
            var config = PlayableConfig();
            config.LassoSlowMultiplier = 0.25;
            config.LassoDurationSeconds = 4.5;

            var sim = AbilitySim(out var state, config, HeroClass.Rancher, "hero_ranch", q: 1, e: 1,
                clock: new SimClock(40.0));
            var monster = AddMonster(state, "m1", new Vec2(2, 0), speed: 8.0);

            var outcome = sim.CastAbility(Cast("hero_ranch", AbilitySlot.Q, targetId: "m1"));

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Accepted, Is.True);
                Assert.That(outcome.Ability, Is.EqualTo(AbilityName.Lasso));
                Assert.That(monster.CurrentSpeed, Is.EqualTo(2.0).Within(Tolerance),
                    "8.0 base speed multiplied by the configured 0.25");
                Assert.That(monster.BaseSpeed, Is.EqualTo(8.0).Within(Tolerance),
                    "the slow multiplies current speed; base speed is what expiry restores");
                Assert.That(outcome.EffectExpiresAt, Is.EqualTo(44.5).Within(Tolerance),
                    "clock plus the configured duration");
                Assert.That(outcome.MonstersAffected, Is.EquivalentTo(new[] { "m1" }));
            });
        }

        /// <summary>
        /// R-31 Rancher E. "Dash + knockback" is displacement: the monsters the dash goes through
        /// end up further along the aim direction than they started. Distances are config the PRD
        /// never states, so this asserts the direction and that something moved, not how far.
        /// R-26/R-36: an ally standing in the lane takes no damage.
        /// </summary>
        [Test]
        public void Stampede_knocks_back_the_monsters_it_dashes_through()
        {
            var config = PlayableConfig();
            config.HeroKits.KitFor(HeroClass.Rancher).E.Radius = 3.0;

            var sim = AbilitySim(out var state, config, HeroClass.Rancher, "hero_ranch", q: 1, e: 1);
            var near = AddMonster(state, "m_near", new Vec2(2, 0), hp: 500.0);
            var ally = AddHero(state, "hero_ally", HeroClass.Sawbones, new Vec2(3, 0), "acct_ally");
            var far = AddMonster(state, "m_far", new Vec2(4, 0), hp: 500.0);

            var line = new List<LineEntity>
            {
                Entity(near.Id, "monster", near.Pos),
                Entity(ally.Id, "hero", ally.Pos),
                Entity(far.Id, "monster", far.Pos),
            };

            var outcome = sim.CastAbility(Cast("hero_ranch", AbilitySlot.E,
                aim: new Vec2(1, 0), line: line));

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Accepted, Is.True);
                Assert.That(outcome.Ability, Is.EqualTo(AbilityName.Stampede));
                Assert.That(outcome.MonstersAffected, Is.EquivalentTo(new[] { "m_near", "m_far" }));
                Assert.That(near.Pos.X, Is.GreaterThan(2.0), "knocked back along the aim direction");
                Assert.That(far.Pos.X, Is.GreaterThan(4.0), "knocked back along the aim direction");
                Assert.That(ally.Hp, Is.EqualTo(1000.0).Within(Tolerance), "R-26: no friendly fire");
                Assert.That(outcome.MonstersAffected, Does.Not.Contain(ally.Id));
            });
        }

        /// <summary>
        /// R-31 Sawbones Q. An AoE spin damages every monster inside the kit's radius and nothing
        /// outside it — and, per R-26/R-36, nothing friendly inside it either. Radius and damage
        /// are config the PRD never states, so the test supplies both and places its monsters
        /// unambiguously inside and outside.
        /// </summary>
        [Test]
        public void Whirl_damages_every_monster_inside_its_radius_and_nothing_else()
        {
            var config = PlayableConfig();
            var kit = config.HeroKits.KitFor(HeroClass.Sawbones);
            kit.Q.Radius = 5.0;
            kit.Q.Damage = 15.0;

            var sim = AbilitySim(out var state, config, HeroClass.Sawbones, "hero_saw", q: 1, e: 1);
            var inFront = AddMonster(state, "m_front", new Vec2(3, 0), hp: 500.0);
            var behind = AddMonster(state, "m_behind", new Vec2(0, -4), hp: 500.0);
            var outside = AddMonster(state, "m_outside", new Vec2(9, 0), hp: 500.0);
            var ally = AddHero(state, "hero_ally", HeroClass.Gunslinger, new Vec2(1, 1), "acct_ally");
            var barricade = AddBarricade(state, "bar_1", new Vec2(2, 2));

            var outcome = sim.CastAbility(Cast("hero_saw", AbilitySlot.Q));

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Accepted, Is.True);
                Assert.That(outcome.Ability, Is.EqualTo(AbilityName.Whirl));
                Assert.That(outcome.MonstersAffected,
                    Is.EquivalentTo(new[] { "m_front", "m_behind" }),
                    "the spin reaches everything within the radius, in every direction");
                Assert.That(inFront.Hp, Is.EqualTo(485.0).Within(Tolerance));
                Assert.That(behind.Hp, Is.EqualTo(485.0).Within(Tolerance));
                Assert.That(outside.Hp, Is.EqualTo(500.0).Within(Tolerance), "outside the radius");
                Assert.That(ally.Hp, Is.EqualTo(1000.0).Within(Tolerance), "R-26: no friendly fire");
                Assert.That(barricade.Hp, Is.EqualTo(300.0).Within(Tolerance), "R-26: no friendly fire");
                Assert.That(outcome.TotalDamage, Is.EqualTo(30.0).Within(Tolerance));
            });
        }

        /// <summary>
        /// R-31 Sawbones E. Bulwark is 60% damage reduction, so a hit taken while it is up costs
        /// at most 40% of the incoming hit. How it composes with the class's flat 30% passive is
        /// NOT stated by the PRD (multiplicative, additive, or replacing), so only the ceiling and
        /// the direction are pinned — every reading satisfies both.
        /// </summary>
        [Test]
        public void Bulwark_reduces_incoming_damage_while_it_is_active()
        {
            var config = PlayableConfig();
            var kit = config.HeroKits.KitFor(HeroClass.Sawbones);
            kit.E.Magnitude = 0.6;
            kit.E.DurationSeconds = 2.5;

            var sim = AbilitySim(out var state, config, HeroClass.Sawbones, "hero_saw", q: 1, e: 1,
                clock: new SimClock(10.0));
            AddHero(state, "hero_bare", HeroClass.Sawbones, new Vec2(9, 0), "acct_bare");

            var cast = sim.CastAbility(Cast("hero_saw", AbilitySlot.E));
            var guarded = sim.ApplyHeroDamage(Hit("m_atk", 100.0, "hero_saw")).DamageTaken;
            var unguarded = sim.ApplyHeroDamage(Hit("m_atk", 100.0, "hero_bare")).DamageTaken;

            Assert.Multiple(() =>
            {
                Assert.That(cast.Accepted, Is.True);
                Assert.That(cast.Ability, Is.EqualTo(AbilityName.Bulwark));
                Assert.That(cast.EffectExpiresAt, Is.EqualTo(12.5).Within(Tolerance),
                    "clock plus the configured duration");
                Assert.That(guarded, Is.LessThan(unguarded), "Bulwark must actually reduce the hit");
                Assert.That(guarded, Is.LessThanOrEqualTo(40.0 + Tolerance),
                    "60% reduction means at most 40% of the incoming 100 gets through");
            });
        }

        /// <summary>
        /// R-31 Bulwark expiry, on the same inclusive boundary G-019 pins for the lasso: the tick
        /// AT expires_at removes the effect. One tick earlier the hit is still reduced; at the
        /// deadline the Sawbones is back to its bare 30% passive (floor(100 * 0.7) = 70).
        /// </summary>
        [Test]
        public void Bulwark_expires_at_exactly_its_deadline_not_a_tick_later()
        {
            var config = PlayableConfig();
            var kit = config.HeroKits.KitFor(HeroClass.Sawbones);
            kit.E.Magnitude = 0.6;
            kit.E.DurationSeconds = 2.5;

            var clock = new SimClock(10.0);
            var sim = AbilitySim(out var state, config, HeroClass.Sawbones, "hero_saw", q: 1, e: 1,
                clock: clock);
            sim.CastAbility(Cast("hero_saw", AbilitySlot.E));

            clock.Advance(2.499);
            sim.TickStatusEffects();
            var stillGuarded = sim.ApplyHeroDamage(Hit("m_atk", 100.0, "hero_saw")).DamageTaken;

            clock.Advance(0.001); // sim_elapsed is now exactly the 12.5 deadline
            var tick = sim.TickStatusEffects();
            var afterExpiry = sim.ApplyHeroDamage(Hit("m_atk", 100.0, "hero_saw")).DamageTaken;

            Assert.Multiple(() =>
            {
                Assert.That(stillGuarded, Is.LessThanOrEqualTo(40.0 + Tolerance),
                    "still guarded a thousandth of a second before the deadline");
                Assert.That(tick.Expired.Select(e => e.TargetId), Does.Contain("hero_saw"),
                    "the effect expires AT its deadline, not after it (G-019's convention)");
                Assert.That(afterExpiry, Is.EqualTo(70.0).Within(Tolerance),
                    "back to the bare Sawbones passive: floor(100 * 0.7)");
                Assert.That(state.Heroes["hero_saw"].StatusEffects, Is.Empty,
                    "an expired effect is removed, not left to linger");
            });
        }

        // ---- R-31: the two remaining class passives --------------------------------------------

        /// <summary>
        /// R-31 Gunslinger passive: every 4th basic crits for double. Eight shots, so the 4th and
        /// the 8th both land — a "crit once then never again" counter fails on the 8th, and a
        /// "crit every shot" reading fails on the 1st. Class-conditional: the same eight shots
        /// from a Rancher never crit.
        /// </summary>
        [TestCase(HeroClass.Gunslinger, 2.0)]
        [TestCase(HeroClass.Rancher, 1.0)]
        public void Every_fourth_gunslinger_basic_crits_for_double(
            string heroClass, double fourthShotMultiplier)
        {
            var sim = AbilitySim(out var state, PlayableConfig(), heroClass, "hero_shooter", q: 1, e: 1);
            var monster = AddMonster(state, "m1", new Vec2(3, 0), hp: 10000.0);

            var damagePerShot = new List<double>();
            for (var shot = 1; shot <= 8; shot++)
            {
                damagePerShot.Add(sim.ResolveHeroAttack(new HeroAttackRequest
                {
                    AttackerId = "hero_shooter",
                    AttackerClass = heroClass,
                    Damage = 25.0,
                    EntitiesOnLine = Line(monster),
                }).DamageDealt);
            }

            var expected = new[]
            {
                25.0, 25.0, 25.0, 25.0 * fourthShotMultiplier,
                25.0, 25.0, 25.0, 25.0 * fourthShotMultiplier,
            };

            Assert.That(damagePerShot, Is.EqualTo(expected).Within(Tolerance),
                heroClass + " shots 1-8; only the Gunslinger's 4th and 8th double");
        }

        /// <summary>
        /// R-31's crit counter is per hero — two Gunslingers in the same lobby each count their
        /// own basics. A counter kept on the sim rather than the shooter would make the second
        /// player's first shot crit off the first player's three.
        /// </summary>
        [Test]
        public void The_crit_counter_is_kept_per_hero()
        {
            var sim = AbilitySim(out var state, PlayableConfig(), HeroClass.Gunslinger, "hero_a",
                q: 1, e: 1);
            AddHero(state, "hero_b", HeroClass.Gunslinger, new Vec2(0, 2), "acct_b");
            var monster = AddMonster(state, "m1", new Vec2(3, 0), hp: 10000.0);

            for (var shot = 1; shot <= 3; shot++)
            {
                Shoot(sim, "hero_a", HeroClass.Gunslinger, monster);
            }

            var otherHerosFirstShot = Shoot(sim, "hero_b", HeroClass.Gunslinger, monster);
            var thisHerosFourthShot = Shoot(sim, "hero_a", HeroClass.Gunslinger, monster);

            Assert.Multiple(() =>
            {
                Assert.That(otherHerosFirstShot, Is.EqualTo(25.0).Within(Tolerance),
                    "hero_b's first basic must not inherit hero_a's count");
                Assert.That(thisHerosFourthShot, Is.EqualTo(50.0).Within(Tolerance),
                    "hero_a's own 4th basic still crits");
            });
        }

        /// <summary>
        /// R-31 Rancher passive: basics hit up to 2 targets — and not 3. The ally standing second
        /// in the line is not one of the two (R-26/R-36), so the pair is the first two *monsters*,
        /// not the first two entities. The Gunslinger case is the control: one target only.
        /// </summary>
        [TestCase(HeroClass.Rancher, 2)]
        [TestCase(HeroClass.Gunslinger, 1)]
        public void Rancher_basics_hit_up_to_two_monsters(string heroClass, int expectedMonstersHit)
        {
            var sim = AbilitySim(out var state, PlayableConfig(), heroClass, "hero_shooter", q: 1, e: 1);
            var first = AddMonster(state, "m1", new Vec2(2, 0), hp: 500.0);
            var ally = AddHero(state, "hero_ally", HeroClass.Sawbones, new Vec2(3, 0), "acct_ally");
            var second = AddMonster(state, "m2", new Vec2(4, 0), hp: 500.0);
            var third = AddMonster(state, "m3", new Vec2(6, 0), hp: 500.0);

            sim.ResolveHeroAttack(new HeroAttackRequest
            {
                AttackerId = "hero_shooter",
                AttackerClass = heroClass,
                Damage = 12.0,
                EntitiesOnLine = new List<LineEntity>
                {
                    Entity(first.Id, "monster", first.Pos),
                    Entity(ally.Id, "hero", ally.Pos),
                    Entity(second.Id, "monster", second.Pos),
                    Entity(third.Id, "monster", third.Pos),
                },
            });

            var damaged = new[] { first, second, third }.Count(m => m.Hp < 500.0);

            Assert.Multiple(() =>
            {
                Assert.That(damaged, Is.EqualTo(expectedMonstersHit),
                    heroClass + " basics should hit " + expectedMonstersHit + " monster(s)");
                Assert.That(first.Hp, Is.EqualTo(488.0).Within(Tolerance),
                    "the nearest monster is hit whatever the class");
                Assert.That(third.Hp, Is.EqualTo(500.0).Within(Tolerance),
                    "never a third target");
                Assert.That(ally.Hp, Is.EqualTo(1000.0).Within(Tolerance), "R-26: no friendly fire");
            });
        }

        // ---- sad paths -------------------------------------------------------------------------

        /// <summary>
        /// A slot nobody has is a malformed command, not a crash and not a free cast. Shape only:
        /// the rejection wording is nobody's contract (T-09's "invalid_choice" is the precedent
        /// but not a promise this operation repeats).
        /// </summary>
        [TestCase("R")]
        [TestCase("lasso")]
        [TestCase("")]
        [TestCase((string)null)]
        public void An_unknown_ability_slot_is_refused_without_changing_anything(string slot)
        {
            var clock = new SimClock(100.0);
            var sim = CooldownSim(out var state, clock, qCooldown: 4.0, eCooldown: 11.5);
            var before = Snapshot(state);

            AbilityCastOutcome outcome = null;
            Assert.DoesNotThrow(() => outcome = sim.CastAbility(Cast("hero_saw", slot, targetId: "m1")));

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Accepted, Is.False);
                Assert.That(outcome.RejectionReason, Is.Not.Null.And.Not.Empty);
                Assert.That(sim.LastObservation.StateChanges, Is.Empty);
            });

            AssertUnchanged(state, before);
        }

        /// <summary>
        /// A cast aimed at a target that is gone or already dead must corrupt nothing. Whether
        /// the sim refuses such a cast outright or accepts it as a whiff is left open — the PRD
        /// says neither — so only non-corruption and "the corpse was not affected" are asserted.
        /// </summary>
        [TestCase("no_such_monster", TestName = "missing_target")]
        [TestCase("m_dead", TestName = "dead_target")]
        public void A_cast_at_a_missing_or_dead_target_corrupts_nothing(string targetId)
        {
            var config = PlayableConfig();
            config.LassoSlowMultiplier = 0.25;

            var sim = AbilitySim(out var state, config, HeroClass.Rancher, "hero_ranch", q: 1, e: 1);
            var corpse = AddMonster(state, "m_dead", new Vec2(2, 0), hp: 0.0);
            corpse.Alive = false;
            var before = Snapshot(state);

            AbilityCastOutcome outcome = null;
            Assert.DoesNotThrow(() =>
                outcome = sim.CastAbility(Cast("hero_ranch", AbilitySlot.Q, targetId: targetId)));

            Assert.Multiple(() =>
            {
                Assert.That(outcome.MonstersAffected, Does.Not.Contain("m_dead"),
                    "R-16/R-33: a corpse is not a valid target");
                Assert.That(outcome.MonstersAffected, Does.Not.Contain(targetId));
                Assert.That(corpse.Alive, Is.False, "nothing here resurrects a monster");
            });

            AssertUnchanged(state, before);
        }

        /// <summary>
        /// R-33: dead heroes are untargetable and spectate. They also do not cast — a corpse
        /// pressing Q must change nothing at all, including its own cooldowns.
        /// </summary>
        [TestCase(AbilitySlot.Q)]
        [TestCase(AbilitySlot.E)]
        public void A_dead_hero_cannot_cast(string slot)
        {
            var clock = new SimClock(100.0);
            var sim = CooldownSim(out var state, clock, qCooldown: 4.0, eCooldown: 11.5);
            var hero = state.Heroes["hero_saw"];
            hero.Alive = false;
            hero.Hp = 0.0;
            hero.RespawnAt = 110.0;

            var before = Snapshot(state);
            var outcome = sim.CastAbility(Cast("hero_saw", slot, targetId: "m1"));

            Assert.Multiple(() =>
            {
                Assert.That(outcome.Accepted, Is.False);
                Assert.That(outcome.RejectionReason, Is.Not.Null.And.Not.Empty);
                Assert.That(outcome.MonstersAffected, Is.Empty);
                Assert.That(sim.LastObservation.StateChanges, Is.Empty);
                Assert.That(hero.CooldownReadyAt.Values, Is.All.LessThanOrEqualTo(100.0),
                    "a refused cast from a corpse must not burn the cooldown either");
            });

            AssertUnchanged(state, before);
        }

        // ---- builders --------------------------------------------------------------------------

        /// <summary>
        /// Ability numbers that are deliberately NOT the shipped defaults, so no test below can
        /// pass against a constant baked into the ability code. Individual tests overwrite the
        /// rows they actually assert on.
        /// </summary>
        private static SimConfig PlayableConfig()
        {
            var config = new SimConfig
            {
                LassoSlowMultiplier = 0.25,   // not the fixture-locked 0.5
                LassoDurationSeconds = 4.5,   // not the fixture-locked 3.0
            };

            var gunslinger = config.HeroKits.KitFor(HeroClass.Gunslinger);
            gunslinger.Q.Damage = 10.0;
            gunslinger.Q.Hits = 6;
            gunslinger.E.Damage = 33.0;

            var rancher = config.HeroKits.KitFor(HeroClass.Rancher);
            rancher.E.Radius = 3.0;

            var sawbones = config.HeroKits.KitFor(HeroClass.Sawbones);
            sawbones.Q.Radius = 5.0;
            sawbones.Q.Damage = 15.0;
            sawbones.E.Magnitude = 0.6;
            sawbones.E.DurationSeconds = 2.5;   // not the PRD's 2.0, to prove it is read from config

            return config;
        }

        private static MatchSim SimWith(
            out MatchState state, SimConfig config, IClock clock, IProfileStore store)
        {
            state = new MatchState();
            return new MatchSim(state, config, store, clock, null);
        }

        /// <summary>
        /// One hero of <paramref name="heroClass"/> with the given allocations already applied,
        /// which is the state R-31/R-43 says a match starts in.
        /// </summary>
        private static MatchSim AbilitySim(
            out MatchState state,
            SimConfig config,
            string heroClass,
            string heroId,
            int q,
            int e,
            SimClock clock = null)
        {
            var accountId = "acct_" + heroId;
            var store = new InMemoryProfileStore();
            Seed(store, accountId, q, e);

            var sim = SimWith(out state, config, clock ?? new SimClock(0.0), store);
            AddHero(state, heroId, heroClass, new Vec2(0, 0), accountId);
            sim.ApplySavedAbilityAllocations();
            return sim;
        }

        /// <summary>
        /// The standard cooldown scenario: a Sawbones with both slots unlocked, a monster inside
        /// Whirl's radius so Q always has something to resolve on, and cooldowns that are not the
        /// PRD's 8s/20s.
        /// </summary>
        private static MatchSim CooldownSim(
            out MatchState state, SimClock clock, double qCooldown, double eCooldown)
        {
            var config = PlayableConfig();
            var kit = config.HeroKits.KitFor(HeroClass.Sawbones);
            kit.QCooldownSeconds = qCooldown;
            kit.ECooldownSeconds = eCooldown;

            var store = new InMemoryProfileStore();
            Seed(store, "acct_hero_saw", q: 1, e: 1);
            Seed(store, "acct_hero_saw_2", q: 1, e: 1);

            var sim = SimWith(out state, config, clock, store);
            AddHero(state, "hero_saw", HeroClass.Sawbones, new Vec2(0, 0), "acct_hero_saw");
            AddMonster(state, "m1", new Vec2(1, 0));
            sim.ApplySavedAbilityAllocations();
            return sim;
        }

        /// <summary>
        /// One Fan the Hammer cast at <paramref name="rank"/>. Per-shot damage is fixed so the
        /// only thing varying across ranks is the rank scaling itself; the curve comes from
        /// config, defaulting to whatever the shipped kit carries.
        /// </summary>
        private static AbilityCastOutcome FanTheHammer(int rank, double? rankScaling = null)
        {
            var config = new SimConfig();
            var kit = config.HeroKits.KitFor(HeroClass.Gunslinger);
            kit.Q.Damage = 10.0;
            kit.Q.Hits = 6;
            if (rankScaling.HasValue)
            {
                kit.Q.RankScalingPerRank = rankScaling.Value;
            }

            var store = new InMemoryProfileStore();
            Seed(store, "acct_rank", q: rank, e: 0);

            var sim = SimWith(out var state, config, new SimClock(0.0), store);
            AddHero(state, "hero_gun", HeroClass.Gunslinger, new Vec2(0, 0), "acct_rank");
            AddMonster(state, "m1", new Vec2(3, 0), hp: 10000.0);
            sim.ApplySavedAbilityAllocations();

            return sim.CastAbility(Cast("hero_gun", AbilitySlot.Q, targetId: "m1",
                aim: new Vec2(1, 0), line: Line(state.Monsters["m1"])));
        }

        private static void Seed(InMemoryProfileStore store, string accountId, int q, int e)
        {
            var profile = new AccountProfile { AccountId = accountId, Level = 5 };
            profile.Abilities[AbilitySlot.Q] = q;
            profile.Abilities[AbilitySlot.E] = e;
            store.Seed(profile);
        }

        private static Hero AddHero(
            MatchState state, string id, string heroClass, Vec2 pos, string accountId)
        {
            // HP headroom is deliberate: no test here is about dying, and a hero that fell over
            // mid-scenario would change what the assertions mean.
            var hero = new Hero
            {
                Id = id,
                HeroClass = heroClass,
                AccountId = accountId,
                Pos = pos,
                Hp = 1000.0,
                MaxHp = 1000.0,
                Alive = true,
            };

            state.Heroes[id] = hero;
            return hero;
        }

        private static Monster AddMonster(
            MatchState state, string id, Vec2 pos, double hp = 500.0, double speed = 5.0)
        {
            var monster = new Monster
            {
                Id = id,
                Type = MonsterType.Ravager,
                Pos = pos,
                Hp = hp,
                Alive = true,
                BaseSpeed = speed,
                CurrentSpeed = speed,
            };

            state.Monsters[id] = monster;
            return monster;
        }

        private static Placeable AddBarricade(MatchState state, string id, Vec2 pos)
        {
            var placeable = new Placeable
            {
                Id = id,
                Type = PlaceableType.Barricade,
                Pos = pos,
                Hp = 300.0,
                Exists = true,
            };

            state.Placeables[id] = placeable;
            return placeable;
        }

        private static LineEntity Entity(string id, string kind, Vec2 pos)
        {
            return new LineEntity { Id = id, Kind = kind, Pos = pos };
        }

        private static List<LineEntity> Line(params Monster[] monsters)
        {
            return monsters.Select(m => Entity(m.Id, "monster", m.Pos)).ToList();
        }

        /// <summary>
        /// Every geometry field is filled in wherever a test can supply it: which of them a given
        /// ability reads is the implementer's choice (a burst aimed at one monster and a burst
        /// down a line are both defensible readings of R-31), and these tests are about the
        /// damage rule, not about the plumbing that delivered the aim.
        /// </summary>
        private static HeroAbilityRequest Cast(
            string casterId,
            string slot,
            string targetId = null,
            Vec2 aim = default,
            List<LineEntity> line = null)
        {
            return new HeroAbilityRequest
            {
                CasterId = casterId,
                Slot = slot,
                TargetId = targetId,
                AimDirection = aim,
                EntitiesOnLine = line ?? new List<LineEntity>(),
            };
        }

        private static HeroDamageRequest Hit(string attackerId, double damage, string targetId)
        {
            return new HeroDamageRequest
            {
                AttackerId = attackerId,
                AttackerType = MonsterType.Ravager,
                Damage = damage,
                TargetId = targetId,
            };
        }

        private static double Shoot(MatchSim sim, string heroId, string heroClass, Monster target)
        {
            return sim.ResolveHeroAttack(new HeroAttackRequest
            {
                AttackerId = heroId,
                AttackerClass = heroClass,
                Damage = 25.0,
                EntitiesOnLine = Line(target),
            }).DamageDealt;
        }

        // ---- non-corruption ----------------------------------------------------------------------

        /// <summary>
        /// Every field a refused or whiffed cast could plausibly corrupt, in one comparable value
        /// per entity. "Changes nothing" is a whole-world claim, so it is asserted as one.
        /// </summary>
        private static Dictionary<string, string> Snapshot(MatchState state)
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var monster in state.Monsters.Values)
            {
                snapshot[monster.Id] = string.Join("|",
                    monster.Hp, monster.CurrentSpeed, monster.BaseSpeed, monster.Pos,
                    monster.Alive, monster.StatusEffects.Count);
            }

            foreach (var hero in state.Heroes.Values)
            {
                snapshot[hero.Id] = string.Join("|",
                    hero.Hp, hero.Pos, hero.Alive, hero.StatusEffects.Count,
                    hero.Abilities[AbilitySlot.Q], hero.Abilities[AbilitySlot.E]);
            }

            foreach (var placeable in state.Placeables.Values)
            {
                snapshot[placeable.Id] = string.Join("|", placeable.Hp, placeable.Exists);
            }

            return snapshot;
        }

        private static void AssertUnchanged(MatchState state, Dictionary<string, string> before)
        {
            Assert.That(Snapshot(state), Is.EqualTo(before), "the cast changed the world");
        }
    }
}
