using System;
using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// One monster's presentation, driven entirely from replicated sim state (R-51).
    ///
    /// <see cref="RenderFrom"/> mirrors — it does not decide. Whether a monster is alive, how much
    /// HP it has and where it stands are all the sim's answers (R-17/R-18/R-51); a view that
    /// recomputed any of them would disagree with the host the moment the numbers were retuned.
    /// It writes nothing back: see T10_HostLoopTests' IL invariant, which enforces that for every
    /// MonoBehaviour in this assembly.
    /// </summary>
    public sealed class MonsterView : MonoBehaviour
    {
        public string MonsterId { get; private set; }

        public VisualHandle Visual { get; private set; }

        public Vector3 WorldPosition { get; private set; }

        /// <summary>Exactly what the sim says this monster's HP is. Not a fraction, not a clamp.</summary>
        public double DisplayedHp { get; private set; }

        /// <summary>Exactly what the sim says. The view never applies the death rule itself.</summary>
        public bool DisplayedAlive { get; private set; }

        public void Bind(string monsterId, VisualHandle visual)
        {
            throw new NotImplementedException("ticket 016 — monster view binding");
        }

        public void RenderFrom(MatchState state)
        {
            throw new NotImplementedException("ticket 016 — render from replicated sim state");
        }
    }
}
