using System;
using RedHollow.Game.Art;
using RedHollow.Game.Net;
using RedHollow.Game.View;
using RedHollow.Sim;

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
        public ShellBootstrap(ShellBootstrapOptions options = null)
        {
            throw new NotImplementedException(
                "T21 not implemented: the shell composition root — session, UI, feel and art wired together");
        }

        /// <summary>The session this shell fronts. Hosting/joining goes through it directly.</summary>
        public NetSession Session
        {
            get { throw new NotImplementedException("T21 not implemented: Session"); }
        }

        /// <summary>R-60 — the screen router the UI activation follows.</summary>
        public UiRouter Router
        {
            get { throw new NotImplementedException("T21 not implemented: Router"); }
        }

        /// <summary>The built uGUI hierarchy.</summary>
        public ShellUi Ui
        {
            get { throw new NotImplementedException("T21 not implemented: Ui"); }
        }

        /// <summary>R-64 — the feel router every replicated event is offered to.</summary>
        public FeelRouter Feel
        {
            get { throw new NotImplementedException("T21 not implemented: Feel"); }
        }

        /// <summary>
        /// R-51 — the view binder every match this shell starts is bound with (one binder for the
        /// shell's lifetime; its reconciliation follows whatever match is live).
        /// </summary>
        public MatchViewBinder Views
        {
            get { throw new NotImplementedException("T21 not implemented: Views"); }
        }

        /// <summary>
        /// R-15 — the asset seam the binder resolves through: an <see cref="ArtVisualResolver"/>
        /// over <see cref="Art"/>, chained in front of the placeholder. Total for any input.
        /// </summary>
        public IVisualResolver Visuals
        {
            get { throw new NotImplementedException("T21 not implemented: Visuals"); }
        }

        /// <summary>The artKey→asset table behind <see cref="Visuals"/>.</summary>
        public ArtCatalog Art
        {
            get { throw new NotImplementedException("T21 not implemented: Art"); }
        }

        /// <summary>
        /// R-61 — the combat HUD model for the local account. Null until a match is live; rebuilt
        /// for each new match (a rematch is a different <see cref="HostedMatch"/>, R-07).
        /// </summary>
        public CombatHudModel Hud
        {
            get { throw new NotImplementedException("T21 not implemented: Hud"); }
        }

        /// <summary>One presentation frame. See the class doc for the six-step contract.</summary>
        public void Pump(double deltaSeconds)
        {
            throw new NotImplementedException("T21 not implemented: Pump");
        }

        /// <summary>
        /// Destroy every GameObject this shell created (the UI under "RedHollow_Shell", the
        /// binder's views) and release the transport. Safe to call twice.
        /// </summary>
        public void TearDown()
        {
            throw new NotImplementedException("T21 not implemented: TearDown");
        }

        /// <summary>
        /// R-15 — the default catalog: the four imported representative assets
        /// (Assets/Game/Art/{Textures,Characters,Icons,UI}/, the exact files T13's seam tests pin)
        /// registered under the <see cref="ShellArtKeys"/> spellings, each with a factory that
        /// instantiates a renderable GameObject carrying that asset. HOW the asset is loaded is
        /// free (a Resources copy, a serialized catalog asset — but never AssetDatabase: this is
        /// runtime code); T13's imported paths must stay where its locked tests read them.
        /// </summary>
        public static ArtCatalog LoadRepresentativeArt()
        {
            throw new NotImplementedException(
                "T21 not implemented: the four representative assets registered as catalog data");
        }
    }
}
