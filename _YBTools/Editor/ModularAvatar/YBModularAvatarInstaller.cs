using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace YBTools.Dissolve.Editor
{
    internal sealed class YBModularAvatarResult
    {
        public bool success;
        public string message;
        public GameObject prefab;
        public GameObject installedObject;
    }

    internal static class YBModularAvatarInstaller
    {
        internal const string ContainerName = "溶解换装";
        internal const string LegacyContainerName = "__YB_Dissolve_MA__";
        internal const string PrefabFileName = "溶解换装.prefab";
        internal const string LegacyPrefabFileName = "YB_Dissolve_MA.prefab";
        private const string FxObjectName = "FX";
        private const string ActionObjectName = "Action";
        private const string MenuObjectName = "换装";
        private const string MergeAnimatorTypeName = "nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator";
        private const string ParametersTypeName = "nadena.dev.modular_avatar.core.ModularAvatarParameters";
        private const string MenuInstallerTypeName = "nadena.dev.modular_avatar.core.ModularAvatarMenuInstaller";
        private const string MenuItemTypeName = "nadena.dev.modular_avatar.core.ModularAvatarMenuItem";

        internal static string ValidateInputs(
            YBDissolveProfile profile,
            YBAnimatorFXAssets fx,
            YBAnimatorActionAssets action)
        {
            if (profile == null) return "MA 安装检查失败：Profile 为空。";
            if (fx == null || fx.controller == null) return "MA 安装检查失败：FX Controller 尚未生成。";
            if (string.IsNullOrWhiteSpace(profile.parameterName)) return "MA 安装检查失败：Bool 参数名为空。";
            if (string.IsNullOrWhiteSpace(profile.menuName)) return "MA 安装检查失败：菜单名称为空。";
            AnimatorControllerParameter fxParam = fx.controller.parameters.FirstOrDefault(p => p.name == profile.parameterName && p.type == AnimatorControllerParameterType.Bool);
            if (fxParam == null) return "MA 安装检查失败：FX Controller 缺少 Bool 参数 " + profile.parameterName + "。";
            if (profile.action != null && profile.action.enabled)
            {
                if (action == null || action.controller == null)
                    return "MA 安装检查失败：已启用换装动作，但 Action Controller 尚未生成。\n这正是 Controller 生成后动作仍不播放的常见原因：Action Controller 必须通过 MA Merge Animator 合并到 VRC Action Layer。";
                AnimatorControllerParameter ap = action.controller.parameters.FirstOrDefault(p => p.name == profile.parameterName && p.type == AnimatorControllerParameterType.Bool);
                if (ap == null) return "MA 安装检查失败：Action Controller 缺少 Bool 参数 " + profile.parameterName + "。";
            }

            string[] needed = { MergeAnimatorTypeName, ParametersTypeName, MenuInstallerTypeName, MenuItemTypeName };
            string missing = string.Join(", ", needed.Where(n => FindType(n) == null).Select(n => n.Split('.').Last()).ToArray());
            if (!string.IsNullOrEmpty(missing)) return "MA 安装检查失败：未检测到完整 Modular Avatar：" + missing;

            return "MA 安装检查通过：FX 将合并到 FX Layer" +
                   (profile.action != null && profile.action.enabled ? "；Action Controller 将合并到 Action Layer" : string.Empty) +
                   "；共用 Bool 参数 " + profile.parameterName + "；菜单 Toggle=" + profile.menuName + "。";
        }

        internal static YBModularAvatarResult BuildPrefabAndInstall(
            GameObject avatarRoot,
            YBDissolveProfile profile,
            YBAnimatorFXAssets fx,
            YBAnimatorActionAssets action,
            string outputRoot)
        {
            var result = new YBModularAvatarResult();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("请退出 Play Mode 后安装 Modular Avatar。");
                if (avatarRoot == null) throw new InvalidOperationException("Avatar Root 为空。");
                string check = ValidateInputs(profile, fx, action);
                if (check.IndexOf("通过", StringComparison.Ordinal) < 0) throw new InvalidOperationException(check);

                EnsureFolder(outputRoot.TrimEnd('/') + "/Prefab");
                string prefabPath = GetPrefabPath(outputRoot);
                GameObject temp = CreateConfiguredRoot(profile, fx, action);
                try
                {
                    GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
                    result.prefab = prefab;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(temp);
                }

                RemoveNamedInstalledRoots(avatarRoot);

                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabAsset == null) throw new InvalidOperationException("已保存 Prefab 但 AssetDatabase 无法重新读取：" + prefabPath);
                GameObject instance = PrefabUtility.InstantiatePrefab(prefabAsset, avatarRoot.scene) as GameObject;
                if (instance == null) throw new InvalidOperationException("无法实例化 MA Prefab：" + prefabPath);
                Undo.RegisterCreatedObjectUndo(instance, "Install YB Dissolve MA prefab");
                Undo.SetTransformParent(instance.transform, avatarRoot.transform, "Parent YB Dissolve MA");
                instance.name = ContainerName;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                EditorUtility.SetDirty(instance);
                if (instance.transform.parent != avatarRoot.transform)
                    throw new InvalidOperationException("Prefab 已生成，但实例未能挂到 Avatar Root。请检查 Avatar 是否处于不可编辑的 Prefab/Preview 状态。");
                result.installedObject = instance;

                MarkSceneDirty(avatarRoot);
                Selection.activeGameObject = instance;
                result.success = true;
                result.message = "MA 菜单预制件已生成并安装：\n" + prefabPath +
                                 "\nHierarchy：" + GetRelativePath(avatarRoot.transform, instance.transform) +
                                 "\nFX Merge=FX/Absolute；" +
                                 (profile.action != null && profile.action.enabled ? "Action Merge=Action/Absolute；" : string.Empty) +
                                 "Parameters=" + profile.parameterName + " Bool；Toggle=「" + profile.menuName + "」。";
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                result.message = "MA Prefab/菜单安装失败：" + ex.Message;
            }
            return result;
        }

        internal static string ValidateInstalled(GameObject avatarRoot, YBDissolveProfile profile, YBAnimatorFXAssets fx, YBAnimatorActionAssets action)
        {
            if (avatarRoot == null) return "MA 验证失败：Avatar Root 为空。";
            Transform root = FindInstalledRoot(avatarRoot);
            if (root == null)
                return "MA 验证失败：未找到「" + ContainerName + "」实例。\n" +
                       "已同时检查旧名称 " + LegacyContainerName + " 与 MA Parameters/Merge Animator 组件特征。\n" +
                       "请确认生成后 Hierarchy 的 Avatar Root 下确实存在该 Prefab 实例，而不只是 Project 中存在 Prefab 资产。";
            Type mergeType = FindType(MergeAnimatorTypeName);
            Type paramType = FindType(ParametersTypeName);
            Type installerType = FindType(MenuInstallerTypeName);
            Type itemType = FindType(MenuItemTypeName);
            if (mergeType == null || paramType == null || installerType == null || itemType == null)
                return "MA 验证失败：当前工程缺少 Modular Avatar 组件类型。";

            Component[] merges = root.GetComponentsInChildren(mergeType, true);
            Component fxMerge = merges.FirstOrDefault(m => string.Equals(Convert.ToString(GetFieldOrProperty(m, "layerType")), "FX", StringComparison.OrdinalIgnoreCase));
            Component actionMerge = merges.FirstOrDefault(m => string.Equals(Convert.ToString(GetFieldOrProperty(m, "layerType")), "Action", StringComparison.OrdinalIgnoreCase));
            if (fxMerge == null) return "MA 验证失败：缺少 FX Merge Animator。";
            RuntimeAnimatorController assignedFx = GetFieldOrProperty(fxMerge, "animator") as RuntimeAnimatorController;
            if (fx != null && fx.controller != null && assignedFx != fx.controller) return "MA 验证失败：FX Merge 引用错误。";

            bool actionEnabled = profile != null && profile.action != null && profile.action.enabled;
            if (actionEnabled)
            {
                if (actionMerge == null) return "MA 验证失败：已启用动作但缺少 Action Merge Animator，因此动作不会播放。";
                RuntimeAnimatorController assignedAction = GetFieldOrProperty(actionMerge, "animator") as RuntimeAnimatorController;
                if (action != null && action.controller != null && assignedAction != action.controller) return "MA 验证失败：Action Merge 引用错误。";
            }

            if (root.GetComponent(paramType) == null) return "MA 验证失败：缺少 MA Parameters。";
            Transform menu = root.Find(MenuObjectName);
            if (menu == null || menu.GetComponent(installerType) == null || menu.GetComponent(itemType) == null)
                return "MA 验证失败：缺少菜单 Installer/Toggle。";

            string legacyNote = root.name == LegacyContainerName ? "\n注意：当前仍是旧实例名 " + LegacyContainerName + "，建议重新执行一次 MA 安装迁移为「" + ContainerName + "」。" : string.Empty;
            return "MA 安装验证通过：「" + root.name + "」；FX Merge ✓" + (actionEnabled ? "；Action Merge ✓" : string.Empty) +
                   "；Parameters ✓；菜单 Toggle ✓。\n现在由同一个 " + profile.parameterName + " 同时驱动溶解与动作。" + legacyNote;
        }

        internal static string Uninstall(GameObject avatarRoot)
        {
            if (avatarRoot == null) return "Avatar Root 为空。";
            Transform root = FindInstalledRoot(avatarRoot);
            if (root == null) return "没有找到「" + ContainerName + "」或旧版 " + LegacyContainerName + "。";
            string removedName = root.name;
            Undo.DestroyObjectImmediate(root.gameObject);
            MarkSceneDirty(avatarRoot);
            return "已从 Avatar 移除「" + removedName + "」。生成的 Prefab/Controller/动画仍保留。";
        }

        internal static string GetPrefabPath(string outputRoot)
        {
            return outputRoot.TrimEnd('/') + "/Prefab/" + PrefabFileName;
        }

        internal static GameObject LoadGeneratedPrefab(string outputRoot)
        {
            string current = GetPrefabPath(outputRoot);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(current);
            if (prefab != null) return prefab;
            string legacy = outputRoot.TrimEnd('/') + "/Prefab/" + LegacyPrefabFileName;
            return AssetDatabase.LoadAssetAtPath<GameObject>(legacy);
        }

        internal static Transform FindInstalledRoot(GameObject avatarRoot)
        {
            if (avatarRoot == null) return null;
            Transform direct = avatarRoot.transform.Find(ContainerName);
            if (direct != null) return direct;
            direct = avatarRoot.transform.Find(LegacyContainerName);
            if (direct != null) return direct;

            Transform[] all = avatarRoot.GetComponentsInChildren<Transform>(true);
            Transform byName = all.FirstOrDefault(t => t != avatarRoot.transform &&
                (string.Equals(t.name, ContainerName, StringComparison.Ordinal) ||
                 string.Equals(t.name, LegacyContainerName, StringComparison.Ordinal)));
            if (byName != null) return byName;

            Type paramType = FindType(ParametersTypeName);
            Type mergeType = FindType(MergeAnimatorTypeName);
            if (paramType == null || mergeType == null) return null;
            foreach (Transform t in all)
            {
                if (t == avatarRoot.transform) continue;
                if (t.GetComponent(paramType) == null) continue;
                if (t.GetComponentsInChildren(mergeType, true).Length == 0) continue;
                Transform menu = t.Find(MenuObjectName);
                if (menu != null) return t;
            }
            return null;
        }

        private static void RemoveNamedInstalledRoots(GameObject avatarRoot)
        {
            if (avatarRoot == null) return;
            Transform[] all = avatarRoot.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all.Where(t => t != avatarRoot.transform &&
                     (string.Equals(t.name, ContainerName, StringComparison.Ordinal) ||
                      string.Equals(t.name, LegacyContainerName, StringComparison.Ordinal))).ToArray())
            {
                if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
            }
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null) return string.Empty;
            if (target == root) return root.name;
            var parts = new System.Collections.Generic.List<string>();
            Transform t = target;
            while (t != null && t != root)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return root.name + "/" + string.Join("/", parts.ToArray());
        }

        private static GameObject CreateConfiguredRoot(YBDissolveProfile profile, YBAnimatorFXAssets fx, YBAnimatorActionAssets action)
        {
            var root = new GameObject(ContainerName) { hideFlags = HideFlags.HideAndDontSave };
            root.hideFlags = HideFlags.None;
            Type mergeType = FindType(MergeAnimatorTypeName);
            Type paramType = FindType(ParametersTypeName);
            Type installerType = FindType(MenuInstallerTypeName);
            Type itemType = FindType(MenuItemTypeName);

            GameObject fxGo = new GameObject(FxObjectName);
            fxGo.transform.SetParent(root.transform, false);
            Component fxMerge = fxGo.AddComponent(mergeType);
            ConfigureMerge(fxMerge, fx.controller, "FX", false);

            if (profile.action != null && profile.action.enabled && action != null && action.controller != null)
            {
                GameObject actionGo = new GameObject(ActionObjectName);
                actionGo.transform.SetParent(root.transform, false);
                Component actionMerge = actionGo.AddComponent(mergeType);
                ConfigureMerge(actionMerge, action.controller, "Action", true);
            }

            Component parameters = root.AddComponent(paramType);
            ConfigureParameters(parameters, profile.parameterName, profile.defaultOutfit == YBOutfitDefault.OutfitB, true, true);

            GameObject menuGo = new GameObject(MenuObjectName);
            menuGo.transform.SetParent(root.transform, false);
            menuGo.AddComponent(installerType);
            Component menuItem = menuGo.AddComponent(itemType);
            ConfigureMenuItem(menuItem, profile.menuName, profile.parameterName, profile.defaultOutfit == YBOutfitDefault.OutfitB, true, true);
            return root;
        }

        private static void ConfigureMerge(Component merge, RuntimeAnimatorController controller, string layer, bool matchWriteDefaults)
        {
            SetFieldOrProperty(merge, "animator", controller, false);
            SetEnumField(merge, "layerType", layer, false);
            SetEnumField(merge, "pathMode", "Absolute", false);
            SetEnumField(merge, "mergeAnimatorMode", "Append", true);
            SetFieldOrProperty(merge, "deleteAttachedAnimator", false, true);
            SetFieldOrProperty(merge, "matchAvatarWriteDefaults", matchWriteDefaults, true);
            SetFieldOrProperty(merge, "layerPriority", 10, true);
            EditorUtility.SetDirty(merge);
        }

        private static void ConfigureParameters(Component parameters, string parameterName, bool defaultValue, bool saved, bool synced)
        {
            var so = new SerializedObject(parameters);
            SerializedProperty list = so.FindProperty("parameters");
            if (list == null || !list.isArray) throw new InvalidOperationException("MA Parameters 未找到 serialized field: parameters");
            list.arraySize = 1;
            SerializedProperty el = list.GetArrayElementAtIndex(0);
            SetRelativeString(el, "nameOrPrefix", parameterName);
            SetRelativeString(el, "remapTo", "", true);
            SetRelativeBool(el, "internalParameter", false);
            SetRelativeBool(el, "isPrefix", false, true);
            SetRelativeEnum(el, "syncType", "Bool");
            SetRelativeBool(el, "localOnly", !synced);
            SetRelativeFloat(el, "defaultValue", defaultValue ? 1f : 0f);
            SetRelativeBool(el, "saved", saved);
            SetRelativeBool(el, "hasExplicitDefaultValue", true);
            SetRelativeBool(el, "m_overrideAnimatorDefaults", true, true);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureMenuItem(Component menuItem, string label, string parameterName, bool defaultValue, bool saved, bool synced)
        {
            SetFieldOrProperty(menuItem, "isSynced", synced, true);
            SetFieldOrProperty(menuItem, "isSaved", saved, true);
            SetFieldOrProperty(menuItem, "isDefault", defaultValue, true);
            SetFieldOrProperty(menuItem, "automaticValue", false, true);
            SetFieldOrProperty(menuItem, "label", label, true);

            FieldInfo controlField = menuItem.GetType().GetField("Control", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (controlField == null) throw new InvalidOperationException("MA Menu Item 未找到 Control 字段。");
            object control = controlField.GetValue(menuItem) ?? Activator.CreateInstance(controlField.FieldType);
            Type ct = control.GetType();
            SetMemberOnObject(control, ct, "name", label, true);
            SetEnumMemberOnObject(control, ct, "type", "Toggle");
            SetMemberOnObject(control, ct, "value", 1f, false);

            FieldInfo parameterField = ct.GetField("parameter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (parameterField == null) throw new InvalidOperationException("VRCExpressionsMenu.Control 未找到 parameter 字段。");
            object parameter = parameterField.GetValue(control) ?? Activator.CreateInstance(parameterField.FieldType);
            SetMemberOnObject(parameter, parameter.GetType(), "name", parameterName, false);
            parameterField.SetValue(control, parameter);
            controlField.SetValue(menuItem, control);
            EditorUtility.SetDirty(menuItem);
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static object GetFieldOrProperty(object target, string name)
        {
            if (target == null) return null;
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(target);
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return p != null && p.CanRead ? p.GetValue(target, null) : null;
        }

        private static void SetFieldOrProperty(object target, string name, object value, bool optional)
        {
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) { f.SetValue(target, ConvertValue(value, f.FieldType)); return; }
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite) { p.SetValue(target, ConvertValue(value, p.PropertyType), null); return; }
            if (!optional) throw new MissingMemberException(t.FullName, name);
        }

        private static void SetEnumField(object target, string name, string enumName, bool optional)
        {
            Type t = target.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType.IsEnum) { f.SetValue(target, Enum.Parse(f.FieldType, enumName)); return; }
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite && p.PropertyType.IsEnum) { p.SetValue(target, Enum.Parse(p.PropertyType, enumName), null); return; }
            if (!optional) throw new MissingMemberException(t.FullName, name);
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null) return null;
            if (targetType.IsInstanceOfType(value)) return value;
            return Convert.ChangeType(value, targetType);
        }

        private static void SetRelativeString(SerializedProperty root, string name, string value, bool optional = false)
        {
            SerializedProperty p = root.FindPropertyRelative(name);
            if (p != null) p.stringValue = value; else if (!optional) throw new InvalidOperationException("MA 字段不存在：" + name);
        }
        private static void SetRelativeBool(SerializedProperty root, string name, bool value, bool optional = false)
        {
            SerializedProperty p = root.FindPropertyRelative(name);
            if (p != null) p.boolValue = value; else if (!optional) throw new InvalidOperationException("MA 字段不存在：" + name);
        }
        private static void SetRelativeFloat(SerializedProperty root, string name, float value, bool optional = false)
        {
            SerializedProperty p = root.FindPropertyRelative(name);
            if (p != null) p.floatValue = value; else if (!optional) throw new InvalidOperationException("MA 字段不存在：" + name);
        }
        private static void SetRelativeEnum(SerializedProperty root, string name, string enumName, bool optional = false)
        {
            SerializedProperty p = root.FindPropertyRelative(name);
            if (p == null) { if (!optional) throw new InvalidOperationException("MA 字段不存在：" + name); return; }
            int idx = Array.IndexOf(p.enumNames, enumName);
            if (idx < 0) idx = Array.FindIndex(p.enumDisplayNames, x => string.Equals(x, enumName, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) { if (!optional) throw new InvalidOperationException("MA 枚举 " + name + " 不含 " + enumName); return; }
            p.enumValueIndex = idx;
        }

        private static void SetMemberOnObject(object target, Type t, string name, object value, bool optional)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) { f.SetValue(target, ConvertValue(value, f.FieldType)); return; }
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite) { p.SetValue(target, ConvertValue(value, p.PropertyType), null); return; }
            if (!optional) throw new MissingMemberException(t.FullName, name);
        }
        private static void SetEnumMemberOnObject(object target, Type t, string name, string enumName)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType.IsEnum) { f.SetValue(target, Enum.Parse(f.FieldType, enumName)); return; }
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite && p.PropertyType.IsEnum) { p.SetValue(target, Enum.Parse(p.PropertyType, enumName), null); return; }
            throw new MissingMemberException(t.FullName, name);
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

        private static void MarkSceneDirty(GameObject avatarRoot)
        {
            if (avatarRoot != null && avatarRoot.scene.IsValid()) EditorSceneManager.MarkSceneDirty(avatarRoot.scene);
        }
    }
}
