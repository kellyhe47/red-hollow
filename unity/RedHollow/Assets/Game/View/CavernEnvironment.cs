using RedHollow.Sim;
using UnityEngine;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Lykos seed set dressing (owner override, 2026-08-26): a 3D subterranean colony of
    /// simple meshes, URP Lit materials, sourced amber lights and fog — not a tiled floor
    /// quad. Western look stays on character sprites only. Building style is stacked
    /// blocky hive architecture (flat roofs, dusty beige), matching seed-env.webp.
    ///
    /// Raised at runtime so the same description feeds Play, EditMode, and the headless
    /// scene builder. Deterministic: no <see cref="Random"/>, so a test that builds the
    /// scene twice gets the same rocks.
    /// </summary>
    internal static class CavernEnvironment
    {
        /// <summary>World XZ of the glowing lift-shaft landmark (sim ground space).</summary>
        internal static readonly Vec2 LiftShaft = new Vec2(6.0, 20.0);

        private static readonly Color Rock = new Color(0.22f, 0.12f, 0.07f);
        private static readonly Color RockDeep = new Color(0.10f, 0.055f, 0.03f);
        private static readonly Color Stone = new Color(0.62f, 0.55f, 0.44f);
        private static readonly Color StoneWarm = new Color(0.70f, 0.60f, 0.46f);
        private static readonly Color Roof = new Color(0.48f, 0.42f, 0.34f);
        private static readonly Color AmberGlow = new Color(1.0f, 0.70f, 0.28f);

        /// <summary>
        /// Parent named Ground (T16) holding the cavern floor, cliffs, stacked colony,
        /// and the lift shaft. No textured plane — the floor is rock slabs.
        /// </summary>
        internal static GameObject Build(Transform root, ColonyMap map, float coverSpan)
        {
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root, false);
            ground.transform.position = new Vector3(0f, SimSpace.GroundHeight, 0f);

            var rock = TopDownArt.LitMaterial(Rock, 0.08f);
            var rockDeep = TopDownArt.LitMaterial(RockDeep, 0.04f);
            var stone = TopDownArt.LitMaterial(Stone, 0.18f);
            var stoneWarm = TopDownArt.LitMaterial(StoneWarm, 0.16f);
            var roof = TopDownArt.LitMaterial(Roof, 0.14f);
            var glow = TopDownArt.EmissiveMaterial(AmberGlow, 3.5f);

            // Cavern-ground tile wraps the rock meshes (UVs), never as a floor quad.
            var cavernAlbedo = Resources.Load<Texture2D>("RedHollowArt/cavern-ground");
            TopDownArt.BindAlbedo(rock, cavernAlbedo, 5f);
            TopDownArt.BindAlbedo(rockDeep, cavernAlbedo, 7f);

            var half = coverSpan * 0.5f;
            LayRockFloor(ground.transform, half, rock, rockDeep);
            RaiseCliffs(ground.transform, half, rockDeep);
            RaiseColony(ground.transform, map, half, stone, stoneWarm, roof, glow);
            RaiseLiftShaft(ground.transform, glow);

            return ground;
        }

        /// <summary>
        /// Uneven rock slabs whose tops sit at or below the sim floor. Variation is
        /// thickness and terrace height — never a single tiled quad.
        /// </summary>
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
                    var mat = h > 0.62f ? deep : rock;
                    Box(
                        parent, "rock_" + ix + "_" + iz, mat,
                        new Vector3(x, top - thickness * 0.5f, z),
                        new Vector3(cell * 1.04f, thickness, cell * 1.04f));
                }
            }
        }

        /// <summary>High cavern walls around the 16:9 cover so the Game view never pillarboxes.</summary>
        private static void RaiseCliffs(Transform parent, float half, Material rock)
        {
            const float wall = 14f;
            const float height = 26f;
            var length = half * 2f + wall;

            // Extra-tall near-camera ridge (world -Z is the bottom of the y-down frame).
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

        private static void RaiseColony(
            Transform parent, ColonyMap map, float half,
            Material stone, Material stoneWarm, Material roof, Material glow)
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
                    Cluster(parent, spec.Id, world.x, world.z, 5, 11f, stone, stoneWarm, roof, glow);
                }
            }

            // Dense hive fill — skip the spawn plaza and the cardinal roads to the tunnels
            // so heroes stay readable. Buildings still dwarf the 2D sprites.
            const float step = 7.5f;
            var n = 0;
            for (var x = -half + 10f; x <= half - 10f; x += step)
            {
                for (var z = -half + 10f; z <= half - 10f; z += step)
                {
                    var h = Hash((int)(x * 3f), (int)(z * 3f));
                    if (h < 0.42f)
                    {
                        continue;
                    }

                    if (new Vector2(x, z).magnitude < 10f)
                    {
                        continue;
                    }

                    if (Mathf.Abs(x) < 4.2f || Mathf.Abs(z) < 4.2f)
                    {
                        continue;
                    }

                    if (NearHotspot(map, x, z, 9f))
                    {
                        continue;
                    }

                    var storeys = 1 + (int)(Hash((int)x + 11, (int)z + 19) * 3.2f);
                    var wide = 3.6f + h * 4.4f;
                    var deep = 3.2f + Hash((int)z, (int)x) * 3.8f;
                    Stack(
                        parent, "block_" + n, x, z, wide, deep, storeys,
                        h > 0.7f ? stoneWarm : stone, roof, glow, h);
                    n++;
                }
            }
        }

        private static void Cluster(
            Transform parent, string id, float x, float z, int count, float radius,
            Material stone, Material stoneWarm, Material roof, Material glow)
        {
            for (var i = 0; i < count; i++)
            {
                var a = Hash(i * 17 + Stable(id), i * 13);
                var b = Hash(i * 29, Stable(id) + i);
                var px = x + (a - 0.5f) * radius * 2f;
                var pz = z + (b - 0.5f) * radius * 2f;
                var storeys = 2 + (int)(Hash(i + 3, Stable(id)) * 3.4f);
                var wide = 4.2f + a * 5.5f;
                var deep = 3.8f + b * 5.0f;
                Stack(
                    parent, id + "_" + i, px, pz, wide, deep, storeys,
                    a > 0.5f ? stone : stoneWarm, roof, glow, b);
            }
        }

        private static void Stack(
            Transform parent, string name, float x, float z,
            float wide, float deep, int storeys, Material wall, Material roof, Material glow,
            float salt)
        {
            var y = 0f;
            for (var s = 0; s < storeys; s++)
            {
                var inset = s * 0.55f;
                var h = 3.1f + Hash(s + 2, Stable(name)) * 2.4f;
                var w = Mathf.Max(1.6f, wide - inset);
                var d = Mathf.Max(1.6f, deep - inset * 0.85f);
                var mat = s == storeys - 1 ? roof : wall;
                Box(
                    parent, name + "_s" + s, mat,
                    new Vector3(x, y + h * 0.5f, z),
                    new Vector3(w, h, d));
                y += h;
            }

            // Roof lantern / window dots — emissive, not extra Light components (URP per-object cap).
            if (salt > 0.55f)
            {
                Box(
                    parent, name + "_lamp", glow,
                    new Vector3(x + (salt - 0.75f) * wide * 0.4f, y + 0.25f, z),
                    new Vector3(0.45f, 0.45f, 0.45f));
            }
        }

        private static void RaiseLiftShaft(Transform parent, Material glow)
        {
            var world = SimSpace.ToWorld(LiftShaft);
            const float height = 24f;
            Box(
                parent, "lift_shaft", glow,
                new Vector3(world.x, height * 0.5f, world.z),
                new Vector3(2.4f, height, 2.4f));
            Box(
                parent, "lift_shaft_cap", glow,
                new Vector3(world.x, height + 0.6f, world.z),
                new Vector3(3.2f, 0.5f, 3.2f));
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

        private static void Box(Transform parent, string name, Material material, Vector3 center, Vector3 size)
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
                go.transform.position = center;
                go.transform.localScale = size;
                return;
            }

            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            TopDownArt.StripCollider(go);
            TopDownArt.PaintLit(go, material);
        }

        /// <summary>Stable 0..1 hash — same inputs, same cavern, no Unity RNG.</summary>
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
