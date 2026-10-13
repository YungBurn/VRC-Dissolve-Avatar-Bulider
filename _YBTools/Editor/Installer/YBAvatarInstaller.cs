using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace YBTools.Dissolve.Editor
{
    internal sealed class YBInstallOperationResult
    {
        public bool success;
        public string message;
        public YBAvatarInstallRecord record;
    }

    internal static class YBAvatarInstaller
    {
        internal const string OverlayPrefix = "__YBFinalOverlay__";
        private const int BakedUvChannel = 3;

        private sealed class InstallPlanEntry
        {
            public YBGeneratedRendererRecord manifestRecord;
            public Renderer renderer;
            public Mesh bakedMesh;
            public Material[] sourceMaterials;
            public Material[] installedMaterials;
            public List<int> overlaySourceSlots = new List<int>();
            public List<Material> overlayMaterials = new List<Material>();
            public string locateReason;
        }

        internal static string GetInstallRecordPath(string outputRoot)
        {
            return outputRoot.TrimEnd('/') + "/Data/YBInstallRecord.asset";
        }

        internal static YBAvatarInstallRecord LoadInstallRecord(string outputRoot)
        {
            return AssetDatabase.LoadAssetAtPath<YBAvatarInstallRecord>(GetInstallRecordPath(outputRoot));
        }

        internal static string ValidatePreflight(GameObject avatarRoot, YBDissolveProfile profile, YBGeneratedAssetManifest manifest, string outputRoot)
        {
            var errors = new List<string>();
            if (EditorApplication.isPlayingOrWillChangePlaymode) errors.Add("请先退出 Play Mode。");
            if (avatarRoot == null) errors.Add("Avatar Root 为空。");
            if (profile == null) errors.Add("当前 Profile 为空。");
            if (manifest == null) errors.Add("Generation Manifest 为空，请先生成或加载。");
            if (!string.IsNullOrEmpty(outputRoot) && LoadInstallRecord(outputRoot) != null)
                errors.Add("当前 Profile 已存在 YBInstallRecord。请先验证当前安装或卸载后再安装。");

            if (errors.Count == 0)
            {
                try { BuildPlan(avatarRoot, profile, manifest, true); }
                catch (Exception ex) { errors.Add(ex.Message); }
            }

            if (errors.Count == 0) return null;
            var sb = new StringBuilder();
            sb.AppendLine("安装前检查未通过：");
            int show = Mathf.Min(errors.Count, 12);
            for (int i = 0; i < show; i++) sb.AppendLine("• " + errors[i]);
            if (errors.Count > show) sb.AppendLine("…另有 " + (errors.Count - show) + " 项");
            return sb.ToString();
        }

        internal static YBInstallOperationResult Install(
            GameObject avatarRoot,
            YBDissolveProfile profile,
            YBGeneratedAssetManifest manifest,
            string outputRoot)
        {
            var result = new YBInstallOperationResult();
            string recordPath = GetInstallRecordPath(outputRoot);
            string overlayMeshFolder = outputRoot.TrimEnd('/') + "/Backup/InstallOverlayMesh";
            int undoGroup = -1;
            bool sceneTouched = false;
            try
            {
                string preflight = ValidatePreflight(avatarRoot, profile, manifest, outputRoot);
                if (!string.IsNullOrEmpty(preflight)) throw new InvalidOperationException(preflight);

                var plan = BuildPlan(avatarRoot, profile, manifest, false);
                if (avatarRoot.GetComponentsInChildren<Transform>(true)
                    .Any(t => t != null && t.name.StartsWith(OverlayPrefix, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Avatar 中已经存在 " + OverlayPrefix + " 对象。请先确认是否已有旧安装残留。");

                if (AssetDatabase.IsValidFolder(overlayMeshFolder)) AssetDatabase.DeleteAsset(overlayMeshFolder);
                EnsureAssetFolder(overlayMeshFolder);

                var overlayMeshes = new Dictionary<InstallPlanEntry, Mesh>();
                foreach (var entry in plan)
                {
                    if (entry.overlaySourceSlots.Count == 0) continue;
                    overlayMeshes[entry] = BuildOverlayMeshAsset(entry, overlayMeshFolder);
                }
                AssetDatabase.SaveAssets();

                var record = ScriptableObject.CreateInstance<YBAvatarInstallRecord>();
                record.name = "YBInstallRecord";
                record.toolVersion = YBToolVersion.Version;
                record.installedUtc = DateTime.UtcNow.ToString("o");
                record.avatarHint = avatarRoot.name;
                record.profile = profile;
                record.generationManifest = manifest;

                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("YB Dissolve Avatar Install");

                CaptureAndActivateRequired(avatarRoot, plan.Select(p => p.renderer), record);
                sceneTouched = true;

                foreach (var entry in plan)
                {
                    Renderer r = entry.renderer;
                    var rr = new YBInstalledRendererRecord
                    {
                        manifestEditorPath = entry.manifestRecord.editorPath,
                        installedRendererPath = YBPathUtility.GetRelativePath(avatarRoot.transform, r.transform),
                        rendererName = r.name,
                        rendererType = r.GetType().Name,
                        originalEnabled = r.enabled,
                        originalMesh = GetRendererMesh(r),
                        installedBakedMesh = entry.bakedMesh
                    };
                    rr.originalMaterials.AddRange(entry.sourceMaterials);
                    rr.installedMaterials.AddRange(entry.installedMaterials);

                    Undo.RecordObject(r, "YB Install Renderer");
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null) Undo.RecordObject(mf, "YB Install Mesh");
                    AssignMesh(r, entry.bakedMesh);
                    r.sharedMaterials = entry.installedMaterials;
                    r.enabled = true;
                    RecordPrefab(r);
                    if (mf != null) RecordPrefab(mf);

                    if (entry.overlaySourceSlots.Count > 0)
                    {
                        Mesh overlayMesh = overlayMeshes[entry];
                        string overlayName = OverlayPrefix + SafeName(r.name) + "_" + Hash8(entry.manifestRecord.editorPath);
                        Renderer overlay = CreateOverlayRenderer(r, overlayMesh, entry.overlayMaterials.ToArray(), overlayName);
                        rr.overlayObjectName = overlayName;
                        rr.overlayObjectPath = YBPathUtility.GetRelativePath(avatarRoot.transform, overlay.transform);
                        rr.overlayMesh = overlayMesh;
                        rr.overlayMaterials.AddRange(entry.overlayMaterials);
                    }

                    record.renderers.Add(rr);
                }

                record.installedRendererCount = record.renderers.Count;
                record.installedMaterialSlotCount = record.renderers.Sum(r => r.installedMaterials.Count);
                record.overlayRendererCount = record.renderers.Count(r => !string.IsNullOrEmpty(r.overlayObjectPath));

                AssetDatabase.CreateAsset(record, recordPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Undo.CollapseUndoOperations(undoGroup);
                if (avatarRoot.scene.IsValid()) EditorSceneManager.MarkSceneDirty(avatarRoot.scene);

                result.success = true;
                result.record = record;
                result.message = string.Format(
                    "安装完成：Renderer {0}；材质槽 {1}；Overlay Renderer {2}。\n" +
                    "两套 Outfit 已保持可渲染，默认视觉由 Profile 的 Role 决定；Overlay 默认强度为 0。\n安装记录：{3}",
                    record.installedRendererCount, record.installedMaterialSlotCount,
                    record.overlayRendererCount, recordPath);
                return result;
            }
            catch (Exception ex)
            {
                if (sceneTouched && undoGroup >= 0)
                {
                    try { Undo.RevertAllDownToGroup(undoGroup); } catch { }
                }
                if (AssetDatabase.LoadAssetAtPath<YBAvatarInstallRecord>(recordPath) != null)
                    AssetDatabase.DeleteAsset(recordPath);
                if (AssetDatabase.IsValidFolder(overlayMeshFolder)) AssetDatabase.DeleteAsset(overlayMeshFolder);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                result.success = false;
                result.message = "安装失败：" + ex.Message;
                return result;
            }
        }

        internal static string ValidateInstalled(GameObject avatarRoot, YBAvatarInstallRecord record)
        {
            if (Application.isPlaying) return ValidateInstalledRuntime(avatarRoot, record);
            if (avatarRoot == null) return "安装验证失败：Avatar Root 为空。";
            if (record == null) return "安装验证失败：YBInstallRecord 为空。";

            int rendererOk = 0, meshOk = 0, materialOk = 0, overlayOk = 0, enabledOk = 0;
            var details = new List<string>();
            foreach (var rr in record.renderers)
            {
                Renderer r = FindInstalledRenderer(avatarRoot, rr);
                if (r == null)
                {
                    details.Add("缺少 Renderer：" + rr.installedRendererPath);
                    continue;
                }
                rendererOk++;
                if (GetRendererMesh(r) == rr.installedBakedMesh) meshOk++;
                else details.Add("Mesh 不一致：" + rr.installedRendererPath);

                var mats = r.sharedMaterials ?? Array.Empty<Material>();
                bool matsMatch = mats.Length == rr.installedMaterials.Count;
                if (matsMatch)
                {
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != rr.installedMaterials[i]) { matsMatch = false; break; }
                }
                if (matsMatch) materialOk++;
                else details.Add("材质不一致：" + rr.installedRendererPath);

                if (r.enabled) enabledOk++;
                else details.Add("Renderer.enabled=false：" + rr.installedRendererPath);

                if (!string.IsNullOrEmpty(rr.overlayObjectPath))
                {
                    Transform ot = avatarRoot.transform.Find(rr.overlayObjectPath);
                    if (ot == null && !string.IsNullOrEmpty(rr.overlayObjectName))
                        ot = r.transform.Find(rr.overlayObjectName);
                    Renderer or = ot != null ? ot.GetComponent<Renderer>() : null;
                    if (or != null && GetRendererMesh(or) == rr.overlayMesh)
                    {
                        var om = or.sharedMaterials ?? Array.Empty<Material>();
                        bool overlayMaterialsMatch = om.Length == rr.overlayMaterials.Count;
                        if (overlayMaterialsMatch)
                            for (int i = 0; i < om.Length; i++)
                                if (om[i] != rr.overlayMaterials[i]) { overlayMaterialsMatch = false; break; }
                        if (overlayMaterialsMatch) overlayOk++;
                        else details.Add("Overlay 材质不一致：" + rr.overlayObjectPath);
                    }
                    else details.Add("Overlay 缺失/错误：" + rr.overlayObjectPath);
                }
            }

            int expected = record.renderers.Count;
            int expectedOverlays = record.renderers.Count(r => !string.IsNullOrEmpty(r.overlayObjectPath));
            bool ok = rendererOk == expected && meshOk == expected && materialOk == expected && enabledOk == expected && overlayOk == expectedOverlays;
            var sb = new StringBuilder();
            sb.AppendFormat("安装验证：Renderer {0}/{1}；Baked Mesh {2}/{1}；材质 {3}/{1}；Enabled {4}/{1}；Overlay {5}/{6}。",
                rendererOk, expected, meshOk, materialOk, enabledOk, overlayOk, expectedOverlays);
            if (ok) sb.Append("\n验证通过，可以进入 Animator / MA 阶段。");
            else
            {
                sb.Append("\n存在不一致，请先恢复或修复，不要继续 Animator / MA。");
                int show = Mathf.Min(details.Count, 10);
                for (int i = 0; i < show; i++) sb.Append("\n• ").Append(details[i]);
                if (details.Count > show) sb.Append("\n…另有 ").Append(details.Count - show).Append(" 项");
            }
            return sb.ToString();
        }


        private static string ValidateInstalledRuntime(GameObject avatarRoot, YBAvatarInstallRecord record)
        {
            if (avatarRoot == null) return "Play Mode 安装验证失败：Avatar Root 为空。";
            if (record == null) return "Play Mode 安装验证失败：YBInstallRecord 为空。";

            Renderer[] all = avatarRoot.GetComponentsInChildren<Renderer>(true);
            int rendererOk = 0, meshOk = 0, materialOk = 0, overlayOk = 0, enabledOk = 0;
            int moved = 0;
            var details = new List<string>();

            foreach (var rr in record.renderers)
            {
                Renderer r = FindInstalledRendererRuntime(avatarRoot, all, rr, false);
                if (r == null)
                {
                    details.Add("运行时缺少 Renderer：" + rr.rendererName + " | " + rr.installedRendererPath);
                    continue;
                }

                rendererOk++;
                string actualPath = YBPathUtility.GetRelativePath(avatarRoot.transform, r.transform);
                if (!string.Equals(actualPath, rr.installedRendererPath, StringComparison.Ordinal)) moved++;

                Mesh currentMesh = GetRendererMesh(r);
                if (RuntimeMeshCompatible(currentMesh, rr.installedBakedMesh)) meshOk++;
                else details.Add("运行时 Mesh/UV4 不兼容：" + actualPath);

                if (RuntimeMaterialsCompatible(r.sharedMaterials, rr.installedMaterials)) materialOk++;
                else details.Add("运行时材质扫描属性不兼容：" + actualPath);

                if (r.enabled) enabledOk++;
                else details.Add("Renderer.enabled=false：" + actualPath);

                if (!string.IsNullOrEmpty(rr.overlayObjectPath))
                {
                    Renderer overlay = FindInstalledRendererRuntime(avatarRoot, all, rr, true);
                    if (overlay != null && RuntimeMeshCompatible(GetRendererMesh(overlay), rr.overlayMesh) &&
                        RuntimeMaterialsCompatible(overlay.sharedMaterials, rr.overlayMaterials))
                    {
                        overlayOk++;
                    }
                    else
                    {
                        details.Add("运行时 Overlay 无法定位/不兼容：" + rr.overlayObjectName);
                    }
                }
            }

            int expected = record.renderers.Count;
            int expectedOverlays = record.renderers.Count(r => !string.IsNullOrEmpty(r.overlayObjectPath));
            bool ok = rendererOk == expected && meshOk == expected && materialOk == expected &&
                      enabledOk == expected && overlayOk == expectedOverlays;

            var sb = new StringBuilder();
            sb.AppendFormat(
                "Play Mode / MA 运行验证：Renderer {0}/{1}；Baked UV4 {2}/{1}；材质兼容 {3}/{1}；Enabled {4}/{1}；Overlay {5}/{6}。",
                rendererOk, expected, meshOk, materialOk, enabledOk, overlayOk, expectedOverlays);
            sb.Append("\n运行时允许 Mesh / Overlay 被 MA、NDMF 或 Play Mode 构建复制，不再要求与 Edit Mode Asset 引用完全相等。");
            if (moved > 0) sb.Append("\n检测到 ").Append(moved).Append(" 个 Renderer 路径发生运行时重定位（正常兼容情况）。");
            if (ok)
            {
                sb.Append("\n运行验证通过，可以继续 Animator / MA 测试。");
            }
            else
            {
                sb.Append("\n存在真正的运行时缺失/扫描数据异常，请先检查后再继续。");
                int show = Mathf.Min(details.Count, 10);
                for (int i = 0; i < show; i++) sb.Append("\n• ").Append(details[i]);
                if (details.Count > show) sb.Append("\n…另有 ").Append(details.Count - show).Append(" 项");
            }
            return sb.ToString();
        }

        private static Renderer FindInstalledRendererRuntime(
            GameObject avatarRoot, Renderer[] all, YBInstalledRendererRecord rr, bool overlay)
        {
            string path = overlay ? rr.overlayObjectPath : rr.installedRendererPath;
            Transform exact = !string.IsNullOrEmpty(path) ? avatarRoot.transform.Find(path) : null;
            Renderer direct = exact != null ? exact.GetComponent<Renderer>() : null;
            if (direct != null) return direct;

            string expectedName = overlay ? rr.overlayObjectName : rr.rendererName;
            string expectedType = rr.rendererType;
            Mesh expectedMesh = overlay ? rr.overlayMesh : rr.installedBakedMesh;
            var candidates = new List<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                if (!string.IsNullOrEmpty(expectedName) && r.name != expectedName) continue;
                if (!overlay && !string.IsNullOrEmpty(expectedType) &&
                    r.GetType().Name != expectedType && r.GetType().FullName != expectedType) continue;
                candidates.Add(r);
            }

            if (candidates.Count == 1) return candidates[0];
            var meshCompatible = candidates.Where(r => RuntimeMeshCompatible(GetRendererMesh(r), expectedMesh)).ToList();
            if (meshCompatible.Count == 1) return meshCompatible[0];
            return null;
        }

        private static bool RuntimeMeshCompatible(Mesh current, Mesh expected)
        {
            if (current == null || expected == null) return false;
            if (current == expected) return true;
            if (current.vertexCount != expected.vertexCount || current.subMeshCount != expected.subMeshCount) return false;
            try
            {
                var uv4 = new List<Vector4>();
                current.GetUVs(BakedUvChannel, uv4);
                return uv4.Count == current.vertexCount;
            }
            catch
            {
                return false;
            }
        }

        private static bool RuntimeMaterialsCompatible(IList<Material> current, IList<Material> expected)
        {
            if (current == null || expected == null || current.Count != expected.Count) return false;
            for (int i = 0; i < expected.Count; i++)
            {
                Material c = current[i];
                Material e = expected[i];
                if (c == e) continue;
                if (e == null)
                {
                    if (c != null) return false;
                    continue;
                }
                if (c == null || c.shader == null || e.shader == null) return false;

                bool expectedScan = e.HasProperty("_YBScanHeight") || e.HasProperty("_YBScanIntensity");
                if (expectedScan)
                {
                    bool mainCompatible = e.HasProperty("_YBScanHeight") &&
                                          c.HasProperty("_YBScanHeight") &&
                                          c.HasProperty("_YBScanRole") &&
                                          c.HasProperty("_YBScanDissolveEnabled");
                    bool overlayCompatible = e.HasProperty("_YBScanIntensity") &&
                                             c.HasProperty("_YBScanHeight") &&
                                             c.HasProperty("_YBScanIntensity");
                    if (!mainCompatible && !overlayCompatible) return false;
                }
                else if (!string.Equals(c.shader.name, e.shader.name, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        internal static YBInstallOperationResult Uninstall(GameObject avatarRoot, YBAvatarInstallRecord record, string outputRoot)
        {
            var result = new YBInstallOperationResult();
            int undoGroup = -1;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
                if (avatarRoot == null) throw new InvalidOperationException("Avatar Root 为空。");
                if (record == null) throw new InvalidOperationException("YBInstallRecord 为空。");

                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("YB Dissolve Avatar Uninstall");

                // Overlay 先删除，避免恢复主 Renderer 时留下重复绘制。
                foreach (var rr in record.renderers)
                {
                    Transform ot = !string.IsNullOrEmpty(rr.overlayObjectPath) ? avatarRoot.transform.Find(rr.overlayObjectPath) : null;
                    if (ot == null && !string.IsNullOrEmpty(rr.overlayObjectName))
                    {
                        Renderer source = FindInstalledRenderer(avatarRoot, rr);
                        if (source != null) ot = source.transform.Find(rr.overlayObjectName);
                    }
                    if (ot != null && ot.name.StartsWith(OverlayPrefix, StringComparison.Ordinal))
                        Undo.DestroyObjectImmediate(ot.gameObject);
                }

                foreach (var rr in record.renderers)
                {
                    Renderer r = FindInstalledRenderer(avatarRoot, rr);
                    if (r == null) throw new InvalidOperationException("卸载时找不到 Renderer：" + rr.installedRendererPath);
                    if (rr.originalMesh == null) throw new InvalidOperationException("原 Mesh 引用丢失：" + rr.rendererName);

                    Undo.RecordObject(r, "YB Restore Renderer");
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null) Undo.RecordObject(mf, "YB Restore Mesh");
                    AssignMesh(r, rr.originalMesh);
                    r.sharedMaterials = rr.originalMaterials.ToArray();
                    r.enabled = rr.originalEnabled;
                    RecordPrefab(r);
                    if (mf != null) RecordPrefab(mf);
                }

                foreach (var ar in record.activatedObjects
                    .OrderByDescending(a => PathDepth(a.transformPath)))
                {
                    Transform t = avatarRoot.transform.Find(ar.transformPath);
                    if (t == null) continue;
                    Undo.RecordObject(t.gameObject, "YB Restore Active State");
                    t.gameObject.SetActive(ar.originalActiveSelf);
                    RecordPrefab(t.gameObject);
                }

                Undo.CollapseUndoOperations(undoGroup);
                if (avatarRoot.scene.IsValid()) EditorSceneManager.MarkSceneDirty(avatarRoot.scene);

                string recordPath = AssetDatabase.GetAssetPath(record);
                if (!string.IsNullOrEmpty(recordPath)) AssetDatabase.DeleteAsset(recordPath);
                string overlayMeshFolder = outputRoot.TrimEnd('/') + "/Backup/InstallOverlayMesh";
                if (AssetDatabase.IsValidFolder(overlayMeshFolder)) AssetDatabase.DeleteAsset(overlayMeshFolder);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                result.success = true;
                result.message = "已卸载当前安装：原 Mesh / 材质 / Renderer.enabled / 安装器开启的 GameObject 已恢复，Overlay Renderer 已删除。生成的 Baked/Dissolve/Overlay 主资产保留。";
                return result;
            }
            catch (Exception ex)
            {
                if (undoGroup >= 0)
                {
                    try { Undo.RevertAllDownToGroup(undoGroup); } catch { }
                }
                result.success = false;
                result.message = "卸载失败：" + ex.Message;
                return result;
            }
        }

        private static List<InstallPlanEntry> BuildPlan(GameObject avatarRoot, YBDissolveProfile profile, YBGeneratedAssetManifest manifest, bool readOnly)
        {
            if (manifest.profile != null && manifest.profile != profile)
                throw new InvalidOperationException("Generation Manifest 属于另一个 Profile：" + manifest.profile.name);
            if (manifest.renderers == null || manifest.renderers.Count == 0)
                throw new InvalidOperationException("Generation Manifest 没有 Renderer 记录。");

            var plan = new List<InstallPlanEntry>();
            var used = new HashSet<Renderer>();
            foreach (var mr in manifest.renderers)
            {
                if (mr == null) continue;
                if (mr.bakedMesh == null) throw new InvalidOperationException(mr.editorPath + "：Baked Mesh 引用为空。");
                var uv4 = new List<Vector4>();
                mr.bakedMesh.GetUVs(BakedUvChannel, uv4);
                if (uv4.Count != mr.bakedMesh.vertexCount)
                    throw new InvalidOperationException(mr.editorPath + "：Baked Mesh UV4/TEXCOORD3 无效。");

                var fp = BuildManifestFingerprint(mr);
                var located = YBRendererLocator.Locate(avatarRoot.transform, fp);
                if (located.renderer == null) throw new InvalidOperationException(mr.editorPath + "：无法定位 Renderer（" + located.reason + "）。");
                if (located.ambiguous) throw new InvalidOperationException(mr.editorPath + "：Renderer 指纹存在并列候选，需要先修复/手动定位。");
                Renderer r = located.renderer;
                if (!used.Add(r)) throw new InvalidOperationException("多个 Manifest 记录解析到同一 Renderer：" + r.name);
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer))
                    throw new InvalidOperationException(mr.editorPath + "：暂不支持 Renderer 类型 " + r.GetType().Name);

                Mesh currentMesh = GetRendererMesh(r);
                if (currentMesh != mr.sourceMesh)
                    throw new InvalidOperationException(mr.editorPath + "：当前 Mesh 已与生成时不同。请重新分析/生成后再安装。");

                Material[] current = r.sharedMaterials ?? Array.Empty<Material>();
                int expectedSlotCount = mr.materialSlots != null ? mr.materialSlots.Count : 0;
                if (current.Length != expectedSlotCount)
                    throw new InvalidOperationException(mr.editorPath + "：当前材质槽数量 " + current.Length + " 与 Manifest " + expectedSlotCount + " 不一致。");

                var installed = (Material[])current.Clone();
                var entry = new InstallPlanEntry
                {
                    manifestRecord = mr,
                    renderer = r,
                    bakedMesh = mr.bakedMesh,
                    sourceMaterials = current,
                    installedMaterials = installed,
                    locateReason = located.reason
                };

                for (int i = 0; i < mr.materialSlots.Count; i++)
                {
                    var sr = mr.materialSlots[i];
                    if (sr.slotIndex < 0 || sr.slotIndex >= current.Length)
                        throw new InvalidOperationException(mr.editorPath + "：Manifest Slot 越界：" + sr.slotIndex);
                    if (current[sr.slotIndex] != sr.sourceMaterial)
                        throw new InvalidOperationException(mr.editorPath + " / Slot " + sr.slotIndex + "：当前源材质已改变，请重新生成 Manifest。");

                    if (sr.dissolveMaterial != null)
                    {
                        if (sr.dissolveMaterial.shader == null || ShaderUtil.ShaderHasError(sr.dissolveMaterial.shader))
                            throw new InvalidOperationException(mr.editorPath + " / Slot " + sr.slotIndex + "：Dissolve Shader 未就绪/存在编译错误。");
                        installed[sr.slotIndex] = sr.dissolveMaterial;
                    }

                    if (sr.overlayMaterial != null && sr.slotIndex < mr.bakedMesh.subMeshCount)
                    {
                        if (sr.overlayMaterial.shader == null || ShaderUtil.ShaderHasError(sr.overlayMaterial.shader))
                            throw new InvalidOperationException(mr.editorPath + " / Slot " + sr.slotIndex + "：Overlay Shader 未就绪/存在编译错误。");
                        entry.overlaySourceSlots.Add(sr.slotIndex);
                        entry.overlayMaterials.Add(sr.overlayMaterial);
                    }
                }

                plan.Add(entry);
            }
            return plan;
        }

        private static YBRendererFingerprint BuildManifestFingerprint(YBGeneratedRendererRecord mr)
        {
            var fp = new YBRendererFingerprint
            {
                editorPath = mr.editorPath,
                rendererName = mr.rendererName,
                rendererType = mr.rendererType != null && mr.rendererType.IndexOf('.') >= 0 ? mr.rendererType : "UnityEngine." + mr.rendererType,
                meshName = mr.sourceMesh != null ? mr.sourceMesh.name : mr.meshName,
                materialSlotCount = mr.materialSlots != null ? mr.materialSlots.Count : 0
            };
            if (mr.materialSlots != null)
            {
                foreach (var sr in mr.materialSlots.OrderBy(s => s.slotIndex))
                {
                    string path = sr.sourceMaterial != null ? AssetDatabase.GetAssetPath(sr.sourceMaterial) : string.Empty;
                    fp.materialAssetGuids.Add(string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path));
                }
            }
            return fp;
        }

        private static void CaptureAndActivateRequired(GameObject avatarRoot, IEnumerable<Renderer> renderers, YBAvatarInstallRecord record)
        {
            var pending = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            foreach (Renderer r in renderers)
            {
                Transform t = r.transform;
                while (t != null && t != avatarRoot.transform)
                {
                    if (!t.gameObject.activeSelf)
                    {
                        string path = YBPathUtility.GetRelativePath(avatarRoot.transform, t);
                        if (!pending.ContainsKey(path)) pending.Add(path, t.gameObject);
                    }
                    t = t.parent;
                }
            }

            foreach (var kv in pending.OrderBy(kv => PathDepth(kv.Key)))
            {
                record.activatedObjects.Add(new YBInstalledActiveStateRecord
                {
                    transformPath = kv.Key,
                    originalActiveSelf = kv.Value.activeSelf
                });
                Undo.RecordObject(kv.Value, "YB Activate Outfit");
                kv.Value.SetActive(true);
                RecordPrefab(kv.Value);
            }
        }

        private static Mesh BuildOverlayMeshAsset(InstallPlanEntry entry, string folder)
        {
            Mesh source = entry.bakedMesh;
            bool full = entry.overlaySourceSlots.Count == source.subMeshCount;
            if (full)
            {
                for (int i = 0; i < source.subMeshCount; i++)
                    if (entry.overlaySourceSlots[i] != i) { full = false; break; }
            }
            if (full) return source;

            var clone = UnityEngine.Object.Instantiate(source);
            clone.name = source.name + "_YBOverlay";
            var indexSets = new List<int[]>();
            var topologies = new List<MeshTopology>();
            for (int i = 0; i < entry.overlaySourceSlots.Count; i++)
            {
                int src = entry.overlaySourceSlots[i];
                indexSets.Add(source.GetIndices(src, true));
                topologies.Add(source.GetTopology(src));
            }
            clone.subMeshCount = indexSets.Count;
            for (int i = 0; i < indexSets.Count; i++)
                clone.SetIndices(indexSets[i], topologies[i], i, false);
            clone.bounds = source.bounds;

            string file = SafeName(entry.renderer.name) + "_" + Hash8(entry.manifestRecord.editorPath) + "_Overlay.asset";
            string path = AssetDatabase.GenerateUniqueAssetPath(folder.TrimEnd('/') + "/" + file);
            AssetDatabase.CreateAsset(clone, path);
            return clone;
        }

        private static Renderer CreateOverlayRenderer(Renderer source, Mesh overlayMesh, Material[] materials, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "YB Create Overlay");
            go.layer = source.gameObject.layer;
            go.transform.SetParent(source.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            Renderer overlay;
            var sourceSkin = source as SkinnedMeshRenderer;
            if (sourceSkin != null)
            {
                var skin = go.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = overlayMesh;
                skin.bones = sourceSkin.bones;
                skin.rootBone = sourceSkin.rootBone;
                skin.localBounds = sourceSkin.localBounds;
                skin.quality = sourceSkin.quality;
                skin.updateWhenOffscreen = sourceSkin.updateWhenOffscreen;
                overlay = skin;
            }
            else
            {
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = overlayMesh;
                overlay = go.AddComponent<MeshRenderer>();
            }

            overlay.sharedMaterials = materials;
            overlay.enabled = true;
            overlay.shadowCastingMode = ShadowCastingMode.Off;
            overlay.receiveShadows = false;
            overlay.lightProbeUsage = LightProbeUsage.Off;
            overlay.reflectionProbeUsage = ReflectionProbeUsage.Off;
            overlay.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            overlay.allowOcclusionWhenDynamic = false;
            overlay.sortingLayerID = source.sortingLayerID;
            overlay.sortingOrder = source.sortingOrder;
            RecordPrefab(overlay);
            return overlay;
        }

        private static Renderer FindInstalledRenderer(GameObject avatarRoot, YBInstalledRendererRecord rr)
        {
            if (avatarRoot == null || rr == null) return null;
            if (!string.IsNullOrEmpty(rr.installedRendererPath))
            {
                Transform t = avatarRoot.transform.Find(rr.installedRendererPath);
                if (t != null)
                {
                    Renderer r = t.GetComponent<Renderer>();
                    if (r != null && !r.name.StartsWith(OverlayPrefix, StringComparison.Ordinal)) return r;
                }
            }

            var all = avatarRoot.GetComponentsInChildren<Renderer>(true)
                .Where(r => r != null && !r.name.StartsWith(OverlayPrefix, StringComparison.Ordinal))
                .Where(r => r.name == rr.rendererName && r.GetType().Name == rr.rendererType)
                .ToList();
            var meshHits = all.Where(r => GetRendererMesh(r) == rr.installedBakedMesh || GetRendererMesh(r) == rr.originalMesh).ToList();
            if (meshHits.Count == 1) return meshHits[0];
            return all.Count == 1 ? all[0] : null;
        }

        private static Mesh GetRendererMesh(Renderer renderer)
        {
            var smr = renderer as SkinnedMeshRenderer;
            if (smr != null) return smr.sharedMesh;
            var mf = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            return mf != null ? mf.sharedMesh : null;
        }

        private static void AssignMesh(Renderer renderer, Mesh mesh)
        {
            var smr = renderer as SkinnedMeshRenderer;
            if (smr != null) { smr.sharedMesh = mesh; return; }
            var mf = renderer.GetComponent<MeshFilter>();
            if (mf == null) throw new InvalidOperationException("MeshRenderer 缺少 MeshFilter：" + renderer.name);
            mf.sharedMesh = mesh;
        }

        private static void EnsureAssetFolder(string fullPath)
        {
            string path = fullPath.Replace('\\', '/');
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

        private static int PathDepth(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            int d = 1;
            for (int i = 0; i < path.Length; i++) if (path[i] == '/') d++;
            return d;
        }

        private static string SafeName(string name)
        {
            return YBPathUtility.SanitizeAssetName(name, "Renderer").Replace(' ', '_');
        }

        private static string Hash8(string text)
        {
            string h = Hash128.Compute(text ?? string.Empty).ToString();
            return h.Length > 8 ? h.Substring(0, 8) : h;
        }

        private static void RecordPrefab(UnityEngine.Object obj)
        {
            if (obj == null) return;
            try { PrefabUtility.RecordPrefabInstancePropertyModifications(obj); } catch { }
        }
    }
}
