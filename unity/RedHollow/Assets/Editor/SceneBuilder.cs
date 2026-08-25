#if UNITY_EDITOR
using System;

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
        /// </summary>
        public static void Build()
        {
            throw new NotImplementedException("ticket 016 — headless scene build");
        }
    }
}
#endif
