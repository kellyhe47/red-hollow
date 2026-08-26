using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RedHollow.Game.Input;
using RedHollow.Game.Net;
using RedHollow.Game.UI;
using RedHollow.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace RedHollow.Tests.EditMode
{
    /// <summary>
    /// Ticket 023 (T-23) — interactive UI controls. Found while closing 022: the shell has labels
    /// and screen roots but ZERO Buttons or InputFields — Play boots to a title screen nobody can
    /// click through. These tests pin the wireframe controls (docs/ui-wireframes.html is
    /// normative) wired to the EXISTING ticket-012 model actions, reached through the pinned
    /// accessor convention: <see cref="ShellBootstrap.Controls"/> properties (plus the
    /// <see cref="ShellBootstrap.Title"/> / <see cref="ShellBootstrap.Lobby"/> /
    /// <see cref="ShellBootstrap.Planning"/> model accessors the wiring composes over).
    ///
    /// Everything is driven the T21/T22 way: EditMode, no play mode — Button.onClick.Invoke()
    /// is the click, InputField .text + onValueChanged.Invoke is the typing, and the shell only
    /// ever moves through <see cref="ShellBootstrap.Pump"/>. Pointer-driven planning actions are
    /// pinned at the wiring seam (<see cref="ShellControls.ClickGround"/> takes a ground position,
    /// <see cref="ShellControls.ClickPlaceable"/> a placeable id) — resolving a screen ray to
    /// either is play-mode raycasting and is deliberately NOT pinned here.
    ///
    /// <b>Deliberately NOT pinned</b>: label/button copy, layout, colors (grey/red is
    /// presentation — the Affordable flag is the contract), the volume control on the ESC
    /// overlay, join SUCCESS from a second process (a loopback shell fronts one session; the
    /// only join outcome reachable here is the modeled failure), and whether MAIN MENU leaves
    /// the session re-hostable (see that test's remark — the session surface has no clean
    /// return-to-title today, and the honest pin is "S1 is shown", nothing more).
    /// </summary>
    [TestFixture]
    public class T23_InteractiveControlsTests
    {
        private const double Step60Hz = 1.0 / 60.0;
        private const double SimTolerance = 1e-6;

        private const string HostPeerId = "peer_host";
        private const string HostAccount = "acc_calamity";
        private const string GuestPeerId = "peer_guest";
        private const string GuestAccount = "acc_doc";

        /// <summary>The well-known roots the shell composes under (T21's teardown convention).</summary>
        private static readonly string[] ShellRootNames =
        {
            "RedHollow_Shell", "RedHollow_MatchViews", "RedHollow_Match",
        };

        private ShellBootstrap _shell;
        private InMemoryProfileStore _profiles;
        private FakeInputSource _input;

        [TearDown]
        public void DestroyEverythingThisTestBuilt()
        {
            if (_shell != null)
            {
                try
                {
                    _shell.TearDown();
                }
                catch (Exception)
                {
                    // A stub or half-built shell must not turn a red test into a teardown error.
                }

                _shell = null;
            }

            foreach (var name in ShellRootNames)
            {
                for (var go = GameObject.Find(name); go != null; go = GameObject.Find(name))
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        // ==========================================================================================
        //  S1 — Title / Join
        // ==========================================================================================

        /// <summary>
        /// The wireframe's S1 controls exist and live UNDER the Title screen root, so R-60's
        /// activation flipping shows and hides them with the screen. The accessor convention is
        /// the contract: <c>shell.Controls.&lt;Name&gt;</c>, never a scene-wide Find.
        /// </summary>
        [Test]
        public void S1_carries_its_controls_under_the_title_root()
        {
            var shell = NewShell();
            shell.Pump(0.0);

            var root = shell.Ui.ScreenRoot(UiScreen.Title);
            AssertControlUnder(shell.Controls.CallsignInput, root, "the callsign input");
            AssertControlUnder(shell.Controls.PlayMatchButton, root, "PLAY MATCH");
            AssertControlUnder(shell.Controls.HostButton, root, "HOST GAME");
            AssertControlUnder(shell.Controls.JoinCodeInput, root, "the join-code input");
            AssertControlUnder(shell.Controls.JoinButton, root, "JOIN");
            AssertControlUnder(shell.Controls.JoinErrorLabel, root, "the inline join error");

            var hostCaption = shell.Controls.HostButton.GetComponentInChildren<Text>(true);
            Assert.That(hostCaption, Is.Not.Null, "HOST GAME still has a caption");
            Assert.That(hostCaption.text, Is.EqualTo("HOST GAME"),
                "T-23: HOST GAME remains the LAN host label — PLAY MATCH is a separate control");
            var playCaption = shell.Controls.PlayMatchButton.GetComponentInChildren<Text>(true);
            Assert.That(playCaption, Is.Not.Null, "PLAY MATCH has a caption");
            Assert.That(playCaption.text, Is.EqualTo("PLAY MATCH"));
        }

        /// <summary>
        /// R-44 — typing a callsign IS logging in: the input routes to
        /// <see cref="TitleScreenModel.SetCallsign"/>, which loads the profile keyed to it. The
        /// seeded level proves the load actually happened (a fresh account would read 1).
        /// </summary>
        [Test]
        public void Typing_a_callsign_loads_that_accounts_profile()
        {
            var shell = NewShell();
            _profiles.Seed(new AccountProfile
            {
                AccountId = HostAccount,
                Level = 3,
                LifetimeXp = 450.0,
            });
            shell.Pump(0.0);

            Assert.That(shell.Title.ProfileLoaded, Is.False,
                "sanity: nothing typed, nothing loaded");

            TypeInto(shell.Controls.CallsignInput, HostAccount);
            shell.Pump(0.0);

            Assert.That(shell.Title.ProfileLoaded, Is.True,
                "R-44: typing the callsign loads the profile through the input's wiring");
            Assert.That(shell.Title.Callsign, Is.EqualTo(HostAccount),
                "the model holds the callsign as typed");
            Assert.That(shell.Title.Level, Is.EqualTo(3),
                "R-41: the SEEDED profile was loaded — a fresh account would read level 1");
        }

        /// <summary>
        /// R-50 — HOST GAME through the button alone: the session opens a lobby with the local
        /// peer seated as host, carrying the TYPED callsign as its account (R-44: the callsign is
        /// the account). The next pump routes S2 in.
        /// </summary>
        [Test]
        public void Host_game_click_opens_the_lobby_seated_as_the_typed_callsign()
        {
            var shell = NewShell();
            shell.Pump(0.0);

            TypeInto(shell.Controls.CallsignInput, HostAccount);
            shell.Controls.HostButton.onClick.Invoke();
            shell.Pump(0.0);

            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "R-50: HOST GAME opened the lobby through the session");
            AssertOnlyActiveScreen(shell, UiScreen.Lobby, "hosting lands on S2");

            Assert.That(shell.Session.Seats.Count, Is.EqualTo(1), "solo: the host's seat only");
            var seat = shell.Session.Seats[0];
            Assert.That(seat.IsHost, Is.True, "R-50: the first seat is the host");
            Assert.That(seat.PeerId, Is.EqualTo(HostPeerId),
                "the wiring hosts as the shell's own LocalPeerId");
            Assert.That(seat.AccountId, Is.EqualTo(HostAccount),
                "R-44: the seat carries the TYPED callsign as its account");
        }

        /// <summary>
        /// PLAY MATCH is the one-click path a human can find on S1: same four calls as
        /// GameEntryBehaviour.Start (SetCallsign, RequestHost, PickClass(Gunslinger),
        /// SetReady(true)). HOST GAME still only opens the lobby.
        /// </summary>
        [Test]
        public void Play_match_click_lands_in_the_live_match_without_a_lobby_hunt()
        {
            var shell = NewShell();
            shell.Pump(0.0);
            AssertOnlyActiveScreen(shell, UiScreen.Title, "a fresh shell is on S1");

            shell.Controls.PlayMatchButton.onClick.Invoke();
            shell.Pump(0.0);
            shell.Pump(0.0);

            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.InMatch),
                "PLAY MATCH hosted, picked Gunslinger, and readied — no lobby scavenger hunt");
            Assert.That(shell.Router.Screen,
                Is.EqualTo(UiScreen.Combat).Or.EqualTo(UiScreen.Planning),
                "the player is looking at the live match (planning or combat)");

            var hero = shell.Session.Match.State.Heroes.Values.FirstOrDefault(
                h => string.Equals(h.AccountId, "Kelly", StringComparison.Ordinal));
            Assert.That(hero, Is.Not.Null, "the started match seated Kelly");
            Assert.That(hero.HeroClass, Is.EqualTo(HeroClass.Gunslinger),
                "PLAY MATCH picks Gunslinger, matching GameEntryBehaviour.Start");
        }

        /// <summary>
        /// The wireframe's one S1 failure state: a join that cannot land raises the MODELED
        /// inline error (<see cref="TitleScreenModel.JoinError"/>), shows it on the inline label,
        /// and stays on S1 — and editing the code clears it again. A loopback shell fronts its
        /// own session (nothing is hosted here), so the failed path is the only join outcome
        /// EditMode can reach; join success belongs to the transport tickets.
        /// </summary>
        [Test]
        public void A_failed_join_shows_the_modeled_inline_error_and_stays_on_S1()
        {
            var shell = NewShell();
            shell.Pump(0.0);

            TypeInto(shell.Controls.JoinCodeInput, "NOPE42");
            shell.Controls.JoinButton.onClick.Invoke();
            shell.Pump(0.0);

            Assert.That(shell.Title.JoinError, Is.Not.Null.And.Not.Empty,
                "R-60/S1: the failed join raised the modeled inline error");
            Assert.That(shell.Controls.JoinErrorLabel.text, Is.Not.Empty,
                "the inline label under the code input shows the error (copy is free)");
            AssertOnlyActiveScreen(shell, UiScreen.Title,
                "a failed join STAYS on S1 — no screen change, no throw");
            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.Offline),
                "nothing was joined and nothing was hosted");

            TypeInto(shell.Controls.JoinCodeInput, "NOPE43");
            shell.Pump(0.0);

            Assert.That(shell.Title.JoinError, Is.Null,
                "editing the code clears the error — a stale error blames the wrong input");
            Assert.That(shell.Controls.JoinErrorLabel.text, Is.Empty,
                "and the inline label empties with it");
        }

        // ==========================================================================================
        //  S2 — Lobby
        // ==========================================================================================

        /// <summary>The three class cards' PICK buttons and READY, under the Lobby root.</summary>
        [Test]
        public void S2_carries_a_pick_button_per_class_and_ready_under_the_lobby_root()
        {
            var shell = NewHostedShell();
            shell.Pump(0.0);

            var root = shell.Ui.ScreenRoot(UiScreen.Lobby);
            AssertControlUnder(shell.Controls.ClassPickButton(HeroClass.Gunslinger), root,
                "the gunslinger PICK");
            AssertControlUnder(shell.Controls.ClassPickButton(HeroClass.Rancher), root,
                "the rancher PICK");
            AssertControlUnder(shell.Controls.ClassPickButton(HeroClass.Sawbones), root,
                "the sawbones PICK");
            AssertControlUnder(shell.Controls.LobbyReadyButton, root, "READY");
            AssertControlUnder(shell.Controls.LobbyJoinCodeLabel, root, "the join code to share");
        }

        /// <summary>
        /// Wireframe S2: HOST GAME shows a join code, and waiting alone hints "share code". The
        /// label must carry the session's live code so a second player can type it on S1.
        /// </summary>
        [Test]
        public void S2_shows_the_session_join_code_to_share()
        {
            var shell = NewHostedShell();
            shell.Pump(0.0);

            var code = shell.Session.JoinCode;
            Assert.That(code, Is.Not.Null.And.Not.Empty, "sanity (R-50): a hosted lobby has a code");
            Assert.That(shell.Lobby.WaitingAlone, Is.True, "solo host is the share-code state");
            Assert.That(shell.Controls.LobbyJoinCodeLabel.text, Does.Contain(code),
                "S2: the code on screen is the session's, so the one that works");
            Assert.That(shell.Controls.LobbyJoinCodeLabel.text.ToUpperInvariant(), Does.Contain("SHARE"),
                "S2: waiting alone shows the share-code hint");
        }

        /// <summary>
        /// S2's whole job through clicks: PICK routes to <see cref="LobbyScreenModel.PickClass"/>
        /// (a re-pick re-picks), READY to <see cref="LobbyScreenModel.SetReady"/>, and the model's
        /// own all-ready rule starts the solo match on the next pump — no direct session call, no
        /// host force-start button (the wireframe has none).
        /// </summary>
        [Test]
        public void Pick_and_ready_clicks_start_the_solo_match_through_the_model()
        {
            var shell = NewHostedShell();
            shell.Pump(0.0);
            AssertOnlyActiveScreen(shell, UiScreen.Lobby, "a hosted session is on S2");

            shell.Controls.ClassPickButton(HeroClass.Gunslinger).onClick.Invoke();
            Assert.That(shell.Session.Seats[0].HeroClass, Is.EqualTo(HeroClass.Gunslinger),
                "PICK routes to the model, which writes the local seat");

            shell.Controls.ClassPickButton(HeroClass.Rancher).onClick.Invoke();
            Assert.That(shell.Session.Seats[0].HeroClass, Is.EqualTo(HeroClass.Rancher),
                "R-31: a re-pick re-picks — the card is a choice, not a lock");

            shell.Pump(0.0);
            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "anti-vacuity: picking alone starts nothing — READY is the trigger");

            shell.Controls.LobbyReadyButton.onClick.Invoke();
            shell.Pump(0.0);
            shell.Pump(0.0);

            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.InMatch),
                "R-50: a solo lobby needs only your own READY — the model's all-ready rule "
                + "started the match through the pump");
            var hero = OwnHero(shell.Session.Match.State);
            Assert.That(hero, Is.Not.Null, "the started match seated the local hero");
            Assert.That(hero.HeroClass, Is.EqualTo(HeroClass.Rancher),
                "the hero wears the LAST pick made before ready");
        }

        // ==========================================================================================
        //  S3 — Planning: shop bar, ghost placement, sell, ready up
        // ==========================================================================================

        /// <summary>
        /// R-63 — one shop button per R-23 catalog row, under the Planning root, each
        /// non-interactable exactly when the modeled <see cref="ShopItem.Affordable"/> flag is
        /// false. The scrip is engineered so BOTH states exist — asserting equality against an
        /// all-true column would pin nothing.
        /// </summary>
        [Test]
        public void Shop_buttons_exist_per_catalog_item_and_follow_the_affordable_flag()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            var costs = shell.Planning.ShopItems.Select(i => i.Cost).ToList();
            Assert.That(costs.Min(), Is.LessThan(costs.Max()),
                "sanity (R-23): the catalog prices differ, so a mid-range pool splits them");

            match.State.Team.Scrip = costs.Max() - 1;
            shell.Pump(0.0);

            var root = shell.Ui.ScreenRoot(UiScreen.Planning);
            var flags = new HashSet<bool>();
            foreach (var item in shell.Planning.ShopItems)
            {
                var button = shell.Controls.ShopItemButton(item.Type);
                AssertControlUnder(button, root, "the " + item.Type + " shop button");
                Assert.That(button.interactable, Is.EqualTo(item.Affordable),
                    "R-63: " + item.Type + "'s button interactability IS the modeled Affordable "
                    + "flag — grey/red styling stays presentation");
                flags.Add(item.Affordable);
            }

            Assert.That(flags, Is.EquivalentTo(new[] { true, false }),
                "anti-vacuity: the engineered pool leaves some items affordable and some not");

            AssertControlUnder(shell.Controls.PlanningReadyButton, root, "READY UP");
        }

        /// <summary>
        /// R-63's happy path through the wiring seam: shop click → ghost up; pointer → ghost
        /// follows; ground click in a valid zone → ONE catalog-priced purchase lands at that
        /// position and the ghost clears. Every expected number is read off the catalog/model.
        /// </summary>
        [Test]
        public void Shop_click_then_ground_click_places_the_item_at_the_clicked_position()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            var item = shell.Planning.ShopItems.OrderBy(i => i.Cost).First();
            Assert.That(item.Affordable, Is.True, "sanity: the cheapest item is affordable");

            shell.Controls.ShopItemButton(item.Type).onClick.Invoke();
            Assert.That(shell.Planning.GhostActive, Is.True,
                "R-63: clicking a shop item starts the ghost");
            Assert.That(shell.Planning.GhostType, Is.EqualTo(item.Type),
                "the ghost is the clicked item");

            var hover = new Vec2(2.0, 3.0);
            shell.Controls.PointerAt(hover, zoneValid: true);
            Assert.That(shell.Planning.GhostPos, Is.EqualTo(hover),
                "the ghost follows the pointer through the wiring seam");

            var scripBefore = match.State.Team.Scrip;
            var placeablesBefore = match.State.Placeables.Keys.ToList();
            var placeAt = new Vec2(4.0, 5.0);

            shell.Controls.ClickGround(placeAt, zoneValid: true);
            shell.Pump(0.0);

            var placed = match.State.Placeables
                .Where(kv => !placeablesBefore.Contains(kv.Key))
                .Select(kv => kv.Value)
                .ToList();
            Assert.That(placed.Count, Is.EqualTo(1),
                "R-63: one ground click is ONE purchase command");
            Assert.That(placed[0].Type, Is.EqualTo(item.Type), "the clicked item was placed");
            Assert.That(placed[0].Pos.X, Is.EqualTo(placeAt.X).Within(SimTolerance),
                "placed where the click landed (x)");
            Assert.That(placed[0].Pos.Y, Is.EqualTo(placeAt.Y).Within(SimTolerance),
                "placed where the click landed (y)");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore - item.Cost),
                "R-23: the shared pool paid the CATALOG price, never a UI literal");
            Assert.That(shell.Planning.GhostActive, Is.False,
                "R-63: an accepted placement clears the ghost");
        }

        /// <summary>
        /// R-24 — a click in an invalid zone is a rejected purchase: nothing is placed, nothing
        /// is charged, the ghost STAYS up for the retry, and the modeled rejection reason is
        /// surfaced (the shake is presentation).
        /// </summary>
        [Test]
        public void An_invalid_zone_click_rejects_and_keeps_the_ghost_up()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            var item = shell.Planning.ShopItems.OrderBy(i => i.Cost).First();
            shell.Controls.ShopItemButton(item.Type).onClick.Invoke();

            var scripBefore = match.State.Team.Scrip;
            var countBefore = match.State.Placeables.Count;

            shell.Controls.ClickGround(new Vec2(1.0, 1.0), zoneValid: false);
            shell.Pump(0.0);

            Assert.That(match.State.Placeables.Count, Is.EqualTo(countBefore),
                "R-24: an invalid-zone click places nothing");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore),
                "R-24: a rejected purchase charges nothing");
            Assert.That(shell.Planning.GhostActive, Is.True,
                "R-63: the rejected ghost stays up for the retry");
            Assert.That(shell.Planning.LastPurchaseRejection, Is.Not.Null.And.Not.Empty,
                "the modeled rejection reason is surfaced for the UI");
        }

        /// <summary>
        /// R-22 — clicking a standing placeable (no ghost up) is the SELL path: the placeable
        /// goes away and the pool is credited exactly the MODELED refund
        /// (<see cref="PlanningScreenModel.SellRefundFor"/> — the tooltip's own figure), never a
        /// literal.
        /// </summary>
        [Test]
        public void Clicking_a_standing_placeable_sells_it_for_the_modeled_refund()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            var item = shell.Planning.ShopItems.OrderBy(i => i.Cost).First();
            var before = match.State.Placeables.Keys.ToList();
            shell.Controls.ShopItemButton(item.Type).onClick.Invoke();
            shell.Controls.ClickGround(new Vec2(4.0, 5.0), zoneValid: true);
            shell.Pump(0.0);

            var placedId = match.State.Placeables.Keys.Single(k => !before.Contains(k));
            var expectedRefund = shell.Planning.SellRefundFor(placedId);
            Assert.That(expectedRefund, Is.GreaterThan(0),
                "sanity (R-22): the modeled refund for a standing placeable is positive");

            var scripBefore = match.State.Team.Scrip;
            var standingBefore = match.State.PlaceableCount;

            shell.Controls.ClickPlaceable(placedId);
            shell.Pump(0.0);

            Assert.That(match.State.PlaceableCount, Is.EqualTo(standingBefore - 1),
                "R-22: the clicked placeable no longer stands");
            Assert.That(match.State.Team.Scrip, Is.EqualTo(scripBefore + expectedRefund),
                "R-22: the pool was credited exactly the refund the tooltip modeled");
            Assert.That(shell.Planning.LastSellRefused, Is.False,
                "an accepted sale raises no refusal flag");
        }

        /// <summary>
        /// R-03 — READY UP through the button: the solo player's ready is everyone's ready, so
        /// combat starts early and the next pump routes S4 in.
        /// </summary>
        [Test]
        public void Ready_up_click_starts_combat_early()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            ReachPlanning(shell, match);

            shell.Controls.PlanningReadyButton.onClick.Invoke();
            shell.Pump(0.0);

            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Combat),
                "R-03: the solo READY UP is all-ready — combat opened early");
            AssertOnlyActiveScreen(shell, UiScreen.Combat, "combat is S4");
        }

        // ==========================================================================================
        //  S4 — Combat: level-up badge, picker cards, L hotkey, ESC overlay
        // ==========================================================================================

        /// <summary>
        /// R-61/R-62 — the badge is visible exactly while a point is banked
        /// (<see cref="CombatHudModel.SkillPointBadge"/>), and clicking it opens the picker
        /// without touching the sim, the clock, or the session.
        /// </summary>
        [Test]
        public void The_levelup_badge_mirrors_the_banked_point_and_opens_the_picker()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            shell.Pump(0.0);
            AssertOnlyActiveScreen(shell, UiScreen.Combat, "a started match is on S4");

            Assert.That(shell.Hud.SkillPointBadge, Is.False, "sanity: nothing banked yet");
            Assert.That(shell.Controls.LevelUpBadgeButton.gameObject.activeInHierarchy, Is.False,
                "R-61: no banked point, no badge");

            _profiles.Load(HostAccount).SkillPoints = 1;
            shell.Pump(0.0);

            Assert.That(shell.Controls.LevelUpBadgeButton.gameObject.activeInHierarchy, Is.True,
                "R-61: the badge shows exactly when a point is banked");

            var clockBefore = match.Clock.ElapsedSeconds;
            shell.Controls.LevelUpBadgeButton.onClick.Invoke();

            Assert.That(shell.Hud.PickerOpen, Is.True,
                "R-62: clicking the badge opens the picker");
            Assert.That(match.Clock.ElapsedSeconds, Is.EqualTo(clockBefore),
                "R-62: opening the picker stops NOTHING — the clock did not move because of it");
        }

        /// <summary>
        /// R-62/R-42 — one card button per modeled choice, aligned by index with
        /// <see cref="CombatHudModel.PickerChoices"/>; clicking one spends the banked point
        /// through <see cref="CombatHudModel.Spend"/> — the profile proves it landed.
        /// </summary>
        [Test]
        public void Picker_card_clicks_spend_the_banked_point()
        {
            var shell = NewHostedShell();
            StartMatch(shell);
            _profiles.Load(HostAccount).SkillPoints = 1;
            shell.Pump(0.0);

            shell.Controls.LevelUpBadgeButton.onClick.Invoke();
            shell.Pump(0.0);

            var choices = shell.Hud.PickerChoices;
            Assert.That(choices.Count, Is.EqualTo(2),
                "sanity (R-42): a fresh profile offers unlock_Q and unlock_E");
            Assert.That(shell.Controls.PickerChoiceButtons.Count, Is.EqualTo(choices.Count),
                "R-62: one card button per modeled choice, refreshed by the pump");

            var unlockQ = -1;
            for (var i = 0; i < choices.Count; i++)
            {
                if (choices[i].Choice == "unlock_" + AbilitySlot.Q)
                {
                    unlockQ = i;
                }
            }

            Assert.That(unlockQ, Is.GreaterThanOrEqualTo(0), "sanity: unlock_Q is offered");

            shell.Controls.PickerChoiceButtons[unlockQ].onClick.Invoke();
            shell.Pump(0.0);

            var profile = _profiles.Load(HostAccount);
            Assert.That(profile.Abilities[AbilitySlot.Q], Is.EqualTo(1),
                "R-42: the card's click spent the point on ITS choice — Q is unlocked");
            Assert.That(profile.SkillPoints, Is.EqualTo(0),
                "R-42: the banked point was consumed");
        }

        /// <summary>
        /// R-62 — hotkey L opens the picker through the INPUT PATH: the shell's own
        /// <see cref="IInputSource"/> holds <see cref="PlayerKey.L"/> and a pump does the rest.
        /// No control is invoked directly, which is the whole pin.
        /// </summary>
        [Test]
        public void The_L_key_opens_the_picker_through_the_input_path()
        {
            var shell = NewHostedShell();
            StartMatch(shell);
            shell.Pump(0.0);

            Assert.That(shell.Hud.PickerOpen, Is.False,
                "anti-vacuity: pumping without L opens nothing");

            _input.Held.Add(PlayerKey.L);
            shell.Pump(Step60Hz);

            Assert.That(shell.Hud.PickerOpen, Is.True,
                "R-62: held L reached CombatHudModel.OpenPicker through the shell's input path");
        }

        /// <summary>
        /// R-30 hygiene for the two keys this ticket adds to <see cref="PlayerKey"/>: L and
        /// Escape are UI keys, and UI keys produce NO gameplay intent — the same table row the
        /// mouse buttons already pin in T16.
        /// </summary>
        [TestCase(PlayerKey.L)]
        [TestCase(PlayerKey.Escape)]
        public void A_ui_key_produces_no_gameplay_intent(PlayerKey key)
        {
            var snapshot = new InputSnapshot();
            snapshot.Pressed.Add(key);

            var intent = new DefaultHeroInputMap().Resolve(snapshot);

            Assert.That(intent.MoveDirection, Is.EqualTo(Vector2.zero),
                "R-30: a UI key never moves the hero");
            Assert.That(intent.BasicAttack, Is.False, "R-30: a UI key never attacks");
            Assert.That(intent.Ability, Is.Null, "R-30: a UI key never casts");
        }

        /// <summary>
        /// R-55 — ESC through the input path raises the overlay:
        /// <see cref="NetSession.SetOverlayOpen"/> true, overlay root visible, and the sim keeps
        /// running underneath (never a pause). Releasing ESC closes nothing; the overlay's own
        /// close button does, symmetrically.
        /// </summary>
        [Test]
        public void Esc_opens_the_overlay_and_the_close_button_closes_it()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            shell.Pump(0.0);

            Assert.That(shell.Session.IsOverlayOpen, Is.False, "sanity: closed at birth");
            Assert.That(shell.Controls.EscOverlayRoot.activeInHierarchy, Is.False,
                "R-55: no overlay while the flag is down");

            _input.Held.Add(PlayerKey.Escape);
            var clockBefore = match.Clock.ElapsedSeconds;
            shell.Pump(Step60Hz);

            Assert.That(shell.Session.IsOverlayOpen, Is.True,
                "R-55: ESC reached NetSession.SetOverlayOpen(true) through the input path");
            Assert.That(shell.Controls.EscOverlayRoot.activeInHierarchy, Is.True,
                "the overlay root is visible while the flag is up");
            Assert.That(match.Clock.ElapsedSeconds, Is.GreaterThan(clockBefore),
                "R-55: the sim RAN under the open overlay — an overlay is never a pause");

            _input.Held.Clear();
            shell.Pump(Step60Hz);
            Assert.That(shell.Session.IsOverlayOpen, Is.True,
                "releasing ESC closes nothing — the close control does");

            shell.Controls.EscCloseButton.onClick.Invoke();
            shell.Pump(0.0);

            Assert.That(shell.Session.IsOverlayOpen, Is.False,
                "R-55: the overlay's close button is SetOverlayOpen(false)");
            Assert.That(shell.Controls.EscOverlayRoot.activeInHierarchy, Is.False,
                "and the root hides with the flag");
        }

        /// <summary>
        /// R-55/R-53 — LEAVE MATCH is the local peer leaving the session
        /// (<see cref="NetSession.Disconnect"/> for the local peer id) — the only leave the
        /// session surface offers. For this solo HOST that ends the session (DEC-RUN-10: no host
        /// migration) and the router lands back on S1. The overlay does not survive the leave.
        /// </summary>
        [Test]
        public void The_overlay_leave_button_leaves_the_session_and_lands_on_the_title()
        {
            var shell = NewHostedShell();
            StartMatch(shell);
            shell.Pump(0.0);

            _input.Held.Add(PlayerKey.Escape);
            shell.Pump(Step60Hz);
            _input.Held.Clear();

            shell.Controls.EscLeaveButton.onClick.Invoke();
            shell.Pump(0.0);

            Assert.That(shell.Session.Seats.Any(s => s.PeerId == HostPeerId), Is.False,
                "R-53: LEAVE unseated the local peer through the session");
            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.Ended),
                "DEC-RUN-10: the leaving local peer WAS the host, so the session ended — the "
                + "only semantics the session surface offers a leaver today");
            AssertOnlyActiveScreen(shell, UiScreen.Title, "the leaver is back on S1");
            Assert.That(shell.Session.IsOverlayOpen, Is.False,
                "the overlay does not follow the player out of the match");
        }

        // ==========================================================================================
        //  S6 / S7 — post-match: PLAY AGAIN / RETRY, MAIN MENU
        // ==========================================================================================

        /// <summary>
        /// R-07/DEC-RUN-11 — PLAY AGAIN (S6) / RETRY (S7) through the button: interactable
        /// exactly when the host may rematch (host-only is already modeled — this local peer IS
        /// the host, so enabled), and the click returns the party to S2 with the SAME join code
        /// and picks, the finished match discarded.
        /// </summary>
        [TestCase(true, TestName = "S6's PLAY AGAIN rematches back to the lobby")]
        [TestCase(false, TestName = "S7's RETRY rematches back to the lobby")]
        public void The_rematch_button_returns_the_party_to_the_lobby(bool victory)
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            shell.Pump(0.0);
            FinishMatch(match, victory);
            shell.Pump(0.0);

            var screen = victory ? UiScreen.Victory : UiScreen.Defeat;
            AssertOnlyActiveScreen(shell, screen, "the finished match is on its post-match screen");

            var codeBefore = shell.Session.JoinCode;
            var classBefore = shell.Session.Seats[0].HeroClass;

            var button = shell.Controls.RematchButton(screen);
            AssertControlUnder(button, shell.Ui.ScreenRoot(screen), "the rematch button");
            Assert.That(button.interactable, Is.True,
                "R-07: this local peer is the host, so the rematch button is enabled "
                + "(interactable == PostMatchModel.CanRematch)");

            button.onClick.Invoke();
            shell.Pump(0.0);

            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "DEC-RUN-11: the whole party is back in the lobby");
            AssertOnlyActiveScreen(shell, UiScreen.Lobby, "rematch lands on S2");
            Assert.That(shell.Session.Match, Is.Null,
                "R-07: the finished match was discarded, not reset in place");
            Assert.That(shell.Session.JoinCode, Is.EqualTo(codeBefore),
                "DEC-RUN-11: the SAME lobby — the join code survives");
            Assert.That(shell.Session.Seats[0].HeroClass, Is.EqualTo(classBefore),
                "DEC-RUN-11: the class picks survive");
        }

        /// <summary>
        /// MAIN MENU, pinned at its honest minimum: the click leaves the post-match screen and
        /// the shell shows S1 again. The session surface offers no clean return-to-title today —
        /// the only reachable path is the local peer leaving its own session, which ends it
        /// (DEC-RUN-10) — so THIS test pins only "S1 is shown" and deliberately not the session's
        /// final phase, the title error text, or re-hostability. See the ticket's handoff notes.
        /// </summary>
        [Test]
        public void Main_menu_returns_to_the_title_screen()
        {
            var shell = NewHostedShell();
            var match = StartMatch(shell);
            shell.Pump(0.0);
            FinishMatch(match, victory: true);
            shell.Pump(0.0);

            var button = shell.Controls.MainMenuButton(UiScreen.Victory);
            AssertControlUnder(button, shell.Ui.ScreenRoot(UiScreen.Victory), "MAIN MENU");

            button.onClick.Invoke();
            shell.Pump(0.0);

            Assert.That(shell.Router.Screen, Is.EqualTo(UiScreen.Title),
                "S6's MAIN MENU returns to the title screen");
            Assert.That(shell.Ui.ScreenRoot(UiScreen.Title).activeInHierarchy, Is.True,
                "R-60: S1's root is the active one again");
        }

        // ==========================================================================================
        //  the acceptance flow — title to combat through controls alone
        // ==========================================================================================

        /// <summary>
        /// The ticket's second acceptance criterion, end to end: a solo Play session drives
        /// title → host → lobby (pick + ready, auto-start) → the live match's screen using ONLY
        /// control invocations and pumps — not one direct model or session mutation. A started
        /// match opens in combat today (ticket 011), but the criterion says planning-or-combat,
        /// so that is what is pinned.
        /// </summary>
        [Test]
        public void A_solo_session_reaches_the_live_match_through_controls_alone()
        {
            var shell = NewShell();
            shell.Pump(0.0);
            AssertOnlyActiveScreen(shell, UiScreen.Title, "a fresh shell is on S1");

            TypeInto(shell.Controls.CallsignInput, HostAccount);
            shell.Controls.HostButton.onClick.Invoke();
            shell.Pump(0.0);
            AssertOnlyActiveScreen(shell, UiScreen.Lobby, "HOST GAME lands on S2");

            shell.Controls.ClassPickButton(HeroClass.Gunslinger).onClick.Invoke();
            shell.Controls.LobbyReadyButton.onClick.Invoke();
            shell.Pump(0.0);
            shell.Pump(0.0);

            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.InMatch),
                "the solo ready auto-started the match — no direct session call anywhere");
            Assert.That(shell.Router.Screen,
                Is.EqualTo(UiScreen.Combat).Or.EqualTo(UiScreen.Planning),
                "the player is looking at the live match (planning or combat)");

            var hero = OwnHero(shell.Session.Match.State);
            Assert.That(hero, Is.Not.Null,
                "R-44: the hero belongs to the TYPED callsign's account");
            Assert.That(hero.HeroClass, Is.EqualTo(HeroClass.Gunslinger),
                "the hero wears the clicked pick");
        }

        /// <summary>
        /// R-01, the bar itself: a SOLO Play session wins a REAL ten-wave match through the
        /// shipped combat path — every monster dies to a SPACE basic attack routed by the pump
        /// (<c>ResolveHeroAttack</c> → the shell's reap → <c>RecordMonsterKill</c>), and every
        /// planning phase is ended by the shell's own READY UP control. T-11 walks the campaign
        /// with direct kill commands; this is the first test in the repo in which the campaign is
        /// WON by playing it.
        ///
        /// Tuned for runtime, not balance — both knobs are numbers the PRD itself calls
        /// config-tunable: the gunslinger's <c>BasicAttackDamage</c> one-shots so a wave dies in
        /// about its headcount of pumps, and the attack cadence matches the pump so a held SPACE
        /// fires every pump. R-19 says the balance numbers are playtest taste; what this test
        /// grades is that the LOOP — spawn → aim → fire → reap → wave clear → planning → ready →
        /// next wave → victory — is real, with not one harness kill anywhere.
        /// </summary>
        [Test]
        public void A_solo_session_wins_a_real_ten_wave_match_through_the_combat_path()
        {
            var shell = NewOneShotShell();
            shell.Pump(0.0);

            TypeInto(shell.Controls.CallsignInput, HostAccount);
            shell.Controls.HostButton.onClick.Invoke();
            shell.Pump(0.0);

            shell.Controls.ClassPickButton(HeroClass.Gunslinger).onClick.Invoke();
            shell.Controls.LobbyReadyButton.onClick.Invoke();
            shell.Pump(0.0);
            shell.Pump(0.0);

            var match = shell.Session.Match;
            Assert.That(match, Is.Not.Null, "sanity: the solo ready auto-started a match");

            DriveTheCampaignByPlaying(shell, match);

            Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Victory),
                "R-01: playing all ten waves through the combat path wins the map");
            Assert.That(match.State.Wave.Number, Is.EqualTo(match.State.Wave.TotalWaves),
                "R-01: the victory is the final wave's, not an early exit");
            Assert.That(match.State.TotalCivilians, Is.GreaterThan(0),
                "R-02: a won colony still holds civilians — victory and defeat are exclusive");

            shell.Pump(Step60Hz);
            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.PostMatch),
                "the session noticed the win");
            AssertOnlyActiveScreen(shell, UiScreen.Victory, "the campaign ends on S6");
        }

        /// <summary>
        /// R-50 — the same real ten-wave victory with a 2-player party seated: a guest joins the
        /// lobby before READY, the match seats two heroes, and the host's combat path carries the
        /// campaign while the guest's READY arrives as the sim command a replicated client ready
        /// would issue. 4-player is R-50's ceiling and NGO territory; two seated players IS the
        /// current bar.
        /// </summary>
        [Test]
        public void A_two_player_party_wins_a_real_ten_wave_match_through_the_combat_path()
        {
            var shell = NewOneShotShell();
            shell.Pump(0.0);

            TypeInto(shell.Controls.CallsignInput, HostAccount);
            shell.Controls.HostButton.onClick.Invoke();
            shell.Pump(0.0);

            Assert.That(shell.Session.TryJoin(new NetPeer
            {
                PeerId = GuestPeerId,
                AccountId = GuestAccount,
                HeroClass = HeroClass.Sawbones,
            }), Is.True, "R-50: a second player takes a lobby seat");

            shell.Controls.ClassPickButton(HeroClass.Gunslinger).onClick.Invoke();
            shell.Controls.LobbyReadyButton.onClick.Invoke();

            // The guest's lobby READY, on the seam a replicated toggle arrives through (T-12).
            shell.Lobby.NotePeerReady(GuestPeerId, true);
            shell.Pump(0.0);
            shell.Pump(0.0);

            var match = shell.Session.Match;
            Assert.That(match, Is.Not.Null, "the all-ready party auto-started the match");
            Assert.That(match.State.Heroes.Count, Is.EqualTo(2),
                "R-50: one hero per seated player");

            var guestSlot = match.State.Players
                .First(p => string.Equals(p.AccountId, GuestAccount, StringComparison.Ordinal)).Id;

            DriveTheCampaignByPlaying(shell, match, guestSlot);

            Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Victory),
                "R-01/R-50: a 2-player party plays all ten waves to a victory");
            Assert.That(match.State.Heroes.Count, Is.EqualTo(2),
                "both heroes are still seated at the end");
        }

        // ==========================================================================================
        //  thinness — the wiring is plain C# inside the scanned assembly
        // ==========================================================================================

        /// <summary>
        /// T-10's invariant, at the seam this ticket adds: the control wiring is plain C# (a
        /// MonoBehaviour here is what the Cecil scan rejects) and it compiles into the shell
        /// assembly, where that scan — which reads compiler-generated closure classes too —
        /// already covers every closure a button invokes.
        /// </summary>
        [Test]
        public void The_control_wiring_is_plain_C_sharp_in_the_scanned_assembly()
        {
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(typeof(ShellControls)), Is.False,
                "T-10: the control wiring is a plain C# class, never a MonoBehaviour");
            Assert.That(typeof(ShellControls).Assembly, Is.SameAs(typeof(ShellBootstrap).Assembly),
                "T-10: the wiring compiles into the shell assembly the Cecil scan reads — "
                + "closures included");
        }

        // ==========================================================================================
        //  scenario builders and helpers
        // ==========================================================================================

        /// <summary>A fresh shell on S1: loopback, in-memory profiles, a scripted input source.</summary>
        private ShellBootstrap NewShell()
        {
            _profiles = new InMemoryProfileStore();
            _input = new FakeInputSource();

            _shell = new ShellBootstrap(new ShellBootstrapOptions
            {
                Transport = new LoopbackNetTransport(),
                Profiles = _profiles,
                SimConfig = new SimConfig(),
                LocalPeerId = HostPeerId,
                LocalAccountId = HostAccount,
                InputSource = _input,
            });

            return _shell;
        }

        /// <summary>
        /// The full-campaign shell: identical wiring to <see cref="NewShell"/> with two tuned
        /// numbers so ten played waves fit a test's runtime — one-shot basics (config-tunable
        /// damage, R-31's numbers are balance data) and a per-pump attack cadence. Nothing else
        /// differs; every command still travels the shipped path.
        /// </summary>
        private ShellBootstrap NewOneShotShell()
        {
            _profiles = new InMemoryProfileStore();
            _input = new FakeInputSource();

            var config = new SimConfig();
            config.HeroKits.KitFor(HeroClass.Gunslinger).BasicAttackDamage = 100000.0;

            _shell = new ShellBootstrap(new ShellBootstrapOptions
            {
                Transport = new LoopbackNetTransport(),
                Profiles = _profiles,
                SimConfig = config,
                LocalPeerId = HostPeerId,
                LocalAccountId = HostAccount,
                InputSource = _input,
                CombatActions = new CombatActionConfig { AttackCadenceSeconds = Step60Hz },
            });

            return _shell;
        }

        /// <summary>
        /// Play the campaign out: every combat pump aims the cursor at the nearest living monster
        /// and holds SPACE (the shell fires, reaps and advances the wave), every planning phase is
        /// ended with the READY UP control (plus the guest's own sim-level ready when a second
        /// slot is seated). Bounded so a stalled campaign fails naming where it stopped rather
        /// than hanging the runner.
        /// </summary>
        private void DriveTheCampaignByPlaying(
            ShellBootstrap shell, HostedMatch match, string guestSlotId = null)
        {
            _input.Held.Add(PlayerKey.Space);

            // A played wave dies in about its headcount of pumps and planning is one ready-up
            // pump, so a healthy campaign finishes near 500 pumps. 12000 (200 sim-seconds) is the
            // loud-failure bound.
            const int MaxPumps = 12000;

            for (var i = 0; i < MaxPumps && !match.State.IsOver; i++)
            {
                var state = match.State;

                if (state.Phase == MatchPhase.Combat)
                {
                    AimAtTheNearestMonster(state);
                }
                else if (state.Phase == MatchPhase.Planning)
                {
                    shell.Controls.PlanningReadyButton.onClick.Invoke();

                    if (guestSlotId != null)
                    {
                        // The command a replicated client READY issues (T-11's reading).
                        match.Sim.SetPlayerReady(guestSlotId);
                    }
                }

                shell.Pump(Step60Hz);
            }

            _input.Held.Remove(PlayerKey.Space);

            Assert.That(match.State.IsOver, Is.True, DescribeUnfinishedCampaign(match));
        }

        /// <summary>Park the cursor on the nearest living monster, so the aim line crosses it.</summary>
        private void AimAtTheNearestMonster(MatchState state)
        {
            var hero = OwnHero(state);
            if (hero == null)
            {
                return;
            }

            Monster nearest = null;
            var best = double.MaxValue;
            foreach (var monster in state.Monsters.Values)
            {
                if (monster == null || !monster.Alive)
                {
                    continue;
                }

                var distance = hero.Pos.DistanceTo(monster.Pos);
                if (distance < best)
                {
                    best = distance;
                    nearest = monster;
                }
            }

            if (nearest != null)
            {
                _input.Cursor = new Vector2((float)nearest.Pos.X, (float)nearest.Pos.Y);
            }
        }

        /// <summary>Names where a stalled campaign stopped, for whoever reads the red test.</summary>
        private static string DescribeUnfinishedCampaign(HostedMatch match)
        {
            var state = match.State;
            var living = state.Monsters.Values.Count(m => m != null && m.Alive);
            var hero = OwnHero(state);

            return "the campaign never ended: wave " + state.Wave.Number + "/" + state.Wave.TotalWaves
                + ", phase '" + state.Phase + "', status '" + state.Status + "', "
                + state.Wave.LivingMonsterIds.Count + " on the roster (" + living + " alive), "
                + state.TotalCivilians + " civilian(s) left, hero "
                + (hero == null ? "MISSING" : (hero.Alive ? "alive at " + hero.Hp + " HP" : "down"));
        }

        /// <summary>A shell with the host seated — the S2 starting point (T21's helper).</summary>
        private ShellBootstrap NewHostedShell()
        {
            var shell = NewShell();

            shell.Session.StartHost(new NetPeer
            {
                PeerId = HostPeerId,
                AccountId = HostAccount,
                HeroClass = HeroClass.Gunslinger,
                IsHost = true,
            });

            Assert.That(shell.Session.Phase, Is.EqualTo(NetSessionPhase.Lobby),
                "sanity (R-50): hosting opens a lobby");

            return shell;
        }

        private static HostedMatch StartMatch(ShellBootstrap shell)
        {
            Assert.That(shell.Session.TryStartMatch(HostPeerId), Is.True,
                "sanity (R-50): the host starts the match");

            var match = shell.Session.Match;
            Assert.That(match, Is.Not.Null, "the session holds the live match");
            return match;
        }

        /// <summary>Clear the live wave and ride S5's hold into S3 (T21's recipe).</summary>
        private static void ReachPlanning(ShellBootstrap shell, HostedMatch match)
        {
            shell.Pump(0.0);
            KillWave(match, match.State.Wave.LivingMonsterIds.ToList());
            shell.Pump(0.0);

            var holdSteps = (int)Math.Ceiling(shell.Router.InterstitialSeconds / Step60Hz) + 2;
            for (var i = 0; i < holdSteps; i++)
            {
                shell.Pump(Step60Hz);
            }

            Assert.That(shell.Router.Screen, Is.EqualTo(UiScreen.Planning),
                "sanity (R-04): the interstitial fell back to planning");
            Assert.That(match.State.Phase, Is.EqualTo(MatchPhase.Planning),
                "sanity: the sim is in its planning phase");
        }

        /// <summary>Victory by clearing the final wave; defeat by emptying the colony (T21).</summary>
        private static void FinishMatch(HostedMatch match, bool victory)
        {
            if (victory)
            {
                match.State.Wave.Number = match.State.Wave.TotalWaves;
                KillWave(match, match.State.Wave.LivingMonsterIds.ToList());
                Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Victory),
                    "sanity (R-01): clearing the final wave wins the map");
            }
            else
            {
                foreach (var hotspot in match.State.Hotspots.Values.ToList())
                {
                    while (hotspot.Civilians > 0)
                    {
                        match.Sim.ApplyHotspotAttack(new HotspotAttackRequest
                        {
                            AttackerId = "m_wipeout",
                            AttackerType = MonsterType.Shambler,
                            Damage = 1000.0,
                            TargetId = hotspot.Id,
                        });
                    }
                }

                Assert.That(match.State.Status, Is.EqualTo(MatchStatus.Defeat),
                    "sanity (R-02): an emptied colony is the defeat");
            }
        }

        /// <summary>Clears a wave through the sim's own kill command (T-12/T-21's helper).</summary>
        private static void KillWave(HostedMatch match, IEnumerable<string> monsterIds)
        {
            foreach (var id in monsterIds.ToList())
            {
                match.Sim.RecordMonsterKill(new MonsterKillRequest
                {
                    MonsterId = id,
                    MonsterType =
                        match.State.Monsters.TryGetValue(id, out var monster) ? monster.Type : null,
                    Bounty = 0,
                });
            }
        }

        private static Hero OwnHero(MatchState state)
        {
            return state.Heroes.Values.FirstOrDefault(
                h => string.Equals(h.AccountId, HostAccount, StringComparison.Ordinal));
        }

        /// <summary>
        /// Type into an InputField the EditMode way: set the text, then raise onValueChanged the
        /// way the runtime would. (Setting .text may raise it once already; the wiring's handlers
        /// route to idempotent model setters, so a double delivery is harmless by design.)
        /// </summary>
        private static void TypeInto(InputField field, string text)
        {
            Assert.That(field, Is.Not.Null, "the input field exists");
            field.text = text;
            field.onValueChanged.Invoke(text);
        }

        /// <summary>The control exists and hangs under its screen's root (the pin that lets R-60's
        /// activation flipping show and hide it with the screen).</summary>
        private static void AssertControlUnder(Component control, GameObject screenRoot, string what)
        {
            Assert.That(control, Is.Not.Null, what + " exists");
            Assert.That(control.transform.IsChildOf(screenRoot.transform), Is.True,
                what + " lives under its screen's root, so screen switching shows/hides it");
        }

        /// <summary>Exactly one screen root is active in the hierarchy: the routed one (T21).</summary>
        private static void AssertOnlyActiveScreen(ShellBootstrap shell, UiScreen expected, string because)
        {
            Assert.That(shell.Router.Screen, Is.EqualTo(expected),
                "sanity — the router itself must be on " + expected + ": " + because);

            foreach (UiScreen screen in Enum.GetValues(typeof(UiScreen)))
            {
                var root = shell.Ui.ScreenRoot(screen);
                Assert.That(root, Is.Not.Null, "every screen has a root: " + screen);
                Assert.That(root.activeInHierarchy, Is.EqualTo(screen == expected),
                    "R-60: " + because + " — " + screen + "'s root must be "
                    + (screen == expected ? "the one active container" : "inactive")
                    + " while the router is on " + expected);
            }
        }

        // ==========================================================================================
        //  test doubles
        // ==========================================================================================

        /// <summary>A scripted device (T22's shape): held keys + a cursor ground point.</summary>
        private sealed class FakeInputSource : IInputSource
        {
            public readonly HashSet<PlayerKey> Held = new HashSet<PlayerKey>();
            public Vector2 Cursor;

            public InputSnapshot Sample()
            {
                var snapshot = new InputSnapshot { CursorGroundPoint = Cursor };
                foreach (var key in Held)
                {
                    snapshot.Pressed.Add(key);
                }

                return snapshot;
            }
        }
    }
}
