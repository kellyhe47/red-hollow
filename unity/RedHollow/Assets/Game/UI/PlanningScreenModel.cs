using System;
using System.Collections.Generic;
using RedHollow.Game.Net;
using RedHollow.Sim;

namespace RedHollow.Game.UI
{
    /// <summary>One shop-bar entry (R-63). Greyed-with-cost-in-red is presentation; the flag is contract.</summary>
    public sealed class ShopItem
    {
        /// <summary>A <see cref="PlaceableType"/> constant.</summary>
        public string Type;

        /// <summary>The R-23 catalog's price — never a literal typed into the UI.</summary>
        public int Cost;

        /// <summary>False when <see cref="Cost"/> exceeds the shared pool → grey + red cost.</summary>
        public bool Affordable;
    }

    /// <summary>One hotspot's top-bar readout, shared by S3 and S4.</summary>
    public sealed class HotspotReadout
    {
        public string HotspotId;
        public int Civilians;

        /// <summary>R-12/R-13 — emptied → building marked dark/lost.</summary>
        public bool Lost;
    }

    /// <summary>
    /// Ticket 012 (T-12) — S3 Planning (R-60 / R-63): top bar, pulsing entry preview, shop bar
    /// with ghost placement, sell-on-click with the 50% tooltip, and the ready panel.
    ///
    /// Read-only over <see cref="MatchState"/>; every mutation is a <see cref="MatchSim"/> command
    /// issued through the hosted match (T-10's IL invariant is what makes this shape mandatory).
    ///
    /// R-05 / DEC-018 — the preview is PARTIAL by construction: this model exposes the activating
    /// entry-tunnel indices from <see cref="MatchSim.PreviewUpcomingWave"/> and has no member able
    /// to name a monster type or count for the upcoming wave. Do not add one.
    /// </summary>
    public sealed class PlanningScreenModel
    {
        public PlanningScreenModel(HostedMatch match, string localPlayerId) =>
            throw new NotImplementedException("T-12 / R-63: the planning screen");

        // ---- top bar --------------------------------------------------------------------------

        public int WaveNumber =>
            throw new NotImplementedException("T-12 / R-61: wave number");

        public int TotalWaves =>
            throw new NotImplementedException("T-12 / R-61: total waves");

        /// <summary>R-20 — the one shared pool.</summary>
        public int Scrip =>
            throw new NotImplementedException("T-12 / R-61: shared scrip");

        /// <summary>
        /// R-03 — seconds left on the planning countdown, clamped at 0. The deadline is INCLUSIVE
        /// repo-wide: at now == deadline the sim has already opened combat.
        /// </summary>
        public double TimerRemainingSeconds =>
            throw new NotImplementedException("T-12 / R-03: the planning timer");

        public IReadOnlyList<HotspotReadout> Hotspots =>
            throw new NotImplementedException("T-12 / R-61: per-hotspot civilians");

        // ---- entry preview (R-05 / DEC-018) ---------------------------------------------------

        /// <summary>The entry tunnels that pulse red — indices only, no composition.</summary>
        public IReadOnlyList<int> PulsingEntryTunnels =>
            throw new NotImplementedException("T-12 / R-05: pulsing entries");

        // ---- shop bar and ghost placement (R-63) ----------------------------------------------

        /// <summary>Every R-23 catalog row, catalog-priced, affordability derived from the pool.</summary>
        public IReadOnlyList<ShopItem> ShopItems =>
            throw new NotImplementedException("T-12 / R-63: the shop bar");

        public bool GhostActive =>
            throw new NotImplementedException("T-12 / R-63: ghost placement");

        public string GhostType =>
            throw new NotImplementedException("T-12 / R-63: ghost type");

        public Vec2 GhostPos =>
            throw new NotImplementedException("T-12 / R-63: ghost position");

        /// <summary>R-24 — invalid zone under the cursor → ghost tints red.</summary>
        public bool GhostInvalid =>
            throw new NotImplementedException("T-12 / R-24: invalid-zone tint");

        /// <summary>
        /// The reason string off the last `purchase_rejected` event, or null. The reason IS
        /// carried for purchases (unlike sales) and the UI surfaces it.
        /// </summary>
        public string LastPurchaseRejection =>
            throw new NotImplementedException("T-12 / R-21: purchase rejection reason");

        /// <summary>
        /// R-22 — a refused sale is `accepted: false` and NOTHING else: SellResult carries no
        /// reason field, so neither does the UI.
        /// </summary>
        public bool LastSellRefused =>
            throw new NotImplementedException("T-12 / R-22: refused sale flag");

        /// <summary>Click a shop item: the ghost starts following the cursor.</summary>
        public void BeginPlacement(string placeableType) =>
            throw new NotImplementedException("T-12 / R-63: begin placement");

        /// <summary>The cursor moved; the shell answers whether the zone under it is valid (R-24).</summary>
        public void MoveGhost(Vec2 pos, bool zoneValid) =>
            throw new NotImplementedException("T-12 / R-63: move the ghost");

        public void CancelPlacement() =>
            throw new NotImplementedException("T-12 / R-63: cancel placement");

        /// <summary>
        /// Click to place: one <see cref="MatchSim.PurchasePlacement"/> command, catalog-priced.
        /// A rejection leaves the ghost up (the shake is presentation) and surfaces the reason.
        /// </summary>
        public PurchaseResult ConfirmPlacement() =>
            throw new NotImplementedException("T-12 / R-21: purchase");

        // ---- sell (R-22) ----------------------------------------------------------------------

        /// <summary>The tooltip's refund figure: the R-22 ratio of what was paid, never a literal.</summary>
        public int SellRefundFor(string placeableId) =>
            throw new NotImplementedException("T-12 / R-22: the 50% tooltip");

        public SellResult Sell(string placeableId) =>
            throw new NotImplementedException("T-12 / R-22: sell");

        // ---- ready panel (R-03) ---------------------------------------------------------------

        public int ReadyCount =>
            throw new NotImplementedException("T-12 / R-03: ready count");

        /// <summary>The denominator = connected players, so a leaver stops being waited on.</summary>
        public int ConnectedCount =>
            throw new NotImplementedException("T-12 / R-03: connected count");

        /// <summary>READY UP — <see cref="MatchSim.SetPlayerReady"/> for the local player.</summary>
        public ReadyResult ReadyUp() =>
            throw new NotImplementedException("T-12 / R-03: ready up");

        /// <summary>Re-read the state and the preview.</summary>
        public void Refresh() =>
            throw new NotImplementedException("T-12: refresh the planning screen");
    }
}
