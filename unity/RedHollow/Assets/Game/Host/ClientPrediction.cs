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
    /// </summary>
    public sealed class RemoteEntityInterpolator
    {
        /// <summary>
        /// <paramref name="t"/> is 0 at <paramref name="from"/> and 1 at <paramref name="to"/>.
        ///
        /// Straight linear blend, deliberately: the PRD names no easing, and anything with overshoot
        /// would put a remote entity somewhere neither sample ever reported. <paramref name="t"/> is
        /// clamped because a late packet makes the caller's elapsed/interval ratio exceed 1, and
        /// extrapolating off the end of the segment is how remotes visibly slide through walls.
        ///
        /// Written as <c>from + (to - from) * t</c> so the endpoints come back bit-exact rather
        /// than merely close: presentation may interpolate, but it must not invent a position the
        /// host never sent (R-51).
        /// </summary>
        public Vec2 Sample(Vec2 from, Vec2 to, double t)
        {
            var clamped = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);

            return new Vec2(
                from.X + ((to.X - from.X) * clamped),
                from.Y + ((to.Y - from.Y) * clamped));
        }
    }

    /// <summary>
    /// R-52, own-hero half — the local player's movement is applied immediately (prediction) and
    /// then pulled back onto whatever the host says is true (reconciliation), because the host is
    /// authoritative (R-51) and the client is not.
    ///
    /// Neither the smoothing rate nor the error budget is specified by the PRD, so ticket 010 pins
    /// only the direction: reconciling repeatedly against a fixed authoritative position must never
    /// increase the error and must converge on it. Ticket 011 exercises it against real replication.
    /// </summary>
    public sealed class LocalHeroPrediction
    {
        /// <summary>
        /// The fraction of the remaining error each reconciliation removes. Not a spec number — the
        /// PRD states no smoothing rate — so it is named here rather than hidden in an expression,
        /// and ticket 011 is expected to replace it with one measured against real latency. Any
        /// value in (0, 1] satisfies R-52's direction; this one blends rather than snaps so a
        /// correction reads as movement instead of a teleport.
        /// </summary>
        private const double ReconciliationBlend = 0.25;

        private Vec2 _predicted;

        public LocalHeroPrediction(Vec2 startingPosition)
        {
            _predicted = startingPosition;
        }

        /// <summary>Where the local client is currently drawing its own hero.</summary>
        public Vec2 Predicted => _predicted;

        /// <summary>
        /// Applies a locally predicted move immediately, ahead of any host confirmation. This is the
        /// whole point of R-52's own-hero half: input feels instant because the client does not wait
        /// for the round trip, and is corrected afterwards if it guessed wrong.
        /// </summary>
        public void Predict(Vec2 delta)
        {
            _predicted = new Vec2(_predicted.X + delta.X, _predicted.Y + delta.Y);
        }

        /// <summary>
        /// Pulls the prediction back towards the host's authoritative position.
        ///
        /// Strictly contracting towards <paramref name="authoritative"/>: each call removes a fixed
        /// fraction of the remaining error, so repeated reconciliation against a fixed host position
        /// converges and can never move the hero further from it. A blend that overshot — or one
        /// that mixed in more prediction — would let the client drift away from the host while
        /// appearing to correct (R-51).
        /// </summary>
        public void Reconcile(Vec2 authoritative)
        {
            _predicted = new Vec2(
                _predicted.X + ((authoritative.X - _predicted.X) * ReconciliationBlend),
                _predicted.Y + ((authoritative.Y - _predicted.Y) * ReconciliationBlend));
        }
    }
}
