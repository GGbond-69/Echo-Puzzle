// 角色轮廓线：只画发光描边，内部保持全黑。
// Pass 1: 剔除正面 + 沿法线外扩 -> 得到一圈外壳（描边）
// Pass 2: 正常绘制黑体，把外壳内侧盖掉，只留下边缘辉光
Shader "EchoPuzzle/Outline"
{
    Properties
    {
        _OutlineColor  ("Outline Color", Color)   = (1, 1, 1, 1)
        _OutlineWidth  ("Outline Width", Range(0, 0.2)) = 0.022
        _Glow          ("Glow", Range(0, 8))      = 2.0
        _BodyColor     ("Body Color", Color)      = (0, 0, 0, 1)
        _RimPower      ("Rim Power", Range(0.5, 8)) = 2.5
        _RimStrength   ("Rim Strength", Range(0, 4)) = 1.1
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // ---------------- Pass 1: 外扩描边 ----------------
        Pass
        {
            Name "OutlineShell"
            Tags { "LightMode" = "UniversalForward" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
                float _Glow;
                half4 _BodyColor;
                float _RimPower;
                float _RimStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 posOS = IN.positionOS.xyz + normalize(IN.normalOS) * _OutlineWidth;
                OUT.positionCS = TransformObjectToHClip(posOS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return half4(_OutlineColor.rgb * _Glow, 1.0);
            }
            ENDHLSL
        }

        // ---------------- Pass 2: 黑色本体 + 边缘光 ----------------
        Pass
        {
            Name "Body"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
                float _Glow;
                half4 _BodyColor;
                float _RimPower;
                float _RimStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewWS     : TEXCOORD1;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewWS     = GetWorldSpaceViewDir(p.positionWS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewWS);
                float  fres = pow(saturate(1.0 - saturate(dot(n, v))), _RimPower);

                float3 col = _BodyColor.rgb + _OutlineColor.rgb * fres * _RimStrength * _Glow;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
