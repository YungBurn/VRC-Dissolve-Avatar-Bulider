# Troubleshooting

## 改变扫描方向后验证失败
方向属于 Baked Mesh 数据。重新生成 Baked Mesh，并重新安装主扫描；使用材质变体时也需要重新构建材质变体 Mesh。

## Edit Mode 38/38，Play Mode 提示 Mesh / Overlay 不一致
MA / NDMF 可能移动 Renderer 或重建 Mesh 实例。0.2.0 的 Play Mode 验证使用兼容检查，不应再要求 Asset 引用完全相同。

## MA Bone Proxy 后找不到发型或配件
使用运行时 Renderer 重定位诊断。路径变化本身不是错误；只有 Fingerprint 也无法匹配才算缺失。

## Fur / FurTwoPass 不随扫描
确认使用当前 FinalBaked Fur Shader，并重新生成对应材质。Fur Geometry Pass 必须把 UV4.x 扫描坐标传入可见 Fragment。

## Action State 进入但身体不动
Action Controller 必须包含 VRCPlayableLayerControl；需要身体接管时还应包含 VRCAnimatorTrackingControl，并可使用 VRCAnimatorLocomotionControl。

## A→B 与 B→A 动作长度不同
在效果页分别设置两个方向的扫描前延迟，或使用各自的“让扫描结束对齐动作”。
