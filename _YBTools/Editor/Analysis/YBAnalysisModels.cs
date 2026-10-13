using System;
using System.Collections.Generic;
using UnityEngine;

namespace YBTools.Dissolve.Editor
{
    internal enum YBAnalysisSeverity
    {
        Passed,
        Warning,
        Blocking
    }

    internal enum YBMaterialRenderMode
    {
        Opaque,
        Cutout,
        Transparent,
        Refraction,
        RefractionBlur,
        Fur,
        FurCutout,
        FurTwoPass,
        Gem,
        Unsupported,
        Missing
    }

    [Serializable]
    internal sealed class YBMaterialSlotAnalysis
    {
        public int slotIndex;
        public Material material;
        public string materialName;
        public string shaderName;
        public string assetPath;
        public string assetGuid;
        public YBMaterialRenderMode renderMode;
        public string note;
    }

    [Serializable]
    internal sealed class YBRendererAnalysis
    {
        public string outfitGroup;
        public Renderer renderer;
        public string hierarchyPath;
        public string rendererName;
        public string rendererType;
        public Mesh mesh;
        public string meshName;
        public bool meshReadable;
        public bool uv4Occupied;
        public bool maPathRisk;
        public bool ambiguousFingerprint;
        public List<YBMaterialSlotAnalysis> materials = new List<YBMaterialSlotAnalysis>();
    }

    [Serializable]
    internal sealed class YBAnalysisMessage
    {
        public YBAnalysisSeverity severity;
        public string title;
        public string detail;
    }

    [Serializable]
    internal sealed class YBAnalysisReport
    {
        public readonly List<YBRendererAnalysis> renderers = new List<YBRendererAnalysis>();
        public readonly List<YBAnalysisMessage> messages = new List<YBAnalysisMessage>();

        public int RendererCount => renderers.Count;
        public int MaterialSlotCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < renderers.Count; i++) count += renderers[i].materials.Count;
                return count;
            }
        }

        public int BlockingCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < messages.Count; i++) if (messages[i].severity == YBAnalysisSeverity.Blocking) count++;
                return count;
            }
        }

        public int WarningCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < messages.Count; i++) if (messages[i].severity == YBAnalysisSeverity.Warning) count++;
                return count;
            }
        }
    }
}
