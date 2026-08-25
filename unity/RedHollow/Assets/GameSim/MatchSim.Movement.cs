using System;

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
        ///
        /// The rule is one line — <c>CurrentSpeed * deltaSeconds</c> along whatever direction the
        /// oracle answered — and everything around it is the guard that keeps that line honest.
        /// <see cref="Monster.CurrentSpeed"/> is re-read every tick rather than captured, because
        /// it is the field DEC-008's lasso multiplies and G-019's expiry restores: a mover that
        /// cached it, or that read <see cref="Monster.BaseSpeed"/> instead, would leave a slow
        /// either permanent or inert.
        ///
        /// <b>What this replicates.</b> The count, and nothing else — see
        /// <see cref="MonsterMovementResult"/>. This is the highest-frequency operation the sim
        /// has (60 ticks a second against a wave of up to 30 monsters, R-19), and a `pos` delta per
        /// monster per tick would put a wave-sized payload on the wire for information the client
        /// already re-reads from replicated transforms. G-013 set the precedent by replicating
        /// <c>placeables.count</c> rather than the placeable itself.
        /// </summary>
        /// <param name="deltaSeconds">The step to advance, in sim seconds.</param>
        public MonsterMovementResult TickMonsterMovement(double deltaSeconds)
        {
            BeginCommand();

            var result = new MonsterMovementResult { DeltaSeconds = deltaSeconds };

            // A zero-length step moves nobody, and a negative one is a caller bug rather than a
            // game state — SimClock already refuses to run time backwards, and the only reading of
            // a backwards delta here would walk the entire wave back through the colony. Both are
            // an empty tick: the host's loop should not have to catch on a frame it clamped.
            if (!(deltaSeconds > 0.0))
            {
                return Finish(result);
            }

            foreach (var monster in State.Monsters.Values)
            {
                // Corpses stay in the roster until it is cleared (they are flagged, not deleted),
                // so a mover that walked the dictionary blind would march the graveyard at the
                // shelters alongside the living wave.
                if (!monster.Alive)
                {
                    continue;
                }

                // No target, or one that no longer resolves — the hero it was chasing died and
                // left the field (R-33), or the barricade it was chewing was sold in planning
                // (R-22). Both are ordinary mid-match states, and holding position is the reading
                // that costs nothing: R-16's SelectTarget runs on the host's next pass and gives
                // it somewhere to go. Wandering would be inventing a mechanic the PRD never names.
                if (!TryTargetPosition(monster.TargetId, out var targetPos))
                {
                    continue;
                }

                var from = monster.Pos;

                // R-18 / R-51: which way is a NavMesh question, so it is the shell's. The sim
                // takes the answer as a *direction only* — normalising it is what stops a shell
                // from handing out ground the lasso cannot slow (DEC-008), and a zero answer is
                // the seam's defined "no path", which must hold the monster rather than fall back
                // to a straight line through the geometry the navigation data exists to avoid.
                if (!TryUnitDirection(Directions.DirectionFor(monster.Id, from, targetPos), out var direction))
                {
                    continue;
                }

                var step = monster.CurrentSpeed * deltaSeconds;
                if (!(step > 0.0))
                {
                    continue;
                }

                // Arrival (R-18). The step is clamped to the ground there actually is to cover, so
                // a monster lands on its target rather than sailing past it and then oscillating
                // across it for the rest of the match once the direction is recomputed. A clamp
                // rather than a stopping radius because the PRD names no melee reach — inventing
                // one here would ship a guess as spec, and the clamp needs no number at all. At a
                // real 60Hz tick it never binds; it exists for the coarse steps a stalled host
                // catches up with.
                var gap = from.DistanceTo(targetPos);
                if (step > gap)
                {
                    step = gap;
                }

                if (!(step > 0.0))
                {
                    // Already standing on it. Holding still here is the other half of arrival:
                    // together with the clamp above, an arrived monster is stable forever.
                    continue;
                }

                monster.Pos = Displaced(from, direction, step);

                if (!monster.Pos.Equals(from))
                {
                    result.MonstersMoved++;
                }
            }

            return Finish(result);
        }

        /// <summary>
        /// R-30 / R-33. A player is holding a movement key: step their hero that way.
        ///
        /// Heroes deliberately bypass <see cref="Directions"/>. WASD is a *command* — the shell
        /// resolves the keys into <c>HeroIntent.MoveDirection</c> and sends it like any other
        /// command (R-51) — not a pathing question. Monsters need an oracle because nobody is
        /// driving them; a hero has a driver.
        ///
        /// The commanded direction is normalised, which is a decision this ticket makes rather
        /// than one the PRD states: a raw WASD diagonal has both components at 1, so applying it
        /// as given would make "hold W and D" a 1.41x sprint and the fastest way across the map.
        /// Magnitude carries no meaning here for the same reason it carries none on the oracle —
        /// the sim owns speed, and a client that could name its own would own it instead.
        ///
        /// Unlike the monster tick this does replicate a `pos` delta: there are at most four
        /// heroes (R-30) rather than a wave of thirty, and the other clients need the host's
        /// authoritative answer to a move they predicted locally.
        /// </summary>
        public HeroMoveResult MoveHero(HeroMoveRequest request)
        {
            BeginCommand();

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var result = new HeroMoveResult { HeroId = request.HeroId };

            // A hero the match does not hold is a caller bug, not a game state — the same reading
            // SelectTarget takes for an unknown monster. An empty step writes nothing and can
            // never be mistaken for a real one: it covered no ground.
            if (request.HeroId == null || !State.Heroes.TryGetValue(request.HeroId, out var hero))
            {
                return Finish(result);
            }

            result.Pos = hero.Pos;

            // R-33. A dead hero spectates a living ally; their body is not on the field. Their
            // client is still running and still sending input, so trusting the command rather than
            // the world would let a corpse tour the colony for the ten seconds until respawn
            // (DEC-010) — and respawn writes a position of its own, which that ghost walk would
            // silently be fighting with.
            if (!hero.Alive)
            {
                return Finish(result);
            }

            if (!(request.DeltaSeconds > 0.0))
            {
                return Finish(result);
            }

            // No keys held is the zero vector, and it is a normal frame rather than a bad command.
            if (!TryUnitDirection(request.Direction, out var direction))
            {
                return Finish(result);
            }

            var step = HeroMoveSpeed(hero) * request.DeltaSeconds;
            if (!(step > 0.0))
            {
                return Finish(result);
            }

            var from = hero.Pos;
            hero.Pos = Displaced(from, direction, step);

            result.Pos = hero.Pos;
            result.DistanceMoved = step;

            RecordChange(hero.Id, "pos", from, hero.Pos);
            return Finish(result);
        }

        // ---- movement helpers ------------------------------------------------------------------

        /// <summary>
        /// R-30 — how fast this hero walks: its class's configured pace, or the default when the
        /// config does not name the class. The lookup is a rule and so lives here, which is why
        /// <see cref="HeroMovementConfig"/> is plain fields with no resolution logic of its own.
        /// </summary>
        private double HeroMoveSpeed(Hero hero)
        {
            if (hero.HeroClass != null
                && HeroMovement.MoveSpeedByClass.TryGetValue(hero.HeroClass, out var configured))
            {
                return configured;
            }

            return HeroMovement.DefaultMoveSpeed;
        }

        /// <summary>
        /// Where a target id actually stands, across the three things R-16 lets a monster pick: a
        /// hero, a hotspot, or the barricade blocking its way (G-004). Answers false when the id
        /// names nothing the monster can still walk to.
        ///
        /// A dead hero and a destroyed placeable resolve to nothing rather than to their last
        /// position: both have left the field, and chasing the ghost of one would keep a monster
        /// walking at a spot no rule will ever let it attack. An emptied hotspot still resolves —
        /// the shelter is still a building standing in the colony, and R-12's "no longer a valid
        /// target" is a *targeting* rule that SelectTarget applies on the host's next pass.
        /// </summary>
        private bool TryTargetPosition(string targetId, out Vec2 pos)
        {
            pos = new Vec2(0.0, 0.0);

            if (targetId == null)
            {
                return false;
            }

            if (State.Heroes.TryGetValue(targetId, out var hero))
            {
                pos = hero.Pos;
                return hero.Alive;
            }

            if (State.Hotspots.TryGetValue(targetId, out var hotspot))
            {
                pos = hotspot.Pos;
                return true;
            }

            if (State.Placeables.TryGetValue(targetId, out var placeable))
            {
                pos = placeable.Pos;
                return placeable.Exists;
            }

            return false;
        }

        /// <summary>
        /// The unit-length reading of a direction that came from outside the sim, or false when
        /// there is no usable direction in it.
        ///
        /// Both callers take their direction from an untrusted source — an
        /// <see cref="IDirectionOracle"/> implemented in the shell, and a movement command that
        /// arrived over the wire from a client — so this is the guard as much as the maths. The
        /// single <c>!(length &gt; 0.0)</c> test rejects the zero vector (the seam's defined "no
        /// step", and a player touching no keys) and a NaN alike, since NaN fails every
        /// comparison; the infinity check catches the other way an answer can carry no direction.
        /// Normalising blind is the classic position poisoner: one NaN coordinate spreads to every
        /// distance computed from it and every target selection after that.
        ///
        /// Deliberately separate from <see cref="Normalized"/>, which answers a zero vector with a
        /// zero vector — fine for an aim that resolves to "no dash", but here the caller must be
        /// able to tell "hold still" apart from "step this way" without inspecting the result.
        /// </summary>
        private static bool TryUnitDirection(Vec2 direction, out Vec2 unit)
        {
            unit = new Vec2(0.0, 0.0);

            var length = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
            if (!(length > 0.0) || double.IsInfinity(length))
            {
                return false;
            }

            unit = new Vec2(direction.X / length, direction.Y / length);
            return true;
        }
    }
}
