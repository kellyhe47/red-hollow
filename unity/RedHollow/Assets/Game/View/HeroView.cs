using RedHollow.Game.Input;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// The local hero's presentation. Position, HP and liveness mirror replicated sim state exactly
    /// as <see cref="MonsterView"/> does; <see cref="Facing"/> is the one thing that does not come
    /// from the sim, because the sim has no facing — R-30 makes it a pure function of where the
    /// cursor is.
    /// </summary>
    public sealed class HeroView : MonoBehaviour
    {
        /// <summary>Below this a cursor is sitting on the hero, and "which way" has no answer.</summary>
        private const float DegenerateAim = 1e-6f;

        public string HeroId { get; private set; }

        public VisualHandle Visual { get; private set; }

        public Vector3 WorldPosition { get; private set; }

        public double DisplayedHp { get; private set; }

        public bool DisplayedAlive { get; private set; }

        /// <summary>
        /// R-30 — the unit ground-space direction the hero is turned towards: from the hero's
        /// replicated position to <see cref="HeroIntent.AimPoint"/>. Never the movement direction.
        /// A direction rather than a rotation, because the PRD pins neither a turn rate nor a
        /// rotation representation.
        /// </summary>
        public Vector2 Facing { get; private set; }

        public void Bind(string heroId, VisualHandle visual)
        {
            HeroId = heroId;
            Visual = visual;

            // A bound hero always has a facing, so nothing downstream has to handle a zero one. Up
            // is ground-space forward (see InputSnapshot.CursorGroundPoint); the first Apply
            // replaces it.
            Facing = Vector2.up;

            ViewRig.Attach(transform, visual);
        }

        /// <summary>
        /// R-51 — the hero's own values, copied out of replicated state. Identical in kind to
        /// <see cref="MonsterView.RenderFrom"/>: the sim's numbers, not the client's guess. Ticket
        /// 011 owns reconciling this with local prediction (R-52); the read direction is the same
        /// either way.
        /// </summary>
        public void RenderFrom(MatchState state)
        {
            if (state == null || string.IsNullOrEmpty(HeroId))
            {
                return;
            }

            Hero hero;
            if (!state.Heroes.TryGetValue(HeroId, out hero) || hero == null)
            {
                return;
            }

            DisplayedHp = hero.Hp;
            DisplayedAlive = hero.Alive;
            WorldPosition = SimSpace.ToWorld(hero.Pos);

            transform.position = WorldPosition;
            ViewRig.SetVisible(Visual, DisplayedAlive);
        }

        /// <summary>
        /// R-30 — turn to face this frame's aim point.
        ///
        /// The aim point and *only* the aim point: <see cref="HeroIntent.MoveDirection"/> is not
        /// read here, which is the whole of "the hero faces the mouse cursor rather than turning
        /// toward movement". A hero strafing right while the cursor sits behind it walks right and
        /// looks back, and that is the case the test discriminates on.
        ///
        /// A cursor resting exactly on the hero leaves the facing where it was rather than snapping
        /// to a default — the direction is genuinely undefined there, and inventing one makes the
        /// hero spin whenever the mouse passes over its feet.
        /// </summary>
        public void Apply(HeroIntent intent)
        {
            if (intent == null)
            {
                return;
            }

            var here = SimSpace.ToGroundVector(WorldPosition);
            var toCursor = intent.AimPoint - here;

            if (toCursor.sqrMagnitude <= DegenerateAim)
            {
                return;
            }

            Facing = toCursor.normalized;
            transform.rotation = Quaternion.LookRotation(SimSpace.DirectionToWorld(Facing), Vector3.up);
        }
    }
}
