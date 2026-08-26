using System;
using System.Collections.Generic;
using RedHollow.Game.UI;
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

        /// <summary>Unity's built-in Plane primitive is ten world units across at scale 1.</summary>
        private const float PlanePrimitiveSize = 10f;

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

            scene.Ground = BuildGround(scene.Root.transform, resolver, playArea);

            // Modest sourced lanterns for leftover lit mats. Do NOT call LanternDeepLighting.Apply
            // here: that raises the cavern dome (top ~y=15) which covers this camera at y=60.
            RaiseLanterns(scene.Root.transform, map);

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
                    scene.Root.transform, resolver, VisualClass.Hotspot, "Hotspot_" + spec.Id, spec.Pos);

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

            // Play-mode Game view: an untagged Skybox-clear camera over a square ground plane
            // in a wide Game window letterboxes into a slate column (Unity's default clear
            // 0.19/0.30/0.47) and every placeholder shares Default-Material gray, so a
            // y-down look cannot tell a capsule from the floor. Tag + solid cavern clear
            // makes this the Game camera and kills the skybox bars; tint is in the resolver.
            go.tag = "MainCamera";
            camera.depth = 10f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.07f, 0.04f, 1f);

            // URP ignores a Camera that has no UniversalAdditionalCameraData; without it the
            // Game view falls through to a second Base camera (or nothing) and letterboxes black.
            var urp = go.GetComponent<UniversalAdditionalCameraData>();
            if (urp == null)
            {
                urp = go.AddComponent<UniversalAdditionalCameraData>();
            }

            urp.renderType = CameraRenderType.Base;

            go.transform.position = new Vector3(
                playArea.center.x, SimSpace.GroundHeight + CameraHeight, playArea.center.z);

            // LookRotation rather than an Euler triple: this states the forward vector the test
            // asserts on directly, instead of an angle that happens to produce it.
            go.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

            return camera;
        }

        // The pre-013 placeholder KeyLight (a directional light) is retired: R-15 forbids any
        // sun-like light. LanternDeepLighting.Apply is the full look (dome + fog) but must not
        // run at Play until the camera sits inside the dome; Build raises point lanterns only.

        /// <summary>
        /// The colony floor: resolved through the art seam (cavern-ground when the catalog has
        /// it, otherwise the unlit rust plane) and sized to cover the whole play area so no
        /// part of the map a monster can walk to is over a hole.
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
                var span = Mathf.Max(playArea.size.x, playArea.size.z) + (ViewMargin * 2f);
                SizeGroundToCover(visual.Instance, span);
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
        /// R-15 — sourced amber point lights over spawn and each shelter. Named and typed as
        /// lanterns (never Directional) so the no-sun tests still pass. Range covers the
        /// hotspot cluster; intensity is for any leftover lit materials (placeholders are unlit).
        /// </summary>
        private static void RaiseLanterns(Transform root, ColonyMap map)
        {
            const float height = 6f;
            var amber = new Color(1.0f, 0.62f, 0.28f);

            AddLantern(root, "Lantern_Spawn", map.TeamSpawn, height, amber, 32f, 18f);

            foreach (var spec in map.Hotspots)
            {
                if (spec == null || string.IsNullOrEmpty(spec.Id))
                {
                    continue;
                }

                AddLantern(root, "Lantern_" + spec.Id, spec.Pos, height, amber, 28f, 14f);
            }
        }

        private static void AddLantern(
            Transform root, string name, Vec2 pos, float height, Color color, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(
                (float)pos.X, SimSpace.GroundHeight + height, (float)pos.Y);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
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
