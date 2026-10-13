# 安装、依赖与升级

## 安装位置

将整个 `_YBTools/` 目录复制到 Unity 工程的 `Assets/_YBTools/`，包括 `.meta` 文件。
这个路径是工具中固定的 Shader 重导入路径和资产路径约定；请勿改名或安装在其他位置。

**不要**把本包复制到 `Assets/_YBToolsGenerated/`；后者由工具为 Avatar / Profile 生成。

## 外部依赖

| 组件 | 用途 | 是否需要 |
| --- | --- | --- |
| Unity Editor（VRChat 项目配套版本） | 工具运行 | 必须 |
| lilToon (`jp.lilxyzw.liltoon`) | Shader 引用、材质转换 | 核心必需 |
| VRChat SDK Avatars | Action Controller | 使用 Action 时需要 |
| Modular Avatar | 自动配置 FX / Action / Parameters / Menu | 使用 MA 自动安装时需要 |

核心生成不应强制要求 VRChat SDK 或 Modular Avatar；对应功能会在 Builder 中检查依赖并报告缺失。
Shader 明确引用 `Packages/jp.lilxyzw.liltoon/Shader/Includes/`，请确保该 UPM 包路径可用。

## 从旧版升级

1. 备份 Unity 工程及 Avatar 场景/Prefab，先完成当前工具生成资产的必要备份或卸载。
2. 对于旧版 **0.1.x**：先移除旧版遗留的 `YBDissolveAnimatorFX016Window.cs`、`YBLegacyWindowCleanup.cs`、`YBDissolveLilToonInspector.cs`，然后复制本版 `_YBTools/` 覆盖现有同名文件；仅覆盖文件并不会自动删除旧文件。
3. 保留本包自带 `.meta`，避免脚本 / Shader GUID 因重新创建而改变。
4. 打开 Builder，按当前方向重新生成 Mesh / 材质并执行安装验证。
5. 新包不包含 `_YBToolsGenerated/`。已有生成资源应视作工程数据，升级期间不要随手删除。

本包保留 `Shader/LegacyRootMatrix`，用于旧工程兼容；新功能走 `FinalBaked`。
