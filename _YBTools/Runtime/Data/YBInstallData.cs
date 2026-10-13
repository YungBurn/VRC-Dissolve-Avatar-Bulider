using System;
using System.Collections.Generic;
using UnityEngine;

namespace YBTools.Dissolve
{
    [Serializable]
    public sealed class YBInstalledActiveStateRecord
    {
        public string transformPath;
        public bool originalActiveSelf;
    }

    [Serializable]
    public sealed class YBInstalledRendererRecord
    {
        public string manifestEditorPath;
        public string installedRendererPath;
        public string rendererName;
        public string rendererType;
        public bool originalEnabled;

        public Mesh originalMesh;
        public List<Material> originalMaterials = new List<Material>();

        public Mesh installedBakedMesh;
        public List<Material> installedMaterials = new List<Material>();

        public string overlayObjectPath;
        public string overlayObjectName;
        public Mesh overlayMesh;
        public List<Material> overlayMaterials = new List<Material>();
    }

}
