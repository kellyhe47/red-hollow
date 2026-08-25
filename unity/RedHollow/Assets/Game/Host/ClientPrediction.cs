using System;
using RedHollow.Sim;

namespace RedHollow.Game.Host
{
    /// <summary>
    /// R-52, remote half — a client renders entities it does not own by interpolating between the
    /// replicated samples it has, rather than snapping to each one as it arrives.
    ///
    /// The curve is not specified by the PRD and is not pinned by ticket 010: only that a sample
    /// between two positions lies between them, and that the endpoints are the samples themselves.
    /// Ticket 011 exercises this against real replication.
    ///
    /// SHAPE ONLY (ticket 010, TDD stub) — implementation belongs to the implementing agent.
    /// </summary>
    public sealed class RemoteEntityInterpolator
    {
        /// <summary><paramref name="t"/> is 0 at <paramref name="from"/> and 1 at <paramref name="to"/>.</summary>
        public Vec2 Sample(Vec2 from, Vec2 to, double t) =>
            throw new NotImplementedException("T-10 not implemented: RemoteEntityInterpolator.Sample");
    }

    /// <summary>
    /// R-52, own-hero half — the local player's movement is applied immediately (prediction) and
    /// then pulled back onto whatever the host says is true (reconciliation), because the host is
    /// authoritative (R-51) and the client is not.
    ///
    /// Neither the smoothing rate nor the error budget is specified by the PRD, so ticket 010 pins
    /// only the direction: reconciling repeatedly against a fixed authoritative position must never
    /// increase the error and must converge on it. Ticket 011 exercises it against real replication.
    ///
    /// SHAPE ONLY (ticket 010, TDD stub) — implementation belongs to the implementing agent.
    /// </summary>
    public sealed class LocalHeroPrediction
    {
        public LocalHeroPrediction(Vec2 startingPosition)
        {
        }

        /// <summary>Where the local client is currently drawing its own hero.</summary>
        public Vec2 Predicted => throw NotYet(nameof(Predicted));

        /// <summary>Applies a locally predicted move immediately, ahead of any host confirmation.</summary>
        public void Predict(Vec2 delta) => throw NotYet(nameof(Predict));

        /// <summary>Pulls the prediction back towards the host's authoritative position.</summary>
        public void Reconcile(Vec2 authoritative) => throw NotYet(nameof(Reconcile));

        private static NotImplementedException NotYet(string member) =>
            new NotImplementedException("T-10 not implemented: LocalHeroPrediction." + member);
    }
}
