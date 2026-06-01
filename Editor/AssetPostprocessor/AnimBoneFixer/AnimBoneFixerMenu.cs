using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DivineDragon.Editor
{
    /// <summary>
    /// Menu entry points for running the AnimBoneFixer manually.
    /// </summary>
    public static class AnimBoneFixerMenu
    {
        [MenuItem("Divine Dragon/Dumper/Utilities/Anim Bone Fixer/Fix All Animations", false, 1701)]
        public static void FixAllAnimations()
        {
            if (!EditorUtility.DisplayDialog(
                    "Fix All Animations",
                    "The Dumper already automatically fixes animation bone paths on import.\n\n" +
                    "However, if you have existing .anim files that were imported before this feature was added, they will still contain the old unresolved paths (e.g. path_0x12345678).\n\n" +
                    $"This will scan every .anim file in the project and rewrite any path_0x… placeholder paths.\n\n" +
                    "Continue?",
                    "Yes", "Cancel"))
            {
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:AnimationClip");
            List<string> paths = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".anim"))
                {
                    paths.Add(path);
                }
            }

            if (paths.Count == 0)
            {
                EditorUtility.DisplayDialog("No Animations Found", "No .anim files were found in the project.", "OK");
                return;
            }

            RunFixOnPaths(paths, "Fix All Animations");
        }

        private static void RunFixOnPaths(List<string> paths, string title)
        {
            int filesFixed = 0;
            int totalReplaced = 0;

            for (int i = 0; i < paths.Count; i++)
            {
                string assetPath = paths[i];
                if (EditorUtility.DisplayCancelableProgressBar(
                        title,
                        $"Processing: {Path.GetFileName(assetPath)} ({i + 1}/{paths.Count})",
                        (float)(i + 1) / paths.Count))
                {
                    break;
                }

                if (AnimBoneFixer.FixAnimationClip(assetPath, out int fixedCount))
                {
                    filesFixed++;
                    totalReplaced += fixedCount;
                }
            }

            EditorUtility.ClearProgressBar();

            if (filesFixed > 0)
            {
                AssetDatabase.SaveAssets();
            }

            string summary =
                $"Files modified: {filesFixed}/{paths.Count}\n" +
                $"Total paths replaced: {totalReplaced}\n" +
                $"Bone hash map: {BoneHashMap.Count} entries";

            EditorUtility.DisplayDialog(title + " complete", summary, "OK");
            Debug.Log($"[AnimBoneFixer] {title}: {summary.Replace('\n', ' ')}");
        }
    }
}
