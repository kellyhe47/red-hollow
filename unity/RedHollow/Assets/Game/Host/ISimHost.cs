using RedHollow.Sim;

namespace RedHollow.Game.Host
{
    /// <summary>
    /// The shell's seam onto the host-authoritative sim (R-51). Everything the fixed-step host loop
    /// is allowed to do to a match goes through here, and nothing else in the shell may reach past
    /// it to touch <see cref="MatchState"/> directly.
    ///
    /// Why an interface at all when <see cref="MatchSim"/> is a perfectly good concrete class:
    /// <see cref="MatchSim"/> is sealed, so the only way to observe *which* sim operations one host
    /// step made — the thing R-50/R-52's spine actually has to be correct about — is to put a seam
    /// in front of it. <see cref="MatchSimHost"/> is the one production implementation; the tests
    /// bind a recording fake.
    ///
    /// The surface is deliberately narrow: the five ticks the sim cannot schedule for itself
    /// (R-03, R-23, R-31, R-33, R-35), the R-18 attack gate, and the three damage commands that
    /// gate guards. Per-entity commands the host also drives (SelectTarget, TurretTick, hero
    /// attacks, abilities, economy) widen <see cref="IMatchSimHost"/> instead of this interface —
    /// adding a member here would break ticket 010's locked recording fake.
    /// </summary>
    public interface ISimHost
    {
        /// <summary>The world the commands below mutate. Read-only as far as the shell is concerned.</summary>
        MatchState State { get; }

        /// <summary>R-16 — every tunable the shell reads instead of hardcoding a rule constant.</summary>
        SimConfig Config { get; }

        /// <summary>R-51 — sim time. The host advances it; the sim never reads a wall clock.</summary>
        IClock Clock { get; }

        /// <summary>What the most recent command produced. This is what netcode replicates from.</summary>
        SimObservation LastObservation { get; }

        /// <summary>
        /// R-51 — advance sim time by one host step. The sim schedules nothing for itself, so
        /// every deadline in it (planning, respawn, status expiry, regen, med-station aura) only
        /// moves because the host moved this first.
        /// </summary>
        void AdvanceClock(double deltaSeconds);

        /// <summary>R-03 — without this, planning never ends unless every connected player readies.</summary>
        void TickPlanningTimer();

        /// <summary>R-31 — without this, lasso slows and Bulwark never expire.</summary>
        StatusTickResult TickStatusEffects();

        /// <summary>R-35 — without this, there is no out-of-combat healing.</summary>
        void TickHeroRegen();

        /// <summary>R-33 — without this, dead heroes never come back.</summary>
        void TickHeroRespawns();

        /// <summary>R-23 — without this, Med Stations heal nobody.</summary>
        void TickMedStations();

        /// <summary>
        /// R-18 — "may this monster land a hit right now?". Advisory: the sim cannot force the host
        /// to ask, which is exactly why the host loop's use of it is a locked test rather than a
        /// convention. Must be asked BEFORE the damage command, never after.
        /// </summary>
        bool TryMonsterAttack(string monsterId);

        /// <summary>R-11 — a monster connects with a civilian shelter.</summary>
        HotspotAttackResult ApplyHotspotAttack(HotspotAttackRequest request);

        /// <summary>R-33 — a monster connects with a hero.</summary>
        HeroDamageResult ApplyHeroDamage(HeroDamageRequest request);

        /// <summary>R-16 / R-23 — a monster connects with a barricade or other placeable.</summary>
        PlaceableDamageResult ApplyPlaceableDamage(PlaceableDamageRequest request);
    }
}
