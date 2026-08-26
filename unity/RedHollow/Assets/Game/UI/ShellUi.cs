using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RedHollow.Game.UI
{
    /// <summary>
    /// Ticket 021 (T-21) — the built uGUI hierarchy: one Canvas, one root GameObject per
    /// <see cref="UiScreen"/> (S1–S7), and the combat HUD's bound labels. This is a HANDLE object,
    /// not a presenter: every value a label shows comes out of the ticket-012 models
    /// (<see cref="CombatHudModel"/> and friends), pushed in by <see cref="ShellBootstrap.Pump"/> —
    /// nothing here reads sim state, decides a rule, or holds a sim reference (T-10's invariant).
    ///
    /// Label COPY is presentation and is never contract: the tests assert that a label's text
    /// contains the model's value ("Wave 1/10", "1", "wave 1" all pass), not what surrounds it.
    ///
    /// Built headlessly on purpose (plain GameObjects + components, no prefab, no scene asset), the
    /// same shape as <see cref="RedHollow.Game.View.MatchSceneBuilder"/>: one description of the UI
    /// serves the runtime bootstrap and an EditMode test without drifting.
    /// </summary>
    public sealed class ShellUi
    {
        /// <summary>
        /// The root everything below hangs from, named "RedHollow_Shell" so a session (or a test
        /// teardown) can find and destroy the whole UI in one call — the same convention as
        /// "RedHollow_Match" and "RedHollow_MatchViews".
        /// </summary>
        public GameObject Root;

        /// <summary>The one Canvas the shell renders through (the project has no UI Toolkit assets).</summary>
        public Canvas Canvas;

        /// <summary>R-61 — shows <see cref="CombatHudModel.WaveNumber"/> (TotalWaves may ride along).</summary>
        public Text WaveLabel;

        /// <summary>R-61 — shows <see cref="CombatHudModel.Scrip"/>.</summary>
        public Text ScripLabel;

        /// <summary>R-61 — shows the own hero's HP off <see cref="CombatHudModel.Hp"/>.</summary>
        public Text HpLabel;

        /// <summary>R-61 — shows <see cref="CombatHudModel.MonstersRemaining"/>.</summary>
        public Text MonstersRemainingLabel;

        /// <summary>
        /// R-61 — one label per <see cref="CombatHudModel.Hotspots"/> readout, each showing that
        /// shelter's civilian count. Order matches the model's readout order.
        /// </summary>
        public IReadOnlyList<Text> HotspotLabels;

        /// <summary>
        /// The panel the HUD labels hang under, kept so the bootstrap can grow the per-hotspot
        /// label row to match the live colony without rebuilding the shell.
        /// </summary>
        internal GameObject HudPanel;

        /// <summary>The writable list behind <see cref="HotspotLabels"/>.</summary>
        internal readonly List<Text> HotspotLabelList = new List<Text>();

        private readonly Dictionary<UiScreen, GameObject> _screenRoots =
            new Dictionary<UiScreen, GameObject>();

        /// <summary>
        /// R-60 — the container GameObject for one screen. Total over the enum: every screen S1–S7
        /// has a root, they are distinct, and after a <see cref="ShellBootstrap.Pump"/> exactly the
        /// root of <see cref="UiRouter.Screen"/> is active in the hierarchy — screen switching is
        /// activation flipping, never rebuild-per-frame.
        /// </summary>
        public GameObject ScreenRoot(UiScreen screen)
        {
            GameObject root;
            return _screenRoots.TryGetValue(screen, out root) ? root : null;
        }

        /// <summary>
        /// Build the whole headless hierarchy: root, Canvas, one container per S1–S7, and the
        /// pinned HUD labels. Layout, fonts and copy are deliberately absent — presentation.
        /// </summary>
        internal static ShellUi Build()
        {
            var ui = new ShellUi();

            ui.Root = new GameObject("RedHollow_Shell");

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(ui.Root.transform, false);
            ui.Canvas = canvasGo.AddComponent<Canvas>();

            foreach (UiScreen screen in Enum.GetValues(typeof(UiScreen)))
            {
                var screenGo = new GameObject("Screen_" + screen);
                screenGo.transform.SetParent(canvasGo.transform, false);
                screenGo.SetActive(false);
                ui._screenRoots[screen] = screenGo;
            }

            ui.HudPanel = new GameObject("HUD");
            ui.HudPanel.transform.SetParent(canvasGo.transform, false);

            ui.WaveLabel = NewLabel(ui.HudPanel, "WaveLabel");
            ui.ScripLabel = NewLabel(ui.HudPanel, "ScripLabel");
            ui.HpLabel = NewLabel(ui.HudPanel, "HpLabel");
            ui.MonstersRemainingLabel = NewLabel(ui.HudPanel, "MonstersRemainingLabel");
            ui.HotspotLabels = ui.HotspotLabelList;

            return ui;
        }

        /// <summary>R-60 — exactly the routed screen's root is active; everything else is off.</summary>
        internal void SetActiveScreen(UiScreen active)
        {
            foreach (var pair in _screenRoots)
            {
                var shouldBeActive = pair.Key == active;
                if (pair.Value != null && pair.Value.activeSelf != shouldBeActive)
                {
                    pair.Value.SetActive(shouldBeActive);
                }
            }
        }

        /// <summary>
        /// R-61 — one label per shelter readout. Grows and shrinks with the live colony; the
        /// labels themselves persist across pumps (refresh, never rebuild-per-frame).
        /// </summary>
        internal void EnsureHotspotLabels(int count)
        {
            while (HotspotLabelList.Count < count)
            {
                HotspotLabelList.Add(NewLabel(HudPanel, "HotspotLabel_" + HotspotLabelList.Count));
            }

            while (HotspotLabelList.Count > count)
            {
                var last = HotspotLabelList[HotspotLabelList.Count - 1];
                HotspotLabelList.RemoveAt(HotspotLabelList.Count - 1);
                if (last != null)
                {
                    DestroyGameObject(last.gameObject);
                }
            }
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
                UnityEngine.Object.Destroy(go);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
