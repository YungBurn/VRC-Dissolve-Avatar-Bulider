using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace YBTools.Dissolve.Editor
{
    internal static class YBProfileUtility
    {
        public static void EnsureCurrentDataVersion(YBDissolveProfile profile)
        {
            if (profile == null || profile.dataVersion >= YBToolVersion.ProfileDataVersion) return;

            Undo.RecordObject(profile, "Upgrade YB Dissolve Profile");
            if (profile.scan == null) profile.scan = new YBScanSettings();

            if (profile.dataVersion < 3)
            {
                float legacyDelay = Mathf.Max(0f, profile.scan.delaySeconds);
                profile.scan.delayAToBSeconds = legacyDelay;
                profile.scan.delayBToASeconds = legacyDelay;
                if (profile.scan.customDirection.sqrMagnitude < 0.000001f)
                    profile.scan.customDirection = Vector3.down;
            }

            profile.dataVersion = YBToolVersion.ProfileDataVersion;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        public static void SaveSceneBindings(
            YBDissolveProfile profile,
            GameObject avatarRoot,
            IList<List<GameObject>> outfitRoots)
        {
            if (profile == null || avatarRoot == null) return;
            Undo.RecordObject(profile, "Save YB Dissolve Profile bindings");
            profile.avatarHint = avatarRoot.name;
            profile.dataVersion = YBToolVersion.ProfileDataVersion;
            for (int i = 0; i < profile.OutfitCount; i++)
                profile.GetOutfit(i).rootPaths = BuildPaths(avatarRoot.transform, outfitRoots[i]);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        public static List<GameObject> ResolveSceneBindings(
            YBDissolveProfile profile,
            GameObject avatarRoot,
            int outfitIndex)
        {
            var result = new List<GameObject>();
            if (profile == null || avatarRoot == null) return result;
            var paths = profile.GetOutfit(outfitIndex)?.rootPaths;
            if (paths == null) return result;

            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i] ?? string.Empty;
                Transform t = string.IsNullOrEmpty(path) ? avatarRoot.transform : avatarRoot.transform.Find(path);
                if (t != null) result.Add(t.gameObject);
            }
            return result;
        }

        public static string GetOutfitLayoutHash(GameObject avatarRoot, IList<List<GameObject>> outfitRoots)
        {
            var layout = new StringBuilder();
            foreach (var roots in outfitRoots)
            {
                layout.Append('|');
                foreach (string path in BuildPaths(avatarRoot.transform, roots).Distinct().OrderBy(p => p, StringComparer.Ordinal))
                    layout.Append(path.Length).Append(':').Append(path);
            }
            return Hash128.Compute(layout.ToString()).ToString();
        }

        private static List<string> BuildPaths(Transform avatarRoot, IList<GameObject> roots)
        {
            var result = new List<string>();
            if (roots == null) return result;
            for (int i = 0; i < roots.Count; i++)
            {
                var go = roots[i];
                if (go == null) continue;
                var path = YBPathUtility.GetRelativePath(avatarRoot, go.transform);
                if (go.transform == avatarRoot || !string.IsNullOrEmpty(path)) result.Add(path);
            }
            return result;
        }
    }
}
