#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RedHollow.EditorTools
{
    /// <summary>
    /// Headless assertions about project configuration the compiler cannot catch: that URP is the
    /// ACTIVE pipeline (R-15's fog/bloom/grading depend on it), that the sim assembly loaded with no
    /// engine reference (R-51), and that the cloud project is linked (R-50).
    /// Run: Unity -batchmode -quit -projectPath . -executeMethod RedHollow.EditorTools.ProjectVerify.Run
    /// </summary>
    public static class ProjectVerify
    {
        public static void Run()
        {
            var fail = 0;

            var rp = GraphicsSettings.defaultRenderPipeline;
            Debug.Log($"[verify] active render pipeline: {(rp == null ? "BUILT-IN (null)" : rp.GetType().Name)}");
            if (rp == null || !rp.GetType().Name.Contains("Universal")) { Debug.LogError("[verify] URP is NOT active"); fail++; }

            var sim = System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "GameSim");
            if (sim == null) { Debug.LogError("[verify] GameSim assembly not loaded"); fail++; }
            else
            {
                var engineRefs = sim.GetReferencedAssemblies().Where(n => n.Name.StartsWith("Unity")).ToList();
                Debug.Log($"[verify] GameSim loaded; Unity* references: {engineRefs.Count}");
                if (engineRefs.Count != 0) { Debug.LogError("[verify] R-51 VIOLATED: " + string.Join(",", engineRefs.Select(r => r.Name))); fail++; }
                Debug.Log($"[verify] GameSim public types: {sim.GetTypes().Count(t => t.IsPublic)}");
            }

            var pid = CloudProjectSettings.projectId;
            Debug.Log($"[verify] cloudProjectId: '{(string.IsNullOrEmpty(pid) ? "(empty)" : pid)}'");
            if (string.IsNullOrEmpty(pid)) { Debug.LogWarning("[verify] cloud project not linked"); }

            Debug.Log(fail == 0 ? "[verify] ALL OK" : $"[verify] {fail} FAILURE(S)");
            EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
