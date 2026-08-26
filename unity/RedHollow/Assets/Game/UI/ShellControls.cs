using System;
using System.Collections.Generic;
using RedHollow.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace RedHollow.Game.UI
{
    /// <summary>
    /// Ticket 023 (T-23) — the shell's interactive controls: every uGUI Button / InputField the
    /// wireframes (docs/ui-wireframes.html) name, wired to the ticket-012 model actions the shell
    /// already owns. This is the STUB the T23 tests were written against; every member throws
    /// until the implementer lands it.
    ///
    /// The pinned convention is ACCESSOR PROPERTIES on this class (the same shape as
    /// <see cref="ShellUi"/>'s label fields), reached through <see cref="ShellBootstrap.Controls"/>.
    /// Screen-specific controls live UNDER their screen's root (<see cref="ShellUi.ScreenRoot"/>),
    /// so R-60's activation flipping shows and hides them for free.
    ///
    /// Thinness (T-10): plain C#, never a MonoBehaviour, and the closures the buttons invoke call
    /// MODEL / SESSION methods only — never sim state. The Cecil scan reads compiler-generated
    /// closure classes too.
    ///
    /// Pointer-driven planning actions (ghost follow, place, sell) are pinned at the WIRING SEAM:
    /// the methods below take a ground-space position (<see cref="Vec2"/>, SimSpace's x-right /
    /// y-forward plane) or a placeable id. Resolving a screen ray to that position / id is
    /// play-mode raycasting territory and is deliberately not pinned here.
    /// </summary>
    public sealed class ShellControls
    {
        private static Exception NotYet() =>
            new NotImplementedException("ticket 023 — interactive UI controls are not wired yet");

        // ---- S1 · Title / Join ----------------------------------------------------------------

        /// <summary>S1 — the callsign input; typing loads the profile (TitleScreenModel.SetCallsign).</summary>
        public InputField CallsignInput => throw NotYet();

        /// <summary>S1 — HOST GAME: opens the lobby as the typed callsign (R-50).</summary>
        public Button HostButton => throw NotYet();

        /// <summary>S1 — the join-code input; editing clears the inline error.</summary>
        public InputField JoinCodeInput => throw NotYet();

        /// <summary>S1 — JOIN: a failed join raises the modeled inline error and stays on S1.</summary>
        public Button JoinButton => throw NotYet();

        /// <summary>S1 — the inline error under the code input; empty while no error is modeled.</summary>
        public Text JoinErrorLabel => throw NotYet();

        // ---- S2 · Lobby -----------------------------------------------------------------------

        /// <summary>S2 — one PICK button per hero class card (a <see cref="HeroClass"/> literal).</summary>
        public Button ClassPickButton(string heroClass) => throw NotYet();

        /// <summary>S2 — READY (LobbyScreenModel.SetReady; all-ready auto-starts the match).</summary>
        public Button LobbyReadyButton => throw NotYet();

        // ---- S3 · Planning --------------------------------------------------------------------

        /// <summary>
        /// S3 — one shop-bar button per R-23 catalog row (a <see cref="PlaceableType"/> literal).
        /// Non-interactable exactly when the modeled <see cref="ShopItem.Affordable"/> is false.
        /// </summary>
        public Button ShopItemButton(string placeableType) => throw NotYet();

        /// <summary>S3 — READY UP (PlanningScreenModel.ReadyUp; all-ready starts combat early).</summary>
        public Button PlanningReadyButton => throw NotYet();

        /// <summary>
        /// The cursor moved over the ground: while a ghost is up this is
        /// <see cref="PlanningScreenModel.MoveGhost"/>. Zone validity is the caller's answer —
        /// resolving it from geometry is the play-mode raycaster's job.
        /// </summary>
        public void PointerAt(Vec2 groundPos, bool zoneValid) => throw NotYet();

        /// <summary>
        /// A ground click: with a ghost up, move it there and
        /// <see cref="PlanningScreenModel.ConfirmPlacement"/> (a rejection leaves the ghost up).
        /// </summary>
        public void ClickGround(Vec2 groundPos, bool zoneValid) => throw NotYet();

        /// <summary>
        /// A click on a standing placeable: <see cref="PlanningScreenModel.Sell"/> — the R-22
        /// refund path. The id is the seam; hit-testing a click to an id is play-mode territory.
        /// </summary>
        public void ClickPlaceable(string placeableId) => throw NotYet();

        // ---- S4 · Combat ----------------------------------------------------------------------

        /// <summary>
        /// S4 — the skill-point badge (R-61/R-62): visible exactly while
        /// <see cref="CombatHudModel.SkillPointBadge"/>; clicking opens the picker.
        /// </summary>
        public Button LevelUpBadgeButton => throw NotYet();

        /// <summary>
        /// S4 — one button per <see cref="CombatHudModel.PickerChoices"/> card, aligned by index;
        /// clicking the i-th spends that choice (<see cref="CombatHudModel.Spend"/>).
        /// </summary>
        public IReadOnlyList<Button> PickerChoiceButtons => throw NotYet();

        /// <summary>R-55 — the ESC overlay's root: active exactly while the overlay is open.</summary>
        public GameObject EscOverlayRoot => throw NotYet();

        /// <summary>R-55 — closes the overlay (NetSession.SetOverlayOpen(false)). Never a pause.</summary>
        public Button EscCloseButton => throw NotYet();

        /// <summary>
        /// R-55 — LEAVE MATCH: the local peer leaves the session
        /// (<see cref="RedHollow.Game.Net.NetSession.Disconnect"/> for the local peer id).
        /// </summary>
        public Button EscLeaveButton => throw NotYet();

        // ---- S6 / S7 · Post-match -------------------------------------------------------------

        /// <summary>
        /// S6's PLAY AGAIN / S7's RETRY (screen ∈ {Victory, Defeat}): interactable exactly when
        /// <see cref="PostMatchModel.CanRematch"/>; clicking requests the rematch back to S2.
        /// </summary>
        public Button RematchButton(UiScreen screen) => throw NotYet();

        /// <summary>S6/S7 — MAIN MENU: returns to the title screen (see the T23 tests' pin).</summary>
        public Button MainMenuButton(UiScreen screen) => throw NotYet();
    }
}
