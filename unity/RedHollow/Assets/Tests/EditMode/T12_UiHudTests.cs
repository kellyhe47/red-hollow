using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RedHollow.Game.Host;
using RedHollow.Game.Net;
using RedHollow.Game.UI;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 012 (T-12), part 2 of 2: S3 Planning (R-63) and S4 Combat (R-61 / R-62), with every
    /// wireframe state each screen lists. Part 1 (<see cref="T12_UiScreensTests"/>) owns the flow
    /// and the other screens.
    ///
    /// S3's states: pulsing entry preview that hides composition (R-05 / DEC-018), the catalog
    /// shop bar, greyed unaffordable items, ghost placement with invalid-zone rejection, the sell
    /// tooltip and R-22's reasonless refusal, the ready panel over connected players, and the
    /// timer's inclusive-deadline auto-transition to combat.
    ///
    /// S4's states: the persistent HUD readouts (every one off the replicated state or the profile
    /// store — the exact sources the sim handoff names), cooldown sweeps with the inclusive
    /// ready-at, padlocked locked slots, the level-up toast/badge/picker that never pauses the sim
    /// (R-62), dead-hero spectate with the respawn countdown and a living-ally camera target,
    /// civilians-lost toast + red flash, lost-hotspot marking, monster-spawn entry flares, and the
    /// disconnect toast.
    ///
    /// <b>Not asserted</b>: any copy, any colour (grey/red/dark are presentation of flags pinned
    /// here), pulse/flash/shake animation, and every number the PRD does not own — prices come
    /// from the R-23 catalog, refunds from the R-22 ratio, respawn from R-33's config.
    /// </summary>
    [TestFixture]
    public class T12_UiHudTests
    {
        private const double Step60Hz = 1.0 / 60.0;
        private const double SimTolerance = 1e-6;

        /// <summary>Countdowns are sums of 1/60 deltas, so they are compared loosely.</summary>
        private const double ClockTolerance = 1e-3;

        private const string HostPeerId = "peer_host";
        private const string GuestPeerId = "peer_guest";
        private const string HostAccount = "acc_calamity";
        private const string GuestAccount = "acc_doc";

        /// <summary>
        /// A spot on open colony ground: clear of every hotspot building, every tunnel mouth and
        /// everything a test places elsewhere (checked against <see cref="ColonyMap.V1"/>'s layout
        /// and the sim's default exclusion radii).
        /// </summary>
        private static readonly Vec2 OpenGround = new Vec2(8.0, -2.0);

        private float _timeScaleAtStart;

        [SetUp]
        public void RememberTimeScale()
        {
            _timeScaleAtStart = Time.timeScale;
        }

        [TearDown]
        public void RestoreTimeScale()
        {
            Time.timeScale = _timeScaleAtStart;
        }

        // ==========================================================================================
        //  S3 — top bar and timer
        // ==========================================================================================

        /// <summary>
        /// R-61 / R-63 — the planning top bar mirrors the replicated state: wave n of the
        /// campaign's total, the one shared pool, and each hotspot's civilians by id. Every figure
        /// is the state's, never a recomputation.
        /// </summary>
        [Test]
        public void The_planning_top_bar_mirrors_the_replicated_state()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            DriveToPlanning(lobby, match);

            var model = NewPlanningModel(lobby, HostAccount);
            model.Refresh();

            Assert.That(model.WaveNumber, Is.EqualTo(match.State.Wave.Number),
                "R-61: the wave number is State.Wave.Number");
            Assert.That(model.WaveNumber, Is.EqualTo(2),
                "sanity: wave 1 was cleared, so S3 plans wave 2");
            Assert.That(model.TotalWaves, Is.EqualTo(match.State.Wave.TotalWaves),
                "R-61: 'of 10' is State.Wave.TotalWaves, not a literal");
            Assert.That(model.Scrip, Is.EqualTo(match.State.Team.Scrip),
                "R-61 / R-20: the shared pool");

            Assert.That(model.Hotspots.Select(h => h.HotspotId),
                Is.EquivalentTo(match.State.Hotspots.Keys),
                "R-61: one readout per hotspot, by the sim's own ids");
            foreach (var readout in model.Hotspots)
            {
                Assert.That(readout.Civilians,
                    Is.EqualTo(match.State.Hotspots[readout.HotspotId].Civilians),
                    "R-61: " + readout.HotspotId + " shows its replicated count");
            }
        }

        /// <summary>
        /// R-03 / R-63 — the countdown: it starts inside the configured window, ticks down with
        /// sim time, and never reads negative. The deadline is INCLUSIVE repo-wide (now >=
        /// deadline), so 0 is a value the bar may legitimately show and negative never is.
        /// </summary>
        [Test]
        public void The_planning_timer_counts_down_and_never_goes_negative()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            DriveToPlanning(lobby, match);

            var model = NewPlanningModel(lobby, HostAccount);
            model.Refresh();

            var duration = lobby.SimConfig.PlanningDurationSeconds;
            var before = model.TimerRemainingSeconds;

            Assert.That(before, Is.GreaterThan(0.0), "R-03: planning just opened — time remains");
            Assert.That(before, Is.LessThanOrEqualTo(duration),
                "R-03: never more than the configured window");

            for (var i = 0; i < 60; i++)
            {
                lobby.Session.Step(Step60Hz);
            }

            model.Refresh();
            Assert.That(model.TimerRemainingSeconds, Is.EqualTo(before - 1.0).Within(ClockTolerance),
                "R-03: one second of sim time is one second off the countdown");
            Assert.That(model.TimerRemainingSeconds, Is.GreaterThanOrEqualTo(0.0),
                "the countdown clamps at zero; a negative timer is a UI showing debt");
        }

        /// <summary>
        /// R-03 / R-63, wireframe S3 state: "timer hits 0 → auto-transition to combat". Nobody
        /// readies up; the deadline alone must open combat and spawn the wave, and the router must
        /// follow the phase to S4 with no event fed to it.
        /// </summary>
        [Test]
        public void The_timer_running_out_auto_transitions_to_combat()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            DriveToPlanning(lobby, match);

            var router = new UiRouter(lobby.Session);

            var arrived = DriveUntil(
                lobby.Session, match.Clock,
                () => match.State.Phase == MatchPhase.Combat
                      && match.State.Wave.LivingMonsterIds.Count > 0,
                budgetSeconds: lobby.SimConfig.PlanningDurationSeconds + 5.0);

            Assert.That(arrived, Is.True,
                "R-03: the timer alone must end planning — the party never readied, and combat "
                + "must still open with wave " + match.State.Wave.Number + " spawned");

            router.Update();
            Assert.That(router.Screen, Is.EqualTo(UiScreen.Combat),
                "R-60: the router follows the phase to S4 with no event required");
        }

        // ==========================================================================================
        //  S3 — the entry preview (R-05 / DEC-018)
        // ==========================================================================================

        /// <summary>
        /// R-05 / DEC-018 — the partial preview: the pulsing entries are exactly the sim's own
        /// <see cref="MatchSim.PreviewUpcomingWave"/> answer, and the presenter is STRUCTURALLY
        /// unable to leak composition — no public member of it names <see cref="WaveSpec"/>,
        /// <see cref="MonsterGroup"/> or <see cref="WaveTable"/>, the three types that know what
        /// comes out of a tunnel. Hiding types/counts is the requirement; do not work around it.
        /// </summary>
        [Test]
        public void The_pulsing_entry_preview_names_tunnels_and_is_unable_to_name_composition()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            DriveToPlanning(lobby, match);

            var model = NewPlanningModel(lobby, HostAccount);
            model.Refresh();

            var preview = match.Sim.PreviewUpcomingWave();
            Assert.That(preview.ActiveEntryTunnels, Is.Not.Empty,
                "sanity (R-19): the upcoming wave activates at least one breach");

            Assert.That(model.PulsingEntryTunnels, Is.EqualTo(preview.ActiveEntryTunnels),
                "R-05: the pulsing entries are the preview's indices — all of them, and nothing "
                + "that is not in it");

            // The structural half: the presenter's public surface cannot carry composition.
            var forbidden = new[] { typeof(WaveSpec), typeof(MonsterGroup), typeof(WaveTable) };
            foreach (var member in typeof(PlanningScreenModel).GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                foreach (var involved in TypesInvolvedIn(member))
                {
                    Assert.That(forbidden.Contains(involved), Is.False,
                        "DEC-018: PlanningScreenModel." + member.Name + " involves " + involved.Name
                        + " — the planning UI must be unable to hold the upcoming wave's "
                        + "composition by construction");
                }
            }
        }

        // ==========================================================================================
        //  S3 — the shop bar (R-63)
        // ==========================================================================================

        /// <summary>
        /// R-23 / R-63 — the shop bar IS the catalog: every row the R-23 catalog holds, priced by
        /// the catalog, affordability derived from the shared pool. No type and no price is typed
        /// into the UI.
        /// </summary>
        [Test]
        public void The_shop_bar_lists_the_catalog_at_catalog_prices()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            DriveToPlanning(lobby, match);

            var model = NewPlanningModel(lobby, HostAccount);
            model.Refresh();

            var catalog = lobby.SimConfig.Placeables;
            Assert.That(model.ShopItems.Select(i => i.Type), Is.EquivalentTo(catalog.Types),
                "R-63: one shop entry per catalog row — no more, no fewer");

            foreach (var item in model.ShopItems)
            {
                Assert.That(item.Cost, Is.EqualTo(catalog.StatsFor(item.Type).Cost),
                    "R-23: " + item.Type + " is priced by the catalog");
                Assert.That(item.Affordable, Is.EqualTo(item.Cost <= match.State.Team.Scrip),
                    "R-63: affordability is cost against the shared pool, per item");
            }
        }

        /// <summary>
        /// R-63, wireframe S3 state: "scrip too low → shop item greyed with cost in red". The
        /// grey and the red are presentation; the pinned contract is that the item STAYS LISTED
        /// and is flagged unaffordable — and that buying it anyway is refused by the sim with the
        /// reason surfaced, pool untouched.
        /// </summary>
        [Test]
        public void An_unaffordable_item_is_flagged_not_hidden_and_buying_it_is_refused()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            DriveToPlanning(lobby, match);

            // Between spike trap and barricade: something is affordable, something is not, so the
            // flag below is discriminating rather than uniform.
            match.State.Team.Scrip = 80;

            var model = NewPlanningModel(lobby, HostAccount);
            model.Refresh();

            var catalog = lobby.SimConfig.Placeables;
            var affordable = model.ShopItems.Where(i => i.Cost <= 80).ToList();
            var unaffordable = model.ShopItems.Where(i => i.Cost > 80).ToList();
            Assert.That(affordable, Is.Not.Empty, "sanity: something still costs 80 or less");
            Assert.That(unaffordable, Is.Not.Empty, "sanity: something costs more than 80");

            Assert.That(model.ShopItems.Count, Is.EqualTo(catalog.Count),
                "R-63: an unaffordable item is greyed, NOT removed from the bar");
            Assert.That(affordable.All(i => i.Affordable), Is.True, "affordable rows say so");
            Assert.That(unaffordable.All(i => !i.Affordable), Is.True,
                "R-63: unaffordable rows are flagged (the grey + red-cost state)");

            // Buying one anyway: the sim is the authority and refuses with a reason (R-21/G-014).
            var tooDear = unaffordable.OrderByDescending(i => i.Cost).First();
            var scripBefore = match.State.Team.Scrip;
            var placeablesBefore = match.State.Placeables.Count;

            model.BeginPlacement(tooDear.Type);
            model.MoveGhost(OpenGround, zoneValid: true);
            var result = model.ConfirmPlacement();

            Assert.That(result.Accepted, Is.False, "R-21: the pool cannot cover it");
            Assert.That(result.RejectionReason, Is.Not.Null.And.Not.Empty,
                "R-21: a refused purchase carries a reason");
            Assert.That(model.LastPurchaseRejection, Is.EqualTo(result.RejectionReason),
                "the UI surfaces the purchase_rejected reason, verbatim");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore),
                "G-014: a refusal moves no scrip");
            Assert.That(match.State.Placeables.Count, Is.EqualTo(placeablesBefore),
                "G-014: and places nothing");
        }

        /// <summary>
        /// R-24 / R-63 — ghost placement: click an item → the ghost follows the cursor; an invalid
        /// zone tints it (flag, not colour); clicking there is REJECTED — the sim refuses, the
        /// reason is surfaced, nothing is placed, and the ghost stays up for the retry (the shake
        /// is presentation).
        /// </summary>
        [Test]
        public void The_placement_ghost_follows_the_cursor_and_an_invalid_zone_is_rejected()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            DriveToPlanning(lobby, match);

            var model = NewPlanningModel(lobby, HostAccount);
            model.Refresh();

            Assert.That(model.GhostActive, Is.False, "no ghost before a shop click");

            model.BeginPlacement(PlaceableType.Turret);
            Assert.That(model.GhostActive, Is.True, "R-63: clicking a shop item raises the ghost");
            Assert.That(model.GhostType, Is.EqualTo(PlaceableType.Turret), "of that item");

            model.MoveGhost(OpenGround, zoneValid: true);
            Assert.That(model.GhostPos, Is.EqualTo(OpenGround), "R-63: the ghost follows the cursor");
            Assert.That(model.GhostInvalid, Is.False, "open ground is a valid zone");

            // Inside a hotspot building — R-24's first exclusion, and the sim agrees.
            var insideTheChapel = match.State.Hotspots["hs_chapel"].Pos;
            model.MoveGhost(insideTheChapel, zoneValid: false);
            Assert.That(model.GhostInvalid, Is.True,
                "R-24 / R-63: an invalid zone tints the ghost (the red is presentation; the flag "
                + "is the contract)");

            var scripBefore = match.State.Team.Scrip;
            var result = model.ConfirmPlacement();

            Assert.That(result.Accepted, Is.False,
                "R-24: placement inside a hotspot building is refused by the sim");
            Assert.That(model.LastPurchaseRejection, Is.EqualTo(result.RejectionReason),
                "and the UI surfaces the reason");
            Assert.That(model.LastPurchaseRejection, Is.Not.EqualTo("insufficient_scrip"),
                "R-24: a zone problem must not be reported as a money problem");
            Assert.That(match.State.Placeables, Is.Empty, "nothing was placed");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore), "nothing was charged");
            Assert.That(model.GhostActive, Is.True,
                "R-63: a rejected ghost stays up for the retry — the shake is presentation");
        }

        /// <summary>
        /// R-21 / R-23 / R-63 — the happy path: a valid click buys at the catalog price, the
        /// placeable stands where the ghost was, the pool drops by exactly the price, and the
        /// ghost clears.
        /// </summary>
        [Test]
        public void A_valid_placement_buys_at_the_catalog_price_and_clears_the_ghost()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            DriveToPlanning(lobby, match);

            var model = NewPlanningModel(lobby, HostAccount);
            model.Refresh();

            var cost = lobby.SimConfig.Placeables.StatsFor(PlaceableType.Barricade).Cost;
            var scripBefore = match.State.Team.Scrip;

            model.BeginPlacement(PlaceableType.Barricade);
            model.MoveGhost(OpenGround, zoneValid: true);
            var result = model.ConfirmPlacement();

            Assert.That(result.Accepted, Is.True, "R-21: a valid planning-phase buy is accepted");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore - cost),
                "R-23: charged the catalog price — the UI typed no number of its own");

            var placed = match.State.Placeables.Values.SingleOrDefault(p => p.Exists);
            Assert.That(placed, Is.Not.Null, "the placeable is in the world");
            Assert.That(placed.Type, Is.EqualTo(PlaceableType.Barricade), "of the ghost's type");
            Assert.That(placed.Pos, Is.EqualTo(OpenGround), "where the ghost stood");

            Assert.That(model.GhostActive, Is.False,
                "R-63: an accepted placement clears the ghost");
        }

        // ==========================================================================================
        //  S3 — sell on click (R-22)
        // ==========================================================================================

        /// <summary>
        /// R-22 / R-63 — "click one → SELL (50%) tooltip": the tooltip's figure is the R-22 ratio
        /// of what was paid, and selling refunds exactly that into the shared pool and removes the
        /// placeable.
        /// </summary>
        [Test]
        public void The_sell_tooltip_names_the_half_refund_and_selling_pays_it()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            DriveToPlanning(lobby, match);

            // Standing from an earlier wave, seeded the way T11 seeds one: what is under test is
            // the sell, not the buy.
            match.State.Placeables["p_wall"] = new Placeable
            {
                Id = "p_wall",
                Type = PlaceableType.Barricade,
                Pos = OpenGround,
                OwnerPlayerId = match.State.Players[0].Id,
                PurchaseCost = 100,
                Hp = 200.0,
                Exists = true,
            };

            var model = NewPlanningModel(lobby, HostAccount);
            model.Refresh();

            var expectedRefund = (int)(100 * lobby.SimConfig.SellRefundRatio);
            Assert.That(model.SellRefundFor("p_wall"), Is.EqualTo(expectedRefund),
                "R-22: the tooltip's number is the configured ratio of the recorded purchase "
                + "cost — never a literal 50 typed into the UI");

            var scripBefore = match.State.Team.Scrip;
            var result = model.Sell("p_wall");

            Assert.That(result.Accepted, Is.True, "R-22: a planning-phase sell is accepted");
            Assert.That(result.Refund, Is.EqualTo(expectedRefund), "at the tooltip's figure");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore + expectedRefund),
                "R-20: refunded into the one shared pool");
            Assert.That(match.State.Placeables["p_wall"].Exists, Is.False,
                "R-22: the sold placeable leaves the field");
        }

        /// <summary>
        /// R-22 — a refused sale is `accepted: false` and NOTHING more: <see cref="SellResult"/>
        /// carries no reason field (pinned structurally below), so the UI surfaces a flag and must
        /// not invent a reason to go with it.
        /// </summary>
        [Test]
        public void A_refused_sale_carries_no_reason_and_the_ui_invents_none()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            DriveToPlanning(lobby, match);

            var model = NewPlanningModel(lobby, HostAccount);
            model.Refresh();

            var result = model.Sell("p_never_existed");

            Assert.That(result.Accepted, Is.False, "R-22: an unknown id pays nothing");
            Assert.That(result.Refund, Is.EqualTo(0), "and refunds nothing");
            Assert.That(model.LastSellRefused, Is.True, "the UI surfaces the refusal as a flag");
            Assert.That(model.LastPurchaseRejection, Is.Null,
                "and no reason string appears anywhere — a refused sale has none to show");

            // The structural half, pinned so a later 'helpful' reason field is a deliberate act:
            var reasonish = typeof(SellResult)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name.IndexOf("Reason", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            Assert.That(reasonish, Is.Empty,
                "R-22: SellResult has no reason field — 'accepted: false' is the whole story");
        }

        // ==========================================================================================
        //  S3 — the ready panel (R-03)
        // ==========================================================================================

        /// <summary>
        /// R-03 / R-63 — "2/4 ready (denominator = connected players)", and READY UP starts combat
        /// early only when everyone connected has readied.
        /// </summary>
        [Test]
        public void Ready_up_counts_connected_players_and_the_last_ready_starts_combat_early()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            DriveToPlanning(lobby, match);

            var model = NewPlanningModel(lobby, HostAccount);
            model.Refresh();

            Assert.That(model.ConnectedCount, Is.EqualTo(2), "R-03: the denominator is connected players");
            Assert.That(model.ReadyCount, Is.EqualTo(0), "nobody has readied");

            var first = model.ReadyUp();
            model.Refresh();

            Assert.That(first.CombatStarted, Is.False,
                "R-03: one of two is not everyone — planning holds");
            Assert.That(model.ReadyCount, Is.EqualTo(1), "ready 1/2");
            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Planning), "still planning");

            // The guest readies through the sim, as their client would.
            var guestSlot = match.State.Players.First(p => p.AccountId == GuestAccount);
            var second = match.Sim.SetPlayerReady(guestSlot.Id);

            Assert.That(second.CombatStarted, Is.True,
                "R-03 / G-017: everyone connected is ready — combat starts early");
            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Combat), "and the phase moved");
        }

        /// <summary>
        /// R-03 / R-53 — the denominator SHRINKS when a player leaves: a disconnected player is
        /// neither waited on nor counted as a yes, so the remaining player's ready alone starts
        /// combat.
        /// </summary>
        [Test]
        public void A_leaver_stops_being_counted_in_the_ready_denominator()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            DriveToPlanning(lobby, match);

            var model = NewPlanningModel(lobby, HostAccount);
            model.Refresh();
            Assert.That(model.ConnectedCount, Is.EqualTo(2), "two connected before the drop");

            lobby.Session.Disconnect(GuestPeerId);
            model.Refresh();

            Assert.That(model.ConnectedCount, Is.EqualTo(1),
                "R-03 / R-53: the leaver is out of the denominator");

            var result = model.ReadyUp();
            Assert.That(result.CombatStarted, Is.True,
                "R-03: the one remaining player's ready is everyone's ready");
        }

        // ==========================================================================================
        //  S4 — the persistent HUD (R-61)
        // ==========================================================================================

        /// <summary>
        /// R-61 — every top-bar and self-bar readout, each off the source the sim handoff names:
        /// wave off <c>State.Wave</c>, monsters off the living roster, civilians off the hotspots,
        /// scrip off the team, HP/class off the hero.
        /// </summary>
        [Test]
        public void The_combat_hud_mirrors_the_replicated_state()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            lobby.Session.Step(Step60Hz);

            var hud = NewHud(lobby, HostAccount);
            hud.Refresh();

            var state = match.State;
            var hero = HeroFor(state, HostAccount);

            Assert.That(hud.WaveNumber, Is.EqualTo(state.Wave.Number), "R-61: wave n");
            Assert.That(hud.TotalWaves, Is.EqualTo(state.Wave.TotalWaves), "R-61: of 10");
            Assert.That(hud.MonstersRemaining, Is.EqualTo(state.Wave.LivingMonsterIds.Count),
                "R-61: monsters remaining is the living roster's count");
            Assert.That(hud.MonstersRemaining, Is.GreaterThan(0), "sanity (R-19): wave 1 is in");
            Assert.That(hud.Scrip, Is.EqualTo(state.Team.Scrip), "R-61: the shared pool");

            Assert.That(hud.Hotspots.Select(h => h.HotspotId), Is.EquivalentTo(state.Hotspots.Keys),
                "R-61: per-hotspot counts, by id");
            foreach (var readout in hud.Hotspots)
            {
                Assert.That(readout.Civilians,
                    Is.EqualTo(state.Hotspots[readout.HotspotId].Civilians),
                    "R-61: " + readout.HotspotId);
            }

            Assert.That(hud.Hp, Is.EqualTo(hero.Hp).Within(SimTolerance), "R-61: own HP");
            Assert.That(hud.MaxHp, Is.EqualTo(hero.MaxHp).Within(SimTolerance), "R-61: of max");
            Assert.That(hud.HeroClass, Is.EqualTo(hero.HeroClass), "R-61: the class icon's class");
        }

        /// <summary>R-20 / R-61 — "+ticks on kills": a bounty lands in the pool and on the bar.</summary>
        [Test]
        public void Scrip_ticks_up_and_monsters_remaining_ticks_down_on_a_kill()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            lobby.Session.Step(Step60Hz);

            var hud = NewHud(lobby, HostAccount);
            hud.Refresh();
            var scripBefore = hud.Scrip;
            var monstersBefore = hud.MonstersRemaining;

            var victim = match.State.Wave.LivingMonsterIds.First();
            match.Sim.RecordMonsterKill(new MonsterKillRequest
            {
                MonsterId = victim,
                MonsterType = match.State.Monsters[victim].Type,
                Bounty = 25,
            });

            hud.Refresh();
            Assert.That(hud.Scrip, Is.EqualTo(scripBefore + 25), "R-20: the bounty ticks the pool");
            Assert.That(hud.MonstersRemaining, Is.EqualTo(monstersBefore - 1),
                "R-61: one fewer monster remains");
        }

        /// <summary>
        /// R-32 / R-61 — the cooldown sweep, off <see cref="Hero.CooldownReadyAt"/>: an ABSENT key
        /// means ready, a future ready-at counts down, and the deadline is INCLUSIVE — at now ==
        /// ready-at the slot is ready with zero remaining.
        /// </summary>
        [Test]
        public void Cooldown_sweeps_treat_an_absent_key_as_ready_and_the_deadline_as_inclusive()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            lobby.Session.Step(Step60Hz);

            var hero = HeroFor(match.State, HostAccount);
            var hud = NewHud(lobby, HostAccount);
            hud.Refresh();

            var idle = hud.SlotFor(AbilitySlot.Q);
            Assert.That(idle.Ready, Is.True, "R-32: an absent CooldownReadyAt key means ready");
            Assert.That(idle.CooldownRemainingSeconds, Is.EqualTo(0.0).Within(SimTolerance),
                "with nothing left on the sweep");

            var now = match.Clock.ElapsedSeconds;
            hero.CooldownReadyAt[AbilitySlot.Q] = now + 4.0;
            hud.Refresh();

            var cooling = hud.SlotFor(AbilitySlot.Q);
            Assert.That(cooling.Ready, Is.False, "R-32: a future ready-at is a cooling slot");
            Assert.That(cooling.CooldownRemainingSeconds, Is.EqualTo(4.0).Within(ClockTolerance),
                "R-61: the sweep shows the seconds left");

            hero.CooldownReadyAt[AbilitySlot.Q] = match.Clock.ElapsedSeconds;
            hud.Refresh();

            var atDeadline = hud.SlotFor(AbilitySlot.Q);
            Assert.That(atDeadline.Ready, Is.True,
                "deadlines are INCLUSIVE repo-wide: at now == ready-at the slot is ready");
            Assert.That(atDeadline.CooldownRemainingSeconds, Is.EqualTo(0.0).Within(SimTolerance),
                "and the sweep is done");
        }

        /// <summary>
        /// R-31 / R-42 / R-61 — locked slots show the padlock until a point unlocks them, and the
        /// level/XP/badge readouts come from the PROFILE STORE (the handoff's named source), not
        /// from anything recomputed in the UI.
        /// </summary>
        [Test]
        public void Locked_slots_level_and_the_badge_follow_the_profile()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            lobby.Session.Step(Step60Hz);

            var hud = NewHud(lobby, HostAccount);
            hud.Refresh();

            Assert.That(hud.SlotFor(AbilitySlot.Q).Locked, Is.True,
                "R-31: a fresh account's Q is locked → padlock");
            Assert.That(hud.SlotFor(AbilitySlot.E).Locked, Is.True, "R-31: and E");
            Assert.That(hud.Level, Is.EqualTo(1), "R-41: fresh account, level 1");
            Assert.That(hud.SkillPointBadge, Is.False, "R-61: no badge with nothing banked");

            // 350 lifetime XP crosses the 100 and 300 thresholds: level 3, two points banked.
            AwardXp(match, HostAccount, 350);
            hud.Refresh();

            var profile = lobby.Profiles.Load(HostAccount);
            Assert.That(hud.Level, Is.EqualTo(profile.Level), "R-61: the level is the profile's");
            Assert.That(hud.Level, Is.GreaterThan(1), "sanity (R-41): 350 XP levelled the account");
            Assert.That(hud.LifetimeXp, Is.EqualTo(profile.LifetimeXp).Within(SimTolerance),
                "R-61: the XP bar is the profile's lifetime figure");
            Assert.That(hud.UnspentSkillPoints, Is.EqualTo(profile.SkillPoints),
                "R-61: unspent points are the profile's");
            Assert.That(hud.SkillPointBadge, Is.True, "R-61: banked points → badge");

            var spend = hud.Spend("unlock_Q");
            Assert.That(spend.Accepted, Is.True, "R-42: a banked point unlocks Q");
            hud.Refresh();

            Assert.That(hud.SlotFor(AbilitySlot.Q).Locked, Is.False,
                "R-31: the padlock comes off an unlocked slot");
            Assert.That(hud.SlotFor(AbilitySlot.Q).Rank, Is.EqualTo(1), "at rank 1");
            Assert.That(hud.UnspentSkillPoints, Is.EqualTo(profile.SkillPoints),
                "R-61: the badge count follows the store after the spend");
        }

        // ==========================================================================================
        //  S4 — the level-up overlay (R-62 / R-42)
        // ==========================================================================================

        /// <summary>
        /// R-62 — a `level_up` event raises the toast (and the badge, covered above); the picker's
        /// cards follow the profile: unlock for locked abilities, rank-up for unlocked ones below
        /// the configured max — and nothing for an ability already at max rank.
        /// </summary>
        [Test]
        public void The_level_up_toast_fires_and_the_picker_offers_the_lawful_choices()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            lobby.Session.Step(Step60Hz);

            var hud = NewHud(lobby, HostAccount);
            hud.Refresh();

            hud.OnSimEvent(new SimEvent("level_up", new Dictionary<string, object>
            {
                { "hero_id", HeroFor(match.State, HostAccount).Id },
                { "new_level", 2 },
            }));

            Assert.That(hud.Toasts, Is.Not.Empty, "R-62: levelling up raises a toast");
            Assert.That(hud.Toasts.Last().Kind, Is.EqualTo(HudToastKind.LevelUp),
                "of the level-up kind (its copy is presentation)");

            // 1000 lifetime XP → level 5 → four banked points: enough to max one ability.
            AwardXp(match, HostAccount, 1000);
            hud.Refresh();

            var choices = hud.PickerChoices.Select(c => c.Choice).ToList();
            Assert.That(choices, Does.Contain("unlock_Q"), "R-42: a locked Q offers its unlock");
            Assert.That(choices, Does.Contain("unlock_E"), "R-42: a locked E offers its unlock");
            Assert.That(choices, Does.Not.Contain("rank_Q").And.Not.Contain("rank_E"),
                "R-42: nothing unlocked → nothing to rank up");

            Assert.That(hud.Spend("unlock_Q").Accepted, Is.True, "unlock Q");
            hud.Refresh();

            choices = hud.PickerChoices.Select(c => c.Choice).ToList();
            Assert.That(choices, Does.Contain("rank_Q"), "R-42: an unlocked Q offers its rank-up");
            Assert.That(choices, Does.Not.Contain("unlock_Q"),
                "R-42: an unlocked ability cannot be unlocked again");
            Assert.That(choices, Does.Contain("unlock_E"), "E is still locked and still offered");

            // Rank Q to the configured max.
            for (var rank = 1; rank < lobby.SimConfig.MaxAbilityRank; rank++)
            {
                Assert.That(hud.Spend("rank_Q").Accepted, Is.True, "rank Q to " + (rank + 1));
            }

            hud.Refresh();
            choices = hud.PickerChoices.Select(c => c.Choice).ToList();
            Assert.That(choices, Does.Not.Contain("rank_Q"),
                "R-42: an ability at max rank (" + lobby.SimConfig.MaxAbilityRank
                + ") offers nothing further");
        }

        /// <summary>
        /// R-62 — the whole point of the requirement: the picker is a NON-BLOCKING overlay. With
        /// it open, sim time advances by every delta, the world keeps moving, spending a point is
        /// an ordinary command, and <c>Time.timeScale</c> is never touched.
        /// </summary>
        [Test]
        public void Opening_the_level_up_picker_never_pauses_the_sim()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            lobby.Session.Step(Step60Hz);

            AwardXp(match, HostAccount, 350);

            var hud = NewHud(lobby, HostAccount);
            hud.Refresh();

            var walker = match.State.Monsters.Values.FirstOrDefault(
                m => m.Alive && !string.IsNullOrEmpty(m.TargetId));
            Assert.That(walker, Is.Not.Null, "sanity (R-16): a targeted monster to watch");
            var posBefore = walker.Pos;
            var clockBefore = match.Clock.ElapsedSeconds;

            hud.OpenPicker();
            Assert.That(hud.PickerOpen, Is.True,
                "R-62: hotkey L / badge click opens the picker");

            const int Steps = 120;
            for (var i = 0; i < Steps; i++)
            {
                lobby.Session.Step(Step60Hz);
            }

            Assert.That(match.Clock.ElapsedSeconds,
                Is.EqualTo(clockBefore + (Steps * Step60Hz)).Within(SimTolerance),
                "R-62: the sim NEVER pauses for the level-up overlay — every delta lands");
            Assert.That(posBefore.DistanceTo(walker.Pos), Is.GreaterThan(0.0),
                "R-62: the world keeps moving under the open picker");
            Assert.That(Time.timeScale, Is.EqualTo(1f),
                "R-62: and never by way of Time.timeScale");

            var spend = hud.Spend("unlock_Q");
            Assert.That(spend.Accepted, Is.True,
                "R-62: SpendSkillPoint is a normal command issued mid-combat, not a modal result");

            hud.ClosePicker();
            Assert.That(hud.PickerOpen, Is.False, "and the picker closes");
        }

        /// <summary>
        /// R-42 — a rejected spend surfaces the `spend_rejected` reason string, verbatim, and
        /// changes nothing (G-026: the point stays banked — here there was never one).
        /// </summary>
        [Test]
        public void A_rejected_spend_surfaces_the_reason_string()
        {
            var lobby = NewTwoPlayerMatch();
            lobby.Session.Step(Step60Hz);

            // The guest never earned a point.
            var hud = NewHud(lobby, GuestAccount);
            hud.Refresh();
            Assert.That(hud.UnspentSkillPoints, Is.EqualTo(0), "sanity: nothing banked");

            var result = hud.Spend("unlock_Q");

            Assert.That(result.Accepted, Is.False, "R-42 / G-026: no point, no spend");
            Assert.That(result.RejectionReason, Is.Not.Null.And.Not.Empty,
                "R-42: a rejected spend carries a reason");
            Assert.That(hud.LastSpendRejection, Is.EqualTo(result.RejectionReason),
                "the UI surfaces the spend_rejected reason, verbatim");
        }

        // ==========================================================================================
        //  S4 — dead-hero spectate (R-33)
        // ==========================================================================================

        /// <summary>
        /// R-33 / R-60, wireframe S4 state: "hero dead → grey overlay 'Respawning in Ns', camera
        /// follows a living ally". The countdown is R-33's configured delay counting down against
        /// sim time (inclusive deadline, never negative), the camera target is a LIVING ally, and
        /// the respawn takes the overlay down again.
        /// </summary>
        [Test]
        public void A_dead_hero_spectates_a_living_ally_until_the_respawn()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            lobby.Session.Step(Step60Hz);

            var guestHero = HeroFor(match.State, GuestAccount);
            var hostHero = HeroFor(match.State, HostAccount);
            var guestHeroId = guestHero.Id;

            var hud = NewHud(lobby, GuestAccount);
            hud.Refresh();
            Assert.That(hud.SpectateOverlayVisible, Is.False, "alive → no overlay");

            match.Sim.ApplyHeroDamage(new HeroDamageRequest
            {
                AttackerId = "m_killer",
                AttackerType = MonsterType.Ravager,
                Damage = guestHero.MaxHp * 10.0,
                TargetId = guestHeroId,
            });
            Assert.That(guestHero.Alive, Is.False, "sanity (R-33): the hero is down");

            hud.Refresh();

            Assert.That(hud.SpectateOverlayVisible, Is.True,
                "R-33: a dead hero gets the spectate overlay");
            Assert.That(hud.RespawnInSeconds,
                Is.EqualTo(lobby.SimConfig.RespawnDelaySeconds).Within(0.25),
                "R-33: the countdown opens on the configured delay, not a literal");
            Assert.That(hud.SpectateTargetHeroId, Is.EqualTo(hostHero.Id),
                "the camera follows the living ally — the one hero still standing");

            for (var i = 0; i < 60; i++)
            {
                lobby.Session.Step(Step60Hz);
            }

            hud.Refresh();
            Assert.That(hud.RespawnInSeconds,
                Is.EqualTo(lobby.SimConfig.RespawnDelaySeconds - 1.0).Within(0.25),
                "R-33: one second of sim time is one second off the countdown");
            Assert.That(hud.RespawnInSeconds, Is.GreaterThanOrEqualTo(0.0),
                "the countdown clamps at zero (the deadline is inclusive)");

            var respawned = DriveUntil(
                lobby.Session, match.Clock,
                () => match.State.Heroes.TryGetValue(guestHeroId, out var h) && h.Alive,
                budgetSeconds: lobby.SimConfig.RespawnDelaySeconds + 5.0);
            Assert.That(respawned, Is.True, "R-33: the hero respawns on the timer");

            hud.Refresh();
            Assert.That(hud.SpectateOverlayVisible, Is.False,
                "R-33: the respawn takes the overlay down");
        }

        // ==========================================================================================
        //  S4 — colony feedback states
        // ==========================================================================================

        /// <summary>
        /// R-13 / R-60, wireframe S4 state: "civilians_killed → red flash + toast 'Civilians lost
        /// at the Chapel!'". A kill event raises both, naming the hotspot; a zero-count event (a
        /// hit on an already-empty shelter) raises neither — flashing red for nobody dying is
        /// crying wolf.
        /// </summary>
        [Test]
        public void Civilians_lost_raises_the_toast_and_the_red_flash()
        {
            var lobby = NewTwoPlayerMatch();
            lobby.Session.Step(Step60Hz);

            var hud = NewHud(lobby, HostAccount);
            hud.Refresh();
            Assert.That(hud.RedFlashActive, Is.False, "no flash before anything happened");

            hud.OnSimEvent(new SimEvent("civilians_killed", new Dictionary<string, object>
            {
                { "hotspot_id", "hs_chapel" },
                { "count", 2 },
            }));

            Assert.That(hud.RedFlashActive, Is.True, "R-13: civilians died → red flash");
            Assert.That(hud.Toasts, Is.Not.Empty, "and a toast");
            Assert.That(hud.Toasts.Last().Kind, Is.EqualTo(HudToastKind.CiviliansLost),
                "of the civilians-lost kind");
            Assert.That(hud.Toasts.Last().SubjectId, Is.EqualTo("hs_chapel"),
                "naming WHERE — the copy ('at the Chapel!') is presentation, the hotspot is contract");

            var toastsBefore = hud.Toasts.Count;
            var freshHud = NewHud(lobby, HostAccount);
            freshHud.Refresh();
            freshHud.OnSimEvent(new SimEvent("civilians_killed", new Dictionary<string, object>
            {
                { "hotspot_id", "hs_chapel" },
                { "count", 0 },
            }));

            Assert.That(freshHud.RedFlashActive, Is.False,
                "R-13: a hit that killed nobody flashes nothing");
            Assert.That(freshHud.Toasts, Is.Empty, "and toasts nothing");
            Assert.That(hud.Toasts.Count, Is.EqualTo(toastsBefore),
                "sanity: models do not share toast state");
        }

        /// <summary>
        /// R-12 / R-60, wireframe S4 state: "hotspot emptied → building marked dark/lost". The
        /// dark is presentation; the pinned contract is the per-hotspot Lost flag: true for the
        /// emptied shelter, false for the ones still standing.
        /// </summary>
        [Test]
        public void The_lost_hotspot_flag_follows_the_emptied_shelter_and_only_it()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            lobby.Session.Step(Step60Hz);

            var hud = NewHud(lobby, HostAccount);
            hud.Refresh();
            Assert.That(hud.Hotspots.Any(h => h.Lost), Is.False,
                "nothing is lost while every shelter holds civilians");

            // Empty the saloon through the sim (R-11: damage → kills). 8 civilians at 10 HP each.
            var beforeOver = match.State.IsOver;
            match.Sim.ApplyHotspotAttack(new HotspotAttackRequest
            {
                AttackerId = "m_raider",
                AttackerType = MonsterType.BullBehemoth,
                Damage = 1000.0,
                TargetId = "hs_saloon",
            });
            Assert.That(match.State.Hotspots["hs_saloon"].Civilians, Is.EqualTo(0),
                "sanity: the saloon is emptied");
            Assert.That(match.State.IsOver, Is.EqualTo(beforeOver),
                "sanity (R-02): one lost shelter of three does not end the match");

            hud.OnSimEvent(new SimEvent("hotspot_emptied", new Dictionary<string, object>
            {
                { "hotspot_id", "hs_saloon" },
            }));
            hud.Refresh();

            var saloon = hud.Hotspots.Single(h => h.HotspotId == "hs_saloon");
            Assert.That(saloon.Lost, Is.True,
                "R-12: the emptied shelter is marked lost (the dark is presentation)");
            Assert.That(saloon.Civilians, Is.EqualTo(0), "with its zero on the bar");
            Assert.That(hud.Hotspots.Where(h => h.HotspotId != "hs_saloon").All(h => !h.Lost),
                Is.True, "and ONLY it — the standing shelters are not painted lost");
        }

        /// <summary>
        /// R-60, wireframe S4 state: "monster spawn → entry point flare". The `wave_spawned` event
        /// names no tunnels (DEC-018 keeps the table host-side), so the flare targets are the
        /// entries the planning preview named, carried across the phase change.
        /// </summary>
        [Test]
        public void A_wave_spawn_flares_the_previewed_entry_points()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            DriveToPlanning(lobby, match);

            var preview = match.Sim.PreviewUpcomingWave();
            Assert.That(preview.ActiveEntryTunnels, Is.Not.Empty, "sanity: breaches will open");

            var hud = NewHud(lobby, HostAccount);
            hud.Refresh();
            hud.SetExpectedEntryTunnels(preview.ActiveEntryTunnels);
            Assert.That(hud.EntryFlares, Is.Empty, "no flare before the wave arrives");

            hud.OnSimEvent(new SimEvent("wave_spawned", new Dictionary<string, object>
            {
                { "wave", match.State.Wave.Number },
                { "monster_count", 5 },
            }));

            Assert.That(hud.EntryFlares, Is.EqualTo(preview.ActiveEntryTunnels),
                "the flare fires at the entries the wave was previewed to use — all of them and "
                + "no others");
        }

        /// <summary>
        /// R-53 / R-60, cross-cutting: "player disconnects mid-match → toast shown; match
        /// continues". The session already despawns and retargets (T11); what S4 owes the player
        /// is the toast, of the right kind, about the right peer.
        /// </summary>
        [Test]
        public void A_player_disconnect_shows_a_toast_and_the_match_continues()
        {
            var lobby = NewTwoPlayerMatch();
            var match = lobby.Session.Match;
            lobby.Session.Step(Step60Hz);

            var hud = NewHud(lobby, HostAccount);
            hud.Refresh();

            lobby.Session.Disconnect(GuestPeerId);
            var notice = lobby.Session.Notices.Last();
            Assert.That(notice.Kind, Is.EqualTo(SessionNoticeKind.PlayerDisconnected),
                "sanity (T11): the session raised the disconnect notice");

            hud.OnSessionNotice(notice);

            Assert.That(hud.Toasts, Is.Not.Empty, "R-53: a toast is shown");
            Assert.That(hud.Toasts.Last().Kind, Is.EqualTo(HudToastKind.PlayerDisconnected),
                "of the disconnect kind (copy not asserted)");
            Assert.That(hud.Toasts.Last().SubjectId, Is.EqualTo(GuestPeerId),
                "naming who left");

            Assert.That(match.State.IsOver, Is.False, "R-53: the match continues");
            Assert.That(lobby.Session.Phase, Is.EqualTo(NetSessionPhase.InMatch),
                "R-53: and the session is still in it");
        }

        // ==========================================================================================
        //  scenario builders
        // ==========================================================================================

        private sealed class Lobby
        {
            public SimConfig SimConfig;
            public InMemoryProfileStore Profiles;
            public NetSession Session;
        }

        /// <summary>A hosted two-player loopback lobby with the match already started.</summary>
        private static Lobby NewTwoPlayerMatch()
        {
            var simConfig = new SimConfig();
            var profiles = new InMemoryProfileStore();
            var session = new NetSession(
                new NetSessionConfig(),
                new LoopbackNetTransport(),
                new ColonyMatchFactory(ColonyMap.V1(), simConfig, profiles));

            session.StartHost(NewPeer(HostPeerId, HostAccount, HeroClass.Gunslinger, isHost: true));
            Assert.That(
                session.TryJoin(NewPeer(GuestPeerId, GuestAccount, HeroClass.Sawbones)),
                Is.True, "R-50: a second player joins");
            Assert.That(session.TryStartMatch(HostPeerId), Is.True, "the match starts");

            return new Lobby { SimConfig = simConfig, Profiles = profiles, Session = session };
        }

        private static NetPeer NewPeer(string peerId, string accountId, string heroClass, bool isHost = false)
        {
            return new NetPeer
            {
                PeerId = peerId,
                AccountId = accountId,
                HeroClass = heroClass,
                IsHost = isHost,
            };
        }

        /// <summary>
        /// Clear wave 1 and step the session until the campaign is planning wave 2 — the state
        /// every S3 test starts from. (A fresh match opens in combat with wave 1 spawned, R-19.)
        /// </summary>
        private static void DriveToPlanning(Lobby lobby, HostedMatch match)
        {
            foreach (var id in match.State.Wave.LivingMonsterIds.ToList())
            {
                match.Sim.RecordMonsterKill(new MonsterKillRequest
                {
                    MonsterId = id,
                    MonsterType = match.State.Monsters[id].Type,
                    Bounty = 0,
                });
            }

            var arrived = DriveUntil(
                lobby.Session, match.Clock,
                () => match.State.Phase == MatchPhase.Planning && match.State.Wave.Number == 2,
                budgetSeconds: 5.0);

            Assert.That(arrived, Is.True,
                "the campaign must reach wave 2's planning phase (it is on wave "
                + match.State.Wave.Number + ", phase '" + match.State.Phase + "')");
        }

        private static PlanningScreenModel NewPlanningModel(Lobby lobby, string accountId)
        {
            var playerId = lobby.Session.Match.State.Players
                .First(p => p.AccountId == accountId).Id;
            return new PlanningScreenModel(lobby.Session.Match, playerId);
        }

        private static CombatHudModel NewHud(Lobby lobby, string accountId)
        {
            return new CombatHudModel(lobby.Session.Match, accountId, lobby.Profiles);
        }

        private static Hero HeroFor(MatchState state, string accountId)
        {
            var heroes = state.Heroes.Values.Where(h => h.AccountId == accountId).ToList();
            Assert.That(heroes.Count, Is.EqualTo(1),
                "R-50 / R-31: exactly one hero for account '" + accountId + "'");
            return heroes[0];
        }

        /// <summary>Bank lifetime XP for an account through the sim's own award (R-40).</summary>
        private static void AwardXp(HostedMatch match, string accountId, int amount)
        {
            match.Sim.AwardKillXp(
                new MonsterKillRequest
                {
                    MonsterId = "m_scored_" + accountId + "_" + amount,
                    MonsterType = MonsterType.Shambler,
                    Bounty = amount,
                    KillerHeroId = HeroFor(match.State, accountId).Id,
                },
                accountId);
        }

        private static bool DriveUntil(
            NetSession session, SimClock clock, Func<bool> done, double budgetSeconds)
        {
            var deadline = clock.ElapsedSeconds + budgetSeconds;
            var maxSteps = (int)(budgetSeconds / Step60Hz) + 64;

            for (var i = 0; i < maxSteps; i++)
            {
                if (done())
                {
                    return true;
                }

                session.Step(Step60Hz);

                if (clock.ElapsedSeconds > deadline)
                {
                    break;
                }
            }

            return done();
        }

        /// <summary>Every type a member's public shape involves: return, field, property, parameters.</summary>
        private static IEnumerable<Type> TypesInvolvedIn(MemberInfo member)
        {
            switch (member)
            {
                case FieldInfo field:
                    yield return Unwrap(field.FieldType);
                    break;
                case PropertyInfo property:
                    yield return Unwrap(property.PropertyType);
                    break;
                case MethodInfo method:
                    yield return Unwrap(method.ReturnType);
                    foreach (var parameter in method.GetParameters())
                    {
                        yield return Unwrap(parameter.ParameterType);
                    }

                    break;
            }
        }

        /// <summary>Sees through List&lt;T&gt;/IReadOnlyList&lt;T&gt;-style wrappers and arrays.</summary>
        private static Type Unwrap(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            if (type.IsGenericType && type.GetGenericArguments().Length == 1)
            {
                return type.GetGenericArguments()[0];
            }

            return type;
        }
    }
}
