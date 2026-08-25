using System;
using UnityEngine;

namespace RedHollow.Game.Host
{
    /// <summary>
    /// The scene's only tie to the match: a component that owns a <see cref="HostLoop"/> and pumps
    /// it from Unity's fixed step. It holds no rule, computes nothing and writes no sim state — see
    /// T10_HostLoopTests' IL invariant, which enforces that mechanically for every MonoBehaviour in
    /// this assembly rather than trusting review.
    ///
    /// Scene wiring, camera, input and visuals are ticket 016; this exists at 010 so the
    /// architecture invariant has something real to scan.
    ///
    /// SHAPE ONLY (ticket 010, TDD stub) — implementation belongs to the implementing agent.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchHostBehaviour : MonoBehaviour
    {
        /// <summary>Hands the component the loop built by the (plain C#) match bootstrapper.</summary>
        public void Bind(HostLoop loop) => throw NotYet(nameof(Bind));

        private void FixedUpdate() => throw NotYet(nameof(FixedUpdate));

        private static NotImplementedException NotYet(string member) =>
            new NotImplementedException("T-10 not implemented: MatchHostBehaviour." + member);
    }
}
