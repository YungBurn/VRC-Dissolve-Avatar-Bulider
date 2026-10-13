using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace YBTools.Dissolve.Editor
{
    internal static class YBPathUtility
    {
        public const string GeneratedRoot = "Assets/_YBToolsGenerated";

        public static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null) return string.Empty;
            if (root == target) return string.Empty;

            var parts = new System.Collections.Generic.List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            if (current != root) return string.Empty;
            parts.Reverse();
            return string.Join("/", parts);
        }

        public static string SanitizeAssetName(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) value = fallback;
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                bool bad = false;
                for (int i = 0; i < invalid.Length; i++)
                {
                    if (c == invalid[i]) { bad = true; break; }
                }
                sb.Append(bad || c == '/' || c == '\\' ? '_' : c);
            }
            return sb.ToString().Trim();
        }

        public static string EnsureGeneratedProfileFolders(string avatarName, string profileName)
        {
            EnsureFolder("Assets", "_YBToolsGenerated");
            var avatarFolder = GeneratedRoot + "/" + SanitizeAssetName(avatarName, "Avatar");
            EnsureFolder(GeneratedRoot, Path.GetFileName(avatarFolder));

            var profileFolder = avatarFolder + "/" + SanitizeAssetName(profileName, "Default");
            EnsureFolder(avatarFolder, Path.GetFileName(profileFolder));

            EnsureFolder(profileFolder, "Mesh");
            EnsureFolder(profileFolder, "Materials");
            EnsureFolder(profileFolder + "/Materials", "Dissolve");
            EnsureFolder(profileFolder + "/Materials", "Overlay");
            EnsureFolder(profileFolder + "/Materials", "Variants");
            EnsureFolder(profileFolder, "Animations");
            EnsureFolder(profileFolder, "Animator");
            EnsureFolder(profileFolder, "Prefab");
            EnsureFolder(profileFolder, "Data");
            EnsureFolder(profileFolder, "Backup");
            AssetDatabase.Refresh();
            return profileFolder;
        }


        public static string GetOrCreateProfileOutputRoot(GameObject avatarRoot, YBDissolveProfile profile, string fallbackProfileName)
        {
            if (profile != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(profile);
                if (!string.IsNullOrEmpty(assetPath) && assetPath.StartsWith(GeneratedRoot + "/", StringComparison.Ordinal))
                {
                    string dataFolder = Path.GetDirectoryName(assetPath).Replace('\\', '/');
                    if (dataFolder.EndsWith("/Data", StringComparison.Ordinal))
                    {
                        string profileFolder = Path.GetDirectoryName(dataFolder).Replace('\\', '/');
                        EnsureGeneratedSubfolders(profileFolder);
                        return profileFolder;
                    }
                }
            }

            string avatarName = avatarRoot != null ? avatarRoot.name : "Avatar";
            string profileName = profile != null ? profile.name : fallbackProfileName;
            return EnsureGeneratedProfileFolders(avatarName, profileName);
        }

        public static void EnsureGeneratedSubfolders(string profileFolder)
        {
            if (string.IsNullOrEmpty(profileFolder) || !AssetDatabase.IsValidFolder(profileFolder)) return;
            EnsureFolder(profileFolder, "Mesh");
            EnsureFolder(profileFolder, "Materials");
            EnsureFolder(profileFolder + "/Materials", "Dissolve");
            EnsureFolder(profileFolder + "/Materials", "Overlay");
            EnsureFolder(profileFolder + "/Materials", "Variants");
            EnsureFolder(profileFolder, "Animations");
            EnsureFolder(profileFolder, "Animator");
            EnsureFolder(profileFolder, "Prefab");
            EnsureFolder(profileFolder, "Data");
            EnsureFolder(profileFolder, "Backup");
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }
    }
}
