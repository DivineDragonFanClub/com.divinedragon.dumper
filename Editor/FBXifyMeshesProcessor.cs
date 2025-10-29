using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;
using static DivineDragon.EditorUtilities.FBXifyMeshes;

namespace DivineDragon.EditorUtilities
{
    public static class FBXifyMeshesProcessor
    {
        public static bool ProcessPrefab(string prefabPath)
        {
            // Load the prefab
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                LogError($"Failed to load prefab: {prefabPath}");
                return false;
            }

            string prefabName = Path.GetFileNameWithoutExtension(prefabPath);

            // Load prefab contents for editing
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
            {
                LogError($"Failed to load prefab contents: {prefabPath}");
                return false;
            }

            try
            {
                // Find all MeshFilters in the prefab
                MeshFilter[] meshFilters = prefabRoot.GetComponentsInChildren<MeshFilter>(true);
                if (meshFilters.Length == 0)
                {
                    LogWarning($"No MeshFilters found in prefab: {prefabName}");
                    return false;
                }

                Log($"Found {meshFilters.Length} MeshFilters in {prefabName}");

                // Export each GameObject with a MeshFilter as a separate FBX
                int exportedCount = 0;
                var exportedAssetPaths = new List<string>();

                foreach (var meshFilter in meshFilters)
                {
                    if (meshFilter.sharedMesh == null)
                    {
                        LogWarning($"Skipping {meshFilter.gameObject.name} - no mesh assigned");
                        continue;
                    }

                    string gameObjectName = meshFilter.gameObject.name;
                    string fbxPath = GetIndividualFBXOutputPath(prefabName, gameObjectName);

                    // Export this specific GameObject
                    string exportedPath = ModelExporter.ExportObject(fbxPath, meshFilter.gameObject);
                    if (string.IsNullOrEmpty(exportedPath))
                    {
                        LogError($"Failed to export GameObject: {gameObjectName}");
                        continue;
                    }

                    exportedAssetPaths.Add(GetIndividualFBXAssetPath(prefabName, gameObjectName));

                    Log($"Exported {gameObjectName} to: {exportedPath}");
                    exportedCount++;
                }

                if (exportedCount == 0)
                {
                    LogError($"No GameObjects were exported from {prefabName}");
                    return false;
                }

                // Force Unity to import all the FBX files
                AssetDatabase.Refresh();

                ApplyModelImportSettings(exportedAssetPaths);
                AssetDatabase.SaveAssets();

                // Update mesh references to point to the FBX files
                if (!UpdatePrefabMeshReferences(prefabRoot, prefabName, meshFilters))
                {
                    LogError($"Failed to update mesh references: {prefabName}");
                    return false;
                }

                // Save the updated prefab
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                Log($"Successfully processed {exportedCount} meshes in {prefabName}");

                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static bool UpdatePrefabMeshReferences(GameObject prefabRoot, string prefabName, MeshFilter[] meshFilters)
        {
            int updatedCount = 0;

            foreach (var meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh == null)
                    continue;

                string gameObjectName = meshFilter.gameObject.name;
                string fbxAssetPath = GetIndividualFBXAssetPath(prefabName, gameObjectName);

                // Check if the FBX file exists
                if (!File.Exists(Path.Combine(Application.dataPath, "..", fbxAssetPath)))
                {
                    LogWarning($"FBX not found for {gameObjectName}, skipping");
                    continue;
                }

                // Load the FBX
                GameObject fbxObject = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
                if (fbxObject == null)
                {
                    LogWarning($"Failed to load FBX for {gameObjectName}");
                    continue;
                }

                // Get the mesh from the FBX
                // The FBX should contain the mesh as a sub-asset
                UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath);
                Mesh fbxMesh = null;

                foreach (UnityEngine.Object asset in subAssets)
                {
                    if (asset is Mesh mesh)
                    {
                        fbxMesh = mesh;
                        break;
                    }
                }

                if (fbxMesh != null)
                {
                    meshFilter.sharedMesh = fbxMesh;
                    updatedCount++;
                }
                else
                {
                    LogWarning($"No mesh found in FBX for {gameObjectName}");
                }
            }

            return updatedCount > 0;
        }

        public static bool ProcessGameObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                LogError("GameObject is null");
                return false;
            }

            string rootName = gameObject.name;

            // Find all MeshFilters in the GameObject and its children
            MeshFilter[] meshFilters = gameObject.GetComponentsInChildren<MeshFilter>(true);
            if (meshFilters.Length == 0)
            {
                LogWarning($"No MeshFilters found in GameObject: {rootName}");
                return false;
            }

            Log($"Found {meshFilters.Length} MeshFilters in {rootName}");

            // Use "SceneObjects" as the folder for non-prefab objects
            string folderName = $"SceneObjects_{rootName}";

            // Export each GameObject with a MeshFilter as a separate FBX
            int exportedCount = 0;
            var exportedAssetPaths = new List<string>();

            foreach (var meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh == null)
                {
                    LogWarning($"Skipping {meshFilter.gameObject.name} - no mesh assigned");
                    continue;
                }

                string gameObjectName = meshFilter.gameObject.name;
                string fbxPath = GetIndividualFBXOutputPath(folderName, gameObjectName);

                // Export this specific GameObject
                string exportedPath = ModelExporter.ExportObject(fbxPath, meshFilter.gameObject);
                if (string.IsNullOrEmpty(exportedPath))
                {
                    LogError($"Failed to export GameObject: {gameObjectName}");
                    continue;
                }

                exportedAssetPaths.Add(GetIndividualFBXAssetPath(folderName, gameObjectName));

                Log($"Exported {gameObjectName} to: {exportedPath}");
                exportedCount++;
            }

            if (exportedCount == 0)
            {
                LogError($"No GameObjects were exported from {rootName}");
                return false;
            }

            // Force Unity to import all the FBX files
            AssetDatabase.Refresh();

            ApplyModelImportSettings(exportedAssetPaths);
            AssetDatabase.SaveAssets();

            // Update mesh references to point to the FBX files
            if (!UpdateSceneGameObjectMeshReferences(meshFilters, folderName))
            {
                LogError($"Failed to update mesh references: {rootName}");
                return false;
            }

            Log($"Successfully processed {exportedCount} meshes in {rootName}");
            return true;
        }

        private static bool UpdateSceneGameObjectMeshReferences(MeshFilter[] meshFilters, string folderName)
        {
            int updatedCount = 0;

            foreach (var meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh == null)
                    continue;

                string gameObjectName = meshFilter.gameObject.name;
                string fbxAssetPath = GetIndividualFBXAssetPath(folderName, gameObjectName);

                // Check if the FBX file exists
                if (!File.Exists(Path.Combine(Application.dataPath, "..", fbxAssetPath)))
                {
                    LogWarning($"FBX not found for {gameObjectName}, skipping");
                    continue;
                }

                // Load the FBX
                GameObject fbxObject = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAssetPath);
                if (fbxObject == null)
                {
                    LogWarning($"Failed to load FBX for {gameObjectName}");
                    continue;
                }

                // Get the mesh from the FBX
                UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxAssetPath);
                Mesh fbxMesh = null;

                foreach (UnityEngine.Object asset in subAssets)
                {
                    if (asset is Mesh mesh)
                    {
                        fbxMesh = mesh;
                        break;
                    }
                }

                if (fbxMesh != null)
                {
                    // Use Undo for scene objects so changes can be undone
                    Undo.RecordObject(meshFilter, $"{TOOL_NAME} Mesh Reference");

                    // Update the mesh reference
                    meshFilter.sharedMesh = fbxMesh;

                    // Force the change to be registered
                    EditorUtility.SetDirty(meshFilter);
                    EditorUtility.SetDirty(meshFilter.gameObject);

                    updatedCount++;
                }
                else
                {
                    LogWarning($"No mesh found in FBX for {gameObjectName}");
                }
            }

            return updatedCount > 0;
        }

        private static void ApplyModelImportSettings(IEnumerable<string> assetPaths)
        {
            if (assetPaths == null)
            {
                return;
            }

            var uniquePaths = new HashSet<string>(assetPaths);

            foreach (string assetPath in uniquePaths)
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

                var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer == null)
                {
                    LogWarning($"Importer not found or not a model importer for {assetPath}");
                    continue;
                }

                bool changed = false;

                changed |= EnsureUseFileUnits(importer);
                changed |= EnsureUseFileScale(importer);
                changed |= EnsureOptimizeMeshVertexOrder(importer);
                changed |= EnsureImportTangents(importer);

                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }
        }

        private static bool EnsureUseFileUnits(ModelImporter importer)
        {
            if (importer.useFileUnits)
                return false;

            importer.useFileUnits = true;
            return true;
        }

        private static bool EnsureUseFileScale(ModelImporter importer)
        {
            if (importer.useFileScale)
                return false;

            importer.useFileScale = true;
            return true;
        }

        private static bool EnsureOptimizeMeshVertexOrder(ModelImporter importer)
        {
            if (importer.optimizeMeshVertices)
                return false;

            importer.optimizeMeshVertices = true;
            return true;
        }

        private static bool EnsureImportTangents(ModelImporter importer)
        {
            if (importer.importTangents == ModelImporterTangents.Import)
                return false;

            importer.importTangents = ModelImporterTangents.Import;
            return true;
        }
    }
}
