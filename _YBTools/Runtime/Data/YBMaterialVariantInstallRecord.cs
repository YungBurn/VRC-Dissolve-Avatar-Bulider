using System;
using System.Collections.Generic;
using UnityEngine;

namespace YBTools.Dissolve
{
    [Serializable]
    public sealed class YBMaterialVariantRendererRecord
    {
        public string rendererPath;
        public string rendererName;
        public Mesh originalMesh;
        public List<Material> originalMaterials = new List<Material>();
        public Mesh generatedMesh;
        public int sourceSlot;
        public int appendedSlot;
        public Material outfitARole0;
        public Material outfitARole1;
        public Material outfitBRole0;
        public Material outfitBRole1;
    }

    public sealed class YBMaterialVariantInstallRecord : ScriptableObject
    {
        public int dataVersion = 1;
        public string toolVersion;
        public string avatarHint;
        public YBDissolveProfile profile;
        public List<YBMaterialVariantRendererRecord> renderers = new List<YBMaterialVariantRendererRecord>();
    }
}
