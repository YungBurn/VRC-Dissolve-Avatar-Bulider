using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YBTools.Dissolve.Editor
{
    internal sealed class YBRendererLocateResult
    {
        public Renderer renderer;
        public int score;
        public bool exactPath;
        public bool ambiguous;
        public string reason;
    }

    internal static class YBRendererLocator
    {
        public static YBRendererFingerprint BuildFingerprint(Transform avatarRoot, Renderer renderer)
        {
            if (avatarRoot == null || renderer == null) return null;
            var mesh = GetMesh(renderer);
            var fp = new YBRendererFingerprint
            {
                editorPath = YBPathUtility.GetRelativePath(avatarRoot, renderer.transform),
                rendererName = renderer.name,
                rendererType = renderer.GetType().FullName,
                meshName = mesh != null ? mesh.name : string.Empty,
                materialSlotCount = renderer.sharedMaterials.Length
            };

            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var path = materials[i] != null ? AssetDatabase.GetAssetPath(materials[i]) : string.Empty;
                fp.materialAssetGuids.Add(string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path));
            }
            return fp;
        }

        public static YBRendererLocateResult Locate(Transform avatarRoot, YBRendererFingerprint fp)
        {
            if (avatarRoot == null || fp == null) return new YBRendererLocateResult { reason = "Avatar Root / Fingerprint 为空" };

            if (!string.IsNullOrEmpty(fp.editorPath))
            {
                var exact = avatarRoot.Find(fp.editorPath);
                if (exact != null)
                {
                    var r = exact.GetComponent<Renderer>();
                    if (r != null && TypeMatches(r, fp))
                        return new YBRendererLocateResult { renderer = r, score = 10000, exactPath = true, reason = "原路径命中" };
                }
            }

            var candidates = avatarRoot.GetComponentsInChildren<Renderer>(true);
            var scored = new List<YBRendererLocateResult>();
            for (int i = 0; i < candidates.Length; i++)
            {
                var r = candidates[i];
                int score = Score(r, fp);
                if (score <= 0) continue;
                scored.Add(new YBRendererLocateResult { renderer = r, score = score, reason = "Fingerprint fallback" });
            }

            if (scored.Count == 0) return new YBRendererLocateResult { reason = "没有匹配 Renderer" };
            scored.Sort((a, b) => b.score.CompareTo(a.score));
            var best = scored[0];
            if (scored.Count > 1 && scored[1].score == best.score)
            {
                best.ambiguous = true;
                best.reason = "最高分存在并列候选，需要 Material Signature / 手动 Override";
            }
            return best;
        }

        private static int Score(Renderer r, YBRendererFingerprint fp)
        {
            int score = 0;
            if (r == null) return score;
            if (r.name == fp.rendererName) score += 120;
            if (TypeMatches(r, fp)) score += 120;

            var mesh = GetMesh(r);
            if (mesh != null && mesh.name == fp.meshName) score += 220;

            var mats = r.sharedMaterials;
            if (mats.Length == fp.materialSlotCount) score += 80;

            int exactGuidPositions = 0;
            int guidMatches = 0;
            for (int i = 0; i < mats.Length; i++)
            {
                var path = mats[i] != null ? AssetDatabase.GetAssetPath(mats[i]) : string.Empty;
                var guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) continue;
                if (i < fp.materialAssetGuids.Count && guid == fp.materialAssetGuids[i]) exactGuidPositions++;
                if (fp.materialAssetGuids.Contains(guid)) guidMatches++;
            }
            score += exactGuidPositions * 60;
            score += guidMatches * 20;
            return score;
        }

        private static bool TypeMatches(Renderer r, YBRendererFingerprint fp)
        {
            return r != null && (r.GetType().FullName ?? r.GetType().Name) == fp.rendererType;
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            var smr = renderer as SkinnedMeshRenderer;
            if (smr != null) return smr.sharedMesh;
            var mf = renderer.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }
    }
}
