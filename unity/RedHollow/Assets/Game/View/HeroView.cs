using System;
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
            throw new NotImplementedException("ticket 016 — hero view binding");
        }

        public void RenderFrom(MatchState state)
        {
            throw new NotImplementedException("ticket 016 — render from replicated sim state");
        }

        /// <summary>R-30 — turn to face this frame's aim point.</summary>
        public void Apply(HeroIntent intent)
        {
            throw new NotImplementedException("ticket 016 — cursor facing");
        }
    }
}
