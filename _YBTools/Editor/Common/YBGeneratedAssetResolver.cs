using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace YBTools.Dissolve.Editor
{
    internal sealed class YBManifestResolveResult
    {
        public YBGeneratedAssetManifest manifest;
        public string assetPath;
        public bool repaired;
        public string message;
    }

    internal static class YBGeneratedAssetResolver
    {
        private const string ManifestFileName = "YBGenerationManifest.asset";

        public static string ExpectedManifestPath(string outputRoot)
        {
            return string.IsNullOrEmpty(outputRoot) ? string.Empty : outputRoot.TrimEnd('/') + "/Data/" + ManifestFileName;
        }

        public static YBManifestResolveResult Resolve(GameObject avatarRoot, YBDissolveProfile profile, string outputRoot)
        {
            var result = new YBManifestResolveResult();
            var candidates = new List<string>();
            string expected = ExpectedManifestPath(outputRoot);
            if (!string.IsNullOrEmpty(expected)) candidates.Add(expected);

            if (profile != null)
            {
                string profilePath = AssetDatabase.GetAssetPath(profile);
                if (!string.IsNullOrEmpty(profilePath))
                {
                    string dataDir = Path.GetDirectoryName(profilePath).Replace('\\', '/');
                    if (dataDir.EndsWith("/Data", StringComparison.Ordinal))
                    {
                        string p = dataDir + "/" + ManifestFileName;
                        if (!candidates.Contains(p)) candidates.Add(p);
                    }
                }
            }

            bool hasDeterministicCandidateOnDisk = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!string.IsNullOrEmpty(candidates[i]) && File.Exists(ToAbsolutePath(candidates[i])))
                {
                    hasDeterministicCandidateOnDisk = true;
                    break;
                }
            }

            if (!hasDeterministicCandidateOnDisk && AssetDatabase.IsValidFolder(YBPathUtility.GeneratedRoot))
            {
                string[] guids = AssetDatabase.FindAssets("YBGenerationManifest", new[] { YBPathUtility.GeneratedRoot });
                for (int i = 0; i < guids.Length; i++)
                {
                    string p = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!string.IsNullOrEmpty(p) && p.EndsWith("/" + ManifestFileName, StringComparison.OrdinalIgnoreCase) && !candidates.Contains(p))
                        candidates.Add(p);
                }
            }

            var incompatible = new List<string>();
            for (int i = 0; i < candidates.Count; i++)
            {
                string path = candidates[i];
                if (string.IsNullOrEmpty(path)) continue;
                string absolute = ToAbsolutePath(path);
                if (!File.Exists(absolute)) continue;
                YBGeneratedAssetManifest m = LoadTypedAtPath(path);
                if (m != null && Matches(m, avatarRoot, profile))
                {
                    result.manifest = m;
                    result.assetPath = path;
                    result.message = "已识别 Generation Manifest：" + path;
                    return result;
                }

                if (File.Exists(ToAbsolutePath(path))) incompatible.Add(path);
            }

            if (incompatible.Count > 0)
            {
                result.assetPath = incompatible[0];
                result.message = "找到 Manifest 文件，但当前 Unity 无法将它识别为 YBGeneratedAssetManifest：\n" + incompatible[0] +
                                 "\n\n这通常是脚本/程序集名称变化、旧资产 m_Script 丢失或类型迁移造成的。可在 Builder 的“验证”页执行兼容修复。";
                return result;
            }

            result.message = "没有找到当前 Profile 对应的 Generation Manifest。";
            return result;
        }

        public static YBAvatarInstallRecord ResolveInstallRecord(string outputRoot)
        {
            if (string.IsNullOrEmpty(outputRoot)) return null;
            string expected = outputRoot.TrimEnd('/') + "/Data/YBInstallRecord.asset";
            var direct = AssetDatabase.LoadAssetAtPath<YBAvatarInstallRecord>(expected);
            if (direct != null) return direct;

            string data = outputRoot.TrimEnd('/') + "/Data";
            if (!AssetDatabase.IsValidFolder(data)) return null;
            string[] guids = AssetDatabase.FindAssets("t:YBAvatarInstallRecord", new[] { data });
            for (int i = 0; i < guids.Length; i++)
            {
                var item = AssetDatabase.LoadAssetAtPath<YBAvatarInstallRecord>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (item != null) return item;
            }
            return null;
        }

        public static YBManifestResolveResult RepairAtPath(string assetPath, GameObject avatarRoot, YBDissolveProfile profile)
        {
            var result = new YBManifestResolveResult { assetPath = assetPath };
            if (string.IsNullOrEmpty(assetPath))
            {
                result.message = "没有可修复的 Manifest 路径。";
                return result;
            }

            string abs = ToAbsolutePath(assetPath);
            if (!File.Exists(abs))
            {
                result.message = "Manifest 文件不存在：" + assetPath;
                return result;
            }

            var typed = LoadTypedAtPath(assetPath);
            if (typed != null)
            {
                result.manifest = typed;
                result.message = "Manifest 已经可以正常识别，无需修复。";
                return result;
            }

            string yaml = File.ReadAllText(abs, Encoding.UTF8);
            YBGeneratedAssetManifest parsed;
            string parseError;
            if (!TryParseYamlManifest(yaml, out parsed, out parseError))
            {
                result.message = "兼容解析失败：" + parseError;
                return result;
            }

            if (profile != null) parsed.profile = profile;
            if (avatarRoot != null && string.IsNullOrEmpty(parsed.avatarHint)) parsed.avatarHint = avatarRoot.name;

            string dataDir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string backupDir = dataDir + "/LegacyManifestBackup";
            EnsureAssetFolder(backupDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string backupAssetPath = backupDir + "/YBGenerationManifest_unrecognized_" + stamp + ".txt";
            File.Copy(abs, ToAbsolutePath(backupAssetPath), true);
            AssetDatabase.ImportAsset(backupAssetPath, ImportAssetOptions.ForceSynchronousImport);

            AssetDatabase.DeleteAsset(assetPath);
            parsed.name = "YBGenerationManifest";
            AssetDatabase.CreateAsset(parsed, assetPath);
            EditorUtility.SetDirty(parsed);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            result.manifest = AssetDatabase.LoadAssetAtPath<YBGeneratedAssetManifest>(assetPath);
            result.repaired = result.manifest != null;
            result.message = result.repaired
                ? "Manifest 兼容修复完成。旧 YAML 已备份：\n" + backupAssetPath + "\n新资产：\n" + assetPath
                : "已重建 Manifest 文件，但 Unity 仍无法加载当前类型。请检查 _YBTools.Runtime.asmdef 与 YBGenerationData.cs 是否存在编译错误。";
            return result;
        }

        private static YBGeneratedAssetManifest LoadTypedAtPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var direct = AssetDatabase.LoadAssetAtPath<YBGeneratedAssetManifest>(path);
            if (direct != null) return direct;
            UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < all.Length; i++)
            {
                var m = all[i] as YBGeneratedAssetManifest;
                if (m != null) return m;
            }
            return null;
        }

        private static bool Matches(YBGeneratedAssetManifest m, GameObject avatarRoot, YBDissolveProfile profile)
        {
            if (m == null) return false;
            if (profile != null && m.profile == profile) return true;
            if (avatarRoot != null && !string.IsNullOrEmpty(m.avatarHint) && string.Equals(m.avatarHint, avatarRoot.name, StringComparison.Ordinal)) return true;
            return profile == null && avatarRoot == null;
        }

        private static bool TryParseYamlManifest(string yaml, out YBGeneratedAssetManifest manifest, out string error)
        {
            manifest = ScriptableObject.CreateInstance<YBGeneratedAssetManifest>();
            manifest.name = "YBGenerationManifest";
            error = null;
            try
            {
                string[] lines = yaml.Replace("\r\n", "\n").Split('\n');
                YBGeneratedRendererRecord currentRenderer = null;
                YBGeneratedMaterialSlotRecord currentSlot = null;

                for (int i = 0; i < lines.Length; i++)
                {
                    string raw = lines[i];
                    string trim = raw.Trim();
                    if (trim.Length == 0) continue;

                    if (raw.StartsWith("  - outfitGroup:", StringComparison.Ordinal))
                    {
                        currentRenderer = new YBGeneratedRendererRecord();
                        currentRenderer.outfitGroup = Scalar(trim, "- outfitGroup:");
                        manifest.renderers.Add(currentRenderer);
                        currentSlot = null;
                        continue;
                    }
                    if (currentRenderer != null && raw.StartsWith("    - slotIndex:", StringComparison.Ordinal))
                    {
                        currentSlot = new YBGeneratedMaterialSlotRecord();
                        currentSlot.slotIndex = ParseInt(Scalar(trim, "- slotIndex:"));
                        currentRenderer.materialSlots.Add(currentSlot);
                        continue;
                    }

                    if (currentSlot != null && raw.StartsWith("      ", StringComparison.Ordinal))
                    {
                        if (trim.StartsWith("sourceMaterial:", StringComparison.Ordinal)) currentSlot.sourceMaterial = LoadReference<Material>(ReadReference(lines, ref i, trim));
                        else if (trim.StartsWith("sourceShader:", StringComparison.Ordinal)) currentSlot.sourceShader = Scalar(trim, "sourceShader:");
                        else if (trim.StartsWith("resolvedMode:", StringComparison.Ordinal)) currentSlot.resolvedMode = Scalar(trim, "resolvedMode:");
                        else if (trim.StartsWith("generationAction:", StringComparison.Ordinal)) currentSlot.generationAction = Scalar(trim, "generationAction:");
                        else if (trim.StartsWith("dissolveMaterial:", StringComparison.Ordinal)) currentSlot.dissolveMaterial = LoadReference<Material>(ReadReference(lines, ref i, trim));
                        else if (trim.StartsWith("overlayMaterial:", StringComparison.Ordinal)) currentSlot.overlayMaterial = LoadReference<Material>(ReadReference(lines, ref i, trim));
                        else if (trim.StartsWith("note:", StringComparison.Ordinal)) currentSlot.note = DecodeYamlString(Scalar(trim, "note:"));
                        continue;
                    }

                    if (currentRenderer != null && raw.StartsWith("    ", StringComparison.Ordinal) && !raw.StartsWith("      ", StringComparison.Ordinal))
                    {
                        if (trim.StartsWith("editorPath:", StringComparison.Ordinal)) currentRenderer.editorPath = DecodeYamlString(Scalar(trim, "editorPath:"));
                        else if (trim.StartsWith("rendererName:", StringComparison.Ordinal)) currentRenderer.rendererName = DecodeYamlString(Scalar(trim, "rendererName:"));
                        else if (trim.StartsWith("rendererType:", StringComparison.Ordinal)) currentRenderer.rendererType = DecodeYamlString(Scalar(trim, "rendererType:"));
                        else if (trim.StartsWith("meshName:", StringComparison.Ordinal)) currentRenderer.meshName = DecodeYamlString(Scalar(trim, "meshName:"));
                        else if (trim.StartsWith("maPathRisk:", StringComparison.Ordinal)) currentRenderer.maPathRisk = ParseInt(Scalar(trim, "maPathRisk:")) != 0;
                        else if (trim.StartsWith("sourceMesh:", StringComparison.Ordinal)) currentRenderer.sourceMesh = LoadReference<Mesh>(ReadReference(lines, ref i, trim));
                        else if (trim.StartsWith("bakedMesh:", StringComparison.Ordinal)) currentRenderer.bakedMesh = LoadReference<Mesh>(ReadReference(lines, ref i, trim));
                        continue;
                    }

                    if (!raw.StartsWith("  ", StringComparison.Ordinal)) continue;
                    if (trim.StartsWith("dataVersion:", StringComparison.Ordinal)) manifest.dataVersion = ParseInt(Scalar(trim, "dataVersion:"));
                    else if (trim.StartsWith("toolVersion:", StringComparison.Ordinal)) manifest.toolVersion = DecodeYamlString(Scalar(trim, "toolVersion:"));
                    else if (trim.StartsWith("generatedUtc:", StringComparison.Ordinal)) manifest.generatedUtc = DecodeYamlString(Scalar(trim, "generatedUtc:"));
                    else if (trim.StartsWith("avatarHint:", StringComparison.Ordinal)) manifest.avatarHint = DecodeYamlString(Scalar(trim, "avatarHint:"));
                    else if (trim.StartsWith("profile:", StringComparison.Ordinal)) manifest.profile = LoadReference<YBDissolveProfile>(ReadReference(lines, ref i, trim));
                    else if (trim.StartsWith("bakedScanDataVersion:", StringComparison.Ordinal)) manifest.bakedScanDataVersion = ParseInt(Scalar(trim, "bakedScanDataVersion:"));
                    else if (trim.StartsWith("bakedScanMoveDirection:", StringComparison.Ordinal)) manifest.bakedScanMoveDirection = ParseVector3(Scalar(trim, "bakedScanMoveDirection:"), Vector3.down);
                    else if (trim.StartsWith("sourceRendererCount:", StringComparison.Ordinal)) manifest.sourceRendererCount = ParseInt(Scalar(trim, "sourceRendererCount:"));
                    else if (trim.StartsWith("generatedMeshCount:", StringComparison.Ordinal)) manifest.generatedMeshCount = ParseInt(Scalar(trim, "generatedMeshCount:"));
                    else if (trim.StartsWith("generatedDissolveMaterialCount:", StringComparison.Ordinal)) manifest.generatedDissolveMaterialCount = ParseInt(Scalar(trim, "generatedDissolveMaterialCount:"));
                    else if (trim.StartsWith("generatedOverlayMaterialCount:", StringComparison.Ordinal)) manifest.generatedOverlayMaterialCount = ParseInt(Scalar(trim, "generatedOverlayMaterialCount:"));
                    else if (trim.StartsWith("skippedSlotCount:", StringComparison.Ordinal)) manifest.skippedSlotCount = ParseInt(Scalar(trim, "skippedSlotCount:"));
                }

                if (manifest.renderers == null || manifest.renderers.Count == 0)
                    throw new InvalidOperationException("YAML 中没有 renderers 记录。");
                if (manifest.sourceRendererCount <= 0) manifest.sourceRendererCount = manifest.renderers.Count;
                return true;
            }
            catch (Exception ex)
            {
                if (manifest != null) UnityEngine.Object.DestroyImmediate(manifest);
                manifest = null;
                error = ex.Message;
                return false;
            }
        }

        private struct AssetRefInfo
        {
            public long fileId;
            public string guid;
        }

        private static AssetRefInfo ReadReference(string[] lines, ref int index, string firstTrimmed)
        {
            var sb = new StringBuilder(firstTrimmed);
            while (sb.ToString().IndexOf('}') < 0 && index + 1 < lines.Length)
            {
                index++;
                sb.Append(' ').Append(lines[index].Trim());
            }
            string text = sb.ToString();
            var info = new AssetRefInfo();
            Match fm = Regex.Match(text, @"fileID:\s*(-?\d+)");
            Match gm = Regex.Match(text, @"guid:\s*([0-9a-fA-F]+)");
            if (fm.Success) long.TryParse(fm.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out info.fileId);
            if (gm.Success) info.guid = gm.Groups[1].Value;
            return info;
        }

        private static T LoadReference<T>(AssetRefInfo info) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(info.guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(info.guid);
            if (string.IsNullOrEmpty(path)) return null;
            UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < all.Length; i++)
            {
                T t = all[i] as T;
                if (t == null) continue;
                string g;
                long local;
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(t, out g, out local) && g == info.guid && (info.fileId == 0 || local == info.fileId))
                    return t;
            }
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static string Scalar(string text, string key)
        {
            int idx = text.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return string.Empty;
            return text.Substring(idx + key.Length).Trim();
        }

        private static int ParseInt(string value)
        {
            int v;
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
            return v;
        }

        private static Vector3 ParseVector3(string value, Vector3 fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            Match x = Regex.Match(value, @"x:\s*(-?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)");
            Match y = Regex.Match(value, @"y:\s*(-?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)");
            Match z = Regex.Match(value, @"z:\s*(-?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)");
            if (!x.Success || !y.Success || !z.Success) return fallback;

            float vx, vy, vz;
            if (!float.TryParse(x.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out vx) ||
                !float.TryParse(y.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out vy) ||
                !float.TryParse(z.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out vz))
                return fallback;

            Vector3 parsed = new Vector3(vx, vy, vz);
            return parsed.sqrMagnitude < 0.000001f ? fallback : parsed.normalized;
        }

        private static string DecodeYamlString(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                value = value.Substring(1, value.Length - 2);
            value = Regex.Replace(value, @"\\u([0-9a-fA-F]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
            value = value.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
            return value;
        }

        private static void EnsureAssetFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string ToAbsolutePath(string assetPath)
        {
            return Path.GetFullPath(assetPath);
        }
    }
}
