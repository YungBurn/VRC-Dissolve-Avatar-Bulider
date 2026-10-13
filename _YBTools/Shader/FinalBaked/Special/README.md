# Special lilToon Rendering Modes

支持 Refraction、Refraction Blur、Fur、Fur Cutout、Fur TwoPass 与 Gem。

扫描标量来自 Mesh UV4.x / TEXCOORD3.x。Fur Geometry Pass 会把该标量传递到可见 Fragment；ShadowCaster 保持兼容路径，不强制访问不存在的 UV3 字段。
