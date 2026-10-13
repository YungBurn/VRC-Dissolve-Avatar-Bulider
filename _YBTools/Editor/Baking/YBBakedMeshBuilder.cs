using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace YBTools.Dissolve.Editor
{
    internal static class YBBakedMeshBuilder
    {
        public static Mesh BuildOrUpdate(
            Transform avatarRoot,
            Renderer renderer,
            Mesh source,
            string assetPath,
            bool allowOverwriteUv4)
        {
            return BuildOrUpdate(avatarRoot, renderer, source, assetPath, allowOverwriteUv4, Vector3.down);
        }

        public static Mesh BuildOrUpdate(
            Transform avatarRoot,
            Renderer renderer,
            Mesh source,
            string assetPath,
            bool allowOverwriteUv4,
            Vector3 scanMoveDirection)
        {
            if (avatarRoot == null) throw new ArgumentNullException("avatarRoot");
            if (renderer == null) throw new ArgumentNullException("renderer");
            if (source == null) throw new InvalidOperationException("Renderer 缺少 Mesh：" + renderer.name);
            if (!source.isReadable) throw new InvalidOperationException("Mesh Read/Write 未开启：" + source.name);

            bool occupied = source.HasVertexAttribute(VertexAttribute.TexCoord3);
            if (occupied && !allowOverwriteUv4)
                throw new InvalidOperationException("UV4/TEXCOORD3 已被占用，未允许覆盖：" + source.name);

            Vector3 moveDirection = scanMoveDirection.sqrMagnitude < 0.000001f
                ? Vector3.down
                : scanMoveDirection.normalized;
            Vector3 projectionAxis = -moveDirection;

            Mesh baked = UnityEngine.Object.Instantiate(source);
            baked.name = source.name + "_YBBakedScanCoord";

            Vector3[] vertices = source.vertices;
            var uv4 = new List<Vector4>(vertices.Length);
            if (occupied)
            {
                source.GetUVs(3, uv4);
                if (uv4.Count != vertices.Length)
                {
                    uv4.Clear();
                    for (int i = 0; i < vertices.Length; i++) uv4.Add(Vector4.zero);
                }
            }
            else
            {
                for (int i = 0; i < vertices.Length; i++) uv4.Add(Vector4.zero);
            }

            Matrix4x4 objectToAvatar = avatarRoot.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 avatarLocal = objectToAvatar.MultiplyPoint3x4(vertices[i]);
                Vector4 packed = uv4[i];
                packed.x = Vector3.Dot(avatarLocal, projectionAxis);
                uv4[i] = packed;
            }

            baked.SetUVs(3, uv4);
            baked.UploadMeshData(false);
            return SaveOrUpdateMesh(baked, assetPath);
        }

        private static Mesh SaveOrUpdateMesh(Mesh temporary, string assetPath)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(temporary, existing);
                existing.name = temporary.name;
                UnityEngine.Object.DestroyImmediate(temporary);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            UnityEngine.Object occupied = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (occupied != null) AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(temporary, assetPath);
            return temporary;
        }
    }
}
