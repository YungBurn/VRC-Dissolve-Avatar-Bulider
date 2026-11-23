# Troubleshooting

## PCSS4VRC 的衣服投影先于溶解动画切换

更新 `_YBTools/Shader/FinalBaked` 并等 Unity 重新导入。新版 ShadowCaster 与衣服表面使用同一套溶解裁剪，PCSS 灯光可从更新后的阴影图读取逐步变化的衣服投影。现有 FinalBaked 材质和动画引用可直接使用。

测试时确认衣服 Renderer 开启了 Cast Shadows，PCSS 灯光包含其 Layer，并且其他换装 Animator 在扫描中没有提前关闭衣服对象或 Renderer。

## PCSS4VRC 光强滑杆只影响身体

旧版生成的衣服材质使用普通 lilToon 的受光方式。更新插件后，在 **验证** 页点击 **原位更新已安装服装的 PCSS 受光**。它会更新当前 Avatar 已安装的不透明、镂空和半透明溶解材质，保留原材质资产引用和扫描动画。衣服材质的 Shader 名称应以 `YBTools/PCSS4VRC/` 开头。看到材质更新数量后，再构建或上传 Avatar。

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
