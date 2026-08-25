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

        /// <summary>
        /// Ties this component to one replicated monster id and to the visual it wears. The visual
        /// is parented here so the two share a lifetime — a view destroyed on despawn must not leave
        /// its stand-in standing in the colony.
        /// </summary>
        public void Bind(string monsterId, VisualHandle visual)
        {
            MonsterId = monsterId;
            Visual = visual;
            ViewRig.Attach(transform, visual);
        }

        /// <summary>
        /// R-51 — copy this frame's replicated values out of the world. Read-only by construction:
        /// every assignment below writes a property of this component, never a field of the sim.
        ///
        /// An unknown id is a no-op rather than an error. Replication and view lifetime are ticket
        /// 011's to synchronise; until then a view that outlives its monster by a frame must keep
        /// showing its last replicated values rather than throw in the middle of a session.
        /// </summary>
        public void RenderFrom(MatchState state)
        {
            if (state == null || string.IsNullOrEmpty(MonsterId))
            {
                return;
            }

            Monster monster;
            if (!state.Monsters.TryGetValue(MonsterId, out monster) || monster == null)
            {
                return;
            }

            // Mirrored, not derived: DisplayedAlive is the sim's ruling even when it disagrees with
            // the HP beside it, because only MatchSim decides death (R-51).
            DisplayedHp = monster.Hp;
            DisplayedAlive = monster.Alive;
            WorldPosition = SimSpace.ToWorld(monster.Pos);

            transform.position = WorldPosition;
            ViewRig.SetVisible(Visual, DisplayedAlive);
        }
    }
}
