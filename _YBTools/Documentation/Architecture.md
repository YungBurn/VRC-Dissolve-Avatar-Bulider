# Architecture

## 扫描坐标

0.2.0 仍只占用 `UV4.x / TEXCOORD3.x`，不修改已经验证稳定的 FinalBaked / Fur Shader 接口。

用户设置的是 Avatar Root 本地空间中的“扫描移动方向”。生成 Mesh 时使用其反方向作为投影轴：

`scanCoord = dot(avatarLocalPosition, -scanMoveDirection)`

Shader 继续用 `_YBScanHeight` 对这个标量坐标做 Role 互补裁剪。因此旧的从上到下方案与新任意方向方案使用同一套 Shader。

改变方向会改变 UV4.x 中的烘焙数据，必须重新生成并重新安装。

## 双向时间

- A→B：`delayAToB + scanDuration`
- B→A：`delayBToA + scanDuration`

两段 Action Clip 可以有不同长度。效果页可分别根据对应 Clip 自动计算延迟，让扫描结束与动作结束对齐。

## 定位与安装

Renderer 使用 Editor Path + Name + Type + Mesh + Material Signature 回退定位。Edit Mode 安装验证可严格比较 Asset；Play Mode / MA 构建后改用运行时兼容检查，允许 Bone Proxy / Merge Armature 改变路径和 Mesh 实例。
