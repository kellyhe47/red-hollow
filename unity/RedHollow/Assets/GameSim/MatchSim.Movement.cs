namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 018 (T-18) owns this half of <see cref="MatchSim"/>: advancing a position over time.
    /// Requirements R-17, R-18, R-30, R-33, R-51; DEC-008. It grades no fixture.
    ///
    /// Nothing in the sim moved anything before this file. Positions were only ever *set* — at
    /// spawn, at respawn, by Stampede's knockback and by placement — so monsters never walked to a
    /// hotspot, never arrived, never attacked one, and R-02's defeat condition was unreachable in a
    /// real match. <see cref="Monster.CurrentSpeed"/> was written at spawn and multiplied by the
    /// lasso and then read by nothing at all, which left DEC-008's 50% slow affecting nothing and
    /// R-17's Speed column inert. G-018 grades the slow being applied and G-019 grades it expiring;
    /// the behaviour they bracket did not exist. The contract therefore lives entirely in
    /// T18_MovementTests.
    ///
    /// The seam: **the sim owns how far, the shell owns which way.** R-18 routes monsters over a
    /// NavMesh, which is UnityEngine and cannot live here (R-51), so direction comes from an
    /// injected <see cref="IDirectionOracle"/> exactly as blocking comes from an injected
    /// <see cref="IPathOracle"/>. Speed stays a rule, because it is one.
    ///
    /// The shared core — fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        private IDirectionOracle _directions;

        /// <summary>
        /// R-18 / R-51 — which way movers step. Settable rather than constructor-injected, like
        /// <see cref="WaveTable"/> and <see cref="ColonyMap"/>: the Unity shell hands the host a
        /// NavMesh-backed oracle at match start, and a match built without one walks in straight
        /// lines so solo play and the test suite need no shell.
        /// </summary>
        public IDirectionOracle Directions
        {
            // Built on first read rather than in the constructor, matching WaveTable: a caller
            // supplying its own oracle must not pay for a discarded default.
            get => _directions ?? (_directions = new StraightLineDirectionOracle());
            set => _directions = value;
        }

        private HeroMovementConfig _heroMovement;

        /// <summary>
        /// R-30 — hero pace. It lives here rather than on <see cref="SimConfig"/> for the same
        /// reason <see cref="WaveTable"/> does: it is movement-rule data nothing else in the sim
        /// reads. See <see cref="HeroMovementConfig"/> for why the PRD supplies no number.
        /// </summary>
        public HeroMovementConfig HeroMovement
        {
            get => _heroMovement ?? (_heroMovement = new HeroMovementConfig());
            set => _heroMovement = value;
        }

        /// <summary>
        /// R-17 / R-18 / DEC-008. Advance every living monster toward its target for one step.
        ///
        /// A single tick rather than a per-monster command, unlike <see cref="SelectTarget"/>:
        /// nobody sends this from a client. It is the host's own loop, and one call per tick is
        /// what keeps every monster on the same clock.
        /// </summary>
        /// <param name="deltaSeconds">The step to advance, in sim seconds.</param>
        public MonsterMovementResult TickMonsterMovement(double deltaSeconds)
        {
            BeginCommand();
            throw NotYet("T-18", "monsters close on their targets at CurrentSpeed (R-17/R-18/DEC-008)");
        }

        /// <summary>
        /// R-30 / R-33. A player is holding a movement key: step their hero that way.
        /// </summary>
        public HeroMoveResult MoveHero(HeroMoveRequest request)
        {
            BeginCommand();
            throw NotYet("T-18", "heroes step on the commanded direction at their configured speed (R-30)");
        }
    }
}
