// 墙体 / 地面材质。
//
// 两种用途：
//   正式  —— 纯黑，不参与光照，只负责挡住深度、被射线命中（全黑迷宫）
//   调试  —— 把 _Color 调成灰色即可看见关卡形状；_DebugLit = 1 时启用一套很弱的
//            漫反射 + 环境光，纯粹为了看清几何，正式版请设为 0。
Shader "EchoPuzzle/Matte"
{
    Properties
    {
        _Color     ("Color", Color)                = (0, 0, 0, 1)
        _DebugLit  ("Debug Lit", Range(0, 1))      = 0
        _DebugLight("Debug Light Dir", Vector)     = (0.35, 0.8, 0.45, 0)
        // 0 = Back（默认，背面剔除）；2 = Off（双面渲染）
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Matte"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4  _Color;
                float  _DebugLit;
                float4 _DebugLight;
                float  _Cull;
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
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                if (_DebugLit < 0.5) return _Color;

                // 只用于调试：极简光照，让几何形状可读
                float3 n = normalize(IN.normalWS);
                float3 l = normalize(_DebugLight.xyz);
                float  ndl = saturate(dot(n, l)) * 0.65 + 0.35;

                float3 ambient = unity_AmbientSky.rgb * 0.5 + 0.04;
                float3 col = _Color.rgb * (ndl + ambient);

                // 给三个轴向上一点颜色区分，便于判断朝向
                col += float3(saturate(n.y) * 0.03, saturate(-n.z) * 0.02, saturate(n.x) * 0.02);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
