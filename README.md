# YB Dissolve Avatar Builder

[**简体中文**](README.md) | [**English**](README.en.md) | [**日本語**](README.ja.md)

**版本：** 0.2.0 · **作者：** YungBurn · **许可协议：** [MIT License](LICENSE)

为 **Unity / VRChat Avatar** 制作的服装扫描溶解与换装辅助工具。通过编辑器窗口分析两套 Outfit、生成扫描所需 Mesh 与材质，并按需配置 Animator、VRChat Action Controller 和 Modular Avatar。

> 本仓库包含可编辑源码、Shader、说明文档和 Unity `.meta` 文件，不包含 Unity 完整工程、Avatar 模型或用户生成的资源。

## 功能特点

- **双套服装切换：** 处理 Outfit A / B 的多个 Root 与 Renderer，提供分析和安装验证。
- **定向扫描溶解：** 在 Avatar Root 本地空间设置扫描移动方向，烘焙扫描坐标至 `UV4.x`；修改方向后需重新烘焙 Mesh。
- **双向独立控制：** A→B、B→A 可分别设置扫描前延迟，并与动作时长对齐。
- **材质与效果：** 支持扫描边缘、Overlay、Face / Body 材质变体，以及多种 lilToon 渲染模式。
- **自动化配置：** 支持生成 FX Controller、可选的 VRChat Action Controller 与 Modular Avatar 菜单/参数配置。
- **可逆工作流：** 提供安装、验证、卸载及还原相关工具。
- **三语言界面：** 中文（首次默认）、English、日本語；通过 Unity `EditorPrefs` 记住上次选择的语言。

## 依赖

| 组件 | 用途 | 需求 |
| --- | --- | --- |
| Unity Editor | 运行编辑器工具 | 必需；使用与你的 VRChat 项目兼容的 Unity 版本 |
| [lilToon](https://github.com/lilxyzw/lilToon)（`jp.lilxyzw.liltoon`） | Shader Includes / ShaderGUI 及材质转换 | 必需 |
| VRChat SDK Avatars | VRChat Action 等相关功能 | 使用对应功能时需要 |
| [Modular Avatar](https://modular-avatar.nadena.dev/) | 自动配置 FX、Parameters、Menu | 使用 MA 相关功能时需要 |

**说明：** 工具中的 Shader 引用了 `Packages/jp.lilxyzw.liltoon/Shader/Includes/`；请确保依赖按对应包路径正确安装。本仓库**不包含 lilToon 的完整发行包**。

## 安装

1. 在 Unity 项目中安装所需依赖，至少包含 lilToon。
2. 下载或克隆此仓库，把仓库内的 **`_YBTools/`** 目录完整复制到你的 Unity 项目 **`Assets/_YBTools/`**。
3. 保留目录名以及所有 `.meta` 文件；**不要**将其改放到 Unity `Packages/` 或其他目录中。
4. 等待 Unity 导入和编译完成，在菜单中打开 **`YBTools > 溶解 > Dissolve Avatar Builder`**。

## 快速开始

1. 在 **Avatar** 页指定 Avatar Root、Outfit A / B 的 Root 与 Profile。
2. 在 **分析** 页检查 Renderer、材质、Read/Write、UV4 等兼容状态。
3. 在 **效果** 页设置扫描方向、范围及 A→B / B→A 的扫描延迟。
4. 在 **生成/安装** 页创建 Baked Mesh 与材质，确认检查结果后执行安装。
5. 可选：在 **材质变体** 页配置 Face / Body 变体，在 **Animator/MA** 页生成 FX、Action 和 MA 配置。
6. 在 **验证** 页检查安装；需要时在 Unity Play Mode / VRChat 测试环境中进一步验证。

**重要：** 更改扫描方向后，旧 Mesh 中的 `UV4.x` 不会自动更新，请重新生成相关 Baked Mesh 并重新安装。

## 目录说明

```text
.
├── README.md / README.en.md / README.ja.md
├── LICENSE                        # YungBurn 原创部分的 MIT 授权
├── THIRD_PARTY_NOTICES.md         # 上游许可说明
└── _YBTools/                      # 复制到 Unity Assets/ 下
    ├── Editor/                    # 编辑器窗口、生成器、安装器
    ├── Runtime/                   # 配置与安装记录的数据定义
    ├── Shader/                    # 扫描 Shader（包含兼容版本）
    ├── Data/Schema/               # 版本与 Shader 配置
    └── Documentation/             # 安装、使用和第三方许可
```

更多细节：[`QuickStart.md`](_YBTools/Documentation/QuickStart.md) · [`Installation.md`](_YBTools/Documentation/Installation.md) · [`MaterialSupport.md`](_YBTools/Documentation/MaterialSupport.md) · [`Troubleshooting.md`](_YBTools/Documentation/Troubleshooting.md)

## 开源许可与署名

本项目中 **YungBurn 原创的代码及文档**采用 [MIT License](LICENSE)，版权所有 **© 2026 YungBurn**。

你可以使用、修改、复制、再分发和商业使用这些内容。**再分发软件的全部或实质部分时，必须保留原始版权声明和 MIT 许可文本**，以保留对作者 YungBurn 的法定署名信息。欢迎在二次开发的 README 中额外注明“Based on YB Dissolve Avatar Builder by YungBurn”，但 MIT 本身并不强制在软件界面、广告或 README 中额外显示署名。

部分 Shader 涉及 lilToon 的上游代码或实现，**仍须保留 lilToon 原有 MIT 许可与版权声明**。详见 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)；不得将全部上游内容冒称为 YungBurn 的独立原创。

## 兼容性与反馈

请根据项目所使用的 Unity、VRChat SDK、lilToon 和 Modular Avatar 版本进行实际编译与测试。本仓库的打包整理和静态核对**不代表已经完成 Unity/VRChat 端到端兼容性测试**。

发现问题时，请在 GitHub 仓库的 Issues 中尽量附上复现步骤、Unity/依赖版本和相关 Console 日志（移除私人信息）。
