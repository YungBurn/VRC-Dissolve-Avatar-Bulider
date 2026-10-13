using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace YBTools.Dissolve.Editor
{
    internal static class YBAvatarAnalyzer
    {
        private static readonly string[] GeneratedNamePrefixes =
        {
            "__YBFinalOverlay__",
            "_YBFinalOverlay_",
            "__YB_MaterialVariantPreview__",
            "__YBScanPreview__"
        };

        public static YBAnalysisReport Analyze(
            GameObject avatarRoot,
            IList<GameObject> outfitARoots,
            IList<GameObject> outfitBRoots)
        {
            var report = new YBAnalysisReport();
            if (avatarRoot == null)
            {
                Add(report, YBAnalysisSeverity.Blocking, "Avatar Root 未设置", "请先指定 Avatar 根对象。");
                return report;
            }

            var seen = new HashSet<int>();
            CollectGroup(report, avatarRoot.transform, outfitARoots, "Outfit A", seen);
            CollectGroup(report, avatarRoot.transform, outfitBRoots, "Outfit B", seen);

            if (report.renderers.Count == 0)
            {
                Add(report, YBAnalysisSeverity.Blocking, "未找到 Renderer", "Outfit A / B Root 下没有可分析的 Renderer。");
                return report;
            }

            MarkAmbiguousFingerprints(report);
            AddSummaryMessages(report);
            return report;
        }

        private static void CollectGroup(
            YBAnalysisReport report,
            Transform avatarRoot,
            IList<GameObject> roots,
            string groupName,
            HashSet<int> seen)
        {
            if (roots == null) return;

            for (int i = 0; i < roots.Count; i++)
            {
                var root = roots[i];
                if (root == null) continue;

                if (!root.transform.IsChildOf(avatarRoot) && root.transform != avatarRoot)
                {
                    Add(report, YBAnalysisSeverity.Blocking,
                        groupName + " Root 不属于 Avatar",
                        root.name + " 不在 " + avatarRoot.name + " 层级内。");
                    continue;
                }

                var renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    var renderer = renderers[r];
                    if (renderer == null || ShouldIgnore(renderer.gameObject.name)) continue;
                    if (!seen.Add(renderer.GetInstanceID())) continue;

                    report.renderers.Add(AnalyzeRenderer(avatarRoot, renderer, groupName));
                }
            }
        }

        private static bool ShouldIgnore(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < GeneratedNamePrefixes.Length; i++)
                if (name.StartsWith(GeneratedNamePrefixes[i], StringComparison.Ordinal)) return true;
            return false;
        }

        private static YBRendererAnalysis AnalyzeRenderer(Transform avatarRoot, Renderer renderer, string groupName)
        {
            var result = new YBRendererAnalysis
            {
                outfitGroup = groupName,
                renderer = renderer,
                hierarchyPath = YBPathUtility.GetRelativePath(avatarRoot, renderer.transform),
                rendererName = renderer.name,
                rendererType = renderer.GetType().Name,
                mesh = GetMesh(renderer),
                maPathRisk = HasModularAvatarPathRisk(renderer.transform, avatarRoot)
            };

            result.meshName = result.mesh != null ? result.mesh.name : "(none)";
            result.meshReadable = result.mesh != null && result.mesh.isReadable;
            result.uv4Occupied = result.mesh != null && result.mesh.HasVertexAttribute(VertexAttribute.TexCoord3);

            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                result.materials.Add(AnalyzeMaterial(i, materials[i]));

            return result;
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            var smr = renderer as SkinnedMeshRenderer;
            if (smr != null) return smr.sharedMesh;

            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private static YBMaterialSlotAnalysis AnalyzeMaterial(int slot, Material material)
        {
            string assetPath = material != null ? AssetDatabase.GetAssetPath(material) : string.Empty;
            var result = new YBMaterialSlotAnalysis
            {
                slotIndex = slot,
                material = material,
                materialName = material != null ? material.name : "(missing)",
                shaderName = material != null && material.shader != null ? material.shader.name : "(missing)",
                assetPath = assetPath,
                assetGuid = string.IsNullOrEmpty(assetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(assetPath),
                renderMode = YBMaterialRenderMode.Missing
            };

            if (material == null || material.shader == null)
            {
                result.note = "Material 或 Shader 缺失";
                return result;
            }

            string shaderName = material.shader.name ?? string.Empty;

            // YB FinalBaked：Shader 名本身就是最终渲染模式，优先直接识别。
            if (Contains(shaderName, "FinalBaked/Hair Dissolve Refraction Blur"))
            {
                result.renderMode = YBMaterialRenderMode.RefractionBlur;
                result.note = "YB FinalBaked Refraction Blur";
                return result;
            }
            if (Contains(shaderName, "FinalBaked/Hair Dissolve Refraction"))
            {
                result.renderMode = YBMaterialRenderMode.Refraction;
                result.note = "YB FinalBaked Refraction";
                return result;
            }
            if (Contains(shaderName, "FinalBaked/Hair Dissolve Fur TwoPass"))
            {
                result.renderMode = YBMaterialRenderMode.FurTwoPass;
                result.note = "YB FinalBaked Fur TwoPass";
                return result;
            }
            if (Contains(shaderName, "FinalBaked/Hair Dissolve Fur Cutout"))
            {
                result.renderMode = YBMaterialRenderMode.FurCutout;
                result.note = "YB FinalBaked Fur Cutout";
                return result;
            }
            if (Contains(shaderName, "FinalBaked/Hair Dissolve Fur"))
            {
                result.renderMode = YBMaterialRenderMode.Fur;
                result.note = "YB FinalBaked Fur";
                return result;
            }
            if (Contains(shaderName, "FinalBaked/Hair Dissolve Gem"))
            {
                result.renderMode = YBMaterialRenderMode.Gem;
                result.note = "YB FinalBaked Gem";
                return result;
            }
            if (Contains(shaderName, "FinalBaked/Hair Dissolve Transparent"))
            {
                result.renderMode = YBMaterialRenderMode.Transparent;
                result.note = "YB FinalBaked Transparent";
                return result;
            }
            if (Contains(shaderName, "FinalBaked/Hair Dissolve Cutout"))
            {
                result.renderMode = YBMaterialRenderMode.Cutout;
                result.note = "YB FinalBaked Cutout";
                return result;
            }
            if (Contains(shaderName, "FinalBaked/Hair Dissolve"))
            {
                result.renderMode = YBMaterialRenderMode.Opaque;
                result.note = "YB FinalBaked Opaque";
                return result;
            }

            bool looksLikeLilToon =
                Contains(shaderName, "lilToon") ||
                material.HasProperty("_lilToonVersion") ||
                material.HasProperty("_TransparentMode");

            if (!looksLikeLilToon)
            {
                result.renderMode = YBMaterialRenderMode.Unsupported;
                result.note = "非 lilToon / 未识别 Shader";
                return result;
            }

            // lilToon 本身也会根据 Shader 名区分 Refraction/Fur/Gem 及其子模式，
            // 因此这里必须先于普通 Cutout/Transparent 与 _TransparentMode 判断。
            if (Contains(shaderName, "RefractionBlur"))
            {
                result.renderMode = YBMaterialRenderMode.RefractionBlur;
                result.note = "lilToon Refraction Blur（由 Shader Variant 识别）";
                return result;
            }
            if (Contains(shaderName, "Refraction"))
            {
                result.renderMode = YBMaterialRenderMode.Refraction;
                result.note = "lilToon Refraction（由 Shader Variant 识别）";
                return result;
            }
            if (Contains(shaderName, "FurTwoPass") || (Contains(shaderName, "Fur") && Contains(shaderName, "TwoPass")))
            {
                result.renderMode = YBMaterialRenderMode.FurTwoPass;
                result.note = "lilToon Fur Two Pass（由 Shader Variant 识别）";
                return result;
            }
            if (Contains(shaderName, "FurCutout") || (Contains(shaderName, "Fur") && Contains(shaderName, "Cutout")))
            {
                result.renderMode = YBMaterialRenderMode.FurCutout;
                result.note = "lilToon Fur Cutout（由 Shader Variant 识别）";
                return result;
            }
            if (Contains(shaderName, "Fur"))
            {
                result.renderMode = YBMaterialRenderMode.Fur;
                result.note = "lilToon Fur（由 Shader Variant 识别）";
                return result;
            }
            if (Contains(shaderName, "Gem"))
            {
                result.renderMode = YBMaterialRenderMode.Gem;
                result.note = "lilToon Gem（由 Shader Variant 识别）";
                return result;
            }

            // FakeShadow/Tessellation 等尚未纳入本轮特殊 Shader。
            string unsupportedReason;
            if (TryGetUnsupportedLilToonVariant(shaderName, out unsupportedReason))
            {
                result.renderMode = YBMaterialRenderMode.Unsupported;
                result.note = unsupportedReason;
                return result;
            }

            // Hidden/lilToonTwoPassTransparent、Hidden/lilToonTransparentOutline、
            // Hidden/lilToonCutout 等内部 Shader 的 _TransparentMode 在部分版本中并不能
            // 代表最终渲染模式，因此明确 Shader 变体必须优先于属性值。
            if (Contains(shaderName, "Cutout"))
            {
                result.renderMode = YBMaterialRenderMode.Cutout;
                result.note = "lilToon Cutout（由 Shader Variant 识别）";
                return result;
            }

            if (Contains(shaderName, "Transparent"))
            {
                result.renderMode = YBMaterialRenderMode.Transparent;
                result.note = "lilToon Transparent（由 Shader Variant 识别）";
                return result;
            }

            // 普通 lilToon / lilToonMulti / Hidden/lilToonOutline 等，再读取材质实际模式。
            if (material.HasProperty("_TransparentMode"))
            {
                int mode = Mathf.RoundToInt(material.GetFloat("_TransparentMode"));
                switch (mode)
                {
                    case 0:
                        result.renderMode = YBMaterialRenderMode.Opaque;
                        result.note = "lilToon Opaque（_TransparentMode=0）";
                        return result;
                    case 1:
                        result.renderMode = YBMaterialRenderMode.Cutout;
                        result.note = "lilToon Cutout（_TransparentMode=1）";
                        return result;
                    case 2:
                        result.renderMode = YBMaterialRenderMode.Transparent;
                        result.note = "lilToon Transparent（_TransparentMode=2）";
                        return result;
                    case 3:
                        result.renderMode = YBMaterialRenderMode.Refraction;
                        result.note = "lilToon Refraction（_TransparentMode=3）";
                        return result;
                    case 4:
                        result.renderMode = YBMaterialRenderMode.Fur;
                        result.note = "lilToon Fur（_TransparentMode=4）";
                        return result;
                    case 5:
                        result.renderMode = YBMaterialRenderMode.FurCutout;
                        result.note = "lilToon Fur Cutout（_TransparentMode=5）";
                        return result;
                    case 6:
                        result.renderMode = YBMaterialRenderMode.Gem;
                        result.note = "lilToon Gem（_TransparentMode=6）";
                        return result;
                    default:
                        result.renderMode = YBMaterialRenderMode.Unsupported;
                        result.note = "lilToon _TransparentMode=" + mode + "，当前无法可靠映射";
                        return result;
                }
            }

            // 某些派生/旧版本 lilToon Shader 不暴露 _TransparentMode。
            // 这时只接受明确的 RenderType Tag，避免“识别失败 = Opaque”的危险回退。
            string renderType = material.GetTag("RenderType", false, string.Empty) ?? string.Empty;
            if (string.Equals(renderType, "TransparentCutout", StringComparison.OrdinalIgnoreCase))
            {
                result.renderMode = YBMaterialRenderMode.Cutout;
                result.note = "lilToon Cutout（由 RenderType=TransparentCutout 推断）";
                return result;
            }
            if (string.Equals(renderType, "Transparent", StringComparison.OrdinalIgnoreCase))
            {
                result.renderMode = YBMaterialRenderMode.Transparent;
                result.note = "lilToon Transparent（由 RenderType=Transparent 推断）";
                return result;
            }
            if (string.Equals(renderType, "Opaque", StringComparison.OrdinalIgnoreCase))
            {
                result.renderMode = YBMaterialRenderMode.Opaque;
                result.note = "lilToon Opaque（由 RenderType=Opaque 推断）";
                return result;
            }

            // Render Queue 只用于识别非常明确的透明队列；不使用它猜测 Opaque，
            // 因为很多材质会人为调整 Queue Offset。
            if (material.renderQueue >= (int)RenderQueue.Transparent)
            {
                result.renderMode = YBMaterialRenderMode.Transparent;
                result.note = "lilToon Transparent（缺少模式属性，由 RenderQueue>=3000 推断）";
                return result;
            }

            result.renderMode = YBMaterialRenderMode.Unsupported;
            result.note = "lilToon 派生 Shader，但无法可靠判断 Opaque/Cutout/Transparent；请手动检查";
            return result;
        }

        private static bool TryGetUnsupportedLilToonVariant(string shaderName, out string reason)
        {
            if (Contains(shaderName, "FakeShadow"))
            {
                reason = "lilToon FakeShadow：当前未提供对应 FinalBaked Shader";
                return true;
            }
            if (Contains(shaderName, "Tessellation"))
            {
                reason = "lilToon Tessellation：当前未保留 Tessellation 路径";
                return true;
            }

            reason = null;
            return false;
        }

        private static bool Contains(string value, string token)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasModularAvatarPathRisk(Transform target, Transform avatarRoot)
        {
            var current = target;
            while (current != null)
            {
                var components = current.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    var c = components[i];
                    if (c == null) continue;
                    string fullName = c.GetType().FullName ?? string.Empty;
                    if (fullName.IndexOf("ModularAvatarBoneProxy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fullName.IndexOf("ModularAvatarMergeArmature", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fullName.IndexOf("ModularAvatarArmature", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }

                if (current == avatarRoot) break;
                current = current.parent;
            }
            return false;
        }

        private static void MarkAmbiguousFingerprints(YBAnalysisReport report)
        {
            var groups = report.renderers.GroupBy(BuildFingerprint).Where(g => g.Count() > 1);
            foreach (var group in groups)
            {
                foreach (var renderer in group) renderer.ambiguousFingerprint = true;
                Add(report, YBAnalysisSeverity.Warning,
                    "Renderer 指纹存在重复",
                    "存在 " + group.Count() + " 个 Renderer 共享相同 Name/Type/Mesh/SlotCount：" + group.Key +
                    "。后续 Locator 需要 Material Signature 或手动 Override。");
            }
        }

        private static string BuildFingerprint(YBRendererAnalysis r)
        {
            return r.rendererName + " | " + r.rendererType + " | " + r.meshName + " | slots=" + r.materials.Count;
        }

        private static void AddSummaryMessages(YBAnalysisReport report)
        {
            int missingMesh = report.renderers.Count(r => r.mesh == null);
            int unreadable = report.renderers.Count(r => r.mesh != null && !r.meshReadable);
            int uv4 = report.renderers.Count(r => r.uv4Occupied);
            int maRisk = report.renderers.Count(r => r.maPathRisk);
            int unsupported = report.renderers.Sum(r => r.materials.Count(m =>
                m.renderMode == YBMaterialRenderMode.Unsupported || m.renderMode == YBMaterialRenderMode.Missing));
            int transparent = report.renderers.Sum(r => r.materials.Count(m => m.renderMode == YBMaterialRenderMode.Transparent));
            int cutout = report.renderers.Sum(r => r.materials.Count(m => m.renderMode == YBMaterialRenderMode.Cutout));
            int opaque = report.renderers.Sum(r => r.materials.Count(m => m.renderMode == YBMaterialRenderMode.Opaque));
            int refraction = report.renderers.Sum(r => r.materials.Count(m => m.renderMode == YBMaterialRenderMode.Refraction));
            int refractionBlur = report.renderers.Sum(r => r.materials.Count(m => m.renderMode == YBMaterialRenderMode.RefractionBlur));
            int fur = report.renderers.Sum(r => r.materials.Count(m => m.renderMode == YBMaterialRenderMode.Fur));
            int furCutout = report.renderers.Sum(r => r.materials.Count(m => m.renderMode == YBMaterialRenderMode.FurCutout));
            int furTwoPass = report.renderers.Sum(r => r.materials.Count(m => m.renderMode == YBMaterialRenderMode.FurTwoPass));
            int gem = report.renderers.Sum(r => r.materials.Count(m => m.renderMode == YBMaterialRenderMode.Gem));

            if (missingMesh > 0)
                Add(report, YBAnalysisSeverity.Blocking, "Renderer 缺少 Mesh", missingMesh + " 个 Renderer 没有可用 Mesh。");

            if (unreadable > 0)
                Add(report, YBAnalysisSeverity.Blocking, "Mesh Read/Write 未开启", unreadable + " 个 Mesh 当前不可读，后续无法烘焙 UV4。");

            if (uv4 > 0)
                Add(report, YBAnalysisSeverity.Warning, "UV4 已被占用", uv4 + " 个 Mesh 已存在 UV4/TEXCOORD3。正式生成前不能默认覆盖。");

            if (unsupported > 0)
                Add(report, YBAnalysisSeverity.Blocking, "存在未支持材质", unsupported + " 个 Material Slot 不属于当前自动支持模式，或材质缺失。");

            if (maRisk > 0)
                Add(report, YBAnalysisSeverity.Warning, "检测到 MA 路径变化风险", maRisk + " 个 Renderer 位于 Bone Proxy / Merge Armature 相关层级。后续将启用 Fingerprint + Play Mode 重定位。");

            Add(report, YBAnalysisSeverity.Passed, "材质分类完成",
                "Opaque=" + opaque + "，Cutout=" + cutout + "，Transparent=" + transparent +
                "，Refraction=" + refraction + "，RefractionBlur=" + refractionBlur +
                "，Fur=" + fur + "，FurCutout=" + furCutout + "，FurTwoPass=" + furTwoPass +
                "，Gem=" + gem + "，Unsupported/Missing=" + unsupported + "。");
            Add(report, YBAnalysisSeverity.Passed, "分析统计",
                "Renderer=" + report.RendererCount + "，Material Slots=" + report.MaterialSlotCount + "。");
        }

        private static void Add(YBAnalysisReport report, YBAnalysisSeverity severity, string title, string detail)
        {
            report.messages.Add(new YBAnalysisMessage { severity = severity, title = title, detail = detail });
        }
    }
}
