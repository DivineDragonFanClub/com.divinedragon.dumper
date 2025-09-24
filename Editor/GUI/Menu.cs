using System;
using System.IO;
using DivineDragon.GUI.Settings;
using UnityEditor;
using UnityEngine;

namespace DivineDragon
{
    public class Menu
    {
        // Validator for ExtractBundle
        [MenuItem("Divine Dragon/Dumper/Extract a bundle", true)]
        private static bool ValidateExtractBundle()
        {
            // Ensure the DragonStone settings we need are configured
            return !string.IsNullOrEmpty(Dragonstone.EngageAddressableSettings.GameRuntimePath);
        }
        
        [MenuItem("Divine Dragon/Dumper/Extract a bundle", false, 1400)]
        public static void ExtractBundle()
        {
            string path = EditorUtility.OpenFilePanel(
                "Select bundle file to extract",
                Dragonstone.EngageAddressableSettings.GameBuildPath, // Open the directory in the user's game dump for convenience
                "bundle"
            );

            if (string.IsNullOrEmpty(path))
                return;

            EditorUtility.DisplayProgressBar("AssetRipper", "Starting AssetRipper...", 0.1f);

            try
            {
                // TODO: This shouldn't be needed. The problem is initializing the Catalog cache, not the responsibility of the Dumper package.
                Dumper.Initialize();
                bool success = Dumper.ExtractAsset(CBT.PathToInternalId(path));
                
                if (success)
                {
                    EditorUtility.DisplayDialog("Success",
                        $"Assets extracted successfully.",
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Error",
                        "Failed to extract assets. Check the console for details.",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error running AssetRipper: {ex.Message}");

                EditorUtility.DisplayDialog("Error",
                    $"Error running AssetRipper:\n{ex.Message}",
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}