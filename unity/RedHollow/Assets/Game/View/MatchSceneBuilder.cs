using System;
using System.Collections.Generic;
using RedHollow.Sim;
using UnityEngine;
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
        /// R-15 — the cavern-dome mesh that IS the sky (there is no skybox in Lantern Deep). Null
        /// until <c>RedHollow.Game.Art.LanternDeepLighting.Apply</c> raises it; ticket 013.
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
        private const float CameraHeight = 60f;

        /// <summary>World units of breathing room around the colony, so nothing sits on the frame edge.</summary>
        private const float ViewMargin = 4f;

        /// <summary>Warm cavern clear — matches the dust fog so the haze reads at the frame edge.</summary>
        private static readonly Color CavernClear = new Color(0.16f, 0.10f, 0.06f);

        /// <summary>Sourced lantern amber (R-15 — no sun).</summary>
        private static readonly Color LanternAmber = new Color(1.0f, 0.62f, 0.28f);

        /// <summary>How far above the floor the hotspot/spawn lanterns hang.</summary>
        private const float LanternHeight = 8f;

        /// <summary>
        /// Compose the scene the session is played in: a top-down camera, the colony floor, the
        /// team spawn (R-33) and one marker per shelter (R-10).
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

            scene.Ground = CavernEnvironment.Build(scene.Root.transform, map, WidescreenCover(playArea));

            PlaceLanterns(scene.Root.transform, map);

            // R-33 — one team spawn, where heroes enter at wave 1 and come back after a death.
            scene.TeamSpawn = Marker(
                scene.Root.transform, resolver, VisualClass.Placeable, "TeamSpawn", map.TeamSpawn);

            // R-10 — one marker per shelter, named by the sim's own id so a marker and the hotspot
            // it stands for cannot be matched up wrongly downstream.
            foreach (var spec in map.Hotspots)
            {
                if (spec == null || string.IsNullOrEmpty(spec.Id))
                {
                    continue;
                }

                var hotspotMarker = Marker(
                    scene.Root.transform, resolver, VisualClass.Hotspot, "Hotspot_" + spec.Id,
                    spec.Pos);

                // T-26 / S4 — the observable lost-state component, named by the sim's own id.
                // Not lost at build: the colony starts with everyone alive; the shell pump mirrors
                // the sim's emptied answer onto it later.
                hotspotMarker.AddComponent<HotspotMarkerView>().Bind(spec.Id);

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
                scene.EntryTunnelMarkers[i] = tunnelMarker;
            }

            return scene;
        }

        /// <summary>
        /// R-30 — genuinely top-down: the camera is placed over the middle of the play area and
        /// aimed straight down the world's vertical axis, not merely tilted steeply.
        ///
        /// Orthographic, sized from the map, because a top-down colony-defence read is about
        /// relative distance — which shelter a wave is closer to — and perspective makes the same
        /// gap read differently at the edge of the frame than at the centre. Height, field of view
        /// and projection are all free of the PRD; what is pinned is the direction of the look.
        /// </summary>
        private static Camera BuildCamera(Transform root, Bounds playArea)
        {
            var go = new GameObject("TopDownCamera");
            go.transform.SetParent(root, false);

            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(playArea.extents.x, playArea.extents.z) + ViewMargin;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = CameraHeight * 2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = CavernClear;
            camera.depth = 10;
            try
            {
                go.tag = "MainCamera";
            }
            catch (UnityException)
            {
                // Builtin tags are always present in the player; EditMode hosts can lack them.
            }

            try
            {
                var additional = camera.GetUniversalAdditionalCameraData();
                additional.renderType = CameraRenderType.Base;
                additional.renderPostProcessing = false;
            }
            catch (Exception)
            {
                // URP additional-camera data is best-effort: SolidColor + MainCamera already
                // make the Game view playable if the component cannot be added here.
            }

            go.transform.position = new Vector3(
                playArea.center.x, SimSpace.GroundHeight + CameraHeight, playArea.center.z);

            // LookRotation rather than an Euler triple: this states the forward vector the test
            // asserts on directly, instead of an angle that happens to produce it.
            go.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

            return camera;
        }

        // Compose the 3D Lykos cavern (meshes + URP lights). LanternDeepLighting.Apply is
        // invoked from GameEntryBehaviour in Play so fog/ambient/dome land without dirtying
        // EditMode RenderSettings. The dome is now taller than the camera, so Apply no
        // longer occludes the colony.

        /// <summary>
        /// World span that fills a 16:9 Game view at the match ortho size, with extra so a
        /// wider Free Aspect panel still shows cavern walls instead of black pillarbox.
        /// </summary>
        private static float WidescreenCover(Bounds playArea)
        {
            var ortho = Mathf.Max(playArea.extents.x, playArea.extents.z) + ViewMargin;
            var viewHeight = 2f * ortho;
            var viewWidth16x9 = viewHeight * (16f / 9f);
            return Mathf.Max(viewWidth16x9, viewHeight) * 1.5f;
        }

        /// <summary>
        /// Sourced amber point lights — spawn, shelters, and the lift-shaft landmark from
        /// the Lykos seed. Few lights, long range: URP's per-object additional-light cap
        /// is 8 (PC/Mobile RP assets). Window dots on buildings are emissive meshes.
        /// </summary>
        private static void PlaceLanterns(Transform root, ColonyMap map)
        {
            PlaceLantern(root, "Lantern_Spawn", map.TeamSpawn, 22f, 7f);
            PlaceLantern(root, "Lantern_Lift", CavernEnvironment.LiftShaft, 36f, 14f, 14f);

            foreach (var spec in map.Hotspots)
            {
                if (spec == null || string.IsNullOrEmpty(spec.Id))
                {
                    continue;
                }

                PlaceLantern(root, "Lantern_" + spec.Id, spec.Pos, 20f, 6.5f);
            }
        }

        private static void PlaceLantern(
            Transform root, string name, Vec2 pos, float range, float intensity, float height = -1f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var hang = height < 0f ? LanternHeight : height;
            go.transform.position = SimSpace.ToWorld(pos) + (Vector3.up * hang);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LanternAmber;
            light.intensity = intensity;
            light.range = range;
            light.shadows = height > 0f ? LightShadows.Soft : LightShadows.None;
        }

        /// <summary>
        /// One named marker standing at a sim position, wearing whatever the asset seam answers
        /// with. The marker itself carries the position and the visual hangs off it, so a
        /// placeholder's own vertical offset never moves the point the sim meant.
        /// </summary>
        private static GameObject Marker(
            Transform root, IVisualResolver visuals, VisualClass visualClass, string name, Vec2 pos,
            string artKey = null)
        {
            var marker = new GameObject(name);
            marker.transform.SetParent(root, false);
            marker.transform.position = SimSpace.ToWorld(pos);

            ViewRig.Attach(marker.transform, visuals.Resolve(visualClass, artKey));

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
