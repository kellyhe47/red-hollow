#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace RedHollow.EditorTools
{
    /// <summary>
    /// Import the CC0 Quaternius Standard FBX at 1:1 metres, no kit materials
    /// (Lykos URP Lit is bound at instance time).
    /// </summary>
    public sealed class SciFiKitImporter : AssetPostprocessor
    {
        private const string Marker = "/Kits/QuaterniusSciFi/";

        private void OnPreprocessModel()
        {
            if (assetPath == null || assetPath.IndexOf(Marker) < 0)
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.addCollider = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private void OnPreprocessTexture()
        {
            if (assetPath == null || assetPath.IndexOf(Marker) < 0)
            {
                return;
            }

            // Kit trim maps are unused this pass (Lykos albedo wins). Keep them as
            // default textures so the Standard pack stays complete on disk.
            var importer = (TextureImporter)assetImporter;
            if (assetPath.IndexOf("_Normal") >= 0)
            {
                importer.textureType = TextureImporterType.NormalMap;
            }
        }
    }
}
#endif
