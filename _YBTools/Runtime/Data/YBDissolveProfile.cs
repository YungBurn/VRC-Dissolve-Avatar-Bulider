using System;
using System.Collections.Generic;
using UnityEngine;

namespace YBTools.Dissolve
{
    public enum YBOutfitDefault
    {
        OutfitA = 0,
        OutfitB = 1
    }

    public enum YBScanCurvePreset
    {
        Linear = 0,
        EaseIn = 1,
        EaseOut = 2,
        EaseInOut = 3,
        Custom = 4
    }

    public enum YBScanDirectionPreset
    {
        TopToBottom = 0,
        BottomToTop = 1,
        LeftToRight = 2,
        RightToLeft = 3,
        FrontToBack = 4,
        BackToFront = 5,
        Custom = 6
    }

    [Serializable]
    public sealed class YBOutfitBinding
    {
        public string displayName = "Outfit";
        public List<string> rootPaths = new List<string>();
    }

    [Serializable]
    public sealed class YBMaterialVariantProfile
    {
        public string rendererPath;
        public int materialSlot;
        public Material outfitAMaterial;
        public Material outfitBMaterial;
    }

    [Serializable]
    public sealed class YBScanSettings
    {
        public YBScanDirectionPreset directionPreset = YBScanDirectionPreset.TopToBottom;
        public Vector3 customDirection = Vector3.down;

        public float startHeight = 1.5f;
        public float endHeight = -0.15f;

        public float delaySeconds = 6f;
        public float delayAToBSeconds = -1f;
        public float delayBToASeconds = -1f;

        public float durationSeconds = 7f;
        public YBScanCurvePreset curvePreset = YBScanCurvePreset.Linear;
        public AnimationCurve customCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        public Color scanColor = new Color(0.05f, 0.85f, 1f, 1f);
        public float scanIntensity = 4f;
        public float scanWidth = 0.08f;
        public float scanSoftness = 0.08f;
        public float noiseStrength = 0.04f;

        public Vector3 GetMoveDirection()
        {
            Vector3 direction;
            switch (directionPreset)
            {
                case YBScanDirectionPreset.BottomToTop: direction = Vector3.up; break;
                case YBScanDirectionPreset.LeftToRight: direction = Vector3.right; break;
                case YBScanDirectionPreset.RightToLeft: direction = Vector3.left; break;
                case YBScanDirectionPreset.FrontToBack: direction = Vector3.back; break;
                case YBScanDirectionPreset.BackToFront: direction = Vector3.forward; break;
                case YBScanDirectionPreset.Custom: direction = customDirection; break;
                default: direction = Vector3.down; break;
            }

            if (direction.sqrMagnitude < 0.000001f) return Vector3.down;
            return direction.normalized;
        }

        public Vector3 GetProjectionAxis()
        {
            return -GetMoveDirection();
        }

        public float GetDelayAToB()
        {
            return Mathf.Max(0f, delayAToBSeconds >= 0f ? delayAToBSeconds : delaySeconds);
        }

        public float GetDelayBToA()
        {
            return Mathf.Max(0f, delayBToASeconds >= 0f ? delayBToASeconds : delaySeconds);
        }

        public void SetDirectionalDelays(float aToB, float bToA)
        {
            delayAToBSeconds = Mathf.Max(0f, aToB);
            delayBToASeconds = Mathf.Max(0f, bToA);
            delaySeconds = delayAToBSeconds;
        }
    }

    [Serializable]
    public sealed class YBActionSettings
    {
        public bool enabled;
        public AnimationClip outfitAToB;
        public AnimationClip outfitBToA;
        public bool reuseForwardClipWhenReverseMissing = true;
        public bool controlTracking = true;
        public bool disableLocomotion = true;
        public float blendDuration = 0.12f;
    }

    [CreateAssetMenu(
        fileName = "YBDissolveProfile",
        menuName = "YBTools/Dissolve/Dissolve Profile",
        order = 1000)]
    public sealed class YBDissolveProfile : ScriptableObject
    {
        public const int MaxOutfits = 8;

        [Header("Profile")]
        public int dataVersion = 3;
        public string avatarHint;
        public string parameterName = "YBUniform";
        public string menuName = "换装";
        public YBOutfitDefault defaultOutfit = YBOutfitDefault.OutfitA;

        [Header("Outfits")]
        public YBOutfitBinding outfitA = new YBOutfitBinding { displayName = "服装 A" };
        public YBOutfitBinding outfitB = new YBOutfitBinding { displayName = "服装 B" };
        [HideInInspector] public List<YBOutfitBinding> additionalOutfits = new List<YBOutfitBinding>();
        [HideInInspector] public int additionalDefaultOutfit = -1;

        public int OutfitCount => 2 + (additionalOutfits?.Count ?? 0);
        public bool HasMultipleOutfits => OutfitCount > 2;
        public int DefaultOutfitIndex => additionalDefaultOutfit >= 2 && additionalDefaultOutfit < OutfitCount
            ? additionalDefaultOutfit : (defaultOutfit == YBOutfitDefault.OutfitB ? 1 : 0);

        public YBOutfitBinding GetOutfit(int index)
        {
            if (index == 0) return outfitA;
            if (index == 1) return outfitB;
            return additionalOutfits[index - 2];
        }

        public static string OutfitGroup(int index)
        {
            return "Outfit " + (char)('A' + index);
        }

        public string OutfitName(int index)
        {
            string label = GetOutfit(index)?.displayName;
            return string.IsNullOrWhiteSpace(label) ? OutfitGroup(index) : label.Trim();
        }

        public float GetTransitionDelay(int from, int to)
        {
            return from == 1 && to == 0 ? scan.GetDelayBToA() : scan.GetDelayAToB();
        }

        [Header("Scan")]
        public YBScanSettings scan = new YBScanSettings();

        [Header("Optional Action")]
        public YBActionSettings action = new YBActionSettings();

        [Header("Material Scan Variants")]
        public List<YBMaterialVariantProfile> materialVariants = new List<YBMaterialVariantProfile>();

        [Header("Unsupported Material Overrides")]
        public List<YBUnsupportedMaterialOverride> unsupportedMaterialOverrides = new List<YBUnsupportedMaterialOverride>();
    }
}
