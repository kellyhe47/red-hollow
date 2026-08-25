using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Sim;

namespace RedHollow.Sim.Tests
{
    /// <summary>
    /// Ticket 005 (T-05), the parts the golden fixtures do not grade.
    ///
    /// G-013/G-014/G-015 pin three concrete purchase arrangements and G-022 pins one sell, all
    /// through the locked golden adapter, so nothing here re-encodes them. What those four cannot
    /// see is everything R-20..R-25 says that no fixture happens to arrange:
    ///
    ///  - the R-23 cost table itself (<see cref="SimConfig.Placeables"/> ships empty, and every
    ///    fixture states its own cost, so a catalog that stayed empty would still pass all four);
    ///  - R-21's "no negative balance" as a *rule* rather than G-014's single arrangement —
    ///    in particular a cost exactly equal to the pool, which must succeed and land on zero;
    ///  - R-24 placement zones, which the sim does not check at all today (see
    ///    <see cref="PurchaseRequest.ZoneValid"/> — the fixtures hand the sim the client's own
    ///    verdict, and a host that trusts it has no zone rule);
    ///  - R-22's flooring, which G-022's even 250 cannot distinguish from rounding;
    ///  - R-25, trivially true today and pinned here against a future ownership check.
    ///
    /// Scenarios are built from production types directly rather than through the fixture JSON
    /// loader: the loader is the adapter's contract with eval/golden, not a test fixture builder.
    /// </summary>
    [TestFixture]
    public class T05EconomyTests
    {
        /// <summary>G-014 / G-015 pin these two strings, so they are contract rather than taste.</summary>
        private const string InsufficientScrip = "insufficient_scrip";
        private const string WrongPhase = "wrong_phase";

        // Open colony ground on the v1 map: >13 units clear of every hotspot, every tunnel mouth
        // and the team spawn, so no plausible footprint radius makes these invalid. Positions are
        // chosen this way on purpose — R-24 gives no footprint sizes, so pinning one would invent
        // spec. Invalid positions below are instead *exactly coincident* with the thing they are
        // invalid for, which any positive radius rejects.
        private static readonly Vec2 OpenGroundA = new Vec2(20.0, -20.0);
        private static readonly Vec2 OpenGroundB = new Vec2(-20.0, -14.0);
        private static readonly Vec2 OccupiedGround = new Vec2(18.0, -14.0);

        // ---- R-23: the cost table is configuration (owned here per DEC-RUN-1) ----------------------

        /// <summary>
        /// The R-23 cost column, verbatim from the PRD. Only cost: the effect numbers on the same
        /// rows (HP, damage, trigger count, blast radius, range, heal rate) are ticket 006's to pin,
        /// and asserting them here would grade a requirement this ticket does not own.
        /// </summary>
        private static IEnumerable<TestCaseData> CostTable()
        {
            yield return new TestCaseData(PlaceableType.Barricade, 100).SetName("cost_barricade");
            yield return new TestCaseData(PlaceableType.SpikeTrap, 75).SetName("cost_spike_trap");
            yield return new TestCaseData(PlaceableType.DynamiteTrap, 150).SetName("cost_dynamite_trap");
            yield return new TestCaseData(PlaceableType.Turret, 250).SetName("cost_turret");
            yield return new TestCaseData(PlaceableType.MedStation, 200).SetName("cost_med_station");
        }

        /// <summary>
        /// R-23 / DEC-RUN-1: every purchase price comes out of <see cref="SimConfig.Placeables"/>.
        /// Asserted on the catalog rather than through a purchase for the same reason
        /// <see cref="T02TargetingTests.Configured_roster_matches_the_R17_table"/> is — the criterion
        /// is about where the numbers live, and a sim-level assertion would pass just as happily
        /// against constants in rule code.
        /// </summary>
        [TestCaseSource(nameof(CostTable))]
        public void Configured_placeable_costs_match_the_R23_table(string placeableType, int cost)
        {
            Assert.That(new SimConfig().Placeables.StatsFor(placeableType).Cost, Is.EqualTo(cost),
                placeableType + " cost (R-23)");
        }

        /// <summary>
        /// R-23 names exactly five placeables. A sixth row is something a client could buy that no
        /// requirement describes; a missing row is a KeyNotFoundException the moment somebody tries
        /// to place it.
        /// </summary>
        [Test]
        public void Catalog_holds_exactly_the_five_R23_placeables()
        {
            var catalog = new SimConfig().Placeables;

            Assert.That(catalog.Types, Is.EquivalentTo(new[]
            {
                PlaceableType.Barricade,
                PlaceableType.SpikeTrap,
                PlaceableType.DynamiteTrap,
                PlaceableType.Turret,
                PlaceableType.MedStation,
            }));
            Assert.That(catalog.Count, Is.EqualTo(5));
        }

        /// <summary>
        /// R-23: "numeric stats config-tunable". The table only counts as configuration if a caller
        /// can retune a cost on its own <see cref="SimConfig"/> and have the change stay there — and
        /// stay out of every other config. A hardcoded constant fails the first half; defaults shared
        /// through static state fail the second.
        /// </summary>
        [Test]
        public void Placeable_costs_are_overridable_per_config_instance()
        {
            var tuned = new SimConfig();
            tuned.Placeables.Set(PlaceableType.Turret, new PlaceableStats { Cost = 7 });

            Assert.That(tuned.Placeables.StatsFor(PlaceableType.Turret).Cost, Is.EqualTo(7));
            Assert.That(new SimConfig().Placeables.StatsFor(PlaceableType.Turret).Cost, Is.EqualTo(250),
                "one config's override leaked into another; the catalog is shared static state, not config");
        }

        // ---- R-20: the starting stake --------------------------------------------------------------

        /// <summary>
        /// R-20: "Starting stake 500". A structural guard that already holds — <see cref="SimConfig"/>
        /// ships the number — and it is here because nothing else in the assembly does: no code path
        /// reads <see cref="SimConfig.StartingScrip"/>, so this pins the only half of R-20's stake
        /// clause that currently exists. Wave-to-wave carryover is G-016's and ticket 004's.
        /// </summary>
        [Test]
        public void Starting_stake_is_five_hundred_scrip_and_lives_in_config()
        {
            Assert.That(new SimConfig().StartingScrip, Is.EqualTo(500));
        }

        // ---- R-21: the pool boundary, and no negative balance ---------------------------------------

        /// <summary>
        /// R-21: "beyond the pool (`insufficient_scrip`, no negative balance)". G-014 arranges one
        /// point on this line (90 in the pool, 100 asked); the rule is the whole line, and its
        /// interesting point is the one no fixture states — a cost *exactly* equal to the pool, which
        /// is affordable and must leave the pool at exactly zero rather than tripping a
        /// greater-than-or-equal written the wrong way round.
        ///
        /// The catalog row is retuned to the case's cost so that charging the request's price and
        /// charging the catalog's price are the same number here: which of the two the sim trusts is
        /// a separate open question (see
        /// <see cref="Request_cost_disagreeing_with_the_catalog_never_charges_a_price_it_did_not_record"/>)
        /// and this test deliberately does not depend on the answer.
        /// </summary>
        [TestCase(100, 99, true, 1, TestName = "pool_boundary_just_under_the_pool")]
        [TestCase(100, 100, true, 0, TestName = "pool_boundary_exactly_the_pool")]
        [TestCase(100, 101, false, 100, TestName = "pool_boundary_just_over_the_pool")]
        public void Purchase_at_the_pool_boundary_never_drives_scrip_negative(
            int pool, int cost, bool expectAccepted, int expectedScripAfter)
        {
            var config = new SimConfig();
            config.Placeables.Set(PlaceableType.Barricade, new PlaceableStats { Cost = cost });

            var sim = PlanningSim(out var state, out _, scrip: pool, config: config);
            var result = sim.PurchasePlacement(Buy("hero_a", PlaceableType.Barricade, cost, OpenGroundA));

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.EqualTo(expectAccepted));
                Assert.That(result.ScripAfter, Is.EqualTo(expectedScripAfter));
                Assert.That(state.Team.Scrip, Is.EqualTo(expectedScripAfter),
                    "the result and the pool disagree about what the purchase cost");
                Assert.That(state.Team.Scrip, Is.GreaterThanOrEqualTo(0), "R-21: no negative balance");

                if (expectAccepted)
                {
                    Assert.That(result.RejectionReason, Is.Null, "an accepted purchase names no reason");
                    Assert.That(state.PlaceableCount, Is.EqualTo(1), "an affordable purchase places");
                }
                else
                {
                    Assert.That(result.RejectionReason, Is.EqualTo(InsufficientScrip));
                    Assert.That(state.PlaceableCount, Is.EqualTo(0), "a refused purchase places nothing");
                    AssertRejectedWithNoStateChange(sim, InsufficientScrip);
                }
            });
        }

        /// <summary>
        /// R-21 gates placement to the planning phase, and there are three phases, not two. G-015
        /// covers combat; the lobby is the one nobody grades, and it is reachable — a client whose
        /// build screen is still open when the host rewinds to the lobby (R-07 rematch) is exactly
        /// this call. The reason string is the same gate's, so it is G-015's `wrong_phase`.
        /// </summary>
        [Test]
        public void Purchase_from_the_lobby_is_refused_like_a_purchase_during_combat()
        {
            var sim = PlanningSim(out var state, out _, scrip: 500);
            state.Phase = MatchPhase.Lobby;

            var result = sim.PurchasePlacement(Buy("hero_a", PlaceableType.Turret, 250, OpenGroundA));

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.False);
                Assert.That(result.RejectionReason, Is.EqualTo(WrongPhase));
                Assert.That(result.ScripAfter, Is.EqualTo(500));
                Assert.That(state.Team.Scrip, Is.EqualTo(500));
                Assert.That(state.PlaceableCount, Is.EqualTo(0));
                AssertRejectedWithNoStateChange(sim, WrongPhase);
            });
        }

        // ---- R-24: placement zones -------------------------------------------------------------------

        /// <summary>
        /// R-24: "anywhere on colony ground except inside hotspot buildings, on entry tunnel mouths,
        /// or overlapping other placeables."
        ///
        /// Every request here sets <c>ZoneValid = true</c> on purpose. That is the whole point: the
        /// sim is host-authoritative (R-51) and a client that marks its own placement legal is not
        /// evidence. A sim that only reads <see cref="PurchaseRequest.ZoneValid"/> passes all four
        /// golden fixtures and fails every case below.
        ///
        /// The rejection *reason* is not pinned to a literal — no fixture states one, and R-24 lumps
        /// the three exclusions into a single rule, so one shared reason for all three is a correct
        /// implementation. What is pinned is that a reason is given and that it is not one of the two
        /// the fixtures do define: a zone problem reported as `insufficient_scrip` or `wrong_phase`
        /// would send the player to fix the wrong thing.
        /// </summary>
        [TestCase("hotspot", TestName = "zone_reject_inside_a_hotspot_building")]
        [TestCase("tunnel", TestName = "zone_reject_on_an_entry_tunnel_mouth")]
        [TestCase("overlap", TestName = "zone_reject_overlapping_another_placeable")]
        public void Purchase_in_an_excluded_zone_is_refused_even_when_the_client_says_it_is_valid(
            string excludedZone)
        {
            var sim = PlanningSim(out var state, out var map, scrip: 500);
            AddPlaceable(state, "bar_1", PlaceableType.Barricade, OccupiedGround, purchaseCost: 100);

            Vec2 pos;
            switch (excludedZone)
            {
                case "hotspot":
                    pos = map.Hotspots[0].Pos;
                    break;
                case "tunnel":
                    pos = map.EntryTunnels[0];
                    break;
                default:
                    pos = OccupiedGround;
                    break;
            }

            var placeablesBefore = state.PlaceableCount;
            var result = sim.PurchasePlacement(Buy("hero_a", PlaceableType.Turret, 250, pos));

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.False, excludedZone + " is not colony ground R-24 allows");
                Assert.That(result.RejectionReason, Is.Not.Null.And.Not.Empty,
                    "a refused placement must say why, so the shell can mark the ghost (R-24)");
                Assert.That(result.RejectionReason, Is.Not.EqualTo(InsufficientScrip)
                    .And.Not.EqualTo(WrongPhase),
                    "a zone problem reported as a money or phase problem sends the player to fix the wrong thing");
                Assert.That(result.ScripAfter, Is.EqualTo(500));
                Assert.That(state.Team.Scrip, Is.EqualTo(500), "a refused placement charges nothing");
                Assert.That(state.PlaceableCount, Is.EqualTo(placeablesBefore),
                    "a refused placement places nothing");
                AssertRejectedWithNoStateChange(sim, result.RejectionReason);
            });
        }

        /// <summary>
        /// The control for the three exclusions above, and the reason they are evidence of a rule
        /// rather than of a checker that refuses everything: the same match, the same map, the same
        /// already-placed barricade, an ordinary open patch of colony ground — accepted.
        /// </summary>
        [Test]
        public void Purchase_on_open_colony_ground_is_accepted()
        {
            var sim = PlanningSim(out var state, out _, scrip: 500);
            AddPlaceable(state, "bar_1", PlaceableType.Barricade, OccupiedGround, purchaseCost: 100);

            var result = sim.PurchasePlacement(Buy("hero_a", PlaceableType.Turret, 250, OpenGroundA));

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.True);
                Assert.That(result.RejectionReason, Is.Null);
                Assert.That(result.ScripAfter, Is.EqualTo(250));
                Assert.That(state.Team.Scrip, Is.EqualTo(250));
                Assert.That(state.PlaceableCount, Is.EqualTo(2), "the new turret stands alongside the barricade");
            });
        }

        /// <summary>
        /// R-24's overlap exclusion is about placeables that are *there*. A sold defence is not, so
        /// its tile goes back to being colony ground — otherwise every sale over a ten-wave match
        /// leaves an invisible dead zone behind it and the buildable area shrinks monotonically.
        /// This is the reading R-24's wording carries ("overlapping other placeables"), and it is
        /// where R-22 and R-24 meet.
        /// </summary>
        [Test]
        public void Selling_a_placeable_frees_its_ground_for_a_new_one()
        {
            var sim = PlanningSim(out var state, out _, scrip: 500);
            AddPlaceable(state, "t1", PlaceableType.Turret, OccupiedGround, purchaseCost: 250);

            sim.SellPlacement(Sell("hero_a", "t1"));
            var result = sim.PurchasePlacement(Buy("hero_a", PlaceableType.Barricade, 100, OccupiedGround));

            Assert.That(result.Accepted, Is.True,
                "the turret that stood here was sold; nothing overlaps this spot any more");
        }

        // ---- R-25: any player may spend from the shared pool ------------------------------------------

        /// <summary>
        /// R-25: "Any player may spend from the shared pool; no votes or locks." One pool, two
        /// different buyers, back to back — both accepted, and the second is charged against what the
        /// first left behind. Trivially true of an implementation that never looks at
        /// <see cref="PurchaseRequest.PlayerId"/>, which is exactly the point: this pins that no
        /// ownership, vote or lock check is ever added.
        /// </summary>
        [Test]
        public void Any_player_may_spend_from_the_shared_pool()
        {
            var sim = PlanningSim(out var state, out _, scrip: 500);

            var first = sim.PurchasePlacement(Buy("hero_a", PlaceableType.Turret, 250, OpenGroundA));
            var firstBuyer = BuyerNamedByEvents(sim);

            var second = sim.PurchasePlacement(Buy("hero_b", PlaceableType.Barricade, 100, OpenGroundB));
            var secondBuyer = BuyerNamedByEvents(sim);

            Assert.Multiple(() =>
            {
                Assert.That(first.Accepted, Is.True, "hero_a bought from the shared pool");
                Assert.That(first.RejectionReason, Is.Null);
                Assert.That(second.Accepted, Is.True,
                    "hero_b was refused a pool R-25 says is theirs to spend too");
                Assert.That(second.RejectionReason, Is.Null);

                Assert.That(second.ScripAfter, Is.EqualTo(150), "500 - 250 - 100; one pool, not two");
                Assert.That(state.Team.Scrip, Is.EqualTo(150));
                Assert.That(state.PlaceableCount, Is.EqualTo(2));

                Assert.That(firstBuyer, Is.EqualTo("hero_a"));
                Assert.That(secondBuyer, Is.EqualTo("hero_b"),
                    "the event credits whoever spent, not whoever spent first");
            });
        }

        /// <summary>
        /// The sell half of R-25. Co-op negotiation is verbal, so there is no lock: hero_b may sell
        /// the turret hero_a placed, and the refund lands in the same shared pool. An implementation
        /// that compares <see cref="SellRequest.PlayerId"/> against
        /// <see cref="Placeable.OwnerPlayerId"/> fails here and contradicts R-25.
        /// </summary>
        [Test]
        public void A_player_may_sell_a_placeable_another_player_placed()
        {
            var sim = PlanningSim(out var state, out _, scrip: 40);
            AddPlaceable(state, "t1", PlaceableType.Turret, OccupiedGround, purchaseCost: 250,
                owner: "hero_a");

            var result = sim.SellPlacement(Sell("hero_b", "t1"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.True, "R-25: no ownership check gates a sale");
                Assert.That(result.Refund, Is.EqualTo(125));
                Assert.That(state.Team.Scrip, Is.EqualTo(165), "the refund lands in the shared pool");
                Assert.That(state.Placeables["t1"].Exists, Is.False);
            });
        }

        // ---- R-22: the refund floors ------------------------------------------------------------------

        /// <summary>
        /// R-22 / DEC-011: "placeables sell for floor(cost/2)". G-022 halves 250, which floor and
        /// round and ceiling all answer 125 to — so the fixture cannot tell a flooring implementation
        /// from any other. An odd cost can: 75 halves to 37.5, and R-22 says 37.
        ///
        /// The direction is what matters, so both cases are odd and the expected value is strictly
        /// below the true half. <c>cost * 0.5</c> is exact in binary, so DEC-RUN-2's epsilon guard is
        /// not needed here — but a ratio that is ever retuned off 0.5 would need it.
        /// </summary>
        [TestCase(75, 37, TestName = "refund_floors_75_to_37")]
        [TestCase(99, 49, TestName = "refund_floors_99_to_49")]
        public void Sell_refund_floors_a_fractional_half(int purchaseCost, int expectedRefund)
        {
            var sim = PlanningSim(out var state, out _, scrip: 40);
            AddPlaceable(state, "p1", PlaceableType.SpikeTrap, OccupiedGround, purchaseCost);

            var result = sim.SellPlacement(Sell("hero_a", "p1"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.True);
                Assert.That(result.Refund, Is.EqualTo(expectedRefund),
                    "floor(" + purchaseCost + " * 0.5), not a rounded or ceiling half");
                Assert.That(result.ScripAfter, Is.EqualTo(40 + expectedRefund));
                Assert.That(state.Team.Scrip, Is.EqualTo(40 + expectedRefund));
                Assert.That(state.Placeables["p1"].Exists, Is.False,
                    "a refund paid without removing the entity is a free placeable");
                Assert.That(sim.LastObservation.EmittedEvents.Select(e => e.Type),
                    Does.Contain("placeable_sold"));
            });
        }

        /// <summary>
        /// R-22 scopes selling to the planning phase, exactly as R-21 scopes buying — and G-015
        /// already establishes that a mid-combat placement command is refused rather than honoured.
        /// <see cref="SellResult"/> carries no reason field, so only the shape is asserted: nothing
        /// paid, nothing removed, nothing replicated.
        /// </summary>
        [Test]
        public void Selling_during_combat_pays_nothing_and_removes_nothing()
        {
            var sim = PlanningSim(out var state, out _, scrip: 40);
            AddPlaceable(state, "t1", PlaceableType.Turret, OccupiedGround, purchaseCost: 250);
            state.Phase = MatchPhase.Combat;

            var result = sim.SellPlacement(Sell("hero_a", "t1"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.False, "R-22 sells during planning");
                Assert.That(result.Refund, Is.EqualTo(0));
                Assert.That(result.ScripAfter, Is.EqualTo(40));
                Assert.That(state.Team.Scrip, Is.EqualTo(40));
                Assert.That(state.Placeables["t1"].Exists, Is.True,
                    "the turret is still standing in the middle of the fight");
                Assert.That(sim.LastObservation.StateChanges, Is.Empty);
                Assert.That(sim.LastObservation.EmittedEvents.Select(e => e.Type),
                    Does.Not.Contain("placeable_sold"));
            });
        }

        // ---- sad paths ---------------------------------------------------------------------------------

        /// <summary>
        /// The economy's duplicate-spend bug, which no fixture covers: a second sell command for a
        /// placeable already sold — a double-click, or a retried packet — must not pay a second
        /// refund. Whether the second call refuses or throws is open (the sim does both elsewhere:
        /// <see cref="MatchSim.RecordMonsterKill"/> no-ops a duplicate kill,
        /// <see cref="MatchSim.SetPlayerReady"/> throws on an unknown id), so only the money is
        /// pinned.
        /// </summary>
        [Test]
        public void Selling_the_same_placeable_twice_pays_one_refund()
        {
            var sim = PlanningSim(out var state, out _, scrip: 40);
            AddPlaceable(state, "t1", PlaceableType.Turret, OccupiedGround, purchaseCost: 250);

            var first = sim.SellPlacement(Sell("hero_a", "t1"));
            Assert.That(first.Accepted, Is.True, "the first sale is the ordinary G-022 one");
            Assert.That(state.Team.Scrip, Is.EqualTo(165));

            var second = Attempt(() => sim.SellPlacement(Sell("hero_b", "t1")), out var thrown);

            Assert.Multiple(() =>
            {
                Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                    "sell_placement is still a stub, so a repeated sale has no defined behaviour yet");
                Assert.That(state.Team.Scrip, Is.EqualTo(165),
                    "the second sale paid out again — the same turret was refunded twice");
                Assert.That(state.Placeables["t1"].Exists, Is.False);

                if (thrown == null)
                {
                    Assert.That(second.Accepted, Is.False);
                    Assert.That(second.Refund, Is.EqualTo(0));
                    Assert.That(second.ScripAfter, Is.EqualTo(165));
                }
            });
        }

        /// <summary>
        /// A sell command naming a placeable this match never had. Same open question as the double
        /// sale, same single pin: it must not conjure money out of an entity that does not exist.
        /// </summary>
        [Test]
        public void Selling_an_unknown_placeable_pays_nothing()
        {
            var sim = PlanningSim(out var state, out _, scrip: 40);

            var result = Attempt(() => sim.SellPlacement(Sell("hero_a", "no_such_placeable")), out var thrown);

            Assert.Multiple(() =>
            {
                Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                    "sell_placement is still a stub, so an unknown placeable id has no defined behaviour yet");
                Assert.That(state.Team.Scrip, Is.EqualTo(40), "an id the match never had cannot be refunded");

                if (thrown == null)
                {
                    Assert.That(result.Accepted, Is.False);
                    Assert.That(result.Refund, Is.EqualTo(0));
                    Assert.That(result.ScripAfter, Is.EqualTo(40));
                    Assert.That(sim.LastObservation.StateChanges, Is.Empty);
                }
            });
        }

        /// <summary>
        /// A purchase naming a placeable type the R-23 catalog does not hold — a client on a newer
        /// build, or a hand-crafted packet. <see cref="PlaceableCatalog.StatsFor"/> throws by design
        /// for an unknown key, so a throw out of here is a defensible answer and a rejection is too;
        /// what is not defensible is charging the pool for it or placing it. Only that is pinned.
        /// </summary>
        [Test]
        public void Unknown_placeable_type_is_never_placed_or_charged_for()
        {
            var sim = PlanningSim(out var state, out _, scrip: 500);

            var result = Attempt(
                () => sim.PurchasePlacement(Buy("hero_a", "trebuchet", 250, OpenGroundA)), out var thrown);

            Assert.Multiple(() =>
            {
                Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                    "purchase_placement is still a stub, so an unknown type has no defined behaviour yet");
                Assert.That(state.Team.Scrip, Is.EqualTo(500), "an unknown placeable was charged for");
                Assert.That(state.PlaceableCount, Is.EqualTo(0), "an unknown placeable was placed");

                if (thrown == null)
                {
                    Assert.That(result.Accepted, Is.False);
                    Assert.That(result.RejectionReason, Is.Not.Null.And.Not.Empty);
                    Assert.That(sim.LastObservation.StateChanges, Is.Empty);
                }
            });
        }

        /// <summary>
        /// The one genuinely open question in this ticket: the request carries its own
        /// <see cref="PurchaseRequest.Cost"/> and the R-23 catalog carries another, and every fixture
        /// supplies a cost that already agrees with the catalog — so the acceptance contract cannot
        /// say which the sim trusts. Both readings are defensible (trust the catalog and the client
        /// cannot name its own price; trust the request and the fixtures' `cost` field means
        /// something), so neither is pinned.
        ///
        /// What is pinned is the invariant that holds under both, and whose absence is an infinite
        /// money exploit: the scrip actually taken and the
        /// <see cref="Placeable.PurchaseCost"/> written onto the entity must be the same number,
        /// because R-22 pays the refund back out of that field. Charge 10 and record 100 and every
        /// buy-sell cycle nets 40 scrip.
        /// </summary>
        [Test]
        public void Request_cost_disagreeing_with_the_catalog_never_charges_a_price_it_did_not_record()
        {
            var config = new SimConfig();
            config.Placeables.Set(PlaceableType.Barricade, new PlaceableStats { Cost = 100 });

            var sim = PlanningSim(out var state, out _, scrip: 500, config: config);
            var before = new HashSet<string>(state.Placeables.Keys);

            var result = Attempt(
                () => sim.PurchasePlacement(Buy("hero_a", PlaceableType.Barricade, 10, OpenGroundA)),
                out var thrown);

            Assert.That(thrown, Is.Not.InstanceOf<NotImplementedException>(),
                "purchase_placement is still a stub, so a mismatched cost has no defined behaviour yet");

            var charged = 500 - state.Team.Scrip;
            var placed = state.Placeables.Values.FirstOrDefault(p => !before.Contains(p.Id));

            Assert.Multiple(() =>
            {
                Assert.That(state.Team.Scrip, Is.GreaterThanOrEqualTo(0), "R-21: no negative balance");

                if (placed == null)
                {
                    Assert.That(charged, Is.EqualTo(0), "nothing was placed, so nothing may be charged");
                    return;
                }

                Assert.That(charged, Is.AnyOf(10, 100),
                    "the pool was charged a price that is neither the request's nor the catalog's");
                Assert.That(placed.PurchaseCost, Is.EqualTo(charged),
                    "the entity records a purchase cost the team never paid; R-22 refunds half of it");
                Assert.That(result.ScripAfter, Is.EqualTo(state.Team.Scrip));
            });
        }

        // ---- shared assertions -------------------------------------------------------------------------

        /// <summary>
        /// The half of the criterion every refusal shares (G-014 / G-015 shape): a
        /// `purchase_rejected` event carrying the same reason the result names, and not one byte of
        /// replicated state moved. A refusal that quietly mutates is the bug that survives a green
        /// result assertion.
        /// </summary>
        private static void AssertRejectedWithNoStateChange(MatchSim sim, string expectedReason)
        {
            Assert.That(sim.LastObservation.StateChanges, Is.Empty, "a refused purchase changes no state");
            Assert.That(sim.LastObservation.ExternalCalls, Is.Empty);

            var rejected = sim.LastObservation.EmittedEvents.FirstOrDefault(e => e.Type == "purchase_rejected");
            Assert.That(rejected, Is.Not.Null, "every refusal emits purchase_rejected");
            Assert.That(rejected.Fields.TryGetValue("reason", out var reason), Is.True,
                "purchase_rejected must carry the reason the result named");
            Assert.That(reason, Is.EqualTo(expectedReason));
        }

        /// <summary>The player a `placeable_created` event credits, or null when none was emitted.</summary>
        private static string BuyerNamedByEvents(MatchSim sim)
        {
            var created = sim.LastObservation.EmittedEvents.FirstOrDefault(e => e.Type == "placeable_created");
            if (created == null || !created.Fields.TryGetValue("by", out var by))
            {
                return null;
            }

            return by as string;
        }

        /// <summary>
        /// Runs a command that may legitimately throw, capturing the exception instead of failing.
        /// Used only where the PRD leaves the sad path open; the NotImplementedException guard at
        /// each call site is what keeps those tests red until T-05 lands.
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

        // ---- scenario builders -------------------------------------------------------------------------

        /// <summary>
        /// A match mid-planning on the real v1 colony map (R-10), which is what makes the R-24
        /// exclusions expressible: the hotspots come through <see cref="ColonyMap.CreateMatchState"/>
        /// onto <see cref="MatchState.Hotspots"/>, and the entry tunnels stay on the map itself.
        /// Both are handed to the sim so the implementation may read either source.
        /// </summary>
        private static MatchSim PlanningSim(
            out MatchState state, out ColonyMap map, int scrip, SimConfig config = null)
        {
            map = ColonyMap.V1();
            state = map.CreateMatchState();
            state.Phase = MatchPhase.Planning;
            state.Team.Scrip = scrip;

            return new MatchSim(state, config ?? new SimConfig()) { ColonyMap = map };
        }

        /// <summary>
        /// A purchase command. <c>ZoneValid</c> is always true here — the fixtures set it that way,
        /// and R-24 is only a rule if the sim reaches its own verdict regardless (R-51).
        /// </summary>
        private static PurchaseRequest Buy(string playerId, string placeableType, int cost, Vec2 pos) =>
            new PurchaseRequest
            {
                PlayerId = playerId,
                PlaceableType = placeableType,
                Cost = cost,
                Pos = pos,
                ZoneValid = true,
            };

        private static SellRequest Sell(string playerId, string placeableId) =>
            new SellRequest { PlayerId = playerId, PlaceableId = placeableId };

        private static void AddPlaceable(
            MatchState state, string id, string type, Vec2 pos, int purchaseCost, string owner = "hero_a")
        {
            state.Placeables[id] = new Placeable
            {
                Id = id,
                Type = type,
                Pos = pos,
                OwnerPlayerId = owner,
                PurchaseCost = purchaseCost,
                Hp = 300.0,
                Exists = true,
            };
        }
    }
}
