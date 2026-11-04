// 变色球专用的「顶点色透明」着色器。
//
// 为什么要单独的 shader：
//   Sprites/Default、Particles/Standard Unlit 都是**内置管线**的 shader，
//   在 URP 下 Shader.Find 会返回 null，new Material(null) 直接抛异常。
//
// 这个 shader：
//   · URP 兼容（Tags 里写了 UniversalPipeline）
//   · 直接用**顶点色**着色（顶点色由 SenseSphereVisual 逐顶点算出来）
//   · 透明混合、不写深度、双面（Cull Off）
//   · Unlit —— 不受光照影响，保证红色是"纯红"
Shader "EchoPuzzle/VertexColor"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "VertexColor"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color      = IN.color * _Color;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return IN.color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
