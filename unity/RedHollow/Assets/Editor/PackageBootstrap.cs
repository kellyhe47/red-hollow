#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace RedHollow.EditorTools
{
    /// <summary>
    /// Adds the R-50 transport stack and R-30 input package by NAME, letting the Package Manager
    /// resolve the version compatible with this editor — pinning versions by hand guesses at
    /// compatibility, and Unity 6 pairs with Netcode for GameObjects 2.x rather than the 1.x most
    /// examples use. Run headless:
    ///   Unity -batchmode -quit -projectPath . -executeMethod RedHollow.EditorTools.PackageBootstrap.AddMultiplayerStack
    /// </summary>
    public static class PackageBootstrap
    {
        private static readonly string[] Wanted =
        {
            "com.unity.netcode.gameobjects", // R-50/R-51 host-authoritative transport
            "com.unity.transport",           // NGO's underlying transport
            "com.unity.services.core",       // UGS bootstrap
            "com.unity.services.authentication",
            "com.unity.services.lobby",      // R-50 join codes
            "com.unity.services.relay",      // R-50 relay
            "com.unity.inputsystem",         // R-30 WASD + mouse aim
        };

        public static void AddMultiplayerStack()
        {
            Debug.Log("[bootstrap] adding: " + string.Join(", ", Wanted));
            AddAndRemoveRequest request = Client.AddAndRemove(Wanted, Array.Empty<string>());

            while (!request.IsCompleted)
            {
                System.Threading.Thread.Sleep(100);
            }

            if (request.Status == StatusCode.Failure)
            {
                Debug.LogError("[bootstrap] FAILED: " + request.Error.message);
                EditorApplication.Exit(1);
                return;
            }

            foreach (var p in request.Result.OrderBy(p => p.name))
            {
                Debug.Log($"[bootstrap] resolved {p.name}@{p.version}");
            }

            Debug.Log("[bootstrap] OK");
            EditorApplication.Exit(0);
        }
    }
}
#endif
