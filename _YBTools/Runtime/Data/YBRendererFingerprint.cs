using System;
using System.Collections.Generic;

namespace YBTools.Dissolve
{
    [Serializable]
    public sealed class YBRendererFingerprint
    {
        public string editorPath;
        public string rendererName;
        public string rendererType;
        public string meshName;
        public int materialSlotCount;
        public List<string> materialAssetGuids = new List<string>();
    }
}
