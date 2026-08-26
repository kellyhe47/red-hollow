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

        /// <summary>
        /// Ticket 025 (T-25) — the combat action tunables (attack cadence, aim-line footprint).
        /// Null means the shipped <see cref="CombatActionConfig"/> defaults.
        /// </summary>
        public CombatActionConfig CombatActions;
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
    /// Ticket 025 (T-25) — the shell's read of one Q/E cast's <see cref="AbilityCastOutcome"/>,
    /// surfaced on <see cref="ShellBootstrap.LastAbilityOutcome"/>. A presentation VIEW and not
    /// the sim type itself for one concrete reason: the sim's outcome carries public FIELDS, and
    /// the UI layer (NUnit property constraints included) reads presentation state through
    /// PROPERTIES. Every value is copied verbatim at cast time — the shell translates nothing
    /// (R-31: `ability_locked` / `ability_cooling` ride through exactly as the sim spelled them).
    /// </summary>
    public sealed class AbilityOutcomeView
    {
        public AbilityOutcomeView(AbilityCastOutcome outcome)
        {
            Accepted = outcome.Accepted;
            CasterId = outcome.CasterId;
            Slot = outcome.Slot;
            Ability = outcome.Ability;
            Rank = outcome.Rank;
            RejectionReason = outcome.RejectionReason;
            CooldownReadyAt = outcome.CooldownReadyAt;
            TotalDamage = outcome.TotalDamage;
        }

        /// <summary>R-31/R-32 — did the sim accept the cast?</summary>
        public bool Accepted { get; }

        public string CasterId { get; }

        /// <summary>The <see cref="AbilitySlot"/> that was pressed.</summary>
        public string Slot { get; }

        /// <summary>The <see cref="AbilityName"/> the caster's class binds to that slot.</summary>
        public string Ability { get; }

        public int Rank { get; }

        /// <summary>The sim's own fixture-shaped reason, untranslated. Null when accepted.</summary>
        public string RejectionReason { get; }

        public double CooldownReadyAt { get; }

        public double TotalDamage { get; }
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
        private readonly ShellUi _ui;
        private readonly FeelRouter _feel = new FeelRouter();
        private readonly MatchViewBinder _views;
        private readonly IVisualResolver _visuals;
        private readonly ArtCatalog _catalog;
        private readonly IProfileStore _profiles;
        private readonly INetTransport _transport;
        private readonly IInputSource _input;
        private readonly LocalHeroIntentSource _heroIntents;
        private readonly string _localPeerId;
        private readonly NetSessionConfig _netConfig;
        private readonly IHostedMatchFactory _matchFactory;
        private readonly TitleScreenModel _title;
        private readonly ShellControls _controls;

        /// <summary>
        /// T-23 — NOT readonly: <see cref="RequestHost"/> builds a FRESH session when hosting is
        /// requested over a dead one (see that method), and the router follows its session.
        /// </summary>
        private NetSession _session;

        private UiRouter _router;

        private LobbyScreenModel _lobby;

        /// <summary>
        /// R-43/R-44 — the account the HUD and the local-input path key off. Seeded from
        /// <see cref="ShellBootstrapOptions.LocalAccountId"/>; <see cref="RequestHost"/> re-seats
        /// it as the TYPED callsign (R-44: the callsign IS the account), so the hosted hero and
        /// the HUD agree on whose progression is on screen.
        /// </summary>
        private string _accountId;

        /// <summary>The event tap each match this shell created carries (see the factory).</summary>
        private readonly Dictionary<HostedMatch, SimEventTap> _taps =
            new Dictionary<HostedMatch, SimEventTap>();

        /// <summary>Scratch list the pump routes each frame's events out of.</summary>
        private readonly List<SimEvent> _routing = new List<SimEvent>();

        private HostedMatch _boundMatch;
        private SimEventTap _tap;
        private CombatHudModel _hud;
        private PlanningScreenModel _planning;
        private PostMatchModel _postMatch;
        private MatchStatsTracker _stats;
        private int _noticesDelivered;
        private bool _tornDown;

        /// <summary>T-24 — the client-side R-24 mirror for the bound match (null without one).</summary>
        private PlacementZoneOracle _zoneOracle;

        /// <summary>T-26 — the attached colony scene (null until the entry hands one over).</summary>
        private MatchScene _scene;

        /// <summary>
        /// T-24 — was MouseLeft held on the PREVIOUS pump? <see cref="InputSnapshot.Pressed"/> is
        /// held-this-frame, so a press EDGE (one click per press, never one per pump) needs the
        /// last frame remembered here.
        /// </summary>
        private bool _pointerMouseWasDown;

        // ---- T-25 combat action routing state ------------------------------------------------

        /// <summary>T-25 — the combat tunables this shell was composed with (never null).</summary>
        private readonly CombatActionConfig _combatActions;

        /// <summary>T-25 — was SPACE held on the PREVIOUS pump (same edge pattern as T-24's mouse)?</summary>
        private bool _attackWasDown;

        /// <summary>T-25 — was Q held on the previous pump?</summary>
        private bool _qWasDown;

        /// <summary>T-25 — was E held on the previous pump?</summary>
        private bool _eWasDown;

        /// <summary>
        /// T-25 — pumped seconds since the last basic attack fired while SPACE stays held. The
        /// press itself fires immediately (edge); this only paces the re-fire at the cadence.
        /// </summary>
        private double _attackClock;

        /// <summary>T-25 — the most recent Q/E cast's outcome (null before the first press).</summary>
        private AbilityOutcomeView _lastAbilityOutcome;

        /// <summary>Scratch list the kill reap scans the living roster through (no per-kill LINQ).</summary>
        private readonly List<string> _reapScratch = new List<string>();

        public ShellBootstrap(ShellBootstrapOptions options = null)
        {
            options = options ?? new ShellBootstrapOptions();

            _transport = options.Transport ?? new LoopbackNetTransport();
            _profiles = options.Profiles ?? new InMemoryProfileStore();
            _accountId = options.LocalAccountId;

            // T-22 / R-30 — the device seam, exactly as supplied (null = headless, no local input).
            // One intent source for the shell's lifetime: it reads whatever match is stepped, so a
            // rematch needs no rewiring.
            _input = options.InputSource;
            _heroIntents = new LocalHeroIntentSource(this);

            // T-25 — combat tunables are shell policy: composed, never constants in the routing.
            _combatActions = options.CombatActions ?? new CombatActionConfig();

            // R-15 — real art by default; the placeholder answers for everything unregistered.
            _catalog = options.ArtCatalog ?? LoadRepresentativeArt();
            _visuals = new ArtVisualResolver(_catalog, new PlaceholderVisualResolver());

            // R-51 — one binder for the shell's lifetime; every match's session reuses it.
            _views = new MatchViewBinder(_visuals);

            var factory = new ColonyMatchFactory(options.Map, options.SimConfig, _profiles);
            _matchFactory = new ViewBoundMatchFactory(this, factory);
            _netConfig = options.NetConfig;
            _localPeerId = options.LocalPeerId;
            _session = new NetSession(_netConfig, _transport, _matchFactory);

            _router = new UiRouter(_session);
            _ui = ShellUi.Build();

            // T-23 — S1's model exists from birth (the title screen does), S2's follows the
            // session, and the controls hang under the screen roots the UI just built.
            _title = new TitleScreenModel(_profiles);
            _lobby = new LobbyScreenModel(_session, _localPeerId);
            _controls = new ShellControls(this, _ui);

            // The shell shows a screen from birth (S1 before anything is hosted).
            RefreshPresentation();
        }

        /// <summary>The session this shell fronts. Hosting/joining goes through it directly.</summary>
        public NetSession Session => _session;

        /// <summary>T-26 — the colony scene this shell refreshes marker state on (null until attached).</summary>
        public MatchScene Scene => _scene;

        /// <summary>
        /// T-26 — hand this shell the built <see cref="MatchScene"/> so the pump can refresh the
        /// wireframe marker states from the models each frame: entry-tunnel pulse (S3, from
        /// <see cref="PlanningScreenModel.PulsingEntryTunnels"/>), entry flare (S4, from
        /// <see cref="CombatHudModel.EntryFlares"/>), and the lost/dark hotspot marking (S4, from
        /// the sim's emptied answer). Null detaches. The shell never builds the scene itself —
        /// scene ownership stays with the entry point, exactly as before.
        /// </summary>
        public void AttachScene(MatchScene scene)
        {
            _scene = scene;

            // Mirror the current model answers immediately: an attach between pumps must not show
            // a frame of stale (or default) marker state before the next pump happens to run.
            RefreshSceneMarkers();
        }

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
        public IInputSource Input => _input;

        /// <summary>
        /// Ticket 025 (T-25) — the combat action tunables this shell routes SPACE/Q/E with:
        /// the <see cref="ShellBootstrapOptions.CombatActions"/> it was composed with, or the
        /// shipped defaults when none was.
        /// </summary>
        public CombatActionConfig CombatActions => _combatActions;

        /// <summary>
        /// Ticket 025 (T-25) / R-31 / R-32 — the outcome of the most recent Q/E cast this shell's
        /// pump issued for the local hero (null before the first press). The shell only issues the
        /// command; cooldowns, locks and ranks stay sim-side, so an accepted cast and a rejection
        /// (ability_locked, ability_cooling) both land here for the UI to surface — and a
        /// rejection never breaks the pump.
        /// </summary>
        public AbilityOutcomeView LastAbilityOutcome => _lastAbilityOutcome;

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

        /// <summary>
        /// Ticket 023 (T-23) — S1's model: the callsign→profile load and the join-code inline
        /// error. Alive from birth (the title screen exists before anything is hosted).
        /// </summary>
        public TitleScreenModel Title => _title;

        /// <summary>
        /// Ticket 023 (T-23) — S2's model for the local peer: class picks, ready, and the
        /// all-ready auto-start its Update performs. Refreshed by the pump while a lobby is open.
        /// Rebuilt when the match changes hands (a rematch's lobby starts with nobody ready) and
        /// when a re-host builds a fresh session.
        /// </summary>
        public LobbyScreenModel Lobby => _lobby;

        /// <summary>
        /// Ticket 023 (T-23) — S3's model for the live match (null without one, like
        /// <see cref="Hud"/>): shop, ghost placement, sell, ready-up. Refreshed by the pump.
        /// </summary>
        public PlanningScreenModel Planning
        {
            get
            {
                BindToCurrentMatch();
                return _planning;
            }
        }

        /// <summary>
        /// Ticket 023 (T-23) — the interactive controls (buttons, input fields, the pointer
        /// wiring seam), built beside the labels under the same screen roots.
        /// </summary>
        public ShellControls Controls => _controls;

        /// <summary>
        /// T-23 — S6/S7's model (rematch enablement/stats), following the live match like
        /// <see cref="Hud"/>. Internal: the tests pin the CONTROLS; the model rides underneath.
        /// </summary>
        internal PostMatchModel PostMatch
        {
            get
            {
                BindToCurrentMatch();
                return _postMatch;
            }
        }

        /// <summary>
        /// T-23 / R-50 — HOST GAME: seat the local peer as host, carrying the TYPED callsign as
        /// its account (R-44). Ignored while a lobby/match is already up (those screens carry no
        /// HOST button anyway).
        ///
        /// <b>The re-host gap (orchestrator decision at 023):</b> after MAIN MENU / LEAVE the old
        /// session is <see cref="NetSessionPhase.Ended"/> — DEC-RUN-10 leaves it there forever and
        /// <see cref="NetSession.StartHost"/> throws for it (R-50's one-lobby guard). The dead
        /// session is not re-driven: hosting over one builds a FRESH <see cref="NetSession"/> on
        /// the same transport/factory at this composition root, so the player can always host
        /// again. The ended session object itself stays Ended, exactly as DEC-RUN-10 pins.
        /// </summary>
        public void RequestHost()
        {
            if (string.IsNullOrEmpty(_localPeerId))
            {
                // A shell with no local identity fronts nobody; there is no peer to seat.
                return;
            }

            if (_session.Phase == NetSessionPhase.Ended)
            {
                RebuildSessionForRehost();
            }

            if (_session.Phase != NetSessionPhase.Offline)
            {
                return;
            }

            // R-44 — the callsign IS the account: the seat, the HUD and the input path must all
            // key off what was typed, or the hosted hero belongs to somebody else's profile.
            if (!string.IsNullOrEmpty(_title.Callsign))
            {
                _accountId = _title.Callsign;
            }

            _session.StartHost(new NetPeer
            {
                PeerId = _localPeerId,
                AccountId = _title.Callsign,
                IsHost = true,
            });
        }

        /// <summary>
        /// T-23 — JOIN: this loopback shell fronts its own session, so there is no remote lobby a
        /// code could land in — the join fails and the model raises S1's inline error. Join
        /// SUCCESS is transport territory (a second endpoint) and lands here when it exists.
        /// </summary>
        public void RequestJoin()
        {
            _title.NoteJoinFailed();
        }

        /// <summary>
        /// T-23 / R-53 — LEAVE MATCH and MAIN MENU: the local peer leaves its own session — the
        /// only leave the session surface offers. For a host that ends the session (DEC-RUN-10)
        /// and the router lands on S1. The overlay never follows the player out of the match.
        /// </summary>
        public void LeaveToTitle()
        {
            _session.SetOverlayOpen(false);

            if (!string.IsNullOrEmpty(_localPeerId))
            {
                _session.Disconnect(_localPeerId);
            }
        }

        /// <summary>One presentation frame. See the class doc for the six-step contract.</summary>
        public void Pump(double deltaSeconds)
        {
            BindToCurrentMatch();

            // T-23 / R-62 / R-55 — the UI keys, through the same input seam as the hero keys:
            // held L opens the picker, held ESC raises the overlay. Open-only on purpose (release
            // closes nothing — the overlay's own close control does), and neither produces any
            // gameplay intent (DefaultHeroInputMap never reads them).
            HandleUiKeys();

            // T-24 / R-24 / R-30 — the planning pointer path: cursor → PointerAt with the oracle's
            // zone answer, a fresh MouseLeft press → one ground/placeable click through the T23
            // seam. Planning-only; combat never routes a mouse button anywhere (R-30).
            HandlePlanningPointer();

            // T-25 / R-30/R-31/R-32 — the combat action path: held SPACE fires one basic attack
            // per cadence window along the cursor aim line (the press fires immediately), Q/E
            // press-edges issue one HeroAbilityRequest each. Runs BEFORE the pre-step drain so the
            // commands' events ride this same pump's routing.
            HandleCombatActions(deltaSeconds);

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

                if (_stats != null)
                {
                    _stats.OnSimEvent(evt);
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

            // T-25 — combat routing state is per match: a fresh match starts with no ability
            // outcome, no cadence in flight, and no key remembered as held from the old one.
            _lastAbilityOutcome = null;
            _attackClock = 0.0;
            _attackWasDown = false;
            _qWasDown = false;
            _eWasDown = false;

            // T-23 / DEC-RUN-11 — the lobby model is rebuilt whenever the match changes hands: a
            // rematch returns the party to S2 with the picks retained (they live on the seats)
            // but with NOBODY ready — a stale ready flag would start the next match on arrival.
            _lobby = new LobbyScreenModel(_session, _localPeerId);

            if (match == null)
            {
                _tap = null;
                _hud = null;
                _planning = null;
                _postMatch = null;
                _stats = null;
                _zoneOracle = null;
                return;
            }

            _taps.TryGetValue(match, out _tap);
            // T-24 — one oracle per match, over the match's own map; its radii are re-copied off
            // the live sim every pump (see HandlePlanningPointer) so retunes move both sides.
            _zoneOracle = new PlacementZoneOracle(match.Sim.ColonyMap);
            _hud = new CombatHudModel(match, _accountId, _profiles);
            _planning = new PlanningScreenModel(match, PlayerSlotIdFor(match, _accountId));
            _stats = new MatchStatsTracker(match.Sim.Config.Placeables);

            // T-26 / R-23 — the barricade damage readout divides by the SIM'S catalog MaxHp, so
            // the binder is handed the live match's own rows (never a second copy of the numbers).
            _views.PlaceableCatalog = match.Sim.Config.Placeables;
            _postMatch = new PostMatchModel(
                _session, _localPeerId, _stats, match.State.TotalCivilians);
        }

        /// <summary>
        /// The sim addresses planning commands by PLAYER SLOT id, not by account — resolved off
        /// the seated party the factory just built. Null when this shell's account holds no slot
        /// (a headless observer), which the sim answers with a rejection, never a crash.
        /// </summary>
        private static string PlayerSlotIdFor(HostedMatch match, string accountId)
        {
            if (string.IsNullOrEmpty(accountId))
            {
                return null;
            }

            var players = match.State.Players;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player != null
                    && string.Equals(player.AccountId, accountId, StringComparison.Ordinal))
                {
                    return player.Id;
                }
            }

            return null;
        }

        /// <summary>See <see cref="RequestHost"/> — the fresh-session-per-hosting-attempt rule.</summary>
        private void RebuildSessionForRehost()
        {
            // Same transport (loopback's Shutdown left it restartable; it mints a fresh join
            // code), same factory, same profiles — only the SESSION is new. The old one is
            // dropped where it stands: Ended, exactly as DEC-RUN-10 leaves it.
            _session = new NetSession(_netConfig, _transport, _matchFactory);
            _router = new UiRouter(_session);
            _lobby = new LobbyScreenModel(_session, _localPeerId);

            _noticesDelivered = 0;
            _boundMatch = null;
            _tap = null;
            _hud = null;
            _planning = null;
            _postMatch = null;
            _stats = null;
            _zoneOracle = null;
            _pointerMouseWasDown = false;
            _lastAbilityOutcome = null;
            _attackClock = 0.0;
            _attackWasDown = false;
            _qWasDown = false;
            _eWasDown = false;
            _taps.Clear();
        }

        /// <summary>
        /// T-23 — the UI keys' input path (see <see cref="Pump"/>). In-match only: outside one
        /// there is no picker to open and the ESC overlay is a match overlay.
        /// </summary>
        private void HandleUiKeys()
        {
            if (_input == null || _session.Phase != NetSessionPhase.InMatch)
            {
                return;
            }

            var snapshot = _input.Sample();
            if (snapshot == null)
            {
                return;
            }

            if (snapshot.Pressed.Contains(PlayerKey.L) && _hud != null)
            {
                _hud.OpenPicker();
            }

            if (snapshot.Pressed.Contains(PlayerKey.Escape))
            {
                _session.SetOverlayOpen(true);
            }
        }

        /// <summary>
        /// T-24 — the planning pointer path (see <see cref="Pump"/>). Each in-match pump samples
        /// the cursor's ground point and, in the sim's PLANNING phase only:
        ///
        ///  * routes it to <see cref="ShellControls.PointerAt"/> with the oracle's R-24 answer
        ///    (the ghost follows the pointer and tints by zone);
        ///  * on a FRESH MouseLeft press (edge, not level — one click per press): a ghost up is a
        ///    ground click at the cursor, no ghost with a standing placeable under the cursor is a
        ///    placeable click (the R-22 sell), and a ghostless click on clear ground is NOTHING.
        ///
        /// The held-last-pump flag is updated in every phase, so a button pressed during combat
        /// does not read as a fresh press the moment planning returns. Combat routes no click
        /// anywhere: mouse buttons stay UI, and the planning UI is not on screen (R-30).
        /// </summary>
        private void HandlePlanningPointer()
        {
            if (_input == null || _session.Phase != NetSessionPhase.InMatch
                || _boundMatch == null || _planning == null || _zoneOracle == null)
            {
                return;
            }

            var snapshot = _input.Sample();
            if (snapshot == null)
            {
                return;
            }

            var mouseDown = snapshot.Pressed.Contains(PlayerKey.MouseLeft);
            var freshPress = mouseDown && !_pointerMouseWasDown;
            _pointerMouseWasDown = mouseDown;

            if (_boundMatch.State.Phase != MatchPhase.Planning)
            {
                return;
            }

            var cursor = new Vec2(snapshot.CursorGroundPoint.x, snapshot.CursorGroundPoint.y);

            // Mirror the LIVE sim's radii every pump — the oracle's defaults are only defaults,
            // and a retuned sim must move the tint with it or the tint lies (T-24's pin).
            var sim = _boundMatch.Sim;
            _zoneOracle.HotspotBuildingRadius = sim.HotspotBuildingRadius;
            _zoneOracle.EntryTunnelMouthRadius = sim.EntryTunnelMouthRadius;
            _zoneOracle.PlaceableFootprintRadius = sim.PlaceableFootprintRadius;

            var zoneValid = _zoneOracle.WouldAccept(_boundMatch.State, cursor);
            _controls.PointerAt(cursor, zoneValid);

            if (!freshPress)
            {
                return;
            }

            if (_planning.GhostActive)
            {
                // The click belongs to placement — even over a standing placeable (T23's
                // precedence: ClickPlaceable is ignored while a ghost is up; the overlap rule
                // rejects the attempt and the ghost stays for the retry).
                _controls.ClickGround(cursor, zoneValid);
                return;
            }

            // No ghost: the click is a sell exactly when a standing placeable is under the
            // cursor. The pick radius is unpinned by the tests except at distance zero; the
            // footprint radius is the natural "on it" extent.
            var picked = PlaceablePicker.Pick(
                _boundMatch.State, cursor, sim.PlaceableFootprintRadius);
            if (picked != null)
            {
                _controls.ClickPlaceable(picked);
            }
        }

        /// <summary>
        /// T-25 — the combat action path (see <see cref="Pump"/>). Each in-match pump reads SPACE,
        /// Q and E off the same snapshot seam as every other input ticket:
        ///
        ///  * <b>SPACE</b> (R-30/R-26): a FRESH press fires one <c>ResolveHeroAttack</c>
        ///    immediately; holding re-fires exactly once per
        ///    <see cref="CombatActionConfig.AttackCadenceSeconds"/> of pumped time, never once per
        ///    pump. Damage is the class's catalog <c>BasicAttackDamage</c> (per-pellet for the
        ///    Rancher, DEC-RUN-8) and the line is <see cref="AimLine"/> along the cursor.
        ///  * <b>Q/E</b> (R-31/R-32): press-EDGE only — one <c>HeroAbilityRequest</c> per press,
        ///    outcome (accepted or the sim's own rejection) surfaced on
        ///    <see cref="LastAbilityOutcome"/>. Cooldowns, locks and ranks stay sim-side; the
        ///    shell never client-side-gates a cast.
        ///
        /// The was-down flags update in EVERY phase (T-24's precedent), so a key held through
        /// planning does not read as a fresh press the moment combat returns — and the sim's
        /// COMBAT phase gates all issuing: SPACE/Q/E during planning route nothing (a routed
        /// planning basic would still advance the Gunslinger crit rhythm as a miss).
        ///
        /// Kill accounting: <c>ResolveHeroAttack</c> and the ability effects deliberately never
        /// kill at 0 HP — <c>RecordMonsterKill</c> is the sim's kill command (alive flip, roster,
        /// R-20 bounty) and nothing else shipped issues it for hero damage, so this routing reaps
        /// after every request it lands (see <see cref="ReapDeadMonsters"/>).
        /// </summary>
        private void HandleCombatActions(double deltaSeconds)
        {
            if (_input == null || _session.Phase != NetSessionPhase.InMatch || _boundMatch == null)
            {
                return;
            }

            var snapshot = _input.Sample();
            if (snapshot == null)
            {
                return;
            }

            var spaceDown = snapshot.Pressed.Contains(PlayerKey.Space);
            var qDown = snapshot.Pressed.Contains(PlayerKey.Q);
            var eDown = snapshot.Pressed.Contains(PlayerKey.E);
            var spaceFresh = spaceDown && !_attackWasDown;
            var qFresh = qDown && !_qWasDown;
            var eFresh = eDown && !_eWasDown;
            _attackWasDown = spaceDown;
            _qWasDown = qDown;
            _eWasDown = eDown;

            var state = _boundMatch.State;
            if (state == null || state.Phase != MatchPhase.Combat)
            {
                // Phase-dead keys: nothing is issued outside combat, and the cadence does not
                // accrue toward a free shot the moment combat returns.
                _attackClock = 0.0;
                return;
            }

            var hero = LocalHeroOf(state);
            if (hero == null || !hero.Alive)
            {
                // No seated local hero (headless) — or a downed one (R-33: dead heroes spectate).
                _attackClock = 0.0;
                return;
            }

            var cursor = new Vec2(snapshot.CursorGroundPoint.x, snapshot.CursorGroundPoint.y);
            var sim = _boundMatch.Sim;

            if (spaceDown)
            {
                if (spaceFresh)
                {
                    // The press fires immediately (T-24's pump-edge precedent); the cadence only
                    // paces the re-fire from here.
                    _attackClock = 0.0;
                    FireBasicAttack(sim, state, hero, cursor);
                }
                else
                {
                    _attackClock += deltaSeconds;
                    if (_attackClock >= _combatActions.AttackCadenceSeconds)
                    {
                        _attackClock -= _combatActions.AttackCadenceSeconds;
                        FireBasicAttack(sim, state, hero, cursor);
                    }
                }
            }
            else
            {
                _attackClock = 0.0;
            }

            if (qFresh)
            {
                CastAbilitySlot(sim, state, hero, cursor, AbilitySlot.Q);
            }

            if (eFresh)
            {
                CastAbilitySlot(sim, state, hero, cursor, AbilitySlot.E);
            }
        }

        /// <summary>
        /// One basic attack: catalog damage (the kit's per-pellet quantum for the Rancher —
        /// DEC-RUN-8 makes the sim's spread ride the same line), the honest <see cref="AimLine"/>
        /// report, and the kill reap after. Draining the tap after each command keeps every event
        /// (monster_damaged, monster_killed, xp_awarded) in this pump's feed — the sim's
        /// LastObservation is per-command and the next command would overwrite it.
        /// </summary>
        private void FireBasicAttack(MatchSim sim, MatchState state, Hero hero, Vec2 cursor)
        {
            var kit = sim.Config.HeroKits.KitFor(hero.HeroClass);
            var line = AimLine.EntitiesAlong(
                state, hero.Id, hero.Pos, cursor,
                _combatActions.AimLineLength, _combatActions.AimLineWidth);

            sim.ResolveHeroAttack(new HeroAttackRequest
            {
                AttackerId = hero.Id,
                AttackerClass = hero.HeroClass,
                Damage = kit.BasicAttackDamage,
                EntitiesOnLine = line,
            });
            DrainTap();

            ReapDeadMonsters(sim, state, hero);
        }

        /// <summary>
        /// One Q/E press-edge: the request carries the same honest aim line (a skillshot reads it,
        /// a self-AoE ignores it) plus the normalized aim direction for the dash classes; TargetId
        /// stays null — the sim's own nearest-on-line fallback resolves single-target casts. The
        /// outcome — accepted or the sim's fixture-shaped rejection — lands on
        /// <see cref="LastAbilityOutcome"/>, and a rejection never breaks the pump.
        /// </summary>
        private void CastAbilitySlot(MatchSim sim, MatchState state, Hero hero, Vec2 cursor, string slot)
        {
            var line = AimLine.EntitiesAlong(
                state, hero.Id, hero.Pos, cursor,
                _combatActions.AimLineLength, _combatActions.AimLineWidth);

            var dx = cursor.X - hero.Pos.X;
            var dy = cursor.Y - hero.Pos.Y;
            var magnitude = Math.Sqrt((dx * dx) + (dy * dy));
            var aimDirection = magnitude > 0.0
                ? new Vec2(dx / magnitude, dy / magnitude)
                : new Vec2(0.0, 0.0);

            var outcome = sim.CastAbility(new HeroAbilityRequest
            {
                CasterId = hero.Id,
                Slot = slot,
                AimDirection = aimDirection,
                EntitiesOnLine = line,
            });
            DrainTap();

            if (outcome == null)
            {
                return;
            }

            _lastAbilityOutcome = new AbilityOutcomeView(outcome);
            if (outcome.Accepted)
            {
                ReapDeadMonsters(sim, state, hero);
            }
        }

        /// <summary>
        /// T-25 / R-02 / R-20 / R-40 — the kill accounting the sim deliberately leaves to its
        /// caller: <c>ResolveHeroAttack</c> (and the ability damage helper) clamp a monster to
        /// 0 HP without killing it, and <c>RecordMonsterKill</c> is the one command that flips
        /// `alive`, shrinks the wave roster and pays the catalog bounty — exactly once, because
        /// only monsters still on the living roster with their HP emptied are reaped, and the kill
        /// itself removes them from that roster. The kill's XP credits the attacker's account
        /// (R-40 — the shell answers "who is credited" before the sim is called).
        /// </summary>
        private void ReapDeadMonsters(MatchSim sim, MatchState state, Hero attacker)
        {
            _reapScratch.Clear();
            _reapScratch.AddRange(state.Wave.LivingMonsterIds);

            for (var i = 0; i < _reapScratch.Count; i++)
            {
                var monsterId = _reapScratch[i];
                if (!state.Monsters.TryGetValue(monsterId, out var monster)
                    || monster == null || !monster.Alive || monster.Hp > 0.0)
                {
                    continue;
                }

                // TryGet, not StatsFor: a reap must never throw mid-frame for an archetype with
                // no catalog row — a rowless kill still dies, it just pays nothing.
                var stats = sim.Config.Monsters.TryGet(monster.Type);
                var kill = new MonsterKillRequest
                {
                    MonsterId = monsterId,
                    MonsterType = monster.Type,
                    Bounty = stats == null ? 0 : stats.Bounty,
                    KillerHeroId = attacker.Id,
                };

                sim.RecordMonsterKill(kill);
                DrainTap();

                if (!string.IsNullOrEmpty(attacker.AccountId))
                {
                    sim.AwardKillXp(kill, attacker.AccountId);
                    DrainTap();
                }
            }
        }

        /// <summary>Capture the just-issued command's events before the next command overwrites them.</summary>
        private void DrainTap()
        {
            if (_tap != null)
            {
                _tap.Drain();
            }
        }

        /// <summary>The seated hero whose AccountId is this shell's account (null when none is).</summary>
        private Hero LocalHeroOf(MatchState state)
        {
            if (state == null || string.IsNullOrEmpty(_accountId))
            {
                return null;
            }

            foreach (var hero in state.Heroes.Values)
            {
                if (hero != null && string.Equals(hero.AccountId, _accountId, StringComparison.Ordinal))
                {
                    return hero;
                }
            }

            return null;
        }

        // ---- presentation refresh -------------------------------------------------------------

        private void RefreshPresentation()
        {
            // T-23 — the lobby model's own Update carries the R-50 all-ready auto-start (on the
            // host's model only), so it runs before the router derives the screen: READY on a
            // solo lobby lands S3/S4 on this same pump's refresh.
            if (_lobby != null)
            {
                _lobby.Update();
            }

            // The lobby's Update may just have started a match — bind before deriving anything.
            BindToCurrentMatch();

            _router.Update();

            if (_hud != null)
            {
                _hud.Refresh();
            }

            if (_planning != null)
            {
                _planning.Refresh();
            }

            // T-26 / DEC-018 — `wave_spawned` names no tunnels, so the HUD's entry flare targets
            // are the entries the planning preview named, carried across the phase change: every
            // planning refresh re-arms the HUD with the current preview, and the spawn event that
            // arrives after the phase flips reads the last planning answer.
            if (_planning != null && _hud != null && _boundMatch != null
                && _boundMatch.State != null && _boundMatch.State.Phase == MatchPhase.Planning)
            {
                _hud.SetExpectedEntryTunnels(_planning.PulsingEntryTunnels);
            }

            RefreshLabels();

            if (_controls != null)
            {
                _controls.Refresh();
            }

            _ui.SetActiveScreen(_router.Screen);

            RefreshSceneMarkers();
        }

        /// <summary>
        /// T-26 — mirror the models onto the attached scene's marker components (wireframe S3/S4):
        ///
        ///  * <b>pulse</b> — a tunnel marker pulses exactly while the sim is in its PLANNING phase
        ///    and <see cref="PlanningScreenModel.PulsingEntryTunnels"/> names it (the model itself
        ///    already answers empty outside planning, so the phase gate here is what keeps the
        ///    pulse from leaking into combat even across a stale refresh);
        ///  * <b>flare</b> — a tunnel marker flares exactly while the sim is in COMBAT and
        ///    <see cref="CombatHudModel.EntryFlares"/> names it; riding the phase means the flare
        ///    has always cleared by the next planning screen (the pinned deadline) without this
        ///    class inventing a timer;
        ///  * <b>lost</b> — a hotspot marker is dark exactly when the sim's own count answers
        ///    emptied (Civilians == 0), read straight off replicated state.
        ///
        /// Everything here READS models/state and WRITES marker components — no sim state is ever
        /// touched (T-10), and no marker decides anything for itself.
        /// </summary>
        private void RefreshSceneMarkers()
        {
            if (_scene == null)
            {
                return;
            }

            var state = _boundMatch == null ? null : _boundMatch.State;
            var planningPhase = state != null && state.Phase == MatchPhase.Planning;
            var combatPhase = state != null && state.Phase == MatchPhase.Combat;

            foreach (var pair in _scene.EntryTunnelMarkers)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                var view = pair.Value.GetComponent<EntryTunnelMarkerView>();
                if (view == null)
                {
                    continue;
                }

                var pulsing = planningPhase && _planning != null
                    && Names(_planning.PulsingEntryTunnels, pair.Key);
                var flaring = combatPhase && _hud != null
                    && Names(_hud.EntryFlares, pair.Key);

                view.SetStates(pulsing, flaring);
            }

            foreach (var pair in _scene.HotspotMarkers)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                var view = pair.Value.GetComponent<HotspotMarkerView>();
                if (view == null)
                {
                    continue;
                }

                var lost = false;
                if (state != null && state.Hotspots.TryGetValue(pair.Key, out var hotspot)
                    && hotspot != null)
                {
                    lost = hotspot.Civilians <= 0;
                }

                view.SetLost(lost);
            }
        }

        /// <summary>Does the model's tunnel list name this index? (No LINQ — this runs per pump.)</summary>
        private static bool Names(IReadOnlyList<int> tunnels, int index)
        {
            if (tunnels == null)
            {
                return false;
            }

            for (var i = 0; i < tunnels.Count; i++)
            {
                if (tunnels[i] == index)
                {
                    return true;
                }
            }

            return false;
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
                // SpriteRenderer faces +Z (XY plane). The match camera looks down -Y at XZ,
                // so an unrotated sprite is edge-on and invisible. Lay it on the colony floor.
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                // Ground is sized by MatchSceneBuilder to cover the play area. Characters and
                // props that come in smaller than a body from y-down get enlarged so they read
                // at camera height 60 (~2-3 world units across).
                if (artKey != ShellArtKeys.GroundTile)
                {
                    var across = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
                    const float minCharacterSpan = 2.5f;
                    if (across > 0.0001f && across < minCharacterSpan)
                    {
                        var s = minCharacterSpan / across;
                        go.transform.localScale = new Vector3(s, s, 1f);
                    }
                }

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
                match.Session = new MatchSession(tap, _shell._heroIntents, _shell._views);

                _shell._taps[match] = tap;
                return match;
            }
        }

        /// <summary>
        /// Ticket 022 (T-22) / R-30 — the hole ticket 021 left null: each host step, sample the
        /// shell's <see cref="IInputSource"/> once, resolve it through the shipped
        /// <see cref="DefaultHeroInputMap"/>, and address the result to the LOCAL hero — the one
        /// whose <c>AccountId</c> is the shell's <see cref="ShellBootstrapOptions.LocalAccountId"/>.
        ///
        /// Candidates only: the sim still decides what the intent is worth (R-33 — a dead hero does
        /// not walk), and <see cref="HostLoop"/> already skips a zero direction. No source, no
        /// local hero, or a headless shell simply contributes nothing — never throws mid-frame.
        /// </summary>
        private sealed class LocalHeroIntentSource : IHeroIntentSource
        {
            private readonly ShellBootstrap _shell;
            private readonly DefaultHeroInputMap _map = new DefaultHeroInputMap();

            /// <summary>Reused per step so a held key does not allocate sixty times a second.</summary>
            private readonly HeroIntentCommand _command = new HeroIntentCommand();

            private readonly List<HeroIntentCommand> _commands = new List<HeroIntentCommand>(1);

            public LocalHeroIntentSource(ShellBootstrap shell)
            {
                _shell = shell;
            }

            public IReadOnlyList<HeroIntentCommand> IntentsThisStep(ISimHost sim, double deltaSeconds)
            {
                var source = _shell._input;
                if (source == null || sim == null)
                {
                    return null;
                }

                var hero = LocalHero(sim.State);
                if (hero == null)
                {
                    return null;
                }

                _command.HeroId = hero.Id;
                _command.Intent = _map.Resolve(source.Sample());

                _commands.Clear();
                _commands.Add(_command);
                return _commands;
            }

            private Hero LocalHero(MatchState state)
            {
                var accountId = _shell._accountId;
                if (state == null || string.IsNullOrEmpty(accountId))
                {
                    return null;
                }

                foreach (var hero in state.Heroes.Values)
                {
                    if (hero != null && string.Equals(hero.AccountId, accountId, StringComparison.Ordinal))
                    {
                        return hero;
                    }
                }

                return null;
            }
        }
    }
}
