using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace YBTools.Dissolve.Editor
{
    public sealed class YBDissolveBuilderWindow : EditorWindow
    {
        private const string LanguagePrefKey = "YBTools.DissolveBuilderWindow.UILanguage";
        private static readonly string[] LanguageNames = { "中文", "English", "日本語" };
        private int uiLanguage; // EditorPrefs-backed, not stored in Avatar/Profile assets.

        private string L(string chinese)
        {
            return YBWindowLocalization.Translate(chinese, uiLanguage);
        }

        // Display-only diagnostics: underlying Chinese strings stay unchanged for validation logic.
        private string D(string diagnostic)
        {
            return YBWindowLocalization.TranslateDiagnostic(diagnostic, uiLanguage);
        }

        private static readonly string[] Tabs =
        {
            "环境", "Avatar", "分析", "效果", "材质变体", "生成/安装", "Animator/MA", "验证"
        };

        [SerializeField] private int tab;
        [SerializeField] private GameObject avatarRoot;
        [SerializeField] private YBDissolveProfile profile;
        [SerializeField] private string newProfileName = "OutfitA_OutfitB";
        [SerializeField] private List<GameObject> outfitARoots = new List<GameObject>();
        [SerializeField] private List<GameObject> outfitBRoots = new List<GameObject>();
        [SerializeField] private bool showOnlyIssues;
        [SerializeField] private float scanRangeEstimateMargin = 0.05f;
        [SerializeField] private bool allowOverwriteUv4;
        [SerializeField] private bool showGenerationDetails;
        [SerializeField] private bool showInstallDetails;
        // Editor-only presentation state. The profile, generation and installation behavior is unchanged.
        [SerializeField] private bool showEnvironmentHelp;
        [SerializeField] private bool showAvatarHelp;
        [SerializeField] private bool showScanHelp;
        [SerializeField] private bool showActionHelp;
        [SerializeField] private bool showVariantHelp;
        [SerializeField] private bool showVariantAssetDetails;
        [SerializeField] private bool showGenerationHelp;
        [SerializeField] private bool showShaderHelp;
        [SerializeField] private bool showSpecialShaderStatus;
        [SerializeField] private bool showUnsupportedMaterialHelp;
        [SerializeField] private bool showInstallHelp;
        [SerializeField] private bool showAnimatorHelp;
        [SerializeField] private bool showFxAssetDetails;
        [SerializeField] private bool showMAHelp;
        [SerializeField] private bool animatorOverwriteExisting = true;
        [SerializeField] private bool previewRuntimeRebind = true;
        [SerializeField] private bool previewRunOriginalController = false;

        private Vector2 scroll;
        private YBDependencyStatus dependencies;
        private YBAnalysisReport report;
        private YBGeneratedAssetManifest generationManifest;
        private string generationStatus;
        private YBAvatarInstallRecord installRecord;
        private string installStatus;
        private YBAnimatorFXAssets animatorAssets;
        private YBAnimatorActionAssets actionAssets;
        private YBMaterialVariantInstallRecord materialVariantRecord;
        private string materialVariantStatus;
        private string maStatus;
        private string animatorStatus;
        private string actionStatus;
        private string validationStatus;
        private string manifestResolveStatus;
        private string unresolvedManifestPath;
        private string generatedAssetResolveContextKey;
        private bool generatedAssetResolveAttempted;

        [MenuItem("YBTools/溶解/Dissolve Avatar Builder", priority = 1)]
        public static void Open()
        {
            var window = GetWindow<YBDissolveBuilderWindow>("YB Dissolve Builder");
            window.minSize = new Vector2(720f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            // Default to Chinese. EditorPrefs keeps the choice between window/editor sessions.
            uiLanguage = Mathf.Clamp(EditorPrefs.GetInt(LanguagePrefKey, 0), 0, LanguageNames.Length - 1);
            dependencies = YBDependencyDetector.Detect();
            TryPickAvatarFromSelection();
        }

        private void OnDisable()
        {
            if (YBAnimatorFXPreview.Active) YBAnimatorFXPreview.Stop();
        }

        private void OnGUI()
        {
            YBProfileUtility.EnsureCurrentDataVersion(profile);
            DrawHeader();
            string[] localizedTabs = new string[Tabs.Length];
            for (int i = 0; i < Tabs.Length; i++) localizedTabs[i] = L(Tabs[i]);
            int next = GUILayout.Toolbar(tab, localizedTabs, GUILayout.Height(26));
            if (next != tab)
            {
                tab = next;
                GUI.FocusControl(null);
            }

            EditorGUILayout.Space(6);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case 0: DrawEnvironmentPage(); break;
                case 1: DrawAvatarPage(); break;
                case 2: DrawAnalysisPage(); break;
                case 3: DrawProfileScanSettingsPreview(); break;
                case 4: DrawMaterialVariantsPage(); break;
                case 5: DrawGenerationPage(); break;
                case 6: DrawAnimatorMAPage(); break;
                case 7: DrawValidationPage(); break;
                default: DrawFuturePage(); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("YB Dissolve Avatar Builder", EditorStyles.boldLabel, GUILayout.MinWidth(178));
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(L("语言"), GUILayout.Width(65));
                int nextLanguage = GUILayout.Toolbar(uiLanguage, LanguageNames, GUILayout.Width(210));
                if (nextLanguage != uiLanguage)
                {
                    uiLanguage = nextLanguage;
                    EditorPrefs.SetInt(LanguagePrefKey, uiLanguage);
                    GUI.FocusControl(null);
                    Repaint();
                }
                EditorGUILayout.LabelField("v" + YBToolVersion.Version, GUILayout.Width(55));
            }
        }

        private static void DrawHelpFoldout(ref bool expanded, string title, string detail)
        {
            expanded = EditorGUILayout.Foldout(expanded, title, true);
            if (expanded)
                EditorGUILayout.HelpBox(detail, MessageType.Info);
        }

        private void DrawEnvironmentPage()
        {
            EditorGUILayout.LabelField(L("1. 环境检查"), EditorStyles.largeLabel);

            YBEditorGUI.Section(L("Dependencies"));
            YBEditorGUI.StatusRow("lilToon", dependencies != null && dependencies.lilToon,
                L("已安装"), L("未检测到 Packages/jp.lilxyzw.liltoon"));
            YBEditorGUI.StatusRow("FinalBaked Shaders", dependencies != null && dependencies.finalBakedShaders,
                L("4 个 Shader 已加载"), L("Shader 未加载"));
            YBEditorGUI.StatusRow("VRC SDK Avatars", dependencies != null && dependencies.vrcSdkAvatars,
                L("已检测"), L("未检测（当前分析页仍可使用）"));
            YBEditorGUI.StatusRow("Modular Avatar", dependencies != null && dependencies.modularAvatar,
                L("已检测"), L("未检测（MA 页后续会禁用）"));

            EditorGUILayout.Space(8);
            if (GUILayout.Button(L("重新检测环境"), GUILayout.Height(30)))
            {
                dependencies = YBDependencyDetector.Detect();
                Repaint();
            }

            DrawHelpFoldout(ref showEnvironmentHelp, L("环境与扫描数据说明"),
                L("检查 lilToon、VRC SDK、Modular Avatar 和 FinalBaked Shader。\n") +
                L("扫描方向使用 Avatar Root 本地坐标，投影结果写入 UV4.x；现有 Shader 仍按 UV4.x 读取。"));
        }

        private void DrawAvatarPage()
        {
            EditorGUILayout.LabelField(L("2. Avatar / Outfit 配置"), EditorStyles.largeLabel);
            avatarRoot = (GameObject)EditorGUILayout.ObjectField("Avatar Root", avatarRoot, typeof(GameObject), true);
            profile = (YBDissolveProfile)EditorGUILayout.ObjectField(L("当前 Profile"), profile, typeof(YBDissolveProfile), false);

            DrawHelpFoldout(ref showAvatarHelp, L("Profile 与 Outfit Root 说明"),
                L("Profile 记录服装 Root 路径、扫描、动作、材质变体和菜单设置。\n") +
                L("Root 可包含服装、发型或配饰等对象，分析时会递归收集 Renderer 并去重。\n") +
                L("Profile 只存相对 Avatar Root 的路径，不保存场景对象引用。"));

            using (new EditorGUILayout.HorizontalScope())
            {
                newProfileName = EditorGUILayout.TextField(L("新 Profile 名称"), newProfileName);
                using (new EditorGUI.DisabledScope(avatarRoot == null))
                {
                    if (GUILayout.Button(L("创建 Profile"), GUILayout.Width(100))) CreateProfile();
                }
            }

            EditorGUILayout.Space(4);
            int defaultIndex = profile != null && profile.defaultOutfit == YBOutfitDefault.OutfitB ? 1 : 0;
            int newDefaultIndex = EditorGUILayout.Popup(L("默认服装"), defaultIndex, new[] { L("服装 A"), L("服装 B") });
            if (profile != null && newDefaultIndex != defaultIndex)
            {
                Undo.RecordObject(profile, "Change default outfit");
                profile.defaultOutfit = newDefaultIndex == 0 ? YBOutfitDefault.OutfitA : YBOutfitDefault.OutfitB;
                EditorUtility.SetDirty(profile);
            }

            DrawRootList(L("服装 A Roots（默认服装 A）"), outfitARoots);
            DrawRootList(L("服装 B Roots（切换服装 B）"), outfitBRoots);

            YBEditorGUI.Section(L("Profile Scene Binding"));
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(profile == null || avatarRoot == null))
                {
                    if (GUILayout.Button(L("保存当前 Root 路径到 Profile")))
                        YBProfileUtility.SaveSceneBindings(profile, avatarRoot, outfitARoots, outfitBRoots);
                    if (GUILayout.Button(L("从 Profile 恢复 Root"))) LoadProfileBindings();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(avatarRoot == null))
                {
                    if (GUILayout.Button(L("自动查找当前 Avatar 的 Profile"))) FindProfileForCurrentAvatar();
                }
                if (GUILayout.Button(L("从当前 Selection 识别 Avatar"))) TryPickAvatarFromSelection(true);
                using (new EditorGUI.DisabledScope(avatarRoot == null))
                {
                    if (GUILayout.Button(L("创建 _YBToolsGenerated 输出目录"))) EnsureGeneratedFolders();
                }
            }

        }

        private void DrawAnalysisPage()
        {
            EditorGUILayout.LabelField(L("3. 自动分析 / Preflight"), EditorStyles.largeLabel);
            using (new EditorGUI.DisabledScope(avatarRoot == null))
            {
                if (GUILayout.Button(L("分析 Avatar / Outfit"), GUILayout.Height(34)))
                    report = YBAvatarAnalyzer.Analyze(avatarRoot, outfitARoots, outfitBRoots);
            }

            if (report == null)
            {
                EditorGUILayout.HelpBox(L("尚未运行分析。先在 Avatar 页设置 Outfit A / B Roots。"), MessageType.Info);
                return;
            }

            DrawAnalysisSummary();

            showOnlyIssues = EditorGUILayout.ToggleLeft(L("Renderer 列表只显示有问题的项目"), showOnlyIssues);
            EditorGUILayout.Space(4);

            for (int i = 0; i < report.renderers.Count; i++)
            {
                var r = report.renderers[i];
                bool issue = !r.meshReadable || r.uv4Occupied || r.maPathRisk || r.ambiguousFingerprint || HasMaterialIssue(r);
                if (showOnlyIssues && !issue) continue;
                DrawRendererAnalysis(r);
            }

            YBEditorGUI.Section(L("Messages"));
            for (int i = 0; i < report.messages.Count; i++) DrawMessage(report.messages[i]);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L("导出分析报告.txt"))) ExportAnalysisReport();
                if (GUILayout.Button(L("清空报告"))) report = null;
            }
        }

        private void DrawFuturePage()
        {
            EditorGUILayout.HelpBox(L("当前功能已全部整合到顶部页面。"), MessageType.Info);
        }


        private void DrawProfileScanSettingsPreview()
        {
            if (profile == null)
            {
                EditorGUILayout.HelpBox(L("请先在 Avatar 页创建或选择当前 Profile。"), MessageType.Warning);
                return;
            }

            YBEditorGUI.Section(L("扫描效果参数"));
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(L("当前 Profile"), profile.name);

                EditorGUI.BeginChangeCheck();
                bool changedByButton = false;

                YBEditorGUI.Section(L("扫描方向"));
                string[] directionNames =
                {
                    L("从上到下"),
                    L("从下到上"),
                    L("从左到右"),
                    L("从右到左"),
                    L("从前到后"),
                    L("从后到前"),
                    L("自定义")
                };
                int directionIndex = Mathf.Clamp((int)profile.scan.directionPreset, 0, directionNames.Length - 1);
                directionIndex = EditorGUILayout.Popup(
                    new GUIContent(L("扫描移动方向"), L("方向使用 Avatar Root 本地坐标。改变方向后需要重新生成 Baked Mesh 并重新安装。")),
                    directionIndex, directionNames);
                YBScanDirectionPreset directionPreset = (YBScanDirectionPreset)directionIndex;
                Vector3 customDirection = profile.scan.customDirection;
                if (directionPreset == YBScanDirectionPreset.Custom)
                {
                    customDirection = EditorGUILayout.Vector3Field(
                        new GUIContent(L("自定义方向"), L("表示扫描线实际移动方向，例如 (1,-1,0) 表示向右下移动。")),
                        customDirection);
                }

                Vector3 moveDirection = ResolveScanMoveDirection(directionPreset, customDirection);
                EditorGUILayout.LabelField(L("Avatar 本地移动向量"),
                    string.Format("({0:0.###}, {1:0.###}, {2:0.###})", moveDirection.x, moveDirection.y, moveDirection.z));

                YBEditorGUI.Section(L("扫描范围"));
                float startHeight = EditorGUILayout.FloatField(
                    new GUIContent(L("扫描起始坐标"), L("沿当前扫描方向对应的烘焙坐标起点。建议使用自动估算。")),
                    profile.scan.startHeight);
                float endHeight = EditorGUILayout.FloatField(
                    new GUIContent(L("扫描结束坐标"), L("沿当前扫描方向对应的烘焙坐标终点。建议使用自动估算。")),
                    profile.scan.endHeight);

                EditorGUILayout.LabelField(L("扫描距离"), Mathf.Abs(startHeight - endHeight).ToString("0.###"));

                using (new EditorGUILayout.HorizontalScope())
                {
                    scanRangeEstimateMargin = Mathf.Max(0f, EditorGUILayout.FloatField(
                        new GUIContent(L("自动估算余量"), L("在 Outfit A/B 的投影范围两端追加的安全余量。")),
                        scanRangeEstimateMargin));
                    using (new EditorGUI.DisabledScope(
                               avatarRoot == null ||
                               moveDirection.sqrMagnitude < 0.000001f ||
                               (outfitARoots.Count == 0 && outfitBRoots.Count == 0)))
                    {
                        if (GUILayout.Button(L("按当前方向自动估算"), GUILayout.Width(uiLanguage == 1 ? 235 : 180)))
                        {
                            float estimatedStart;
                            float estimatedEnd;
                            if (TryEstimateScanRange(moveDirection, out estimatedStart, out estimatedEnd))
                            {
                                startHeight = estimatedStart;
                                endHeight = estimatedEnd;
                                changedByButton = true;
                                GUI.FocusControl(null);
                            }
                            else
                            {
                                EditorUtility.DisplayDialog(
                                    L("无法估算扫描范围"),
                                    L("没有找到可用于估算的 Renderer Bounds。"),
                                    L("确定"));
                            }
                        }
                    }
                }

                EditorGUILayout.Space(4);
                YBEditorGUI.Section(L("时间与速度"));
                float durationSeconds = Mathf.Max(0.01f, EditorGUILayout.FloatField(
                    new GUIContent(L("扫描持续时间（秒）"), L("两次换装共用同一个扫描持续时间。")),
                    profile.scan.durationSeconds));

                float delayAToB = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent(L("服装 A → B 扫描前延迟"), L("切换到服装 B 后等待多久开始扫描。")),
                    profile.scan.GetDelayAToB()));
                float delayBToA = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent(L("服装 B → A 扫描前延迟"), L("切换回服装 A 后等待多久开始扫描。")),
                    profile.scan.GetDelayBToA()));

                AnimationClip forwardAction = profile.action != null ? profile.action.outfitAToB : null;
                AnimationClip reverseAction = ResolveReverseActionClip(profile);
                if (profile.action != null && profile.action.enabled)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            L("A→B 动作长度"),
                            forwardAction != null ? forwardAction.length.ToString("0.###") + L(" 秒") : L("未选择"));
                        using (new EditorGUI.DisabledScope(forwardAction == null))
                        {
                            if (GUILayout.Button(L("让扫描结束对齐动作"), GUILayout.Width(uiLanguage == 1 ? 208 : 160)))
                            {
                                delayAToB = Mathf.Max(0f, forwardAction.length - durationSeconds);
                                changedByButton = true;
                            }
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(
                            L("B→A 动作长度"),
                            reverseAction != null ? reverseAction.length.ToString("0.###") + L(" 秒") : L("未选择"));
                        using (new EditorGUI.DisabledScope(reverseAction == null))
                        {
                            if (GUILayout.Button(L("让扫描结束对齐动作"), GUILayout.Width(uiLanguage == 1 ? 208 : 160)))
                            {
                                delayBToA = Mathf.Max(0f, reverseAction.length - durationSeconds);
                                changedByButton = true;
                            }
                        }
                    }
                }

                float distance = Mathf.Abs(startHeight - endHeight);
                float avgSpeed = durationSeconds > 0f ? distance / durationSeconds : 0f;
                EditorGUILayout.LabelField(L("平均扫描速度"), avgSpeed.ToString("0.###") + L(" 坐标单位/秒"));

                int curveIndex = (int)profile.scan.curvePreset;
                string[] curveNames = { L("线性"), L("缓入"), L("缓出"), L("缓入缓出"), L("自定义曲线") };
                curveIndex = EditorGUILayout.Popup(
                    new GUIContent(L("扫描速度曲线"), L("控制扫描坐标随时间变化的节奏。")),
                    Mathf.Clamp(curveIndex, 0, curveNames.Length - 1), curveNames);
                YBScanCurvePreset curvePreset = (YBScanCurvePreset)curveIndex;
                AnimationCurve customCurve = profile.scan.customCurve;
                if (curvePreset == YBScanCurvePreset.Custom)
                {
                    customCurve = EditorGUILayout.CurveField(
                        L("自定义曲线"),
                        customCurve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f));
                }

                EditorGUILayout.Space(4);
                YBEditorGUI.Section(L("扫描发光"));
                Color scanColor = EditorGUILayout.ColorField(L("扫描颜色"), profile.scan.scanColor);
                float scanIntensity = Mathf.Max(0f, EditorGUILayout.FloatField(L("发光强度"), profile.scan.scanIntensity));
                float scanWidth = Mathf.Max(0f, EditorGUILayout.FloatField(L("扫描带宽度"), profile.scan.scanWidth));
                float scanSoftness = Mathf.Max(0f, EditorGUILayout.FloatField(L("边缘柔和度"), profile.scan.scanSoftness));
                float noiseStrength = Mathf.Max(0f, EditorGUILayout.FloatField(L("溶解噪声强度"), profile.scan.noiseStrength));

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(L("恢复默认参数")))
                    {
                        directionPreset = YBScanDirectionPreset.TopToBottom;
                        customDirection = Vector3.down;
                        startHeight = 1.5f;
                        endHeight = -0.15f;
                        delayAToB = 6f;
                        delayBToA = 6f;
                        durationSeconds = 7f;
                        curvePreset = YBScanCurvePreset.Linear;
                        customCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                        scanColor = new Color(0.05f, 0.85f, 1f, 1f);
                        scanIntensity = 4f;
                        scanWidth = 0.08f;
                        scanSoftness = 0.08f;
                        noiseStrength = 0.04f;
                        changedByButton = true;
                    }
                    if (GUILayout.Button(L("快速扫描预设")))
                    {
                        delayAToB = 0f;
                        delayBToA = 0f;
                        durationSeconds = 2f;
                        curvePreset = YBScanCurvePreset.Linear;
                        changedByButton = true;
                    }
                }

                if (directionPreset == YBScanDirectionPreset.Custom && customDirection.sqrMagnitude < 0.000001f)
                    EditorGUILayout.HelpBox(L("自定义扫描方向不能为 (0,0,0)。"), MessageType.Error);
                if (Mathf.Approximately(startHeight, endHeight))
                    EditorGUILayout.HelpBox(L("扫描起始坐标与结束坐标相同。"), MessageType.Error);
                if (startHeight < endHeight)
                    EditorGUILayout.HelpBox(L("当前起始坐标小于结束坐标，实际扫描移动会与所选方向相反。建议点击“按当前方向自动估算”。"), MessageType.Warning);

                if (EditorGUI.EndChangeCheck() || changedByButton)
                {
                    Undo.RecordObject(profile, "修改 YB 扫描效果参数");
                    profile.dataVersion = YBToolVersion.ProfileDataVersion;
                    profile.scan.directionPreset = directionPreset;
                    profile.scan.customDirection = customDirection;
                    profile.scan.startHeight = startHeight;
                    profile.scan.endHeight = endHeight;
                    profile.scan.SetDirectionalDelays(delayAToB, delayBToA);
                    profile.scan.durationSeconds = durationSeconds;
                    profile.scan.curvePreset = curvePreset;
                    profile.scan.customCurve = customCurve;
                    profile.scan.scanColor = scanColor;
                    profile.scan.scanIntensity = scanIntensity;
                    profile.scan.scanWidth = scanWidth;
                    profile.scan.scanSoftness = scanSoftness;
                    profile.scan.noiseStrength = noiseStrength;
                    EditorUtility.SetDirty(profile);
                }
            }

            EditorGUILayout.LabelField(L("提示：更改扫描方向后需重新生成并安装；材质变体也要重建。"), EditorStyles.miniLabel);
            DrawHelpFoldout(ref showScanHelp, L("扫描数据与方向说明"),
                L("扫描值烘焙在 Mesh 的 UV4.x，方向以 Avatar Root 本地坐标为准。\n") +
                L("调整方向后，原 Baked Mesh 与材质变体需要重新生成、安装。"));

            DrawActionEffectSettings();
        }

        private void DrawActionEffectSettings()
        {
            YBEditorGUI.Section(L("换装动作（可选 / VRC Action Layer）"));
            DrawHelpFoldout(ref showActionHelp, L("Action 动作说明"),
                L("A→B / B→A 可分别指定动作和扫描前延迟。\n") +
                L("Action Controller 负责 Action Weight、Tracking 与 Locomotion 控制。"));

            if (profile.action == null)
            {
                Undo.RecordObject(profile, "初始化 YB Action 设置");
                profile.action = new YBActionSettings();
                EditorUtility.SetDirty(profile);
            }

            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.ToggleLeft(L("启用换装动作"), profile.action.enabled);
            AnimationClip forward = profile.action.outfitAToB;
            AnimationClip reverse = profile.action.outfitBToA;
            bool reuse = profile.action.reuseForwardClipWhenReverseMissing;
            bool tracking = profile.action.controlTracking;
            bool locomotion = profile.action.disableLocomotion;
            float blend = profile.action.blendDuration;

            using (new EditorGUI.DisabledScope(!enabled))
            {
                forward = (AnimationClip)EditorGUILayout.ObjectField(
                    new GUIContent(L("服装 A → B 动作"), L("切换到服装 B 时立即播放的身体动作 AnimationClip。")),
                    forward, typeof(AnimationClip), false);
                reverse = (AnimationClip)EditorGUILayout.ObjectField(
                    new GUIContent(L("服装 B → A 动作"), L("可留空；勾选复用时会使用同一个 A→B 动作。")),
                    reverse, typeof(AnimationClip), false);
                reuse = EditorGUILayout.ToggleLeft(L("B→A 未指定时复用 A→B 动作"), reuse);
                tracking = EditorGUILayout.ToggleLeft(L("动作期间由 Animation 接管身体 Tracking"), tracking);
                locomotion = EditorGUILayout.ToggleLeft(L("动作期间禁止 Locomotion（全身动作推荐）"), locomotion);
                blend = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent(L("Action 混合时间（秒）"), L("Action Weight / State 转换的混合时间。")),
                    blend));

                AnimationClip resolvedReverse = reverse != null ? reverse : (reuse ? forward : null);
                if (forward != null)
                    EditorGUILayout.LabelField(L("A→B 动作长度"), forward.length.ToString("0.###") + L(" 秒"));
                if (resolvedReverse != null)
                    EditorGUILayout.LabelField(L("B→A 动作长度"), resolvedReverse.length.ToString("0.###") + L(" 秒"));
                if (forward != null && forward.legacy)
                    EditorGUILayout.HelpBox(L("A→B AnimationClip 是 Legacy，Action Controller 不支持。"), MessageType.Error);
                if (resolvedReverse != null && resolvedReverse.legacy)
                    EditorGUILayout.HelpBox(L("B→A AnimationClip 是 Legacy，Action Controller 不支持。"), MessageType.Error);
                if (forward != null && !forward.humanMotion)
                    EditorGUILayout.HelpBox(L("A→B 动作不是 Unity 标记的 Humanoid humanMotion。可以生成，但 VRChat 全身动作可能不正确。"), MessageType.Warning);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(profile, "修改 YB 换装动作设置");
                profile.action.enabled = enabled;
                profile.action.outfitAToB = forward;
                profile.action.outfitBToA = reverse;
                profile.action.reuseForwardClipWhenReverseMissing = reuse;
                profile.action.controlTracking = tracking;
                profile.action.disableLocomotion = locomotion;
                profile.action.blendDuration = blend;
                EditorUtility.SetDirty(profile);
            }
        }

        private void DrawMaterialVariantsPage()
        {
            EditorGUILayout.LabelField(L("5. 材质变体"), EditorStyles.largeLabel);
            if (avatarRoot == null || profile == null)
            {
                EditorGUILayout.HelpBox(L("请先在 Avatar 页选择 Avatar Root 与 Profile。"), MessageType.Warning);
                return;
            }

            DrawHelpFoldout(ref showVariantHelp, L("材质变体原理与使用说明"),
                L("同一个 SkinnedMeshRenderer 可在 A/B 材质间扫描切换，不会新建第二个 Renderer；BlendShape、Viseme 和 EyeLook 仍由原 Renderer 控制。\n") +
                L("安装变体后，要到 Animator/MA 页重新生成 FX，以写入四段材质切换曲线。"));
            showVariantAssetDetails = EditorGUILayout.Foldout(showVariantAssetDetails, L("显示路径与材质资产详情"), true);

            if (profile.materialVariants == null)
            {
                Undo.RecordObject(profile, "初始化材质变体列表");
                profile.materialVariants = new List<YBMaterialVariantProfile>();
                EditorUtility.SetDirty(profile);
            }

            for (int i = 0; i < profile.materialVariants.Count; i++)
            {
                YBMaterialVariantProfile v = profile.materialVariants[i] ?? (profile.materialVariants[i] = new YBMaterialVariantProfile());
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(L("材质变体 ") + (i + 1), EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(L("删除"), GUILayout.Width(54)))
                        {
                            Undo.RecordObject(profile, "删除材质变体");
                            profile.materialVariants.RemoveAt(i--);
                            EditorUtility.SetDirty(profile);
                            continue;
                        }
                    }

                    SkinnedMeshRenderer current = ResolveVariantRenderer(v.rendererPath);
                    EditorGUI.BeginChangeCheck();
                    SkinnedMeshRenderer selected = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                        L("目标 SkinnedMeshRenderer"), current, typeof(SkinnedMeshRenderer), true);
                    int slot = Mathf.Max(0, EditorGUILayout.IntField(L("变化 Material Slot"), v.materialSlot));
                    Material a = (Material)EditorGUILayout.ObjectField(L("服装 A Material"), v.outfitAMaterial, typeof(Material), false);
                    Material b = (Material)EditorGUILayout.ObjectField(L("服装 B Material"), v.outfitBMaterial, typeof(Material), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(profile, "修改材质变体");
                        if (selected != current)
                        {
                            string rp = selected != null ? YBPathUtility.GetRelativePath(avatarRoot.transform, selected.transform) : string.Empty;
                            v.rendererPath = rp;
                            if (selected != null && a == null && slot < selected.sharedMaterials.Length) a = selected.sharedMaterials[slot];
                        }
                        v.materialSlot = slot;
                        v.outfitAMaterial = a;
                        v.outfitBMaterial = b;
                        EditorUtility.SetDirty(profile);
                    }

                    if (showVariantAssetDetails)
                    {
                        EditorGUILayout.LabelField(L("Renderer Path"), string.IsNullOrEmpty(v.rendererPath) ? L("(未设置)") : v.rendererPath);
                        if (selected != null)
                        {
                            EditorGUILayout.LabelField(L("当前材质槽"), selected.sharedMaterials.Length.ToString());
                            EditorGUILayout.LabelField(L("当前 SubMesh"), selected.sharedMesh != null ? selected.sharedMesh.subMeshCount.ToString() : "0");
                        }
                        if (a != null) EditorGUILayout.LabelField(L("A 资产"), AssetDatabase.GetAssetPath(a));
                        if (b != null) EditorGUILayout.LabelField(L("B 资产"), AssetDatabase.GetAssetPath(b));
                    }
                }
            }

            if (GUILayout.Button(L("+ 添加 Face / Body 材质变体")))
            {
                Undo.RecordObject(profile, "添加材质变体");
                profile.materialVariants.Add(new YBMaterialVariantProfile());
                EditorUtility.SetDirty(profile);
            }

            string root = YBPathUtility.GetOrCreateProfileOutputRoot(avatarRoot, profile, newProfileName);
            EnsureGeneratedAssetsResolvedOnce();
            YBEditorGUI.Section(L("检查 / 正式安装"));
            string check = YBMaterialVariantBuilder.ValidateInputs(avatarRoot, profile, installRecord);
            MessageType mt = check.IndexOf("通过", StringComparison.Ordinal) >= 0 ? MessageType.Info :
                (check.IndexOf("没有配置", StringComparison.Ordinal) >= 0 ? MessageType.None : MessageType.Error);
            EditorGUILayout.HelpBox(D(check), mt);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || check.IndexOf("通过", StringComparison.Ordinal) < 0))
            {
                if (GUILayout.Button(L("生成并正式安装材质变体"), GUILayout.Height(34)))
                {
                    YBMaterialVariantBuildResult r = YBMaterialVariantBuilder.BuildAndInstall(
                        avatarRoot, profile, installRecord, root, allowOverwriteUv4);
                    materialVariantRecord = r.record;
                    materialVariantStatus = r.message;
                    animatorAssets = null;
                }
            }

            materialVariantRecord = materialVariantRecord != null ? materialVariantRecord : YBMaterialVariantBuilder.LoadExisting(root);
            if (materialVariantRecord != null)
            {
                EditorGUILayout.ObjectField(L("Material Variant Record"), materialVariantRecord, typeof(YBMaterialVariantInstallRecord), false);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(L("验证材质变体安装")))
                        materialVariantStatus = YBMaterialVariantBuilder.ValidateInstalled(avatarRoot, materialVariantRecord);
                    if (GUILayout.Button(L("恢复材质变体安装")))
                    {
                        if (EditorUtility.DisplayDialog(L("恢复材质变体"), L("恢复 Face/Body 的原 Mesh / Material；生成资产保留。"), L("恢复"), L("取消")))
                        {
                            materialVariantStatus = YBMaterialVariantBuilder.Restore(avatarRoot, root);
                            materialVariantRecord = null;
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(materialVariantStatus)) EditorGUILayout.HelpBox(D(materialVariantStatus), MessageType.Info);
        }

        private SkinnedMeshRenderer ResolveVariantRenderer(string relativePath)
        {
            if (avatarRoot == null || string.IsNullOrEmpty(relativePath)) return null;
            Transform t = avatarRoot.transform.Find(relativePath);
            if (t != null)
            {
                SkinnedMeshRenderer r = t.GetComponent<SkinnedMeshRenderer>();
                if (r != null) return r;
            }
            string name = relativePath.Contains("/") ? relativePath.Substring(relativePath.LastIndexOf('/') + 1) : relativePath;
            var all = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer found = null;
            int count = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name != name) continue;
                found = all[i]; count++;
            }
            return count == 1 ? found : null;
        }

        private void DrawGenerationPage()
        {
            EditorGUILayout.LabelField(L("6. 生成 / 安装"), EditorStyles.largeLabel);
            DrawHelpFoldout(ref showGenerationHelp, L("生成与安装流程说明"),
                L("先生成 Baked Mesh、Dissolve/Overlay 材质和 Manifest（仅写入 _YBToolsGenerated，不修改 Avatar）。\n") +
                L("再正式安装到 Avatar，并保存可恢复的 InstallRecord。"));

            if (avatarRoot == null || profile == null)
            {
                EditorGUILayout.HelpBox(L("请先在 Avatar 页选择 Avatar Root，并创建/选择当前 Profile。"), MessageType.Warning);
                return;
            }

            if (report == null)
            {
                EditorGUILayout.HelpBox(L("尚未有分析结果。生成前必须先运行分析。"), MessageType.Warning);
                if (GUILayout.Button(L("立即分析 Avatar / Outfit"), GUILayout.Height(30)))
                    report = YBAvatarAnalyzer.Analyze(avatarRoot, outfitARoots, outfitBRoots);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L("重新分析当前 Avatar")))
                    report = YBAvatarAnalyzer.Analyze(avatarRoot, outfitARoots, outfitBRoots);
                if (GUILayout.Button(L("定位生成目录")))
                {
                    string root = YBPathUtility.GetOrCreateProfileOutputRoot(avatarRoot, profile, newProfileName);
                    UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(root);
                    if (folder != null) EditorGUIUtility.PingObject(folder);
                }
            }

            YBEditorGUI.Section(L("lilToon Rendering Mode / 特殊 Shader"));
            DrawHelpFoldout(ref showShaderHelp, L("支持模式与 Shader 检查说明"),
                L("支持 Opaque、Cutout、Transparent、Refraction、Refraction Blur、Fur、Fur Cutout、Fur TwoPass 和 Gem。\n") +
                L("安装前会检查 Shader 的加载和编译状态。"));
            showSpecialShaderStatus = EditorGUILayout.Foldout(showSpecialShaderStatus, L("查看特殊 Shader 逐项状态"), true);
            if (showSpecialShaderStatus) DrawSpecialShaderStatusTable();
            string specialShaderStatus = YBGeneratedAssetBuilder.ValidateSpecialShaderAvailability(report);
            if (string.IsNullOrEmpty(specialShaderStatus))
                EditorGUILayout.HelpBox(L("Shader 检查通过。"), MessageType.Info);
            else
                EditorGUILayout.HelpBox(D(specialShaderStatus), MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L("重新导入 Special Shader")))
                {
                    AssetDatabase.ImportAsset("Assets/_YBTools/Shader/FinalBaked/Special", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
                    AssetDatabase.Refresh();
                }
                if (GUILayout.Button(L("定位 Special Shader 目录")))
                {
                    var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/_YBTools/Shader/FinalBaked/Special");
                    if (folder != null) EditorGUIUtility.PingObject(folder);
                }
            }

            YBEditorGUI.Section(L("仍未支持 / 自定义材质处理"));
            DrawHelpFoldout(ref showUnsupportedMaterialHelp, L("未支持材质处理说明"),
                L("FakeShadow、Tessellation、非 lilToon 及未知派生 Shader 默认阻止生成。\n") +
                L("可跳过该材质槽，或自行承担视觉风险进行实验性模式映射。"));
            DrawUnsupportedMaterialOverrides();

            YBEditorGUI.Section(L("Baked Mesh 设置"));
            allowOverwriteUv4 = EditorGUILayout.ToggleLeft(
                L("允许覆盖已存在的 UV4/TEXCOORD3（危险，默认关闭）"), allowOverwriteUv4);
            if (allowOverwriteUv4)
                EditorGUILayout.HelpBox(L("开启后只改写 UV4.x 为 Avatar Root 本地空间中沿扫描轴的投影值，并尽量保留 UV4.yzw。确认原 Shader/工具不依赖 UV4.x 后再使用。"), MessageType.Warning);

            string preflight = YBGeneratedAssetBuilder.ValidatePreflight(profile, report, allowOverwriteUv4);
            if (string.IsNullOrEmpty(preflight))
                EditorGUILayout.HelpBox(L("生成前检查通过。"), MessageType.Info);
            else
                EditorGUILayout.HelpBox(D(preflight), MessageType.Error);

            YBEditorGUI.Section(L("A. 生成资产（不安装）"));
            using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(preflight) || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button(L("生成/刷新 Baked Mesh + Dissolve/Overlay（不安装）"), GUILayout.Height(38)))
                {
                    string root = YBPathUtility.GetOrCreateProfileOutputRoot(avatarRoot, profile, newProfileName);
                    var generated = YBGeneratedAssetBuilder.Generate(avatarRoot, profile, report, root, allowOverwriteUv4);
                    generationStatus = generated.message;
                    generationManifest = generated.manifest;
                    installRecord = YBGeneratedAssetResolver.ResolveInstallRecord(root);
                    if (generated.success && generationManifest != null)
                    {
                        Selection.activeObject = generationManifest;
                        EditorGUIUtility.PingObject(generationManifest);
                    }
                    EditorUtility.DisplayDialog(generated.success ? L("生成完成") : L("生成失败"), D(generated.message), L("确定"));
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L("加载已有 Generation Manifest"))) LoadGenerationManifest();
                using (new EditorGUI.DisabledScope(generationManifest == null))
                {
                    if (GUILayout.Button(L("验证已生成资产")))
                        generationStatus = YBGeneratedAssetBuilder.ValidateManifest(generationManifest);
                }
            }

            if (!string.IsNullOrEmpty(generationStatus))
                EditorGUILayout.HelpBox(D(generationStatus),
                    generationStatus.IndexOf("失败", StringComparison.Ordinal) >= 0 ? MessageType.Error : MessageType.Info);

            if (generationManifest != null)
            {
                showGenerationDetails = EditorGUILayout.Foldout(showGenerationDetails, L("Generation Manifest 摘要"), true);
                if (showGenerationDetails)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.ObjectField(L("Manifest"), generationManifest, typeof(YBGeneratedAssetManifest), false);
                        EditorGUILayout.LabelField(L("源 Renderer"), generationManifest.sourceRendererCount.ToString());
                        EditorGUILayout.LabelField(L("Baked Mesh"), generationManifest.generatedMeshCount.ToString());
                        EditorGUILayout.LabelField(L("Dissolve 材质"), generationManifest.generatedDissolveMaterialCount.ToString());
                        EditorGUILayout.LabelField(L("Overlay 材质"), generationManifest.generatedOverlayMaterialCount.ToString());
                        EditorGUILayout.LabelField(L("跳过 Slot"), generationManifest.skippedSlotCount.ToString());
                        EditorGUILayout.LabelField(L("生成工具版本"), generationManifest.toolVersion ?? string.Empty);
                        EditorGUILayout.LabelField(L("生成时间 UTC"), generationManifest.generatedUtc ?? string.Empty);
                    }
                }
            }

            EditorGUILayout.Space(12);
            YBEditorGUI.Section(L("B. 正式安装到 Avatar（会修改场景）"));
            EditorGUILayout.HelpBox(L("安装会修改当前 Avatar 的 Mesh、材质及启用状态；请先保存场景。"), MessageType.Warning);
            DrawHelpFoldout(ref showInstallHelp, L("安装改动与恢复范围"),
                L("安装时替换 Mesh 与支持的材质槽，启用所需的 Outfit 对象和 Renderer，创建 __YBFinalOverlay__。\n") +
                L("YBInstallRecord 记录原状态，可卸载恢复。动画、FX/Action Controller 与 MA 菜单需另行生成。"));

            string outputRoot = YBPathUtility.GetOrCreateProfileOutputRoot(avatarRoot, profile, newProfileName);
            EnsureGeneratedAssetsResolvedOnce();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L("加载安装记录")))
                {
                    installRecord = YBGeneratedAssetResolver.ResolveInstallRecord(outputRoot);
                    installStatus = installRecord != null ? L("已加载 YBInstallRecord。") : L("未找到 YBInstallRecord，当前 Profile 尚未正式安装。");
                }
                using (new EditorGUI.DisabledScope(installRecord == null))
                {
                    if (GUILayout.Button(L("定位安装记录")))
                    {
                        Selection.activeObject = installRecord;
                        EditorGUIUtility.PingObject(installRecord);
                    }
                }
            }

            if (installRecord == null)
            {
                string installPreflight = generationManifest == null
                    ? L("请先生成或加载 Generation Manifest。")
                    : YBAvatarInstaller.ValidatePreflight(avatarRoot, profile, generationManifest, outputRoot);

                if (string.IsNullOrEmpty(installPreflight))
                    EditorGUILayout.HelpBox(L("安装前检查通过。"), MessageType.Info);
                else
                    EditorGUILayout.HelpBox(D(installPreflight), MessageType.Error);

                using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(installPreflight) || EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button(L("正式安装 Baked Mesh + Dissolve + Overlay"), GUILayout.Height(42)))
                    {
                        if (EditorUtility.DisplayDialog(L("正式安装"),
                            L("这一步会修改当前 Avatar 场景对象，但会创建可逆 InstallRecord。\n\n") +
                            L("建议先保存场景。是否继续？"), L("正式安装"), L("取消")))
                        {
                            var installed = YBAvatarInstaller.Install(avatarRoot, profile, generationManifest, outputRoot);
                            installStatus = installed.message;
                            installRecord = installed.record;
                            if (installed.success && installRecord != null)
                            {
                                Selection.activeObject = installRecord;
                                EditorGUIUtility.PingObject(installRecord);
                            }
                            EditorUtility.DisplayDialog(installed.success ? L("安装完成") : L("安装失败"), D(installed.message), L("确定"));
                        }
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox(L("检测到当前 Profile 已安装。不要重复安装；请先验证，若需重新生成/安装请先卸载恢复。"), MessageType.Info);
                showInstallDetails = EditorGUILayout.Foldout(showInstallDetails, L("YBInstallRecord 摘要"), true);
                if (showInstallDetails)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.ObjectField(L("Install Record"), installRecord, typeof(YBAvatarInstallRecord), false);
                        EditorGUILayout.LabelField(L("已安装 Renderer"), installRecord.installedRendererCount.ToString());
                        EditorGUILayout.LabelField(L("材质槽"), installRecord.installedMaterialSlotCount.ToString());
                        EditorGUILayout.LabelField(L("Overlay Renderer"), installRecord.overlayRendererCount.ToString());
                        EditorGUILayout.LabelField(L("安装工具版本"), installRecord.toolVersion ?? string.Empty);
                        EditorGUILayout.LabelField(L("安装时间 UTC"), installRecord.installedUtc ?? string.Empty);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(L("验证当前安装（Edit/Play 自动适配）"), GUILayout.Height(32)))
                    {
                        installStatus = YBAvatarInstaller.ValidateInstalled(avatarRoot, installRecord);
                        EditorUtility.DisplayDialog(L("安装验证"), D(installStatus), L("确定"));
                    }
                    using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                    {
                        if (GUILayout.Button(L("卸载并恢复安装前状态"), GUILayout.Height(32)))
                        {
                            if (EditorUtility.DisplayDialog(L("卸载并恢复"),
                                L("将删除 __YBFinalOverlay__，恢复安装前的 Mesh、材质、Renderer.enabled 与由安装器开启的 GameObject。\n") +
                                L("不会删除 _YBToolsGenerated 中的 Baked/Dissolve/Overlay 主资产。是否继续？"),
                                L("卸载并恢复"), L("取消")))
                            {
                                var restored = YBAvatarInstaller.Uninstall(avatarRoot, installRecord, outputRoot);
                                installStatus = restored.message;
                                if (restored.success) installRecord = null;
                                EditorUtility.DisplayDialog(restored.success ? L("恢复完成") : L("恢复失败"), D(restored.message), L("确定"));
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(installStatus))
                EditorGUILayout.HelpBox(D(installStatus),
                    installStatus.IndexOf("失败", StringComparison.Ordinal) >= 0 || installStatus.IndexOf("不一致", StringComparison.Ordinal) >= 0
                        ? MessageType.Error : MessageType.Info);
        }

        private void DrawSpecialShaderStatusTable()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(L("特殊 Rendering Mode Shader 状态"), EditorStyles.boldLabel);
                DrawSpecialShaderStatusLine(L("折射"), YBMaterialRenderMode.Refraction, false);
                DrawSpecialShaderStatusLine(L("折射模糊"), YBMaterialRenderMode.RefractionBlur, false);
                DrawSpecialShaderStatusLine(L("毛发"), YBMaterialRenderMode.Fur, false);
                DrawSpecialShaderStatusLine(L("毛发（镂空）"), YBMaterialRenderMode.FurCutout, false);
                DrawSpecialShaderStatusLine(L("毛发（2 pass）"), YBMaterialRenderMode.FurTwoPass, true);
                DrawSpecialShaderStatusLine(L("宝石"), YBMaterialRenderMode.Gem, false);
            }
        }

        private void DrawSpecialShaderStatusLine(string label, YBMaterialRenderMode mode, bool compatibility)
        {
            string shaderName = YBGeneratedAssetBuilder.ShaderNameForMode(mode);
            Shader shader = Shader.Find(shaderName);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(130));
                string status = shader == null ? L("○ 等待 lilToon 生成") : (ShaderUtil.ShaderHasError(shader) ? L("✕ Shader 编译错误") : L("✓ 已加载 / 编译通过"));
                EditorGUILayout.LabelField(status, GUILayout.Width(uiLanguage == 1 ? 210 : 165));
                if (compatibility)
                    EditorGUILayout.LabelField(L("兼容实现 / 需视觉验证"), EditorStyles.miniLabel);
                else
                    EditorGUILayout.LabelField(shaderName, EditorStyles.miniLabel);
            }
        }

        private sealed class UnsupportedDisplay
        {
            public string key;
            public Material material;
            public string label;
            public string note;
            public int occurrences;
        }

        private void DrawUnsupportedMaterialOverrides()
        {
            if (report == null || profile == null) return;
            if (profile.unsupportedMaterialOverrides == null)
            {
                Undo.RecordObject(profile, "Initialize unsupported material overrides");
                profile.unsupportedMaterialOverrides = new List<YBUnsupportedMaterialOverride>();
                EditorUtility.SetDirty(profile);
            }

            var groups = new Dictionary<string, UnsupportedDisplay>();
            for (int i = 0; i < report.renderers.Count; i++)
            {
                var r = report.renderers[i];
                for (int s = 0; s < r.materials.Count; s++)
                {
                    var m = r.materials[s];
                    if (m.renderMode != YBMaterialRenderMode.Unsupported && m.renderMode != YBMaterialRenderMode.Missing) continue;
                    string key = YBGeneratedAssetBuilder.GetOverrideKey(r, m);
                    UnsupportedDisplay item;
                    if (!groups.TryGetValue(key, out item))
                    {
                        item = new UnsupportedDisplay
                        {
                            key = key,
                            material = m.material,
                            label = m.material != null ? m.material.name : r.hierarchyPath + " / Slot " + m.slotIndex,
                            note = m.note,
                            occurrences = 0
                        };
                        groups.Add(key, item);
                    }
                    item.occurrences++;
                }
            }

            if (groups.Count == 0)
            {
                EditorGUILayout.HelpBox(L("未发现需要处理的材质。"), MessageType.Info);
                return;
            }

            string[] actionNames = {
                L("阻止生成（默认）"), L("跳过该材质 Slot"),
                L("实验：按 Opaque"), L("实验：按 Cutout"), L("实验：按 Transparent"),
                L("实验：按 Refraction"), L("实验：按 Refraction Blur"),
                L("实验：按 Fur"), L("实验：按 Fur Cutout"), L("实验：按 Fur TwoPass"), L("实验：按 Gem")
            };
            foreach (var pair in groups)
            {
                UnsupportedDisplay item = pair.Value;
                YBUnsupportedMaterialOverride current = FindUnsupportedOverride(item.key);
                YBUnsupportedMaterialAction action = current != null ? current.action : YBUnsupportedMaterialAction.Block;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.ObjectField(item.material, typeof(Material), false, GUILayout.Width(220));
                        EditorGUILayout.LabelField(item.label + "  ×" + item.occurrences, EditorStyles.boldLabel);
                    }
                    EditorGUILayout.LabelField(L("原因"), D(item.note ?? string.Empty));
                    int next = EditorGUILayout.Popup(L("处理方式"), (int)action, actionNames);
                    if (next != (int)action)
                        SetUnsupportedOverride(item, (YBUnsupportedMaterialAction)next);
                }
            }
        }

        private YBUnsupportedMaterialOverride FindUnsupportedOverride(string key)
        {
            if (profile == null || profile.unsupportedMaterialOverrides == null) return null;
            for (int i = 0; i < profile.unsupportedMaterialOverrides.Count; i++)
            {
                var item = profile.unsupportedMaterialOverrides[i];
                if (item != null && item.key == key) return item;
            }
            return null;
        }

        private void SetUnsupportedOverride(UnsupportedDisplay display, YBUnsupportedMaterialAction action)
        {
            if (profile == null) return;
            if (profile.unsupportedMaterialOverrides == null)
                profile.unsupportedMaterialOverrides = new List<YBUnsupportedMaterialOverride>();

            Undo.RecordObject(profile, "修改未支持材质处理方式");
            var item = FindUnsupportedOverride(display.key);
            if (item == null)
            {
                item = new YBUnsupportedMaterialOverride
                {
                    key = display.key,
                    displayName = display.label,
                    material = display.material
                };
                profile.unsupportedMaterialOverrides.Add(item);
            }
            item.action = action;
            item.displayName = display.label;
            item.material = display.material;
            profile.dataVersion = YBToolVersion.ProfileDataVersion;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        private void LoadGenerationManifest()
        {
            ResolveGeneratedAssets(false);
            generationStatus = generationManifest != null
                ? L("已识别 Generation Manifest。")
                : (string.IsNullOrEmpty(manifestResolveStatus) ? L("未找到 Generation Manifest，请先执行生成步骤。") : manifestResolveStatus);
        }

        private string GetGeneratedAssetResolveContextKey()
        {
            int avatarId = avatarRoot != null ? avatarRoot.GetInstanceID() : 0;
            string profilePath = profile != null ? AssetDatabase.GetAssetPath(profile) : string.Empty;
            return avatarId + "|" + profilePath;
        }

        private void EnsureGeneratedAssetsResolvedOnce()
        {
            if (avatarRoot == null || profile == null) return;
            string key = GetGeneratedAssetResolveContextKey();
            if (generatedAssetResolveAttempted && string.Equals(generatedAssetResolveContextKey, key, StringComparison.Ordinal))
                return;

            generatedAssetResolveAttempted = true;
            generatedAssetResolveContextKey = key;
            ResolveGeneratedAssets(false);
        }

        private void InvalidateGeneratedAssetResolveCache()
        {
            generatedAssetResolveAttempted = false;
            generatedAssetResolveContextKey = null;
        }

        private void ResolveGeneratedAssets(bool notify)
        {
            if (avatarRoot == null || profile == null)
            {
                manifestResolveStatus = L("请先选择 Avatar Root 与 Profile。");
                if (notify) EditorUtility.DisplayDialog(L("生成资产识别"), D(manifestResolveStatus), L("确定"));
                return;
            }

            generatedAssetResolveAttempted = true;
            generatedAssetResolveContextKey = GetGeneratedAssetResolveContextKey();

            string root = YBPathUtility.GetOrCreateProfileOutputRoot(avatarRoot, profile, newProfileName);
            var resolved = YBGeneratedAssetResolver.Resolve(avatarRoot, profile, root);
            generationManifest = resolved.manifest;
            unresolvedManifestPath = generationManifest == null ? resolved.assetPath : string.Empty;
            manifestResolveStatus = resolved.message;
            installRecord = YBGeneratedAssetResolver.ResolveInstallRecord(root);
            animatorAssets = YBAnimatorFXBuilder.LoadExisting(root);
            actionAssets = YBAnimatorActionBuilder.LoadExisting(root);
            materialVariantRecord = YBMaterialVariantBuilder.LoadExisting(root);

            if (notify) EditorUtility.DisplayDialog(L("生成资产识别"), D(manifestResolveStatus), L("确定"));
        }

        private void RepairUnrecognizedManifest()
        {
            if (avatarRoot == null || profile == null)
            {
                validationStatus = L("请先选择 Avatar Root 与 Profile。");
                return;
            }
            string root = YBPathUtility.GetOrCreateProfileOutputRoot(avatarRoot, profile, newProfileName);
            if (string.IsNullOrEmpty(unresolvedManifestPath))
            {
                var r = YBGeneratedAssetResolver.Resolve(avatarRoot, profile, root);
                unresolvedManifestPath = r.assetPath;
                if (r.manifest != null)
                {
                    generationManifest = r.manifest;
                    validationStatus = L("Manifest 已经可以正常识别，无需修复。");
                    return;
                }
            }

            if (string.IsNullOrEmpty(unresolvedManifestPath))
            {
                validationStatus = L("没有找到需要修复的 Manifest 文件。");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    L("修复无法识别的 Manifest"),
                    L("将先把原 YAML 备份为 .txt，然后在原路径重建为当前 YBGeneratedAssetManifest 类型。\n\n") +
                    unresolvedManifestPath + L("\n\n是否继续？"),
                    L("备份并修复"), L("取消"))) return;

            var fixedResult = YBGeneratedAssetResolver.RepairAtPath(unresolvedManifestPath, avatarRoot, profile);
            generationManifest = fixedResult.manifest;
            manifestResolveStatus = fixedResult.message;
            validationStatus = fixedResult.message;
            if (generationManifest != null) unresolvedManifestPath = string.Empty;
        }

        private void DrawAnimatorMAPage()
        {
            EditorGUILayout.LabelField("7. Animator / MA", EditorStyles.largeLabel);
            DrawHelpFoldout(ref showAnimatorHelp, L("Animator / MA 流程说明"),
                L("生成 FX / Action Controller，然后用 Modular Avatar 安装参数、菜单和 Merge Animator。"));

            if (avatarRoot == null || profile == null)
            {
                EditorGUILayout.HelpBox(L("请先在 Avatar 页选择 Avatar Root 与当前 Profile。"), MessageType.Warning);
                return;
            }

            string root = YBPathUtility.GetOrCreateProfileOutputRoot(avatarRoot, profile, newProfileName);
            EnsureGeneratedAssetsResolvedOnce();

            YBEditorGUI.Section(L("生成资产识别"));
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ObjectField(L("Generation Manifest"), generationManifest, typeof(YBGeneratedAssetManifest), false);
                EditorGUILayout.ObjectField(L("Install Record"), installRecord, typeof(YBAvatarInstallRecord), false);
                EditorGUILayout.LabelField(L("输出目录"), root);
                if (!string.IsNullOrEmpty(manifestResolveStatus))
                    EditorGUILayout.HelpBox(D(manifestResolveStatus), generationManifest != null ? MessageType.Info : MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L("重新识别生成资产"))) ResolveGeneratedAssets(true);
                using (new EditorGUI.DisabledScope(generationManifest != null || string.IsNullOrEmpty(unresolvedManifestPath)))
                {
                    if (GUILayout.Button(L("修复无法识别的 Manifest"))) RepairUnrecognizedManifest();
                }
            }

            YBEditorGUI.Section(L("Animator-FX 设置"));
            EditorGUILayout.LabelField(L("Bool 参数"), profile.parameterName);
            EditorGUILayout.LabelField(L("默认服装"), profile.defaultOutfit == YBOutfitDefault.OutfitB ? L("服装 B") : L("服装 A"));
            EditorGUILayout.LabelField(L("A → B 时间"),
                string.Format(L("延迟 {0:0.###}s + 扫描 {1:0.###}s"), profile.scan.GetDelayAToB(), profile.scan.durationSeconds));
            EditorGUILayout.LabelField(L("B → A 时间"),
                string.Format(L("延迟 {0:0.###}s + 扫描 {1:0.###}s"), profile.scan.GetDelayBToA(), profile.scan.durationSeconds));
            Vector3 scanDirection = profile.scan.GetMoveDirection();
            EditorGUILayout.LabelField(L("扫描方向"),
                string.Format("({0:0.###}, {1:0.###}, {2:0.###})", scanDirection.x, scanDirection.y, scanDirection.z));
            EditorGUILayout.LabelField(L("扫描坐标"),
                string.Format("{0:0.###} → {1:0.###}", profile.scan.startHeight, profile.scan.endHeight));
            animatorOverwriteExisting = EditorGUILayout.ToggleLeft(L("覆盖同名 AnimationClip / FX Controller"), animatorOverwriteExisting);

            string preflight = YBAnimatorFXBuilder.ValidateInputs(avatarRoot, profile, generationManifest, installRecord, root);
            MessageType preflightType = preflight.IndexOf("通过", StringComparison.Ordinal) >= 0 ? MessageType.Info : MessageType.Error;
            EditorGUILayout.HelpBox(D(preflight), preflightType);

            using (new EditorGUI.DisabledScope(
                EditorApplication.isPlayingOrWillChangePlaymode ||
                preflight.IndexOf("通过", StringComparison.Ordinal) < 0))
            {
                if (GUILayout.Button(L("生成 / 更新四段 FX Clip + FX Controller"), GUILayout.Height(34)))
                {
                    animatorAssets = YBAnimatorFXBuilder.Build(
                        avatarRoot, profile, generationManifest, installRecord, root, animatorOverwriteExisting);
                    animatorStatus = animatorAssets.message;
                    if (animatorAssets.success && animatorAssets.controller != null)
                    {
                        Selection.activeObject = animatorAssets.controller;
                        EditorGUIUtility.PingObject(animatorAssets.controller);
                    }
                }
            }

            if (animatorAssets == null) animatorAssets = YBAnimatorFXBuilder.LoadExisting(root);
            if (animatorAssets != null)
            {
                showFxAssetDetails = EditorGUILayout.Foldout(showFxAssetDetails, L("已生成 FX 资产详情"), true);
                if (showFxAssetDetails)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.ObjectField(L("Outfit A Idle"), animatorAssets.outfitAIdle, typeof(AnimationClip), false);
                        EditorGUILayout.ObjectField(L("Outfit A → B"), animatorAssets.outfitAToB, typeof(AnimationClip), false);
                        EditorGUILayout.ObjectField(L("Outfit B Idle"), animatorAssets.outfitBIdle, typeof(AnimationClip), false);
                        EditorGUILayout.ObjectField(L("Outfit B → A"), animatorAssets.outfitBToA, typeof(AnimationClip), false);
                        EditorGUILayout.ObjectField(L("FX Controller"), animatorAssets.controller, typeof(RuntimeAnimatorController), false);
                    }
                }
            }

            if (!string.IsNullOrEmpty(animatorStatus))
                EditorGUILayout.HelpBox(D(animatorStatus),
                    animatorStatus.IndexOf("失败", StringComparison.Ordinal) >= 0 ? MessageType.Error : MessageType.Info);

            YBEditorGUI.Section(L("VRC Action Controller"));
            if (profile.action == null || !profile.action.enabled)
            {
                EditorGUILayout.HelpBox(L("换装动作未启用（可在“效果”页设置）。"), MessageType.None);
            }
            else
            {
                string actionPreflight = YBAnimatorActionBuilder.ValidateInputs(avatarRoot, profile);
                EditorGUILayout.HelpBox(D(actionPreflight),
                    actionPreflight.IndexOf("通过", StringComparison.Ordinal) >= 0 ? MessageType.Info : MessageType.Error);

                using (new EditorGUI.DisabledScope(
                    EditorApplication.isPlayingOrWillChangePlaymode ||
                    actionPreflight.IndexOf("通过", StringComparison.Ordinal) < 0))
                {
                    if (GUILayout.Button(L("生成 / 更新 VRC Action Controller"), GUILayout.Height(34)))
                    {
                        actionAssets = YBAnimatorActionBuilder.Build(avatarRoot, profile, root, animatorOverwriteExisting);
                        actionStatus = actionAssets.message;
                        if (actionAssets.success && actionAssets.controller != null)
                        {
                            Selection.activeObject = actionAssets.controller;
                            EditorGUIUtility.PingObject(actionAssets.controller);
                        }
                    }
                }

                if (actionAssets == null) actionAssets = YBAnimatorActionBuilder.LoadExisting(root);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.ObjectField(L("Action Controller"),
                        actionAssets != null ? actionAssets.controller : null, typeof(RuntimeAnimatorController), false);
                    EditorGUILayout.LabelField(L("共用参数"), profile.parameterName + L(" (Bool)"));
                    EditorGUILayout.LabelField(L("动作接管"),
                        L("Action Weight 0↔1") +
                        (profile.action.controlTracking ? L(" / Tracking Animation↔Tracking") : string.Empty) +
                        (profile.action.disableLocomotion ? L(" / 动作时禁止 Locomotion") : string.Empty));
                }
                if (!string.IsNullOrEmpty(actionStatus))
                    EditorGUILayout.HelpBox(D(actionStatus),
                        actionStatus.IndexOf("失败", StringComparison.Ordinal) >= 0 ? MessageType.Error : MessageType.Info);
            }

            YBEditorGUI.Section(L("Modular Avatar / 菜单预制件"));
            EditorGUILayout.LabelField(L("Controller 需合并到对应 Layer 才会生效。"), EditorStyles.miniLabel);
            DrawHelpFoldout(ref showMAHelp, L("MA 合并与菜单说明"),
                L("FX Controller 合并到 FX Layer，Action Controller 合并到 Action Layer。\n") +
                L("将 Profile 专用「溶解换装.prefab」安装到 Avatar，FX、Action 与菜单 Toggle 共用 Bool 参数。"));

            GameObject maPrefabAsset = YBModularAvatarInstaller.LoadGeneratedPrefab(root);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ObjectField(L("菜单预制件"), maPrefabAsset, typeof(GameObject), false);
                Transform maInstalled = YBModularAvatarInstaller.FindInstalledRoot(avatarRoot);
                EditorGUILayout.LabelField(L("Avatar 实例"), maInstalled != null ? maInstalled.name + "  ✓" : L("未安装"));
                using (new EditorGUI.DisabledScope(maPrefabAsset == null))
                {
                    if (GUILayout.Button(L("定位生成的「溶解换装」预制件")))
                    {
                        Selection.activeObject = maPrefabAsset;
                        EditorGUIUtility.PingObject(maPrefabAsset);
                    }
                }
            }

            if (animatorAssets == null) animatorAssets = YBAnimatorFXBuilder.LoadExisting(root);
            if (actionAssets == null) actionAssets = YBAnimatorActionBuilder.LoadExisting(root);
            string maCheck = YBModularAvatarInstaller.ValidateInputs(profile, animatorAssets, actionAssets);
            EditorGUILayout.HelpBox(D(maCheck), maCheck.IndexOf("通过", StringComparison.Ordinal) >= 0 ? MessageType.Info : MessageType.Error);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || maCheck.IndexOf("通过", StringComparison.Ordinal) < 0))
            {
                if (GUILayout.Button(L("生成「溶解换装」预制件并安装 FX + Action + Toggle"), GUILayout.Height(36)))
                {
                    YBModularAvatarResult r = YBModularAvatarInstaller.BuildPrefabAndInstall(
                        avatarRoot, profile, animatorAssets, actionAssets, root);
                    maStatus = r.message;
                    if (r.installedObject != null) Selection.activeGameObject = r.installedObject;
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L("验证「溶解换装」MA / Action 安装")))
                    maStatus = YBModularAvatarInstaller.ValidateInstalled(avatarRoot, profile, animatorAssets, actionAssets);
                if (GUILayout.Button(L("卸载「溶解换装」MA 容器")))
                {
                    if (EditorUtility.DisplayDialog(L("卸载 溶解换装"), L("只从 Avatar 移除「溶解换装」（同时兼容旧 __YB_Dissolve_MA__），不会删除生成的 Prefab、动画或 Controller。"), L("卸载"), L("取消")))
                        maStatus = YBModularAvatarInstaller.Uninstall(avatarRoot);
                }
            }
            if (!string.IsNullOrEmpty(maStatus)) EditorGUILayout.HelpBox(D(maStatus), maStatus.IndexOf("失败", StringComparison.Ordinal) >= 0 ? MessageType.Error : MessageType.Info);
        }

        private void DrawValidationPage()
        {
            EditorGUILayout.LabelField(L("8. 验证 / 兼容修复"), EditorStyles.largeLabel);
            if (avatarRoot == null || profile == null)
            {
                EditorGUILayout.HelpBox(L("请先选择 Avatar Root 与 Profile。"), MessageType.Warning);
                return;
            }

            string root = YBPathUtility.GetOrCreateProfileOutputRoot(avatarRoot, profile, newProfileName);
            EnsureGeneratedAssetsResolvedOnce();

            YBEditorGUI.Section(L("Asset 识别"));
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(L("Generation Manifest"), generationManifest != null ? L("✓ 已识别") : L("✕ 未识别"));
                EditorGUILayout.LabelField(L("Install Record"), installRecord != null ? L("✓ 已识别") : L("○ 未找到"));
                bool fxOk = animatorAssets != null && animatorAssets.success;
                EditorGUILayout.LabelField("Animator-FX", fxOk ? L("✓ 已识别") : L("○ 未完整生成"));
                bool actionEnabled = profile.action != null && profile.action.enabled;
                bool actionOk = actionAssets != null && actionAssets.success;
                EditorGUILayout.LabelField("Animator-Action", !actionEnabled ? L("○ 未启用") : (actionOk ? L("✓ 已识别") : L("○ 未生成")));
                string outputRoot = YBPathUtility.GetOrCreateProfileOutputRoot(avatarRoot, profile, newProfileName);
                materialVariantRecord = materialVariantRecord != null ? materialVariantRecord : YBMaterialVariantBuilder.LoadExisting(outputRoot);
                EditorGUILayout.LabelField(L("Material Variants"), materialVariantRecord != null ? L("✓ 已安装") : L("○ 未安装"));
                Transform maRoot = avatarRoot.transform.Find(YBModularAvatarInstaller.ContainerName);
                EditorGUILayout.LabelField("Modular Avatar", maRoot != null ? L("✓ 已安装") : L("○ 未安装"));
                if (!string.IsNullOrEmpty(unresolvedManifestPath))
                    EditorGUILayout.LabelField(L("无法识别的 Manifest"), unresolvedManifestPath);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(L("重新扫描 / 识别所有生成资产"))) ResolveGeneratedAssets(true);
                using (new EditorGUI.DisabledScope(generationManifest != null || string.IsNullOrEmpty(unresolvedManifestPath)))
                {
                    if (GUILayout.Button(L("修复 Manifest 类型 / 脚本引用"))) RepairUnrecognizedManifest();
                }
            }

            if (generationManifest != null)
            {
                string g = YBGeneratedAssetBuilder.ValidateManifest(generationManifest);
                EditorGUILayout.HelpBox(D(g), g.IndexOf("通过", StringComparison.Ordinal) >= 0 ? MessageType.Info : MessageType.Error);
            }

            if (installRecord != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(L("验证当前安装（Edit/Play 自动适配）")))
                        validationStatus = YBAvatarInstaller.ValidateInstalled(avatarRoot, installRecord);
                    if (GUILayout.Button(L("验证 Animator-FX 资产")))
                    {
                        animatorAssets = YBAnimatorFXBuilder.LoadExisting(root);
                        validationStatus = YBAnimatorFXBuilder.ValidateGenerated(profile, animatorAssets);
                    }
                    if (GUILayout.Button(L("验证 Action Controller")))
                    {
                        actionAssets = YBAnimatorActionBuilder.LoadExisting(root);
                        validationStatus = YBAnimatorActionBuilder.ValidateGenerated(profile, actionAssets);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(L("验证材质变体")))
                    {
                        materialVariantRecord = YBMaterialVariantBuilder.LoadExisting(root);
                        validationStatus = YBMaterialVariantBuilder.ValidateInstalled(avatarRoot, materialVariantRecord);
                    }
                    if (GUILayout.Button(L("验证 MA / Action Merge")))
                    {
                        animatorAssets = YBAnimatorFXBuilder.LoadExisting(root);
                        actionAssets = YBAnimatorActionBuilder.LoadExisting(root);
                        validationStatus = YBModularAvatarInstaller.ValidateInstalled(avatarRoot, profile, animatorAssets, actionAssets);
                    }
                }
            }

            YBEditorGUI.Section(L("Play Mode Animator-FX 验证"));
            previewRuntimeRebind = EditorGUILayout.ToggleLeft(L("Play Mode 自动重绑定当前 Renderer 路径（推荐开启）"), previewRuntimeRebind);
            previewRunOriginalController = EditorGUILayout.ToggleLeft(L("测试时同时运行 Avatar 原 Controller（默认关闭）"), previewRunOriginalController);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(L("进入 Play Mode 后可诊断 Renderer 路径并测试 A/B 双向 FX。"), MessageType.None);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying || installRecord == null))
            {
                if (GUILayout.Button(L("诊断当前 Play Mode Renderer 路径（不启动 FX）")))
                    validationStatus = YBAnimatorFXPreview.DiagnoseRuntimePaths(avatarRoot, installRecord);
            }

            bool fxReady = animatorAssets != null && animatorAssets.success;
            using (new EditorGUI.DisabledScope(!Application.isPlaying || installRecord == null || !fxReady))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!YBAnimatorFXPreview.Active)
                    {
                        if (GUILayout.Button(L("开始临时 Animator-FX 验证"), GUILayout.Height(32)))
                            validationStatus = YBAnimatorFXPreview.Start(
                                avatarRoot, profile, installRecord, animatorAssets,
                                previewRunOriginalController, previewRuntimeRebind);
                    }
                    else
                    {
                        if (GUILayout.Button(L("停止临时 Animator-FX 验证"), GUILayout.Height(32)))
                        {
                            YBAnimatorFXPreview.Stop();
                            validationStatus = L("临时 Animator-FX 验证已停止，Avatar Animator 已恢复。");
                        }
                    }
                }

                if (YBAnimatorFXPreview.Active)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(L("服装 A  (参数=false)"), GUILayout.Height(30)))
                            validationStatus = YBAnimatorFXPreview.SetValue(false);
                        if (GUILayout.Button(L("服装 B  (参数=true)"), GUILayout.Height(30)))
                            validationStatus = YBAnimatorFXPreview.SetValue(true);
                    }
                    EditorGUILayout.LabelField(L("当前参数值"), YBAnimatorFXPreview.CurrentValue ? L("true / 服装 B") : L("false / 服装 A"));
                }
            }

            if (!string.IsNullOrEmpty(validationStatus))
                EditorGUILayout.HelpBox(D(validationStatus),
                    validationStatus.IndexOf("失败", StringComparison.Ordinal) >= 0 ||
                    validationStatus.IndexOf("缺", StringComparison.Ordinal) >= 0 ||
                    validationStatus.IndexOf("✕", StringComparison.Ordinal) >= 0
                        ? MessageType.Warning : MessageType.Info);
        }


        private bool TryEstimateScanRange(Vector3 scanMoveDirection, out float estimatedStart, out float estimatedEnd)
        {
            estimatedStart = 0f;
            estimatedEnd = 0f;
            if (avatarRoot == null || scanMoveDirection.sqrMagnitude < 0.000001f) return false;

            Vector3 projectionAxis = -scanMoveDirection.normalized;
            bool found = false;
            float minCoord = float.PositiveInfinity;
            float maxCoord = float.NegativeInfinity;
            var seen = new HashSet<Renderer>();

            AccumulateRendererBounds(outfitARoots, seen, projectionAxis, ref found, ref minCoord, ref maxCoord);
            AccumulateRendererBounds(outfitBRoots, seen, projectionAxis, ref found, ref minCoord, ref maxCoord);

            if (!found) return false;
            estimatedStart = maxCoord + scanRangeEstimateMargin;
            estimatedEnd = minCoord - scanRangeEstimateMargin;
            return true;
        }

        private void AccumulateRendererBounds(
            List<GameObject> roots,
            HashSet<Renderer> seen,
            Vector3 projectionAxis,
            ref bool found,
            ref float minCoord,
            ref float maxCoord)
        {
            if (roots == null || avatarRoot == null) return;
            for (int i = 0; i < roots.Count; i++)
            {
                GameObject root = roots[i];
                if (root == null) continue;
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer renderer = renderers[r];
                    if (renderer == null || !seen.Add(renderer)) continue;
                    Bounds b = renderer.bounds;
                    Vector3 c = b.center;
                    Vector3 e = b.extents;
                    for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 world = c + Vector3.Scale(e, new Vector3(x, y, z));
                        Vector3 local = avatarRoot.transform.InverseTransformPoint(world);
                        float coord = Vector3.Dot(local, projectionAxis);
                        minCoord = Mathf.Min(minCoord, coord);
                        maxCoord = Mathf.Max(maxCoord, coord);
                        found = true;
                    }
                }
            }
        }

        private static Vector3 ResolveScanMoveDirection(YBScanDirectionPreset preset, Vector3 customDirection)
        {
            switch (preset)
            {
                case YBScanDirectionPreset.BottomToTop: return Vector3.up;
                case YBScanDirectionPreset.LeftToRight: return Vector3.right;
                case YBScanDirectionPreset.RightToLeft: return Vector3.left;
                case YBScanDirectionPreset.FrontToBack: return Vector3.back;
                case YBScanDirectionPreset.BackToFront: return Vector3.forward;
                case YBScanDirectionPreset.Custom:
                    return customDirection.sqrMagnitude < 0.000001f ? Vector3.zero : customDirection.normalized;
                default: return Vector3.down;
            }
        }

        private static AnimationClip ResolveReverseActionClip(YBDissolveProfile p)
        {
            if (p == null || p.action == null) return null;
            if (p.action.outfitBToA != null) return p.action.outfitBToA;
            return p.action.reuseForwardClipWhenReverseMissing ? p.action.outfitAToB : null;
        }

        private void DrawRootList(string title, List<GameObject> list)
        {
            YBEditorGUI.Section(title);
            for (int i = 0; i < list.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    list[i] = (GameObject)EditorGUILayout.ObjectField(L("Root ") + (i + 1), list[i], typeof(GameObject), true);
                    if (GUILayout.Button("-", GUILayout.Width(28)))
                    {
                        list.RemoveAt(i);
                        i--;
                    }
                }
            }
            if (GUILayout.Button(L("+ 添加 Root"))) list.Add(null);
        }

        private void DrawAnalysisSummary()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(L("分析结果"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(L("Renderer: ") + report.RendererCount + "    Material Slots: " + report.MaterialSlotCount);
                var old = GUI.color;
                GUI.color = report.BlockingCount > 0 ? new Color(1f, 0.55f, 0.5f) : new Color(0.65f, 1f, 0.65f);
                EditorGUILayout.LabelField(L("Blocking: ") + report.BlockingCount + L("    Warning: ") + report.WarningCount, EditorStyles.boldLabel);
                GUI.color = old;
            }
        }

        private static bool HasMaterialIssue(YBRendererAnalysis r)
        {
            for (int i = 0; i < r.materials.Count; i++)
            {
                var mode = r.materials[i].renderMode;
                if (mode == YBMaterialRenderMode.Unsupported || mode == YBMaterialRenderMode.Missing) return true;
            }
            return false;
        }

        private void DrawRendererAnalysis(YBRendererAnalysis r)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("[" + r.outfitGroup + "] " + r.hierarchyPath, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (r.renderer != null && GUILayout.Button(L("Ping"), GUILayout.Width(48))) EditorGUIUtility.PingObject(r.renderer.gameObject);
                }

                EditorGUILayout.LabelField(L("Renderer"), r.rendererName + " / " + r.rendererType);
                EditorGUILayout.LabelField(L("Mesh"), r.meshName);
                EditorGUILayout.LabelField(L("Read/Write"), r.meshReadable ? "✓ Readable" : "✕ Disabled");
                EditorGUILayout.LabelField(L("UV4"), r.uv4Occupied ? L("⚠ 已存在 TEXCOORD3") : L("✓ 空闲"));
                if (r.maPathRisk) EditorGUILayout.LabelField("MA Path", L("⚠ Bone Proxy / Merge Armature 风险"));
                if (r.ambiguousFingerprint) EditorGUILayout.LabelField("Locator", L("⚠ 指纹重复，后续需 Material Signature / Override"));

                for (int i = 0; i < r.materials.Count; i++)
                {
                    var m = r.materials[i];
                    EditorGUILayout.LabelField(
                        L("Slot ") + m.slotIndex,
                        m.materialName + "  →  " + m.renderMode + "  [" + D(m.note) + "]");
                    if (!string.IsNullOrEmpty(m.assetPath))
                        EditorGUILayout.LabelField(L("    Asset"), m.assetPath);
                }
            }
        }

        private void DrawMessage(YBAnalysisMessage m)
        {
            MessageType type = MessageType.None;
            if (m.severity == YBAnalysisSeverity.Warning) type = MessageType.Warning;
            if (m.severity == YBAnalysisSeverity.Blocking) type = MessageType.Error;
            EditorGUILayout.HelpBox(D(m.title) + "\n" + D(m.detail), type);
        }

        private void CreateProfile()
        {
            string folder = YBPathUtility.EnsureGeneratedProfileFolders(avatarRoot.name, newProfileName);
            string dataFolder = folder + "/Data";
            string assetName = YBPathUtility.SanitizeAssetName(newProfileName, "Profile") + "_YBDissolveProfile.asset";
            string path = AssetDatabase.GenerateUniqueAssetPath(dataFolder + "/" + assetName);
            var created = ScriptableObject.CreateInstance<YBDissolveProfile>();
            created.name = Path.GetFileNameWithoutExtension(assetName);
            created.avatarHint = avatarRoot.name;
            created.defaultOutfit = YBOutfitDefault.OutfitA;
            created.outfitA.displayName = "服装 A";
            created.outfitB.displayName = "服装 B";
            AssetDatabase.CreateAsset(created, path);
            AssetDatabase.SaveAssets();
            profile = created;
            YBProfileUtility.SaveSceneBindings(profile, avatarRoot, outfitARoots, outfitBRoots);
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }


        private void FindProfileForCurrentAvatar()
        {
            if (avatarRoot == null) return;

            var found = new List<YBDissolveProfile>();
            var guids = AssetDatabase.FindAssets("t:YBDissolveProfile", new[] { "Assets/_YBToolsGenerated" });
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var candidate = AssetDatabase.LoadAssetAtPath<YBDissolveProfile>(path);
                if (candidate == null) continue;
                if (candidate.avatarHint == avatarRoot.name) found.Add(candidate);
            }

            if (found.Count == 0)
            {
                EditorUtility.DisplayDialog(L("未找到 Profile"),
                    L("没有找到 avatarHint 与当前 Avatar 匹配的 YBDissolveProfile。\n请先填写‘新 Profile 名称’并创建。"), L("确定"));
                return;
            }

            if (found.Count == 1)
            {
                SelectProfile(found[0]);
                return;
            }

            var menu = new GenericMenu();
            for (int i = 0; i < found.Count; i++)
            {
                var item = found[i];
                var path = AssetDatabase.GetAssetPath(item);
                string label = item.name + "    [" + path + "]";
                menu.AddItem(new GUIContent(label), item == profile, () => SelectProfile(item));
            }
            menu.ShowAsContext();
        }

        private void SelectProfile(YBDissolveProfile selected)
        {
            profile = selected;
            if (profile == null) return;
            LoadProfileBindings();
            ResolveGeneratedAssets(false);
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
            Repaint();
        }

        private void LoadProfileBindings()
        {
            outfitARoots = YBProfileUtility.ResolveSceneBindings(profile, avatarRoot, true);
            outfitBRoots = YBProfileUtility.ResolveSceneBindings(profile, avatarRoot, false);
        }

        private void EnsureGeneratedFolders()
        {
            string folder = YBPathUtility.GetOrCreateProfileOutputRoot(avatarRoot, profile, newProfileName);
            Debug.Log("[YBTools] Generated folder ready: " + folder);
        }

        private void ExportAnalysisReport()
        {
            if (avatarRoot == null || report == null) return;
            string folder = YBPathUtility.GetOrCreateProfileOutputRoot(avatarRoot, profile, newProfileName) + "/Data";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/AnalysisReport.txt");
            string absolute = Path.GetFullPath(assetPath);

            using (var writer = new StreamWriter(absolute, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine("YB Dissolve Avatar Builder Analysis");
                writer.WriteLine("Tool Version: " + YBToolVersion.Version);
                writer.WriteLine("Avatar: " + avatarRoot.name);
                writer.WriteLine(L("Renderer: ") + report.RendererCount);
                writer.WriteLine("Material Slots: " + report.MaterialSlotCount);
                writer.WriteLine(L("Blocking: ") + report.BlockingCount);
                writer.WriteLine("Warnings: " + report.WarningCount);
                writer.WriteLine();

                for (int i = 0; i < report.renderers.Count; i++)
                {
                    var r = report.renderers[i];
                    writer.WriteLine("[" + r.outfitGroup + "] " + r.hierarchyPath);
                    writer.WriteLine("  Renderer=" + r.rendererName + " / " + r.rendererType);
                    writer.WriteLine("  Mesh=" + r.meshName + " Readable=" + r.meshReadable + " UV4=" + r.uv4Occupied + " MAPathRisk=" + r.maPathRisk);
                    for (int s = 0; s < r.materials.Count; s++)
                    {
                        var m = r.materials[s];
                        writer.WriteLine("  Slot " + m.slotIndex + ": " + m.materialName + " | " + m.shaderName + " | " + m.renderMode + " | " + m.note + " | " + m.assetPath + " | GUID=" + m.assetGuid);
                    }
                    writer.WriteLine();
                }

                writer.WriteLine("Messages:");
                for (int i = 0; i < report.messages.Count; i++)
                {
                    var m = report.messages[i];
                    writer.WriteLine("[" + m.severity + "] " + m.title + " - " + m.detail);
                }
            }

            AssetDatabase.ImportAsset(assetPath);
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private void TryPickAvatarFromSelection(bool force = false)
        {
            if (!force && avatarRoot != null) return;
            var selected = Selection.activeGameObject;
            if (selected == null) return;

            // Avoid compile-time VRC SDK dependency: locate descriptor by type name through components.
            Transform current = selected.transform;
            while (current != null)
            {
                var components = current.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    var c = components[i];
                    if (c == null) continue;
                    if ((c.GetType().FullName ?? string.Empty) == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor")
                    {
                        avatarRoot = current.gameObject;
                        return;
                    }
                }
                current = current.parent;
            }

            if (force) avatarRoot = selected;
        }
    }
}
