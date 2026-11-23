# Quick Start - 0.2.0

1. 打开 `YBTools > 溶解 > Dissolve Avatar Builder`。
2. 在 Avatar 页选择 Avatar Root 与 Profile，填写每套服装的 Roots，并选择默认服装。点击 **+ 添加服装** 可配置 2 到 8 套。多套配置中，同一个 Renderer 只能分配给一套服装。
3. 分析页确认 Renderer / 材质 / ReadWrite / UV4 状态。
4. 效果页选择扫描移动方向，点击“按当前方向自动估算”。
5. 分别设置 A→B、B→A 延迟。涉及新增服装的切换沿用 A→B 的扫描前延迟。使用 Action 时，多套模式会按扫描总时长调整动作速度。
6. 生成/安装页重新生成 Baked Mesh 与材质，并正式安装。
7. 可在材质变体页安装 Face / Body 等共用 Renderer 的 A/B 材质变体。新增服装对应 A 材质。
8. Animator/MA 页生成 FX；启用 Action 时生成 Action Controller；安装 Modular Avatar 菜单。
9. 验证页在 Edit Mode 检查安装，在 Play Mode 用 **预览服装** 选择任意目标。VRChat 的多套换装子菜单按名称列出各套服装。扫描时再次选择会在当前扫描结束后切向最后选中的服装。

已有双套安装升级时，先在 **生成/安装** 页卸载现有服装资产，重新分析和生成后正式安装，再重新生成 FX、可选的 Action 和 Modular Avatar 菜单。

改变扫描方向后不要沿用旧 Baked Mesh；先卸载/恢复需要重建的安装，再重新生成与安装。
