# YB Dissolve Avatar Builder

**版本：** 0.2.0  
**作者：** YungBurn  
**许可：** MIT License（原创部分；详见 `Documentation/YBTools-MIT-LICENSE.txt`）

Unity Editor / VRChat Avatar 的 lilToon 扫描溶解换装工具，具有中文、English、日本語界面。首次打开默认中文；切换后通过 Unity `EditorPrefs` 记住语言。

## 功能

- Outfit A / B 多 Root、Renderer 分析与安装验证
- Avatar 本地任意方向扫描、Mesh `UV4.x` 数据烘焙及双向独立延迟
- lilToon Opaque / Cutout / Transparent / Refraction / Fur / Gem 等材质模式
- Overlay、Face / Body 材质变体、FX、可选 VRChat Action 与 Modular Avatar 配置
- 可逆安装、验证及还原

## 安装

1. 在 Unity 工程中安装 lilToon；使用对应功能时再安装 VRChat SDK Avatars 与 Modular Avatar。
2. 将完整的 `_YBTools/` 目录放入 Unity 工程 `Assets/_YBTools/`，保留 `.meta` 与目录名。
3. 打开 `YBTools > 溶解 > Dissolve Avatar Builder`。
4. 参见 `Documentation/QuickStart.md` 完成生成与安装。

生成资源位于 `Assets/_YBToolsGenerated/`，不属于源码包。改变扫描方向后必须重新生成相关 Baked Mesh。

## 许可与第三方组件

YungBurn 原创代码及文档：**Copyright (c) 2026 YungBurn，MIT License**。许可文本位于 `Documentation/YBTools-MIT-LICENSE.txt`。
再分发时须保留原版权声明与许可文本。

部分 Shader 改编或引用 lilToon，请同时保留 lilToon 原版权和 MIT 协议：见 `Documentation/ThirdPartyNotices.md` 与 `Documentation/lilToon-MIT-LICENSE.txt`。lilToon 需单独安装。

注意：源码静态检查不等于 Unity / VRChat 全功能实机测试；公开发布前建议按 `Documentation/ReleaseChecklist.md` 验证。
