using System.Collections.Generic;
using UnityEngine;

namespace YBTools.Dissolve
{
    public sealed class YBGeneratedAssetManifest : ScriptableObject
    {
        public int dataVersion = 2;
        public string toolVersion;
        public string generatedUtc;
        public string avatarHint;
        public YBDissolveProfile profile;

        public int bakedScanDataVersion = 2;
        public Vector3 bakedScanMoveDirection = Vector3.down;

        public int sourceRendererCount;
        public int generatedMeshCount;
        public int generatedDissolveMaterialCount;
        public int generatedOverlayMaterialCount;
        public int skippedSlotCount;
        public List<YBGeneratedRendererRecord> renderers = new List<YBGeneratedRendererRecord>();
    }
}
