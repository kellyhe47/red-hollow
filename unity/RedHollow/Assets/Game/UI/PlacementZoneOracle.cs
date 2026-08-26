using System;
using RedHollow.Sim;

namespace RedHollow.Game.UI
{
    /// <summary>
    /// Ticket 024 (T-24) — the client-side R-24 zone answer for the red-tint UX: "would the sim
    /// accept a placement here?", asked of live <see cref="MatchState"/> before a command is spent
    /// on a verdict the shell can already see coming.
    ///
    /// ADVISORY ONLY. The sim's <c>MatchSim.PurchasePlacement</c> stays authoritative and reaches
    /// its own verdict on everything actually sent (R-51 — <c>PurchaseRequest.ZoneValid</c> is
    /// deliberately never read there). This class is the <c>ClientPrediction</c> precedent applied
    /// to placement: a client-side mirror of a sim rule, property-tested against the real sim so
    /// drift fails loudly rather than tinting ghosts wrong.
    ///
    /// R-24's three exclusions, mirrored: inside hotspot buildings (live
    /// <see cref="MatchState.Hotspots"/> — an emptied shelter is still a building), on entry
    /// tunnel mouths (the <see cref="ColonyMap"/> is the only place they exist), and overlapping
    /// placeables that still stand (<see cref="Placeable.Exists"/> — a sold tile is ground again).
    ///
    /// The radii are the sim's placement-rule tunables (they live per <c>MatchSim</c> instance,
    /// not in <see cref="SimConfig"/>): defaults here MUST mirror the sim's shipped defaults, and
    /// a shell wiring this oracle to a live match must copy the match sim's actual values rather
    /// than trust the defaults — retuned radii move both sides together or the tint lies.
    /// </summary>
    public sealed class PlacementZoneOracle
    {
        /// <param name="map">
        /// The colony layout the match is played on — the tunnel mouths exist nowhere else.
        /// </param>
        public PlacementZoneOracle(ColonyMap map)
        {
            throw new NotImplementedException("T-24: placement zone oracle not implemented yet.");
        }

        /// <summary>Mirror of <c>MatchSim.HotspotBuildingRadius</c> (same shipped default).</summary>
        public double HotspotBuildingRadius { get; set; }

        /// <summary>Mirror of <c>MatchSim.EntryTunnelMouthRadius</c> (same shipped default).</summary>
        public double EntryTunnelMouthRadius { get; set; }

        /// <summary>Mirror of <c>MatchSim.PlaceableFootprintRadius</c> (same shipped default).</summary>
        public double PlaceableFootprintRadius { get; set; }

        /// <summary>
        /// Would the sim's R-24 zone gate accept a placement at <paramref name="pos"/> given the
        /// live <paramref name="state"/>? Zone geometry ONLY — phase, catalog and scrip are the
        /// sim's other gates and not this question.
        /// </summary>
        public bool WouldAccept(MatchState state, Vec2 pos)
        {
            throw new NotImplementedException("T-24: placement zone oracle not implemented yet.");
        }
    }
}
