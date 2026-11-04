// 瞄准辅助层：瞄准圈 / 落点圈 / 瞄准指示线专用。
//
// ★ 核心用途：**永远画在最上层，不被任何实体遮挡**。
//
//   为什么需要专门的 shader：
//     · 本项目是 URP。内置管线的 "Sprites/Default" 在 URP 下不保证遵守
//       _ZTest / _ZWrite 这类逐材质的 RenderState 覆盖，实测会被
//       WallOccluder、怪物实体挡住。
//     · URP 的 "Universal Render Pipeline/Unlit" 虽然有这些属性，
//       但它们由 shader 里的 [Enum] 属性驱动，运行时 SetInt 不一定生效。
//     · 直接把状态**硬编码在 Pass 里**最可靠 —— 不依赖任何材质属性。
//
//   这个 shader 做的事：
//     · ZTest Always     不做深度比较 -> 就算前面有墙也照画
//     · ZWrite Off       自己不写深度 -> 不影响场景其它物体
//     · Queue = Overlay  排在所有 Transparent 之后 -> 没有后画的半透明
//                        物体会盖在它上面
//     · 顶点色 + _Color  两种着色来源，材质颜色和顶点色相乘
Shader "EchoPuzzle/AimOverlay"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Overlay"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Pass
        {
            Name "AimOverlay"
            Tags { "LightMode" = "UniversalForward" }

            // 普通 alpha 混合
            Blend SrcAlpha OneMinusSrcAlpha

            // ★ 关键三项
            ZWrite Off
            ZTest  Always

            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color      = IN.color * _Color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return IN.color;
            }
            ENDHLSL
        }
    }

    // 兜底：万一在不支持 URP 的环境里，至少别变成粉红色
    FallBack "Unlit/Transparent"
}
