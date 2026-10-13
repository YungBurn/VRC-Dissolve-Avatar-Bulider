using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YBTools.Dissolve.Editor
{
    internal sealed class YBAnimatorActionAssets
    {
        public AnimatorController controller;
        public bool success;
        public string message;
    }

    internal static class YBAnimatorActionBuilder
    {
        private const string ControllerName = "YB_Dissolve_Action.controller";

        internal static string ValidateInputs(GameObject avatarRoot, YBDissolveProfile profile)
        {
            if (avatarRoot == null) return "Action 输入检查失败：Avatar Root 为空。";
            if (profile == null) return "Action 输入检查失败：Profile 为空。";
            if (profile.action == null || !profile.action.enabled) return "Action 未启用。";
            if (profile.action.outfitAToB == null) return "Action 输入检查失败：未选择 服装 A → B 动作 AnimationClip。";
            AnimationClip reverse = ResolveReverse(profile);
            if (reverse == null) return "Action 输入检查失败：未选择 服装 B → A 动作，且未启用复用 A→B 动作。";
            if (profile.action.outfitAToB.legacy) return "Action 输入检查失败：A→B 动作是 Legacy AnimationClip。";
            if (reverse.legacy) return "Action 输入检查失败：B→A 动作是 Legacy AnimationClip。";
            if (string.IsNullOrWhiteSpace(profile.parameterName)) return "Action 输入检查失败：Profile Bool 参数名为空。";

            Type playable = FindVrcBehaviourType("VRCPlayableLayerControl");
            if (playable == null)
                return "Action 输入检查失败：未检测到 VRCPlayableLayerControl。需要安装 VRChat Avatars SDK。";
            if (profile.action.controlTracking && FindVrcBehaviourType("VRCAnimatorTrackingControl") == null)
                return "Action 输入检查失败：未检测到 VRCAnimatorTrackingControl。";
            if (profile.action.disableLocomotion && FindVrcBehaviourType("VRCAnimatorLocomotionControl") == null)
                return "Action 输入检查失败：未检测到 VRCAnimatorLocomotionControl。";

            Animator animator = avatarRoot.GetComponent<Animator>();
            string humanoid = animator != null && animator.avatar != null && animator.avatar.isHuman
                ? "Humanoid ✓"
                : "未确认 Humanoid（请在实际 Avatar 中测试）";
            string humanMotionWarning = (!profile.action.outfitAToB.humanMotion || !reverse.humanMotion)
                ? "\n⚠ 至少一个动作不是 Unity 标记的 Humanoid humanMotion。Controller 仍可生成，但 VRChat Action 层的身体动作可能不符合预期。"
                : string.Empty;

            float fxAToB = profile.scan.GetDelayAToB() + Mathf.Max(0.01f, profile.scan.durationSeconds);
            float fxBToA = profile.scan.GetDelayBToA() + Mathf.Max(0.01f, profile.scan.durationSeconds);
            string timingNote = string.Empty;
            if (Mathf.Abs(profile.action.outfitAToB.length - fxAToB) > 0.05f ||
                Mathf.Abs(reverse.length - fxBToA) > 0.05f)
            {
                timingNote = "\n提示：Action 与 FX 总时长不完全一致。可在‘效果’页分别点击‘让扫描结束对齐动作’。";
            }

            return string.Format(
                "Action 输入检查通过：{0}\n" +
                "A→B 动作={1} ({2:0.###}s)，FX={3:0.###}s；B→A 动作={4} ({5:0.###}s)，FX={6:0.###}s；Blend={7:0.###}s。\n" +
                "动作 State：Action Weight=1{8}{9}；Idle：Action Weight=0。{10}{11}",
                humanoid,
                profile.action.outfitAToB.name, profile.action.outfitAToB.length, fxAToB,
                reverse.name, reverse.length, fxBToA,
                Mathf.Max(0f, profile.action.blendDuration),
                profile.action.controlTracking ? "；Tracking=Animation" : string.Empty,
                profile.action.disableLocomotion ? "；Locomotion Disabled" : string.Empty,
                humanMotionWarning, timingNote);
        }

        internal static YBAnimatorActionAssets Build(
            GameObject avatarRoot,
            YBDissolveProfile profile,
            string outputRoot,
            bool overwrite)
        {
            var result = new YBAnimatorActionAssets();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("请退出 Play Mode 后生成 Action Controller。");
                string validation = ValidateInputs(avatarRoot, profile);
                if (validation.IndexOf("通过", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException(validation);

                string dir = outputRoot.TrimEnd('/') + "/Animator";
                EnsureFolder(dir);
                string path = dir + "/" + ControllerName;
                AnimatorController existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (existing != null)
                {
                    if (!overwrite) throw new InvalidOperationException("Action Controller 已存在：" + path);
                    AssetDatabase.DeleteAsset(path);
                }

                AnimationClip forward = profile.action.outfitAToB;
                AnimationClip reverse = ResolveReverse(profile);
                AnimatorController c = AnimatorController.CreateAnimatorControllerAtPath(path);
                c.AddParameter(new AnimatorControllerParameter
                {
                    name = profile.parameterName,
                    type = AnimatorControllerParameterType.Bool,
                    defaultBool = profile.defaultOutfit == YBOutfitDefault.OutfitB
                });

                AnimatorControllerLayer layer = c.layers[0];
                layer.name = "YB Dissolve Action";
                layer.defaultWeight = 1f;
                layer.blendingMode = AnimatorLayerBlendingMode.Override;
                c.layers = new[] { layer };

                AnimatorStateMachine sm = layer.stateMachine;
                sm.name = "YB Dissolve Action";
                AnimatorState aIdle = AddState(sm, "OutfitAIdle / ActionOff", null, new Vector3(140, 90, 0));
                AnimatorState aToB = AddState(sm, "Action A → B", forward, new Vector3(410, 90, 0));
                AnimatorState bIdle = AddState(sm, "OutfitBIdle / ActionOff", null, new Vector3(680, 90, 0));
                AnimatorState bToA = AddState(sm, "Action B → A", reverse, new Vector3(410, 270, 0));
                sm.defaultState = profile.defaultOutfit == YBOutfitDefault.OutfitB ? bIdle : aIdle;

                ConfigureIdle(aIdle, profile);
                ConfigureMotion(aToB, profile);
                ConfigureIdle(bIdle, profile);
                ConfigureMotion(bToA, profile);

                float blend = Mathf.Max(0f, profile.action.blendDuration);
                AddBoolTransition(aIdle, aToB, true, profile.parameterName, blend);
                AddExitTransition(aToB, bIdle, blend);
                AddBoolTransition(bIdle, bToA, false, profile.parameterName, blend);
                AddExitTransition(bToA, aIdle, blend);

                EditorUtility.SetDirty(c);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                result.controller = c;
                result.success = true;
                result.message = "Action Controller 已生成/更新：\n" + path +
                                 "\n\n结构：Idle Action Weight=0；动作 State Action Weight=1。" +
                                 (profile.action.controlTracking ? "\n动作期间 Tracking=Animation，Idle 恢复 Tracking。" : string.Empty) +
                                 (profile.action.disableLocomotion ? "\n动作期间禁用 Locomotion，Idle 恢复。" : string.Empty) +
                                 "\n参数：" + profile.parameterName + "。完成后在 Modular Avatar 区域安装 Action Merge Animator。";
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                result.success = false;
                result.message = "Action Controller 生成失败：" + ex.Message;
            }
            return result;
        }

        internal static YBAnimatorActionAssets LoadExisting(string outputRoot)
        {
            var r = new YBAnimatorActionAssets();
            if (string.IsNullOrEmpty(outputRoot)) return r;
            string path = outputRoot.TrimEnd('/') + "/Animator/" + ControllerName;
            r.controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            r.success = r.controller != null;
            r.message = r.success ? "已识别现有 Action Controller。" : "Action Controller 尚未生成。";
            return r;
        }

        internal static string ValidateGenerated(YBDissolveProfile profile, YBAnimatorActionAssets assets)
        {
            if (profile == null) return "Profile 为空。";
            if (profile.action == null || !profile.action.enabled) return "Action 未启用，无需验证 Action Controller。";
            if (assets == null || assets.controller == null) return "Action Controller 验证失败：未找到 YB_Dissolve_Action.controller。";

            AnimatorControllerParameter param = assets.controller.parameters.FirstOrDefault(
                p => p.name == profile.parameterName && p.type == AnimatorControllerParameterType.Bool);
            if (param == null) return "Action Controller 验证失败：缺少 Bool 参数 " + profile.parameterName + "。";

            AnimatorControllerLayer layer = assets.controller.layers.FirstOrDefault();
            if (layer == null || layer.stateMachine == null) return "Action Controller 验证失败：缺少 Animator Layer。";
            AnimatorState[] states = layer.stateMachine.states.Select(s => s.state).Where(s => s != null).ToArray();
            AnimatorState[] motions = states.Where(s => s.motion != null).ToArray();
            if (motions.Length < 2) return "Action Controller 验证失败：动作 State 少于 2 个。";

            Type playable = FindVrcBehaviourType("VRCPlayableLayerControl");
            if (playable != null)
            {
                foreach (AnimatorState s in motions)
                {
                    if (!s.behaviours.Any(b => b != null && playable.IsInstanceOfType(b)))
                        return "Action Controller 验证失败：动作 State 缺少 VRCPlayableLayerControl：" + s.name;
                }
            }
            return "Action Controller 验证通过：参数=" + profile.parameterName +
                   "；动作 State=" + motions.Length + "；Write Defaults Off；VRC Action 权重控制已写入。";
        }

        private static AnimationClip ResolveReverse(YBDissolveProfile profile)
        {
            if (profile == null || profile.action == null) return null;
            if (profile.action.outfitBToA != null) return profile.action.outfitBToA;
            return profile.action.reuseForwardClipWhenReverseMissing ? profile.action.outfitAToB : null;
        }

        private static AnimatorState AddState(AnimatorStateMachine sm, string name, Motion motion, Vector3 position)
        {
            AnimatorState s = sm.AddState(name, position);
            s.motion = motion;
            s.writeDefaultValues = false;
            s.speed = 1f;
            return s;
        }

        private static void ConfigureIdle(AnimatorState state, YBDissolveProfile profile)
        {
            AddPlayableLayerControl(state, 0f, profile.action.blendDuration);
            if (profile.action.controlTracking) AddTrackingControl(state, "Tracking");
            if (profile.action.disableLocomotion) AddLocomotionControl(state, false);
        }

        private static void ConfigureMotion(AnimatorState state, YBDissolveProfile profile)
        {
            AddPlayableLayerControl(state, 1f, profile.action.blendDuration);
            if (profile.action.controlTracking) AddTrackingControl(state, "Animation");
            if (profile.action.disableLocomotion) AddLocomotionControl(state, true);
        }

        private static void AddPlayableLayerControl(AnimatorState state, float weight, float blend)
        {
            Type t = RequireVrcBehaviourType("VRCPlayableLayerControl");
            StateMachineBehaviour b = state.AddStateMachineBehaviour(t);
            SetEnumMember(b, "layer", "Action");
            SetMember(b, "goalWeight", weight);
            SetMember(b, "blendDuration", Mathf.Max(0f, blend));
            EditorUtility.SetDirty(b);
        }

        private static void AddTrackingControl(AnimatorState state, string bodyMode)
        {
            Type t = RequireVrcBehaviourType("VRCAnimatorTrackingControl");
            StateMachineBehaviour b = state.AddStateMachineBehaviour(t);
            string[] body = { "trackingHead", "trackingLeftHand", "trackingRightHand", "trackingHip", "trackingLeftFoot", "trackingRightFoot", "trackingLeftFingers", "trackingRightFingers" };
            for (int i = 0; i < body.Length; i++) SetEnumMember(b, body[i], bodyMode);
            SetEnumMember(b, "trackingEyes", "NoChange");
            SetEnumMember(b, "trackingMouth", "NoChange");
            EditorUtility.SetDirty(b);
        }

        private static void AddLocomotionControl(AnimatorState state, bool disable)
        {
            Type t = RequireVrcBehaviourType("VRCAnimatorLocomotionControl");
            StateMachineBehaviour b = state.AddStateMachineBehaviour(t);
            SetMember(b, "disableLocomotion", disable);
            EditorUtility.SetDirty(b);
        }

        private static void AddBoolTransition(AnimatorState from, AnimatorState to, bool expected, string parameter, float blend)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = Mathf.Max(0f, blend);
            t.canTransitionToSelf = false;
            t.interruptionSource = TransitionInterruptionSource.None;
            t.AddCondition(expected ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameter);
        }

        private static void AddExitTransition(AnimatorState from, AnimatorState to, float blend)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = 1f;
            t.hasFixedDuration = true;
            t.duration = Mathf.Max(0f, blend);
            t.canTransitionToSelf = false;
            t.interruptionSource = TransitionInterruptionSource.None;
        }

        private static Type RequireVrcBehaviourType(string simpleName)
        {
            Type t = FindVrcBehaviourType(simpleName);
            if (t == null)
                throw new InvalidOperationException(
                    "未找到 VRChat StateMachineBehaviour：" + simpleName +
                    "。请确认已安装 VRChat Avatars SDK。当前版本会同时兼容 VRC.SDK3.Avatars.Components 与旧命名空间。 ");
            return t;
        }

        private static Type FindVrcBehaviourType(string simpleName)
        {
            if (string.IsNullOrEmpty(simpleName)) return null;

            // Avatar SDK 中这些 StateMachineBehaviour 位于 VRC.SDK3.Avatars.Components。
            // 一些旧代码/文档会同时引用 VRC.SDKBase，因此保留旧命名空间 fallback，
            // 最后再按简单类名扫描已加载程序集，避免 SDK 小版本移动类型后再次误判。
            string[] fullNames =
            {
                "VRC.SDK3.Avatars.Components." + simpleName,
                "VRC.SDKBase." + simpleName
            };

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int n = 0; n < fullNames.Length; n++)
            {
                string fullName = fullNames[n];
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Type t = assemblies[i].GetType(fullName, false);
                    if (t != null && typeof(StateMachineBehaviour).IsAssignableFrom(t)) return t;
                }
            }

            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types;
                try { types = assemblies[i].GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                if (types == null) continue;
                for (int j = 0; j < types.Length; j++)
                {
                    Type t = types[j];
                    if (t == null || t.Name != simpleName) continue;
                    if (typeof(StateMachineBehaviour).IsAssignableFrom(t)) return t;
                }
            }
            return null;
        }

        private static void SetMember(object target, string name, object value)
        {
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                f.SetValue(target, ConvertValue(value, f.FieldType));
                return;
            }
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite)
            {
                p.SetValue(target, ConvertValue(value, p.PropertyType), null);
                return;
            }
            throw new MissingMemberException(t.FullName, name);
        }

        private static void SetEnumMember(object target, string name, string enumName)
        {
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType.IsEnum)
            {
                f.SetValue(target, Enum.Parse(f.FieldType, enumName));
                return;
            }
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite && p.PropertyType.IsEnum)
            {
                p.SetValue(target, Enum.Parse(p.PropertyType, enumName), null);
                return;
            }
            throw new MissingMemberException(t.FullName, name);
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null) return null;
            if (targetType.IsInstanceOfType(value)) return value;
            return Convert.ChangeType(value, targetType);
        }

        private static void EnsureFolder(string fullPath)
        {
            string path = fullPath.Replace('\\', '/').TrimEnd('/');
            string[] parts = path.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets")
                throw new InvalidOperationException("无效 Asset 路径：" + fullPath);
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
