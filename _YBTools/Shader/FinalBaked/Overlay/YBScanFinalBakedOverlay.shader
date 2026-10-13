Shader "YBTools/溶解/FinalBaked/Scan Overlay"
{
    Properties
    {
        [HideInInspector] _MainTex ("Source Texture", 2D) = "white" {}
        [HideInInspector] _Color ("Source Color", Color) = (1,1,1,1)
        [HideInInspector] _Cutoff ("Source Cutoff", Range(0,1)) = 0.5
        [HideInInspector] _YBScanUseCutoff ("Use Source Cutoff", Float) = 0

        [HDR] _YBScanColor ("Scan Color", Color) = (0.05, 0.85, 1.0, 1.0)
        _YBScanIntensity ("Scan Intensity", Range(0, 20)) = 4
        _YBScanHeight ("Scan Height", Float) = 1.2
        _YBScanWidth ("Scan Width", Range(0.001, 1)) = 0.08
        _YBScanSoftness ("Scan Softness", Range(0.001, 1)) = 0.08
        _YBScanRootOffset ("Avatar Root Offset", Vector) = (0,0,0,0)
        [HideInInspector] _YBScanRootWorldToLocal0 ("Root Matrix Row 0", Vector) = (1,0,0,0)
        [HideInInspector] _YBScanRootWorldToLocal1 ("Root Matrix Row 1", Vector) = (0,1,0,0)
        [HideInInspector] _YBScanRootWorldToLocal2 ("Root Matrix Row 2", Vector) = (0,0,1,0)
        [HideInInspector] _YBScanRootWorldToLocal3 ("Root Matrix Row 3", Vector) = (0,0,0,1)
        [Toggle] _YBScanUseAlpha ("Use Source Alpha", Float) = 1
        [HideInInspector] _YBScanBakedObjectToRoot1 ("Object To Avatar Root Y", Vector) = (0,1,0,0)
        [HideInInspector] _YBScanDebugUseLiveRoot ("YB Debug Live Root", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        LOD 100
        Cull Back
        ZWrite Off
        Offset -1, -1
        Blend One One

        Pass
        {
            Name "YB_SCAN_SURFACE"
            Tags { "LightMode" = "Always" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Cutoff;
            float _YBScanUseCutoff;

            fixed4 _YBScanColor;
            float _YBScanIntensity;
            float _YBScanHeight;
            float _YBScanWidth;
            float _YBScanSoftness;
            float4 _YBScanRootOffset;
            float _YBScanUseAlpha;

            float4 _YBScanRootWorldToLocal0;
            float4 _YBScanRootWorldToLocal1;
            float4 _YBScanRootWorldToLocal2;
            float4 _YBScanRootWorldToLocal3;
            float4 _YBScanBakedObjectToRoot1;
            float _YBScanDebugUseLiveRoot;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv3 : TEXCOORD3;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float rootY : TEXCOORD1;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // FinalBaked: UV4.x stores Avatar Root local Y for each source vertex.
                // It follows skinning as an interpolated vertex attribute and needs no runtime C# matrix driver.
                o.rootY = v.uv3.x + _YBScanRootOffset.y;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 source = tex2D(_MainTex, i.uv) * _Color;
                float alpha = lerp(1.0, source.a * i.color.a, saturate(_YBScanUseAlpha));
                float cutoff = lerp(0.001, _Cutoff, saturate(_YBScanUseCutoff));
                clip(alpha - cutoff);

                float distanceToScan = abs(i.rootY - _YBScanHeight);
                float edge = max(_YBScanSoftness, 0.0001);
                float band = 1.0 - smoothstep(_YBScanWidth, _YBScanWidth + edge, distanceToScan);
                band *= alpha;

                fixed3 emission = _YBScanColor.rgb * (_YBScanIntensity * band);
                return fixed4(emission, band * _YBScanColor.a);
            }
            ENDCG
        }
    }
}
