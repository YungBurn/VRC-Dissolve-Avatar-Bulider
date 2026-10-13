using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YBTools.Dissolve.Editor
{
    internal sealed class YBDependencyStatus
    {
        public bool lilToon;
        public bool vrcSdkAvatars;
        public bool modularAvatar;
        public bool finalBakedShaders;

        public bool CoreReady => lilToon && finalBakedShaders;
    }

    internal static class YBDependencyDetector
    {
        private const string VrcDescriptorType = "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor";
        private const string MaMergeAnimatorType = "nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator";

        public static YBDependencyStatus Detect()
        {
            return new YBDependencyStatus
            {
                lilToon = AssetDatabase.IsValidFolder("Packages/jp.lilxyzw.liltoon") || Shader.Find("lilToon") != null,
                vrcSdkAvatars = HasLoadedType(VrcDescriptorType),
                modularAvatar = HasLoadedType(MaMergeAnimatorType),
                finalBakedShaders =
                    Shader.Find("YBTools/溶解/FinalBaked/Hair Dissolve") != null &&
                    Shader.Find("YBTools/溶解/FinalBaked/Hair Dissolve Cutout") != null &&
                    Shader.Find("YBTools/溶解/FinalBaked/Hair Dissolve Transparent") != null &&
                    Shader.Find("YBTools/溶解/FinalBaked/Scan Overlay") != null
            };
        }

        public static bool HasLoadedType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetType(fullName, false) != null) return true;
                }
                catch
                {
                    // Some third-party assemblies can throw while Unity reloads scripts.
                }
            }
            return false;
        }
    }
}
