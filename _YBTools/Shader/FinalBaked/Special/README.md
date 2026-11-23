# Special lilToon Rendering Modes

支持 Refraction、Refraction Blur、Fur、Fur Cutout、Fur TwoPass 与 Gem。

扫描标量来自 Mesh UV4.x / TEXCOORD3.x。Fur Geometry Pass 把该标量传给可见 Fragment。Built-in Render Pipeline 的 ShadowCaster 从基础网格直接读取该标量，与可见 Pass 使用相同的 UV0 噪声和扫描阈值。
