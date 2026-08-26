using System;
using RedHollow.Game.Input;
using UnityEngine;
using UnityEngine.EventSystems;

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
    ///    device-backed <see cref="IInputSource"/> for the local hero (R-30), and ensure an
    ///    <c>EventSystem</c> exists for uGUI clicks (creating one only if the scene has none);
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
        /// <summary>
        /// The identity a launched (offline, loopback) session plays as. Presentation-side naming
        /// only: the sim addresses heroes by the ids the session assigns, and nothing in the PRD
        /// names the local account, so these are the entry's to pick.
        /// </summary>
        private const string LocalPeerId = "peer_local";
        private const string LocalAccountId = "acc_local";

        private ShellBootstrap _shell;
        private Func<double> _deltaSource = ReadFrameDelta;

        /// <summary>The shell this entry constructed on Awake. Readable, never assignable.</summary>
        public ShellBootstrap Shell => _shell;

        /// <summary>
        /// The clock seam: what one frame's delta is. Defaults to reading
        /// <see cref="Time.deltaTime"/>; EditMode tests substitute a scripted clock so the pump
        /// cadence is observable without entering play mode. Null restores the default rather than
        /// arming a NullReferenceException sixty times a second.
        /// </summary>
        public Func<double> DeltaSource
        {
            get => _deltaSource;
            set => _deltaSource = value ?? ReadFrameDelta;
        }

        private void Awake()
        {
            // Loopback by default — every option null except identity and the device seam, which
            // only a scene entry can own (ShellBootstrap deliberately has no device default).
            _shell = new ShellBootstrap(new ShellBootstrapOptions
            {
                LocalPeerId = LocalPeerId,
                LocalAccountId = LocalAccountId,
                InputSource = new LegacyDeviceInputSource(null),
            });

            EnsureEventSystem();
        }

        private void Update()
        {
            if (_shell == null)
            {
                return;
            }

            _shell.Pump(_deltaSource());
        }

        private void OnDestroy()
        {
            var shell = _shell;
            _shell = null;

            if (shell != null)
            {
                shell.TearDown();
            }
        }

        /// <summary>The default <see cref="DeltaSource"/> — the engine's own frame delta.</summary>
        private static double ReadFrameDelta()
        {
            return Time.deltaTime;
        }

        /// <summary>
        /// R-60 — uGUI clicks need exactly one <see cref="EventSystem"/>: present-or-created,
        /// never doubled (two fight over focus and Unity logs errors about it).
        /// </summary>
        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject(
                "RedHollow_EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(null, false);
        }
    }
}
