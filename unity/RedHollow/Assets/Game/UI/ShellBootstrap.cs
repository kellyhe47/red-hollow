using System;
using System.Collections.Generic;
using System.Globalization;
using RedHollow.Game.Art;
using RedHollow.Game.Host;
using RedHollow.Game.Input;
using RedHollow.Game.Net;
using RedHollow.Game.View;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Game.UI
{
    /// <summary>
    /// Ticket 021 (T-21) — everything the shell composition root is assembled from. Every field is
    /// optional-with-a-default except the local identity pair: the models are addressed by account
    /// (<see cref="CombatHudModel"/>) and by peer (<see cref="LobbyScreenModel"/>,
    /// <see cref="PostMatchModel"/>), so a bootstrap that guessed either would wire somebody else's
    /// HUD.
    /// </summary>
    public sealed class ShellBootstrapOptions
    {
        /// <summary>R-50 — null means the offline defaults (loopback, R-50's party cap).</summary>
        public NetSessionConfig NetConfig;

        /// <summary>R-50 — null means <see cref="LoopbackNetTransport"/>.</summary>
        public INetTransport Transport;

        /// <summary>R-43 — null means an in-memory store (XP dies with the process).</summary>
        public IProfileStore Profiles;

        /// <summary>R-10 — null means the shipped <see cref="ColonyMap"/> V1.</summary>
        public ColonyMap Map;

        /// <summary>Tunables. Null means the shipped defaults.</summary>
        public SimConfig SimConfig;

        /// <summary>The peer this shell fronts — <see cref="LobbyScreenModel"/> et al. act as it.</summary>
        public string LocalPeerId;

        /// <summary>R-43/R-44 — the account this shell's HUD reads progression for.</summary>
        public string LocalAccountId;

        /// <summary>
        /// The artKey→asset table. Null means <see cref="ShellBootstrap.LoadRepresentativeArt"/> —
        /// a launched shell resolves real art by default rather than opting into it.
        /// </summary>
        public ArtCatalog ArtCatalog;

        /// <summary>
        /// Ticket 022 (T-22) — R-30: where the local player's raw input comes from. The shell
        /// resolves each sampled <see cref="InputSnapshot"/> through the shipped
        /// <see cref="DefaultHeroInputMap"/> and feeds the resulting intent to the LOCAL hero
        /// (the one whose <c>AccountId</c> is <see cref="LocalAccountId"/>) of whatever match the
        /// session holds — the <see cref="RedHollow.Game.Host.IHeroIntentSource"/> hole that
        /// ticket 021 left null. Null means no local input (a headless shell / most tests).
        /// </summary>
        public IInputSource InputSource;
    }

    /// <summary>
    /// Ticket 021 (T-21) — the art keys the shell registers, declared once so the registration and
    /// every consumer agree on the spelling.
    ///
    /// <b>The character key IS the hero-class literal.</b> <see cref="MatchViewBinder"/> resolves a
    /// hero's visual with <c>artKey = hero.HeroClass</c> ("gunslinger"), so the representative
    /// character asset must be registered under exactly that key or the binder can never find it —
    /// a catalog keyed "characters/gunslinger-portrait" would be full of art no view ever wears.
    /// The other three keys follow the art/ directory spelling (class/subject-slug, no version, no
    /// size: versions and sizes are delivery facts, not identities).
    /// </summary>
    public static class ShellArtKeys
    {
        /// <summary>art/textures/cavern-ground → the environment tile representative.</summary>
        public const string GroundTile = "textures/cavern-ground";

        /// <summary>The character representative — keyed by the class literal the binder resolves with.</summary>
        public const string GunslingerCharacter = HeroClass.Gunslinger;

        /// <summary>art/icons/gs-revolver-shot → the icon representative.</summary>
        public const string RevolverShotIcon = "icons/gs-revolver-shot";

        /// <summary>art/ui/button-normal → the UI chrome representative.</summary>
        public const string ButtonFrame = "ui/button-normal";
    }

    /// <summary>
    /// Ticket 021 (T-21) — the shell composition root: the one place that constructs at runtime
    /// what tickets 012, 013 and 019 built and locked but nothing ever assembled. It owns:
    ///
    ///  * <b>the session</b> — a <see cref="NetSession"/> over the transport, whose match factory
    ///    is wired so every match it creates carries this shell's view binder (so waves are
    ///    visible, R-51) instead of ticket 011's headless default;
    ///  * <b>the UI</b> — a real uGUI hierarchy (<see cref="ShellUi"/>) bound to the 012 models,
    ///    with <see cref="UiRouter"/> deciding which screen root is active (R-60);
    ///  * <b>the feel feed</b> — the sim's emitted events routed into <see cref="FeelRouter"/>,
    ///    <see cref="UiRouter.OnSimEvent"/> and <see cref="CombatHudModel.OnSimEvent"/> (R-64),
    ///    with <see cref="FeelRig"/> offsets applied to live monster views each pump;
    ///  * <b>the art chain</b> — an <see cref="ArtVisualResolver"/> over the catalog, chained in
    ///    front of the <see cref="PlaceholderVisualResolver"/>, handed to the binder (R-15).
    ///
    /// Plain C#, never a MonoBehaviour (T-10's Cecil invariant): a scene component's whole job is
    /// to hold one of these and call <see cref="Pump"/> once per frame with the frame's delta —
    /// the same two-member shape as <see cref="RedHollow.Game.Host.MatchHostBehaviour"/>.
    ///
    /// <b>The pump contract.</b> One <see cref="Pump"/> is one presentation frame:
    ///
    ///  1. collect every <see cref="SimEvent"/> not yet delivered — both the events of commands the
    ///     session drives during this pump's step AND events still visible in the sim's
    ///     <c>LastObservation</c> from commands issued directly between pumps (a test calling
    ///     <c>match.Sim.ResolveHeroAttack</c>, the HUD's own <c>Spend</c>). Each event is delivered
    ///     exactly once, in emission order. (An out-of-band event survives only until a later
    ///     command overwrites the observation — collection happens before this pump steps anything,
    ///     so "call a command, then pump" always sees it.)
    ///  2. step the session by <paramref name="deltaSeconds"/> (<see cref="NetSession.Step"/> is
    ///     already a no-op outside a live match);
    ///  3. route the collected events: <see cref="UiRouter.OnSimEvent"/>,
    ///     <see cref="CombatHudModel.OnSimEvent"/>, <see cref="FeelRouter.Route"/> — and new
    ///     <see cref="SessionNotice"/>s to <see cref="CombatHudModel.OnSessionNotice"/>;
    ///  4. <see cref="FeelRouter.Tick"/> the delta;
    ///  5. refresh: <see cref="UiRouter.Update"/>, the live models' <c>Refresh()</c>, the
    ///     <see cref="ShellUi"/> labels, and the screen-root activation;
    ///  6. apply presentation feel: for every bound monster view,
    ///     <see cref="FeelRig.Apply(MonsterView, EntityFeelState)"/> on top of the authoritative
    ///     position the binder's sync just wrote. The nudge lands on the TRANSFORM only — sim
    ///     positions are never written (T-10).
    ///
    /// <c>Pump(0.0)</c> is a pure refresh: it advances no clock and moves nothing, but still
    /// collects and routes pending events and re-reads all state onto the labels — which is what
    /// makes the whole chain assertable from EditMode without a frame elapsing (T-19's pattern).
    /// </summary>
    public sealed class ShellBootstrap
    {
        private readonly NetSession _session;
        private readonly UiRouter _router;
        private readonly ShellUi _ui;
        private readonly FeelRouter _feel = new FeelRouter();
        private readonly MatchViewBinder _views;
        private readonly IVisualResolver _visuals;
        private readonly ArtCatalog _catalog;
        private readonly IProfileStore _profiles;
        private readonly INetTransport _transport;
        private readonly string _accountId;

        /// <summary>The event tap each match this shell created carries (see the factory).</summary>
        private readonly Dictionary<HostedMatch, SimEventTap> _taps =
            new Dictionary<HostedMatch, SimEventTap>();

        /// <summary>Scratch list the pump routes each frame's events out of.</summary>
        private readonly List<SimEvent> _routing = new List<SimEvent>();

        private HostedMatch _boundMatch;
        private SimEventTap _tap;
        private CombatHudModel _hud;
        private int _noticesDelivered;
        private bool _tornDown;

        public ShellBootstrap(ShellBootstrapOptions options = null)
        {
            options = options ?? new ShellBootstrapOptions();

            _transport = options.Transport ?? new LoopbackNetTransport();
            _profiles = options.Profiles ?? new InMemoryProfileStore();
            _accountId = options.LocalAccountId;

            // R-15 — real art by default; the placeholder answers for everything unregistered.
            _catalog = options.ArtCatalog ?? LoadRepresentativeArt();
            _visuals = new ArtVisualResolver(_catalog, new PlaceholderVisualResolver());

            // R-51 — one binder for the shell's lifetime; every match's session reuses it.
            _views = new MatchViewBinder(_visuals);

            var factory = new ColonyMatchFactory(options.Map, options.SimConfig, _profiles);
            _session = new NetSession(
                options.NetConfig, _transport, new ViewBoundMatchFactory(this, factory));

            _router = new UiRouter(_session);
            _ui = ShellUi.Build();

            // The shell shows a screen from birth (S1 before anything is hosted).
            RefreshPresentation();
        }

        /// <summary>The session this shell fronts. Hosting/joining goes through it directly.</summary>
        public NetSession Session => _session;

        /// <summary>R-60 — the screen router the UI activation follows.</summary>
        public UiRouter Router => _router;

        /// <summary>The built uGUI hierarchy.</summary>
        public ShellUi Ui => _ui;

        /// <summary>R-64 — the feel router every replicated event is offered to.</summary>
        public FeelRouter Feel => _feel;

        /// <summary>
        /// R-51 — the view binder every match this shell starts is bound with (one binder for the
        /// shell's lifetime; its reconciliation follows whatever match is live).
        /// </summary>
        public MatchViewBinder Views => _views;

        /// <summary>
        /// R-15 — the asset seam the binder resolves through: an <see cref="ArtVisualResolver"/>
        /// over <see cref="Art"/>, chained in front of the placeholder. Total for any input.
        /// </summary>
        public IVisualResolver Visuals => _visuals;

        /// <summary>The artKey→asset table behind <see cref="Visuals"/>.</summary>
        public ArtCatalog Art => _catalog;

        /// <summary>
        /// Ticket 022 (T-22) — the input source this shell samples for the local hero, exactly as
        /// supplied via <see cref="ShellBootstrapOptions.InputSource"/> (null when none was: input
        /// is the one option with no offline default, because only a scene entry owns a device).
        /// </summary>
        public IInputSource Input =>
            throw new NotImplementedException("T-22: expose and wire ShellBootstrapOptions.InputSource");

        /// <summary>
        /// R-61 — the combat HUD model for the local account. Null until a match is live; rebuilt
        /// for each new match (a rematch is a different <see cref="HostedMatch"/>, R-07).
        /// </summary>
        public CombatHudModel Hud
        {
            get
            {
                BindToCurrentMatch();
                return _hud;
            }
        }

        /// <summary>One presentation frame. See the class doc for the six-step contract.</summary>
        public void Pump(double deltaSeconds)
        {
            BindToCurrentMatch();

            // 1 — collect BEFORE stepping: events of commands issued directly between pumps are
            // still sitting in LastObservation until the step's first command overwrites it.
            if (_tap != null)
            {
                _tap.Drain();
            }

            // 2 — the step. Every command the session drives passes through the tap, so its
            // events land in the pending list as they happen.
            _session.Step(deltaSeconds);

            // 3 — route, exactly once, in emission order.
            _routing.Clear();
            if (_tap != null)
            {
                _tap.TakePendingInto(_routing);
            }

            for (var i = 0; i < _routing.Count; i++)
            {
                var evt = _routing[i];
                _router.OnSimEvent(evt);
                if (_hud != null)
                {
                    _hud.OnSimEvent(evt);
                }

                _feel.Route(evt);
            }

            var notices = _session.Notices;
            while (_noticesDelivered < notices.Count)
            {
                if (_hud != null)
                {
                    _hud.OnSessionNotice(notices[_noticesDelivered]);
                }

                _noticesDelivered++;
            }

            // 4 — feel time advances by the frame's delta (a zero delta decays nothing).
            _feel.Tick(deltaSeconds);

            // 5 — refresh the router, the models, the labels and the screen activation.
            RefreshPresentation();

            // 6 — presentation feel on top of the authoritative sync (transform only, T-10).
            ApplyFeel();
        }

        /// <summary>
        /// Destroy every GameObject this shell created (the UI under "RedHollow_Shell", the
        /// binder's views) and release the transport. Safe to call twice.
        /// </summary>
        public void TearDown()
        {
            if (_tornDown)
            {
                return;
            }

            _tornDown = true;

            if (_ui != null)
            {
                DestroyGameObject(_ui.Root);
            }

            if (_views != null)
            {
                DestroyGameObject(_views.Root);
            }

            if (_transport != null)
            {
                _transport.Shutdown();
            }
        }

        /// <summary>
        /// R-15 — the default catalog: the four imported representative assets
        /// (Assets/Game/Art/{Textures,Characters,Icons,UI}/, the exact files T13's seam tests pin)
        /// registered under the <see cref="ShellArtKeys"/> spellings, each with a factory that
        /// instantiates a renderable GameObject carrying that asset. Loaded through Resources
        /// copies (Assets/Game/UI/Resources/RedHollowArt/) — a mechanism that works in EditMode
        /// AND a build, never AssetDatabase; T13's imported originals stay untouched where its
        /// locked tests read them.
        /// </summary>
        public static ArtCatalog LoadRepresentativeArt()
        {
            var catalog = new ArtCatalog();

            RegisterResourceArt(catalog, ShellArtKeys.GroundTile, "RedHollowArt/cavern-ground");
            RegisterResourceArt(catalog, ShellArtKeys.GunslingerCharacter, "RedHollowArt/gunslinger");
            RegisterResourceArt(catalog, ShellArtKeys.RevolverShotIcon, "RedHollowArt/gs-revolver-shot");
            RegisterResourceArt(catalog, ShellArtKeys.ButtonFrame, "RedHollowArt/button-normal");

            return catalog;
        }

        // ---- match binding --------------------------------------------------------------------

        /// <summary>
        /// Follow whatever match the session holds: a new match gets a fresh HUD model (R-07 — a
        /// rematch is a different <see cref="HostedMatch"/>) and this shell's tap for it; no match
        /// means no HUD and no tap.
        /// </summary>
        private void BindToCurrentMatch()
        {
            var match = _session.Match;
            if (ReferenceEquals(match, _boundMatch))
            {
                return;
            }

            _boundMatch = match;

            if (match == null)
            {
                _tap = null;
                _hud = null;
                return;
            }

            _taps.TryGetValue(match, out _tap);
            _hud = new CombatHudModel(match, _accountId, _profiles);
        }

        // ---- presentation refresh -------------------------------------------------------------

        private void RefreshPresentation()
        {
            _router.Update();

            if (_hud != null)
            {
                _hud.Refresh();
            }

            RefreshLabels();
            _ui.SetActiveScreen(_router.Screen);
        }

        /// <summary>
        /// R-61 — push the model's values onto the labels. Copy and format are presentation; the
        /// contract is only that each model value appears on its label.
        /// </summary>
        private void RefreshLabels()
        {
            if (_hud == null)
            {
                _ui.WaveLabel.text = string.Empty;
                _ui.ScripLabel.text = string.Empty;
                _ui.HpLabel.text = string.Empty;
                _ui.MonstersRemainingLabel.text = string.Empty;
                _ui.EnsureHotspotLabels(0);
                return;
            }

            _ui.WaveLabel.text = "Wave "
                + _hud.WaveNumber.ToString(CultureInfo.InvariantCulture)
                + "/" + _hud.TotalWaves.ToString(CultureInfo.InvariantCulture);
            _ui.ScripLabel.text = _hud.Scrip.ToString(CultureInfo.InvariantCulture);
            _ui.HpLabel.text = ((int)_hud.Hp).ToString(CultureInfo.InvariantCulture);
            _ui.MonstersRemainingLabel.text =
                _hud.MonstersRemaining.ToString(CultureInfo.InvariantCulture);

            var hotspots = _hud.Hotspots;
            _ui.EnsureHotspotLabels(hotspots.Count);
            for (var i = 0; i < hotspots.Count; i++)
            {
                _ui.HotspotLabelList[i].text = hotspots[i].HotspotId + ": "
                    + hotspots[i].Civilians.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// R-64 — <see cref="FeelRig.Apply"/> per bound monster view, on top of the position the
        /// binder's sync wrote. The nudge lands on the TRANSFORM only; sim state is never written.
        /// </summary>
        private void ApplyFeel()
        {
            foreach (var monsterId in _views.BoundMonsterIds)
            {
                FeelRig.Apply(_views.MonsterViewFor(monsterId), _feel.FeelFor(monsterId));
            }
        }

        // ---- plumbing -------------------------------------------------------------------------

        /// <summary>
        /// One representative asset entry: load the Resources copy, stand it up as a sprite. A
        /// missing resource returns null, which the catalog answers with the resolver's fallback —
        /// the seam stays total (R-30's delivery constraint).
        /// </summary>
        private static void RegisterResourceArt(ArtCatalog catalog, string artKey, string resourcePath)
        {
            Sprite sprite = null;

            catalog.Register(artKey, () =>
            {
                if (sprite == null)
                {
                    var texture = Resources.Load<Texture2D>(resourcePath);
                    if (texture == null)
                    {
                        return null;
                    }

                    sprite = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                }

                var go = new GameObject("art_" + artKey.Replace('/', '_'));
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                return go;
            });
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

        /// <summary>
        /// R-51 — the factory wrap that makes every match this shell's session creates view-bound:
        /// the inner factory builds the match (all of ticket 011's rules), then the host seam is
        /// decorated with the event tap and the session is rebuilt over it with the shell's one
        /// binder. Rule-free — nothing here touches state the inner factory made.
        /// </summary>
        private sealed class ViewBoundMatchFactory : IHostedMatchFactory
        {
            private readonly ShellBootstrap _shell;
            private readonly IHostedMatchFactory _inner;

            public ViewBoundMatchFactory(ShellBootstrap shell, IHostedMatchFactory inner)
            {
                _shell = shell;
                _inner = inner;
            }

            public HostedMatch CreateMatch(IReadOnlyList<NetPeer> party)
            {
                var match = _inner.CreateMatch(party);
                if (match == null)
                {
                    return null;
                }

                var tap = new SimEventTap(match.Host);
                match.Host = tap;
                match.Session = new MatchSession(tap, null, _shell._views);

                _shell._taps[match] = tap;
                return match;
            }
        }
    }
}
