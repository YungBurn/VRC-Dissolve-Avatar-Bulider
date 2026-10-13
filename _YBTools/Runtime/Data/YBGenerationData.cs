using System;
using System.Collections.Generic;
using UnityEngine;

namespace YBTools.Dissolve
{
    public enum YBUnsupportedMaterialAction
    {
        Block = 0,
        Skip = 1,
        ForceOpaque = 2,
        ForceCutout = 3,
        ForceTransparent = 4,
        ForceRefraction = 5,
        ForceRefractionBlur = 6,
        ForceFur = 7,
        ForceFurCutout = 8,
        ForceFurTwoPass = 9,
        ForceGem = 10
    }

    [Serializable]
    public sealed class YBUnsupportedMaterialOverride
    {
        public string key;
        public string displayName;
        public Material material;
        public YBUnsupportedMaterialAction action = YBUnsupportedMaterialAction.Block;
    }

    [Serializable]
    public sealed class YBGeneratedMaterialSlotRecord
    {
        public int slotIndex;
        public Material sourceMaterial;
        public string sourceShader;
        public string resolvedMode;
        public string generationAction;
        public Material dissolveMaterial;
        public Material overlayMaterial;
        public string note;
    }

    [Serializable]
    public sealed class YBGeneratedRendererRecord
    {
        public string outfitGroup;
        public string editorPath;
        public string rendererName;
        public string rendererType;
        public string meshName;
        public bool maPathRisk;
        public Mesh sourceMesh;
        public Mesh bakedMesh;
        public List<YBGeneratedMaterialSlotRecord> materialSlots = new List<YBGeneratedMaterialSlotRecord>();
    }

}
