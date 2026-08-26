using System;
using System.Collections.Generic;
using RedHollow.Game.UI;
using RedHollow.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RedHollow.Game.View
{
    /// <summary>What a built match scene consists of. Handed back so a headless build can verify it.</summary>
    public sealed class MatchScene
    {
        /// <summary>Everything below is parented here, so a scene can be torn down in one call.</summary>
        public GameObject Root;

        /// <summary>R-30 — the top-down camera over the colony.</summary>
        public Camera Camera;

        public GameObject Ground;

        /// <summary>R-10 / R-33 — where heroes enter and respawn.</summary>
        public GameObject TeamSpawn;

        /// <summary>R-10 — one marker per <see cref="ColonyMap.Hotspots"/> entry, keyed by hotspot id.</summary>
        public readonly Dictionary<string, GameObject> HotspotMarkers = new Dictionary<string, GameObject>();

        /// <summary>
        /// T-26 / R-14 — one marker per <see cref="ColonyMap.EntryTunnels"/> entry, keyed by the
        /// tunnel's index in that list (the same index the wave preview and the entry flare name).
        /// </summary>
        public readonly Dictionary<int, GameObject> EntryTunnelMarkers = new Dictionary<int, GameObject>();

        /// <summary>
        /// R-15 — the cavern ceiling that IS the sky (there is no skybox in Lantern Deep). Raised
        /// by the runtime blockout so a camera at y=40 sits inside the cavern.
        /// </summary>
        public GameObject CavernDome;
    }

    /// <summary>
    /// Builds the playable scene from map data (R-10) and the asset seam, in plain runtime code.
    ///
    /// Runtime and not editor-only on purpose: the same call composes the scene for the headless
    /// editor builder (<c>Assets/Editor/SceneBuilder.cs</c>), for a EditMode test, and for a runtime
    /// bootstrap — one description of the scene rather than three that drift. It reads
    /// <see cref="ColonyMap"/> and writes only <see cref="GameObject"/>s: no sim state is touched.
    /// </summary>
    public static class MatchSceneBuilder
    {
        /// <summary>How far above the colony floor the camera sits. Not a PRD number; see below.</summary>
        public const float CameraHeight = 40f;

        /// <summary>
        /// Pitch down from the horizon, degrees. ~60-70 is isometric-ish so roof edges and
        /// building sides read; 90 would hide every vertical face.
        /// </summary>
        public const float CameraPitchDown = 62f;

        /// <summary>World units of breathing room around the colony, so nothing sits on the frame edge.</summary>
        public const float StreetOrthoSize = 10f;

        /// <summary>World offset from the followed ground point to the camera eye.</summary>
        public static Vector3 FollowOffset
        {
            get
            {
                var back = CameraHeight / Mathf.Tan(CameraPitchDown * Mathf.Deg2Rad);
                return new Vector3(0f, CameraHeight, -back);
            }
        }

        public static void PlaceOver(Camera camera, Vector3 lookAt)
        {
            if (camera == null)
            {
                return;
            }

            camera.orthographic = true;
            camera.orthographicSize = StreetOrthoSize;
            camera.transform.position = lookAt + FollowOffset;
            camera.transform.rotation = Quaternion.Euler(CameraPitchDown, 0f, 0f);
        }

        private const float ViewMargin = 2f;

        /// <summary>Unity's built-in Plane primitive is ten world units across at scale 1.</summary>
        private const float PlanePrimitiveSize = 10f;

        /// <summary>
        /// Typical Game-view aspect. The camera ortho size stays map-based (square play area);
        /// the ground is grown to this aspect so 16:9 letterbox is cavern floor, not black bars.
        /// </summary>
        private const float TypicalViewAspect = 16f / 9f;

        /// <summary>Warm brown haze — dust under lamplight, never a blue night mist.</summary>
        private static readonly Color FogDust = new Color(0.46f, 0.26f, 0.12f);

        /// <summary>Near-black umber ambient: dark, warm, and a color — never daylight.</summary>
        private static readonly Color AmbientUmber = new Color(0.24f, 0.16f, 0.08f);

        /// <summary>
        /// Compose the scene the session is played in: a tilted top-down camera, the colony floor,
        /// the cavern shell, Mars habitats on every shelter (R-10), and the team spawn (R-33).
        ///
        /// Every position comes out of <paramref name="map"/> rather than out of a literal here, so
        /// a retuned layout moves the scene with it instead of leaving the markers where the sim no
        /// longer thinks the shelters are.
        /// </summary>
        /// <param name="visuals">
        /// The asset seam. Null falls back to <see cref="PlaceholderVisualResolver"/> rather than
        /// failing: no code path in this ticket may block on art, and "the caller had no resolver
        /// yet" is the same absence wearing a different coat.
        /// </param>
        public static MatchScene Build(ColonyMap map, IVisualResolver visuals)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            var resolver = visuals ?? new PlaceholderVisualResolver();
            var playArea = PlayArea(map);

            var scene = new MatchScene { Root = new GameObject("RedHollow_Match") };

            scene.Camera = BuildCamera(scene.Root.transform, playArea);

            scene.Ground = BuildGround(scene.Root.transform, resolver, playArea);
            CavernBlockout.DressFloor(scene.Root.transform, playArea);

            // Cavern first so the camera at y=40 is inside rock walls, not over a desert quad.
            // Do NOT call LanternDeepLighting.Apply: that raises a sphere dome (top ~y=15)
            // which would sit under this camera. Fog/ambient/no-sun are applied here instead.
            ApplyCavernAtmosphere();
            scene.CavernDome = CavernBlockout.BuildShell(scene.Root.transform, playArea);

            // Sourced amber lanterns that shade URP Lit habs/ground/walls. Named and typed
            // as lanterns (never Directional) so the no-sun tests still pass.
            RaiseLanterns(scene.Root.transform, map);

            // R-33 — one team spawn, where heroes enter at wave 1 and come back after a death.
            scene.TeamSpawn = Marker(
                scene.Root.transform, resolver, VisualClass.Placeable, "TeamSpawn", map.TeamSpawn);
            CavernBlockout.DressSpawnPad(scene.TeamSpawn);

            // R-10 — one marker per shelter, named by the sim's own id so a marker and the hotspot
            // it stands for cannot be matched up wrongly downstream.
            foreach (var spec in map.Hotspots)
            {
                if (spec == null || string.IsNullOrEmpty(spec.Id))
                {
                    continue;
                }

                var hotspotMarker = Marker(
                    scene.Root.transform, resolver, VisualClass.Hotspot, "Hotspot_" + spec.Id, spec.Pos);

                // T-26 / S4 — the observable lost-state component, named by the sim's own id.
                // Not lost at build: the colony starts with everyone alive; the shell pump mirrors
                // the sim's emptied answer onto it later.
                hotspotMarker.AddComponent<HotspotMarkerView>().Bind(spec.Id);

                CavernBlockout.DressHotspot(hotspotMarker, spec.Id, spec.Civilians);

                scene.HotspotMarkers[spec.Id] = hotspotMarker;
            }

            // T-26 / R-14 — one marker per entry tunnel, keyed by the tunnel's INDEX in the map's
            // list (the spelling the wave preview's ActiveEntryTunnels and the HUD's EntryFlares
            // both use, so a marker and the tunnel it stands for cannot be matched up wrongly).
            // Freshly built markers pulse and flare nothing — the models drive those states.
            for (var i = 0; i < map.EntryTunnels.Count; i++)
            {
                var tunnelMarker = Marker(
                    scene.Root.transform, resolver, VisualClass.Hotspot, "EntryTunnel_" + i,
                    map.EntryTunnels[i]);
                tunnelMarker.AddComponent<EntryTunnelMarkerView>().Bind(i);
                CavernBlockout.DressEntryTunnel(tunnelMarker, i);
                scene.EntryTunnelMarkers[i] = tunnelMarker;
            }

            CavernBlockout.ScatterSettlement(scene.Root.transform, map, playArea);

            return scene;
        }

        /// <summary>
        /// R-30 — a 3D top-down look: the camera sits at <see cref="CameraHeight"/> over the
        /// play area and pitches ~60-70° down (isometric-ish) so habitat roofs, wall thickness
        /// and gantries read. Straight-down hides every vertical face.
        ///
        /// Orthographic, sized from the map, because a colony-defence read is about relative
        /// distance — which shelter a wave is closer to — and perspective makes the same gap
        /// read differently at the frame edge than at the centre.
        /// </summary>
        private static Camera BuildCamera(Transform root, Bounds playArea)
        {
            var go = new GameObject("TopDownCamera");
            go.transform.SetParent(root, false);

            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = StreetOrthoSize;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 400f;

            // Play-mode Game view: tag + solid cavern clear makes this the Game camera.
            go.tag = "MainCamera";
            camera.depth = 10f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = FogDust;

            // URP ignores a Camera that has no UniversalAdditionalCameraData; without it the
            // Game view falls through to a second Base camera (or nothing) and letterboxes black.
            var urp = go.GetComponent<UniversalAdditionalCameraData>();
            if (urp == null)
            {
                urp = go.AddComponent<UniversalAdditionalCameraData>();
            }

            urp.renderType = CameraRenderType.Base;

            // Street-scale follow cam: a neighborhood, not the whole board.
            PlaceOver(camera, new Vector3(playArea.center.x, SimSpace.GroundHeight, playArea.center.z));

            return camera;
        }

        /// <summary>
        /// R-15's global half, without LanternDeepLighting.Apply's undersized dome: dark warm
        /// flat ambient, warm exponential fog, zero natural light.
        /// </summary>
        private static void ApplyCavernAtmosphere()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientUmber;

            RenderSettings.fog = true;
            RenderSettings.fogColor = FogDust;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            // Dense enough to haze the far wall / lift shaft; the playable square stays readable.
            RenderSettings.fogDensity = 0.008f;

            RenderSettings.skybox = null;
            RenderSettings.sun = null;

            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light != null && light.type == LightType.Directional)
                {
                    light.enabled = false;
                }
            }
        }

        /// <summary>
        /// The colony floor: resolved through the art seam (cavern-ground when the catalog has
        /// it, otherwise the unlit rust plane) and sized to cover a typical 16:9 frustum around
        /// the square play area so the extra cavern floor fills letterbox, not black bars.
        /// </summary>
        private static GameObject BuildGround(Transform root, IVisualResolver visuals, Bounds playArea)
        {
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root, false);
            ground.transform.position = new Vector3(playArea.center.x, SimSpace.GroundHeight, playArea.center.z);

            var visual = visuals.Resolve(VisualClass.Ground, ShellArtKeys.GroundTile);
            ViewRig.Attach(ground.transform, visual);

            if (visual != null && visual.Instance != null)
            {
                var playSpan = Mathf.Max(playArea.size.x, playArea.size.z) + (ViewMargin * 2f);
                var coverSpan = playSpan * TypicalViewAspect;
                SizeGroundToCover(visual.Instance, coverSpan);
            }

            return ground;
        }

        /// <summary>
        /// Cover the play area without assuming Unity's Plane primitive. A sprite laid on XZ
        /// (Euler 90,0,0) has its size on local XY; applying (span/10, 1, span/10) scales the
        /// thin axis and leaves the sprite a sliver — the "ground shrunk to nothing" bug.
        /// </summary>
        private static void SizeGroundToCover(GameObject instance, float span)
        {
            var sprite = instance.GetComponentInChildren<SpriteRenderer>();
            if (sprite != null && sprite.sprite != null)
            {
                var size = sprite.sprite.bounds.size;
                var current = Mathf.Max(size.x, size.y);
                if (current > 0.0001f)
                {
                    var s = span / current;
                    instance.transform.localScale = new Vector3(s, s, 1f);
                }

                return;
            }

            var scale = span / PlanePrimitiveSize;
            instance.transform.localScale = new Vector3(scale, 1f, scale);
        }

        /// <summary>
        /// Soft shadows on sourced lanterns. llvmpipe may hitch or go black with cubemap
        /// point shadows; flip this off and keep the extra lights if PlayCapture dies.
        /// </summary>
        private const bool LanternSoftShadows = false;

        /// <summary>
        /// Street-mast height. High lamps (y=16+) left umber holes between habs because
        /// inverse-square died before the floor; keep the pool on the pavement.
        /// </summary>
        private const float ClusterLanternHeight = 8f;

        /// <summary>Sphere radius from the cluster lamp; must clear height plus a street radius.</summary>
        private const float ClusterLanternRange = 24f;

        /// <summary>Spawn / shelter keys hang over open courtyards, below the 4-story peak.</summary>
        private const float KeyLanternHeight = 28f;

        /// <summary>
        /// R-15 — sourced amber point lights over spawn, each shelter, each tunnel mouth,
        /// and a fill grid so walking a street is not an umber hole. Named and typed as
        /// lanterns (never Directional) so the no-sun tests still pass.
        /// </summary>
        private static void RaiseLanterns(Transform root, ColonyMap map)
        {
            var amber = new Color(1.0f, 0.62f, 0.28f);
            var keyShadows = LanternSoftShadows ? LightShadows.Soft : LightShadows.None;

            AddLantern(root, "Lantern_Spawn", map.TeamSpawn, KeyLanternHeight, amber, 56f, 110f, keyShadows);

            foreach (var spec in map.Hotspots)
            {
                if (spec == null || string.IsNullOrEmpty(spec.Id))
                {
                    continue;
                }

                AddLantern(root, "Lantern_" + spec.Id, spec.Pos, KeyLanternHeight, amber, 48f, 90f, keyShadows);
            }

            for (var i = 0; i < map.EntryTunnels.Count; i++)
            {
                AddLantern(root, "Lantern_Tunnel_" + i, map.EntryTunnels[i], 14f, amber, 40f, 60f, LightShadows.None);
            }

            var n = 0;
            for (var x = -40; x <= 40; x += 16)
            {
                for (var z = -40; z <= 40; z += 16)
                {
                    if (x == 0 && z == 0)
                    {
                        continue;
                    }

                    AddLantern(root, "Lantern_Fill_" + n, new Vec2(x, z), ClusterLanternHeight,
                        amber, ClusterLanternRange, 220f, LightShadows.None);
                    n++;
                }
            }
        }

        private static void AddLantern(
            Transform root, string name, Vec2 pos, float height, Color color, float range, float intensity,
            LightShadows shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(
                (float)pos.X, SimSpace.GroundHeight + height, (float)pos.Y);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.lightUnit = LightUnit.Candela;
            light.intensity = intensity;
            light.range = range;
            light.shadows = shadows;
            light.renderMode = LightRenderMode.ForcePixel;
            // URP additional-light data so lanterns are realtime punctual, not baked.
            light.GetUniversalAdditionalLightData();
        }

        /// <summary>
        /// One named marker standing at a sim position, wearing whatever the asset seam answers
        /// with. The marker itself carries the position and the visual hangs off it, so a
        /// placeholder's own vertical offset never moves the point the sim meant.
        /// </summary>
        private static GameObject Marker(
            Transform root, IVisualResolver visuals, VisualClass visualClass, string name, Vec2 pos)
        {
            var marker = new GameObject(name);
            marker.transform.SetParent(root, false);
            marker.transform.position = SimSpace.ToWorld(pos);

            ViewRig.Attach(marker.transform, visuals.Resolve(visualClass, null));

            return marker;
        }

        /// <summary>
        /// The world box the colony occupies: the team spawn, every shelter (R-10) and every breach
        /// tunnel (R-14). Derived from the map so the camera frames whatever layout is authored
        /// rather than a rectangle somebody typed once.
        /// </summary>
        private static Bounds PlayArea(ColonyMap map)
        {
            var bounds = new Bounds(SimSpace.ToWorld(map.TeamSpawn), Vector3.zero);

            foreach (var spec in map.Hotspots)
            {
                if (spec != null)
                {
                    bounds.Encapsulate(SimSpace.ToWorld(spec.Pos));
                }
            }

            foreach (var tunnel in map.EntryTunnels)
            {
                bounds.Encapsulate(SimSpace.ToWorld(tunnel));
            }

            return bounds;
        }
    }
}
