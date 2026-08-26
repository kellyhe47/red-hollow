using RedHollow.Game.Art;
using RedHollow.Game.Input;
using RedHollow.Game.Net;
using RedHollow.Game.View;
using RedHollow.Sim;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RedHollow.Game.UI
{
    /// <summary>
    /// Ticket 030 — the LAN/loopback party entry (R-50: "up to 2 players" this stretch, no cloud
    /// project required): ONE component that replaces <see cref="GameEntryBehaviour"/> in a
    /// networked scene and composes either side of an NGO direct-connection party.
    ///
    ///  * <b>Host</b> (default): the full shell over <see cref="NgoNetTransport"/> +
    ///    <see cref="LanServices"/>. HOST GAME on S1 brings the wire up on the configured port;
    ///    the moment it listens, the replication channel attaches and every snapshot/command
    ///    flows through the seams T30 pinned. A knocking client's hello is forwarded to
    ///    <see cref="NetSession.TryJoin"/> — the session stays the only admission judge (R-53) —
    ///    and a refusal kicks the connection.
    ///  * <b>Client</b> (tick <see cref="joinAsClient"/>): dials the join code
    ///    (<c>LAN</c> = same machine; <c>LAN:address:port</c> = across the room), then renders
    ///    the mirror through the same scene builder and view binder the host uses and plays
    ///    through <see cref="ClientMatchPresenter"/> — WASD/SPACE/Q/E over the wire, resolved
    ///    host-side. The v1 client auto-readies each planning phase (its planning UI is the
    ///    host's shop for now — R-25 makes any player's placements the team's).
    ///
    /// Composition only (T-10): every decision lives in the plain-C# pieces the headless suite
    /// executes; this component builds them, pumps them, and tears them down. Like
    /// <see cref="RedHollow.Game.Net.NgoWire"/>, the NGO plumbing itself is hand-verified — this
    /// is the file the owner's two-process playtest exercises.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LanPartyBehaviour : MonoBehaviour
    {
        [SerializeField] private bool joinAsClient;
        [SerializeField] private string joinCode = LanServices.CodePrefix;
        [SerializeField] private string callsign = "drifter";
        [SerializeField] private ushort hostPort = LanServices.DefaultPort;

        private const string HostPeerId = "peer_lan_host";
        private const string ClientPeerId = "peer_lan_client";

        private NgoWire _wire;
        private NgoNetTransport _transport;
        private GameObject _ngoRoot;
        private MatchScene _scene;

        // host side
        private ShellBootstrap _shell;
        private bool _channelAttached;

        // client side
        private ClientMatchPresenter _presenter;
        private MatchViewBinder _binder;
        private bool _clientReadiedThisPlanning;
        private string _clientPhaseSeen;

        /// <summary>Host-side shell (null on the client). For the LAN bring-up and EditMode probes.</summary>
        public ShellBootstrap Shell => _shell;

        /// <summary>The NGO transport this party composed. Null until Awake.</summary>
        public NgoNetTransport Transport => _transport;

        /// <summary>R-07 — the join code the host advertises once the wire is up (LAN / LAN:ip:port).</summary>
        public string JoinCode => _transport == null ? null : _transport.JoinCode;

        private void Awake()
        {
            _ngoRoot = new GameObject("RedHollow_NGO");
            DontDestroyOnLoad(_ngoRoot);
            // Transport first, then assign onto the NetworkConfig Awake already created.
            // Replacing NetworkConfig wholesale is what left ConnectionManager.NetworkManager
            // unset and threw "There is no NetworkManager assigned to this instance!".
            var transport = _ngoRoot.AddComponent<UnityTransport>();
            var networkManager = _ngoRoot.AddComponent<NetworkManager>();
            if (networkManager.NetworkConfig == null)
            {
                networkManager.NetworkConfig = new NetworkConfig();
            }

            networkManager.NetworkConfig.NetworkTransport = transport;
            if (NetworkManager.Singleton != networkManager)
            {
                networkManager.SetSingleton();
            }

            _wire = new NgoWire(networkManager);
            _transport = new NgoNetTransport(new LanServices(port: hostPort), _wire);

            EnsureEventSystem();

            if (joinAsClient)
            {
                AwakeAsClient();
            }
            else
            {
                AwakeAsHost();
            }
        }

        private void AwakeAsHost()
        {
            _wire.SetLocalPeerId(HostPeerId);

            _shell = new ShellBootstrap(new ShellBootstrapOptions
            {
                Transport = _transport,
                LocalPeerId = HostPeerId,
                LocalAccountId = callsign,
                InputSource = new OverlayInputSource(new LegacyDeviceInputSource(null)),
                Profiles = new JsonProfileStore(
                    System.IO.Path.Combine(Application.persistentDataPath, "redhollow-profiles.json")),
            });

            // The door: a knocking client's hello goes to the session, the one admission judge
            // (R-50 cap, R-53 no-mid-match-joins), and a refusal drops the connection.
            _wire.PeerHello += (peerId, accountId, heroClass) =>
            {
                var admitted = _shell.Session.TryJoin(new NetPeer
                {
                    PeerId = peerId,
                    AccountId = accountId,
                    HeroClass = string.IsNullOrEmpty(heroClass) ? HeroClass.Gunslinger : heroClass,
                });

                if (!admitted)
                {
                    _wire.Kick(peerId);
                }
            };

            // R-53 — a dropped wire is a session disconnect AND the end of that peer's held input.
            _transport.PeerDisconnected += peerId =>
            {
                _shell.Session.Disconnect(peerId);
                _shell.DropRemotePeer(peerId);
            };

            BuildColonyScene(_shell.Visuals);
            _shell.AttachScene(_scene);
        }

        private void AwakeAsClient()
        {
            _wire.SetLocalIdentity(ClientPeerId, callsign, HeroClass.Gunslinger);

            if (!_transport.TryJoinAsClient(new NetSessionConfig(), joinCode))
            {
                Debug.LogWarning("LAN join refused: '" + joinCode + "' did not dial");
                return;
            }

            _presenter = new ClientMatchPresenter(
                _wire.CreateClientMatchChannel(),
                new OverlayInputSource(new LegacyDeviceInputSource(null)));

            var catalog = ShellBootstrap.LoadRepresentativeArt();
            var visuals = new ArtVisualResolver(catalog, new PlaceholderVisualResolver());
            _binder = new MatchViewBinder(visuals)
            {
                PlaceableCatalog = new SimConfig().Placeables,
            };

            BuildColonyScene(visuals);
        }

        private void Update()
        {
            if (joinAsClient)
            {
                UpdateAsClient();
            }
            else
            {
                UpdateAsHost();
            }
        }

        private void UpdateAsHost()
        {
            if (_shell == null)
            {
                return;
            }

            // The messaging manager exists only once the wire listens (HOST GAME did that), so
            // the channel attaches on the first pump after bring-up.
            if (!_channelAttached && _transport.IsRunning)
            {
                _shell.AttachHostChannel(_wire.CreateHostMatchChannel());
                _channelAttached = true;
            }

            _transport.Tick(Time.deltaTime);
            _shell.Pump(Time.deltaTime);
        }

        private void UpdateAsClient()
        {
            if (_presenter == null)
            {
                return;
            }

            _presenter.Pump(Time.deltaTime);
            _binder.Sync(_presenter.Mirror);

            // v1 — the client auto-readies once per planning phase: its planning UI is the host's
            // shop for now, and a phase nobody can end is a hung party (R-03's early exit needs
            // every CONNECTED player's ready).
            var phase = _presenter.Mirror.Phase;
            if (!string.Equals(phase, _clientPhaseSeen, System.StringComparison.Ordinal))
            {
                _clientPhaseSeen = phase;
                _clientReadiedThisPlanning = false;
            }

            if (_presenter.Live
                && phase == MatchPhase.Planning
                && !_clientReadiedThisPlanning)
            {
                _presenter.ReadyUp();
                _clientReadiedThisPlanning = true;
            }
        }

        private void OnDestroy()
        {
            var shell = _shell;
            _shell = null;
            if (shell != null)
            {
                shell.TearDown();
            }

            if (_transport != null)
            {
                _transport.Shutdown();
            }

            if (_scene != null && _scene.Root != null)
            {
                Destroy(_scene.Root);
            }

            if (_ngoRoot != null)
            {
                Destroy(_ngoRoot);
            }
        }

        /// <summary>The same colony composition GameEntry does, over whichever visuals this side built.</summary>
        private void BuildColonyScene(IVisualResolver visuals)
        {
            var baked = GameObject.Find("RedHollow_Match");
            if (baked != null)
            {
                foreach (var cam in baked.GetComponentsInChildren<Camera>(true))
                {
                    cam.enabled = false;
                    cam.gameObject.SetActive(false);
                }

                Destroy(baked);
            }

            _scene = MatchSceneBuilder.Build(ColonyMap.V1(), visuals);
            if (Application.isPlaying)
            {
                LanternDeepLighting.Apply(_scene);
            }
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject(
                "RedHollow_EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(null, false);
        }
    }
}
