using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YBTools.Dissolve.Editor
{
    internal sealed class YBAnimatorFXAssets
    {
        public AnimationClip outfitAIdle;
        public AnimationClip outfitAToB;
        public AnimationClip outfitBIdle;
        public AnimationClip outfitBToA;
        public AnimatorController controller;
        public string message;
        public bool success;
    }

    internal static class YBAnimatorFXBuilder
    {
        private const float IdleLength = 1f;
        private const float GlowFadeDefault = 0.15f;

        private sealed class Target
        {
            public string path;
            public Type type;
            public string outfitGroup;
            public bool overlay;
        }

        public static string ValidateInputs(
            GameObject avatarRoot,
            YBDissolveProfile profile,
            YBGeneratedAssetManifest manifest,
            YBAvatarInstallRecord installRecord,
            string outputRoot)
        {
            if (avatarRoot == null) return "Avatar Root 为空。";
            if (profile == null) return "Profile 为空。";
            if (manifest == null) return "无法识别 Generation Manifest。请先在“验证”页重新识别/修复。";
            if (installRecord == null) return "没有找到 YBInstallRecord。请先完成正式安装。";
            if (manifest.renderers == null || manifest.renderers.Count == 0) return "Generation Manifest 没有 Renderer 记录。";
            if (installRecord.renderers == null || installRecord.renderers.Count == 0) return "InstallRecord 没有 Renderer 记录。";
            if (Mathf.Approximately(profile.scan.startHeight, profile.scan.endHeight)) return "扫描起始坐标与结束坐标相同。";
            if (profile.scan.durationSeconds <= 0f) return "扫描持续时间必须大于 0。";
            if (string.IsNullOrWhiteSpace(profile.parameterName)) return "Profile 参数名为空。";
            if (profile.scan.directionPreset == YBScanDirectionPreset.Custom &&
                profile.scan.customDirection.sqrMagnitude < 0.000001f)
                return "自定义扫描方向不能为 (0,0,0)。";

            string manifestValidation = YBGeneratedAssetBuilder.ValidateManifest(manifest);
            if (manifestValidation.IndexOf("通过", StringComparison.Ordinal) < 0)
                return "Generation Manifest 与当前 Profile 不匹配：\n" + manifestValidation;

            List<Target> targets;
            string targetError;
            if (!TryBuildTargets(avatarRoot, manifest, installRecord, out targets, out targetError)) return targetError;
            int main = targets.Count(t => !t.overlay);
            int overlay = targets.Count(t => t.overlay);
            if (main == 0) return "没有可绑定的主 Renderer。";
            if (overlay == 0) return "没有找到 Overlay Renderer。请先验证当前安装。";

            float delayAToB = profile.scan.GetDelayAToB();
            float delayBToA = profile.scan.GetDelayBToA();
            Vector3 direction = profile.scan.GetMoveDirection();
            return string.Format(
                "Animator-FX 输入检查通过：主 Renderer {0}，Overlay Renderer {1}。\n" +
                "参数：{2} (Bool)；默认={3}。\n" +
                "A→B：延迟 {4:0.###}s + 扫描 {6:0.###}s；B→A：延迟 {5:0.###}s + 扫描 {6:0.###}s。\n" +
                "扫描坐标 {7:0.###} → {8:0.###}；移动方向=({9:0.###}, {10:0.###}, {11:0.###})。\n" +
                "输出：{12}/Animations 与 {12}/Animator。",
                main, overlay, profile.parameterName,
                profile.defaultOutfit == YBOutfitDefault.OutfitB ? "服装 B" : "服装 A",
                delayAToB, delayBToA, profile.scan.durationSeconds,
                profile.scan.startHeight, profile.scan.endHeight,
                direction.x, direction.y, direction.z, outputRoot);
        }

        public static YBAnimatorFXAssets Build(
            GameObject avatarRoot,
            YBDissolveProfile profile,
            YBGeneratedAssetManifest manifest,
            YBAvatarInstallRecord installRecord,
            string outputRoot,
            bool overwrite)
        {
            var result = new YBAnimatorFXAssets();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("请退出 Play Mode 后生成 Animator-FX。 ");

                string preflight = ValidateInputs(avatarRoot, profile, manifest, installRecord, outputRoot);
                if (preflight.IndexOf("通过", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException(preflight);

                List<Target> targets;
                string targetError;
                if (!TryBuildTargets(avatarRoot, manifest, installRecord, out targets, out targetError))
                    throw new InvalidOperationException(targetError);

                string animDir = outputRoot.TrimEnd('/') + "/Animations";
                string controllerDir = outputRoot.TrimEnd('/') + "/Animator";
                EnsureFolder(animDir);
                EnsureFolder(controllerDir);

                float delayAToB = profile.scan.GetDelayAToB();
                float delayBToA = profile.scan.GetDelayBToA();
                float duration = Mathf.Max(0.01f, profile.scan.durationSeconds);
                float totalAToB = delayAToB + duration;
                float totalBToA = delayBToA + duration;
                float glowFade = Mathf.Min(GlowFadeDefault, Mathf.Max(0.02f, duration * 0.25f));

                var aIdle = BuildClip("YB_OutfitA_Idle", targets, profile, true, false, 0f, IdleLength, glowFade);
                var aToB = BuildClip("YB_OutfitA_To_B", targets, profile, true, true, delayAToB, totalAToB, glowFade);
                var bIdle = BuildClip("YB_OutfitB_Idle", targets, profile, false, false, 0f, IdleLength, glowFade);
                var bToA = BuildClip("YB_OutfitB_To_A", targets, profile, false, true, delayBToA, totalBToA, glowFade);

                // 可选材质变体：同一 Renderer 内追加 A/B SubMesh，Role 由材质版本固定。
                YBMaterialVariantInstallRecord variantRecord = YBMaterialVariantBuilder.LoadExisting(outputRoot);
                if (variantRecord != null && variantRecord.renderers != null && variantRecord.renderers.Count > 0)
                {
                    YBMaterialVariantBuilder.ApplyBindings(aIdle, variantRecord, true);
                    YBMaterialVariantBuilder.ApplyBindings(aToB, variantRecord, true);
                    YBMaterialVariantBuilder.ApplyBindings(bIdle, variantRecord, false);
                    YBMaterialVariantBuilder.ApplyBindings(bToA, variantRecord, false);
                }

                string aIdlePath = animDir + "/YB_OutfitA_Idle.anim";
                string aToBPath = animDir + "/YB_OutfitA_To_B.anim";
                string bIdlePath = animDir + "/YB_OutfitB_Idle.anim";
                string bToAPath = animDir + "/YB_OutfitB_To_A.anim";
                result.outfitAIdle = SaveClip(aIdle, aIdlePath, overwrite);
                result.outfitAToB = SaveClip(aToB, aToBPath, overwrite);
                result.outfitBIdle = SaveClip(bIdle, bIdlePath, overwrite);
                result.outfitBToA = SaveClip(bToA, bToAPath, overwrite);

                string controllerPath = controllerDir + "/YB_Dissolve_FX.controller";
                result.controller = BuildController(profile, result, controllerPath, overwrite);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                result.success = true;
                result.message = "Animator-FX 已生成/更新：\n" +
                                 aIdlePath + "\n" + aToBPath + "\n" + bIdlePath + "\n" + bToAPath + "\n" + controllerPath +
                                 "\n\n四个 State 均为 Write Defaults Off；换装过程中不允许中途打断。" +
                                 (variantRecord != null && variantRecord.renderers != null && variantRecord.renderers.Count > 0
                                     ? "\n已写入材质变体：" + variantRecord.renderers.Count + " 个 Renderer。"
                                     : string.Empty);
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                result.success = false;
                result.message = "Animator-FX 生成失败：" + ex.Message;
                return result;
            }
        }

        public static YBAnimatorFXAssets LoadExisting(string outputRoot)
        {
            var r = new YBAnimatorFXAssets();
            if (string.IsNullOrEmpty(outputRoot)) return r;
            string a = outputRoot.TrimEnd('/') + "/Animations";
            string c = outputRoot.TrimEnd('/') + "/Animator/YB_Dissolve_FX.controller";
            r.outfitAIdle = AssetDatabase.LoadAssetAtPath<AnimationClip>(a + "/YB_OutfitA_Idle.anim");
            r.outfitAToB = AssetDatabase.LoadAssetAtPath<AnimationClip>(a + "/YB_OutfitA_To_B.anim");
            r.outfitBIdle = AssetDatabase.LoadAssetAtPath<AnimationClip>(a + "/YB_OutfitB_Idle.anim");
            r.outfitBToA = AssetDatabase.LoadAssetAtPath<AnimationClip>(a + "/YB_OutfitB_To_A.anim");
            r.controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(c);
            r.success = r.outfitAIdle != null && r.outfitAToB != null && r.outfitBIdle != null && r.outfitBToA != null && r.controller != null;
            r.message = r.success ? "已识别现有 Animator-FX 资产。" : "Animator-FX 资产尚未完整生成。";
            return r;
        }

        public static string ValidateGenerated(YBDissolveProfile profile, YBAnimatorFXAssets assets)
        {
            if (profile == null) return "Profile 为空。";
            if (assets == null) return "Animator-FX 资产为空。";
            var missing = new List<string>();
            if (assets.outfitAIdle == null) missing.Add("YB_OutfitA_Idle.anim");
            if (assets.outfitAToB == null) missing.Add("YB_OutfitA_To_B.anim");
            if (assets.outfitBIdle == null) missing.Add("YB_OutfitB_Idle.anim");
            if (assets.outfitBToA == null) missing.Add("YB_OutfitB_To_A.anim");
            if (assets.controller == null) missing.Add("YB_Dissolve_FX.controller");
            if (missing.Count > 0) return "Animator-FX 验证失败，缺少：\n• " + string.Join("\n• ", missing.ToArray());

            bool hasParam = assets.controller.parameters.Any(p => p.name == profile.parameterName && p.type == AnimatorControllerParameterType.Bool);
            if (!hasParam) return "Animator-FX 验证失败：Controller 没有 Bool 参数 " + profile.parameterName + "。";
            if (assets.outfitAToB.length < profile.scan.GetDelayAToB() + profile.scan.durationSeconds - 0.05f)
                return "Animator-FX 验证失败：A→B Clip 长度短于 A→B Delay+Duration。";
            if (assets.outfitBToA.length < profile.scan.GetDelayBToA() + profile.scan.durationSeconds - 0.05f)
                return "Animator-FX 验证失败：B→A Clip 长度短于 B→A Delay+Duration。";

            return string.Format(
                "Animator-FX 验证通过：\n参数 {0}；A→B {1:0.###}s；B→A {2:0.###}s；Controller={3}",
                profile.parameterName, assets.outfitAToB.length, assets.outfitBToA.length, AssetDatabase.GetAssetPath(assets.controller));
        }

        private static bool TryBuildTargets(
            GameObject avatarRoot,
            YBGeneratedAssetManifest manifest,
            YBAvatarInstallRecord installRecord,
            out List<Target> targets,
            out string error)
        {
            targets = new List<Target>();
            error = null;
            var manifestByPath = new Dictionary<string, YBGeneratedRendererRecord>(StringComparer.Ordinal);
            for (int i = 0; i < manifest.renderers.Count; i++)
            {
                var mr = manifest.renderers[i];
                if (mr != null && !string.IsNullOrEmpty(mr.editorPath)) manifestByPath[mr.editorPath] = mr;
            }

            var errors = new List<string>();
            for (int i = 0; i < installRecord.renderers.Count; i++)
            {
                var ir = installRecord.renderers[i];
                if (ir == null) continue;
                YBGeneratedRendererRecord mr;
                if (!manifestByPath.TryGetValue(ir.manifestEditorPath ?? string.Empty, out mr))
                {
                    errors.Add("Manifest 找不到安装 Renderer 映射：" + ir.manifestEditorPath);
                    continue;
                }

                Transform t = !string.IsNullOrEmpty(ir.installedRendererPath) ? avatarRoot.transform.Find(ir.installedRendererPath) : null;
                Renderer r = t != null ? t.GetComponent<Renderer>() : null;
                if (r == null)
                {
                    errors.Add("当前 Avatar 找不到已安装 Renderer：" + ir.installedRendererPath);
                    continue;
                }

                targets.Add(new Target
                {
                    path = AnimationUtility.CalculateTransformPath(r.transform, avatarRoot.transform),
                    type = r.GetType(),
                    outfitGroup = mr.outfitGroup,
                    overlay = false
                });

                if (!string.IsNullOrEmpty(ir.overlayObjectPath))
                {
                    Transform ot = avatarRoot.transform.Find(ir.overlayObjectPath);
                    if (ot == null && !string.IsNullOrEmpty(ir.overlayObjectName)) ot = r.transform.Find(ir.overlayObjectName);
                    Renderer or = ot != null ? ot.GetComponent<Renderer>() : null;
                    if (or == null)
                    {
                        errors.Add("当前 Avatar 找不到 Overlay：" + ir.overlayObjectPath);
                        continue;
                    }
                    targets.Add(new Target
                    {
                        path = AnimationUtility.CalculateTransformPath(or.transform, avatarRoot.transform),
                        type = or.GetType(),
                        outfitGroup = mr.outfitGroup,
                        overlay = true
                    });
                }
            }

            if (errors.Count > 0)
            {
                error = "Animator 绑定检查未通过：\n• " + string.Join("\n• ", errors.Take(12).ToArray());
                if (errors.Count > 12) error += "\n…另有 " + (errors.Count - 12) + " 项";
                return false;
            }
            return targets.Count > 0;
        }

        private static AnimationClip BuildClip(
            string name,
            IList<Target> targets,
            YBDissolveProfile profile,
            bool outfitARoleZero,
            bool transition,
            float delay,
            float length,
            float glowFade)
        {
            var clip = new AnimationClip { name = name, frameRate = 60f, legacy = false };
            float start = profile.scan.startHeight;
            float end = profile.scan.endHeight;

            for (int i = 0; i < targets.Count; i++)
            {
                Target t = targets[i];
                bool isA = string.Equals(t.outfitGroup, "Outfit A", StringComparison.Ordinal);
                float role = outfitARoleZero ? (isA ? 0f : 1f) : (isA ? 1f : 0f);

                SetCurve(clip, t, "material._YBScanHeight", transition ? ScanCurve(profile, delay) : Constant(length, start));
                if (!t.overlay)
                {
                    SetCurve(clip, t, "material._YBScanRole", Constant(length, role));
                    SetCurve(clip, t, "material._YBScanDissolveEnabled", Constant(length, 1f));
                }
                else
                {
                    SetCurve(clip, t, "material._YBScanIntensity",
                        transition ? GlowCurve(delay, Mathf.Max(0.01f, length - delay), glowFade, profile.scan.scanIntensity) : Constant(length, 0f));
                }
            }

            SetLoopTime(clip, !transition);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static AnimationCurve ScanCurve(YBDissolveProfile profile, float delay)
        {
            float duration = Mathf.Max(0.01f, profile.scan.durationSeconds);
            float start = profile.scan.startHeight;
            float end = profile.scan.endHeight;
            var curve = new AnimationCurve();
            curve.AddKey(new Keyframe(0f, start));
            if (delay > 0f) curve.AddKey(new Keyframe(delay, start));

            AnimationCurve norm = NormalizedProgressCurve(profile);
            const int samples = 24;
            for (int i = 1; i <= samples; i++)
            {
                float x = i / (float)samples;
                float progress = Mathf.Clamp01(norm.Evaluate(x));
                float time = delay + duration * x;
                float value = Mathf.Lerp(start, end, progress);
                curve.AddKey(new Keyframe(time, value));
            }
            SetLinearTangents(curve);
            return curve;
        }

        private static AnimationCurve NormalizedProgressCurve(YBDissolveProfile profile)
        {
            switch (profile.scan.curvePreset)
            {
                case YBScanCurvePreset.EaseIn:
                    return new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));
                case YBScanCurvePreset.EaseOut:
                    return new AnimationCurve(new Keyframe(0f, 0f, 2f, 2f), new Keyframe(1f, 1f, 0f, 0f));
                case YBScanCurvePreset.EaseInOut:
                    return AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
                case YBScanCurvePreset.Custom:
                    return profile.scan.customCurve != null ? profile.scan.customCurve : AnimationCurve.Linear(0f, 0f, 1f, 1f);
                default:
                    return AnimationCurve.Linear(0f, 0f, 1f, 1f);
            }
        }

        private static AnimationCurve GlowCurve(float delay, float duration, float fade, float intensity)
        {
            float scanStart = delay;
            float scanEnd = delay + Mathf.Max(0.01f, duration);
            float f = Mathf.Min(Mathf.Max(0.01f, fade), Mathf.Max(0.01f, duration * 0.45f));
            var c = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(scanStart, 0f),
                new Keyframe(scanStart + f, intensity),
                new Keyframe(Mathf.Max(scanStart + f, scanEnd - f), intensity),
                new Keyframe(scanEnd, 0f));
            SetLinearTangents(c);
            return c;
        }

        private static AnimationCurve Constant(float length, float value)
        {
            var c = new AnimationCurve(new Keyframe(0f, value), new Keyframe(Mathf.Max(0.01f, length), value));
            SetConstantTangents(c);
            return c;
        }

        private static void SetCurve(AnimationClip clip, Target target, string property, AnimationCurve curve)
        {
            var binding = EditorCurveBinding.FloatCurve(target.path, target.type, property);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static AnimationClip SaveClip(AnimationClip temp, string path, bool overwrite)
        {
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
            {
                if (!overwrite) throw new InvalidOperationException("AnimationClip 已存在：" + path);
                EditorUtility.CopySerialized(temp, existing);
                existing.name = Path.GetFileNameWithoutExtension(path);
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(temp);
                return existing;
            }
            AssetDatabase.CreateAsset(temp, path);
            return temp;
        }

        private static AnimatorController BuildController(YBDissolveProfile profile, YBAnimatorFXAssets assets, string path, bool overwrite)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null)
            {
                if (!overwrite) throw new InvalidOperationException("AnimatorController 已存在：" + path);
                AssetDatabase.DeleteAsset(path);
            }

            AnimatorController c = AnimatorController.CreateAnimatorControllerAtPath(path);
            c.AddParameter(new AnimatorControllerParameter
            {
                name = profile.parameterName,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = profile.defaultOutfit == YBOutfitDefault.OutfitB
            });

            AnimatorControllerLayer layer = c.layers[0];
            layer.name = "YB Dissolve Outfit";
            layer.defaultWeight = 1f;
            AnimatorStateMachine sm = layer.stateMachine;
            sm.name = "YB Dissolve Outfit";

            AnimatorState aIdle = AddState(sm, "OutfitAIdle", assets.outfitAIdle, new Vector3(160, 90, 0));
            AnimatorState aToB = AddState(sm, "OutfitAToB", assets.outfitAToB, new Vector3(410, 90, 0));
            AnimatorState bIdle = AddState(sm, "OutfitBIdle", assets.outfitBIdle, new Vector3(660, 90, 0));
            AnimatorState bToA = AddState(sm, "OutfitBToA", assets.outfitBToA, new Vector3(410, 260, 0));
            sm.defaultState = profile.defaultOutfit == YBOutfitDefault.OutfitB ? bIdle : aIdle;

            AddBoolTransition(aIdle, aToB, true, profile.parameterName);
            AddExitTransition(aToB, bIdle);
            AddBoolTransition(bIdle, bToA, false, profile.parameterName);
            AddExitTransition(bToA, aIdle);

            EditorUtility.SetDirty(c);
            return c;
        }

        private static AnimatorState AddState(AnimatorStateMachine sm, string name, Motion motion, Vector3 position)
        {
            AnimatorState s = sm.AddState(name, position);
            s.motion = motion;
            s.writeDefaultValues = false;
            s.speed = 1f;
            return s;
        }

        private static void AddBoolTransition(AnimatorState from, AnimatorState to, bool value, string parameter)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = 0f;
            t.canTransitionToSelf = false;
            t.interruptionSource = TransitionInterruptionSource.None;
            t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameter);
        }

        private static void AddExitTransition(AnimatorState from, AnimatorState to)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = 1f;
            t.hasFixedDuration = true;
            t.duration = 0f;
            t.canTransitionToSelf = false;
            t.interruptionSource = TransitionInterruptionSource.None;
        }

        private static void SetLoopTime(AnimationClip clip, bool loop)
        {
            SerializedObject so = new SerializedObject(clip);
            SerializedProperty settings = so.FindProperty("m_AnimationClipSettings");
            if (settings != null)
            {
                SerializedProperty p = settings.FindPropertyRelative("m_LoopTime");
                if (p != null) p.boolValue = loop;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            clip.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
        }

        private static void SetLinearTangents(AnimationCurve curve)
        {
            if (curve == null) return;
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
        }

        private static void SetConstantTangents(AnimationCurve curve)
        {
            if (curve == null) return;
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            }
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Replace('\\', '/').Split('/');
            if (parts.Length == 0 || parts[0] != "Assets") throw new InvalidOperationException("输出目录必须位于 Assets 下。 ");
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
