# Baked Scan Mesh

`YBBakedMeshBuilder.cs` 复制源 Mesh，把顶点在 Avatar Root 本地空间中的扫描投影写入 `UV4.x` (`TEXCOORD3.x`)：

`scanCoord = dot(avatarLocalPosition, -scanMoveDirection)`

- 默认拒绝覆盖已经存在的 UV4；确需覆盖时，请先确认其他 Shader/工具不依赖 UV4.x。
- 允许覆盖时仅改写 x，尽量保留 y/z/w。
- 生成资产不会自动安装到 Avatar。
- **改变扫描方向后必须重建 Baked Mesh 并重新安装相关资源。**
