using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace YBTools.Dissolve.Editor
{
    internal sealed class YBMaterialVariantBuildResult
    {
        public bool success;
        public string message;
        public YBMaterialVariantInstallRecord record;
    }

    internal static class YBMaterialVariantBuilder
    {
        private const string OpaqueShader = "YBTools/溶解/FinalBaked/Hair Dissolve";
        private const string CutoutShader = "YBTools/溶解/FinalBaked/Hair Dissolve Cutout";
        private const string TransparentShader = "YBTools/溶解/FinalBaked/Hair Dissolve Transparent";
        private const string RefractionShader = "YBTools/溶解/FinalBaked/Hair Dissolve Refraction";
        private const string RefractionBlurShader = "YBTools/溶解/FinalBaked/Hair Dissolve Refraction Blur";
        private const string FurShader = "YBTools/溶解/FinalBaked/Hair Dissolve Fur";
        private const string FurCutoutShader = "YBTools/溶解/FinalBaked/Hair Dissolve Fur Cutout";
        private const string FurTwoPassShader = "YBTools/溶解/FinalBaked/Hair Dissolve Fur TwoPass";
        private const string GemShader = "YBTools/溶解/FinalBaked/Hair Dissolve Gem";

        private static readonly int ScanHeight = Shader.PropertyToID("_YBScanHeight");
        private static readonly int ScanRole = Shader.PropertyToID("_YBScanRole");
        private static readonly int ScanEnabled = Shader.PropertyToID("_YBScanDissolveEnabled");
        private static readonly int ScanNoise = Shader.PropertyToID("_YBScanNoiseStrength");

        internal static string ValidateInputs(GameObject avatarRoot, YBDissolveProfile profile, YBAvatarInstallRecord baseInstallRecord)
        {
            if (avatarRoot == null) return "材质变体检查失败：Avatar Root 为空。";
            if (profile == null) return "材质变体检查失败：Profile 为空。";
            if (profile.scan.directionPreset == YBScanDirectionPreset.Custom &&
                profile.scan.customDirection.sqrMagnitude < 0.000001f)
                return "材质变体检查失败：自定义扫描方向不能为 (0,0,0)。";
            if (profile.materialVariants == null || profile.materialVariants.Count == 0)
                return "当前没有配置材质变体。";

            var errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < profile.materialVariants.Count; i++)
            {
                var v = profile.materialVariants[i];
                if (v == null) { errors.Add("变体 " + (i + 1) + " 为空。"); continue; }
                if (string.IsNullOrWhiteSpace(v.rendererPath)) { errors.Add("变体 " + (i + 1) + " 未选择 Renderer。"); continue; }
                SkinnedMeshRenderer r = ResolveRenderer(avatarRoot, v.rendererPath);
                if (r == null) { errors.Add("找不到 SkinnedMeshRenderer：" + v.rendererPath); continue; }
                if (r.sharedMesh == null) { errors.Add("Renderer 缺少 Mesh：" + v.rendererPath); continue; }
                if (!r.sharedMesh.isReadable) { errors.Add("Mesh Read/Write 未开启：" + r.sharedMesh.name); continue; }
                if (v.materialSlot < 0 || v.materialSlot >= r.sharedMaterials.Length || v.materialSlot >= r.sharedMesh.subMeshCount)
                {
                    errors.Add("Material Slot/SubMesh 越界：" + v.rendererPath + " #" + v.materialSlot);
                    continue;
                }
                if (v.outfitAMaterial == null || v.outfitBMaterial == null)
                {
                    errors.Add("A/B Material 未完整指定：" + v.rendererPath + " #" + v.materialSlot);
                    continue;
                }
                string key = v.rendererPath + "|" + v.materialSlot;
                if (!seen.Add(key)) errors.Add("重复材质变体：" + key);

                if (baseInstallRecord != null && baseInstallRecord.renderers != null &&
                    baseInstallRecord.renderers.Any(x => x != null && string.Equals(x.installedRendererPath, v.rendererPath, StringComparison.Ordinal)))
                {
                    errors.Add("当前版本不允许把已经作为 Outfit Renderer 安装过的对象再次作为材质变体：" + v.rendererPath);
                }

                string shaderError;
                if (ResolveTargetShader(v.outfitAMaterial, out shaderError) == null)
                    errors.Add("A Material 不支持：" + v.outfitAMaterial.name + " | " + shaderError);
                if (ResolveTargetShader(v.outfitBMaterial, out shaderError) == null)
                    errors.Add("B Material 不支持：" + v.outfitBMaterial.name + " | " + shaderError);
            }

            if (errors.Count > 0)
                return "材质变体输入检查失败：\n• " + string.Join("\n• ", errors.Take(12).ToArray()) +
                       (errors.Count > 12 ? "\n…另有 " + (errors.Count - 12) + " 项" : string.Empty);

            return "材质变体输入检查通过：" + profile.materialVariants.Count +
                   " 项。将使用同一个 SkinnedMeshRenderer 追加重复 SubMesh，A/B 材质由相反 ScanRole 同步扫描。";
        }

        internal static YBMaterialVariantBuildResult BuildAndInstall(
            GameObject avatarRoot,
            YBDissolveProfile profile,
            YBAvatarInstallRecord baseInstallRecord,
            string outputRoot,
            bool allowOverwriteUv4)
        {
            var result = new YBMaterialVariantBuildResult();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("请退出 Play Mode 后安装材质变体。");
                string check = ValidateInputs(avatarRoot, profile, baseInstallRecord);
                if (check.IndexOf("通过", StringComparison.Ordinal) < 0) throw new InvalidOperationException(check);

                string dataDir = outputRoot.TrimEnd('/') + "/Data";
                string meshDir = outputRoot.TrimEnd('/') + "/Mesh/Variants";
                string matDir = outputRoot.TrimEnd('/') + "/Materials/Variants";
                EnsureFolder(dataDir); EnsureFolder(meshDir); EnsureFolder(matDir);
                string recordPath = dataDir + "/YBMaterialVariantInstallRecord.asset";

                YBMaterialVariantInstallRecord previous = AssetDatabase.LoadAssetAtPath<YBMaterialVariantInstallRecord>(recordPath);
                if (previous != null) RestoreInternal(avatarRoot, previous, false);

                var temp = ScriptableObject.CreateInstance<YBMaterialVariantInstallRecord>();
                temp.name = "YBMaterialVariantInstallRecord";
                temp.toolVersion = YBToolVersion.Version;
                temp.avatarHint = avatarRoot.name;
                temp.profile = profile;

                for (int i = 0; i < profile.materialVariants.Count; i++)
                {
                    YBMaterialVariantProfile v = profile.materialVariants[i];
                    SkinnedMeshRenderer r = ResolveRenderer(avatarRoot, v.rendererPath);
                    Mesh sourceMesh = r.sharedMesh;
                    Material[] sourceMaterials = r.sharedMaterials;

                    string safe = YBPathUtility.SanitizeAssetName(r.name, "Renderer") + "_S" + v.materialSlot + "_" + ShortHash(v.rendererPath);
                    string meshPath = meshDir + "/YB_VariantMesh_" + safe + ".asset";
                    Mesh generated = YBBakedMeshBuilder.BuildOrUpdate(
                        avatarRoot.transform, r, sourceMesh, meshPath, allowOverwriteUv4,
                        profile.scan.GetMoveDirection());
                    AppendDuplicateSubmesh(generated, v.materialSlot);

                    Material a0 = BuildRoleMaterial(v.outfitAMaterial, 0f, profile, matDir + "/YB_" + safe + "_A_Role0.mat");
                    Material a1 = BuildRoleMaterial(v.outfitAMaterial, 1f, profile, matDir + "/YB_" + safe + "_A_Role1.mat");
                    Material b0 = BuildRoleMaterial(v.outfitBMaterial, 0f, profile, matDir + "/YB_" + safe + "_B_Role0.mat");
                    Material b1 = BuildRoleMaterial(v.outfitBMaterial, 1f, profile, matDir + "/YB_" + safe + "_B_Role1.mat");

                    var mats = new List<Material>(sourceMaterials);
                    int appended = mats.Count;
                    bool defaultA = profile.defaultOutfit != YBOutfitDefault.OutfitB;
                    mats[v.materialSlot] = defaultA ? a0 : a1;
                    mats.Add(defaultA ? b1 : b0);

                    Undo.RecordObject(r, "Install YB material variant");
                    r.sharedMesh = generated;
                    r.sharedMaterials = mats.ToArray();
                    EditorUtility.SetDirty(r);

                    var rr = new YBMaterialVariantRendererRecord
                    {
                        rendererPath = YBPathUtility.GetRelativePath(avatarRoot.transform, r.transform),
                        rendererName = r.name,
                        originalMesh = sourceMesh,
                        generatedMesh = generated,
                        sourceSlot = v.materialSlot,
                        appendedSlot = appended,
                        outfitARole0 = a0,
                        outfitARole1 = a1,
                        outfitBRole0 = b0,
                        outfitBRole1 = b1
                    };
                    rr.originalMaterials.AddRange(sourceMaterials);
                    temp.renderers.Add(rr);
                }

                YBMaterialVariantInstallRecord saved = SaveRecord(temp, recordPath);
                AssetDatabase.SaveAssets();
                result.success = true;
                result.record = saved;
                result.message = "材质变体已正式安装：" + saved.renderers.Count +
                                 " 个 Renderer。下一步重新生成 FX Controller，Builder 会自动写入 A/B 材质槽切换曲线。";
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                result.message = "材质变体安装失败：" + ex.Message;
            }
            return result;
        }

        internal static YBMaterialVariantInstallRecord LoadExisting(string outputRoot)
        {
            if (string.IsNullOrEmpty(outputRoot)) return null;
            return AssetDatabase.LoadAssetAtPath<YBMaterialVariantInstallRecord>(outputRoot.TrimEnd('/') + "/Data/YBMaterialVariantInstallRecord.asset");
        }

        internal static string ValidateInstalled(GameObject avatarRoot, YBMaterialVariantInstallRecord record)
        {
            if (record == null) return "材质变体尚未安装。";
            int ok = 0;
            var issues = new List<string>();
            foreach (var rr in record.renderers)
            {
                SkinnedMeshRenderer r = ResolveRenderer(avatarRoot, rr.rendererPath);
                if (r == null) { issues.Add("缺少 Renderer：" + rr.rendererPath); continue; }
                Material[] mats = r.sharedMaterials;
                bool pass = r.sharedMesh == rr.generatedMesh && rr.sourceSlot >= 0 && rr.sourceSlot < mats.Length && rr.appendedSlot >= 0 && rr.appendedSlot < mats.Length;
                if (pass) ok++; else issues.Add("Mesh/Material Slot 不一致：" + rr.rendererPath);
            }
            if (issues.Count == 0) return "材质变体安装验证通过：" + ok + "/" + record.renderers.Count + "。";
            return "材质变体安装验证失败：" + ok + "/" + record.renderers.Count + "。\n• " + string.Join("\n• ", issues.Take(10).ToArray());
        }

        internal static string Restore(GameObject avatarRoot, string outputRoot)
        {
            YBMaterialVariantInstallRecord record = LoadExisting(outputRoot);
            if (record == null) return "没有找到材质变体安装记录。";
            RestoreInternal(avatarRoot, record, true);
            return "已恢复材质变体安装前的 Mesh / Material。生成资产仍保留。";
        }

        internal static void ApplyBindings(AnimationClip clip, YBMaterialVariantInstallRecord record, bool outfitAState)
        {
            if (clip == null || record == null || record.renderers == null || record.renderers.Count == 0) return;
            EditorCurveBinding sourceBinding = AnimationUtility.GetCurveBindings(clip)
                .FirstOrDefault(b => b.propertyName.IndexOf("_YBScanHeight", StringComparison.OrdinalIgnoreCase) >= 0);
            AnimationCurve sourceCurve = string.IsNullOrEmpty(sourceBinding.propertyName) ? null : AnimationUtility.GetEditorCurve(clip, sourceBinding);
            float end = Mathf.Max(1f / 60f, clip.length);

            foreach (var rr in record.renderers)
            {
                if (sourceCurve != null)
                {
                    var c = new AnimationCurve(sourceCurve.keys) { preWrapMode = sourceCurve.preWrapMode, postWrapMode = sourceCurve.postWrapMode };
                    var b = EditorCurveBinding.FloatCurve(rr.rendererPath, typeof(Renderer), "material._YBScanHeight");
                    AnimationUtility.SetEditorCurve(clip, b, c);
                }
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(rr.rendererPath, typeof(Renderer), "material._YBScanDissolveEnabled"),
                    AnimationCurve.Constant(0f, end, 1f));

                Material a = outfitAState ? rr.outfitARole0 : rr.outfitARole1;
                Material bmat = outfitAState ? rr.outfitBRole1 : rr.outfitBRole0;
                SetMaterialSlotCurve(clip, rr.rendererPath, rr.sourceSlot, a, end);
                SetMaterialSlotCurve(clip, rr.rendererPath, rr.appendedSlot, bmat, end);
            }
        }

        private static void SetMaterialSlotCurve(AnimationClip clip, string path, int slot, Material material, float end)
        {
            var binding = EditorCurveBinding.PPtrCurve(path, typeof(Renderer), "m_Materials.Array.data[" + slot + "]");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, new[]
            {
                new ObjectReferenceKeyframe { time = 0f, value = material },
                new ObjectReferenceKeyframe { time = end, value = material }
            });
        }

        private static void RestoreInternal(GameObject avatarRoot, YBMaterialVariantInstallRecord record, bool save)
        {
            if (avatarRoot == null || record == null) return;
            foreach (var rr in record.renderers)
            {
                SkinnedMeshRenderer r = ResolveRenderer(avatarRoot, rr.rendererPath);
                if (r == null) continue;
                Undo.RecordObject(r, "Restore YB material variant");
                r.sharedMesh = rr.originalMesh;
                r.sharedMaterials = rr.originalMaterials != null ? rr.originalMaterials.ToArray() : Array.Empty<Material>();
                EditorUtility.SetDirty(r);
            }
            if (save) AssetDatabase.SaveAssets();
        }

        private static SkinnedMeshRenderer ResolveRenderer(GameObject avatarRoot, string path)
        {
            if (avatarRoot == null || string.IsNullOrEmpty(path)) return null;
            Transform t = avatarRoot.transform.Find(path);
            SkinnedMeshRenderer direct = t != null ? t.GetComponent<SkinnedMeshRenderer>() : null;
            if (direct != null) return direct;
            string name = path.Split('/').Last();
            var all = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x => x.name == name).ToArray();
            return all.Length == 1 ? all[0] : null;
        }

        private static void AppendDuplicateSubmesh(Mesh mesh, int sourceSlot)
        {
            if (mesh == null) throw new InvalidOperationException("生成 Mesh 为空。");
            if (sourceSlot < 0 || sourceSlot >= mesh.subMeshCount) throw new InvalidOperationException("SubMesh 越界：" + sourceSlot);
            int oldCount = mesh.subMeshCount;
            int[] indices = mesh.GetIndices(sourceSlot);
            MeshTopology topology = mesh.GetTopology(sourceSlot);
            mesh.subMeshCount = oldCount + 1;
            mesh.SetIndices(indices, topology, oldCount, true);
            EditorUtility.SetDirty(mesh);
        }

        private static Material BuildRoleMaterial(Material source, float role, YBDissolveProfile profile, string path)
        {
            string shaderError;
            Shader shader = ResolveTargetShader(source, out shaderError);
            if (shader == null) throw new InvalidOperationException(shaderError);
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material temp = new Material(source) { name = Path.GetFileNameWithoutExtension(path) };
            int queue = source.renderQueue;
            temp.shader = shader;
            temp.renderQueue = queue;
            if (temp.HasProperty(ScanHeight)) temp.SetFloat(ScanHeight, profile.scan.startHeight);
            if (temp.HasProperty(ScanRole)) temp.SetFloat(ScanRole, role);
            if (temp.HasProperty(ScanEnabled)) temp.SetFloat(ScanEnabled, 1f);
            if (temp.HasProperty(ScanNoise)) temp.SetFloat(ScanNoise, profile.scan.noiseStrength);

            if (existing != null)
            {
                EditorUtility.CopySerialized(temp, existing);
                existing.name = temp.name;
                UnityEngine.Object.DestroyImmediate(temp);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(temp, path);
            return temp;
        }

        private static Shader ResolveTargetShader(Material source, out string error)
        {
            error = null;
            if (source == null) { error = "Material 为空。"; return null; }
            string n = source.shader != null ? source.shader.name : string.Empty;
            string lower = n.ToLowerInvariant();
            string target;
            if (lower.Contains("refractionblur") || (lower.Contains("refraction") && lower.Contains("blur"))) target = RefractionBlurShader;
            else if (lower.Contains("refraction")) target = RefractionShader;
            else if (lower.Contains("furtwopass") || (lower.Contains("fur") && lower.Contains("twopass"))) target = FurTwoPassShader;
            else if (lower.Contains("furcutout") || (lower.Contains("fur") && lower.Contains("cutout"))) target = FurCutoutShader;
            else if (lower.Contains("fur")) target = FurShader;
            else if (lower.Contains("gem")) target = GemShader;
            else if (lower.Contains("transparent")) target = TransparentShader;
            else if (lower.Contains("cutout")) target = CutoutShader;
            else if (source.HasProperty("_TransparentMode"))
            {
                int mode = Mathf.RoundToInt(source.GetFloat("_TransparentMode"));
                switch (mode)
                {
                    case 0: target = OpaqueShader; break;
                    case 1: target = CutoutShader; break;
                    case 2: target = TransparentShader; break;
                    case 3: target = RefractionShader; break;
                    case 4: target = FurShader; break;
                    case 5: target = FurCutoutShader; break;
                    case 6: target = GemShader; break;
                    default: error = "无法识别 lilToon _TransparentMode=" + mode; return null;
                }
            }
            else target = OpaqueShader;

            Shader shader = Shader.Find(target);
            if (shader == null || !shader.isSupported) { error = "找不到/不支持目标 Shader：" + target; return null; }
#if UNITY_2021_2_OR_NEWER
            if (ShaderUtil.ShaderHasError(shader)) { error = "目标 Shader 有编译错误：" + target; return null; }
#endif
            return shader;
        }

        private static YBMaterialVariantInstallRecord SaveRecord(YBMaterialVariantInstallRecord temp, string path)
        {
            YBMaterialVariantInstallRecord existing = AssetDatabase.LoadAssetAtPath<YBMaterialVariantInstallRecord>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(temp, existing);
                UnityEngine.Object.DestroyImmediate(temp);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            UnityEngine.Object occupied = AssetDatabase.LoadMainAssetAtPath(path);
            if (occupied != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(temp, path);
            return temp;
        }

        private static void EnsureFolder(string fullPath)
        {
            string path = fullPath.Replace('\\', '/').TrimEnd('/');
            string[] parts = path.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets") throw new InvalidOperationException("无效 Asset 路径：" + fullPath);
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string ShortHash(string value)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in value ?? string.Empty) { h ^= c; h *= 16777619; }
                return h.ToString("x8");
            }
        }
    }
}
