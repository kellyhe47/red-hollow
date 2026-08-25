using System.Collections.Generic;

namespace RedHollow.Sim
{
    /// <summary>
    /// One shelter as *map data*: where it stands and how many civilians start inside it.
    /// Kept separate from the live <see cref="Hotspot"/> entity because R-10 is authorable config
    /// (the shell overrides it from a ScriptableObject) while the entity carries mutable match
    /// state that a rematch resets (R-07).
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
    /// Everything here is mutable *instance* data rather than constants, for the same reason the
    /// stat catalogs are (DEC-RUN-1): the shell authors the layout, so rule code must never be able
    /// to assume a particular number. <see cref="V1"/> supplies the shipped defaults; a caller is
    /// free to edit the returned instance, and every derived figure follows from the edit.
    /// </summary>
    public sealed class ColonyMap
    {
        /// <summary>R-10 — Saloon 8, Chapel 6, Homestead 6.</summary>
        public readonly List<HotspotSpec> Hotspots = new List<HotspotSpec>();

        /// <summary>R-10 / R-14 — the 4 breach tunnels; the wave table picks which activate (R-19).</summary>
        public readonly List<Vec2> EntryTunnels = new List<Vec2>();

        /// <summary>R-10 / R-33 — one team spawn point, near the map centre.</summary>
        public Vec2 TeamSpawn;

        /// <summary>
        /// The one v1 map. Defaults live here so balance edits never touch rule code.
        ///
        /// The civilian counts are the contract (R-10 / R-02: 8 + 6 + 6 = 20 is a match's whole loss
        /// budget). The coordinates are layout taste — no fixture pins them — and describe a cavern
        /// roughly 60 units across: the three shelters ring the centre, the four tunnels breach the
        /// edges, and the team spawn sits at the middle so every shelter is a comparable run away.
        /// </summary>
        public static ColonyMap V1()
        {
            var map = new ColonyMap();

            map.Hotspots.Add(new HotspotSpec { Id = "hs_saloon", Pos = new Vec2(-12.0, 6.0), Civilians = 8 });
            map.Hotspots.Add(new HotspotSpec { Id = "hs_chapel", Pos = new Vec2(11.0, 9.0), Civilians = 6 });
            map.Hotspots.Add(new HotspotSpec { Id = "hs_homestead", Pos = new Vec2(2.0, -13.0), Civilians = 6 });

            // R-14 — four breaches, one per cavern edge, so no shelter is safe by geometry alone.
            map.EntryTunnels.Add(new Vec2(-30.0, 0.0));
            map.EntryTunnels.Add(new Vec2(30.0, 0.0));
            map.EntryTunnels.Add(new Vec2(0.0, 30.0));
            map.EntryTunnels.Add(new Vec2(0.0, -30.0));

            // R-33 — heroes enter at wave 1 and respawn here; mirrors SimConfig.RespawnPoint.
            map.TeamSpawn = new Vec2(0.0, 0.0);

            return map;
        }

        /// <summary>
        /// Opening live state for a match on this map — the config-to-state bridge. Each
        /// <see cref="HotspotSpec"/> becomes one live <see cref="Hotspot"/> carrying its starting
        /// civilian count, which R-11 then spends as the shelter's HP. The colony total is never
        /// copied across: <see cref="MatchState.TotalCivilians"/> derives it from the hotspots
        /// (R-02 / R-72), so the two can never drift apart.
        /// </summary>
        public MatchState CreateMatchState()
        {
            var state = new MatchState();
            foreach (var spec in Hotspots)
            {
                state.Hotspots[spec.Id] = new Hotspot
                {
                    Id = spec.Id,
                    Pos = spec.Pos,
                    Civilians = spec.Civilians,
                };
            }

            return state;
        }
    }
}
