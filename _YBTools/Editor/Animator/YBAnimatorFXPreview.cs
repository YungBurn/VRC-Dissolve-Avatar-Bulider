using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace YBTools.Dissolve.Editor
{
    internal static class YBAnimatorFXPreview
    {
        private static PlayableGraph graph;
        private static AnimatorControllerPlayable fxPlayable;
        private static Animator previewAnimator;
        private static RuntimeAnimatorController originalController;
        private static AnimatorOverrideController runtimeOverride;
        private static readonly List<AnimationClip> runtimeClips = new List<AnimationClip>();
        private static bool addedAnimator;
        private static bool active;
        private static string parameterName;
        private static bool currentValue;

        static YBAnimatorFXPreview()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode) Stop();
            };
        }

        public static bool Active { get { return active; } }
        public static bool CurrentValue { get { return currentValue; } }

        public static string DiagnoseRuntimePaths(GameObject avatarRoot, YBAvatarInstallRecord installRecord)
        {
            if (!Application.isPlaying) return "请先进入 Play Mode。";
            if (avatarRoot == null || installRecord == null) return "Avatar / InstallRecord 不完整。";

            Dictionary<string, string> map;
            int exact, moved;
            List<string> missing;
            BuildRuntimePathMap(avatarRoot, installRecord, out map, out exact, out moved, out missing);
            string msg = string.Format("Play Mode 路径诊断：原路径 {0}；已重定位 {1}；真正缺失 {2}。", exact, moved, missing.Count);
            if (missing.Count > 0) msg += "\n• " + string.Join("\n• ", missing.Take(12).ToArray());
            return msg;
        }

        public static string Start(
            GameObject avatarRoot,
            YBDissolveProfile profile,
            YBAvatarInstallRecord installRecord,
            YBAnimatorFXAssets assets,
            bool runOriginalController,
            bool runtimeRebind)
        {
            try
            {
                if (!Application.isPlaying) throw new InvalidOperationException("请先进入 Play Mode。 ");
                if (avatarRoot == null) throw new InvalidOperationException("Avatar Root 为空。 ");
                if (profile == null) throw new InvalidOperationException("Profile 为空。 ");
                if (installRecord == null) throw new InvalidOperationException("InstallRecord 为空。 ");
                if (assets == null || !assets.success || assets.controller == null) throw new InvalidOperationException("Animator-FX 资产不完整。 ");
                Stop();

                Dictionary<string, string> pathMap;
                int exact, moved;
                List<string> missing;
                BuildRuntimePathMap(avatarRoot, installRecord, out pathMap, out exact, out moved, out missing);
                if (missing.Count > 0)
                    throw new InvalidOperationException("运行时有 Renderer 无法定位：\n• " + string.Join("\n• ", missing.Take(12).ToArray()));

                RuntimeAnimatorController runtimeController = assets.controller;
                if (runtimeRebind && moved > 0)
                    runtimeController = BuildRuntimeOverride(assets, pathMap);

                previewAnimator = avatarRoot.GetComponent<Animator>();
                addedAnimator = false;
                if (previewAnimator == null)
                {
                    previewAnimator = avatarRoot.AddComponent<Animator>();
                    addedAnimator = true;
                }
                originalController = previewAnimator.runtimeAnimatorController;
                previewAnimator.runtimeAnimatorController = null;

                graph = PlayableGraph.Create("YB Unified Animator-FX Preview");
                graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                bool useBase = runOriginalController && originalController != null;
                AnimationLayerMixerPlayable mixer = AnimationLayerMixerPlayable.Create(graph, useBase ? 2 : 1);
                int fxInput = 0;
                if (useBase)
                {
                    AnimatorControllerPlayable basePlayable = AnimatorControllerPlayable.Create(graph, originalController);
                    graph.Connect(basePlayable, 0, mixer, 0);
                    mixer.SetInputWeight(0, 1f);
                    fxInput = 1;
                }

                fxPlayable = AnimatorControllerPlayable.Create(graph, runtimeController);
                graph.Connect(fxPlayable, 0, mixer, fxInput);
                mixer.SetInputWeight(fxInput, 1f);
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "YB Animator-FX", previewAnimator);
                output.SetSourcePlayable(mixer);
                graph.Play();

                parameterName = profile.parameterName;
                currentValue = profile.defaultOutfit == YBOutfitDefault.OutfitB;
                fxPlayable.SetBool(Animator.StringToHash(parameterName), currentValue);
                active = true;

                return string.Format(
                    "临时 Animator-FX 验证已开始。当前 {0}={1}。路径：原路径 {2} / 重定位 {3}。{4}",
                    parameterName, currentValue ? "true" : "false", exact, moved,
                    runtimeRebind ? "运行时重绑定已开启。" : "运行时重绑定已关闭。");
            }
            catch (Exception ex)
            {
                Stop();
                Debug.LogException(ex);
                return "Animator-FX 预览启动失败：" + ex.Message;
            }
        }

        public static string SetValue(bool value)
        {
            if (!active || !fxPlayable.IsValid()) return "Animator-FX 临时验证尚未启动。";
            currentValue = value;
            fxPlayable.SetBool(Animator.StringToHash(parameterName), value);
            return parameterName + "=" + (value ? "true（服装 B）" : "false（服装 A）");
        }

        public static void Stop()
        {
            try { if (graph.IsValid()) graph.Destroy(); } catch { }
            if (previewAnimator != null)
            {
                try { previewAnimator.runtimeAnimatorController = originalController; } catch { }
                if (addedAnimator)
                {
                    try { UnityEngine.Object.DestroyImmediate(previewAnimator); } catch { }
                }
            }
            if (runtimeOverride != null)
            {
                try { UnityEngine.Object.DestroyImmediate(runtimeOverride); } catch { }
                runtimeOverride = null;
            }
            for (int i = 0; i < runtimeClips.Count; i++)
                if (runtimeClips[i] != null) try { UnityEngine.Object.DestroyImmediate(runtimeClips[i]); } catch { }
            runtimeClips.Clear();
            previewAnimator = null;
            originalController = null;
            addedAnimator = false;
            active = false;
            fxPlayable = default(AnimatorControllerPlayable);
        }

        private static RuntimeAnimatorController BuildRuntimeOverride(YBAnimatorFXAssets assets, Dictionary<string, string> pathMap)
        {
            AnimationClip ai = CloneClip(assets.outfitAIdle, pathMap);
            AnimationClip ab = CloneClip(assets.outfitAToB, pathMap);
            AnimationClip bi = CloneClip(assets.outfitBIdle, pathMap);
            AnimationClip ba = CloneClip(assets.outfitBToA, pathMap);
            runtimeOverride = new AnimatorOverrideController(assets.controller);
            runtimeOverride.name = "YB_Dissolve_FX__RuntimeRebound";
            runtimeOverride[assets.outfitAIdle] = ai;
            runtimeOverride[assets.outfitAToB] = ab;
            runtimeOverride[assets.outfitBIdle] = bi;
            runtimeOverride[assets.outfitBToA] = ba;
            return runtimeOverride;
        }

        private static AnimationClip CloneClip(AnimationClip source, Dictionary<string, string> pathMap)
        {
            var clone = UnityEngine.Object.Instantiate(source);
            clone.name = source.name + "__YBRuntimeRebound";
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clone);
            for (int i = 0; i < bindings.Length; i++)
            {
                EditorCurveBinding oldBinding = bindings[i];
                string newPath;
                if (!pathMap.TryGetValue(oldBinding.path, out newPath) || newPath == oldBinding.path) continue;
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clone, oldBinding);
                AnimationUtility.SetEditorCurve(clone, oldBinding, null);
                EditorCurveBinding nb = oldBinding;
                nb.path = newPath;
                AnimationUtility.SetEditorCurve(clone, nb, curve);
            }
            EditorCurveBinding[] objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clone);
            for (int i = 0; i < objBindings.Length; i++)
            {
                EditorCurveBinding oldBinding = objBindings[i];
                string newPath;
                if (!pathMap.TryGetValue(oldBinding.path, out newPath) || newPath == oldBinding.path) continue;
                ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clone, oldBinding);
                AnimationUtility.SetObjectReferenceCurve(clone, oldBinding, null);
                EditorCurveBinding nb = oldBinding;
                nb.path = newPath;
                AnimationUtility.SetObjectReferenceCurve(clone, nb, keys);
            }
            runtimeClips.Add(clone);
            return clone;
        }

        private static void BuildRuntimePathMap(
            GameObject avatarRoot,
            YBAvatarInstallRecord installRecord,
            out Dictionary<string, string> map,
            out int exact,
            out int moved,
            out List<string> missing)
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            exact = 0;
            moved = 0;
            missing = new List<string>();
            Renderer[] all = avatarRoot.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < installRecord.renderers.Count; i++)
            {
                YBInstalledRendererRecord rec = installRecord.renderers[i];
                Renderer r = ResolveRenderer(avatarRoot, all, rec, false);
                if (r == null)
                {
                    missing.Add("主 Renderer: " + rec.rendererName + " | " + rec.installedRendererPath);
                }
                else
                {
                    string actual = AnimationUtility.CalculateTransformPath(r.transform, avatarRoot.transform);
                    map[rec.installedRendererPath] = actual;
                    if (actual == rec.installedRendererPath) exact++; else moved++;
                }

                if (!string.IsNullOrEmpty(rec.overlayObjectPath))
                {
                    Renderer or = ResolveRenderer(avatarRoot, all, rec, true);
                    if (or == null)
                    {
                        missing.Add("Overlay: " + rec.overlayObjectPath);
                    }
                    else
                    {
                        string actual = AnimationUtility.CalculateTransformPath(or.transform, avatarRoot.transform);
                        map[rec.overlayObjectPath] = actual;
                        if (actual == rec.overlayObjectPath) exact++; else moved++;
                    }
                }
            }
        }

        private static Renderer ResolveRenderer(GameObject avatarRoot, Renderer[] all, YBInstalledRendererRecord rec, bool overlay)
        {
            string path = overlay ? rec.overlayObjectPath : rec.installedRendererPath;
            Transform exact = !string.IsNullOrEmpty(path) ? avatarRoot.transform.Find(path) : null;
            Renderer direct = exact != null ? exact.GetComponent<Renderer>() : null;
            if (direct != null) return direct;

            Mesh expectedMesh = overlay ? rec.overlayMesh : rec.installedBakedMesh;
            string expectedName = overlay ? rec.overlayObjectName : rec.rendererName;
            string expectedType = rec.rendererType;
            var candidates = new List<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                if (!string.IsNullOrEmpty(expectedName) && r.name != expectedName) continue;
                if (!overlay && !string.IsNullOrEmpty(expectedType) && r.GetType().Name != expectedType && r.GetType().FullName != expectedType) continue;
                if (expectedMesh != null && GetMesh(r) != expectedMesh) continue;
                candidates.Add(r);
            }
            return candidates.Count == 1 ? candidates[0] : null;
        }

        private static Mesh GetMesh(Renderer r)
        {
            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            if (smr != null) return smr.sharedMesh;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }
    }
}
