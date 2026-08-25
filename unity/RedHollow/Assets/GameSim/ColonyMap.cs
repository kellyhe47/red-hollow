using System;
using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// One shelter as *map data*: where it stands and how many civilians start inside it.
    /// Kept separate from the live <see cref="Hotspot"/> entity because R-10 is authorable config
    /// (the shell overrides it from a ScriptableObject) while the entity carries mutable match
    /// state that a rematch resets (R-07).
    ///
    /// STUB — ticket T-03 owns populating this. Shape only.
    /// </summary>
    public sealed class HotspotSpec
    {
        public string Id;
        public Vec2 Pos;

        /// <summary>R-11 / R-72 — civilians are a count, never entities.</summary>
        public int Civilians;
    }

    /// <summary>
    /// The v1 colony map as data (R-10): 3 hotspots (Saloon 8, Chapel 6, Homestead 6 = 20
    /// civilians), 4 breach entry tunnels at the cavern edges (R-14), and the single team spawn
    /// near the map centre where heroes enter and respawn (R-33).
    ///
    /// STUB — ticket T-03 owns populating this. Shape only; no data and no behaviour here yet.
    /// </summary>
    public sealed class ColonyMap
    {
        /// <summary>R-10 — Saloon 8, Chapel 6, Homestead 6.</summary>
        public readonly List<HotspotSpec> Hotspots = new List<HotspotSpec>();

        /// <summary>R-10 / R-14 — the 4 breach tunnels; the wave table picks which activate (R-19).</summary>
        public readonly List<Vec2> EntryTunnels = new List<Vec2>();

        /// <summary>R-10 / R-33 — one team spawn point, near the map centre.</summary>
        public Vec2 TeamSpawn;

        /// <summary>The one v1 map. Defaults live here so balance edits never touch rule code.</summary>
        public static ColonyMap V1() =>
            throw new NotImplementedException(
                "T-03 not implemented: the v1 colony map (3 hotspots 8/6/6, 4 entry tunnels, 1 team spawn)");

        /// <summary>Opening live state for a match on this map — the config-to-state bridge.</summary>
        public MatchState CreateMatchState() =>
            throw new NotImplementedException(
                "T-03 not implemented: building MatchState from colony map config");
    }
}
