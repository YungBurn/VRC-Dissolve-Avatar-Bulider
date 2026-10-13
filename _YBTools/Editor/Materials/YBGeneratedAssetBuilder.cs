using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace YBTools.Dissolve.Editor
{
    internal sealed class YBGenerationResult
    {
        public bool success;
        public string message;
        public YBGeneratedAssetManifest manifest;
    }

    internal static class YBGeneratedAssetBuilder
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
        private const string OverlayShader = "YBTools/溶解/FinalBaked/Scan Overlay";

        public static YBGenerationResult Generate(
            GameObject avatarRoot,
            YBDissolveProfile profile,
            YBAnalysisReport report,
            string outputRoot,
            bool allowOverwriteUv4)
        {
            var result = new YBGenerationResult();
            if (avatarRoot == null || profile == null || report == null)
            {
                result.message = "Avatar / Profile / 分析报告不完整。";
                return result;
            }

            Shader opaque = Shader.Find(OpaqueShader);
            Shader cutout = Shader.Find(CutoutShader);
            Shader transparent = Shader.Find(TransparentShader);
            Shader refraction = Shader.Find(RefractionShader);
            Shader refractionBlur = Shader.Find(RefractionBlurShader);
            Shader fur = Shader.Find(FurShader);
            Shader furCutout = Shader.Find(FurCutoutShader);
            Shader furTwoPass = Shader.Find(FurTwoPassShader);
            Shader gem = Shader.Find(GemShader);
            Shader overlay = Shader.Find(OverlayShader);
            if (opaque == null || cutout == null || transparent == null || overlay == null)
            {
                result.message = "基础 FinalBaked Shader 未完整加载，无法生成。";
                return result;
            }

            string preflightError = ValidatePreflight(profile, report, allowOverwriteUv4);
            if (!string.IsNullOrEmpty(preflightError))
            {
                result.message = preflightError;
                return result;
            }

            string meshFolder = outputRoot + "/Mesh";
            string dissolveFolder = outputRoot + "/Materials/Dissolve";
            string overlayFolder = outputRoot + "/Materials/Overlay";
            string dataFolder = outputRoot + "/Data";

            var manifestTemp = ScriptableObject.CreateInstance<YBGeneratedAssetManifest>();
            manifestTemp.name = "YBGenerationManifest";
            manifestTemp.dataVersion = 2;
            manifestTemp.toolVersion = YBToolVersion.Version;
            manifestTemp.generatedUtc = DateTime.UtcNow.ToString("o");
            manifestTemp.avatarHint = avatarRoot.name;
            manifestTemp.profile = profile;
            manifestTemp.bakedScanDataVersion = YBToolVersion.BakedScanDataVersion;
            manifestTemp.bakedScanMoveDirection = profile.scan.GetMoveDirection();
            manifestTemp.sourceRendererCount = report.RendererCount;

            var dissolveCache = new Dictionary<string, Material>();
            var overlayCache = new Dictionary<string, Material>();

            try
            {
                for (int i = 0; i < report.renderers.Count; i++)
                {
                    YBRendererAnalysis analyzed = report.renderers[i];
                    var record = new YBGeneratedRendererRecord
                    {
                        outfitGroup = analyzed.outfitGroup,
                        editorPath = analyzed.hierarchyPath,
                        rendererName = analyzed.rendererName,
                        rendererType = analyzed.rendererType,
                        meshName = analyzed.meshName,
                        maPathRisk = analyzed.maPathRisk,
                        sourceMesh = analyzed.mesh
                    };

                    bool needsBakedMesh = false;
                    for (int s = 0; s < analyzed.materials.Count; s++)
                    {
                        YBMaterialSlotAnalysis slot = analyzed.materials[s];
                        ResolvedSlot resolved = ResolveSlot(profile, analyzed, slot);
                        var slotRecord = new YBGeneratedMaterialSlotRecord
                        {
                            slotIndex = slot.slotIndex,
                            sourceMaterial = slot.material,
                            sourceShader = slot.shaderName,
                            resolvedMode = resolved.mode.ToString(),
                            generationAction = resolved.automatic ? "Auto" : resolved.action.ToString(),
                            note = resolved.note
                        };

                        if (resolved.action == YBUnsupportedMaterialAction.Skip)
                        {
                            manifestTemp.skippedSlotCount++;
                            record.materialSlots.Add(slotRecord);
                            continue;
                        }

                        needsBakedMesh = true;
                        int role = GetInitialRole(profile, analyzed.outfitGroup);
                        string sourceKey = GetSourceMaterialKey(slot.material, slot.assetGuid, analyzed.hierarchyPath, slot.slotIndex);
                        string groupKey = analyzed.outfitGroup == "Outfit B" ? "B" : "A";
                        string dissolveKey = groupKey + "|" + sourceKey + "|" + resolved.mode + "|role=" + role;
                        Material dissolve;
                        if (!dissolveCache.TryGetValue(dissolveKey, out dissolve))
                        {
                            string matName = "YB_Dissolve_" + groupKey + "_" + SafeMaterialName(slot.materialName) + "_" + ShortHash(dissolveKey);
                            string path = dissolveFolder + "/" + matName + ".mat";
                            dissolve = BuildDissolveMaterial(slot.material, GetShader(resolved.mode, opaque, cutout, transparent, refraction, refractionBlur, fur, furCutout, furTwoPass, gem), profile, role, path);
                            dissolveCache[dissolveKey] = dissolve;
                            manifestTemp.generatedDissolveMaterialCount++;
                        }

                        string overlayKey = sourceKey + "|" + resolved.mode;
                        Material overlayMaterial;
                        if (!overlayCache.TryGetValue(overlayKey, out overlayMaterial))
                        {
                            string overlayName = "YB_Overlay_" + SafeMaterialName(slot.materialName) + "_" + ShortHash(overlayKey);
                            string path = overlayFolder + "/" + overlayName + ".mat";
                            overlayMaterial = BuildOverlayMaterial(slot.material, resolved.mode, overlay, profile, path);
                            overlayCache[overlayKey] = overlayMaterial;
                            manifestTemp.generatedOverlayMaterialCount++;
                        }

                        slotRecord.dissolveMaterial = dissolve;
                        slotRecord.overlayMaterial = overlayMaterial;
                        record.materialSlots.Add(slotRecord);
                    }

                    if (needsBakedMesh)
                    {
                        string meshName = "YB_Mesh_" + (analyzed.outfitGroup == "Outfit B" ? "B_" : "A_") +
                                          YBPathUtility.SanitizeAssetName(analyzed.rendererName, "Renderer") + "_" + ShortHash(analyzed.hierarchyPath);
                        string meshPath = meshFolder + "/" + meshName + ".asset";
                        record.bakedMesh = YBBakedMeshBuilder.BuildOrUpdate(
                            avatarRoot.transform, analyzed.renderer, analyzed.mesh, meshPath, allowOverwriteUv4,
                            profile.scan.GetMoveDirection());
                        manifestTemp.generatedMeshCount++;
                    }

                    manifestTemp.renderers.Add(record);
                }

                string manifestPath = dataFolder + "/YBGenerationManifest.asset";
                YBGeneratedAssetManifest manifest = SaveOrUpdateManifest(manifestTemp, manifestPath);
                AssetDatabase.SaveAssets();
                WriteGenerationReport(outputRoot, manifest);
                AssetDatabase.Refresh();

                result.success = true;
                result.manifest = manifest;
                result.message = "生成完成：Mesh=" + manifest.generatedMeshCount +
                                 "，Dissolve Material=" + manifest.generatedDissolveMaterialCount +
                                 "，Overlay Material=" + manifest.generatedOverlayMaterialCount +
                                 "，Skip Slot=" + manifest.skippedSlotCount + "。";
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                if (manifestTemp != null) UnityEngine.Object.DestroyImmediate(manifestTemp);
                result.message = "生成失败：" + ex.Message;
                return result;
            }
        }

        public static string ValidateManifest(YBGeneratedAssetManifest manifest)
        {
            if (manifest == null) return "未找到生成清单。";

            if (manifest.profile != null && manifest.profile.scan != null)
            {
                Vector3 currentDirection = manifest.profile.scan.GetMoveDirection();
                if (manifest.bakedScanDataVersion >= 2)
                {
                    Vector3 bakedDirection = manifest.bakedScanMoveDirection.sqrMagnitude > 0.000001f
                        ? manifest.bakedScanMoveDirection.normalized
                        : Vector3.down;
                    if (Vector3.Dot(currentDirection, bakedDirection) < 0.9999f)
                        return "验证失败：Profile 的扫描方向已改变。请重新生成 Baked Mesh，再重新安装。";
                }
                else if (Vector3.Dot(currentDirection, Vector3.down) < 0.9999f)
                {
                    return "验证失败：当前 Manifest 使用旧 BakedAvatarY 数据，只兼容从上到下扫描。请重新生成 0.2.0 Baked Mesh。";
                }
            }

            int missingMesh = 0;
            int missingDissolve = 0;
            int missingOverlay = 0;
            int bakedUv4Missing = 0;

            for (int i = 0; i < manifest.renderers.Count; i++)
            {
                var r = manifest.renderers[i];
                bool hasGeneratedSlot = false;
                for (int s = 0; s < r.materialSlots.Count; s++)
                {
                    var m = r.materialSlots[s];
                    if (m.generationAction == YBUnsupportedMaterialAction.Skip.ToString()) continue;
                    hasGeneratedSlot = true;
                    if (m.dissolveMaterial == null) missingDissolve++;
                    if (m.overlayMaterial == null) missingOverlay++;
                }
                if (hasGeneratedSlot)
                {
                    if (r.bakedMesh == null) missingMesh++;
                    else if (!r.bakedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord3)) bakedUv4Missing++;
                }
            }

            if (missingMesh + missingDissolve + missingOverlay + bakedUv4Missing > 0)
            {
                return "验证失败：Missing Mesh=" + missingMesh +
                       "，Missing Dissolve=" + missingDissolve +
                       "，Missing Overlay=" + missingOverlay +
                       "，Baked UV4 Missing=" + bakedUv4Missing + "。";
            }

            return "验证通过：生成资产引用完整，Baked Mesh 均包含 UV4/TEXCOORD3，扫描方向数据匹配当前 Profile。";
        }

        public static string ValidatePreflight(YBDissolveProfile profile, YBAnalysisReport report, bool allowOverwriteUv4)
        {
            var errors = new List<string>();
            if (profile == null) return "Profile 为空。";
            if (profile.scan.directionPreset == YBScanDirectionPreset.Custom &&
                profile.scan.customDirection.sqrMagnitude < 0.000001f)
                errors.Add("自定义扫描方向不能为 (0,0,0)");
            for (int i = 0; i < report.renderers.Count; i++)
            {
                var r = report.renderers[i];
                if (r.mesh == null) errors.Add(r.hierarchyPath + "：缺少 Mesh");
                else if (!r.meshReadable) errors.Add(r.hierarchyPath + "：Mesh Read/Write 未开启");
                if (r.uv4Occupied && !allowOverwriteUv4) errors.Add(r.hierarchyPath + "：UV4 已占用");

                for (int s = 0; s < r.materials.Count; s++)
                {
                    var m = r.materials[s];
                    if (m.renderMode != YBMaterialRenderMode.Unsupported && m.renderMode != YBMaterialRenderMode.Missing) continue;
                    ResolvedSlot resolved = ResolveSlot(profile, r, m);
                    if (resolved.action == YBUnsupportedMaterialAction.Block)
                        errors.Add(r.hierarchyPath + " / Slot " + m.slotIndex + "：未支持材质仍为‘阻止生成’");
                    if (m.material == null && resolved.action != YBUnsupportedMaterialAction.Skip)
                        errors.Add(r.hierarchyPath + " / Slot " + m.slotIndex + "：材质缺失，只能选择‘跳过’");
                }
            }

            string specialShaderError = ValidateSpecialShaderAvailability(report);
            if (!string.IsNullOrEmpty(specialShaderError)) errors.Add(specialShaderError);

            if (errors.Count == 0) return null;
            var sb = new StringBuilder();
            sb.AppendLine("生成前检查未通过：");
            int show = Math.Min(errors.Count, 12);
            for (int i = 0; i < show; i++) sb.AppendLine("• " + errors[i]);
            if (errors.Count > show) sb.AppendLine("…另有 " + (errors.Count - show) + " 项");
            return sb.ToString();
        }

        public static string GetOverrideKey(YBRendererAnalysis renderer, YBMaterialSlotAnalysis slot)
        {
            if (!string.IsNullOrEmpty(slot.assetGuid)) return "GUID:" + slot.assetGuid;
            return "SLOT:" + renderer.hierarchyPath + "#" + slot.slotIndex;
        }

        private sealed class ResolvedSlot
        {
            public YBMaterialRenderMode mode;
            public YBUnsupportedMaterialAction action;
            public string note;
            public bool automatic;
        }

        private static ResolvedSlot ResolveSlot(YBDissolveProfile profile, YBRendererAnalysis renderer, YBMaterialSlotAnalysis slot)
        {
            if (slot.renderMode != YBMaterialRenderMode.Unsupported && slot.renderMode != YBMaterialRenderMode.Missing)
            {
                YBUnsupportedMaterialAction autoAction = ActionForMode(slot.renderMode);
                return new ResolvedSlot
                {
                    mode = slot.renderMode,
                    action = autoAction,
                    note = "自动：" + slot.note,
                    automatic = true
                };
            }

            YBUnsupportedMaterialAction action = YBUnsupportedMaterialAction.Block;
            string key = GetOverrideKey(renderer, slot);
            if (profile.unsupportedMaterialOverrides != null)
            {
                for (int i = 0; i < profile.unsupportedMaterialOverrides.Count; i++)
                {
                    var item = profile.unsupportedMaterialOverrides[i];
                    if (item != null && item.key == key)
                    {
                        action = item.action;
                        break;
                    }
                }
            }

            YBMaterialRenderMode mode = YBMaterialRenderMode.Unsupported;
            if (action == YBUnsupportedMaterialAction.ForceOpaque) mode = YBMaterialRenderMode.Opaque;
            if (action == YBUnsupportedMaterialAction.ForceCutout) mode = YBMaterialRenderMode.Cutout;
            if (action == YBUnsupportedMaterialAction.ForceTransparent) mode = YBMaterialRenderMode.Transparent;
            if (action == YBUnsupportedMaterialAction.ForceRefraction) mode = YBMaterialRenderMode.Refraction;
            if (action == YBUnsupportedMaterialAction.ForceRefractionBlur) mode = YBMaterialRenderMode.RefractionBlur;
            if (action == YBUnsupportedMaterialAction.ForceFur) mode = YBMaterialRenderMode.Fur;
            if (action == YBUnsupportedMaterialAction.ForceFurCutout) mode = YBMaterialRenderMode.FurCutout;
            if (action == YBUnsupportedMaterialAction.ForceFurTwoPass) mode = YBMaterialRenderMode.FurTwoPass;
            if (action == YBUnsupportedMaterialAction.ForceGem) mode = YBMaterialRenderMode.Gem;

            return new ResolvedSlot { mode = mode, action = action, note = slot.note };
        }

        private static int GetInitialRole(YBDissolveProfile profile, string outfitGroup)
        {
            bool isB = outfitGroup == "Outfit B";
            bool defaultB = profile.defaultOutfit == YBOutfitDefault.OutfitB;
            bool visibleByDefault = isB == defaultB;
            return visibleByDefault ? 0 : 1;
        }

        private static YBUnsupportedMaterialAction ActionForMode(YBMaterialRenderMode mode)
        {
            switch (mode)
            {
                case YBMaterialRenderMode.Cutout: return YBUnsupportedMaterialAction.ForceCutout;
                case YBMaterialRenderMode.Transparent: return YBUnsupportedMaterialAction.ForceTransparent;
                case YBMaterialRenderMode.Refraction: return YBUnsupportedMaterialAction.ForceRefraction;
                case YBMaterialRenderMode.RefractionBlur: return YBUnsupportedMaterialAction.ForceRefractionBlur;
                case YBMaterialRenderMode.Fur: return YBUnsupportedMaterialAction.ForceFur;
                case YBMaterialRenderMode.FurCutout: return YBUnsupportedMaterialAction.ForceFurCutout;
                case YBMaterialRenderMode.FurTwoPass: return YBUnsupportedMaterialAction.ForceFurTwoPass;
                case YBMaterialRenderMode.Gem: return YBUnsupportedMaterialAction.ForceGem;
                default: return YBUnsupportedMaterialAction.ForceOpaque;
            }
        }

        private static Shader GetShader(
            YBMaterialRenderMode mode, Shader opaque, Shader cutout, Shader transparent,
            Shader refraction, Shader refractionBlur, Shader fur, Shader furCutout, Shader furTwoPass, Shader gem)
        {
            Shader shader;
            switch (mode)
            {
                case YBMaterialRenderMode.Cutout: shader = cutout; break;
                case YBMaterialRenderMode.Transparent: shader = transparent; break;
                case YBMaterialRenderMode.Refraction: shader = refraction; break;
                case YBMaterialRenderMode.RefractionBlur: shader = refractionBlur; break;
                case YBMaterialRenderMode.Fur: shader = fur; break;
                case YBMaterialRenderMode.FurCutout: shader = furCutout; break;
                case YBMaterialRenderMode.FurTwoPass: shader = furTwoPass; break;
                case YBMaterialRenderMode.Gem: shader = gem; break;
                default: shader = opaque; break;
            }
            if (shader == null) throw new InvalidOperationException("对应特殊 Shader 尚未由 lilToon 生成/加载：" + mode);
            return shader;
        }

        internal static string ValidateSpecialShaderAvailability(YBAnalysisReport report)
        {
            if (report == null) return "尚未分析 Avatar。";
            var required = new HashSet<YBMaterialRenderMode>();
            for (int i = 0; i < report.renderers.Count; i++)
                for (int s = 0; s < report.renderers[i].materials.Count; s++)
                {
                    var m = report.renderers[i].materials[s].renderMode;
                    if (m == YBMaterialRenderMode.Refraction || m == YBMaterialRenderMode.RefractionBlur ||
                        m == YBMaterialRenderMode.Fur || m == YBMaterialRenderMode.FurCutout ||
                        m == YBMaterialRenderMode.FurTwoPass || m == YBMaterialRenderMode.Gem) required.Add(m);
                }
            var missing = new List<string>();
            foreach (var mode in required)
            {
                string name = ShaderNameForMode(mode);
                Shader shader = Shader.Find(name);
                if (shader == null)
                    missing.Add(mode + " → " + name + "（未生成/未加载）");
                else if (ShaderUtil.ShaderHasError(shader))
                    missing.Add(mode + " → " + name + "（Shader 编译错误，请先修复 Console 错误）");
            }
            if (missing.Count == 0) return null;
            return "特殊 lilToon Shader 尚未就绪：\n• " + string.Join("\n• ", missing.ToArray()) +
                   "\n请先查看 Console；若只是容器未刷新，可对 _YBTools/Shader/FinalBaked/Special 执行 Reimport。";
        }

        internal static string ShaderNameForMode(YBMaterialRenderMode mode)
        {
            switch (mode)
            {
                case YBMaterialRenderMode.Refraction: return RefractionShader;
                case YBMaterialRenderMode.RefractionBlur: return RefractionBlurShader;
                case YBMaterialRenderMode.Fur: return FurShader;
                case YBMaterialRenderMode.FurCutout: return FurCutoutShader;
                case YBMaterialRenderMode.FurTwoPass: return FurTwoPassShader;
                case YBMaterialRenderMode.Gem: return GemShader;
                case YBMaterialRenderMode.Cutout: return CutoutShader;
                case YBMaterialRenderMode.Transparent: return TransparentShader;
                default: return OpaqueShader;
            }
        }

        private static Material BuildDissolveMaterial(
            Material source,
            Shader targetShader,
            YBDissolveProfile profile,
            int role,
            string assetPath)
        {
            Material temp = new Material(source);
            temp.name = Path.GetFileNameWithoutExtension(assetPath);
            int sourceQueue = source.renderQueue;
            temp.shader = targetShader;
            if (sourceQueue >= 0) temp.renderQueue = sourceQueue;
            SetFloat(temp, "_YBScanDissolveEnabled", 1f);
            SetFloat(temp, "_YBScanHeight", profile.scan.startHeight);
            SetFloat(temp, "_YBScanRole", role);
            SetFloat(temp, "_YBScanNoiseStrength", profile.scan.noiseStrength);
            SetVector(temp, "_YBScanRootOffset", Vector4.zero);
            return SaveOrUpdateMaterial(temp, assetPath);
        }

        private static Material BuildOverlayMaterial(
            Material source,
            YBMaterialRenderMode mode,
            Shader overlayShader,
            YBDissolveProfile profile,
            string assetPath)
        {
            Material temp = new Material(overlayShader);
            temp.name = Path.GetFileNameWithoutExtension(assetPath);

            if (source.HasProperty("_MainTex") && temp.HasProperty("_MainTex"))
            {
                temp.SetTexture("_MainTex", source.GetTexture("_MainTex"));
                temp.SetTextureScale("_MainTex", source.GetTextureScale("_MainTex"));
                temp.SetTextureOffset("_MainTex", source.GetTextureOffset("_MainTex"));
            }
            if (source.HasProperty("_Color") && temp.HasProperty("_Color")) temp.SetColor("_Color", source.GetColor("_Color"));
            if (source.HasProperty("_Cutoff") && temp.HasProperty("_Cutoff")) temp.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));

            SetColor(temp, "_YBScanColor", profile.scan.scanColor);
            SetFloat(temp, "_YBScanIntensity", 0f);
            SetFloat(temp, "_YBScanHeight", profile.scan.startHeight);
            SetFloat(temp, "_YBScanWidth", profile.scan.scanWidth);
            SetFloat(temp, "_YBScanSoftness", profile.scan.scanSoftness);
            bool alphaMode = mode == YBMaterialRenderMode.Transparent || mode == YBMaterialRenderMode.Refraction ||
                             mode == YBMaterialRenderMode.RefractionBlur || mode == YBMaterialRenderMode.Fur ||
                             mode == YBMaterialRenderMode.FurTwoPass || mode == YBMaterialRenderMode.Gem;
            bool cutoffMode = mode == YBMaterialRenderMode.Cutout || mode == YBMaterialRenderMode.FurCutout;
            SetFloat(temp, "_YBScanUseAlpha", alphaMode ? 1f : 0f);
            SetFloat(temp, "_YBScanUseCutoff", cutoffMode ? 1f : 0f);
            SetVector(temp, "_YBScanRootOffset", Vector4.zero);
            return SaveOrUpdateMaterial(temp, assetPath);
        }

        private static Material SaveOrUpdateMaterial(Material temporary, string assetPath)
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(temporary, existing);
                existing.name = temporary.name;
                UnityEngine.Object.DestroyImmediate(temporary);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            UnityEngine.Object occupied = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (occupied != null) AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(temporary, assetPath);
            return temporary;
        }

        private static YBGeneratedAssetManifest SaveOrUpdateManifest(YBGeneratedAssetManifest temporary, string assetPath)
        {
            YBGeneratedAssetManifest existing = AssetDatabase.LoadAssetAtPath<YBGeneratedAssetManifest>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(temporary, existing);
                existing.name = temporary.name;
                UnityEngine.Object.DestroyImmediate(temporary);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            UnityEngine.Object occupied = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (occupied != null) AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(temporary, assetPath);
            return temporary;
        }

        private static void WriteGenerationReport(string outputRoot, YBGeneratedAssetManifest manifest)
        {
            string assetPath = outputRoot + "/Data/GenerationReport.txt";
            string absolute = Path.GetFullPath(assetPath);
            var sb = new StringBuilder();
            sb.AppendLine("YB Dissolve Avatar Builder Generation Report");
            sb.AppendLine("Tool Version: " + manifest.toolVersion);
            sb.AppendLine("Generated UTC: " + manifest.generatedUtc);
            sb.AppendLine("Avatar: " + manifest.avatarHint);
            sb.AppendLine("Baked Scan Data Version: " + manifest.bakedScanDataVersion);
            sb.AppendLine("Scan Move Direction: " + manifest.bakedScanMoveDirection);
            sb.AppendLine("Source Renderers: " + manifest.sourceRendererCount);
            sb.AppendLine("Baked Meshes: " + manifest.generatedMeshCount);
            sb.AppendLine("Dissolve Materials: " + manifest.generatedDissolveMaterialCount);
            sb.AppendLine("Overlay Materials: " + manifest.generatedOverlayMaterialCount);
            sb.AppendLine("Skipped Slots: " + manifest.skippedSlotCount);
            sb.AppendLine();
            for (int i = 0; i < manifest.renderers.Count; i++)
            {
                var r = manifest.renderers[i];
                sb.AppendLine("[" + r.outfitGroup + "] " + r.editorPath);
                sb.AppendLine("  SourceMesh=" + (r.sourceMesh != null ? r.sourceMesh.name : "(none)"));
                sb.AppendLine("  BakedMesh=" + (r.bakedMesh != null ? AssetDatabase.GetAssetPath(r.bakedMesh) : "(not generated)"));
                for (int s = 0; s < r.materialSlots.Count; s++)
                {
                    var m = r.materialSlots[s];
                    sb.AppendLine("  Slot " + m.slotIndex + " Mode=" + m.resolvedMode + " Action=" + m.generationAction +
                                  " Dissolve=" + AssetPathOrNone(m.dissolveMaterial) + " Overlay=" + AssetPathOrNone(m.overlayMaterial));
                }
            }
            File.WriteAllText(absolute, sb.ToString(), Encoding.UTF8);
            AssetDatabase.ImportAsset(assetPath);
        }

        private static string AssetPathOrNone(UnityEngine.Object obj)
        {
            return obj != null ? AssetDatabase.GetAssetPath(obj) : "(none)";
        }

        private static string GetSourceMaterialKey(Material material, string guid, string rendererPath, int slot)
        {
            if (!string.IsNullOrEmpty(guid)) return "GUID:" + guid;
            if (material != null) return "MAT:" + material.name + "|" + material.GetInstanceID();
            return "MISSING:" + rendererPath + "#" + slot;
        }

        private static string SafeMaterialName(string name)
        {
            return YBPathUtility.SanitizeAssetName(name, "Material");
        }

        private static string ShortHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619;
                }
                return hash.ToString("X8");
            }
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material != null && material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static void SetVector(Material material, string property, Vector4 value)
        {
            if (material != null && material.HasProperty(property)) material.SetVector(property, value);
        }

        private static void SetColor(Material material, string property, Color value)
        {
            if (material != null && material.HasProperty(property)) material.SetColor(property, value);
        }
    }
}
