#if UNITY_EDITOR
using System.IO;
using RedHollow.Game.View;
using RedHollow.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RedHollow.EditorTools
{
    /// <summary>
    /// Creates and saves the playable match scene without a GUI, by delegating to
    /// <see cref="RedHollow.Game.View.MatchSceneBuilder"/> and writing the result to
    /// <see cref="ScenePath"/>.
    ///
    /// A script rather than a hand-authored .unity file because there is no GUI in this environment
    /// and because a scene built from <see cref="RedHollow.Sim.ColonyMap"/> cannot silently drift
    /// away from the map data the sim actually uses (R-10).
    ///
    /// Run: Unity -batchmode -quit -projectPath . -executeMethod RedHollow.EditorTools.SceneBuilder.Build
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/RedHollow.unity";

        /// <summary>
        /// The <c>-executeMethod</c> entry point: static, public, parameterless and void, which is
        /// the only signature the batch-mode invoker accepts.
        ///
        /// Deliberately thin. Everything about *what* the scene contains lives in
        /// <see cref="MatchSceneBuilder.Build"/>, in the runtime assembly, where the EditMode tests
        /// can reach it with a strongly-typed reference — this wrapper only supplies the empty scene
        /// to compose into and the path to save it at. An editor-only scene description would be one
        /// no test could grade.
        /// </summary>
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var built = MatchSceneBuilder.Build(ColonyMap.V1(), new PlaceholderVisualResolver());

            // Ticket 022 (T-22) — the entry point that makes pressing Play boot the shell: without
            // it the saved scene contains zero MonoBehaviours and Play shows nothing (the bug the
            // owner found 2026-08-26). Exactly one, enabled; AddComponent in the editor runs no
            // lifecycle, so the serialized scene stays inert until Play calls Awake.
            new GameObject("RedHollow_Entry", typeof(RedHollow.Game.UI.GameEntryBehaviour));

            var directory = Path.GetDirectoryName(ScenePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var saved = EditorSceneManager.SaveScene(scene, ScenePath);

            // Batch mode swallows a failed save otherwise, and an absent scene that reported success
            // is the one outcome this entry point exists to rule out.
            if (!saved)
            {
                Debug.LogError("SceneBuilder: failed to save the match scene to " + ScenePath);
                return;
            }

            AssetDatabase.Refresh();

            Debug.Log(
                "SceneBuilder: wrote " + ScenePath + " with " + built.HotspotMarkers.Count
                + " hotspot markers (R-10) and a top-down camera (R-30).");
        }
    }
}
#endif
