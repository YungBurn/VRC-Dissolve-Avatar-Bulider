# Material Support

当前自动支持 lilToon：

- Opaque
- Cutout
- Transparent
- Refraction
- Refraction Blur
- Fur
- Fur Cutout
- Fur TwoPass
- Gem

普通模式使用 FinalBaked Shader；特殊模式保持 lilToon 对应渲染路径并加入 YB scan clip。FakeShadow、Tessellation 和未知第三方派生 Shader 默认不自动转换。
