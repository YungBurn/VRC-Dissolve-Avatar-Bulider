using System.Collections.Generic;
using UnityEngine;

namespace YBTools.Dissolve
{
    // Same-named file keeps the ScriptableObject MonoScript reference stable across domain reloads.
    public sealed class YBAvatarInstallRecord : ScriptableObject
    {
        public int dataVersion = 1;
        public string toolVersion;
        public string installedUtc;
        public string avatarHint;
        public YBDissolveProfile profile;
        public YBGeneratedAssetManifest generationManifest;
        public int installedRendererCount;
        public int installedMaterialSlotCount;
        public int overlayRendererCount;
        public List<YBInstalledRendererRecord> renderers = new List<YBInstalledRendererRecord>();
        public List<YBInstalledActiveStateRecord> activatedObjects = new List<YBInstalledActiveStateRecord>();
    }
}
