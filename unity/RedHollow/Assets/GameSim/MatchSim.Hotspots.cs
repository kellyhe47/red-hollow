using System;
using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 003 (T-03) owns this half of <see cref="MatchSim"/>: hotspots, the civilian pool
    /// and the defeat rule. Requirements R-10, R-11, R-12, R-13, R-72; graded by fixtures
    /// G-006 through G-009.
    ///
    /// The shared core — fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>R-13 — the reason code the loss stinger and the post-match screen read.</summary>
        private const string AllCiviliansDead = "all_civilians_dead";

        /// <summary>
        /// R-11 / B-004, B-005. A monster connects with a civilian shelter.
        ///
        /// The civilian count *is* the hotspot's HP (DEC-002 / R-72): a hit kills
        /// ceil(damage / <see cref="SimConfig.DamagePerCivilian"/>) civilians, clamped to the number
        /// actually present so the counter never goes negative (R-11 / G-007) — and the *clamped*
        /// figure is what gets reported, because it is how many people actually died.
        ///
        /// Reaching 0 is terminal for that shelter (R-12 / R-13): nothing here can raise a count, so
        /// an emptied hotspot stays emptied and stops being a valid target for good. Defeat is a
        /// property of the *colony*, not of any one shelter (R-02): it fires only on the hit that
        /// takes the sum across every hotspot to 0, which is why G-009 empties a chapel without
        /// ending the match.
        ///
        /// Sad paths the PRD leaves open, decided here:
        ///  - an unknown <c>TargetId</c> throws, matching how a missing catalog row is handled —
        ///    there is no honest result to return for a shelter that does not exist, and a silent
        ///    no-op would hide a targeting bug that is eating monster attacks;
        ///  - non-positive damage kills nobody. It is the natural end of the clamp rather than an
        ///    error: ceil(-10/10) would otherwise be -1 and *resurrect* a civilian (R-11).
        /// </summary>
        public HotspotAttackResult ApplyHotspotAttack(HotspotAttackRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            BeginCommand();

            if (request.TargetId == null || !State.Hotspots.TryGetValue(request.TargetId, out var hotspot))
            {
                throw new KeyNotFoundException(
                    "no hotspot '" + request.TargetId + "' in this match (R-10); "
                    + "attacker '" + request.AttackerId + "' was aimed at a shelter that does not exist");
            }

            var before = hotspot.Civilians;
            var killed = CiviliansKilledBy(request.Damage, before);
            var after = before - killed;
            hotspot.Civilians = after;

            // RecordChange drops non-deltas, so a hit that killed nobody replicates nothing.
            RecordChange(hotspot.Id, "civilians", before, after);
            Emit("civilians_killed", new Dictionary<string, object>
            {
                { "hotspot_id", hotspot.Id },
                { "count", killed },
            });

            // R-12 / R-13 — a transition, not a level: only the hit that takes a live shelter to 0
            // announces the loss. Hitting an already-empty hotspot kills 0, so it never re-fires.
            if (killed > 0 && after == 0)
            {
                Emit("hotspot_emptied", new Dictionary<string, object>
                {
                    { "hotspot_id", hotspot.Id },
                });
            }

            var totalRemaining = State.TotalCivilians;

            // R-02 / B-005 — colony-wide, and only ever from a kill this hit actually made. The
            // loss moves the match *status*; the phase is a separate field that also reads "combat".
            if (killed > 0 && totalRemaining == 0 && !State.IsOver)
            {
                var statusBefore = State.Status;
                State.Status = MatchStatus.Defeat;
                RecordChange("match", "status", statusBefore, State.Status);
                Emit("match_defeat", new Dictionary<string, object>
                {
                    { "reason", AllCiviliansDead },
                });
            }

            return Finish(new HotspotAttackResult
            {
                HotspotId = hotspot.Id,
                CiviliansKilled = killed,
                CiviliansRemaining = after,
                TotalCiviliansRemaining = totalRemaining,
            });
        }

        /// <summary>
        /// R-11 — ceil(damage / DamagePerCivilian), floored at 0 so a non-positive hit can never add
        /// anyone, and capped at <paramref name="present"/> so the shelter bottoms out at 0 (G-007).
        /// </summary>
        private int CiviliansKilledBy(double damage, int present)
        {
            if (damage <= 0.0)
            {
                return 0;
            }

            var raw = (int)Math.Ceiling(damage / _config.DamagePerCivilian);
            return raw < present ? raw : present;
        }
    }
}
