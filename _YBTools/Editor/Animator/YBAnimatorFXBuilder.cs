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
        public AnimationClip[] idleClips;
        public AnimationClip[,] transitionClips;
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
            if (!manifestValidation.StartsWith("验证通过", StringComparison.Ordinal))
                return "Generation Manifest 与当前 Profile 不匹配：\n" + manifestValidation;

            List<Target> targets;
            string targetError;
            if (!TryBuildTargets(avatarRoot, manifest, installRecord, out targets, out targetError)) return targetError;
            int main = targets.Count(t => !t.overlay);
            int overlay = targets.Count(t => t.overlay);
            if (main == 0) return "没有可绑定的主 Renderer。";
            if (overlay == 0) return "没有找到 Overlay Renderer。请先验证当前安装。";

            if (profile.HasMultipleOutfits)
            {
                if (profile.OutfitCount > YBDissolveProfile.MaxOutfits)
                    return "服装数量超过上限 " + YBDissolveProfile.MaxOutfits + "。";
                var expected = Enumerable.Range(0, profile.OutfitCount).Select(YBDissolveProfile.OutfitGroup);
                var installed = targets.Where(t => !t.overlay).Select(t => t.outfitGroup).Distinct();
                if (!new HashSet<string>(expected, StringComparer.Ordinal).SetEquals(installed))
                    return "当前配置与已安装的服装组不一致，请重新分析、生成并安装服装资产。";
                if (installRecord.generationManifest != manifest ||
                    (DateTimeOffset.TryParse(manifest.generatedUtc, out var generatedAt) &&
                     DateTimeOffset.TryParse(installRecord.installedUtc, out var installedAt) && generatedAt > installedAt))
                    return "服装资产已更新，请卸载当前服装资产后重新安装。";
                return $"Animator-FX 输入检查通过：{profile.OutfitCount} 套服装，{profile.OutfitCount * (profile.OutfitCount - 1)} 个切换方向。\n" +
                       $"参数：{profile.parameterName} (Int)，默认：{profile.OutfitName(profile.DefaultOutfitIndex)}。";
            }

            if (targets.Any(t => t.outfitGroup != "Outfit A" && t.outfitGroup != "Outfit B"))
                return "已安装的服装组与当前双套配置不一致，请重新生成并安装服装资产。";

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
                if (!preflight.StartsWith("Animator-FX 输入检查通过", StringComparison.Ordinal))
                    throw new InvalidOperationException(preflight);

                List<Target> targets;
                string targetError;
                if (!TryBuildTargets(avatarRoot, manifest, installRecord, out targets, out targetError))
                    throw new InvalidOperationException(targetError);

                string animDir = outputRoot.TrimEnd('/') + "/Animations";
                string controllerDir = outputRoot.TrimEnd('/') + "/Animator";
                EnsureFolder(animDir);
                EnsureFolder(controllerDir);

                int count = profile.OutfitCount;
                float duration = Mathf.Max(0.01f, profile.scan.durationSeconds);
                float glowFade = Mathf.Min(GlowFadeDefault, Mathf.Max(0.02f, duration * 0.25f));
                string controllerPath = controllerDir + "/YB_Dissolve_FX.controller";
                var variantRecord = YBMaterialVariantBuilder.LoadExisting(outputRoot);
                bool hasVariants = variantRecord?.renderers?.Count > 0;

                result.idleClips = new AnimationClip[count];
                result.transitionClips = new AnimationClip[count, count];
                var paths = new List<string> { controllerPath };
                for (int from = 0; from < count; from++)
                {
                    paths.Add(animDir + "/" + ClipName(from, from) + ".anim");
                    for (int to = 0; to < count; to++)
                        if (from != to) paths.Add(animDir + "/" + ClipName(from, to) + ".anim");
                }
                foreach (string path in paths)
                {
                    UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
                    if (existing == null) continue;
                    bool matchesType = path == controllerPath ? existing is AnimatorController : existing is AnimationClip;
                    if (!matchesType || !overwrite)
                        throw new InvalidOperationException("FX 输出路径已有资产：" + path);
                }

                for (int from = 0; from < count; from++)
                {
                    result.idleClips[from] = BuildAndSaveClip(from, from, 0f, IdleLength);
                    for (int to = 0; to < count; to++)
                    {
                        if (from == to) continue;
                        float delay = profile.GetTransitionDelay(from, to);
                        result.transitionClips[from, to] = BuildAndSaveClip(from, to, delay, delay + duration);
                    }
                }

                AnimationClip BuildAndSaveClip(int from, int to, float delay, float length)
                {
                    string name = ClipName(from, to);
                    var clip = BuildClip(name, targets, profile, from, to, delay, length, glowFade);
                    if (hasVariants && profile.HasMultipleOutfits)
                        YBMaterialVariantBuilder.ApplyBindings(clip, variantRecord, from != 1, to != 1,
                            from != to && (from == 1 || to == 1) ? ScanCurve(profile, delay) : Constant(length, profile.scan.startHeight));
                    else if (hasVariants)
                        YBMaterialVariantBuilder.ApplyBindings(clip, variantRecord, from == 0);
                    return SaveClip(clip, animDir + "/" + name + ".anim", overwrite);
                }

                result.controller = BuildController(profile, result, controllerPath, overwrite);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                result.success = true;
                result.message = $"Animator-FX 已生成：{count} 个待机 Clip，{count * (count - 1)} 个切换 Clip。\n" + controllerPath;
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

        public static YBAnimatorFXAssets LoadExisting(string outputRoot, YBDissolveProfile profile)
        {
            var assets = new YBAnimatorFXAssets();
            if (string.IsNullOrEmpty(outputRoot) || profile == null || profile.OutfitCount > YBDissolveProfile.MaxOutfits)
                return assets;

            int count = profile.OutfitCount;
            string root = outputRoot.TrimEnd('/');
            assets.idleClips = new AnimationClip[count];
            assets.transitionClips = new AnimationClip[count, count];
            assets.controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(root + "/Animator/YB_Dissolve_FX.controller");
            for (int from = 0; from < count; from++)
            {
                assets.idleClips[from] = LoadClip(from, from);
                for (int to = 0; to < count; to++)
                    if (from != to) assets.transitionClips[from, to] = LoadClip(from, to);
            }

            AnimationClip LoadClip(int from, int to) =>
                AssetDatabase.LoadAssetAtPath<AnimationClip>(root + "/Animations/" + ClipName(from, to) + ".anim");

            assets.message = ValidateGenerated(profile, assets);
            assets.success = assets.message.StartsWith("Animator-FX 验证通过", StringComparison.Ordinal);
            return assets;
        }

        public static string ValidateGenerated(YBDissolveProfile profile, YBAnimatorFXAssets assets)
        {
            if (profile == null) return "Profile 为空。";
            int count = profile.OutfitCount;
            if (assets?.controller == null || assets.idleClips?.Length != count || assets.transitionClips?.GetLength(0) != count)
                return "Animator-FX 验证失败：FX 资产不完整，请重新生成。";
            if (!HasSelectionParameter(profile, assets.controller))
                return "Animator-FX 验证失败：Controller 的服装参数类型或默认值与 Profile 不一致，请重新生成。";

            var layer = assets.controller.layers.FirstOrDefault();
            var stateMachine = layer?.stateMachine;
            if (stateMachine == null || stateMachine.states.Length != count * count || stateMachine.defaultState == null ||
                stateMachine.defaultState?.motion != assets.idleClips[profile.DefaultOutfitIndex])
                return "Animator-FX 验证失败：Controller 的服装状态与 Profile 不一致，请重新生成。";

            var motions = new HashSet<Motion>(stateMachine.states.Select(s => s.state?.motion));
            for (int from = 0; from < count; from++)
            {
                if (assets.idleClips[from] == null || !motions.Contains(assets.idleClips[from]))
                    return "Animator-FX 验证失败：缺少待机 Clip " + ClipName(from, from);
                for (int to = 0; to < count; to++)
                {
                    if (from == to) continue;
                    AnimationClip clip = assets.transitionClips[from, to];
                    float expected = profile.GetTransitionDelay(from, to) + Mathf.Max(0.01f, profile.scan.durationSeconds);
                    if (clip == null || !motions.Contains(clip) || Mathf.Abs(clip.length - expected) > 0.05f)
                        return "Animator-FX 验证失败：切换 Clip 缺失或时长已过期 " + ClipName(from, to);
                }
            }
            return $"Animator-FX 验证通过：{count} 套服装，参数 {profile.parameterName}。";
        }

        private static string ClipName(int from, int to)
        {
            string prefix = "YB_Outfit" + (char)('A' + from);
            return from == to ? prefix + "_Idle" : prefix + "_To_" + (char)('A' + to);
        }

        internal static int SelectionValue(int index) => index + 1;

        internal static void AddSelectionParameter(AnimatorController controller, YBDissolveProfile profile)
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = profile.parameterName,
                type = profile.HasMultipleOutfits ? AnimatorControllerParameterType.Int : AnimatorControllerParameterType.Bool,
                defaultInt = SelectionValue(profile.DefaultOutfitIndex),
                defaultBool = profile.DefaultOutfitIndex == 1
            });
        }

        internal static bool HasSelectionParameter(YBDissolveProfile profile, AnimatorController controller)
        {
            return controller.parameters.Any(p => p.name == profile.parameterName &&
                (profile.HasMultipleOutfits
                    ? p.type == AnimatorControllerParameterType.Int && p.defaultInt == SelectionValue(profile.DefaultOutfitIndex)
                    : p.type == AnimatorControllerParameterType.Bool && p.defaultBool == (profile.DefaultOutfitIndex == 1)));
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
            int from,
            int to,
            float delay,
            float length,
            float glowFade)
        {
            var clip = new AnimationClip { name = name, frameRate = 60f, legacy = false };
            bool transition = from != to;
            float start = profile.scan.startHeight;
            string sourceGroup = YBDissolveProfile.OutfitGroup(from);
            string destinationGroup = YBDissolveProfile.OutfitGroup(to);
            AnimationCurve still = Constant(length, start);
            AnimationCurve scan = transition ? ScanCurve(profile, delay) : still;
            AnimationCurve glow = transition
                ? GlowCurve(delay, Mathf.Max(0.01f, length - delay), glowFade, profile.scan.scanIntensity)
                : Constant(length, 0f);
            AnimationCurve off = Constant(length, 0f);
            AnimationCurve on = Constant(length, 1f);

            foreach (Target t in targets)
            {
                bool source = t.outfitGroup == sourceGroup;
                bool participates = source || t.outfitGroup == destinationGroup;
                if (profile.HasMultipleOutfits)
                    SetCurve(clip, t, "m_Enabled", participates && (!t.overlay || transition) ? on : off);

                SetCurve(clip, t, "material._YBScanHeight", participates || !profile.HasMultipleOutfits ? scan : still);
                if (!t.overlay)
                {
                    SetCurve(clip, t, "material._YBScanRole", source ? off : on);
                    SetCurve(clip, t, "material._YBScanDissolveEnabled", on);
                }
                else
                {
                    SetCurve(clip, t, "material._YBScanIntensity", participates || !profile.HasMultipleOutfits ? glow : off);
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
            AddSelectionParameter(c, profile);

            AnimatorControllerLayer layer = c.layers[0];
            layer.name = "YB Dissolve Outfit";
            layer.defaultWeight = 1f;
            c.layers = new[] { layer };
            AnimatorStateMachine sm = layer.stateMachine;
            sm.name = layer.name;

            int count = profile.OutfitCount;
            var idle = new AnimatorState[count];
            for (int i = 0; i < count; i++)
                idle[i] = AddState(sm, "Outfit" + (char)('A' + i) + "Idle", assets.idleClips[i], new Vector3(180 + 250 * i, 90, 0));
            sm.defaultState = idle[profile.DefaultOutfitIndex];

            for (int from = 0; from < count; from++)
            {
                for (int to = 0; to < count; to++)
                {
                    if (from == to) continue;
                    AnimationClip clip = assets.transitionClips[from, to];
                    AnimatorState transition = AddState(sm, clip.name, clip, new Vector3(180 + 250 * to, 190 + 70 * from, 0));
                    AddSelectionTransition(idle[from], transition, profile, to);
                    AddExitTransition(transition, idle[to]);
                }
            }

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

        internal static void AddSelectionTransition(
            AnimatorState from, AnimatorState to, YBDissolveProfile profile, int outfitIndex, float blend = 0f)
        {
            if (!profile.HasMultipleOutfits)
            {
                AddTransition(from, to, false, blend).AddCondition(
                    outfitIndex == 1 ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, profile.parameterName);
                return;
            }

            AddTransition(from, to, false, blend).AddCondition(
                AnimatorConditionMode.Equals, SelectionValue(outfitIndex), profile.parameterName);
            // Turning off a VRChat Toggle writes zero. Use that value to return to the configured default.
            if (outfitIndex == profile.DefaultOutfitIndex)
                AddTransition(from, to, false, blend).AddCondition(AnimatorConditionMode.Equals, 0f, profile.parameterName);
        }

        internal static void AddExitTransition(AnimatorState from, AnimatorState to, float blend = 0f)
        {
            AddTransition(from, to, true, blend);
        }

        private static AnimatorStateTransition AddTransition(AnimatorState from, AnimatorState to, bool exit, float blend)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = exit;
            if (exit) t.exitTime = 1f;
            t.hasFixedDuration = true;
            t.duration = Mathf.Max(0f, blend);
            t.canTransitionToSelf = false;
            t.interruptionSource = TransitionInterruptionSource.None;
            return t;
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
