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
        private readonly HostedMatch _match;

        private readonly string _localPlayerId;

        private readonly List<HotspotReadout> _hotspots = new List<HotspotReadout>();

        private readonly List<ShopItem> _shopItems = new List<ShopItem>();

        private readonly List<int> _pulsingEntryTunnels = new List<int>();

        private bool _ghostActive;

        private string _ghostType;

        private Vec2 _ghostPos;

        private bool _ghostZoneValid = true;

        private string _lastPurchaseRejection;

        private bool _lastSellRefused;

        public PlanningScreenModel(HostedMatch match, string localPlayerId)
        {
            _match = match;
            _localPlayerId = localPlayerId;
        }

        // ---- top bar --------------------------------------------------------------------------

        public int WaveNumber => _match.State.Wave.Number;

        public int TotalWaves => _match.State.Wave.TotalWaves;

        /// <summary>R-20 — the one shared pool.</summary>
        public int Scrip => _match.State.Team.Scrip;

        /// <summary>
        /// R-03 — seconds left on the planning countdown, clamped at 0. The deadline is INCLUSIVE
        /// repo-wide: at now == deadline the sim has already opened combat.
        /// </summary>
        public double TimerRemainingSeconds
        {
            get
            {
                var deadline = _match.State.PlanningStartedAt
                               + _match.Sim.Config.PlanningDurationSeconds;
                var remaining = deadline - _match.Clock.ElapsedSeconds;
                return remaining > 0.0 ? remaining : 0.0;
            }
        }

        public IReadOnlyList<HotspotReadout> Hotspots => _hotspots;

        // ---- entry preview (R-05 / DEC-018) ---------------------------------------------------

        /// <summary>The entry tunnels that pulse red — indices only, no composition.</summary>
        public IReadOnlyList<int> PulsingEntryTunnels => _pulsingEntryTunnels;

        // ---- shop bar and ghost placement (R-63) ----------------------------------------------

        /// <summary>Every R-23 catalog row, catalog-priced, affordability derived from the pool.</summary>
        public IReadOnlyList<ShopItem> ShopItems => _shopItems;

        public bool GhostActive => _ghostActive;

        public string GhostType => _ghostType;

        public Vec2 GhostPos => _ghostPos;

        /// <summary>R-24 — invalid zone under the cursor → ghost tints red.</summary>
        public bool GhostInvalid => _ghostActive && !_ghostZoneValid;

        /// <summary>
        /// The reason string off the last `purchase_rejected` event, or null. The reason IS
        /// carried for purchases (unlike sales) and the UI surfaces it.
        /// </summary>
        public string LastPurchaseRejection => _lastPurchaseRejection;

        /// <summary>
        /// R-22 — a refused sale is `accepted: false` and NOTHING else: SellResult carries no
        /// reason field, so neither does the UI.
        /// </summary>
        public bool LastSellRefused => _lastSellRefused;

        /// <summary>Click a shop item: the ghost starts following the cursor.</summary>
        public void BeginPlacement(string placeableType)
        {
            _ghostActive = true;
            _ghostType = placeableType;
            _ghostZoneValid = true;
        }

        /// <summary>The cursor moved; the shell answers whether the zone under it is valid (R-24).</summary>
        public void MoveGhost(Vec2 pos, bool zoneValid)
        {
            _ghostPos = pos;
            _ghostZoneValid = zoneValid;
        }

        public void CancelPlacement()
        {
            _ghostActive = false;
            _ghostType = null;
        }

        /// <summary>
        /// Click to place: one <see cref="MatchSim.PurchasePlacement"/> command, catalog-priced.
        /// A rejection leaves the ghost up (the shake is presentation) and surfaces the reason.
        /// </summary>
        public PurchaseResult ConfirmPlacement()
        {
            // R-24 (T-23) — the shell's zone answer is already "no": don't issue a command whose
            // outcome is known. The refusal mirrors the sim's own invalid-zone shape (reason
            // surfaced, ghost stays up), and the sim STILL reaches its own verdict on everything
            // actually sent — request.ZoneValid is deliberately never read there (R-51).
            if (!_ghostZoneValid)
            {
                _lastPurchaseRejection = "invalid_zone";
                return new PurchaseResult
                {
                    Accepted = false,
                    PlaceableType = _ghostType,
                    ScripAfter = _match.State.Team.Scrip,
                    RejectionReason = _lastPurchaseRejection,
                };
            }

            var catalogCost = _match.Sim.Config.Placeables.StatsFor(_ghostType).Cost;
            var result = _match.Sim.PurchasePlacement(new PurchaseRequest
            {
                PlayerId = _localPlayerId,
                PlaceableType = _ghostType,
                Cost = catalogCost,
                Pos = _ghostPos,
                ZoneValid = _ghostZoneValid,
            });

            if (result.Accepted)
            {
                // R-63 — an accepted placement clears the ghost …
                _ghostActive = false;
                _ghostType = null;
                _lastPurchaseRejection = null;
            }
            else
            {
                // … and a rejected one leaves it up for the retry, reason surfaced verbatim.
                _lastPurchaseRejection = result.RejectionReason;
            }

            return result;
        }

        // ---- sell (R-22) ----------------------------------------------------------------------

        /// <summary>The tooltip's refund figure: the R-22 ratio of what was paid, never a literal.</summary>
        public int SellRefundFor(string placeableId)
        {
            if (string.IsNullOrEmpty(placeableId)
                || !_match.State.Placeables.TryGetValue(placeableId, out var placeable))
            {
                return 0;
            }

            // Floored, matching the sim's own DEC-011 rule.
            return (int)(placeable.PurchaseCost * _match.Sim.Config.SellRefundRatio);
        }

        public SellResult Sell(string placeableId)
        {
            var result = _match.Sim.SellPlacement(new SellRequest
            {
                PlayerId = _localPlayerId,
                PlaceableId = placeableId,
            });

            // R-22 — a refusal is a flag and nothing more; no reason exists to surface.
            _lastSellRefused = !result.Accepted;

            return result;
        }

        // ---- ready panel (R-03) ---------------------------------------------------------------

        public int ReadyCount
        {
            get
            {
                var count = 0;
                foreach (var player in _match.State.Players)
                {
                    if (player.Connected && player.Ready)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>The denominator = connected players, so a leaver stops being waited on.</summary>
        public int ConnectedCount
        {
            get
            {
                var count = 0;
                foreach (var player in _match.State.Players)
                {
                    if (player.Connected)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>READY UP — <see cref="MatchSim.SetPlayerReady"/> for the local player.</summary>
        public ReadyResult ReadyUp() => _match.Sim.SetPlayerReady(_localPlayerId);

        /// <summary>Re-read the state and the preview.</summary>
        public void Refresh()
        {
            var state = _match.State;

            _hotspots.Clear();
            foreach (var hotspot in state.Hotspots.Values)
            {
                _hotspots.Add(new HotspotReadout
                {
                    HotspotId = hotspot.Id,
                    Civilians = hotspot.Civilians,
                    Lost = hotspot.Civilians <= 0,
                });
            }

            _shopItems.Clear();
            var catalog = _match.Sim.Config.Placeables;
            foreach (var type in catalog.Types)
            {
                var cost = catalog.StatsFor(type).Cost;
                _shopItems.Add(new ShopItem
                {
                    Type = type,
                    Cost = cost,
                    Affordable = cost <= state.Team.Scrip,
                });
            }

            _pulsingEntryTunnels.Clear();
            if (!state.IsOver && state.Phase == MatchPhase.Planning)
            {
                _pulsingEntryTunnels.AddRange(_match.Sim.PreviewUpcomingWave().ActiveEntryTunnels);
            }
        }
    }
}
