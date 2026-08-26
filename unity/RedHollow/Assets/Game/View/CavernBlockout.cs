using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Runtime 3D blockout for the Lantern Deep cavern: a terraformed Mars underground
    /// colony (Lykos / seed-env), not a western town dropped in a cave.
    ///
    /// Mix: ~70% Martian habitat (stacked utilitarian blocks, flat roofs, gantries,
    /// metal/rust plate, carved-rock walls, lantern masts, a lift-shaft landmark) and
    /// ~30% western as accents only (wood trim strips, brass, amber lanterns). Silhouette
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

        public const float HabHeightMin = 6f;
        public const float HabHeightMax = 22f;

        private static readonly Color Rock = new Color(0.42f, 0.26f, 0.14f);
        private static readonly Color RockDark = new Color(0.22f, 0.12f, 0.07f);
        private static readonly Color Metal = new Color(0.48f, 0.30f, 0.18f);
        private static readonly Color MetalDark = new Color(0.22f, 0.14f, 0.09f);
        private static readonly Color Roof = new Color(0.16f, 0.09f, 0.05f);
        private static readonly Color Brass = new Color(0.72f, 0.48f, 0.18f);
        private static readonly Color WoodTrim = new Color(0.40f, 0.26f, 0.14f);
        private static readonly Color CeilingDark = new Color(0.06f, 0.035f, 0.02f);
        private static readonly Color AmberGlow = new Color(1.0f, 0.72f, 0.32f);
        private static readonly Color LostTint = new Color(0.18f, 0.10f, 0.06f);
        private static readonly Color LiveTint = Color.white;
        // Unlit albedo tints: contrast has to live in the texture*color product
        // because lanterns do not shade Unlit meshes.
        private static readonly Color RoofTint = new Color(0.28f, 0.15f, 0.08f);
        private static readonly Color HabWallTint = new Color(1.10f, 0.92f, 0.66f);
        private static readonly Color CavernTint = new Color(0.46f, 0.27f, 0.14f);
        private static readonly Color RockTint = new Color(0.26f, 0.14f, 0.08f);

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
        /// Replace a hotspot's flat marker disc with a Mars habitat: stacked metal blocks,
        /// a flat darker roof, a short gantry, wood-trim accent, a lantern mast, window glow,
        /// and a huddle of civilians so an occupied shelter does not read as already-lost.
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

            var metal = HabitatMaterial();
            var roof = RoofMaterial();
            var brass = ViewLook.Unlit(Brass);
            var glow = ViewLook.Unlit(AmberGlow);

            var hab = new GameObject("Habitat");
            hab.transform.SetParent(marker.transform, false);

            // Courtyard on the sim point: monsters path here, so a solid 8-unit cube
            // swallowed the whole wave. Habs sit on the rim; the pad stays open.
            Box(hab.transform, "Deck",
                new Vector3(0f, 0.08f, 0f),
                new Vector3(13f, 0.16f, 13f),
                DeckingMaterial());

            var yaw = hotspotId == "hs_chapel" ? 25f
                : hotspotId == "hs_homestead" ? -20f
                : 8f;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var wings = new[]
            {
                new Vector3(5.8f, 0f, 1.4f),
                new Vector3(-5.8f, 0f, 1.1f),
                new Vector3(0.4f, 0f, 5.9f),
                new Vector3(1.6f, 0f, -5.7f),
            };
            var wingSizes = new[]
            {
                new Vector3(3.4f, 10.2f, 4.0f),
                new Vector3(3.6f, 7.4f, 3.8f),
                new Vector3(5.0f, 11.6f, 3.4f),
                new Vector3(4.2f, 6.8f, 3.2f),
            };
            var wingStories = new[] { 3, 1, 2, 1 };

            for (var i = 0; i < wings.Length; i++)
            {
                RaiseHab(hab.transform, "Wing_" + i, rot * wings[i], wingSizes[i], metal, roof,
                    wingStories[i]);
            }

            var mast = rot * new Vector3(5.2f, 0f, 5.2f);
            Box(hab.transform, "LanternMast",
                new Vector3(mast.x, 3.4f, mast.z),
                new Vector3(0.28f, 6.8f, 0.28f),
                ViewLook.Unlit(MetalDark));
            Box(hab.transform, "LanternHead",
                new Vector3(mast.x, 6.9f, mast.z),
                new Vector3(0.7f, 0.7f, 0.7f),
                brass);

            var window = rot * new Vector3(0.4f, 0f, 5.9f);
            Box(hab.transform, "Window_0",
                new Vector3(window.x, 5.2f, window.z + 1.74f),
                new Vector3(1.6f, 1.1f, 0.08f),
                glow);

            PinFacade(hab.transform, hotspotId, rot);

            if (civilians > 0)
            {
                RaiseCivilianHuddle(hab.transform, civilians, 2.4f);
            }
        }

        /// <summary>
        /// Authored hab front on the camera-facing (south) rim. Missing art is a no-op so the
        /// placeholder build stays shippable (R-15).
        /// </summary>
        private static void PinFacade(Transform hab, string hotspotId, Quaternion rot)
        {
            var resource = hotspotId == "hs_chapel" ? "RedHollowArt/chapel-hab-facade"
                : hotspotId == "hs_homestead" ? "RedHollowArt/homestead-hab-facade"
                : "RedHollowArt/saloon-hab-facade";
            var tex = ViewLook.LoadTexture(resource);
            if (tex == null)
            {
                return;
            }

            var mat = ViewLook.UnlitCutout(Color.white, tex);
            // South wing Body occupies ~z=-7.3..-4.1; stand the card just outside that
            // face so the 62°-down camera (south of the colony) sees the painted front
            // instead of hab-block cladding. Missing art is already a no-op above (R-15).
            var front = rot * new Vector3(0f, 0f, -8.4f);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Facade";
            quad.transform.SetParent(hab, false);
            quad.transform.localPosition = new Vector3(front.x, 5.6f, front.z);
            quad.transform.localRotation = rot * Quaternion.Euler(0f, 180f, 0f);
            quad.transform.localScale = new Vector3(12.5f, 11.0f, 1f);
            ViewLook.StripCollider(quad);
            ViewLook.Paint(quad, mat);
        }

        /// <summary>
        /// Visual-only packed colony: extra stacked hab cubes, metal decking, lantern masts
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

            var metal = HabitatMaterial();
            var roof = RoofMaterial();
            var dark = ViewLook.Unlit(MetalDark);
            var deck = DeckingMaterial();
            var brass = ViewLook.Unlit(Brass);

            var cluster = new[]
            {
                new Vector3(6.8f, 0f, 3.4f),
                new Vector3(-5.6f, 0f, 5.2f),
                new Vector3(4.4f, 0f, -6.1f),
                new Vector3(-7.2f, 0f, -3.6f),
                new Vector3(8.4f, 0f, -1.8f),
                new Vector3(2.2f, 0f, 7.4f),
            };
            var sizes = new[]
            {
                new Vector3(4.2f, 6.4f, 3.8f),
                new Vector3(3.4f, 8.6f, 3.4f),
                new Vector3(5.0f, 5.2f, 4.2f),
                new Vector3(3.2f, 7.4f, 3.6f),
                new Vector3(4.6f, 5.8f, 3.2f),
                new Vector3(3.8f, 9.6f, 3.5f),
            };
            var storyCounts = new[] { 2, 3, 1, 2, 1, 4 };

            var n = 0;
            foreach (var spec in map.Hotspots)
            {
                if (spec == null)
                {
                    continue;
                }

                var origin = SimSpace.ToWorld(spec.Pos);
                for (var i = 0; i < cluster.Length; i++)
                {
                    var offset = cluster[i];
                    if ((n % 2) == 1)
                    {
                        offset = new Vector3(-offset.x, 0f, offset.z);
                    }

                    // Camera sits south looking north: a packed cube on the south rim
                    // swallows the authored hab facade. Leave that front open.
                    if (offset.z < -3.5f)
                    {
                        continue;
                    }

                    var pos = origin + offset;
                    var size = sizes[(i + n) % sizes.Length];
                    RaiseHab(settlement.transform, "Hab_" + spec.Id + "_" + i, pos, size, metal, roof,
                        storyCounts[(i + n) % storyCounts.Length]);
                }

                // Metal catwalk stitching the cluster together.
                Box(settlement.transform, "DeckStrip_" + spec.Id,
                    new Vector3(origin.x + 1.5f, 0.07f, origin.z),
                    new Vector3(14f, 0.14f, 3.2f),
                    deck);

                n++;
            }

            // Packed extra cubes across the play square, skipping the spawn courtyard
            // and the hotspot footprints so heroes and shelters stay readable.
            var spawn = SimSpace.ToWorld(map.TeamSpawn);
            var extra = 0;
            for (var gx = -22; gx <= 22; gx += 7)
            {
                for (var gz = -22; gz <= 22; gz += 7)
                {
                    var pos = new Vector3(gx + (gz % 2) * 1.4f, 0f, gz + (gx % 3) * 0.6f);
                    if ((pos - spawn).sqrMagnitude < 8.5f * 8.5f)
                    {
                        continue;
                    }

                    var tooClose = false;
                    foreach (var spec in map.Hotspots)
                    {
                        if (spec == null)
                        {
                            continue;
                        }

                        var hp = SimSpace.ToWorld(spec.Pos);
                        var delta = pos - hp;
                        if (delta.sqrMagnitude < 5.5f * 5.5f)
                        {
                            tooClose = true;
                            break;
                        }

                        // Keep the camera-facing (south) apron clear so hab facades read.
                        if (delta.z < 0f && delta.z > -10f && Mathf.Abs(delta.x) < 7f)
                        {
                            tooClose = true;
                            break;
                        }
                    }

                    if (tooClose)
                    {
                        continue;
                    }

                    var h = 5.6f + (extra % 6) * 1.5f;
                    var w = 3.0f + (extra % 3) * 0.5f;
                    var stories = 1 + (extra % 4);
                    RaiseHab(settlement.transform, "GridHab_" + extra, pos,
                        new Vector3(w, h, w - 0.3f), metal, roof, stories);
                    extra++;
                }
            }

            // Lantern masts sprinkled through the packed blocks (geometry + brass head;
            // sourced lights sit in MatchSceneBuilder).
            var masts = new[]
            {
                new Vector3(10f, 0f, 10f),
                new Vector3(-11f, 0f, 8f),
                new Vector3(8f, 0f, -12f),
                new Vector3(-9f, 0f, -9f),
                new Vector3(16f, 0f, 2f),
                new Vector3(-15f, 0f, -4f),
            };
            for (var i = 0; i < masts.Length; i++)
            {
                var p = masts[i];
                Box(settlement.transform, "Mast_" + i,
                    new Vector3(p.x, 5.5f, p.z), new Vector3(0.28f, 11f, 0.28f), dark);
                Box(settlement.transform, "MastHead_" + i,
                    new Vector3(p.x, 11.2f, p.z), new Vector3(0.7f, 0.7f, 0.7f), brass);
            }

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

            // Face inward toward the colony centre.
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

        /// <summary>Metal landing pad at team spawn — industrial, not a hitching post.</summary>
        public static void DressSpawnPad(GameObject marker)
        {
            if (marker == null)
            {
                return;
            }

            HidePlaceholders(marker);

            var deck = DeckingMaterial();
            var dark = ViewLook.Unlit(MetalDark);
            var brass = ViewLook.Unlit(Brass);

            var pad = new GameObject("SpawnPad");
            pad.transform.SetParent(marker.transform, false);
            Box(pad.transform, "Plate",
                new Vector3(0f, 0.06f, 0f), new Vector3(7.5f, 0.12f, 7.5f), deck);
            Box(pad.transform, "Ring",
                new Vector3(0f, 0.14f, 0f), new Vector3(8.4f, 0.08f, 8.4f), dark);
            Box(pad.transform, "Mast",
                new Vector3(3.4f, 3.4f, 3.4f), new Vector3(0.22f, 6.8f, 0.22f), dark);
            Box(pad.transform, "Head",
                new Vector3(3.4f, 6.9f, 3.4f), new Vector3(0.6f, 0.6f, 0.6f), brass);
        }

        /// <summary>
        /// Layer the colony floor: cavern-ground already covers the play square; add a gravel
        /// rim, cracked-soil patches, and a metal plaza so the camera sees more than one tile.
        /// </summary>
        public static void DressFloor(Transform root, Bounds playArea)
        {
            if (root == null)
            {
                return;
            }

            var floor = new GameObject("FloorDressing");
            floor.transform.SetParent(root, false);

            var gravel = Tiled("RedHollowArt/gravel-border", new Color(0.70f, 0.48f, 0.30f), new Vector2(6f, 6f));
            var cracked = Tiled("RedHollowArt/cracked-soil", new Color(0.78f, 0.52f, 0.32f), new Vector2(3.5f, 3.5f));
            var deck = DeckingMaterial();

            var span = Mathf.Max(playArea.size.x, playArea.size.z) + 18f;
            Box(floor.transform, "GravelRim",
                new Vector3(playArea.center.x, 0.015f, playArea.center.z),
                new Vector3(span, 0.03f, span),
                gravel);

            var patches = new[]
            {
                new Vector3(14f, 0.04f, 12f),
                new Vector3(-16f, 0.04f, 8f),
                new Vector3(8f, 0.04f, -18f),
                new Vector3(-10f, 0.04f, -14f),
            };
            for (var i = 0; i < patches.Length; i++)
            {
                var p = patches[i];
                Box(floor.transform, "Soil_" + i,
                    new Vector3(p.x, p.y, p.z),
                    new Vector3(9f + i, 0.04f, 7f + (i % 3)),
                    cracked);
            }

            Box(floor.transform, "Plaza",
                new Vector3(playArea.center.x, 0.05f, playArea.center.z),
                new Vector3(11f, 0.08f, 11f),
                deck);
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

        private static void RaiseHab(
            Transform parent, string name, Vector3 pos, Vector3 size, Material metal, Material roof,
            int stories)
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
            var trim = TrimMaterial();
            var y = 0f;
            for (var s = 0; s < stories; s++)
            {
                var shrink = 1f - (s * 0.16f);
                var storyH = s == 0 ? size.y : Mathf.Max(2.6f, size.y * (0.58f - s * 0.07f));
                var sx = size.x * shrink;
                var sz = size.z * shrink;
                var ox = pos.x + (s * size.x * 0.07f);
                var oz = pos.z;
                var bodyName = s == 0 ? "Body" : "Stack_" + s;
                var roofName = s == 0 ? "Roof" : "StackRoof_" + s;
                var corniceName = s == 0 ? "Cornice" : "Cornice_" + s;

                Box(hab.transform, bodyName,
                    new Vector3(ox, y + storyH * 0.5f, oz),
                    new Vector3(sx, storyH, sz),
                    metal);
                // Western wear: a thin wood eave so the dark roof cap is a separate plane,
                // not the same brown as the wall, at 62° down.
                Box(hab.transform, corniceName,
                    new Vector3(ox, y + storyH + 0.08f, oz),
                    new Vector3(sx + 0.55f, 0.16f, sz + 0.55f),
                    trim);
                Box(hab.transform, roofName,
                    new Vector3(ox, y + storyH + 0.42f, oz),
                    new Vector3(sx + 0.9f, 0.58f, sz + 0.9f),
                    roof);

                y += storyH + 0.58f;
            }
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
            // Dark umber tint so roofs read as a separate plane from the lifted walls.
            var tex = ViewLook.LoadTexture("RedHollowArt/hab-block-roof")
                ?? ViewLook.LoadTexture("RedHollowArt/colony-decking")
                ?? ViewLook.LoadTexture("RedHollowArt/hab-block-cladding");
            var mat = ViewLook.Unlit(tex != null ? RoofTint : Roof, tex);
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
            var mat = ViewLook.Unlit(tex != null ? HabWallTint : Metal, tex);
            if (mat != null && tex != null)
            {
                ViewLook.SetTiling(mat, new Vector2(1.05f, 1.35f));
            }

            return mat;
        }

        private static Material DeckingMaterial()
        {
            return Tiled("RedHollowArt/colony-decking", new Color(0.82f, 0.58f, 0.36f),
                new Vector2(4f, 4f), "RedHollowArt/metal-floor-plate");
        }

        private static Material TrimMaterial()
        {
            var tex = ViewLook.LoadTexture("RedHollowArt/wood-trim");
            return ViewLook.Unlit(tex != null ? new Color(0.75f, 0.55f, 0.32f) : WoodTrim, tex);
        }

        private static Material Tiled(string resourcePath, Color tint, Vector2 scale,
            string altPath = null)
        {
            var tex = ViewLook.LoadTexture(resourcePath)
                ?? (altPath != null ? ViewLook.LoadTexture(altPath) : null);
            var mat = ViewLook.Unlit(tint, tex);
            if (mat != null && tex != null)
            {
                ViewLook.SetTiling(mat, scale);
            }

            return mat;
        }

        private static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            ViewLook.StripCollider(go);
            ViewLook.Paint(go, material);
            return go;
        }
    }
}
