using System;
using UnityEngine;

namespace RedHollow.Game.UI
{
    /// <summary>
    /// Ticket 022 (T-22) — the scene-resident entry point: the one MonoBehaviour serialized into
    /// <c>Assets/Scenes/RedHollow.unity</c>, so that pressing Play actually boots the shell that
    /// ticket 021 built but nothing ever constructed.
    ///
    /// The contract (pinned by T22_PlayEntryTests) is <see cref="RedHollow.Game.Host.MatchHostBehaviour"/>'s
    /// thin-pump shape, applied to the shell:
    ///
    ///  * <b>Awake</b> — construct one <see cref="ShellBootstrap"/> with the offline defaults
    ///    (loopback transport, no UGS id — pressing Play must work with no network) and a
    ///    device-backed <see cref="RedHollow.Game.Input.IInputSource"/> for the local hero
    ///    (R-30), and ensure an <c>EventSystem</c> exists for uGUI clicks (creating one only if
    ///    the scene has none);
    ///  * <b>Update</b> — sample <see cref="DeltaSource"/> exactly once and forward it to exactly
    ///    one <see cref="ShellBootstrap.Pump"/>;
    ///  * <b>OnDestroy</b> — <see cref="ShellBootstrap.TearDown"/>, idempotently.
    ///
    /// It holds no rule and computes nothing — T10's Cecil scan covers it mechanically, and the
    /// shape guard in T22 keeps it near MatchHostBehaviour's member count.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameEntryBehaviour : MonoBehaviour
    {
        /// <summary>The shell this entry constructed on Awake. Readable, never assignable.</summary>
        public ShellBootstrap Shell =>
            throw new NotImplementedException("T-22: Awake constructs the shell this exposes");

        /// <summary>
        /// The clock seam: what one frame's delta is. Defaults to reading
        /// <see cref="Time.deltaTime"/>; EditMode tests substitute a scripted clock so the pump
        /// cadence is observable without entering play mode.
        /// </summary>
        public Func<double> DeltaSource
        {
            get => throw new NotImplementedException("T-22: default is () => Time.deltaTime");
            set => throw new NotImplementedException("T-22: tests script the frame clock here");
        }

        private void Awake()
        {
            throw new NotImplementedException(
                "T-22: build the loopback ShellBootstrap with a device input source and ensure an EventSystem");
        }

        private void Update()
        {
            throw new NotImplementedException("T-22: one Update is one Pump(DeltaSource())");
        }

        private void OnDestroy()
        {
            throw new NotImplementedException("T-22: TearDown the shell, idempotently");
        }
    }
}
