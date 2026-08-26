using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Lykos greybox (DEC-026): a fully 3D terraformed-Mars underground colony with
    /// real building height under the 65° camera. Mix ~70% Martian habitat (rusted
    /// stacks, gantries, carved rock, lantern masts, lift shaft) / ~30% western
    /// (brass/wood/lantern trim on those hulls). Not a western town, not suburban
    /// boxes. Deterministic — no <see cref="Random"/>.
    /// </summary>
    internal static class CavernEnvironment
    {
        internal static readonly Vec2 LiftShaft = new Vec2(6.0, 20.0);

        private static readonly Color Rock = new Color(0.22f, 0.12f, 0.07f);
        private static readonly Color RockDeep = new Color(0.10f, 0.055f, 0.03f);
        private static readonly Color Hull = new Color(0.36f, 0.20f, 0.12f);
        private static readonly Color HullDark = new Color(0.22f, 0.12f, 0.08f);
        private static readonly Color Dust = new Color(0.48f, 0.34f, 0.20f);
        private static readonly Color Roof = new Color(0.26f, 0.14f, 0.09f);
        private static readonly Color Brass = new Color(0.58f, 0.40f, 0.16f);
        private static readonly Color Timber = new Color(0.30f, 0.18f, 0.09f);
        private static readonly Color AmberGlow = new Color(1.0f, 0.70f, 0.28f);

        internal static GameObject Build(Transform root, ColonyMap map, float coverSpan)
        {
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root, false);
            ground.transform.position = new Vector3(0f, SimSpace.GroundHeight, 0f);

            var rock = TopDownArt.LitMaterial(Rock, 0.08f);
            var rockDeep = TopDownArt.LitMaterial(RockDeep, 0.04f);
            var hull = TopDownArt.LitMaterial(Hull, 0.12f);
            var hullDark = TopDownArt.LitMaterial(HullDark, 0.10f);
            var dust = TopDownArt.LitMaterial(Dust, 0.14f);
            var roof = TopDownArt.LitMaterial(Roof, 0.08f);
            var brass = TopDownArt.LitMaterial(Brass, 0.22f);
            var timber = TopDownArt.LitMaterial(Timber, 0.10f);
            var glow = TopDownArt.EmissiveMaterial(AmberGlow, 3.5f);

            var cavernAlbedo = Resources.Load<Texture2D>("RedHollowArt/cavern-ground");
            TopDownArt.BindAlbedo(rock, cavernAlbedo, 5f);
            TopDownArt.BindAlbedo(rockDeep, cavernAlbedo, 7f);
            var rust = TopDownArt.RustPlate();
            TopDownArt.BindAlbedo(hull, rust, 3.2f);
            TopDownArt.BindAlbedo(hullDark, rust, 4.1f);
            TopDownArt.BindAlbedo(dust, rust, 2.4f);
            TopDownArt.BindAlbedo(roof, rust, 2.0f);

            var half = coverSpan * 0.5f;
            var mats = new ColonyMats
            {
                Hull = hull,
                HullDark = hullDark,
                Dust = dust,
                Roof = roof,
                Brass = brass,
                Timber = timber,
                Glow = glow,
                Rock = rockDeep,
            };

            LayRockFloor(ground.transform, half, rock, rockDeep);
            RaisePlaza(ground.transform, mats);
            RaiseCliffs(ground.transform, half, rockDeep);
            RaiseColony(ground.transform, map, half, mats);
            RaiseLiftShaft(ground.transform, mats);
            RaiseIndustry(ground.transform, map, half, mats);

            return ground;
        }

        private struct ColonyMats
        {
            public Material Hull;
            public Material HullDark;
            public Material Dust;
            public Material Roof;
            public Material Brass;
            public Material Timber;
            public Material Glow;
            public Material Rock;
        }

        private static void LayRockFloor(Transform parent, float half, Material rock, Material deep)
        {
            const int cells = 8;
            var span = half * 2f;
            var cell = span / cells;

            for (var ix = 0; ix < cells; ix++)
            {
                for (var iz = 0; iz < cells; iz++)
                {
                    var x = -half + (ix + 0.5f) * cell;
                    var z = -half + (iz + 0.5f) * cell;
                    var h = Hash(ix, iz);
                    var plaza = new Vector2(x, z).magnitude < 9f;
                    var top = plaza ? 0f : (h * 1.35f);
                    var thickness = 1.6f + Hash(ix + 3, iz + 7) * 3.2f;
                    Box(
                        parent, "rock_" + ix + "_" + iz, h > 0.62f ? deep : rock,
                        new Vector3(x, top - thickness * 0.5f, z),
                        new Vector3(cell * 1.04f, thickness, cell * 1.04f));
                }
            }
        }

        private static void RaiseCliffs(Transform parent, float half, Material rock)
        {
            const float wall = 14f;
            const float height = 32f;
            var length = half * 2f + wall;

            Box(parent, "cliff_south", rock,
                new Vector3(0f, height * 0.55f, -half - wall * 0.35f),
                new Vector3(length, height * 1.15f, wall));
            Box(parent, "cliff_north", rock,
                new Vector3(0f, height * 0.5f, half + wall * 0.35f),
                new Vector3(length, height, wall));
            Box(parent, "cliff_west", rock,
                new Vector3(-half - wall * 0.35f, height * 0.5f, 0f),
                new Vector3(wall, height, length));
            Box(parent, "cliff_east", rock,
                new Vector3(half + wall * 0.35f, height * 0.5f, 0f),
                new Vector3(wall, height, length));
        }

        /// <summary>Industrial landing pad at team spawn — metal deck, not a town square.</summary>
        private static void RaisePlaza(Transform parent, ColonyMats mats)
        {
            Box(parent, "plaza_deck", mats.Hull,
                new Vector3(0f, -0.12f, 0f),
                new Vector3(16f, 0.24f, 16f));
            Box(parent, "plaza_ring", mats.Brass,
                new Vector3(0f, 0.08f, 0f),
                new Vector3(9.2f, 0.12f, 9.2f));
            Box(parent, "plaza_core", mats.Glow,
                new Vector3(0f, 0.16f, 0f),
                new Vector3(2.2f, 0.08f, 2.2f));
        }

        /// <summary>
        /// Fewer, taller habitat stacks so the 65° camera reads walls — not a field of
        /// short western-town boxes.
        /// </summary>
        private static void RaiseColony(Transform parent, ColonyMap map, float half, ColonyMats mats)
        {
            if (map != null)
            {
                foreach (var spec in map.Hotspots)
                {
                    if (spec == null)
                    {
                        continue;
                    }

                    var world = SimSpace.ToWorld(spec.Pos);
                    HabitatCluster(parent, spec.Id, world.x, world.z, mats);
                }
            }

            const float step = 11f;
            var n = 0;
            for (var x = -half + 12f; x <= half - 12f; x += step)
            {
                for (var z = -half + 12f; z <= half - 12f; z += step)
                {
                    var h = Hash((int)(x * 3f), (int)(z * 3f));
                    if (h < 0.58f)
                    {
                        continue;
                    }

                    if (new Vector2(x, z).magnitude < 12f)
                    {
                        continue;
                    }

                    if (Mathf.Abs(x) < 4.5f || Mathf.Abs(z) < 4.5f)
                    {
                        continue;
                    }

                    if (NearHotspot(map, x, z, 11f))
                    {
                        continue;
                    }

                    var storeys = 3 + (int)(Hash((int)x + 11, (int)z + 19) * 3.5f);
                    Habitat(
                        parent, "hab_" + n, x, z,
                        5.2f + h * 3.4f, 4.6f + Hash((int)z, (int)x) * 3.0f,
                        storeys, mats, h);
                    n++;
                }
            }
        }

        private static void HabitatCluster(
            Transform parent, string id, float x, float z, ColonyMats mats)
        {
            // Carved-rock socket the habitat is cut into.
            Box(parent, id + "_socket", mats.Rock,
                new Vector3(x, 1.1f, z),
                new Vector3(16f, 2.2f, 16f));

            var count = 4;
            for (var i = 0; i < count; i++)
            {
                var a = Hash(i * 17 + Stable(id), i * 13);
                var b = Hash(i * 29, Stable(id) + i);
                var px = x + (a - 0.5f) * 10f;
                var pz = z + (b - 0.5f) * 10f;
                var storeys = 5 + (int)(Hash(i + 3, Stable(id)) * 3.2f);
                Habitat(
                    parent, id + "_" + i, px, pz,
                    5.4f + a * 3.2f, 4.8f + b * 2.8f,
                    storeys, mats, b);
            }

            Mast(parent, id + "_mast", x + 5.5f, z - 4.2f, 14f, mats);
        }

        /// <summary>
        /// One habitat: tall rusted stack with setbacks (sides read at 65°), brass/timber
        /// trim (the 30%), roof lamp, optional mast.
        /// </summary>
        private static void Habitat(
            Transform parent, string name, float x, float z,
            float wide, float deep, int storeys, ColonyMats mats, float salt)
        {
            var y = 0f;
            for (var s = 0; s < storeys; s++)
            {
                var inset = s * 0.72f;
                var h = 3.6f + Hash(s + 2, Stable(name)) * 1.6f;
                var w = Mathf.Max(2.2f, wide - inset);
                var d = Mathf.Max(2.2f, deep - inset * 0.9f);
                var wall = (s % 2 == 0) ? mats.Hull : mats.HullDark;
                if (salt > 0.78f && s == 0)
                {
                    wall = mats.Dust;
                }

                var mat = s == storeys - 1 ? mats.Roof : wall;
                Box(
                    parent, name + "_s" + s, mat,
                    new Vector3(x, y + h * 0.5f, z),
                    new Vector3(w, h, d));

                // Camera looks north: south (−Z) faces carry window glow so walls read occupied.
                if (s > 0 && s < storeys - 1)
                {
                    Box(
                        parent, name + "_win" + s, mats.Glow,
                        new Vector3(x + (Hash(s, Stable(name)) - 0.5f) * w * 0.25f,
                            y + h * 0.55f, z - d * 0.51f),
                        new Vector3(Mathf.Min(1.1f, w * 0.28f), Mathf.Min(1.4f, h * 0.38f), 0.12f));
                }

                y += h;
            }

            // 30% western: a brass or timber band on the hull, not a porch.
            var bandY = y * 0.42f;
            var bandMat = salt > 0.5f ? mats.Brass : mats.Timber;
            Box(
                parent, name + "_trim", bandMat,
                new Vector3(x, bandY, z),
                new Vector3(Mathf.Max(2.4f, wide * 1.02f), 0.28f, Mathf.Max(2.4f, deep * 1.02f)));

            Box(
                parent, name + "_lamp", mats.Glow,
                new Vector3(x + (salt - 0.5f) * wide * 0.35f, y + 0.35f, z),
                new Vector3(0.55f, 0.55f, 0.55f));

            // Exhaust stack — industrial silhouette, not a chimney or steeple.
            if (salt > 0.35f)
            {
                Cylinder(
                    parent, name + "_vent", mats.HullDark,
                    new Vector3(x - wide * 0.22f, y + 1.5f, z + deep * 0.15f),
                    new Vector3(0.55f, 3.0f, 0.55f));
            }

            if (salt > 0.62f)
            {
                Mast(parent, name + "_ant", x + wide * 0.28f, z + deep * 0.2f, 6f + salt * 5f, mats);
            }
        }

        private static void Mast(
            Transform parent, string name, float x, float z, float height, ColonyMats mats)
        {
            Box(parent, name + "_pole", mats.HullDark,
                new Vector3(x, height * 0.5f, z),
                new Vector3(0.28f, height, 0.28f));
            Box(parent, name + "_arm", mats.Brass,
                new Vector3(x + 0.7f, height - 0.4f, z),
                new Vector3(1.6f, 0.18f, 0.18f));
            Box(parent, name + "_lamp", mats.Glow,
                new Vector3(x + 1.35f, height - 0.55f, z),
                new Vector3(0.4f, 0.4f, 0.4f));
        }

        private static void RaiseLiftShaft(Transform parent, ColonyMats mats)
        {
            var world = SimSpace.ToWorld(LiftShaft);
            const float height = 28f;
            Box(parent, "lift_shaft", mats.Glow,
                new Vector3(world.x, height * 0.5f, world.z),
                new Vector3(2.2f, height, 2.2f));
            Box(parent, "lift_rail_a", mats.HullDark,
                new Vector3(world.x - 1.4f, height * 0.5f, world.z),
                new Vector3(0.22f, height, 0.22f));
            Box(parent, "lift_rail_b", mats.HullDark,
                new Vector3(world.x + 1.4f, height * 0.5f, world.z),
                new Vector3(0.22f, height, 0.22f));
            Box(parent, "lift_gantry", mats.Hull,
                new Vector3(world.x + 4f, height - 2f, world.z),
                new Vector3(8f, 0.35f, 0.45f));
            Box(parent, "lift_cap", mats.Glow,
                new Vector3(world.x, height + 0.5f, world.z),
                new Vector3(3.0f, 0.45f, 3.0f));
            Cylinder(parent, "lift_collar", mats.Hull,
                new Vector3(world.x, 2.2f, world.z),
                new Vector3(5.4f, 4.4f, 5.4f));
        }

        /// <summary>Industrial gantries between habitats — Mars silhouette, not streets.</summary>
        private static void RaiseIndustry(Transform parent, ColonyMap map, float half, ColonyMats mats)
        {
            if (map != null && map.Hotspots.Count >= 2)
            {
                for (var i = 0; i < map.Hotspots.Count; i++)
                {
                    var a = SimSpace.ToWorld(map.Hotspots[i].Pos);
                    var b = SimSpace.ToWorld(map.Hotspots[(i + 1) % map.Hotspots.Count].Pos);
                    var y = 11f + i * 2.5f;
                    Beam(parent, "gantry_" + i, mats.HullDark,
                        new Vector3(a.x, y, a.z), new Vector3(b.x, y, b.z), 0.32f);
                    StringLights(parent, "gantry_" + i + "_lamps", mats.Glow,
                        new Vector3(a.x, y + 0.35f, a.z), new Vector3(b.x, y + 0.35f, b.z));
                }
            }

            // Cardinal gantries toward the lift — industrial, not wagon ruts.
            var lift = SimSpace.ToWorld(LiftShaft);
            Beam(parent, "gantry_lift", mats.Hull,
                new Vector3(0f, 13f, 0f), new Vector3(lift.x, 16f, lift.z), 0.28f);
            StringLights(parent, "gantry_lift_lamps", mats.Glow,
                new Vector3(0f, 13.4f, 0f), new Vector3(lift.x, 16.4f, lift.z));

            Mast(parent, "mast_plaza", 7.5f, -6.5f, 11f, mats);
            Mast(parent, "mast_west", -half * 0.35f, 8f, 13f, mats);

            // Pressure tanks / airlock cylinders — round habitat hardware, not barns.
            Airlock(parent, "airlock_sw", -half + 10f, -half + 8f, mats);
            Airlock(parent, "airlock_ne", half - 9f, half - 7f, mats);
            Airlock(parent, "airlock_s", -14f, -half + 6f, mats);
        }

        private static void Airlock(
            Transform parent, string name, float x, float z, ColonyMats mats)
        {
            Cylinder(parent, name + "_tank", mats.HullDark,
                new Vector3(x, 3.4f, z),
                new Vector3(4.4f, 6.8f, 4.4f));
            Cylinder(parent, name + "_cap", mats.Brass,
                new Vector3(x, 7.0f, z),
                new Vector3(4.8f, 0.44f, 4.8f));
            Box(parent, name + "_pipe", mats.Hull,
                new Vector3(x + 3.2f, 2.4f, z),
                new Vector3(3.6f, 0.45f, 0.45f));
        }

        private static bool NearHotspot(ColonyMap map, float x, float z, float radius)
        {
            if (map == null)
            {
                return false;
            }

            foreach (var spec in map.Hotspots)
            {
                if (spec == null)
                {
                    continue;
                }

                var p = SimSpace.ToWorld(spec.Pos);
                if (new Vector2(p.x - x, p.z - z).magnitude < radius)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Beam(
            Transform parent, string name, Material material, Vector3 from, Vector3 to, float thickness)
        {
            var delta = to - from;
            var length = delta.magnitude;
            if (length < 0.2f)
            {
                return;
            }

            Box(
                parent, name, material,
                (from + to) * 0.5f,
                new Vector3(thickness, thickness, length),
                Quaternion.LookRotation(delta / length, Vector3.up));
        }

        private static void StringLights(
            Transform parent, string name, Material glow, Vector3 from, Vector3 to)
        {
            var delta = to - from;
            var length = delta.magnitude;
            if (length < 1f)
            {
                return;
            }

            var count = Mathf.Max(2, (int)(length / 7f));
            for (var i = 1; i < count; i++)
            {
                var t = i / (float)count;
                Box(
                    parent, name + "_" + i, glow,
                    Vector3.Lerp(from, to, t),
                    new Vector3(0.32f, 0.32f, 0.32f));
            }
        }

        /// <summary>
        /// Cave-mouth geometry parented under an entry-tunnel marker so pulse/flare tints
        /// the breach. Faces the spawn plaza. Does not move the marker (T16 pins position).
        /// </summary>
        internal static void AttachBreachMouth(Transform marker)
        {
            if (marker == null)
            {
                return;
            }

            var pos = marker.position;
            var toCenter = new Vector3(-pos.x, 0f, -pos.z);
            if (toCenter.sqrMagnitude < 0.01f)
            {
                toCenter = Vector3.forward;
            }

            var inward = toCenter.normalized;
            var right = Vector3.Cross(Vector3.up, inward).normalized;
            var facing = Quaternion.LookRotation(inward, Vector3.up);

            var rock = TopDownArt.LitMaterial(new Color(0.10f, 0.055f, 0.03f), 0.04f);
            TopDownArt.BindAlbedo(
                rock, Resources.Load<Texture2D>("RedHollowArt/cavern-ground"), 4f);
            var hull = TopDownArt.LitMaterial(new Color(0.22f, 0.12f, 0.08f), 0.10f);
            TopDownArt.BindAlbedo(hull, TopDownArt.RustPlate(), 3f);
            var glow = TopDownArt.EmissiveMaterial(new Color(1.0f, 0.55f, 0.18f), 2.8f);
            var throat = TopDownArt.LitMaterial(new Color(0.04f, 0.02f, 0.012f), 0.02f);

            Box(marker, "breach_pillar_l", hull,
                pos + (right * -2.4f) + (Vector3.up * 4.2f),
                new Vector3(1.1f, 8.4f, 1.4f), facing);
            Box(marker, "breach_pillar_r", hull,
                pos + (right * 2.4f) + (Vector3.up * 4.2f),
                new Vector3(1.1f, 8.4f, 1.4f), facing);
            Box(marker, "breach_lintel", hull,
                pos + (Vector3.up * 8.5f),
                new Vector3(6.2f, 1.1f, 1.6f), facing);
            Box(marker, "breach_throat", throat,
                pos - (inward * 2.8f) + (Vector3.up * 4.0f),
                new Vector3(5.4f, 8.0f, 4.2f), facing);
            Box(marker, "breach_rim", glow,
                pos + (inward * 0.15f) + (Vector3.up * 3.6f),
                new Vector3(4.6f, 0.22f, 0.22f), facing);
            Box(marker, "breach_sill", rock,
                pos - (inward * 0.4f) + (Vector3.up * 0.4f),
                new Vector3(6.4f, 0.8f, 2.4f), facing);
        }

        private static void Box(
            Transform parent, string name, Material material, Vector3 center, Vector3 size)
        {
            Box(parent, name, material, center, size, Quaternion.identity);
        }

        private static void Box(
            Transform parent, string name, Material material, Vector3 center, Vector3 size,
            Quaternion rotation)
        {
            GameObject go;
            try
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            }
            catch (System.Exception)
            {
                go = new GameObject(name);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(center, rotation);
                go.transform.localScale = size;
                return;
            }

            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, rotation);
            go.transform.localScale = size;
            TopDownArt.StripCollider(go);
            TopDownArt.PaintLit(go, material);
        }

        /// <summary>
        /// World-size cylinder (Y-up). Unity's primitive is 2 units tall / 1 unit diameter,
        /// so Y scale is half the requested height — callers pass extents like <see cref="Box"/>.
        /// </summary>
        private static void Cylinder(
            Transform parent, string name, Material material, Vector3 center, Vector3 size)
        {
            var scale = new Vector3(size.x, size.y * 0.5f, size.z);
            GameObject go;
            try
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            }
            catch (System.Exception)
            {
                go = new GameObject(name);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(center, Quaternion.identity);
                go.transform.localScale = scale;
                return;
            }

            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, Quaternion.identity);
            go.transform.localScale = scale;
            TopDownArt.StripCollider(go);
            TopDownArt.PaintLit(go, material);
        }

        private static float Hash(int x, int z)
        {
            unchecked
            {
                var n = (uint)(x * 374761393 + z * 668265263);
                n = (n ^ (n >> 13)) * 1274126177u;
                return (n & 0xFFFF) / 65535f;
            }
        }

        private static int Stable(string s)
        {
            unchecked
            {
                var h = 23;
                if (s == null)
                {
                    return h;
                }

                for (var i = 0; i < s.Length; i++)
                {
                    h = h * 31 + s[i];
                }

                return h;
            }
        }
    }
}
