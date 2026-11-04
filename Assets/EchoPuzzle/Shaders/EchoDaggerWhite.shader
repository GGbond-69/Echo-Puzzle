// 匕首专用：纯白轮廓材质。
//
// ★ 为什么不用 EchoPuzzle/Outline：
//
//   Outline 靠"沿法线膨胀外壳 + Cull Front"来画描边。这对玩家那种
//   圆润的胶囊体很好用，但对 lightsaber.fbx 这种**几十个小零件紧密
//   拼在一起**的细长模型是灾难：
//     · 每个零件各自沿法线膨胀
//     · 膨胀量超过零件间距时，外壳互相穿插、互相遮挡
//     · 表现就是"从模型上不规则地切掉一块"
//   而且 _OutlineWidth 作用在模型本地空间，模型缩得越小问题越明显。
//
//   所以匕首改用**单 Pass 纯色**方案：
//     · 不膨胀、不画外壳   -> 不会有自穿插、不会被切块
//     · 直接输出白色 + 菲涅尔边缘增强
//     · 与玩家在"透明墙 + 关灯"场景下的观感一致（白色轮廓）
//
//   这样模型长什么样就画成什么样，1:1 忠实呈现。
Shader "EchoPuzzle/DaggerWhite"
{
    Properties
    {
        _Color       ("Base Color",  Color)          = (1, 1, 1, 1)
        _RimStrength ("Rim Strength", Range(0, 4))   = 0.35
        _RimPower    ("Rim Power",    Range(0.5, 8)) = 3.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Overlay"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "DaggerWhite"
            Tags { "LightMode" = "UniversalForward" }

            // ★ 双面绘制：ZTest Always 不做深度比较，
            //   只画单面会让模型内部的背面露出来，看着像"缺了一块"。
            Cull Off

            // ★ 永远画在最上层：不被怪物实体 / WallOccluder / 任何墙体遮挡
            //   ZTest Always —— 不做深度比较
            //   ZWrite Off   —— 自己不写深度，不影响场景
            //   Queue=Overlay(4000) —— 排在所有 Transparent 之后
            ZWrite Off
            ZTest  Always

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _RimStrength;
                float _RimPower;
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
                float3 viewDirWS  : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;

                OUT.normalWS  = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(pos.positionWS);

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewDirWS);

                // 菲涅尔：正对视线的地方稍暗，边缘稍亮，让形体能看出立体感
                float fres = pow(saturate(1.0 - saturate(dot(n, v))), _RimPower);

                half3 col = _Color.rgb * (1.0 + fres * _RimStrength);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack "Unlit/Color"
}
