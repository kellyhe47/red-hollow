using System;
using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 005 (T-05) owns this half of <see cref="MatchSim"/>: the shared scrip economy.
    /// Requirements R-20, R-21, R-22, R-24, R-25; graded by fixtures G-013, G-014, G-015, G-022.
    ///
    /// One pool, two commands. <see cref="PurchasePlacement"/> takes scrip out and puts a defence on
    /// the ground; <see cref="SellPlacement"/> takes the defence off the ground and puts half its
    /// price back. Neither asks who owns what (R-25): the pool is the team's, and so is everything
    /// standing on the map.
    ///
    /// Both are host-authoritative (R-51), which is the whole reason the validation below exists on
    /// this side of the wire at all — see <see cref="PurchaseRequest.ZoneValid"/>.
    ///
    /// The shared core — fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>R-21 / G-015 — fixture-pinned: buying outside a live planning phase.</summary>
        private const string RejectionWrongPhase = "wrong_phase";

        /// <summary>R-21 / G-014 — fixture-pinned: the price is more than the pool holds.</summary>
        private const string RejectionInsufficientScrip = "insufficient_scrip";

        /// <summary>
        /// R-24 — one reason for all three exclusions, because R-24 is one rule: "anywhere on colony
        /// ground except inside hotspot buildings, on entry tunnel mouths, or overlapping other
        /// placeables". No fixture pins the string; what matters is that it is neither
        /// <see cref="RejectionInsufficientScrip"/> nor <see cref="RejectionWrongPhase"/>, since a
        /// zone problem reported as a money or phase problem sends the player to fix the wrong thing.
        /// </summary>
        private const string RejectionInvalidZone = "invalid_zone";

        /// <summary>
        /// R-23 — a placeable type the catalog has no row for: a client on a newer build, or a
        /// hand-crafted packet. Refused rather than thrown, so the host stays up and the buyer is
        /// told; <see cref="PlaceableCatalog.StatsFor"/> still throws for rule code that has no
        /// honest way to continue.
        /// </summary>
        private const string RejectionUnknownPlaceable = "unknown_placeable_type";

        /// <summary>Prefix for ids minted by <see cref="PurchasePlacement"/>.</summary>
        private const string PurchasedPlaceableIdPrefix = "pl_";

        private ColonyMap _colonyMap;

        /// <summary>How many ids <see cref="NextPlaceableId"/> has minted for this match.</summary>
        private int _placeablesPurchased;

        /// <summary>
        /// R-24 — the colony layout this match is played on, and the seam the sim-side placement
        /// checker needs.
        ///
        /// R-24 excludes three things: hotspot building interiors, entry tunnel mouths, and
        /// overlaps with other placeables. Two of the three are already answerable from
        /// <see cref="MatchState"/> (<see cref="MatchState.Hotspots"/> and
        /// <see cref="MatchState.Placeables"/> both carry positions); the tunnel mouths exist
        /// nowhere except <see cref="RedHollow.Sim.ColonyMap.EntryTunnels"/>, so without this the
        /// sim cannot enforce R-24 at all and is left trusting the client's own
        /// <see cref="PurchaseRequest.ZoneValid"/> verdict.
        ///
        /// A settable seam rather than a constructor argument, mirroring <see cref="WaveTable"/>:
        /// the golden adapter builds a <see cref="MatchSim"/> from a fixture's `given`, which names
        /// no map, so the sim must stay constructible without one — and a match with no map named
        /// is a match on the one shipped map (R-10), not a match with no tunnels.
        /// </summary>
        public ColonyMap ColonyMap
        {
            // Built on first read rather than in the constructor, exactly as WaveTable is: a caller
            // that assigns its own authored map must not pay for a discarded default.
            get => _colonyMap ?? (_colonyMap = RedHollow.Sim.ColonyMap.V1());
            set => _colonyMap = value;
        }

        /// <summary>
        /// R-24 — how far "inside a hotspot building" reaches from the shelter's point. R-24 names
        /// the exclusion but no footprint, so this is taste made tunable rather than spec: 4.0 is a
        /// shelter roughly 8 units across on a cavern ~60 units wide (see
        /// <see cref="RedHollow.Sim.ColonyMap.V1"/>), which reads as a building without swallowing
        /// the ground the team is meant to fortify around it.
        ///
        /// Per instance and settable, for the same reason <see cref="WaveTable"/> is: it is
        /// placement-rule data nothing else in the sim reads, so it lives here rather than widening
        /// <see cref="SimConfig"/>.
        /// </summary>
        public double HotspotBuildingRadius { get; set; } = 4.0;

        /// <summary>
        /// R-24 / R-14 — how far the exclusion around an entry tunnel mouth reaches. Smaller than
        /// <see cref="HotspotBuildingRadius"/> because a breach is a hole, not a building: 3.0 keeps
        /// the mouth itself clear so a wave can enter, while still allowing a kill box built right
        /// outside it, which is the intended play.
        /// </summary>
        public double EntryTunnelMouthRadius { get; set; } = 3.0;

        /// <summary>
        /// R-24 — the footprint radius of a single placeable; two overlap when their centres are
        /// closer than twice this. One radius for all five rows because R-23 gives no sizes, and a
        /// per-type table would be inventing numbers the PRD does not have.
        ///
        /// 1.5 puts neighbours 3.0 apart at the closest, which keeps a trap line buildable and stays
        /// well inside a turret's range 8 (R-23), so packing defences together remains a real choice.
        /// </summary>
        public double PlaceableFootprintRadius { get; set; } = 1.5;

        /// <summary>
        /// R-21, R-24, R-25 / B-008. Buy and place a defence.
        ///
        /// The order of the gates is the order R-21 states them, with R-24's geometry last because
        /// it is the only one that has to measure anything: wrong phase (G-015), then a type the
        /// R-23 catalog does not hold, then a price the pool cannot cover (G-014), then the zone.
        /// Nothing mutates until every gate has passed, which is how G-014 and G-015 come out with
        /// `state_changes: []` rather than with deltas that happen to cancel.
        ///
        /// The price charged is the R-23 catalog's, not the request's (R-51). A client naming its
        /// own price is a client setting its own prices; the request's `cost` is the shell's
        /// prediction, useful for showing a ghost's price tag and not evidence of anything. The
        /// charged number and the <see cref="Placeable.PurchaseCost"/> written onto the entity are
        /// the same read of the same row, because R-22 refunds half of that field — charge 10 while
        /// recording 100 and every buy-then-sell cycle mints 40 scrip.
        ///
        /// R-25: no ownership, vote or lock is consulted. <see cref="PurchaseRequest.PlayerId"/> is
        /// recorded on the entity and credited in the event, and that is all it does.
        /// </summary>
        public PurchaseResult PurchasePlacement(PurchaseRequest request)
        {
            BeginCommand();

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            // R-21 / G-015 — placement is planning-phase only, and a match that is already over is
            // not in a planning phase whatever its phase field still reads (R-01/R-02).
            if (State.IsOver || State.Phase != MatchPhase.Planning)
            {
                return RefusePurchase(request, RejectionWrongPhase);
            }

            // R-23 — the price and the entity's stats both come from this row, so no row means no
            // purchase. Looked up without throwing: an unknown type is a bad packet, not a bug here.
            var stats = _config.Placeables.TryGet(request.PlaceableType);
            if (stats == null)
            {
                return RefusePurchase(request, RejectionUnknownPlaceable);
            }

            // R-21 / G-014 — "no negative balance". Affordable at exactly the pool, which must land
            // it on zero rather than being refused by a comparison written the wrong way round.
            var cost = stats.Cost;
            if (cost > State.Team.Scrip)
            {
                return RefusePurchase(request, RejectionInsufficientScrip);
            }

            // R-24 / R-51 — the server reaches its own verdict. request.ZoneValid is deliberately
            // never read: it is the client's opinion of its own legality, and a host that accepts it
            // has the same hole the phase gate above exists to close.
            if (!IsPlaceableGround(request.Pos))
            {
                return RefusePurchase(request, RejectionInvalidZone);
            }

            var scripBefore = State.Team.Scrip;
            State.Team.Scrip = scripBefore - cost;
            RecordChange("team", "scrip", scripBefore, State.Team.Scrip);

            var countBefore = State.PlaceableCount;
            var placeable = new Placeable
            {
                Id = NextPlaceableId(),
                Type = request.PlaceableType,
                Pos = request.Pos,
                OwnerPlayerId = request.PlayerId,

                // The price actually taken out of the pool, so R-22's refund is half of what was
                // really paid. Never request.Cost.
                PurchaseCost = cost,
                Exists = true,

                // R-23 — the rest of the catalog row rides onto the entity here, so ticket 006's
                // effects read the instance rather than re-reading config mid-combat. Those columns
                // are still at their defaults until ticket 006 fills them in.
                Hp = stats.MaxHp,
                Damage = stats.Damage,
                TriggersRemaining = stats.TriggerCount,
                BlastRadius = stats.BlastRadius,
                Range = stats.Range,
            };

            State.Placeables[placeable.Id] = placeable;

            // G-013 replicates the placeable population as a count, not as a per-entity delta: the
            // shell learns *which* placeable from the event below.
            RecordChange("placeables", "count", countBefore, State.PlaceableCount);

            Emit("placeable_created", new Dictionary<string, object>
            {
                { "placeable_type", placeable.Type },
                { "pos", placeable.Pos },
                { "by", request.PlayerId },
            });

            return Finish(new PurchaseResult
            {
                Accepted = true,
                PlaceableType = request.PlaceableType,
                ScripAfter = State.Team.Scrip,

                // Present and null rather than absent: G-013 compares key sets exactly.
                RejectionReason = null,
            });
        }

        /// <summary>
        /// R-22, R-25 / B-014. Sell a placed defence back during planning.
        ///
        /// The refund is `floor(cost * SimConfig.SellRefundRatio)` of the price the entity actually
        /// records (DEC-011). Flooring is the rule, not a rounding convenience: an odd cost such as
        /// the spike trap's 75 must pay 37, and a half rounded up would let a team churn placeables
        /// for free on odd prices.
        ///
        /// R-25: any player may sell any placeable, including one another player placed. Co-op
        /// negotiation is verbal, so there is no ownership check and no lock.
        ///
        /// Sad paths, decided here — both refuse rather than throw, because a sell command is a
        /// button press and a stale one is ordinary (a double-click, a retried packet):
        ///  - a placeable already sold pays nothing a second time. <see cref="Placeable.Exists"/>
        ///    is the guard, and it is checked before any money moves — a duplicate refund is the
        ///    economy's own version of the duplicate-bounty bug <see cref="RecordMonsterKill"/>
        ///    guards against;
        ///  - an id this match never had pays nothing at all.
        ///
        /// A refusal emits no event. <see cref="SellResult"/> carries no reason field (R-22 states
        /// one condition), so there is nothing an event could say that `accepted: false` does not.
        /// </summary>
        public SellResult SellPlacement(SellRequest request)
        {
            BeginCommand();

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var result = new SellResult
            {
                Accepted = false,
                Refund = 0,
                ScripAfter = State.Team.Scrip,
            };

            // R-22 — selling is a planning-phase action, exactly as buying is: a turret cannot be
            // cashed out from under the monsters attacking it.
            if (State.IsOver || State.Phase != MatchPhase.Planning)
            {
                return Finish(result);
            }

            if (!State.Placeables.TryGetValue(request.PlaceableId, out var placeable) || !placeable.Exists)
            {
                return Finish(result);
            }

            var refund = RefundFor(placeable);

            placeable.Exists = false;
            RecordChange(placeable.Id, "exists", true, false);

            var scripBefore = State.Team.Scrip;
            State.Team.Scrip = scripBefore + refund;
            RecordChange("team", "scrip", scripBefore, State.Team.Scrip);

            Emit("placeable_sold", new Dictionary<string, object>
            {
                { "placeable_id", placeable.Id },
                { "refund", refund },
                { "by", request.PlayerId },
            });

            result.Accepted = true;
            result.Refund = refund;
            result.ScripAfter = State.Team.Scrip;

            return Finish(result);
        }

        // ---- helpers ---------------------------------------------------------------------------

        /// <summary>
        /// R-21 / R-24 — the one exit for every refused purchase, so the result and the event can
        /// never name different reasons. Deliberately writes no state: G-014 and G-015 pin
        /// `state_changes: []`, and a refusal that mutates is the bug a green result assertion
        /// hides.
        /// </summary>
        private PurchaseResult RefusePurchase(PurchaseRequest request, string reason)
        {
            Emit("purchase_rejected", new Dictionary<string, object>
            {
                { "reason", reason },
                { "by", request.PlayerId },
            });

            return Finish(new PurchaseResult
            {
                Accepted = false,

                // Echoed even on a refusal so the shell knows which ghost to mark (R-24), and
                // pinned that way by G-014 and G-015.
                PlaceableType = request.PlaceableType,
                ScripAfter = State.Team.Scrip,
                RejectionReason = reason,
            });
        }

        /// <summary>
        /// R-24 — "anywhere on colony ground except inside hotspot buildings, on entry tunnel
        /// mouths, or overlapping other placeables". The three exclusions in that order.
        ///
        /// Hotspot buildings are read from <see cref="MatchState.Hotspots"/> rather than from the
        /// map: the live entity is what actually stands in this match, and an emptied shelter (R-12)
        /// is still a building. Tunnel mouths are read from <see cref="ColonyMap"/>, which is the
        /// only place they exist at all.
        /// </summary>
        private bool IsPlaceableGround(Vec2 pos)
        {
            foreach (var hotspot in State.Hotspots.Values)
            {
                if (pos.DistanceTo(hotspot.Pos) < HotspotBuildingRadius)
                {
                    return false;
                }
            }

            foreach (var tunnel in ColonyMap.EntryTunnels)
            {
                if (pos.DistanceTo(tunnel) < EntryTunnelMouthRadius)
                {
                    return false;
                }
            }

            // Only placeables that are *there* block ground. A sold defence is gone (R-22), so its
            // tile goes back to being colony ground — otherwise every sale over a ten-wave match
            // leaves an invisible dead zone and the buildable area shrinks monotonically.
            var clearance = PlaceableFootprintRadius * 2.0;
            foreach (var placeable in State.Placeables.Values)
            {
                if (placeable.Exists && pos.DistanceTo(placeable.Pos) < clearance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// R-22 / DEC-011 — `floor(cost * SimConfig.SellRefundRatio)`, off the price the entity
        /// records rather than off the catalog's current row: a placeable bought before a rebalance
        /// refunds half of what the team actually paid for it.
        ///
        /// No epsilon guard, unlike <see cref="MatchSim.FloorEpsilon"/>'s callers: the shipped ratio
        /// is 0.5, and halving an integer is exact in binary. A ratio retuned off a power of two
        /// would need one.
        /// </summary>
        private int RefundFor(Placeable placeable) =>
            (int)Math.Floor(placeable.PurchaseCost * _config.SellRefundRatio);

        /// <summary>
        /// A fresh id for a purchased placeable. Ids are the host's to mint (R-51) — a client-chosen
        /// id could collide with, or impersonate, one already on the map. The counter is per match
        /// and the loop makes it collision-proof against ids a fixture or the shell authored.
        /// </summary>
        private string NextPlaceableId()
        {
            while (true)
            {
                _placeablesPurchased++;
                var id = PurchasedPlaceableIdPrefix + _placeablesPurchased;
                if (!State.Placeables.ContainsKey(id))
                {
                    return id;
                }
            }
        }
    }
}
