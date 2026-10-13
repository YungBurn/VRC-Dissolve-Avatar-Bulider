# Installer

Installer 根据 Generation Manifest 执行可逆安装：替换 Baked Scan Mesh、Dissolve 材质并创建 Overlay Renderer，同时保存 InstallRecord 以便恢复。

Edit Mode 验证严格检查 Asset；Play Mode / MA 构建后使用兼容验证与 Renderer 重定位。
