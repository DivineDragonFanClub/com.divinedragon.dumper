using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DivineDragon.Editor
{
    /// <summary>
    /// Rewrites AssetRipper placeholder bone paths (e.g. <c>path_0x1E3208F6_SpwPipH</c>)
    /// on imported animation clips back to their real transform paths via
    /// <see cref="BoneHashMap"/>. Operates on bindings through <see cref="AnimationUtility"/>
    /// so it works under both Force Text and Force Binary asset serialization modes.
    /// </summary>
    public class AnimBoneFixer : AssetPostprocessor
    {
        // AssetRipper's exact placeholder format. Group 1 is the CRC32 in hex.
        private static readonly Regex PlaceholderRegex = new Regex(
            @"^path_0x([0-9A-Fa-f]{1,10})_[H-Wh-w]{6}[HJLN]$",
            RegexOptions.Compiled);

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            bool anyFixed = false;
            foreach (string assetPath in importedAssets)
            {
                if (!assetPath.EndsWith(".anim")) continue;
                if (FixAnimationClip(assetPath, out int fixedCount) && fixedCount > 0)
                {
                    anyFixed = true;
                }
            }
            if (anyFixed)
            {
                AssetDatabase.SaveAssets();
            }
        }

        public static bool FixAnimationClip(string assetPath, out int fixedCount)
        {
            fixedCount = 0;

            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null) return false;

            fixedCount = RenameFloatBindings(clip);

            if (fixedCount > 0)
            {
                EditorUtility.SetDirty(clip);
                Debug.Log($"[AnimBoneFixer] Fixed {fixedCount} bone path{(fixedCount == 1 ? "" : "s")} in {assetPath}");
                return true;
            }
            return false;
        }

        private static int RenameFloatBindings(AnimationClip clip)
        {
            int fixedCount = 0;
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
            {
                if (!TryResolveHashedPath(b.path, out string newPath)) continue;
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, b);
                AnimationUtility.SetEditorCurve(clip, b, null);
                AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
                {
                    path = newPath,
                    type = b.type,
                    propertyName = b.propertyName
                }, curve);
                fixedCount++;
            }
            return fixedCount;
        }

        private static bool TryResolveHashedPath(string path, out string realPath)
        {
            realPath = null;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("path_0x")) return false;
            Match m = PlaceholderRegex.Match(path);
            if (!m.Success) return false;
            uint hash = Convert.ToUInt32(m.Groups[1].Value, 16);
            return BoneHashMap.TryGetPath(hash, out realPath);
        }
    }
}
