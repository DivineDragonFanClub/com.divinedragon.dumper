using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DivineDragon.Editor
{
    /// <summary>
    /// Menu commands that allow manually running the uHead fixer on demand.
    /// </summary>
    public static class UHeadFixerMenu
    {
        [MenuItem("Divine Dragon/Dumper/Utilities/uHead/Fix All Prefabs", false, 1601)]
        public static void FixAllUHeadPrefabs()
        {
            if (!EditorUtility.DisplayDialog(
                    "Fix All uHead Prefabs",
                    "The Dumper automatically fixes uHead prefabs on import.\n\n" +
                    "If you are running this, it likely means you have existing uHead prefabs in your project that were imported before the fixer was added.\n\n" +
                    "This will process every uHead prefab in the project and refresh their facial animation setup.\n\n" +
                    "Continue?",
                    "Yes", "Cancel"))
            {
                return;
            }

            Debug.Log("[UHeadFixer] Starting batch refresh for all uHead prefabs...");

            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            List<string> uHeadPrefabs = new List<string>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (path.Contains("uHead/c") && path.Contains("/Prefabs/") && path.EndsWith(".prefab"))
                {
                    uHeadPrefabs.Add(path);
                }
            }

            if (uHeadPrefabs.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "No uHead Prefabs Found",
                    "No uHead prefabs were found in the project.",
                    "OK");
                return;
            }

            Debug.Log($"[UHeadFixer] Found {uHeadPrefabs.Count} uHead prefabs to process");

            int successCount = 0;
            int failCount = 0;

            for (int i = 0; i < uHeadPrefabs.Count; i++)
            {
                string prefabPath = uHeadPrefabs[i];

                if (EditorUtility.DisplayCancelableProgressBar(
                        "Processing uHead Prefabs",
                        $"Processing: {Path.GetFileName(prefabPath)} ({i + 1}/{uHeadPrefabs.Count})",
                        (float)(i + 1) / uHeadPrefabs.Count))
                {
                    EditorUtility.ClearProgressBar();
                    Debug.Log("[UHeadFixer] Batch refresh cancelled by user");
                    break;
                }

                try
                {
                    UHeadFixer.ProcessUHeadPrefab(prefabPath);
                    successCount++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UHeadFixer] Failed to process {prefabPath}: {e.Message}");
                    failCount++;
                }
            }

            EditorUtility.ClearProgressBar();

            string message = $"Processing complete!\n\n" +
                             $"Successful: {successCount}\n" +
                             $"Failed: {failCount}\n" +
                             $"Total: {uHeadPrefabs.Count}";

            EditorUtility.DisplayDialog("Batch Fix Complete", message, "OK");

            Debug.Log($"[UHeadFixer] Batch refresh complete. Success: {successCount}, Failed: {failCount}");
        }

        [MenuItem("Divine Dragon/Dumper/Utilities/uHead/Fix Selected Prefabs", false, 1602)]
        public static void FixSelectedUHeadPrefabs()
        {
            GameObject[] selectedObjects = Selection.gameObjects;

            if (selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "No Selection",
                    "Please select one or more uHead prefabs in the Project window.",
                    "OK");
                return;
            }

            List<string> prefabPaths = new List<string>();

            foreach (GameObject obj in selectedObjects)
            {
                string path = AssetDatabase.GetAssetPath(obj);

                if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab") &&
                    path.Contains("uHead/c") && path.Contains("/Prefabs/"))
                {
                    prefabPaths.Add(path);
                }
            }

            if (prefabPaths.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "No uHead Prefabs Selected",
                    "Please select uHead prefab files (not instances in the scene).",
                    "OK");
                return;
            }

            Debug.Log($"[UHeadFixer] Processing {prefabPaths.Count} selected prefab(s)...");

            int successCount = 0;
            int failCount = 0;

            foreach (string prefabPath in prefabPaths)
            {
                try
                {
                    UHeadFixer.ProcessUHeadPrefab(prefabPath);
                    successCount++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UHeadFixer] Failed to process {prefabPath}: {e.Message}");
                    failCount++;
                }
            }

            string message = $"Processing complete!\n\n" +
                             $"Successful: {successCount}\n" +
                             $"Failed: {failCount}\n" +
                             $"Total: {prefabPaths.Count}";

            EditorUtility.DisplayDialog("Selected Fix Complete", message, "OK");
        }

        [MenuItem("Divine Dragon/Dumper/Utilities/uHead/Fix Selected Prefabs", true)]
        private static bool ValidateFixSelected()
        {
            GameObject[] selectedObjects = Selection.gameObjects;

            foreach (GameObject obj in selectedObjects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab") &&
                    path.Contains("uHead/c") && path.Contains("/Prefabs/"))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
