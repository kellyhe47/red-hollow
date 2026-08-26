using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Ticket 026 (T-26) — the observable state of one entry-tunnel marker (wireframe S3 "ACTIVE
    /// entry points pulse red"; S4 "monster spawn → entry point flare"). The marker's state is a
    /// mirror of the models — <see cref="RedHollow.Game.UI.PlanningScreenModel.PulsingEntryTunnels"/>
    /// during planning and <see cref="RedHollow.Game.UI.CombatHudModel.EntryFlares"/> in combat —
    /// refreshed by the shell pump. The pulse/flare animation itself is presentation; the state
    /// presence is the contract.
    /// </summary>
    public sealed class EntryTunnelMarkerView : MonoBehaviour
    {
        /// <summary>The index into <see cref="RedHollow.Sim.ColonyMap.EntryTunnels"/> this marker stands on.</summary>
        public int TunnelIndex { get; private set; }

        /// <summary>S3 — true while the planning preview names this tunnel as activating.</summary>
        public bool Pulsing { get; private set; }

        /// <summary>S4 — true while the HUD's entry flare names this tunnel.</summary>
        public bool Flaring { get; private set; }

        /// <summary>Ties this marker to the tunnel index the models speak in. Builder-time only.</summary>
        public void Bind(int tunnelIndex)
        {
            TunnelIndex = tunnelIndex;
        }

        /// <summary>
        /// The shell pump's write: both states in one call because they are one decision — the
        /// models drive them, this component only remembers what it was told (a mirror, T-10).
        /// </summary>
        public void SetStates(bool pulsing, bool flaring)
        {
            Pulsing = pulsing;
            Flaring = flaring;
        }
    }

    /// <summary>
    /// Ticket 026 (T-26) — the observable state of one hotspot marker (wireframe S4 "hotspot
    /// emptied → building marked dark/lost"). Lost mirrors the sim's answer (Civilians == 0);
    /// the darkening itself is presentation.
    /// </summary>
    public sealed class HotspotMarkerView : MonoBehaviour
    {
        /// <summary>The sim's own hotspot id this marker stands for.</summary>
        public string HotspotId { get; private set; }

        /// <summary>R-12/R-13 — true exactly when the shelter it stands for is emptied.</summary>
        public bool Lost { get; private set; }

        /// <summary>Ties this marker to the sim's own hotspot id. Builder-time only.</summary>
        public void Bind(string hotspotId)
        {
            HotspotId = hotspotId;
        }

        /// <summary>The shell pump's write — the sim's emptied answer, mirrored, never decided here.</summary>
        public void SetLost(bool lost)
        {
            Lost = lost;
        }
    }
}
