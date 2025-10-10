using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DivineDragon.Editor
{
    /// <summary>
    /// Automatically fixes uHead prefabs on import (lip sync clips, masks, etc.).
    /// Follows the process described at: https://github.com/DivineDragonFanClub/Lythos/wiki/Fixing-Lip-Sync-Animations-for-uHeads
    /// </summary>
    public class UHeadFixer : AssetPostprocessor
    {
        // Lip animation vowels
        private static readonly string[] LipVowels = { "A", "E", "I", "O", "U" };

        private const string MouthMaskPackagePath = "Packages/com.divinedragon.dumper/Editor/AssetPostprocessor/UHead/Masks/MouthMask.mask";
        private const string EyeMaskPackagePath = "Packages/com.divinedragon.dumper/Editor/AssetPostprocessor/UHead/Masks/EyeMask.mask";

        /// <summary>
        /// Called after all assets have been imported
        /// </summary>
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (string assetPath in importedAssets)
            {
                if (IsUHeadPrefab(assetPath))
                {
                    ProcessUHeadPrefab(assetPath);
                }
            }
        }

        /// <summary>
        /// Checks if the asset path is a uHead prefab
        /// </summary>
        private static bool IsUHeadPrefab(string assetPath)
        {
            return assetPath.EndsWith(".prefab") && assetPath.Contains("uHead/c") && assetPath.Contains("/Prefabs/");
        }

        /// <summary>
        /// Process a uHead prefab to fix lip sync animations
        /// </summary>
        public static void ProcessUHeadPrefab(string prefabPath)
        {
            Debug.Log($"[UHeadFixer] Processing: {prefabPath}");

            // Extract character ID (e.g., c153, c400c, c801s)
            string characterId = ExtractCharacterId(prefabPath);
            if (string.IsNullOrEmpty(characterId))
            {
                Debug.LogWarning($"[UHeadFixer] Could not extract character ID from: {prefabPath}");
                return;
            }

            Debug.Log($"[UHeadFixer] Character ID: {characterId}");

            // Get the base directory for this uHead
            string baseDir = Path.GetDirectoryName(prefabPath);
            string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
            string resourcesDir = Path.Combine(baseDir, prefabName + "_Resources");

            if (!AssetDatabase.IsValidFolder(resourcesDir))
            {
                Debug.LogWarning($"[UHeadFixer] Resources folder not found: {resourcesDir}");
                return;
            }

            // Fix lip animations
            bool animationsFixed = FixLipAnimations(resourcesDir, characterId);

            // Fix animator controller
            bool controllerFixed = FixAnimatorController(resourcesDir, characterId);

            if (animationsFixed && controllerFixed)
            {
                Debug.Log($"[UHeadFixer] Successfully processed: {characterId}");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// Fix the lip sync animations by setting additive reference pose
        /// </summary>
        private static bool FixLipAnimations(string resourcesDir, string characterId)
        {
            // First, find the AnimatorOverrideController to determine which animations are actually used
            string overrideDir = Path.Combine(resourcesDir, "AnimatorOverrideController");
            AnimatorOverrideController overrideController = null;
            AnimationClip normalClip = null;
            Dictionary<string, AnimationClip> lipClips = new Dictionary<string, AnimationClip>();

            if (AssetDatabase.IsValidFolder(overrideDir))
            {
                // Look for AOC_Facial_*.overrideController
                string[] overridePaths = AssetDatabase.FindAssets("t:AnimatorOverrideController", new[] { overrideDir });
                foreach (string guid in overridePaths)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileName(path).StartsWith("AOC_Facial_"))
                    {
                        overrideController = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
                        Debug.Log($"[UHeadFixer] Found override controller: {path}");
                        break;
                    }
                }
            }

            if (overrideController == null)
            {
                Debug.LogError($"[UHeadFixer] No AnimatorOverrideController found for {characterId}. Cannot fix lip sync.");
                return false;
            }

            // Get the actual clips from the override controller
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrideController.GetOverrides(overrides);

            foreach (var pair in overrides)
            {
                if (pair.Key != null && pair.Value != null)
                {
                    string clipName = pair.Key.name;

                    // Find Normal animation
                    if (clipName.Contains("Normal"))
                    {
                        normalClip = pair.Value;
                        Debug.Log($"[UHeadFixer] Found Normal animation from override: {pair.Value.name}");
                    }
                    // Find Lip animations
                    else if (clipName.Contains("Lip"))
                    {
                        foreach (string vowel in LipVowels)
                        {
                            if (clipName.Contains($"Lip{vowel}"))
                            {
                                lipClips[vowel] = pair.Value;
                                Debug.Log($"[UHeadFixer] Found Lip{vowel} animation from override: {pair.Value.name}");
                                break;
                            }
                        }
                    }
                }
            }

            if (normalClip == null)
            {
                Debug.LogWarning($"[UHeadFixer] Normal animation not found for {characterId}");
                return false;
            }

            // Process each lip animation
            int fixedCount = 0;
            foreach (var kvp in lipClips)
            {
                string vowel = kvp.Key;
                AnimationClip lipClip = kvp.Value;

                // Use AnimationUtility to set the additive reference pose
                AnimationUtility.SetAdditiveReferencePose(lipClip, normalClip, 0f);
                EditorUtility.SetDirty(lipClip);

                Debug.Log($"[UHeadFixer] Fixed lip animation: Lip{vowel} ({lipClip.name})");
                fixedCount++;
            }

            if (fixedCount > 0)
            {
                Debug.Log($"[UHeadFixer] Fixed {fixedCount} lip animations for {characterId}");
            }

            return fixedCount > 0;
        }

        /// <summary>
        /// Refresh the animator controller by applying avatar masks to facial layers
        /// </summary>
        private static bool FixAnimatorController(string resourcesDir, string characterId)
        {
            string controllerDir = Path.Combine(resourcesDir, "AnimatorController");
            string controllerPath = Path.Combine(controllerDir, "AC_Face.controller");

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                Debug.LogWarning($"[UHeadFixer] Face controller not found: {controllerPath}");
                return false;
            }

            Debug.Log($"[UHeadFixer] Found face controller: {controllerPath}");

            AvatarMask mouthMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MouthMaskPackagePath);
            if (mouthMask == null)
            {
                return false;
            }

            AvatarMask eyeMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(EyeMaskPackagePath);
            if (eyeMask == null)
            {
                return false;
            }

            int fixedLayers = 0;

            // copy needed so we can change and reassign
            AnimatorControllerLayer[] layers = controller.layers;

            // Apply mouth mask to all Lip layers
            foreach (string vowel in LipVowels)
            {
                string layerName = $"Lip{vowel}";

                for (int i = 0; i < layers.Length; i++)
                {
                    if (layers[i].name == layerName)
                    {
                        layers[i].avatarMask = mouthMask;
                        Debug.Log($"[UHeadFixer] Applied mouth mask to layer: {layerName}");
                        fixedLayers++;
                        break;
                    }
                }
            }

            // Apply eye mask to Eye Layer
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].name == "Eye Layer")
                {
                    layers[i].avatarMask = eyeMask;
                    Debug.Log("[UHeadFixer] Applied eye mask to Eye Layer");
                    fixedLayers++;
                    break;
                }
            }

            if (fixedLayers > 0)
            {
                // IMPORTANT: Reassign the entire layers array back to the controller
                controller.layers = layers;

                // Mark the controller as dirty and save
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();

                Debug.Log($"[UHeadFixer] Updated {fixedLayers} facial layers in controller");
                return true;
            }

            Debug.LogWarning("[UHeadFixer] No facial layers found in controller");
            return false;
        }

        /// <summary>
        /// Extract character ID from the prefab path
        /// </summary>
        private static string ExtractCharacterId(string assetPath)
        {
            // Pattern: .../uHead/cXXX/Prefabs/... where XXX can include suffixes like b, c, s
            // Examples: c153, c400c, c801s, c553b
            string[] parts = assetPath.Split('/');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i] == "uHead" && parts[i + 1].StartsWith("c"))
                {
                    return parts[i + 1]; // Returns full ID including suffix
                }
            }
            return null;
        }
    }
}
