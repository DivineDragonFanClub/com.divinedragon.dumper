using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DivineDragon.EditorUtilities
{
    public static class FBXifyMeshes
    {
        public const string TOOL_NAME = "FBXify";
        public const string TOOL_DISPLAY_NAME = TOOL_NAME + " Meshes";

        private const string MENU_ROOT = "Divine Dragon/Dumper/Utilities/" + TOOL_DISPLAY_NAME + "/";
        private const string FBX_OUTPUT_ROOT = TOOL_NAME;
        private const string LOG_PREFIX = "[" + TOOL_NAME + "]";

        // Simple logging helpers
        internal static void Log(string message) => Debug.Log($"{LOG_PREFIX} {message}");
        internal static void LogWarning(string message) => Debug.LogWarning($"{LOG_PREFIX} {message}");
        internal static void LogError(string message) => Debug.LogError($"{LOG_PREFIX} {message}");

        [MenuItem(MENU_ROOT + "Process Selected Prefab(s)", false)]
        public static void ProcessSelectedPrefabs()
        {
            var selectedPrefabs = GetSelectedPrefabs();

            if (selectedPrefabs.Count == 0)
            {
                // Shouldn't happen since we have the validation
                EditorUtility.DisplayDialog(
                    TOOL_DISPLAY_NAME,
                    "No prefabs selected. Please select one or more prefabs in the Project window.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    TOOL_DISPLAY_NAME,
                    $"Process {selectedPrefabs.Count} selected prefab(s)?",
                    "Process",
                    "Cancel"))
            {
                return;
            }

            ProcessPrefabs(selectedPrefabs);
        }

        [MenuItem(MENU_ROOT + "Process Selected Prefab(s)", true)]
        public static bool ProcessSelectedPrefabsValidation()
        {
            return GetSelectedPrefabs().Count > 0;
        }

        [MenuItem(MENU_ROOT + "Process Selected GameObject(s)", false)]
        public static void ProcessSelectedGameObjects()
        {
            var selectedGameObjects = Selection.gameObjects;

            // Shouldn't happen since we have the validation
            if (selectedGameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    TOOL_DISPLAY_NAME,
                    "No GameObjects selected. Please select one or more GameObjects in the Hierarchy window.",
                    "OK");
                return;
            }

            // Count total MeshFilters to process
            int totalMeshFilters = 0;
            foreach (var go in selectedGameObjects)
            {
                totalMeshFilters += go.GetComponentsInChildren<MeshFilter>(true).Length;
            }

            if (totalMeshFilters == 0)
            {
                EditorUtility.DisplayDialog(
                    TOOL_DISPLAY_NAME,
                    "No MeshFilters found in the selected GameObjects or their children.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    TOOL_DISPLAY_NAME,
                    $"Process {selectedGameObjects.Length} GameObject(s) containing {totalMeshFilters} MeshFilter(s)?",
                    "Process",
                    "Cancel"))
            {
                return;
            }

            ProcessGameObjects(selectedGameObjects);
        }

        [MenuItem(MENU_ROOT + "Process Selected GameObject(s)", true)]
        public static bool ProcessSelectedGameObjectsValidation()
        {
            // Only enable if we have scene GameObjects selected (not project assets)
            if (Selection.gameObjects.Length == 0)
                return false;

            // Check that at least one selected object is a scene object (not a prefab asset)
            foreach (var go in Selection.gameObjects)
            {
                if (go.scene.IsValid())
                    return true;
            }

            return false;
        }

        [MenuItem(MENU_ROOT + "Process All Prefabs in Folder", false)]
        public static void ProcessAllPrefabsInFolder()
        {
            // Get the selected folder path as default for the dialog
            string defaultPath = "Assets";
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                {
                    defaultPath = path;
                    break;
                }
            }

            string folderPath = EditorUtility.OpenFolderPanel("Select Folder with Prefabs", defaultPath, "");

            if (string.IsNullOrEmpty(folderPath))
                return;

            // Convert absolute path to relative path
            if (folderPath.StartsWith(Application.dataPath))
            {
                folderPath = "Assets" + folderPath.Substring(Application.dataPath.Length);
            }

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
            var prefabPaths = prefabGuids.Select(AssetDatabase.GUIDToAssetPath).ToList();

            if (prefabPaths.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    TOOL_DISPLAY_NAME,
                    "No prefabs found in the selected folder.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    TOOL_DISPLAY_NAME,
                    $"Process {prefabPaths.Count} prefab(s) in folder?",
                    "Process",
                    "Cancel"))
            {
                return;
            }

            ProcessPrefabs(prefabPaths);
        }

        private static List<string> GetSelectedPrefabs()
        {
            var prefabs = new List<string>();

            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                {
                    prefabs.Add(path);
                }
            }

            return prefabs;
        }

        private static void ProcessPrefabs(List<string> prefabPaths)
        {
            int processedCount = 0;
            int failedCount = 0;
            var failedPrefabs = new List<string>();

            try
            {
                for (int i = 0; i < prefabPaths.Count; i++)
                {
                    string prefabPath = prefabPaths[i];
                    string prefabName = Path.GetFileNameWithoutExtension(prefabPath);

                    float progress = (float)i / prefabPaths.Count;
                    if (EditorUtility.DisplayCancelableProgressBar(
                            TOOL_DISPLAY_NAME,
                            $"Processing {prefabName} ({i + 1}/{prefabPaths.Count})",
                            progress))
                    {
                        break;
                    }

                    try
                    {
                        if (FBXifyMeshesProcessor.ProcessPrefab(prefabPath))
                        {
                            processedCount++;
                            Log($"Successfully processed: {prefabName}");
                        }
                        else
                        {
                            failedCount++;
                            failedPrefabs.Add(prefabName);
                            LogWarning($"Failed to process: {prefabName}");
                        }
                    }
                    catch (System.Exception e)
                    {
                        failedCount++;
                        failedPrefabs.Add(prefabName);
                        LogError($"Error processing {prefabName}: {e.Message}");
                    }
                }
            }
            finally
            {
                AssetDatabase.Refresh();
                EditorUtility.ClearProgressBar();
            }

            // Show results
            string message = $"Processed {processedCount} prefab(s) successfully.";
            if (failedCount > 0)
            {
                message += $"\n{failedCount} prefab(s) failed:";
                foreach (var failed in failedPrefabs)
                {
                    message += $"\n  • {failed}";
                }
            }

            EditorUtility.DisplayDialog(TOOL_DISPLAY_NAME + " Complete", message, "OK");
        }

        private static void ProcessGameObjects(GameObject[] gameObjects)
        {
            int processedCount = 0;
            int failedCount = 0;
            var failedObjects = new List<string>();

            try
            {
                for (int i = 0; i < gameObjects.Length; i++)
                {
                    GameObject go = gameObjects[i];
                    string rootName = go.name;

                    float progress = (float)i / gameObjects.Length;
                    if (EditorUtility.DisplayCancelableProgressBar(
                            TOOL_DISPLAY_NAME,
                            $"Processing {rootName} ({i + 1}/{gameObjects.Length})",
                            progress))
                    {
                        break;
                    }

                    try
                    {
                        if (FBXifyMeshesProcessor.ProcessGameObject(go))
                        {
                            processedCount++;
                            Log($"Successfully processed: {rootName}");
                        }
                        else
                        {
                            failedCount++;
                            failedObjects.Add(rootName);
                            LogWarning($"Failed to process: {rootName}");
                        }
                    }
                    catch (System.Exception e)
                    {
                        failedCount++;
                        failedObjects.Add(rootName);
                        LogError($"Error processing {rootName}: {e.Message}");
                    }
                }
            }
            finally
            {
                AssetDatabase.Refresh();
                EditorUtility.ClearProgressBar();

                // Mark the scene as dirty so it can be saved
                if (processedCount > 0)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                        UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
                }
            }

            // Show results
            string message = $"Processed {processedCount} GameObject(s) successfully.";
            if (failedCount > 0)
            {
                message += $"\n{failedCount} GameObject(s) failed:";
                foreach (var failed in failedObjects)
                {
                    message += $"\n  • {failed}";
                }
            }

            EditorUtility.DisplayDialog(TOOL_DISPLAY_NAME + " Complete", message, "OK");
        }

        public static string GetIndividualFBXOutputPath(string prefabName, string gameObjectName)
        {
            // Keep FBX files inside Assets folder so Unity can import them
            string fbxFolder = Path.Combine(Application.dataPath, FBX_OUTPUT_ROOT, prefabName);

            if (!Directory.Exists(fbxFolder))
            {
                Directory.CreateDirectory(fbxFolder);
            }

            return Path.Combine(fbxFolder, $"{gameObjectName}.fbx");
        }

        public static string GetIndividualFBXAssetPath(string prefabName, string gameObjectName)
        {
            // Asset path relative to project root
            return Path.Combine("Assets", FBX_OUTPUT_ROOT, prefabName, $"{gameObjectName}.fbx");
        }
    }
}
