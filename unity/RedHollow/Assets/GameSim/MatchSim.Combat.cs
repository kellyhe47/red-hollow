using System;
using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// Ticket 015 (T-15) owns this half of <see cref="MatchSim"/>: the monster attack *cadence*
    /// half of R-18 — "monsters attack once per second". Grades no fixture, which is exactly why
    /// the gap survived to a requirement walk: <see cref="SimConfig.MonsterAttackIntervalSeconds"/>
    /// has been declared since ticket 001 and nothing in the sim has ever read it, so the host's
    /// combat loop could call <see cref="ApplyHotspotAttack"/>, <see cref="ApplyHeroDamage"/> or
    /// <see cref="ApplyPlaceableDamage"/> on every one of its 60 frames a second and land 60 hits.
    ///
    /// R-18's other half — NavMesh movement, and the Burrower path that ignores barricade
    /// obstacles — is not here. The pathing is Unity shell work, and the Burrower's barricade
    /// carve-out already lives in ticket 002 at the targeting level
    /// (<see cref="Monster.IgnoresBarricadesAndHeroes"/>, G-005).
    ///
    /// <b>Why a separate gate rather than a refusal folded into the damage operations.</b>
    /// Six golden fixtures call a damage entry point directly, with no prior attack and no cadence
    /// state: G-006/007/008/009 on <see cref="ApplyHotspotAttack"/> and G-020/021 on
    /// <see cref="ApplyHeroDamage"/>. Each pins an exact `result`, `state_changes` and
    /// `emitted_events` for what is that monster's *first* hit in the scenario. A gate inside those
    /// operations would have to either refuse a first attack (breaking all six) or record the
    /// cadence stamp as a delta (breaking all six a different way). Keeping the question in its own
    /// operation leaves the three damage operations byte-identical: the host asks here first, and
    /// only calls the damage operation when the answer is yes.
    ///
    /// The shared core — fields, constructor and recording plumbing — lives in MatchSim.cs.
    /// </summary>
    public sealed partial class MatchSim
    {
        /// <summary>
        /// R-18 — sim time of each monster's most recent *permitted* attack, keyed by monster id.
        ///
        /// Kept here rather than on <see cref="Monster"/> because it is bookkeeping, not world
        /// state: nothing replicates it, no fixture observes it, and putting it on the entity would
        /// invite a future ticket to serialise it into a `state_changes` row that G-006..G-009 and
        /// G-020/021 would fail on. Per id, the way <see cref="Hero.CooldownReadyAt"/> is per hero
        /// and per slot (R-32) — one Shambler's swing must never silence the whole wave.
        ///
        /// An id absent from this map means "has never attacked", which is *not* the same as
        /// "attacked at time 0": the six fixtures above are first hits taken at clock 0, so a map
        /// that defaulted to 0.0 and then asked `now &lt; 0 + interval` would refuse every one of
        /// them. Entries are never removed — dead monsters stay in
        /// <see cref="MatchState.Monsters"/> too (they are flagged, not deleted), so this grows
        /// with the match exactly as the world does.
        /// </summary>
        private readonly Dictionary<string, double> _lastMonsterAttackAt =
            new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>
        /// R-18. "May this monster land a hit right now?" — and, when the answer is yes, the claim
        /// that starts its next cooldown.
        ///
        /// Ask-and-claim in one call rather than a pure predicate plus a separate "note that it
        /// attacked": two calls could be desynchronised by a host that forgot the second one, and
        /// the whole point of the operation is that the host cannot land more than one hit per
        /// <see cref="SimConfig.MonsterAttackIntervalSeconds"/> however often it asks.
        ///
        /// A monster that has never attacked is permitted immediately, at any clock reading
        /// including 0 — that is the property the six fixtures above depend on.
        ///
        /// The deadline is inclusive, the convention G-019 set for every boundary in this sim and
        /// that tickets 004, 007 and 008 follow: an attack at exactly last + interval lands. The
        /// interval is re-read from config on every query rather than baked into a stored deadline,
        /// so a shell that retunes it mid-match (R-16) takes effect on the next swing.
        ///
        /// Per monster: one monster's cooldown must never gate another's, the same way
        /// <see cref="Hero.CooldownReadyAt"/> is per hero and per slot (R-32).
        ///
        /// Sad paths R-18 leaves open, decided here as a *refusal* rather than a throw: this is a
        /// permission question, and "may this monster swing?" has an honest boolean answer for a
        /// monster that is unknown, unnamed or already a corpse — no. Throwing would also make the
        /// host's per-frame loop responsible for catching on the frame a monster dies, which is the
        /// frame it is most likely to still be asked about. A refusal writes nothing, so a bad id
        /// cannot disturb the cadence of a living monster standing beside it.
        ///
        /// Deliberately not a command: it calls no <c>BeginCommand()</c> because it records no
        /// deltas, no events and no external calls. Doing so would clear
        /// <see cref="LastObservation"/>, and a host loop that gates monster B right after applying
        /// monster A's damage would destroy A's observation before the netcode replicated it
        /// (R-51). The gate answers a question; it never produces one of the observations the
        /// fixtures grade.
        /// </summary>
        public bool TryMonsterAttack(string monsterId)
        {
            if (monsterId == null
                || !State.Monsters.TryGetValue(monsterId, out var monster)
                || !monster.Alive)
            {
                return false;
            }

            var now = _clock.ElapsedSeconds;

            // Absent = never attacked, so the opening swing is never gated (G-006..G-009, G-020/021).
            // Inclusive deadline: strict `<` refuses only *before* last + interval, which is what
            // G-019 pins for every deadline in this sim — `<=` here would drop one hit per second.
            if (_lastMonsterAttackAt.TryGetValue(monsterId, out var lastAttackAt)
                && now < lastAttackAt + _config.MonsterAttackIntervalSeconds)
            {
                return false;
            }

            // Ask-and-claim: a yes consumes the swing, so asking twice on the same frame cannot
            // land twice — that is the whole rate limit, and it survives a host that forgets to
            // tell the sim what it did with the permission.
            _lastMonsterAttackAt[monsterId] = now;
            return true;
        }
    }
}
