using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RedHollow.Game.View
{
    /// <summary>
    /// Runtime loader for the CC0 Quaternius Modular Sci-Fi MegaKit (Standard).
    /// Instances live under Resources/SciFiKit so EditMode tests and Play mode share
    /// one path. Presentation only — no sim types, no MonoBehaviour.
    /// </summary>
    public static class SciFiKit
    {
        /// <summary>Native kit grid, metres. Walls sit on the -X face of this cell.</summary>
        public const float NativeGrid = 4f;

        /// <summary>Native storey height (WallAstra / WallBand), metres.</summary>
        public const float NativeStory = 3f;

        /// <summary>
        /// World multiplier so a kit storey reads as industrial habitat (hero ~2 units,
        /// habs several stories). Native 4m cell → 8 world units; 3m wall → 6.
        /// </summary>
        public const float WorldScale = 2f;

        public const float Grid = NativeGrid * WorldScale;
        public const float StoryHeight = NativeStory * WorldScale;

        public const string Walls = "SciFiKit/Walls/";
        public const string Platforms = "SciFiKit/Platforms/";
        public const string Columns = "SciFiKit/Columns/";
        public const string Props = "SciFiKit/Props/";

        public const string WallSolid = Walls + "WallAstra_Straight";
        public const string WallBand = Walls + "WallBand_Straight";
        public const string WallWindow = Walls + "WallAstra_Straight_Window";
        public const string WallFlatWindow = Walls + "WallAstra_Straight_Flat_Window";
        public const string WallWindowStrip = Walls + "WallWindow_Straight";
        public const string TopTrim = Walls + "TopAstra_Straight";
        public const string TopCables = Walls + "TopCables_Straight";
        public const string BottomTrim = Walls + "BottomMetal_Straight";
        public const string FloorDark = Platforms + "Platform_DarkPlates";
        public const string FloorMetal = Platforms + "Platform_Metal";
        public const string FloorSimple = Platforms + "Platform_Simple";
        public const string FloorSquares = Platforms + "Platform_Squares";
        public const string FloorPlates = Platforms + "Platform_3Plates";
        public const string DoorSimple = Platforms + "Door_Simple";
        public const string ColumnStory = Columns + "Column_Astra";
        public const string ColumnTall = Columns + "Column_Simple";
        public const string ColumnPipes = Columns + "Column_Pipes";
        public const string LightWide = Props + "Prop_Light_Wide";
        public const string LightSmall = Props + "Prop_Light_Small";
        public const string Vent = Props + "Prop_Vent_Big";
        public const string CrateA = Props + "Prop_Crate3";
        public const string CrateB = Props + "Prop_Crate4";
        public const string Barrel = Props + "Prop_Barrel_Large";
        public const string Cable = Props + "Prop_Cable_1";
        public const string PipeHolder = Props + "Prop_PipeHolder";

        /// <summary>West face (kit default, -X). Unity yaw degrees for the other three.</summary>
        public static readonly Quaternion FaceWest = Quaternion.identity;
        public static readonly Quaternion FaceEast = Quaternion.Euler(0f, 180f, 0f);
        public static readonly Quaternion FaceNorth = Quaternion.Euler(0f, 90f, 0f);
        public static readonly Quaternion FaceSouth = Quaternion.Euler(0f, -90f, 0f);

        private static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();
        private static int _available = -1;

        public static bool Available
        {
            get
            {
                if (_available < 0)
                {
                    _available = Load(WallSolid) != null || Load(FloorDark) != null ? 1 : 0;
                }

                return _available == 1;
            }
        }

        public static GameObject Load(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
            {
                return null;
            }

            GameObject prefab;
            if (Cache.TryGetValue(resourcePath, out prefab) && prefab != null)
            {
                return prefab;
            }

            prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab != null)
            {
                Cache[resourcePath] = prefab;
            }

            return prefab;
        }

        /// <summary>
        /// Instance a kit module in parent space, scaled to <see cref="WorldScale"/>,
        /// retextured with a Lykos URP Lit material. Returns null if the FBX is missing (R-15).
        /// </summary>
        public static GameObject Place(
            Transform parent, string name, string resourcePath,
            Vector3 localPos, Quaternion localRot, Material material,
            bool castShadows = true)
        {
            var prefab = Load(resourcePath);
            if (prefab == null)
            {
                return null;
            }

            var go = Object.Instantiate(prefab, parent);
            go.name = name;
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = Vector3.one * WorldScale;
            Strip(go);
            Paint(go, material, castShadows);
            SitOnLocalY(go);
            return go;
        }

        /// <summary>
        /// Slide the instance so its rendered bounds sit on the local Y it was placed at.
        /// Kit pivots vary (floor vs mid-wall); without this, roofs and columns float.
        /// </summary>
        public static void SitOnLocalY(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            var dy = go.transform.position.y - bounds.min.y;
            if (Mathf.Abs(dy) > 0.001f)
            {
                go.transform.position += new Vector3(0f, dy, 0f);
            }
        }

        public static void Strip(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var colliders = go.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    Object.DestroyImmediate(colliders[i]);
                }
            }

            var animators = go.GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                if (animators[i] != null)
                {
                    Object.DestroyImmediate(animators[i]);
                }
            }
        }

        public static void Paint(GameObject go, Material material, bool castShadows)
        {
            if (go == null || material == null)
            {
                return;
            }

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var slots = renderer.sharedMaterials;
                var count = slots != null && slots.Length > 0 ? slots.Length : 1;
                var painted = new Material[count];
                for (var m = 0; m < count; m++)
                {
                    painted[m] = material;
                }

                renderer.sharedMaterials = painted;
                var lit = ViewLook.IsLitShader(material);
                renderer.shadowCastingMode = lit && castShadows
                    ? ShadowCastingMode.On
                    : ShadowCastingMode.Off;
                renderer.receiveShadows = lit;
            }
        }
    }
}
