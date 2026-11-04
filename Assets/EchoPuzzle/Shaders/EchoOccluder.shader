// 完全不透光的遮挡墙体材质。
//
// 和 EchoPuzzle/Invisible 的区别：
//   Invisible  —— discard 掉所有像素、不写深度 => 只是"看不见"，不挡任何东西
//   Occluder   —— 画成纯黑并**写深度**          => 挡住后面的一切
//
// 关键状态：
//   Cull Off   —— 两面都画。玩家可能站在墙体内部（圆形房间的环墙），
//                 只画正面的话从里面看就是透明的，挡不住东西。
//   ZWrite On  —— 写深度，这样后画的东西会被它挡住。
//   ZTest LEqual —— 正常深度测试。
//
// 能挡住什么（这些东西都是 ZTest LEqual，会读深度）：
//   玩家轮廓 EchoPuzzle/Outline、声波光带 EchoPuzzle/SonarTrail、
//   标记与脉冲 EchoPuzzle/Marker
// 挡不住什么：
//   HintZone 文字和所有 UI —— 它们是 IMGUI 在 OnGUI 里画的，完全不经过深度缓冲。
Shader "EchoPuzzle/Occluder"
{
    Properties
    {
        _Color ("Color", Color) = (0, 0, 0, 1)
    }

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
            Name "Occluder"
            Tags { "LightMode" = "UniversalForward" }

            Cull   Off
            ZWrite On
            ZTest  LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

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
                return _Color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
