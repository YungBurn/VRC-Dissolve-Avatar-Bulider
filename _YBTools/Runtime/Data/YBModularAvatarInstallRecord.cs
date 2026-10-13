using UnityEngine;

namespace YBTools.Dissolve
{
    public sealed class YBModularAvatarInstallRecord : ScriptableObject
    {
        public int dataVersion = 1;
        public string toolVersion;
        public string avatarHint;
        public YBDissolveProfile profile;
        public GameObject generatedPrefab;
        public string installedObjectName = "溶解换装";
    }
}
