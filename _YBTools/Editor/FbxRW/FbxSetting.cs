using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class FBXImportSettingsBatchTool : EditorWindow
{
    // ─── State ───────────────────────────────────────────────────────────────
    private bool applyReadWrite        = true;
    private bool applyLegacyBlendShape = true;
    private bool onlySelectedFolder    = false;
    private bool dryRun                = false;

    private Vector2 scrollPos;
    private List<string> logLines = new List<string>();
    private int  totalFound;
    private int  totalModified;
    private bool isRunning;

    // ─── Language ─────────────────────────────────────────────────────────────
    private enum Language { 中文, 日本語, English, Русский, 한국어 }

    // Language preference is persisted via EditorPrefs so it survives domain reloads.
    private const string PrefKey = "FBXBatchTool_Lang";
    private Language currentLang
    {
        get => (Language)EditorPrefs.GetInt(PrefKey, 0);
        set => EditorPrefs.SetInt(PrefKey, (int)value);
    }

    // ─── Localization table ───────────────────────────────────────────────────
    private static readonly Dictionary<Language, Dictionary<string, string>> L =
        new Dictionary<Language, Dictionary<string, string>>
    {
        [Language.中文] = new Dictionary<string, string>
        {
            ["window_title"]     = "FBX 批量修复",
            ["main_title"]       = "VRC FBX 批量导入修复工具",
            ["section_modify"]   = "修改项目",
            ["toggle_rw"]        = "读取/写入（Read/Write Enabled）",
            ["toggle_lbs"]       = "旧版混合形状法线（Legacy Blend Shape Normals）",
            ["section_scope"]    = "扫描范围",
            ["toggle_folder"]    = "仅处理 Project 窗口中已选中的文件夹",
            ["section_options"]  = "运行选项",
            ["toggle_dry"]       = "试运行（Dry Run）—— 仅预览，不写入改动",
            ["btn_dry"]          = "开始预览（Dry Run）",
            ["btn_run"]          = "开始批量修复",
            ["warn_no_toggle"]   = "请至少勾选一个修改项。",
            ["scan_result"]      = "扫描结果：共找到 {0} 个 FBX，{1} {2} 个",
            ["pending"]          = "待修改",
            ["modified"]         = "已修改",
            ["btn_clear"]        = "清空日志",
            ["section_language"] = "界面语言",
            ["log_skip"]         = "[跳过]",
            ["log_noneed"]       = "[无需]",
            ["log_error"]        = "[错误]",
            ["log_change"]       = "[修改]",
            ["log_preview"]      = "[预览]",
            ["log_start"]        = "─── 开始{0} ───  共找到资产 {1} 个",
            ["log_dryrun_tag"]   = " [Dry Run]",
            ["log_done"]         = "─── 完成 ───  扫描 {0} 个 FBX，{1} {2} 个",
            ["log_no_importer"]  = "无法获取 ModelImporter：",
            ["log_rw"]           = "读/写",
            ["log_lbs"]          = "旧版混合形状法线",
            ["log_will_modify"]  = "将修改 ({0})  {1}",
            ["log_no_folder"]    = "未在 Project 窗口中选中任何文件夹，将扫描整个项目。",
            ["log_scan_folders"] = "扫描范围限制为 {0} 个文件夹：{1}",
        },

        [Language.日本語] = new Dictionary<string, string>
        {
            ["window_title"]     = "FBX 一括修正",
            ["main_title"]       = "VRC FBX 一括インポート修正ツール",
            ["section_modify"]   = "変更項目",
            ["toggle_rw"]        = "読み取り/書き込み（Read/Write Enabled）",
            ["toggle_lbs"]       = "レガシーブレンドシェイプ法線（Legacy Blend Shape Normals）",
            ["section_scope"]    = "スキャン範囲",
            ["toggle_folder"]    = "Project ウィンドウで選択したフォルダのみ処理",
            ["section_options"]  = "実行オプション",
            ["toggle_dry"]       = "ドライラン（Dry Run）—— プレビューのみ、変更は保存しない",
            ["btn_dry"]          = "プレビュー開始（Dry Run）",
            ["btn_run"]          = "一括修正を開始",
            ["warn_no_toggle"]   = "少なくとも1つの変更項目を選択してください。",
            ["scan_result"]      = "スキャン結果：FBX {0} 個を検出、{1} {2} 個",
            ["pending"]          = "変更予定",
            ["modified"]         = "変更済み",
            ["btn_clear"]        = "ログをクリア",
            ["section_language"] = "表示言語",
            ["log_skip"]         = "[スキップ]",
            ["log_noneed"]       = "[不要]",
            ["log_error"]        = "[エラー]",
            ["log_change"]       = "[変更]",
            ["log_preview"]      = "[プレビュー]",
            ["log_start"]        = "─── 開始{0} ───  資産 {1} 個を検出",
            ["log_dryrun_tag"]   = " [Dry Run]",
            ["log_done"]         = "─── 完了 ───  FBX {0} 個スキャン、{1} {2} 個",
            ["log_no_importer"]  = "ModelImporter を取得できません：",
            ["log_rw"]           = "読み書き",
            ["log_lbs"]          = "レガシーブレンドシェイプ法線",
            ["log_will_modify"]  = "変更予定 ({0})  {1}",
            ["log_no_folder"]    = "Project ウィンドウでフォルダが選択されていません。プロジェクト全体をスキャンします。",
            ["log_scan_folders"] = "スキャン範囲を {0} 個のフォルダに限定：{1}",
        },

        [Language.English] = new Dictionary<string, string>
        {
            ["window_title"]     = "FBX Batch Fix",
            ["main_title"]       = "VRC FBX Batch Import Fix Tool",
            ["section_modify"]   = "Modifications",
            ["toggle_rw"]        = "Read/Write Enabled",
            ["toggle_lbs"]       = "Legacy Blend Shape Normals",
            ["section_scope"]    = "Scan Scope",
            ["toggle_folder"]    = "Only process folders selected in the Project window",
            ["section_options"]  = "Run Options",
            ["toggle_dry"]       = "Dry Run — Preview only, no changes will be written",
            ["btn_dry"]          = "Start Preview (Dry Run)",
            ["btn_run"]          = "Start Batch Fix",
            ["warn_no_toggle"]   = "Please select at least one modification.",
            ["scan_result"]      = "Scan result: {0} FBX found, {1} {2}",
            ["pending"]          = "pending",
            ["modified"]         = "modified",
            ["btn_clear"]        = "Clear Log",
            ["section_language"] = "Interface Language",
            ["log_skip"]         = "[SKIP]",
            ["log_noneed"]       = "[OK]",
            ["log_error"]        = "[ERROR]",
            ["log_change"]       = "[FIXED]",
            ["log_preview"]      = "[PREVIEW]",
            ["log_start"]        = "─── Start{0} ───  Found {1} assets",
            ["log_dryrun_tag"]   = " [Dry Run]",
            ["log_done"]         = "─── Done ───  Scanned {0} FBX, {1} {2}",
            ["log_no_importer"]  = "Cannot get ModelImporter: ",
            ["log_rw"]           = "Read/Write",
            ["log_lbs"]          = "Legacy Blend Shape Normals",
            ["log_will_modify"]  = "Will modify ({0})  {1}",
            ["log_no_folder"]    = "No folder selected in Project window. Scanning entire project.",
            ["log_scan_folders"] = "Scan limited to {0} folder(s): {1}",
        },

        [Language.Русский] = new Dictionary<string, string>
        {
            ["window_title"]     = "FBX Массовое исправление",
            ["main_title"]       = "VRC FBX Инструмент массового импорта",
            ["section_modify"]   = "Параметры изменений",
            ["toggle_rw"]        = "Чтение/Запись (Read/Write Enabled)",
            ["toggle_lbs"]       = "Устаревшие нормали BlendShape (Legacy Blend Shape Normals)",
            ["section_scope"]    = "Область сканирования",
            ["toggle_folder"]    = "Обрабатывать только выбранные папки в окне Project",
            ["section_options"]  = "Параметры запуска",
            ["toggle_dry"]       = "Пробный запуск (Dry Run) — только предпросмотр, без записи изменений",
            ["btn_dry"]          = "Начать предпросмотр (Dry Run)",
            ["btn_run"]          = "Начать массовое исправление",
            ["warn_no_toggle"]   = "Пожалуйста, выберите хотя бы один параметр изменения.",
            ["scan_result"]      = "Результат сканирования: найдено {0} FBX, {1} {2}",
            ["pending"]          = "ожидает изменений",
            ["modified"]         = "изменено",
            ["btn_clear"]        = "Очистить журнал",
            ["section_language"] = "Язык интерфейса",
            ["log_skip"]         = "[ПРОПУСК]",
            ["log_noneed"]       = "[OK]",
            ["log_error"]        = "[ОШИБКА]",
            ["log_change"]       = "[ИЗМЕНЕНО]",
            ["log_preview"]      = "[ПРОСМОТР]",
            ["log_start"]        = "─── Начало{0} ───  Найдено ресурсов: {1}",
            ["log_dryrun_tag"]   = " [Dry Run]",
            ["log_done"]         = "─── Готово ───  Просканировано {0} FBX, {1} {2}",
            ["log_no_importer"]  = "Не удаётся получить ModelImporter: ",
            ["log_rw"]           = "Чтение/Запись",
            ["log_lbs"]          = "Устаревшие нормали BlendShape",
            ["log_will_modify"]  = "Будет изменено ({0})  {1}",
            ["log_no_folder"]    = "В окне Project не выбрана ни одна папка. Сканируется весь проект.",
            ["log_scan_folders"] = "Область сканирования ограничена {0} папками: {1}",
        },

        [Language.한국어] = new Dictionary<string, string>
        {
            ["window_title"]     = "FBX 일괄 수정",
            ["main_title"]       = "VRC FBX 일괄 임포트 수정 도구",
            ["section_modify"]   = "수정 항목",
            ["toggle_rw"]        = "읽기/쓰기 활성화（Read/Write Enabled）",
            ["toggle_lbs"]       = "레거시 블렌드쉐이프 노멀（Legacy Blend Shape Normals）",
            ["section_scope"]    = "스캔 범위",
            ["toggle_folder"]    = "Project 창에서 선택한 폴더만 처리",
            ["section_options"]  = "실행 옵션",
            ["toggle_dry"]       = "드라이 런（Dry Run）—— 미리 보기만, 변경 사항 저장 안 함",
            ["btn_dry"]          = "미리 보기 시작（Dry Run）",
            ["btn_run"]          = "일괄 수정 시작",
            ["warn_no_toggle"]   = "수정 항목을 하나 이상 선택하세요.",
            ["scan_result"]      = "스캔 결과: FBX {0}개 발견, {1} {2}개",
            ["pending"]          = "수정 예정",
            ["modified"]         = "수정됨",
            ["btn_clear"]        = "로그 지우기",
            ["section_language"] = "인터페이스 언어",
            ["log_skip"]         = "[건너뜀]",
            ["log_noneed"]       = "[불필요]",
            ["log_error"]        = "[오류]",
            ["log_change"]       = "[수정됨]",
            ["log_preview"]      = "[미리 보기]",
            ["log_start"]        = "─── 시작{0} ───  에셋 {1}개 발견",
            ["log_dryrun_tag"]   = " [Dry Run]",
            ["log_done"]         = "─── 완료 ───  FBX {0}개 스캔, {1} {2}개",
            ["log_no_importer"]  = "ModelImporter를 가져올 수 없습니다: ",
            ["log_rw"]           = "읽기/쓰기",
            ["log_lbs"]          = "레거시 블렌드쉐이프 노멀",
            ["log_will_modify"]  = "수정 예정 ({0})  {1}",
            ["log_no_folder"]    = "Project 창에서 선택된 폴더가 없습니다. 전체 프로젝트를 스캔합니다.",
            ["log_scan_folders"] = "스캔 범위를 {0}개 폴더로 제한: {1}",
        },
    };

    // Shorthand text lookup for the current language.
    private string T(string key) => L[currentLang][key];

    // Log-prefix helpers — used for both writing and color-matching log lines.
    private string PrefixSkip    => T("log_skip");
    private string PrefixNoNeed  => T("log_noneed");
    private string PrefixError   => T("log_error");
    private string PrefixChange  => T("log_change");
    private string PrefixPreview => T("log_preview");

    // ─── Menu items ───────────────────────────────────────────────────────────
    // Unity requires [MenuItem] strings to be compile-time constants, so the
    // menu entries themselves cannot be translated at runtime. Instead we
    // register one entry per language; selecting any of them opens the window
    // in that language (preference is persisted via EditorPrefs).

    [MenuItem("YBTools/FBX Batch Fix／批量修复／一括修正 %#F")]
    public static void ShowWindow_Default() => Open(null); // keep current language

    [MenuItem("YBTools/FBX Batch Fix (Language／语言／言語)/中文")]
    public static void ShowWindow_ZH() => Open(Language.中文);

    [MenuItem("YBTools/FBX Batch Fix (Language／语言／言語)/日本語")]
    public static void ShowWindow_JP() => Open(Language.日本語);

    [MenuItem("YBTools/FBX Batch Fix (Language／语言／言語)/English")]
    public static void ShowWindow_EN() => Open(Language.English);

    [MenuItem("YBTools/FBX Batch Fix (Language／语言／言語)/Русский")]
    public static void ShowWindow_RU() => Open(Language.Русский);

    [MenuItem("YBTools/FBX Batch Fix (Language／语言／言語)/한국어")]
    public static void ShowWindow_KR() => Open(Language.한국어);

    // Opens (or focuses) the window, optionally switching language first.
    private static void Open(Language? lang)
    {
        var win = GetWindow<FBXImportSettingsBatchTool>(false, "FBX", true);
        if (lang.HasValue)
            EditorPrefs.SetInt(PrefKey, (int)lang.Value);
        win.titleContent = new GUIContent(L[(Language)EditorPrefs.GetInt(PrefKey, 0)]["window_title"]);
        win.minSize = new Vector2(460, 520);
    }

    // ─── OnGUI ────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 14,
            alignment = TextAnchor.MiddleCenter
        };

        // -- Language selector (pinned to the top) ----------------------------
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(T("section_language"), EditorStyles.boldLabel, GUILayout.Width(100));
        Language newLang = (Language)EditorGUILayout.EnumPopup(currentLang);
        if (newLang != currentLang)
        {
            currentLang  = newLang;
            titleContent = new GUIContent(T("window_title"));
            Repaint();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        DrawHRule();

        // -- Main title -------------------------------------------------------
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(T("main_title"), titleStyle);
        EditorGUILayout.Space(4);
        DrawHRule();

        // -- Modification toggles ---------------------------------------------
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(T("section_modify"), EditorStyles.boldLabel);
        applyReadWrite        = EditorGUILayout.ToggleLeft(T("toggle_rw"),  applyReadWrite);
        applyLegacyBlendShape = EditorGUILayout.ToggleLeft(T("toggle_lbs"), applyLegacyBlendShape);

        // -- Scan scope -------------------------------------------------------
        EditorGUILayout.Space(6);
        DrawHRule();
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(T("section_scope"), EditorStyles.boldLabel);
        onlySelectedFolder = EditorGUILayout.ToggleLeft(T("toggle_folder"), onlySelectedFolder);

        // -- Run options ------------------------------------------------------
        EditorGUILayout.Space(4);
        DrawHRule();
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(T("section_options"), EditorStyles.boldLabel);
        dryRun = EditorGUILayout.ToggleLeft(T("toggle_dry"), dryRun);
        EditorGUILayout.Space(8);

        // -- Action button ----------------------------------------------------
        GUI.enabled = !isRunning && (applyReadWrite || applyLegacyBlendShape);
        Color prevColor = GUI.backgroundColor;
        GUI.backgroundColor = dryRun ? new Color(0.6f, 0.85f, 1f) : new Color(0.55f, 0.92f, 0.55f);
        if (GUILayout.Button(dryRun ? T("btn_dry") : T("btn_run"), GUILayout.Height(36)))
            RunBatch();
        GUI.backgroundColor = prevColor;
        GUI.enabled = true;

        if (!applyReadWrite && !applyLegacyBlendShape)
            EditorGUILayout.HelpBox(T("warn_no_toggle"), MessageType.Warning);

        // -- Scan statistics --------------------------------------------------
        EditorGUILayout.Space(6);
        DrawHRule();
        EditorGUILayout.Space(2);
        string stateWord = dryRun ? T("pending") : T("modified");
        EditorGUILayout.LabelField(
            string.Format(T("scan_result"), totalFound, stateWord, totalModified),
            EditorStyles.miniLabel);

        // -- Log scroll view --------------------------------------------------
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos,
            GUILayout.ExpandHeight(true), GUILayout.MinHeight(140));
        foreach (var line in logLines)
        {
            bool isWarn  = line.StartsWith(PrefixSkip)   || line.StartsWith(PrefixNoNeed);
            bool isError = line.StartsWith(PrefixError);
            bool isOk    = line.StartsWith(PrefixChange) || line.StartsWith(PrefixPreview);

            GUIStyle s = new GUIStyle(EditorStyles.miniLabel);
            if (isWarn)  s.normal.textColor = new Color(0.9f,  0.75f, 0.2f);
            if (isError) s.normal.textColor = new Color(0.95f, 0.3f,  0.3f);
            if (isOk)    s.normal.textColor = new Color(0.3f,  0.85f, 0.45f);
            EditorGUILayout.LabelField(line, s);
        }
        EditorGUILayout.EndScrollView();

        // -- Clear log button -------------------------------------------------
        EditorGUILayout.Space(4);
        if (GUILayout.Button(T("btn_clear"), GUILayout.Height(22)))
        {
            logLines.Clear();
            totalFound = totalModified = 0;
            Repaint();
        }
    }

    // ─── Batch processing ────────────────────────────────────────────────────
    private void RunBatch()
    {
        isRunning = true;
        logLines.Clear();
        totalFound = totalModified = 0;

        string[] searchFolders = GetSearchFolders();
        string[] guids = AssetDatabase.FindAssets("t:Model", searchFolders);

        string dryTag = dryRun ? T("log_dryrun_tag") : "";
        Log(string.Format(T("log_start"), dryTag, guids.Length));

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.ToLower().EndsWith(".fbx")) continue;
                totalFound++;
                ProcessFBX(path);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            if (!dryRun) AssetDatabase.Refresh();
        }

        string doneState = dryRun ? T("pending") : T("modified");
        Log(string.Format(T("log_done"), totalFound, doneState, totalModified));
        isRunning = false;
        Repaint();
    }

    private void ProcessFBX(string path)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            Log($"{PrefixError} {T("log_no_importer")}{path}");
            return;
        }

        // Read the legacy blend shape normals flag via SerializedObject.
        var so      = new SerializedObject(importer);
        var lbsProp = so.FindProperty("legacyComputeAllNormalsFromSmoothingGroupsWhenMeshHasBlendShapes");

        bool rwNeeded  = applyReadWrite        && !importer.isReadable;
        bool lbsNeeded = applyLegacyBlendShape && lbsProp != null && !lbsProp.boolValue;

        if (!rwNeeded && !lbsNeeded)
        {
            Log($"{PrefixNoNeed} {path}");
            return;
        }

        var changes = new List<string>();
        if (rwNeeded)  changes.Add(T("log_rw"));
        if (lbsNeeded) changes.Add(T("log_lbs"));

        if (!dryRun)
        {
            if (rwNeeded) importer.isReadable = true;
            if (lbsNeeded)
            {
                lbsProp.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            importer.SaveAndReimport();
            Log($"{PrefixChange} ({string.Join(" + ", changes)})  {path}");
        }
        else
        {
            Log($"{PrefixPreview} {string.Format(T("log_will_modify"), string.Join(" + ", changes), path)}");
        }

        totalModified++;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────
    private string[] GetSearchFolders()
    {
        if (!onlySelectedFolder) return null;

        var folders = new List<string>();
        foreach (Object obj in Selection.GetFiltered<Object>(SelectionMode.Assets))
        {
            string p = AssetDatabase.GetAssetPath(obj);
            if (AssetDatabase.IsValidFolder(p)) folders.Add(p);
        }

        if (folders.Count == 0)
        {
            Log($"{PrefixSkip} {T("log_no_folder")}");
            return null;
        }

        Log(string.Format(T("log_scan_folders"), folders.Count, string.Join(", ", folders)));
        return folders.ToArray();
    }

    private void Log(string msg)
    {
        logLines.Add(msg);
        Debug.Log($"[FBXBatchFix] {msg}");
    }

    private void DrawHRule()
    {
        Rect r = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.3f));
    }
}