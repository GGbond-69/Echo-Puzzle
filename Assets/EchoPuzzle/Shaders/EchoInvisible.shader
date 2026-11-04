// 完全不可见的碰撞体材质。
//
// 用一个真正"什么都不画"的 Pass，而不是"画成黑色"：
//   黑色材质仍然会写深度，在 Bloom / 环境光 / 后处理下也可能透出一点灰；
//   而这里先 discard 掉所有像素，连深度都不写，所以绝对不会看到任何东西。
//
// 地板 / 天花板用它；墙体在正式玩法下也可以用它。
Shader "EchoPuzzle/Invisible"
{
    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "Queue"           = "Geometry"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Invisible"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite Off
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                discard;
                return half4(0, 0, 0, 0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
