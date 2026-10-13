# Quick Start - 0.2.0

1. 打开 `YBTools > 溶解 > Dissolve Avatar Builder`。
2. 在 Avatar 页选择 Avatar Root、Outfit A/B Roots 与 Profile。
3. 分析页确认 Renderer / 材质 / ReadWrite / UV4 状态。
4. 效果页选择扫描移动方向，点击“按当前方向自动估算”。
5. 分别设置 A→B、B→A 延迟；如使用 Action，可分别点击“让扫描结束对齐动作”。
6. 生成/安装页重新生成 Baked Mesh 与材质，并正式安装。
7. 材质变体页安装 Face / Body 等 A/B 材质变体。
8. Animator/MA 页生成 FX；启用 Action 时生成 Action Controller；安装 Modular Avatar 菜单。
9. 验证页在 Edit Mode 检查安装，在 Play Mode 检查 MA 重定位与双向扫描。

改变扫描方向后不要沿用旧 Baked Mesh；先卸载/恢复需要重建的安装，再重新生成与安装。
