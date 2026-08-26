using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Ticket 014 (T-14): the sim half of the locked v1 non-goals — R-70 (no PvP), R-71 (no
    /// cross-match meta-economy) and R-73 (single map, no boss archetype, no difficulty settings).
    /// Grades no golden fixture: a non-goal has no behaviour to fixture, which is exactly why it
    /// needs a guard — the PRD's section 11 is a list of features that must NOT ship, and nothing
    /// else in the suite would go red if one quietly did.
    ///
    /// <b>These tests are expected to be green on arrival, and that is the point.</b> They assert
    /// ABSENCE: each one is written so that the first commit that adds the feature — a hero-vs-hero
    /// damage path, a currency field on the persistent profile, a second map factory, a boss row in
    /// the roster, a difficulty knob on the config — fails a test that names the non-goal it broke.
    ///
    /// <b>How each guard is kept non-vacuous.</b> Behavioural guards carry an anti-vacuity arm (the
    /// same operation, aimed at a legal target, does land — so "the hero was not damaged" is a
    /// refusal, not a broken harness). Reflection guards scan the real, current member surface
    /// (asserted non-empty, with a floor on how many members were actually seen) and first prove
    /// their own pattern by matching it against the names the feature would plausibly arrive under
    /// — so a scan that could never match anything fails its own self-test before it gets to
    /// certify anything absent.
    ///
    /// The session-surface half of the non-goals (no host migration, no mid-match join, no
    /// spectator seat — R-70's netcode clauses live on <c>NetSession</c>, which this test project
    /// does not compile) is guarded in the EditMode suite: T14_NonGoalTests.cs.
    /// </summary>
    [TestFixture]
    public class T14_NonGoalGuardTests
    {
        private const double Tolerance = 1e-9;

        // ======================================================================================
        //  R-70 — no PvP
        // ======================================================================================

        /// <summary>
        /// R-70 / R-26 / R-36. A hero attack whose aim line crosses another hero FIRST — nearer
        /// than the monster behind it — damages the monster and leaves the hero untouched.
        ///
        /// This is the sharpest arrangement the rule can be put in: the other hero is the nearest
        /// entity on the line, so any implementation that resolved "first thing hit" instead of
        /// "first MONSTER hit" — which is precisely what a PvP mode would change — damages the
        /// hero and fails here. The monster behind is the anti-vacuity arm: the same shot, the
        /// same line, and it does land on the one kind of thing R-36 allows.
        /// </summary>
        [Test]
        public void A_hero_attack_passes_through_a_nearer_hero_and_damages_only_the_monster_behind_it()
        {
            var sim = SimWithTwoHeroesAndAMonster(
                out var shooter, out var bystander, out var monster);

            var bystanderHpBefore = bystander.Hp;
            var monsterHpBefore = monster.Hp;

            var result = sim.ResolveHeroAttack(new HeroAttackRequest
            {
                AttackerId = shooter.Id,
                AttackerClass = shooter.HeroClass,
                Damage = 25.0,
                EntitiesOnLine = new List<LineEntity>
                {
                    // Nearest first, exactly as the shell's raycast reports: the friendly hero is
                    // standing in front of the monster.
                    new LineEntity { Id = bystander.Id, Kind = "hero", Pos = bystander.Pos },
                    new LineEntity { Id = monster.Id, Kind = "monster", Pos = monster.Pos },
                },
            });

            Assert.That(bystander.Hp, Is.EqualTo(bystanderHpBefore).Within(Tolerance),
                "R-70: no PvP — a hero standing nearest on another hero's aim line takes nothing. "
                + "A hit here means the attack resolved 'first entity' rather than 'first monster', "
                + "which is the exact code change a PvP mode would make");

            Assert.That(result.HitId, Is.EqualTo(monster.Id),
                "anti-vacuity (R-36): the same shot lands on the monster behind the hero, so the "
                + "assertion above is about a hero being passed over, not about a shot that missed "
                + "everything");
            Assert.That(monster.Hp, Is.LessThan(monsterHpBefore),
                "anti-vacuity (R-36): and really damaged it");
        }

        /// <summary>
        /// R-70 / R-26. An aim line that crossed ONLY another hero is a clean miss: no hit, no
        /// damage dealt, the hero untouched. Separated from the pass-through test because a PvP
        /// fallback could hide behind it — "damage the first monster, else the first hero" passes
        /// the test above and fails this one.
        /// </summary>
        [Test]
        public void A_hero_attack_that_crosses_only_another_hero_is_a_clean_miss()
        {
            var sim = SimWithTwoHeroesAndAMonster(
                out var shooter, out var bystander, out _);

            var bystanderHpBefore = bystander.Hp;

            var result = sim.ResolveHeroAttack(new HeroAttackRequest
            {
                AttackerId = shooter.Id,
                AttackerClass = shooter.HeroClass,
                Damage = 25.0,
                EntitiesOnLine = new List<LineEntity>
                {
                    new LineEntity { Id = bystander.Id, Kind = "hero", Pos = bystander.Pos },
                },
            });

            Assert.That(result.HitId, Is.Null,
                "R-70: a line holding no monster hits nothing — there is no hero-as-fallback-target "
                + "path, because there is no PvP");
            Assert.That(result.DamageDealt, Is.EqualTo(0.0).Within(Tolerance),
                "R-70: and deals nothing");
            Assert.That(bystander.Hp, Is.EqualTo(bystanderHpBefore).Within(Tolerance),
                "R-70: the hero on the line is untouched");
        }

        /// <summary>
        /// R-70 / R-26 / DEC-019. The friendly-fire knob ships OFF, and there is no shipped mode in
        /// which it is on — the PRD defines no other branch. A default flipped to true is the
        /// cheapest way PvP could arrive without a new API, so the default is pinned.
        /// </summary>
        [Test]
        public void Friendly_fire_ships_off()
        {
            Assert.That(new SimConfig().FriendlyFire, Is.False,
                "R-70 / R-26: hero attacks never damage heroes or placeables, and the config "
                + "default must say so — v1 ships no mode where FriendlyFire is true");
        }

        /// <summary>
        /// R-70. No operation on the sim's public surface is named as a hero-vs-hero mode. The
        /// behavioural guards above pin the one attack path that exists today; this scan is the
        /// tripwire for a NEW path arriving beside it under its own name.
        /// </summary>
        [Test]
        public void The_sim_surface_names_no_pvp_operation()
        {
            AssertNoPublicMemberMatches(
                new[] { typeof(MatchSim), typeof(SimConfig) },
                pattern: "pvp|duel|deathmatch|versusmode|herovshero",
                minimumMembersScanned: 20,
                why: "R-70: no PvP in v1",
                wouldMatch: new[] { "ResolvePvpAttack", "EnterDuel", "PvpEnabled", "HeroVsHeroDamage" });
        }

        // ======================================================================================
        //  R-71 — no meta-economy across matches
        // ======================================================================================

        /// <summary>
        /// R-71. The persistent account profile — the ONLY container that outlives a match (R-43)
        /// — carries XP, level, skill points and ability ranks, and no currency of any kind.
        /// Scrip that survived a match would have to be stored here, so a currency-shaped member
        /// appearing on this type is the meta-economy arriving, whatever it is called.
        ///
        /// The scan proves itself against the names such a field would plausibly take before
        /// certifying anything absent, and the progression members R-43 DOES require are asserted
        /// present — so this cannot pass against an empty or renamed type.
        /// </summary>
        [Test]
        public void The_persistent_profile_carries_progression_and_no_currency()
        {
            var members = PublicMemberNames(typeof(AccountProfile));

            // The surface R-43 requires is really there (the scan is looking at the right type).
            Assert.That(members, Does.Contain("LifetimeXp"), "R-43: lifetime XP persists");
            Assert.That(members, Does.Contain("Level"), "R-43: level persists");
            Assert.That(members, Does.Contain("SkillPoints"), "R-42/R-43: skill points persist");
            Assert.That(members, Does.Contain("Abilities"), "R-43: ability allocations persist");

            AssertNoPublicMemberMatches(
                new[] { typeof(AccountProfile) },
                pattern: "scrip|currenc|gold|money|coin|cash|wallet|balance|premium|shopcredit",
                minimumMembersScanned: 4,
                why: "R-71: no meta-economy — scrip resets every match, so nothing currency-shaped "
                     + "may live on the persistent profile",
                wouldMatch: new[] { "Scrip", "BankedScrip", "Currency", "GoldBalance", "WalletTotal" });
        }

        /// <summary>
        /// R-71 / R-20. A fresh match state opens on the configured stake and on nothing else —
        /// and the config-to-state bridge accepts no channel through which a previous match's pool
        /// could arrive. <see cref="ColonyMap.CreateMatchState"/>'s parameter list is pinned to
        /// exactly one optional <see cref="SimConfig"/>: a `carriedPool` / profile-store parameter
        /// growing onto that signature is how carryover would be wired, and it fails here by name.
        ///
        /// (The session-level version — a REMATCH after a wealthy match opens on the stake — is
        /// guarded in EditMode, where the rematch lives.)
        /// </summary>
        [Test]
        public void A_match_opens_on_the_configured_stake_and_the_bridge_accepts_no_carried_pool()
        {
            var config = new SimConfig();
            var state = ColonyMap.V1().CreateMatchState(config);

            Assert.That(state.Team.Scrip, Is.EqualTo(config.StartingScrip),
                "R-20 / R-71: every match opens on the starting stake");

            var bridge = typeof(ColonyMap).GetMethod(
                "CreateMatchState", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(bridge, Is.Not.Null, "the config-to-state bridge exists");

            var parameters = bridge.GetParameters();
            Assert.That(parameters.Length, Is.EqualTo(1),
                "R-71: the bridge takes the match's tunables and NOTHING else — a second "
                + "parameter here is the channel a carried-over pool or account wallet would "
                + "arrive through. Found: ["
                + string.Join(", ", parameters.Select(p => p.ParameterType.Name + " " + p.Name)) + "]");
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(SimConfig)),
                "R-71: and that one parameter is the SimConfig, not a profile store or a pool");
        }

        // ======================================================================================
        //  R-73 — single map, no boss archetype, no difficulty settings
        // ======================================================================================

        /// <summary>
        /// R-73. One map in v1. <see cref="ColonyMap"/> ships exactly one authored-map factory —
        /// <c>V1()</c> — and a second map would arrive as a sibling (<c>V2()</c>, <c>Desert()</c>,
        /// <c>Mine()</c>...), which this pins against by asserting the exact set rather than
        /// scanning for guessed names.
        /// </summary>
        [Test]
        public void The_colony_ships_exactly_one_authored_map()
        {
            var factories = typeof(ColonyMap)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.ReturnType == typeof(ColonyMap))
                .Select(m => m.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.That(factories, Is.EqualTo(new[] { "V1" }),
                "R-73: no second map in v1 — ColonyMap ships exactly one authored map, V1. A new "
                + "public static factory on this type is a second map shipping, whatever it is "
                + "called. Found: [" + string.Join(", ", factories) + "]");
        }

        /// <summary>
        /// R-73 / R-17. No boss archetype: the shipped roster is EXACTLY the R-17 five, pinned in
        /// both places an archetype exists — the <see cref="MonsterType"/> key vocabulary and the
        /// <see cref="MonsterCatalog"/> default rows. A boss (explicitly deferred, DEC-021) would
        /// have to be added to at least one of them to be spawnable, and either addition fails the
        /// exact-set comparison by name.
        /// </summary>
        [Test]
        public void The_roster_is_exactly_the_r17_five_with_no_boss()
        {
            var r17 = new[]
            {
                MonsterType.Shambler,
                MonsterType.Ravager,
                MonsterType.Spitter,
                MonsterType.Burrower,
                MonsterType.BullBehemoth,
            };

            var vocabulary = typeof(MonsterType)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue())
                .ToList();

            Assert.That(vocabulary, Is.EquivalentTo(r17),
                "R-73 / R-17: the archetype vocabulary is exactly the five decided types — a boss "
                + "was explicitly deferred (DEC-021), so a sixth constant here is the non-goal "
                + "shipping. Found: [" + string.Join(", ", vocabulary) + "]");

            Assert.That(new MonsterCatalog().Types, Is.EquivalentTo(r17),
                "R-73 / R-17: and the shipped catalog rows are exactly those five — a boss row "
                + "seeded into the defaults ships on every config");
        }

        /// <summary>
        /// R-73. No difficulty settings: nothing difficulty-shaped on the sim's tuning surface.
        /// <see cref="SimConfig"/> is where every knob the sim reads lives, so a difficulty
        /// setting has exactly one place to arrive — and <see cref="WaveTable"/> is scanned with
        /// it because "one table per difficulty" is the other likely shape.
        /// </summary>
        [Test]
        public void The_config_surface_has_no_difficulty_knob()
        {
            AssertNoPublicMemberMatches(
                new[] { typeof(SimConfig), typeof(WaveTable), typeof(ColonyMap) },
                pattern: "difficult|nightmare|hardmode|easymode|gamemode",
                minimumMembersScanned: 15,
                why: "R-73: no difficulty settings in v1",
                wouldMatch: new[] { "Difficulty", "DifficultyLevel", "HardMode", "NightmareScale" });
        }

        // ======================================================================================
        //  scenario builders and scan plumbing
        // ======================================================================================

        /// <summary>
        /// A minimal combat world for the R-70 guards: a Gunslinger shooting, a second hero to
        /// stand on the line, a monster to stand behind them. Built from production types, the
        /// convention every sim test here follows — the fixture loader is the adapter's contract
        /// with eval/golden, not a scenario builder.
        /// </summary>
        private static MatchSim SimWithTwoHeroesAndAMonster(
            out Hero shooter, out Hero bystander, out Monster monster)
        {
            var state = new MatchState();

            shooter = new Hero
            {
                Id = "h_shooter",
                HeroClass = HeroClass.Gunslinger,
                AccountId = "acc_shooter",
                Pos = new Vec2(0.0, 0.0),
                Hp = 100.0,
                MaxHp = 100.0,
            };
            bystander = new Hero
            {
                Id = "h_bystander",
                HeroClass = HeroClass.Rancher,
                AccountId = "acc_bystander",
                Pos = new Vec2(2.0, 0.0),
                Hp = 120.0,
                MaxHp = 120.0,
            };
            monster = new Monster
            {
                Id = "m_behind",
                Type = MonsterType.Shambler,
                Pos = new Vec2(4.0, 0.0),
                Hp = 60.0,
            };

            state.Heroes[shooter.Id] = shooter;
            state.Heroes[bystander.Id] = bystander;
            state.Monsters[monster.Id] = monster;
            state.Wave.LivingMonsterIds.Add(monster.Id);

            return new MatchSim(state);
        }

        /// <summary>Every public declared member name of a type, accessor methods included.</summary>
        private static List<string> PublicMemberNames(Type type)
        {
            return type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                            | BindingFlags.DeclaredOnly)
                .Select(m => m.Name)
                .ToList();
        }

        /// <summary>
        /// The absence scan all reflection guards share. Non-vacuous by construction: it proves the
        /// pattern against the names the feature would plausibly arrive under (a pattern that can
        /// match nothing fails its own self-test), and it asserts a floor on how many real members
        /// it actually looked at (a scan pointed at an empty surface certifies nothing).
        /// </summary>
        private static void AssertNoPublicMemberMatches(
            Type[] types, string pattern, int minimumMembersScanned, string why, string[] wouldMatch)
        {
            foreach (var probe in wouldMatch)
            {
                Assert.That(Regex.IsMatch(probe, pattern, RegexOptions.IgnoreCase), Is.True,
                    "self-test: the pattern '" + pattern + "' must match '" + probe
                    + "', or this guard could never catch the feature it exists to catch");
            }

            var scanned = 0;
            foreach (var type in types)
            {
                var names = PublicMemberNames(type);
                scanned += names.Count;

                foreach (var name in names)
                {
                    Assert.That(Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase), Is.False,
                        why + " — but " + type.Name + " has a public member named '" + name
                        + "', which matches the non-goal pattern '" + pattern + "'");
                }
            }

            Assert.That(scanned, Is.GreaterThanOrEqualTo(minimumMembersScanned),
                "non-vacuity: the scan saw only " + scanned + " member(s) across "
                + types.Length + " type(s) — fewer than the " + minimumMembersScanned
                + " this surface is known to carry, so it is probably scanning the wrong thing");
        }
    }
}
