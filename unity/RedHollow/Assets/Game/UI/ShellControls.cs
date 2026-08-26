using System.Collections.Generic;
using RedHollow.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace RedHollow.Game.UI
{
    /// <summary>
    /// Ticket 023 (T-23) — the shell's interactive controls: every uGUI Button / InputField the
    /// wireframes (docs/ui-wireframes.html) name, wired to the ticket-012 model actions the shell
    /// already owns.
    ///
    /// The pinned convention is ACCESSOR PROPERTIES on this class (the same shape as
    /// <see cref="ShellUi"/>'s label fields), reached through <see cref="ShellBootstrap.Controls"/>.
    /// Screen-specific controls live UNDER their screen's root (<see cref="ShellUi.ScreenRoot"/>),
    /// so R-60's activation flipping shows and hides them for free.
    ///
    /// Thinness (T-10): plain C#, never a MonoBehaviour, and the closures the buttons invoke call
    /// MODEL / SESSION methods only (reached through the live <see cref="ShellBootstrap"/>
    /// accessors, so a rematch's fresh models — and a re-host's fresh session — need no rewiring).
    /// Nothing here reads or writes sim state; every mutation goes through a model method that
    /// issues the command.
    ///
    /// Pointer-driven planning actions (ghost follow, place, sell) are pinned at the WIRING SEAM:
    /// <see cref="PointerAt"/> / <see cref="ClickGround"/> take a ground-space position
    /// (<see cref="Vec2"/>, SimSpace's x-right / y-forward plane) plus the caller's zone-validity
    /// answer, and <see cref="ClickPlaceable"/> takes a placeable id. Resolving a screen ray to
    /// either is play-mode raycasting territory and is deliberately not wired here.
    /// </summary>
    public sealed class ShellControls
    {
        private readonly ShellBootstrap _shell;
        private readonly ShellUi _ui;

        private readonly Dictionary<string, Button> _classPicks = new Dictionary<string, Button>();

        /// <summary>One shop button per catalog row, keyed by <see cref="PlaceableType"/>, grown lazily.</summary>
        private readonly Dictionary<string, Button> _shopButtons = new Dictionary<string, Button>();

        /// <summary>The picker's card buttons, resized each refresh to match the modeled choices.</summary>
        private readonly List<Button> _pickerButtons = new List<Button>();

        private readonly Dictionary<UiScreen, Button> _rematchButtons =
            new Dictionary<UiScreen, Button>();

        private readonly Dictionary<UiScreen, Button> _mainMenuButtons =
            new Dictionary<UiScreen, Button>();

        private readonly GameObject _pickerPanel;

        internal ShellControls(ShellBootstrap shell, ShellUi ui)
        {
            _shell = shell;
            _ui = ui;

            // ---- S1 · Title / Join — typing IS the action (R-44), buttons route to the shell.
            var title = ui.ScreenRoot(UiScreen.Title);
            CallsignInput = NewInput(title, "CallsignInput");
            CallsignInput.onValueChanged.AddListener(v => _shell.Title.SetCallsign(v));

            HostButton = NewButton(title, "HostButton");
            HostButton.onClick.AddListener(() => _shell.RequestHost());

            JoinCodeInput = NewInput(title, "JoinCodeInput");
            JoinCodeInput.onValueChanged.AddListener(v => _shell.Title.SetJoinCodeInput(v));

            JoinButton = NewButton(title, "JoinButton");
            JoinButton.onClick.AddListener(() => _shell.RequestJoin());

            JoinErrorLabel = NewLabel(title, "JoinErrorLabel");

            // ---- S2 · Lobby — one PICK per class card, plus READY.
            var lobby = ui.ScreenRoot(UiScreen.Lobby);
            foreach (var heroClass in new[]
                     { HeroClass.Gunslinger, HeroClass.Rancher, HeroClass.Sawbones })
            {
                var pick = NewButton(lobby, "Pick_" + heroClass);
                var picked = heroClass;
                pick.onClick.AddListener(() => _shell.Lobby.PickClass(picked));
                _classPicks[heroClass] = pick;
            }

            LobbyReadyButton = NewButton(lobby, "LobbyReadyButton");
            LobbyReadyButton.onClick.AddListener(() => _shell.Lobby.SetReady(true));

            // ---- S3 · Planning — READY UP now; the shop bar grows per catalog row on refresh.
            PlanningReadyButton = NewButton(ui.ScreenRoot(UiScreen.Planning), "PlanningReadyButton");
            PlanningReadyButton.onClick.AddListener(() =>
            {
                var planning = _shell.Planning;
                if (planning != null)
                {
                    planning.ReadyUp();
                }
            });

            // ---- S4 · Combat — the badge (hidden until a point is banked) and the picker panel.
            var combat = ui.ScreenRoot(UiScreen.Combat);
            LevelUpBadgeButton = NewButton(combat, "LevelUpBadgeButton");
            LevelUpBadgeButton.gameObject.SetActive(false);
            LevelUpBadgeButton.onClick.AddListener(() =>
            {
                var hud = _shell.Hud;
                if (hud != null)
                {
                    hud.OpenPicker();
                }
            });

            _pickerPanel = new GameObject("LevelUpPicker");
            _pickerPanel.transform.SetParent(combat.transform, false);

            // ---- the ESC overlay — an OVERLAY, not a screen (R-55): it hangs beside the screen
            // roots so it can sit on top of whichever one is active, shown exactly while the
            // session's flag is up.
            EscOverlayRoot = new GameObject("EscOverlay");
            EscOverlayRoot.transform.SetParent(_ui.Canvas.transform, false);
            EscOverlayRoot.SetActive(false);

            EscCloseButton = NewButton(EscOverlayRoot, "EscCloseButton");
            EscCloseButton.onClick.AddListener(() => _shell.Session.SetOverlayOpen(false));

            EscLeaveButton = NewButton(EscOverlayRoot, "EscLeaveButton");
            EscLeaveButton.onClick.AddListener(() => _shell.LeaveToTitle());

            // ---- S6 / S7 · Post-match — PLAY AGAIN / RETRY and MAIN MENU, one pair per screen.
            foreach (var screen in new[] { UiScreen.Victory, UiScreen.Defeat })
            {
                var root = ui.ScreenRoot(screen);

                var rematch = NewButton(root, "RematchButton");
                rematch.onClick.AddListener(() =>
                {
                    var postMatch = _shell.PostMatch;
                    if (postMatch != null)
                    {
                        postMatch.RequestRematch();
                    }
                });
                _rematchButtons[screen] = rematch;

                var mainMenu = NewButton(root, "MainMenuButton");
                mainMenu.onClick.AddListener(() => _shell.LeaveToTitle());
                _mainMenuButtons[screen] = mainMenu;
            }
        }

        // ---- S1 · Title / Join ----------------------------------------------------------------

        /// <summary>S1 — the callsign input; typing loads the profile (TitleScreenModel.SetCallsign).</summary>
        public InputField CallsignInput { get; }

        /// <summary>S1 — HOST GAME: opens the lobby as the typed callsign (R-50).</summary>
        public Button HostButton { get; }

        /// <summary>S1 — the join-code input; editing clears the inline error.</summary>
        public InputField JoinCodeInput { get; }

        /// <summary>S1 — JOIN: a failed join raises the modeled inline error and stays on S1.</summary>
        public Button JoinButton { get; }

        /// <summary>S1 — the inline error under the code input; empty while no error is modeled.</summary>
        public Text JoinErrorLabel { get; }

        // ---- S2 · Lobby -----------------------------------------------------------------------

        /// <summary>S2 — one PICK button per hero class card (a <see cref="HeroClass"/> literal).</summary>
        public Button ClassPickButton(string heroClass)
        {
            Button button;
            return heroClass != null && _classPicks.TryGetValue(heroClass, out button)
                ? button
                : null;
        }

        /// <summary>S2 — READY (LobbyScreenModel.SetReady; all-ready auto-starts the match).</summary>
        public Button LobbyReadyButton { get; }

        // ---- S3 · Planning --------------------------------------------------------------------

        /// <summary>
        /// S3 — one shop-bar button per R-23 catalog row (a <see cref="PlaceableType"/> literal).
        /// Non-interactable exactly when the modeled <see cref="ShopItem.Affordable"/> is false.
        /// </summary>
        public Button ShopItemButton(string placeableType)
        {
            Button button;
            return placeableType != null && _shopButtons.TryGetValue(placeableType, out button)
                ? button
                : null;
        }

        /// <summary>S3 — READY UP (PlanningScreenModel.ReadyUp; all-ready starts combat early).</summary>
        public Button PlanningReadyButton { get; }

        /// <summary>
        /// The cursor moved over the ground: while a ghost is up this is
        /// <see cref="PlanningScreenModel.MoveGhost"/>. Zone validity is the caller's answer —
        /// resolving it from geometry is the play-mode raycaster's job.
        /// </summary>
        public void PointerAt(Vec2 groundPos, bool zoneValid)
        {
            var planning = _shell.Planning;
            if (planning != null && planning.GhostActive)
            {
                planning.MoveGhost(groundPos, zoneValid);
            }
        }

        /// <summary>
        /// A ground click: with a ghost up, move it there and
        /// <see cref="PlanningScreenModel.ConfirmPlacement"/> (a rejection leaves the ghost up).
        /// Without a ghost the ground click is nothing — the mouse belongs to the UI (R-30).
        /// </summary>
        public void ClickGround(Vec2 groundPos, bool zoneValid)
        {
            var planning = _shell.Planning;
            if (planning == null || !planning.GhostActive)
            {
                return;
            }

            planning.MoveGhost(groundPos, zoneValid);
            planning.ConfirmPlacement();
        }

        /// <summary>
        /// A click on a standing placeable: <see cref="PlanningScreenModel.Sell"/> — the R-22
        /// refund path. Ignored while a ghost is up (that click belongs to placement). The id is
        /// the seam; hit-testing a click to an id is play-mode territory.
        /// </summary>
        public void ClickPlaceable(string placeableId)
        {
            var planning = _shell.Planning;
            if (planning != null && !planning.GhostActive)
            {
                planning.Sell(placeableId);
            }
        }

        // ---- S4 · Combat ----------------------------------------------------------------------

        /// <summary>
        /// S4 — the skill-point badge (R-61/R-62): visible exactly while
        /// <see cref="CombatHudModel.SkillPointBadge"/>; clicking opens the picker.
        /// </summary>
        public Button LevelUpBadgeButton { get; }

        /// <summary>
        /// S4 — one button per <see cref="CombatHudModel.PickerChoices"/> card, aligned by index;
        /// clicking the i-th spends that choice (<see cref="CombatHudModel.Spend"/>).
        /// </summary>
        public IReadOnlyList<Button> PickerChoiceButtons => _pickerButtons;

        /// <summary>R-55 — the ESC overlay's root: active exactly while the overlay is open.</summary>
        public GameObject EscOverlayRoot { get; }

        /// <summary>R-55 — closes the overlay (NetSession.SetOverlayOpen(false)). Never a pause.</summary>
        public Button EscCloseButton { get; }

        /// <summary>
        /// R-55 — LEAVE MATCH: the local peer leaves the session
        /// (<see cref="RedHollow.Game.Net.NetSession.Disconnect"/> for the local peer id).
        /// </summary>
        public Button EscLeaveButton { get; }

        // ---- S6 / S7 · Post-match -------------------------------------------------------------

        /// <summary>
        /// S6's PLAY AGAIN / S7's RETRY (screen ∈ {Victory, Defeat}): interactable exactly when
        /// <see cref="PostMatchModel.CanRematch"/>; clicking requests the rematch back to S2.
        /// </summary>
        public Button RematchButton(UiScreen screen)
        {
            Button button;
            return _rematchButtons.TryGetValue(screen, out button) ? button : null;
        }

        /// <summary>S6/S7 — MAIN MENU: returns to the title screen (see the T23 tests' pin).</summary>
        public Button MainMenuButton(UiScreen screen)
        {
            Button button;
            return _mainMenuButtons.TryGetValue(screen, out button) ? button : null;
        }

        // ---- per-pump refresh (called from ShellBootstrap.RefreshPresentation) -----------------

        /// <summary>
        /// Mirror the models onto the controls: the inline join error, per-item shop
        /// interactability (== the modeled Affordable flag), the badge's visibility, the picker's
        /// card row, the overlay's activation, and rematch enablement. Values only — every rule
        /// lives in the models.
        /// </summary>
        internal void Refresh()
        {
            JoinErrorLabel.text = _shell.Title.JoinError ?? string.Empty;

            RefreshShopBar();
            RefreshBadgeAndPicker();

            var overlayOpen = _shell.Session.IsOverlayOpen;
            if (EscOverlayRoot.activeSelf != overlayOpen)
            {
                EscOverlayRoot.SetActive(overlayOpen);
            }

            var postMatch = _shell.PostMatch;
            if (postMatch != null)
            {
                var canRematch = postMatch.CanRematch;
                foreach (var pair in _rematchButtons)
                {
                    pair.Value.interactable = canRematch;
                }
            }
        }

        /// <summary>R-63 — one button per catalog row, interactable == the modeled Affordable flag.</summary>
        private void RefreshShopBar()
        {
            var planning = _shell.Planning;
            if (planning == null)
            {
                return;
            }

            var items = planning.ShopItems;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];

                Button button;
                if (!_shopButtons.TryGetValue(item.Type, out button))
                {
                    button = NewButton(_ui.ScreenRoot(UiScreen.Planning), "Shop_" + item.Type);
                    var type = item.Type;
                    button.onClick.AddListener(() =>
                    {
                        var model = _shell.Planning;
                        if (model != null)
                        {
                            model.BeginPlacement(type);
                        }
                    });
                    _shopButtons[item.Type] = button;
                }

                button.interactable = item.Affordable;
            }
        }

        /// <summary>
        /// R-61/R-62 — the badge shows exactly while a point is banked, and the card row is
        /// resized and re-aimed to match <see cref="CombatHudModel.PickerChoices"/> by index.
        /// </summary>
        private void RefreshBadgeAndPicker()
        {
            var hud = _shell.Hud;

            var badgeOn = hud != null && hud.SkillPointBadge;
            if (LevelUpBadgeButton.gameObject.activeSelf != badgeOn)
            {
                LevelUpBadgeButton.gameObject.SetActive(badgeOn);
            }

            var choices = hud == null
                ? (IReadOnlyList<LevelUpChoice>)new LevelUpChoice[0]
                : hud.PickerChoices;

            while (_pickerButtons.Count < choices.Count)
            {
                _pickerButtons.Add(
                    NewButton(_pickerPanel, "PickerChoice_" + _pickerButtons.Count));
            }

            while (_pickerButtons.Count > choices.Count)
            {
                var last = _pickerButtons[_pickerButtons.Count - 1];
                _pickerButtons.RemoveAt(_pickerButtons.Count - 1);
                DestroyGameObject(last.gameObject);
            }

            for (var i = 0; i < choices.Count; i++)
            {
                var choice = choices[i].Choice;
                var button = _pickerButtons[i];
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    var model = _shell.Hud;
                    if (model != null)
                    {
                        model.Spend(choice);
                    }
                });
            }
        }

        // ---- headless control construction ------------------------------------------------------

        private static Button NewButton(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go.AddComponent<Button>();
        }

        private static InputField NewInput(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go.AddComponent<InputField>();
        }

        private static Text NewLabel(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var label = go.AddComponent<Text>();
            label.text = string.Empty;
            return label;
        }

        private static void DestroyGameObject(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(go);
            }
            else
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
