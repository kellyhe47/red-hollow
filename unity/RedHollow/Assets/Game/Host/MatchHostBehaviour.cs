using UnityEngine;

namespace RedHollow.Game.Host
{
    /// <summary>
    /// The scene's only tie to the match: a component that owns a <see cref="HostLoop"/> and pumps
    /// it from Unity's fixed step. It holds no rule, computes nothing and writes no sim state — see
    /// T10_HostLoopTests' IL invariant, which enforces that mechanically for every MonoBehaviour in
    /// this assembly rather than trusting review.
    ///
    /// R-51 in its most literal form: the whole engine-facing surface of the match is "call
    /// <see cref="HostLoop.Step"/> with the frame's delta". Anything more — spawning, targeting,
    /// economy, authoring a <see cref="RedHollow.Sim.Monster"/> — belongs in a plain C# class that
    /// this component delegates to, because an object initializer alone is enough to put a rule in
    /// a MonoBehaviour.
    ///
    /// Scene wiring, camera, input and visuals are ticket 016; this exists at 010 so the
    /// architecture invariant has something real to scan.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchHostBehaviour : MonoBehaviour
    {
        private HostLoop _loop;

        /// <summary>Hands the component the loop built by the (plain C#) match bootstrapper.</summary>
        public void Bind(HostLoop loop)
        {
            _loop = loop;
        }

        /// <summary>
        /// R-51 — one fixed frame is one host step, and the delta comes from the engine rather than
        /// from a constant here so retuning the fixed timestep retunes the sim with it. Unbound the
        /// component is inert: a host that has not been given a match has no match to advance.
        /// </summary>
        private void FixedUpdate()
        {
            if (_loop == null)
            {
                return;
            }

            _loop.Step(Time.fixedDeltaTime);
        }
    }
}
