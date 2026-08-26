using RedHollow.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Runtime 3D blockout for the Lantern Deep cavern: a terraformed Mars underground
    /// colony (Lykos / seed-env), not a western town dropped in a cave.
    ///
    /// Mix: ~70% Martian habitat (Quaternius Sci-Fi kit modules retextured with Lykos
    /// maps — stacked walls/floors/roofs/doors/columns) and ~30% western as accents
    /// only (wood trim, brass, amber lanterns). 2D facade cards are retired. Silhouette
    /// stays industrial-Martian. Presentation only — no sim writes.
    /// </summary>
    public static class CavernBlockout
    {
        public const float WallHeight = 110f;
        public const float CeilingHeight = 96f;
        public const float WallThickness = 18f;
        // Tight enough that the N/S inner faces sit inside a 62° ortho frustum
        // (InnerMargin 8 parked them past the top/bottom of the Game view).
        public const float InnerMargin = 3f;

        public const float HabHeightMin = 9.5f;
        public const float HabHeightMax = 28f;

        /// <summary>Top of the raised colony deck plates. Units plant here, cave bed stays at 0.</summary>
        public const float DeckThickness = 0.55f;
        public const float DeckSurface = 0.55f;

        private static readonly Color Rock = new Color(0.42f, 0.26f, 0.14f);
        private static readonly Color RockDark = new Color(0.22f, 0.12f, 0.07f);
        private static readonly Color Metal = new Color(0.48f, 0.30f, 0.18f);
        private static readonly Color MetalDark = new Color(0.22f, 0.14f, 0.09f);
        private static readonly Color Roof = new Color(0.46f, 0.28f, 0.16f);
        private static readonly Color Brass = new Color(0.72f, 0.48f, 0.18f);
        private static readonly Color WoodTrim = new Color(0.40f, 0.26f, 0.14f);
        private static readonly Color CeilingDark = new Color(0.06f, 0.035f, 0.02f);
        private static readonly Color AmberGlow = new Color(1.0f, 0.72f, 0.32f);
        private static readonly Color LostTint = new Color(0.18f, 0.10f, 0.06f);
        private static readonly Color LiveTint = Color.white;
        // Lit albedo tints: lanterns do the shading. Roof stays darker than walls so
        // the 62° camera still reads a separate roof plane — but not so dark that
        // ambient * albedo collapses to void-black on the +Y face.
        private static readonly Color RoofTint = new Color(0.40f, 0.26f, 0.15f);
        private static readonly Color HabWallTint = new Color(0.48f, 0.33f, 0.20f);
        private static readonly Color CavernTint = new Color(0.62f, 0.38f, 0.20f);
        private static readonly Color RockTint = new Color(0.38f, 0.22f, 0.12f);

        /// <summary>
        /// Raise the cavern shell (wall ring + ceiling) around the play square. Camera at
        /// y=40 must sit INSIDE. Returns the ceiling, which is the sky (no skybox).
        /// </summary>
        public static GameObject BuildShell(Transform root, Bounds playArea)
        {
            var cavern = new GameObject("Cavern");
            cavern.transform.SetParent(root, false);
            cavern.transform.position = new Vector3(
                playArea.center.x, SimSpace.GroundHeight, playArea.center.z);

            var inner = Mathf.Max(playArea.extents.x, playArea.extents.z) + InnerMargin;
            var wallMat = WallMaterial();
            var rockMat = RockMaterial();

            // Four thick slabs: inner faces sit just past the playable square so a tilted
            // y-down camera still sees wall thickness at the colony edge.
            Box(cavern.transform, "Wall_North",
                new Vector3(0f, WallHeight * 0.5f, inner + WallThickness * 0.5f),
                new Vector3((inner + WallThickness) * 2f, WallHeight, WallThickness),
                wallMat);
            Box(cavern.transform, "Wall_South",
                new Vector3(0f, WallHeight * 0.5f, -(inner + WallThickness * 0.5f)),
                new Vector3((inner + WallThickness) * 2f, WallHeight, WallThickness),
                wallMat);
            Box(cavern.transform, "Wall_East",
                new Vector3(inner + WallThickness * 0.5f, WallHeight * 0.5f, 0f),
                new Vector3(WallThickness, WallHeight, inner * 2f),
                wallMat);
            Box(cavern.transform, "Wall_West",
                new Vector3(-(inner + WallThickness * 0.5f), WallHeight * 0.5f, 0f),
                new Vector3(WallThickness, WallHeight, inner * 2f),
                wallMat);

            // Irregular carved-rock terrace in front of the perfect box so the cavern reads
            // excavated, not like four warehouse walls.
            var terrace = new[]
            {
                new Vector3(0f, 7f, inner - 4f),
                new Vector3(14f, 11f, inner - 5.5f),
                new Vector3(-16f, 9f, inner - 4.5f),
                new Vector3(0f, 6f, -(inner - 4f)),
                new Vector3(-12f, 10f, -(inner - 5f)),
                new Vector3(18f, 8f, -(inner - 4.5f)),
                new Vector3(inner - 4.5f, 12f, 8f),
                new Vector3(inner - 5f, 7f, -14f),
                new Vector3(-(inner - 4.5f), 9f, 10f),
                new Vector3(-(inner - 5.5f), 13f, -8f),
            };
            for (var i = 0; i < terrace.Length; i++)
            {
                var p = terrace[i];
                Box(cavern.transform, "RockShelf_" + i,
                    new Vector3(p.x, p.y * 0.5f, p.z),
                    new Vector3(9f + (i % 4), p.y, 6f + (i % 3)),
                    rockMat);
            }

            var buttressH = new[] { 42f, 58f, 36f, 70f, 48f, 64f, 40f, 54f };
            var corners = new[]
            {
                new Vector3(inner - 2f, 0f, inner - 2f),
                new Vector3(-(inner - 2f), 0f, inner - 2f),
                new Vector3(inner - 2f, 0f, -(inner - 2f)),
                new Vector3(-(inner - 2f), 0f, -(inner - 2f)),
                new Vector3(inner * 0.35f, 0f, inner - 1.5f),
                new Vector3(-(inner * 0.4f), 0f, -(inner - 1.5f)),
                new Vector3(inner - 1.5f, 0f, inner * 0.3f),
                new Vector3(-(inner - 1.5f), 0f, -(inner * 0.35f)),
            };
            for (var i = 0; i < corners.Length; i++)
            {
                var h = buttressH[i];
                Box(cavern.transform, "RockCorner_" + i,
                    new Vector3(corners[i].x, h * 0.5f, corners[i].z),
                    new Vector3(10f + (i % 4), h, 9f + ((3 - i) % 4)),
                    rockMat);
            }

            Box(cavern.transform, "Rubble_N",
                new Vector3(0f, 2.2f, inner - 3.5f), new Vector3(inner * 1.4f, 4.4f, 7f), rockMat);
            Box(cavern.transform, "Rubble_S",
                new Vector3(0f, 1.8f, -(inner - 3.5f)), new Vector3(inner * 1.2f, 3.6f, 6.5f), rockMat);

            // In-frustum cliff faces. The 110-unit box walls sit at ±inner; with InnerMargin 3
            // that is z≈±33, which a 62° camera actually sees. Split north/east/west around
            // the four tunnel mouths so a breach still reads as a hole in the rock.
            var cliff = wallMat;
            Box(cavern.transform, "Cliff_North_W",
                new Vector3(-(inner * 0.58f), 38f, inner - 0.6f),
                new Vector3(inner * 0.82f, 76f, 7.5f), cliff);
            Box(cavern.transform, "Cliff_North_E",
                new Vector3(inner * 0.58f, 36f, inner - 0.8f),
                new Vector3(inner * 0.78f, 72f, 7.0f), cliff);
            Box(cavern.transform, "Cliff_East_N",
                new Vector3(inner - 0.6f, 34f, inner * 0.55f),
                new Vector3(7.2f, 68f, inner * 0.72f), cliff);
            Box(cavern.transform, "Cliff_East_S",
                new Vector3(inner - 0.8f, 28f, -(inner * 0.52f)),
                new Vector3(6.8f, 56f, inner * 0.68f), cliff);
            Box(cavern.transform, "Cliff_West_N",
                new Vector3(-(inner - 0.6f), 32f, inner * 0.5f),
                new Vector3(7.0f, 64f, inner * 0.7f), cliff);
            Box(cavern.transform, "Cliff_West_S",
                new Vector3(-(inner - 0.9f), 26f, -(inner * 0.5f)),
                new Vector3(6.6f, 52f, inner * 0.66f), cliff);
            // South of the camera only low geometry stays in front of the lens; a short
            // ridge at the bottom of the frame is the readable "near wall". Split around
            // the south tunnel mouth.
            Box(cavern.transform, "Cliff_South_W",
                new Vector3(-(inner * 0.6f), 6.5f, -(inner - 1.2f)),
                new Vector3(inner * 0.85f, 13f, 6.5f), rockMat);
            Box(cavern.transform, "Cliff_South_E",
                new Vector3(inner * 0.6f, 6.0f, -(inner - 1.4f)),
                new Vector3(inner * 0.8f, 12f, 6.0f), rockMat);

            // Stalactites hanging out of the haze so the ceiling is a cavern, not a lid.
            for (var s = 0; s < 10; s++)
            {
                var ang = s * 0.62f;
                var r = inner * (0.35f + (s % 3) * 0.15f);
                var h = 10f + (s % 5) * 4f;
                Box(cavern.transform, "Stalactite_" + s,
                    new Vector3(Mathf.Cos(ang) * r, CeilingHeight - h * 0.5f, Mathf.Sin(ang) * r),
                    new Vector3(2.2f + (s % 3) * 0.6f, h, 2.2f + ((s + 1) % 3) * 0.5f),
                    rockMat);
            }

            var ceiling = Box(cavern.transform, "Ceiling",
                new Vector3(0f, CeilingHeight, 0f),
                new Vector3((inner + WallThickness) * 2f, 1.2f, (inner + WallThickness) * 2f),
                ViewLook.Unlit(CeilingDark));
            ceiling.name = "CavernDome";

            return ceiling;
        }

        /// <summary>
        /// Replace a hotspot's flat marker disc with a Mars habitat kitbashed from
        /// Quaternius Sci-Fi modules (walls, floors, roofs, doors, columns), retextured
        /// with Lykos URP Lit maps. Courtyard on the sim point stays open for pathing.
        /// The marker root and name stay so tests that search Hotspot_* still find it.
        /// </summary>
        public static void DressHotspot(GameObject marker, string hotspotId)
        {
            DressHotspot(marker, hotspotId, 0);
        }

        public static void DressHotspot(GameObject marker, string hotspotId, int civilians)
        {
            if (marker == null)
            {
                return;
            }

            HidePlaceholders(marker);

            var brass = ViewLook.Unlit(Brass);
            var hab = new GameObject("Habitat");
            hab.transform.SetParent(marker.transform, false);

            var deck = DeckingMaterial();
            Box(hab.transform, "Deck",
                new Vector3(0f, DeckThickness * 0.5f, 0f),
                new Vector3(SciFiKit.Grid, DeckThickness, SciFiKit.Grid),
                deck, castShadows: true);
            SciFiKit.Place(hab.transform, "Courtyard", SciFiKit.FloorSquares,
                new Vector3(0f, DeckSurface, 0f), Quaternion.identity, deck, castShadows: true);

            var yaw = hotspotId == "hs_chapel" ? 12f
                : hotspotId == "hs_homestead" ? -10f
                : 6f;

            var g = SciFiKit.Grid;
            // Courtyard on the sim point stays open. Wings sit a full cell off the
            // marker so pathing through the hotspot origin is clear. Skip a south
            // wing so the follow-cam reads wall SIDES instead of a near-plane slab.
            var wings = new[]
            {
                new Vector3(g * 1.15f, 0f, 0.4f),
                new Vector3(-g * 1.15f, 0f, 0.2f),
                new Vector3(0.2f, 0f, g * 1.15f),
            };
            var stories = new[] { 3, 2, 3 };
            for (var i = 0; i < wings.Length; i++)
            {
                RaiseKitHab(hab.transform, "Wing_" + i, wings[i], stories[i], yaw);
            }

            RaiseStreetLamp(hab.transform, "LanternMast", 4.2f, 4.2f, DarkMetalMaterial(), brass);

            if (civilians > 0)
            {
                RaiseCivilianHuddle(hab.transform, civilians, 2.4f);
            }
        }

        /// <summary>
        /// Retired. Camera-facing Unlit facade cards flattened the south volume into a
        /// 2D stamp. Kit wall modules carry the front; this stays a named no-op.
        /// </summary>
        private static void PinFacade(Transform hab, string hotspotId, Quaternion rot)
        {
            if (hab == null || string.IsNullOrEmpty(hotspotId))
            {
                return;
            }

            _ = rot;
        }

        /// <summary>
        /// Visual-only packed colony: kitbashed hab modules, metal decking, lantern masts
        /// and a lift-shaft landmark. Does not rename Ground / TeamSpawn / Hotspot_* roots.
        /// Leaves a courtyard around spawn so the hero is not buried in geometry.
        /// </summary>
        public static void ScatterSettlement(Transform root, ColonyMap map, Bounds playArea)
        {
            if (root == null || map == null)
            {
                return;
            }

            var settlement = new GameObject("Settlement");
            settlement.transform.SetParent(root, false);

            var dark = DarkMetalMaterial();
            var brass = ViewLook.Unlit(Brass);

            // DressHotspot already raises coherent wings around each shelter. Do NOT
            // dump extra kit cells on those courtyards — that was the floating-shard
            // scatter. Street habs sit outside the spawn follow corridor.
            var spawn = SimSpace.ToWorld(map.TeamSpawn);

            // Inner faces ~|x| 5 so SOUTH windows/eaves sit inside the spawn
            // follow-cam (FOV 38 / eye 16). A 20u street parked the habs off-frame.
            RaiseKitHab(settlement.transform, "StreetHab_SE",
                spawn + new Vector3(9.2f, 0f, 0.8f), 1, 6f);
            RaiseKitHab(settlement.transform, "StreetHab_SW",
                spawn + new Vector3(-9.2f, 0f, 0.8f), 1, -6f);
            RaiseKitHab(settlement.transform, "StreetHab_NE",
                spawn + new Vector3(10.0f, 0f, 7.6f), 1, -8f);
            RaiseKitHab(settlement.transform, "StreetHab_NW",
                spawn + new Vector3(-10.0f, 0f, 7.6f), 1, 10f);

            // Courtyard posts south of origin so glowing heads stay in frustum.
            var masts = new[]
            {
                new Vector3(4.6f, 0f, -2.6f),
                new Vector3(-4.6f, 0f, -2.6f),
                new Vector3(2.2f, 0f, -0.4f),
                new Vector3(-2.2f, 0f, -0.4f),
                new Vector3(6.0f, 0f, -1.4f),
                new Vector3(-6.0f, 0f, -1.4f),
                new Vector3(0.0f, 0f, -3.8f),
                new Vector3(5.0f, 0f, 0.2f),
            };
            for (var i = 0; i < masts.Length; i++)
            {
                var p = masts[i];
                RaiseStreetLamp(settlement.transform, "Mast_" + i, p.x, p.z, dark, brass);
                // Extra posts carry their own dim point (keys stay in MatchSceneBuilder).
                if (i >= 4)
                {
                    PointLamp(settlement.transform, "MastLamp_" + i,
                        new Vector3(p.x + 1.55f, DeckSurface + 7.25f, p.z), 6f, 6f);
                }
            }

            HangStringLights(settlement.transform, spawn);
            RaiseLiftShaft(settlement.transform, playArea, dark);
        }

        /// <summary>
        /// Carved-rock cave mouth at an entry tunnel. Hides the hotspot-sized placeholder cube
        /// so a breach reads as a hole in the cavern wall, not a fourth hab.
        /// </summary>
        public static void DressEntryTunnel(GameObject marker, int tunnelIndex)
        {
            if (marker == null)
            {
                return;
            }

            HidePlaceholders(marker);

            var rock = RockMaterial();
            var dark = ViewLook.Unlit(RockDark);
            var glow = ViewLook.Unlit(new Color(0.55f, 0.22f, 0.08f));

            var inward = -marker.transform.position;
            inward.y = 0f;
            if (inward.sqrMagnitude < 0.01f)
            {
                inward = Vector3.back;
            }

            inward.Normalize();
            var right = Vector3.Cross(Vector3.up, inward).normalized;

            var mouth = new GameObject("CaveMouth");
            mouth.transform.SetParent(marker.transform, false);

            Box(mouth.transform, "Lintel",
                inward * 1.2f + Vector3.up * 5.4f,
                new Vector3(8.5f, 2.2f, 4.5f),
                rock);
            Box(mouth.transform, "Pier_L",
                -right * 3.6f + inward * 0.8f + Vector3.up * 3.2f,
                new Vector3(2.4f, 6.4f, 4.0f),
                rock);
            Box(mouth.transform, "Pier_R",
                right * 3.6f + inward * 0.8f + Vector3.up * 3.2f,
                new Vector3(2.4f, 6.4f, 4.0f),
                rock);
            Box(mouth.transform, "Sill",
                inward * 0.4f + Vector3.up * 0.4f,
                new Vector3(7.2f, 0.8f, 3.2f),
                rock);
            Box(mouth.transform, "Throat",
                -inward * 1.6f + Vector3.up * 3.0f,
                new Vector3(5.2f, 5.5f, 3.5f),
                dark);
            Box(mouth.transform, "Ember",
                -inward * 0.2f + Vector3.up * 1.6f,
                new Vector3(2.4f, 0.3f, 2.4f),
                glow);
        }

        /// <summary>Metal landing pad at team spawn — kit floor plates on a thick deck.</summary>
        public static void DressSpawnPad(GameObject marker)
        {
            if (marker == null)
            {
                return;
            }

            HidePlaceholders(marker);

            var deck = DeckingMaterial();
            var dark = DarkMetalMaterial();
            var brass = ViewLook.Unlit(Brass);

            var pad = new GameObject("SpawnPad");
            pad.transform.SetParent(marker.transform, false);
            Box(pad.transform, "Plate",
                new Vector3(0f, DeckThickness * 0.5f, 0f),
                new Vector3(SciFiKit.Grid, DeckThickness, SciFiKit.Grid), deck, castShadows: true);
            SciFiKit.Place(pad.transform, "KitPlate", SciFiKit.FloorMetal,
                new Vector3(0f, DeckSurface, 0f), Quaternion.identity, deck, castShadows: true);
            SciFiKit.Place(pad.transform, "Col_0", SciFiKit.ColumnTall,
                new Vector3(-3.4f, DeckSurface, 3.4f), Quaternion.identity, dark);
            SciFiKit.Place(pad.transform, "Col_1", SciFiKit.ColumnTall,
                new Vector3(3.4f, DeckSurface, 3.4f), Quaternion.identity, dark);
            RaiseStreetLamp(pad.transform, "PadLamp", 3.4f, 3.4f, dark, brass);
        }

        /// <summary>
        /// Colony walk surface: kit floor modules sitting on thick deck plates so the
        /// perspective camera sees plate sides and rock in the gaps. The Ground plane
        /// stays the cave floor; it is no longer the walkable stamp.
        /// </summary>
        public static void DressFloor(Transform root, Bounds playArea)
        {
            if (root == null)
            {
                return;
            }

            var floor = new GameObject("FloorDressing");
            floor.transform.SetParent(root, false);

            var deck = DeckingMaterial();
            var dark = DarkMetalMaterial();
            var rock = RockMaterial();
            var step = SciFiKit.Grid;
            var n = 0;
            var floors = new[]
            {
                SciFiKit.FloorDark, SciFiKit.FloorMetal, SciFiKit.FloorSimple, SciFiKit.FloorPlates,
            };
            for (var x = -32f; x <= 32f; x += step)
            {
                for (var z = -32f; z <= 32f; z += step)
                {
                    Box(floor.transform, "DeckPlate_" + n,
                        new Vector3(x, DeckThickness * 0.5f, z),
                        new Vector3(step - 0.35f, DeckThickness, step - 0.35f),
                        deck, castShadows: true);
                    SciFiKit.Place(floor.transform, "KitFloor_" + n, floors[n % floors.Length],
                        new Vector3(x, DeckSurface, z), Quaternion.identity, deck, castShadows: true);
                    n++;
                }
            }

            Box(floor.transform, "Curb_X",
                new Vector3(playArea.center.x, DeckThickness + 0.08f, playArea.center.z),
                new Vector3(74f, 0.16f, 0.55f),
                dark);
            Box(floor.transform, "Curb_Z",
                new Vector3(playArea.center.x, DeckThickness + 0.08f, playArea.center.z),
                new Vector3(0.55f, 0.16f, 74f),
                dark);

            Box(floor.transform, "RockGap_0",
                new Vector3(18f, 0.10f, -14f), new Vector3(2.4f, 0.20f, 2.4f), rock);
            Box(floor.transform, "RockGap_1",
                new Vector3(-20f, 0.10f, 12f), new Vector3(2.2f, 0.20f, 2.6f), rock);
        }

        /// <summary>
        /// Presentation of S4 lost-state: darken the habitat and hide the civilian huddle.
        /// Shared materials are not mutated — per-renderer property blocks only.
        /// </summary>
        public static void ApplyLostLook(GameObject marker, bool lost)
        {
            if (marker == null)
            {
                return;
            }

            var renderers = marker.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (renderer.transform.name.StartsWith("placeholder_"))
                {
                    continue;
                }

                if (lost)
                {
                    ViewLook.TintBlock(renderer, LostTint);
                }
                else
                {
                    renderer.SetPropertyBlock(null);
                }
            }

            var huddle = marker.transform.Find("Habitat/Civilians");
            if (huddle != null)
            {
                huddle.gameObject.SetActive(!lost);
            }
        }

        /// <summary>
        /// Pulse / flare colour on a cave mouth. Presentation only; the bools on the marker
        /// view remain the contract.
        /// </summary>
        public static void ApplyTunnelLook(GameObject marker, bool pulsing, bool flaring)
        {
            if (marker == null)
            {
                return;
            }

            Color tint;
            if (flaring)
            {
                tint = new Color(1.0f, 0.35f, 0.12f);
            }
            else if (pulsing)
            {
                tint = new Color(0.95f, 0.28f, 0.14f);
            }
            else
            {
                tint = LiveTint;
            }

            var ember = marker.transform.Find("CaveMouth/Ember");
            if (ember == null)
            {
                return;
            }

            var renderer = ember.GetComponent<Renderer>();
            ViewLook.TintBlock(renderer, tint);
        }

        private static void RaiseCivilianHuddle(Transform hab, int civilians, float bodyD)
        {
            var huddle = new GameObject("Civilians");
            huddle.transform.SetParent(hab, false);

            var shown = civilians > 5 ? 5 : civilians;
            if (shown < 1)
            {
                shown = 1;
            }

            var tints = new[]
            {
                new Color(0.82f, 0.58f, 0.32f),
                new Color(0.70f, 0.42f, 0.22f),
                new Color(0.55f, 0.38f, 0.22f),
                new Color(0.78f, 0.50f, 0.28f),
                new Color(0.62f, 0.36f, 0.18f),
            };

            for (var i = 0; i < shown; i++)
            {
                var x = (i - (shown - 1) * 0.5f) * 1.15f;
                var z = -(bodyD * 0.5f + 1.8f) + (i % 2) * 0.4f;
                var height = 2.35f + (i % 3) * 0.18f;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Civ_" + i;
                quad.transform.SetParent(huddle.transform, false);
                quad.transform.localPosition = new Vector3(x, height * 0.5f, z);
                quad.transform.localScale = new Vector3(1.35f, height, 1f);
                ViewLook.StripCollider(quad);
                ViewLook.Paint(quad, ViewLook.Unlit(tints[i % tints.Length]));
                quad.AddComponent<BillboardFacing>();
            }
        }

        /// <summary>
        /// One coherent habitat volume: structural Lit wall slabs + roof overhang per
        /// storey, kit floor/door/columns/wall cladding sitting on the deck (SitOnLocalY
        /// so kit pivots cannot float). Missing FBX (R-15) still leaves a building.
        /// </summary>
        private static void RaiseKitHab(
            Transform parent, string name, Vector3 pos, int stories, float yawDeg)
        {
            if (stories < 1)
            {
                stories = 1;
            }

            if (stories > 4)
            {
                stories = 4;
            }

            var hab = new GameObject(name);
            hab.transform.SetParent(parent, false);
            hab.transform.localPosition = pos;
            hab.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);

            var metal = HabitatMaterial();
            var roofMat = RoofMaterial();
            var dark = DarkMetalMaterial();
            var deck = DeckingMaterial();
            var g = SciFiKit.Grid;
            var half = g * 0.48f;
            var wallT = 0.62f;
            var storey = SciFiKit.StoryHeight;

            Box(hab.transform, "Deck",
                new Vector3(0f, DeckThickness * 0.5f, 0f),
                new Vector3(g, DeckThickness, g),
                deck, castShadows: true);
            SciFiKit.Place(hab.transform, "Floor", SciFiKit.FloorDark,
                new Vector3(0f, DeckSurface, 0f), Quaternion.identity, deck, castShadows: true);

            var faces = new[]
            {
                SciFiKit.FaceSouth, SciFiKit.FaceNorth, SciFiKit.FaceEast, SciFiKit.FaceWest,
            };
            var faceNames = new[] { "S", "N", "E", "W" };
            var inset = g * 0.42f;

            for (var s = 0; s < stories; s++)
            {
                var y0 = DeckSurface + (s * storey);
                var yMid = y0 + (storey * 0.5f);

                Box(hab.transform, "Wall_S_" + s,
                    new Vector3(0f, yMid, -half),
                    new Vector3(g * 0.96f, storey, wallT), metal, castShadows: true);
                Box(hab.transform, "Wall_N_" + s,
                    new Vector3(0f, yMid, half),
                    new Vector3(g * 0.96f, storey, wallT), metal, castShadows: true);
                Box(hab.transform, "Wall_E_" + s,
                    new Vector3(half, yMid, 0f),
                    new Vector3(wallT, storey, g * 0.96f), metal, castShadows: true);
                Box(hab.transform, "Wall_W_" + s,
                    new Vector3(-half, yMid, 0f),
                    new Vector3(wallT, storey, g * 0.96f), metal, castShadows: true);

                for (var f = 0; f < 4; f++)
                {
                    string module;
                    if (faceNames[f] == "S")
                    {
                        module = s == 0 ? SciFiKit.WallWindow : SciFiKit.WallWindowStrip;
                    }
                    else if (faceNames[f] == "E")
                    {
                        module = SciFiKit.WallFlatWindow;
                    }
                    else
                    {
                        module = (s % 2) == 0 ? SciFiKit.WallSolid : SciFiKit.WallBand;
                    }

                    SciFiKit.Place(hab.transform, "KitWall_" + faceNames[f] + "_" + s,
                        module, new Vector3(0f, y0, 0f), faces[f], metal, castShadows: true);
                }

                SciFiKit.Place(hab.transform, "Col_NW_" + s, SciFiKit.ColumnStory,
                    new Vector3(-inset, y0, inset), Quaternion.identity, dark);
                SciFiKit.Place(hab.transform, "Col_NE_" + s, SciFiKit.ColumnStory,
                    new Vector3(inset, y0, inset), Quaternion.identity, dark);
                SciFiKit.Place(hab.transform, "Col_SW_" + s, SciFiKit.ColumnStory,
                    new Vector3(-inset, y0, -inset), Quaternion.identity, dark);
                SciFiKit.Place(hab.transform, "Col_SE_" + s, SciFiKit.ColumnStory,
                    new Vector3(inset, y0, -inset), Quaternion.identity, dark);
            }

            SciFiKit.Place(hab.transform, "Door", SciFiKit.DoorSimple,
                new Vector3(0f, DeckSurface, 0f), SciFiKit.FaceSouth, dark, castShadows: true);
            Box(hab.transform, "DoorSlab",
                new Vector3(0f, DeckSurface + 1.55f, -(half + 0.08f)),
                new Vector3(1.7f, 3.1f, 0.18f), dark, castShadows: true);

            var roofY = DeckSurface + (stories * storey);
            Box(hab.transform, "RoofSlab",
                new Vector3(0f, roofY + 0.38f, 0f),
                new Vector3(g + 1.6f, 0.76f, g + 1.6f),
                roofMat, castShadows: true);
            Box(hab.transform, "Overhang_S",
                new Vector3(0f, roofY + 0.18f, -(half + 0.7f)),
                new Vector3(g * 0.92f, 0.22f, 1.4f),
                roofMat, castShadows: true);
            SciFiKit.Place(hab.transform, "Roof", SciFiKit.FloorMetal,
                new Vector3(0f, roofY, 0f), Quaternion.identity, roofMat, castShadows: true);
            SciFiKit.Place(hab.transform, "Vent", SciFiKit.Vent,
                new Vector3(1.1f, roofY, 1.1f), Quaternion.identity, dark);

            DressWindowsAndEaveLantern(hab.transform, stories, half, roofY);

            DressRimClutter(hab.transform, "Clutter", 0f, 0f, half + 0.85f);
        }

        /// <summary>
        /// Crates, barrels, pipes along the east/north rims. Outside the hab volume so
        /// they sell the street; not in the spawn/hotspot courtyard.
        /// </summary>
        private static void DressRimClutter(
            Transform parent, string name, float x, float z, float rim)
        {
            var dark = DarkMetalMaterial();
            var deck = DeckingMaterial();
            // South rim (local -Z) sits outside the spawn courtyard for the flanking habs.
            SciFiKit.Place(parent, name + "_crateA", SciFiKit.CrateA,
                new Vector3(x + 1.15f, DeckSurface, z - rim), Quaternion.Euler(0f, 18f, 0f),
                deck, castShadows: true);
            SciFiKit.Place(parent, name + "_crateB", SciFiKit.CrateB,
                new Vector3(x - 0.9f, DeckSurface, z - rim - 0.15f), Quaternion.Euler(0f, -25f, 0f),
                deck, castShadows: true);
            SciFiKit.Place(parent, name + "_barrel", SciFiKit.Barrel,
                new Vector3(x + 2.3f, DeckSurface, z - rim + 0.2f), Quaternion.identity,
                dark, castShadows: true);
            SciFiKit.Place(parent, name + "_pipe", SciFiKit.ColumnPipes,
                new Vector3(x - 2.1f, DeckSurface, z - rim + 0.1f), Quaternion.identity,
                dark, castShadows: true);
        }

        /// <summary>
        /// 3D lantern fixture: pole, arm, cage, glowing head. The PointLight lives in
        /// MatchSceneBuilder; this is the mesh the camera actually sees.
        /// </summary>
        private static void RaiseStreetLamp(
            Transform parent, string name, float x, float z, Material dark, Material brass)
        {
            var glow = ViewLook.Unlit(AmberGlow);
            const float poleH = 8.2f;
            var glassY = DeckSurface + poleH - 1.15f;
            var glassX = x + 1.55f;
            Box(parent, name + "_pole",
                new Vector3(x, DeckSurface + poleH * 0.5f, z),
                new Vector3(0.50f, poleH, 0.50f), dark, castShadows: true);
            Box(parent, name + "_arm",
                new Vector3(x + 0.90f, DeckSurface + poleH - 0.40f, z),
                new Vector3(2.2f, 0.34f, 0.34f), brass);
            Box(parent, name + "_cage",
                new Vector3(glassX, glassY, z),
                new Vector3(1.25f, 1.45f, 1.25f), dark);
            Box(parent, name + "_glass",
                new Vector3(glassX, glassY, z),
                new Vector3(0.98f, 1.18f, 0.98f), glow);
        }

        /// <summary>
        /// Unlit amber window boxes on the street (south) face plus a hanging lantern
        /// under the south eave. Each carries a small point light so the hab actually
        /// lights the street a little — not just a painted glow.
        /// </summary>
        private static void DressWindowsAndEaveLantern(
            Transform hab, int stories, float half, float roofY)
        {
            var glow = ViewLook.Unlit(AmberGlow);
            var dark = DarkMetalMaterial();
            var storey = SciFiKit.StoryHeight;
            // Wall slabs are 0.62 thick centered on ±half; panes must sit proud of
            // the OUTER face or they vanish inside the Lit wall.
            const float wallT = 0.62f;
            var southZ = -(half + (wallT * 0.5f) + 0.28f);
            var eastX = half + (wallT * 0.5f) + 0.28f;
            var westX = -eastX;
            for (var s = 0; s < stories; s++)
            {
                var y = DeckSurface + (s * storey) + 2.45f;
                Box(hab, "Window_SL_" + s,
                    new Vector3(-1.7f, y, southZ),
                    new Vector3(2.35f, 1.85f, 0.36f), glow);
                Box(hab, "Window_SR_" + s,
                    new Vector3(1.7f, y, southZ),
                    new Vector3(2.35f, 1.85f, 0.36f), glow);
                // Street-inner sides — the 55° cam reads these as the hab walls.
                Box(hab, "Window_EL_" + s,
                    new Vector3(eastX, y, -1.15f),
                    new Vector3(0.36f, 1.85f, 2.35f), glow);
                Box(hab, "Window_WL_" + s,
                    new Vector3(westX, y, -1.15f),
                    new Vector3(0.36f, 1.85f, 2.35f), glow);
                PointLamp(hab, "WindowLamp_" + s,
                    new Vector3(0f, y, southZ - 0.4f), 5f, 5f);
            }

            var hangY = roofY - 0.85f;
            var hangZ = -(half + 1.35f);
            Box(hab, "EaveCage",
                new Vector3(0f, hangY, hangZ),
                new Vector3(1.20f, 1.40f, 1.20f), dark);
            Box(hab, "EaveGlass",
                new Vector3(0f, hangY, hangZ),
                new Vector3(0.90f, 1.10f, 0.90f), glow);
            PointLamp(hab, "EaveLamp",
                new Vector3(0f, hangY - 0.2f, hangZ), 6f, 6f);
        }

        /// <summary>
        /// Authored string-lights strip hung across the spawn street, plus a line of
        /// small amber points along each span. Overhead, not a deck fill-grid.
        /// </summary>
        private static void HangStringLights(Transform parent, Vector3 spawn)
        {
            var tex = ViewLook.LoadTexture("RedHollowArt/string-lights");
            var punched = PunchDarkToAlpha(tex);
            var mat = punched != null
                ? ViewLook.UnlitCutout(new Color(1.08f, 0.88f, 0.52f), punched)
                : ViewLook.Unlit(AmberGlow);
            var glow = ViewLook.Unlit(AmberGlow);
            var dark = DarkMetalMaterial();

            // y 7-9, z across the courtyard the spawn cam actually frames.
            var spans = new[]
            {
                new Vector3(0f, 7.5f, -4.0f),
                new Vector3(0f, 8.1f, -1.8f),
                new Vector3(0f, 8.6f, -0.2f),
            };
            for (var i = 0; i < spans.Length; i++)
            {
                var pos = spawn + spans[i];
                Box(parent, "StringCable_" + i,
                    new Vector3(pos.x, pos.y, pos.z),
                    new Vector3(16.5f, 0.14f, 0.14f), dark);

                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "StringLights_" + i;
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3(pos.x, pos.y - 0.55f, pos.z);
                go.transform.localRotation = Quaternion.Euler(62f, 180f, 0f);
                go.transform.localScale = new Vector3(16.5f, 2.6f, 1f);
                ViewLook.StripCollider(go);
                if (mat != null)
                {
                    ViewLook.Paint(go, mat, castShadows: false);
                }

                for (var b = 0; b < 3; b++)
                {
                    var x = -6.0f + (b * 6.0f);
                    var bulbPos = new Vector3(pos.x + x, pos.y - 0.70f, pos.z);
                    var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    bulb.name = "StringGlobe_" + i + "_" + b;
                    bulb.transform.SetParent(parent, false);
                    bulb.transform.position = bulbPos;
                    bulb.transform.localScale = new Vector3(0.42f, 0.50f, 0.42f);
                    ViewLook.StripCollider(bulb);
                    if (glow != null)
                    {
                        ViewLook.Paint(bulb, glow, castShadows: false);
                    }

                    PointLamp(parent, "StringBulb_" + i + "_" + b,
                        bulbPos, 4f, 4f);
                }
            }
        }

        /// <summary>
        /// Authored string-lights PNG is RGB on black. Punch near-black to alpha so
        /// UnlitCutout shows bulbs, not a black slab over the street.
        /// </summary>
        private static Texture2D PunchDarkToAlpha(Texture2D source)
        {
            if (source == null)
            {
                return null;
            }

            Color[] pixels;
            try
            {
                pixels = source.GetPixels();
            }
            catch (System.Exception)
            {
                return source;
            }

            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.wrapMode = TextureWrapMode.Clamp;
            copy.filterMode = FilterMode.Bilinear;
            copy.name = source.name + "_cut";
            for (var i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                var luma = (0.3f * c.r) + (0.59f * c.g) + (0.11f * c.b);
                if (luma < 0.08f)
                {
                    c.a = 0f;
                }
                else
                {
                    c.a = 1f;
                    c.r = Mathf.Min(1f, c.r * 1.25f + 0.08f);
                    c.g = Mathf.Min(1f, c.g * 1.15f + 0.04f);
                    c.b = Mathf.Min(1f, c.b * 1.05f + 0.02f);
                }

                pixels[i] = c;
            }

            copy.SetPixels(pixels);
            copy.Apply(false, false);
            return copy;
        }

        /// <summary>Presentation-only punctual amber. Never Directional. No shadows.</summary>
        private static void PointLamp(
            Transform parent, string name, Vector3 localPos, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1.0f, 0.70f, 0.36f);
            light.lightUnit = LightUnit.Candela;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.GetUniversalAdditionalLightData();
        }

        private static void RaiseLiftShaft(Transform parent, Bounds playArea, Material dark)
        {
            var inner = Mathf.Max(playArea.extents.x, playArea.extents.z);
            var x = playArea.center.x + inner * 0.55f;
            var z = playArea.center.z + inner * 0.55f;
            var shaft = new GameObject("LiftShaft");
            shaft.transform.SetParent(parent, false);

            const float shaftH = 72f;
            Box(shaft.transform, "Core",
                new Vector3(x, shaftH * 0.5f, z),
                new Vector3(2.2f, shaftH, 2.2f),
                dark);

            for (var ring = 0; ring < 8; ring++)
            {
                var y = 6f + ring * 8f;
                Box(shaft.transform, "Ring_" + ring,
                    new Vector3(x, y, z),
                    new Vector3(5.4f, 0.35f, 5.4f),
                    dark);
            }

            var mast = 0.45f;
            var span = 2.4f;
            Box(shaft.transform, "Mast_0", new Vector3(x - span, shaftH * 0.5f, z - span), new Vector3(mast, shaftH, mast), dark);
            Box(shaft.transform, "Mast_1", new Vector3(x + span, shaftH * 0.5f, z - span), new Vector3(mast, shaftH, mast), dark);
            Box(shaft.transform, "Mast_2", new Vector3(x - span, shaftH * 0.5f, z + span), new Vector3(mast, shaftH, mast), dark);
            Box(shaft.transform, "Mast_3", new Vector3(x + span, shaftH * 0.5f, z + span), new Vector3(mast, shaftH, mast), dark);
        }

        private static void HidePlaceholders(GameObject marker)
        {
            foreach (var renderer in marker.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.gameObject.name.StartsWith("placeholder_"))
                {
                    renderer.enabled = false;
                }
            }
        }

        private static Material WallMaterial()
        {
            return Tiled("RedHollowArt/sandstone-wall", CavernTint, new Vector2(3.2f, 4.0f),
                "RedHollowArt/cavern-ground");
        }

        private static Material RockMaterial()
        {
            return Tiled("RedHollowArt/sandstone-wall", RockTint, new Vector2(2.2f, 2.6f),
                "RedHollowArt/cavern-ground");
        }

        private static Material RoofMaterial()
        {
            // Hab cube TOPS: authored rusty roof plates, then decking, then cladding.
            // Warm rust tint (darker than walls, brighter than the old 0.32 crush).
            var tex = ViewLook.LoadTexture("RedHollowArt/hab-block-roof")
                ?? ViewLook.LoadTexture("RedHollowArt/colony-decking")
                ?? ViewLook.LoadTexture("RedHollowArt/hab-block-cladding");
            var mat = ViewLook.Lit(tex != null ? RoofTint : Roof, tex,
                NormalFor("RedHollowArt/hab-block-roof", "RedHollowArt/colony-decking"),
                smoothness: 0.12f);
            if (mat != null && tex != null)
            {
                ViewLook.SetTiling(mat, new Vector2(1.6f, 1.6f));
            }

            return mat;
        }

        private static Material HabitatMaterial()
        {
            var tex = ViewLook.LoadTexture("RedHollowArt/hab-block-wall")
                ?? ViewLook.LoadTexture("RedHollowArt/hab-block-cladding")
                ?? ViewLook.LoadTexture("RedHollowArt/colony-wall")
                ?? ViewLook.LoadTexture("RedHollowArt/metal-floor-plate")
                ?? ViewLook.LoadTexture("RedHollowArt/cavern-ground");
            var mat = ViewLook.Lit(tex != null ? HabWallTint : Metal, tex,
                NormalFor("RedHollowArt/hab-block-wall", "RedHollowArt/colony-wall"),
                smoothness: 0.16f);
            if (mat != null && tex != null)
            {
                ViewLook.SetTiling(mat, new Vector2(1.05f, 1.35f));
            }

            return mat;
        }

        private static Material DeckingMaterial()
        {
            return Tiled("RedHollowArt/colony-decking", new Color(0.56f, 0.40f, 0.24f),
                new Vector2(4f, 4f), "RedHollowArt/metal-floor-plate");
        }

        private static Material TrimMaterial()
        {
            var tex = ViewLook.LoadTexture("RedHollowArt/wood-trim");
            return ViewLook.Lit(tex != null ? new Color(0.75f, 0.55f, 0.32f) : WoodTrim, tex,
                NormalFor("RedHollowArt/wood-trim"), smoothness: 0.22f);
        }

        private static Material Tiled(string resourcePath, Color tint, Vector2 scale,
            string altPath = null)
        {
            var tex = ViewLook.LoadTexture(resourcePath)
                ?? (altPath != null ? ViewLook.LoadTexture(altPath) : null);
            var mat = ViewLook.Lit(tint, tex, NormalFor(resourcePath, altPath), smoothness: 0.14f);
            if (mat != null && tex != null)
            {
                ViewLook.SetTiling(mat, scale);
            }

            return mat;
        }

        /// <summary>
        /// Bind an authored normal if it is already imported next to the albedo
        /// (RedHollowArt/cavern-ground_normal etc.). Missing file is a no-op (R-15).
        /// </summary>
        private static Texture NormalFor(string resourcePath, string altPath = null)
        {
            var n = ViewLook.LoadTexture(resourcePath + "_normal");
            if (n != null)
            {
                return n;
            }

            return altPath != null ? ViewLook.LoadTexture(altPath + "_normal") : null;
        }

        private static Material DarkMetalMaterial()
        {
            var tex = ViewLook.LoadTexture("RedHollowArt/hab-block-cladding")
                ?? ViewLook.LoadTexture("RedHollowArt/metal-floor-plate");
            return ViewLook.Lit(MetalDark, tex,
                NormalFor("RedHollowArt/hab-block-cladding", "RedHollowArt/metal-floor-plate"),
                smoothness: 0.28f);
        }

        private static GameObject Box(
            Transform parent, string name, Vector3 localPos, Vector3 scale, Material material,
            bool castShadows = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            ViewLook.StripCollider(go);
            ViewLook.Paint(go, material, castShadows);
            return go;
        }
    }
}
