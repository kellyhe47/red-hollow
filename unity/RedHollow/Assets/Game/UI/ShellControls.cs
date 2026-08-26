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

        /// <summary>T-27 — the shop bar panel (wireframe S3: bottom band of the planning screen).</summary>
        private readonly GameObject _shopBar;

        /// <summary>T-27 — the placement ghost's on-screen Graphic (see <see cref="GhostVisual"/>).</summary>
        private readonly Image _ghostVisual;

        internal ShellControls(ShellBootstrap shell, ShellUi ui)
        {
            _shell = shell;
            _ui = ui;

            // ---- S1 · Title / Join — typing IS the action (R-44), buttons route to the shell.
            // T-27 lays the column out per the wireframe: banner up top, inputs and buttons down
            // the center, the inline error UNDER the code input it explains.
            var title = ui.ScreenRoot(UiScreen.Title);

            var titleBanner = NewLabel(title, "TitleBanner", 48);
            titleBanner.text = "THE RED HOLLOW";
            titleBanner.color = UiStyle.Ember;
            UiStyle.Anchor(titleBanner.rectTransform, 0.2f, 0.68f, 0.8f, 0.92f);

            CallsignInput = NewInput(title, "CallsignInput", "callsign");
            UiStyle.Anchor((RectTransform)CallsignInput.transform, 0.35f, 0.54f, 0.65f, 0.61f);
            CallsignInput.onValueChanged.AddListener(v => _shell.Title.SetCallsign(v));

            HostButton = NewButton(title, "HostButton", "HOST GAME");
            UiStyle.Anchor((RectTransform)HostButton.transform, 0.35f, 0.44f, 0.65f, 0.52f);
            HostButton.onClick.AddListener(() => _shell.RequestHost());

            JoinCodeInput = NewInput(title, "JoinCodeInput", "join code");
            UiStyle.Anchor((RectTransform)JoinCodeInput.transform, 0.35f, 0.3f, 0.65f, 0.37f);
            JoinCodeInput.onValueChanged.AddListener(v => _shell.Title.SetJoinCodeInput(v));

            JoinButton = NewButton(title, "JoinButton", "JOIN");
            UiStyle.Anchor((RectTransform)JoinButton.transform, 0.35f, 0.2f, 0.65f, 0.28f);
            JoinButton.onClick.AddListener(() => _shell.RequestJoin());

            JoinErrorLabel = NewLabel(title, "JoinErrorLabel", 16);
            JoinErrorLabel.color = UiStyle.ErrorTint;
            UiStyle.Anchor(JoinErrorLabel.rectTransform, 0.25f, 0.13f, 0.75f, 0.18f);

            // ---- S2 · Lobby — one PICK per class card, plus READY.
            var lobby = ui.ScreenRoot(UiScreen.Lobby);

            var lobbyBanner = NewLabel(lobby, "LobbyBanner", 32);
            lobbyBanner.text = "CHOOSE YOUR HERO";
            lobbyBanner.color = UiStyle.Ember;
            UiStyle.Anchor(lobbyBanner.rectTransform, 0.2f, 0.78f, 0.8f, 0.9f);

            var classes = new[] { HeroClass.Gunslinger, HeroClass.Rancher, HeroClass.Sawbones };
            for (var i = 0; i < classes.Length; i++)
            {
                var heroClass = classes[i];
                var pick = NewButton(lobby, "Pick_" + heroClass, heroClass.ToUpperInvariant());
                UiStyle.Anchor((RectTransform)pick.transform,
                    0.13f + (i * 0.26f), 0.35f, 0.35f + (i * 0.26f), 0.65f);
                var picked = heroClass;
                pick.onClick.AddListener(() => _shell.Lobby.PickClass(picked));
                _classPicks[heroClass] = pick;
            }

            LobbyReadyButton = NewButton(lobby, "LobbyReadyButton", "READY");
            UiStyle.Anchor((RectTransform)LobbyReadyButton.transform, 0.4f, 0.12f, 0.6f, 0.22f);
            LobbyReadyButton.onClick.AddListener(() => _shell.Lobby.SetReady(true));

            // ---- S3 · Planning — READY UP now; the shop bar grows per catalog row on refresh
            // into its bottom-band panel (wireframe S3: SHOP BAR along the bottom).
            var planning = ui.ScreenRoot(UiScreen.Planning);

            _shopBar = new GameObject("ShopBar", typeof(RectTransform));
            _shopBar.transform.SetParent(planning.transform, false);
            UiStyle.Anchor((RectTransform)_shopBar.transform, 0f, 0f, 1f, 0.15f);
            var shopBackdrop = _shopBar.AddComponent<Image>();
            shopBackdrop.color = UiStyle.PanelDark;
            shopBackdrop.raycastTarget = false;

            PlanningReadyButton = NewButton(planning, "PlanningReadyButton", "READY UP");
            UiStyle.Anchor((RectTransform)PlanningReadyButton.transform, 0.8f, 0.18f, 0.98f, 0.28f);
            PlanningReadyButton.onClick.AddListener(() =>
            {
                var model = _shell.Planning;
                if (model != null)
                {
                    model.ReadyUp();
                }
            });

            // T-27 — the placement ghost's visual: under the Planning root (R-60 hides it with
            // S3), inactive until a ghost is up, tinted amber over valid ground and red over
            // invalid (the wireframe's tint) by Refresh. Never a raycast target — the ghost must
            // not eat the clicks that place it.
            var ghostGo = new GameObject("GhostVisual", typeof(RectTransform));
            ghostGo.transform.SetParent(planning.transform, false);
            var ghostRt = (RectTransform)ghostGo.transform;
            ghostRt.anchorMin = new Vector2(0.5f, 0.5f);
            ghostRt.anchorMax = new Vector2(0.5f, 0.5f);
            ghostRt.sizeDelta = new Vector2(64f, 64f);
            _ghostVisual = ghostGo.AddComponent<Image>();
            _ghostVisual.color = UiStyle.GhostValid;
            _ghostVisual.raycastTarget = false;
            ghostGo.SetActive(false);

            // ---- S4 · Combat — the badge (hidden until a point is banked) and the picker panel.
            var combat = ui.ScreenRoot(UiScreen.Combat);
            LevelUpBadgeButton = NewButton(combat, "LevelUpBadgeButton", "LEVEL UP!");
            UiStyle.Anchor((RectTransform)LevelUpBadgeButton.transform, 0.86f, 0.6f, 0.99f, 0.68f);
            LevelUpBadgeButton.gameObject.SetActive(false);
            LevelUpBadgeButton.onClick.AddListener(() =>
            {
                var hud = _shell.Hud;
                if (hud != null)
                {
                    hud.OpenPicker();
                }
            });

            _pickerPanel = new GameObject("LevelUpPicker", typeof(RectTransform));
            _pickerPanel.transform.SetParent(combat.transform, false);
            UiStyle.Anchor((RectTransform)_pickerPanel.transform, 0.2f, 0.32f, 0.8f, 0.68f);

            // ---- the ESC overlay — an OVERLAY, not a screen (R-55): it hangs beside the screen
            // roots so it can sit on top of whichever one is active, shown exactly while the
            // session's flag is up. Its backdrop dims the match and catches stray clicks.
            EscOverlayRoot = new GameObject("EscOverlay", typeof(RectTransform));
            EscOverlayRoot.transform.SetParent(_ui.Canvas.transform, false);
            UiStyle.Stretch((RectTransform)EscOverlayRoot.transform);
            var overlayBackdrop = EscOverlayRoot.AddComponent<Image>();
            overlayBackdrop.color = UiStyle.PanelDark;
            EscOverlayRoot.SetActive(false);

            EscCloseButton = NewButton(EscOverlayRoot, "EscCloseButton", "CLOSE");
            UiStyle.Anchor((RectTransform)EscCloseButton.transform, 0.4f, 0.45f, 0.6f, 0.53f);
            EscCloseButton.onClick.AddListener(() => _shell.Session.SetOverlayOpen(false));

            EscLeaveButton = NewButton(EscOverlayRoot, "EscLeaveButton", "LEAVE MATCH");
            UiStyle.Anchor((RectTransform)EscLeaveButton.transform, 0.4f, 0.34f, 0.6f, 0.42f);
            EscLeaveButton.onClick.AddListener(() => _shell.LeaveToTitle());

            // ---- S6 / S7 · Post-match — PLAY AGAIN / RETRY and MAIN MENU, one pair per screen.
            foreach (var screen in new[] { UiScreen.Victory, UiScreen.Defeat })
            {
                var root = ui.ScreenRoot(screen);

                var rematch = NewButton(root, "RematchButton",
                    screen == UiScreen.Victory ? "PLAY AGAIN" : "RETRY");
                UiStyle.Anchor((RectTransform)rematch.transform, 0.38f, 0.28f, 0.62f, 0.36f);
                rematch.onClick.AddListener(() =>
                {
                    var postMatch = _shell.PostMatch;
                    if (postMatch != null)
                    {
                        postMatch.RequestRematch();
                    }
                });
                _rematchButtons[screen] = rematch;

                var mainMenu = NewButton(root, "MainMenuButton", "MAIN MENU");
                UiStyle.Anchor((RectTransform)mainMenu.transform, 0.38f, 0.18f, 0.62f, 0.26f);
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
        /// Ticket 027 (T-27) — the placement ghost's on-screen visual: a <see cref="Graphic"/>
        /// under the Planning screen root, active exactly while
        /// <see cref="PlanningScreenModel.GhostActive"/>, and tinted differently while
        /// <see cref="PlanningScreenModel.GhostInvalid"/> (the wireframe's "invalid zones tint
        /// red") than while the hovered zone is valid. The ghost was model-state only until the
        /// owner's Play test showed nothing on screen.
        /// </summary>
        public Graphic GhostVisual => _ghostVisual;

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

            RefreshGhostVisual();
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

        /// <summary>
        /// T-27 — mirror the ghost model onto its visual: active exactly with GhostActive, amber
        /// over a valid zone, the wireframe's red tint over an invalid one. Values only.
        /// </summary>
        private void RefreshGhostVisual()
        {
            var planning = _shell.Planning;
            var ghostOn = planning != null && planning.GhostActive;

            if (_ghostVisual.gameObject.activeSelf != ghostOn)
            {
                _ghostVisual.gameObject.SetActive(ghostOn);
            }

            if (ghostOn)
            {
                _ghostVisual.color = planning.GhostInvalid
                    ? UiStyle.GhostInvalidTint
                    : UiStyle.GhostValid;
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
            var grown = false;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];

                Button button;
                if (!_shopButtons.TryGetValue(item.Type, out button))
                {
                    button = NewButton(_shopBar, "Shop_" + item.Type,
                        item.Type + "  ·  " + item.Cost);
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
                    grown = true;
                }

                button.interactable = item.Affordable;
            }

            // T-27 — spread the row across the bar whenever it grew: each button takes its slice
            // of the bottom band, in catalog order.
            if (grown)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    Button button;
                    if (_shopButtons.TryGetValue(items[i].Type, out button))
                    {
                        UiStyle.Anchor((RectTransform)button.transform,
                            (i + 0.05f) / items.Count, 0.1f, (i + 0.95f) / items.Count, 0.9f);
                    }
                }
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

            var resized = _pickerButtons.Count != choices.Count;

            while (_pickerButtons.Count < choices.Count)
            {
                _pickerButtons.Add(
                    NewButton(_pickerPanel, "PickerChoice_" + _pickerButtons.Count, string.Empty));
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

                // T-27 — a card row: each choice takes its slice of the picker panel, named on
                // its face (copy is presentation; the choice literal is the honest name).
                if (resized)
                {
                    UiStyle.Anchor((RectTransform)button.transform,
                        (i + 0.04f) / choices.Count, 0f, (i + 0.96f) / choices.Count, 1f);
                }

                SetCaption(button, choice);

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

        /// <summary>
        /// T-27 — a Button the player can SEE and HIT: an enabled raycastable Image background
        /// (the imported RedHollowArt/button-normal chrome when available, a solid Lantern Deep
        /// face otherwise), a ColorTint transition whose disabled tint visibly differs from
        /// normal, and a fonted caption label.
        /// </summary>
        private static Button NewButton(GameObject parent, string name, string caption)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);

            var face = go.AddComponent<Image>();
            var sprite = UiStyle.ButtonSprite;
            if (sprite != null)
            {
                face.sprite = sprite;
                face.color = Color.white;
            }
            else
            {
                face.color = UiStyle.ButtonFace;
            }

            face.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = face;
            button.transition = Selectable.Transition.ColorTint;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.92f, 0.72f, 1f);
            colors.pressedColor = new Color(0.78f, 0.62f, 0.42f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.42f, 0.38f, 0.34f, 0.6f);
            button.colors = colors;

            var label = NewLabel(go, "Label", 20);
            label.text = caption ?? string.Empty;

            return button;
        }

        /// <summary>
        /// T-27 — an InputField that renders what is typed: a background well the player can
        /// click into (targetGraphic), a fonted child textComponent the value renders through,
        /// and a faded placeholder naming what belongs in it.
        /// </summary>
        private static InputField NewInput(GameObject parent, string name, string placeholderCopy)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);

            var well = go.AddComponent<Image>();
            well.color = UiStyle.InputWell;
            well.raycastTarget = true;

            var input = go.AddComponent<InputField>();
            input.targetGraphic = well;

            var text = NewLabel(go, "Text", 18);
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            Pad(text.rectTransform);
            input.textComponent = text;

            var placeholder = NewLabel(go, "Placeholder", 18);
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.color = new Color(0.62f, 0.55f, 0.44f, 0.7f);
            placeholder.text = placeholderCopy ?? string.Empty;
            Pad(placeholder.rectTransform);
            input.placeholder = placeholder;

            return input;
        }

        private static Text NewLabel(GameObject parent, string name, int size = 16)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            UiStyle.Stretch((RectTransform)go.transform);
            var label = go.AddComponent<Text>();
            label.text = string.Empty;
            UiStyle.StyleLabel(label, size);
            return label;
        }

        /// <summary>Retint an existing button's caption label (the picker's re-aimed cards).</summary>
        private static void SetCaption(Button button, string caption)
        {
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = caption ?? string.Empty;
            }
        }

        /// <summary>Inset a stretched child a few pixels from its parent's edges.</summary>
        private static void Pad(RectTransform rt)
        {
            rt.offsetMin = new Vector2(10f, 4f);
            rt.offsetMax = new Vector2(-10f, -4f);
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
