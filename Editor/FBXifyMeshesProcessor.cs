using System;
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
                    string assetPath = GetIndividualFBXAssetPath(prefabName, gameObjectName);
                    string absolutePath = GetIndividualFBXOutputPath(prefabName, gameObjectName);

                    if (TryReuseExistingFbx(meshFilter.sharedMesh, assetPath, absolutePath, gameObjectName))
                    {
                        exportedAssetPaths.Add(assetPath);
                        exportedCount++;
                        continue;
                    }

                    // Export this specific GameObject
                    string exportedPath = ModelExporter.ExportObject(absolutePath, meshFilter.gameObject);
                    if (string.IsNullOrEmpty(exportedPath))
                    {
                        LogError($"Failed to export GameObject: {gameObjectName}");
                        continue;
                    }

                    exportedAssetPaths.Add(assetPath);

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
                Mesh originalMesh = meshFilter.sharedMesh;
                if (originalMesh == null)
                    continue;

                string gameObjectName = meshFilter.gameObject.name;
                string fbxAssetPath = GetIndividualFBXAssetPath(prefabName, gameObjectName);

                // Check if the FBX file exists
                if (!File.Exists(Path.Combine(Application.dataPath, "..", fbxAssetPath)))
                {
                    LogWarning($"FBX not found for {gameObjectName}, skipping");
                    continue;
                }

                Mesh fbxMesh = LoadMeshFromFbx(fbxAssetPath, originalMesh, gameObjectName);

                if (fbxMesh != null)
                {
                    meshFilter.sharedMesh = fbxMesh;
                    updatedCount++;
                }
                else
                {
                    LogWarning($"No matching mesh found in FBX for {gameObjectName} (expected '{originalMesh.name}')");
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
                string assetPath = GetIndividualFBXAssetPath(folderName, gameObjectName);
                string absolutePath = GetIndividualFBXOutputPath(folderName, gameObjectName);

                if (TryReuseExistingFbx(meshFilter.sharedMesh, assetPath, absolutePath, gameObjectName))
                {
                    exportedAssetPaths.Add(assetPath);
                    exportedCount++;
                    continue;
                }

                // Export this specific GameObject
                string exportedPath = ModelExporter.ExportObject(absolutePath, meshFilter.gameObject);
                if (string.IsNullOrEmpty(exportedPath))
                {
                    LogError($"Failed to export GameObject: {gameObjectName}");
                    continue;
                }

                exportedAssetPaths.Add(assetPath);

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
                Mesh originalMesh = meshFilter.sharedMesh;
                if (originalMesh == null)
                    continue;

                string gameObjectName = meshFilter.gameObject.name;
                string fbxAssetPath = GetIndividualFBXAssetPath(folderName, gameObjectName);

                // Check if the FBX file exists
                if (!File.Exists(Path.Combine(Application.dataPath, "..", fbxAssetPath)))
                {
                    LogWarning($"FBX not found for {gameObjectName}, skipping");
                    continue;
                }

                Mesh fbxMesh = LoadMeshFromFbx(fbxAssetPath, originalMesh, gameObjectName);

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
                    LogWarning($"No matching mesh found in FBX for {gameObjectName} (expected '{originalMesh.name}')");
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

        private static bool TryReuseExistingFbx(Mesh originalMesh, string assetPath, string absolutePath, string objectName)
        {
            if (originalMesh == null)
                return false;

            bool assetExists = !string.IsNullOrEmpty(absolutePath) && File.Exists(absolutePath);
            if (!assetExists)
            {
                // Asset might still be known to Unity even if the file is missing
                if (string.IsNullOrEmpty(assetPath) || AssetDatabase.LoadAllAssetsAtPath(assetPath).Length == 0)
                    return false;
            }

            Mesh candidate = LoadMeshFromFbx(assetPath, originalMesh, objectName);
            if (candidate == null)
                return false;

            Log($"Reusing existing FBX for {objectName}: {assetPath}");
            return true;
        }

        private static Mesh LoadMeshFromFbx(string assetPath, Mesh originalMesh, string gameObjectName)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            Mesh bestMatch = null;
            int bestScore = int.MinValue;

            string originalName = NormalizeMeshName(originalMesh?.name);
            string objectName = NormalizeMeshName(gameObjectName);
            int originalVertexCount = originalMesh != null ? originalMesh.vertexCount : 0;
            int originalSubMeshCount = originalMesh != null ? originalMesh.subMeshCount : 0;
            Bounds originalBounds = originalMesh != null ? originalMesh.bounds : default;

            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset is Mesh mesh)
                {
                    string meshName = NormalizeMeshName(mesh.name);
                    int score = 0;

                    if (!string.IsNullOrEmpty(originalName) && NamesMatch(meshName, originalName))
                        score += 100;

                    if (!string.IsNullOrEmpty(objectName) && NamesMatch(meshName, objectName))
                        score += 90;

                    if (!string.IsNullOrEmpty(originalName) && ContainsIgnoreCase(meshName, originalName))
                        score += 50;

                    if (!string.IsNullOrEmpty(objectName) && ContainsIgnoreCase(meshName, objectName))
                        score += 40;

                    if (originalVertexCount > 0 && mesh.vertexCount == originalVertexCount)
                        score += 20;

                    if (originalSubMeshCount > 0 && mesh.subMeshCount == originalSubMeshCount)
                        score += 10;

                    if (originalVertexCount > 0 && mesh.vertexCount != originalVertexCount)
                        score -= Math.Abs(mesh.vertexCount - originalVertexCount) / 10;

                    if (originalSubMeshCount > 0 && mesh.subMeshCount != originalSubMeshCount)
                        score -= Math.Abs(mesh.subMeshCount - originalSubMeshCount) * 2;

                    if (originalVertexCount > 0)
                    {
                        float originalMagnitude = originalBounds.size.magnitude;
                        float candidateMagnitude = mesh.bounds.size.magnitude;
                        if (originalMagnitude > Mathf.Epsilon && candidateMagnitude > Mathf.Epsilon)
                        {
                            float difference = Mathf.Abs(originalMagnitude - candidateMagnitude);
                            score -= Mathf.RoundToInt(difference * 10f);
                        }
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = mesh;
                    }

                    if (bestMatch == null)
                        bestMatch = mesh;
                }
            }

            return bestMatch;
        }

        private static string NormalizeMeshName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            string normalized = name;

            int suffixIndex = normalized.IndexOf("__FBXify", StringComparison.OrdinalIgnoreCase);
            if (suffixIndex >= 0)
            {
                normalized = normalized.Substring(0, suffixIndex);
            }

            return normalized;
        }

        private static bool NamesMatch(string lhs, string rhs)
        {
            if (string.IsNullOrEmpty(lhs) || string.IsNullOrEmpty(rhs))
                return false;

            if (string.Equals(lhs, rhs, StringComparison.OrdinalIgnoreCase))
                return true;

            lhs = TrimUnityDuplicateSuffix(lhs);
            rhs = TrimUnityDuplicateSuffix(rhs);

            return string.Equals(lhs, rhs, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
                return false;

            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string TrimUnityDuplicateSuffix(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            int dotIndex = name.LastIndexOf('.');
            if (dotIndex > 0 && dotIndex < name.Length - 1)
            {
                bool numeric = true;
                for (int i = dotIndex + 1; i < name.Length; i++)
                {
                    if (!char.IsDigit(name[i]))
                    {
                        numeric = false;
                        break;
                    }
                }

                if (numeric)
                    return name.Substring(0, dotIndex);
            }

            return name;
        }
    }
}
