using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Ticket T-03 rule tests: the colony map as config (R-10), the civilian-kill arithmetic as a
    /// *rule* rather than the three arrangements G-006/G-007 happen to pin (R-11), emptied hotspots
    /// staying lost (R-12/R-13), and the structural promise that civilians are never simulated
    /// agents (R-72 / DEC-002 / AUD-3).
    ///
    /// Fixtures G-006..G-009 are graded by the locked golden adapter and are deliberately NOT
    /// re-encoded here. Everything below is either a rule the fixtures under-cover, a sad path they
    /// do not visit, or a structural invariant no observation-compare can express.
    ///
    /// Scenarios are built straight from production types — going through the fixture JSON loader
    /// is the adapter's job, not these tests'.
    /// </summary>
    [TestFixture]
    public class T03_HotspotTests
    {
        // ---- R-10: the map is config -------------------------------------------------------------

        /// <summary>
        /// R-10 names the v1 colony exactly: Saloon 8, Chapel 6, Homestead 6. Matching on the id
        /// case-insensitively rather than on a literal id keeps the naming scheme the implementer's
        /// choice while pinning the numbers, which are the contract.
        /// </summary>
        [Test]
        public void V1_map_has_three_hotspots_with_the_civilian_counts_R10_names()
        {
            var map = ColonyMap.V1();

            Assert.That(map.Hotspots.Count, Is.EqualTo(3), "R-10: the v1 map has exactly 3 hotspots");
            Assert.That(CiviliansOf(map, "saloon"), Is.EqualTo(8));
            Assert.That(CiviliansOf(map, "chapel"), Is.EqualTo(6));
            Assert.That(CiviliansOf(map, "homestead"), Is.EqualTo(6));
        }

        /// <summary>R-10 / R-02: 8 + 6 + 6 = 20 is the whole loss budget for a match.</summary>
        [Test]
        public void V1_map_civilians_sum_to_twenty()
        {
            var map = ColonyMap.V1();

            Assert.That(map.Hotspots.Sum(h => h.Civilians), Is.EqualTo(20));
        }

        /// <summary>
        /// R-10: 4 breach entry tunnels, and *one* team spawn — singular. The spawn's singularity is
        /// structural (a single point, not a collection of them), so it is checked by shape.
        /// </summary>
        [Test]
        public void V1_map_has_four_entry_tunnels_and_a_single_team_spawn()
        {
            var map = ColonyMap.V1();

            Assert.That(map.EntryTunnels.Count, Is.EqualTo(4), "R-10: 4 breach entry tunnels");

            var spawnMembers = typeof(ColonyMap)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.Name.IndexOf("Spawn", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            Assert.That(spawnMembers.Count, Is.EqualTo(1),
                "R-10: one marked team spawn point, not a set of them");
            Assert.That(spawnMembers[0].FieldType, Is.EqualTo(typeof(Vec2)),
                "the team spawn is a single point, not a collection");
        }

        /// <summary>
        /// R-10 is *config*, not constants baked into rule code: an instance must be overridable and
        /// everything derived from it must follow. If civilian counts were `const`s this cannot hold.
        /// </summary>
        [Test]
        public void Map_values_are_overridable_config_not_hardcoded_constants()
        {
            var map = ColonyMap.V1();

            var saloon = HotspotNamed(map, "saloon");
            saloon.Civilians = 12;
            map.EntryTunnels.Add(new Vec2(99, 99));
            map.TeamSpawn = new Vec2(7, -7);

            Assert.That(HotspotNamed(map, "saloon").Civilians, Is.EqualTo(12));
            Assert.That(map.Hotspots.Sum(h => h.Civilians), Is.EqualTo(24),
                "the colony total is derived from the hotspot data, not a stored constant");
            Assert.That(map.EntryTunnels.Count, Is.EqualTo(5));
            Assert.That(map.TeamSpawn, Is.EqualTo(new Vec2(7, -7)));
        }

        /// <summary>
        /// The config-to-state bridge: a match opened on the v1 map starts with 20 civilians spread
        /// over 3 live hotspots, all of them targetable (R-10, R-12).
        /// </summary>
        [Test]
        public void Match_state_built_from_the_v1_map_starts_with_twenty_civilians_in_three_hotspots()
        {
            var state = ColonyMap.V1().CreateMatchState();

            Assert.That(state.Hotspots.Count, Is.EqualTo(3));
            Assert.That(state.TotalCivilians, Is.EqualTo(20));
            Assert.That(state.Hotspots.Values.Select(h => h.Civilians).OrderBy(c => c),
                Is.EqualTo(new[] { 6, 6, 8 }));
            Assert.That(state.Hotspots.Values.All(h => h.IsValidTarget), Is.True,
                "R-12: every hotspot starts with civilians, so every hotspot starts targetable");
        }

        // ---- R-72: civilians are a counter, never agents ------------------------------------------

        /// <summary>
        /// REGRESSION GUARD — passes today by construction, and that is the point. R-72 / DEC-002 /
        /// AUD-3 forbid a civilian entity ever appearing. This is a structural claim about the whole
        /// GameSim assembly, so no observation-comparing fixture can express it; it exists to fail
        /// the moment someone introduces a Civilian type or a per-civilian collection.
        /// </summary>
        [Test]
        public void Civilians_are_a_count_on_a_hotspot_and_never_an_entity_type()
        {
            var civilians = typeof(Hotspot).GetField(nameof(Hotspot.Civilians));
            Assert.That(civilians, Is.Not.Null, "Hotspot exposes civilians directly");
            Assert.That(civilians.FieldType, Is.EqualTo(typeof(int)),
                "R-72: civilians are an integer count, not a collection of agents");

            var entityTypes = typeof(MatchSim).Assembly
                .GetTypes()
                .Where(t => t.Name.IndexOf("Civilian", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(t => t.FullName)
                .ToList();

            Assert.That(entityTypes, Is.Empty,
                "R-72: no civilian is ever simulated as an entity; found " + string.Join(", ", entityTypes));
        }

        /// <summary>
        /// REGRESSION GUARD — also green today. The colony total must stay *derived* from the
        /// hotspots (R-02 checks it every hit), and MatchState must hold no civilian collection of
        /// its own that could drift out of step with the per-hotspot counts.
        /// </summary>
        [Test]
        public void MatchState_total_civilians_is_the_sum_across_hotspots_with_no_civilian_collection()
        {
            var state = StateWith(("hs_saloon", 8), ("hs_chapel", 6), ("hs_homestead", 6));
            Assert.That(state.TotalCivilians, Is.EqualTo(20));

            state.Hotspots["hs_chapel"].Civilians = 0;
            Assert.That(state.TotalCivilians, Is.EqualTo(14), "the total tracks the hotspots");

            var civilianCollections = typeof(MatchState)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name.IndexOf("Civilian", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(m => MemberValueType(m) != null && MemberValueType(m) != typeof(int))
                .Select(m => m.Name)
                .ToList();

            Assert.That(civilianCollections, Is.Empty,
                "R-72: MatchState's only civilian surface is the derived integer total; found "
                + string.Join(", ", civilianCollections));
        }

        // ---- R-11: the kill rule, as a rule -------------------------------------------------------

        /// <summary>
        /// R-11 as arithmetic over the whole domain rather than the two arrangements G-006 and G-007
        /// pin: killed == min(ceil(damage / DamagePerCivilian), present), and the counter never goes
        /// negative. A second, untouched hotspot keeps the colony alive so the defeat rule cannot
        /// interfere with what this test is measuring.
        /// </summary>
        [TestCase(10.0, 8, 1)]
        [TestCase(1.0, 5, 1)]
        [TestCase(9.0, 5, 1)]
        [TestCase(10.0, 5, 1)]
        [TestCase(11.0, 5, 2)]
        [TestCase(25.0, 5, 3)]
        [TestCase(40.0, 3, 3)]
        [TestCase(100.0, 2, 2)]
        [TestCase(10.0, 1, 1)]
        public void Civilian_kills_are_ceil_damage_over_ten_clamped_to_the_survivors(
            double damage, int present, int expectedKilled)
        {
            var sim = SimWith(("hs_target", present), ("hs_other", 9));

            var result = sim.ApplyHotspotAttack(Attack("hs_target", damage));

            Assert.That(result.CiviliansKilled, Is.EqualTo(expectedKilled));
            Assert.That(result.CiviliansRemaining, Is.EqualTo(present - expectedKilled));
            Assert.That(result.CiviliansRemaining, Is.GreaterThanOrEqualTo(0),
                "R-11: the count clamps at 0 and never goes negative");
            Assert.That(sim.State.Hotspots["hs_target"].Civilians, Is.EqualTo(present - expectedKilled));
            Assert.That(result.TotalCiviliansRemaining, Is.EqualTo(sim.State.TotalCivilians),
                "the reported colony total is the live one");
        }

        // ---- R-12 / R-13: emptied hotspots stay lost ----------------------------------------------

        /// <summary>
        /// R-12: emptying a hotspot takes it out of the target pool immediately. G-002 grades a
        /// pre-emptied hotspot; this grades the transition the attack itself causes.
        /// </summary>
        [Test]
        public void Emptying_a_hotspot_makes_it_an_invalid_target()
        {
            var sim = SimWith(("hs_chapel", 1), ("hs_saloon", 4));

            sim.ApplyHotspotAttack(Attack("hs_chapel", 10.0));

            Assert.That(sim.State.Hotspots["hs_chapel"].IsValidTarget, Is.False);
            Assert.That(sim.State.Hotspots["hs_saloon"].IsValidTarget, Is.True);
        }

        /// <summary>
        /// R-13: there is no recapture and no heal. Hitting a hotspot that is already at 0 kills
        /// nobody, cannot push it below 0, cannot push it back above 0, and must not re-announce a
        /// loss the players already saw.
        /// </summary>
        [Test]
        public void Attacking_an_already_emptied_hotspot_kills_nobody_and_leaves_it_lost()
        {
            var sim = SimWith(("hs_chapel", 0), ("hs_saloon", 4));

            var result = sim.ApplyHotspotAttack(Attack("hs_chapel", 40.0));

            Assert.That(result.CiviliansKilled, Is.EqualTo(0));
            Assert.That(result.CiviliansRemaining, Is.EqualTo(0));
            Assert.That(sim.State.Hotspots["hs_chapel"].Civilians, Is.EqualTo(0),
                "R-13: an emptied hotspot never comes back and never goes negative");
            Assert.That(sim.State.Hotspots["hs_chapel"].IsValidTarget, Is.False);
            Assert.That(sim.State.TotalCivilians, Is.EqualTo(4), "the untouched hotspot is unaffected");
            Assert.That(EventTypes(sim), Does.Not.Contain("hotspot_emptied"),
                "R-13: the hotspot was already lost — losing it again is not an event");
            Assert.That(EventTypes(sim), Does.Not.Contain("match_defeat"),
                "civilians still live elsewhere");
            Assert.That(sim.State.Status, Is.EqualTo(MatchStatus.InProgress));
        }

        // ---- R-12 / R-02: defeat is colony-wide and edge-exact ------------------------------------

        /// <summary>
        /// Defeat is a property of the colony total, not of any one hotspot: the same single-civilian
        /// kill ends the match when it takes the colony to 0 and does nothing when a survivor remains
        /// elsewhere. Also pins the field the loss moves — `status`, not `phase`: both read "combat"
        /// while the match is live, so an implementation that conflates them looks correct until you
        /// check which one changed.
        /// </summary>
        [TestCase(0, true, TestName = "Defeat_when_the_last_civilian_in_the_colony_dies")]
        [TestCase(1, false, TestName = "No_defeat_while_a_civilian_survives_elsewhere")]
        public void Defeat_fires_exactly_when_the_colony_wide_total_reaches_zero(
            int elsewhere, bool expectDefeat)
        {
            var sim = SimWith(("hs_homestead", 1), ("hs_saloon", elsewhere));

            var result = sim.ApplyHotspotAttack(Attack("hs_homestead", 10.0));

            Assert.That(result.TotalCiviliansRemaining, Is.EqualTo(elsewhere));
            Assert.That(sim.State.Status,
                Is.EqualTo(expectDefeat ? MatchStatus.Defeat : MatchStatus.InProgress));
            Assert.That(sim.State.IsOver, Is.EqualTo(expectDefeat));
            Assert.That(EventTypes(sim).Contains("match_defeat"), Is.EqualTo(expectDefeat));
            Assert.That(sim.State.Phase, Is.EqualTo(MatchPhase.Combat),
                "R-02 moves the match *status*; the phase is a separate field that also reads 'combat'");
        }

        // ---- sad paths ----------------------------------------------------------------------------

        /// <summary>
        /// An attack naming a hotspot the match does not have. The PRD does not say whether that is
        /// rejected or thrown, so this pins only that the behaviour is *defined* — anything but an
        /// unimplemented-rule blow-up — and that it cannot invent a hotspot, move the colony total,
        /// or trip the loss condition.
        /// </summary>
        [Test]
        public void Attacking_a_hotspot_that_is_not_in_the_match_is_defined_and_leaves_the_colony_untouched()
        {
            var sim = SimWith(("hs_saloon", 4));

            var thrown = Attempt(() => sim.ApplyHotspotAttack(Attack("hs_nowhere", 10.0)));

            AssertDefined(thrown);
            Assert.That(sim.State.Hotspots.Count, Is.EqualTo(1), "no hotspot was conjured");
            Assert.That(sim.State.TotalCivilians, Is.EqualTo(4));
            Assert.That(sim.State.Status, Is.EqualTo(MatchStatus.InProgress));
        }

        /// <summary>
        /// Zero and negative damage. Again the PRD is silent on rejection-versus-no-op, so this pins
        /// only the invariant R-11 actually asserts: a hit never *adds* civilians and never reports a
        /// negative kill count — `ceil(-10/10) = -1` would otherwise resurrect one.
        /// </summary>
        [TestCase(0.0)]
        [TestCase(-10.0)]
        [TestCase(-0.5)]
        public void Non_positive_damage_never_adds_civilians(double damage)
        {
            var sim = SimWith(("hs_saloon", 4));

            HotspotAttackResult result = null;
            var thrown = Attempt(() => result = sim.ApplyHotspotAttack(Attack("hs_saloon", damage)));

            AssertDefined(thrown);
            Assert.That(sim.State.Hotspots["hs_saloon"].Civilians, Is.LessThanOrEqualTo(4),
                "R-11: damage removes civilians or does nothing — it never restores them");
            if (result != null)
            {
                Assert.That(result.CiviliansKilled, Is.GreaterThanOrEqualTo(0));
            }
        }

        // ---- helpers -------------------------------------------------------------------------------

        private static HotspotAttackRequest Attack(string targetId, double damage) =>
            new HotspotAttackRequest
            {
                AttackerId = "m1",
                AttackerType = MonsterType.Shambler,
                Damage = damage,
                TargetId = targetId,
            };

        private static MatchState StateWith(params (string Id, int Civilians)[] hotspots)
        {
            var state = new MatchState();
            foreach (var (id, civilians) in hotspots)
            {
                state.Hotspots[id] = new Hotspot { Id = id, Pos = new Vec2(0, 0), Civilians = civilians };
            }

            return state;
        }

        private static MatchSim SimWith(params (string Id, int Civilians)[] hotspots) =>
            new MatchSim(StateWith(hotspots));

        private static HotspotSpec HotspotNamed(ColonyMap map, string word)
        {
            var match = map.Hotspots
                .Where(h => h.Id != null && h.Id.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            Assert.That(match.Count, Is.EqualTo(1),
                "R-10 names exactly one '" + word + "' hotspot; found " + match.Count);
            return match[0];
        }

        private static int CiviliansOf(ColonyMap map, string word) => HotspotNamed(map, word).Civilians;

        private static List<string> EventTypes(MatchSim sim) =>
            sim.LastObservation.EmittedEvents.Select(e => e.Type).ToList();

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

        /// <summary>Rejecting is fine, no-op is fine; "the rule does not exist" is not.</summary>
        private static void AssertDefined(Exception thrown) =>
            Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                "the sad path must have a decided behaviour, not an unimplemented one: " + thrown);

        private static Type MemberValueType(MemberInfo member) => member switch
        {
            FieldInfo field => field.FieldType,
            PropertyInfo property => property.PropertyType,
            _ => null,
        };
    }
}
