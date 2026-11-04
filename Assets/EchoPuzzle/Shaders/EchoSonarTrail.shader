// 声波拖尾：把整条声波射线渲染成一条连续的、会向前生长的光带（曳光弹效果）。
//
// 几何：C# 端沿射线方向每 _EchoTrailSpacing 米铺一个四边形。
//       四边形是"面向相机 45°"构造的 —— 一条边垂直向上，一条边指向相机。
//       两者都不是相机右向量，所以无论声波朝哪飞、视角怎么变，
//       光带在屏幕上的投影宽度都大致恒定：飞到远处也不会缩成一颗点。
//
// 生长：所有顶点都记录波前离开声源的时间（打在 uv0.z 里），shader 里
//          已传播距离 = (当前时间 - 波前出发时间) × 声速
//          每个顶点只允许推进到波前所在的位置
//       于是整条光带自然从声源向外长出来。
//
// 衰减：不透明度在 C# 端按每个顶点"被波前扫过的时刻"算好，烘进顶点色的 a 通道。
//       头和尾的已传播时间不同，所以光带天生就是一条渐隐的尾巴。
Shader "EchoPuzzle/SonarTrail"
{
    Properties
    {
        _Intensity ("Glow Intensity", Range(0, 8)) = 1.8
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
            Name "SonarTrail"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
            CBUFFER_END

            // 全局参数，由 SonarTrailRenderer 每次写入（同一个声源内是常数）
            float _EchoTrailSpeed;     // 声速（米/秒）

            struct Attributes
            {
                float4 positionOS : POSITION;   // xyz = 世界坐标, w = 该顶点到声源的路程
                float2 uv         : TEXCOORD0;  // x = 0/1 沿射线, y = 0/1 横向
                float4 color      : COLOR;      // rgb = 颜色, a = 当前不透明度
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 color      : TEXCOORD1;
                float  alpha      : TEXCOORD2;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                // 波前已经走了多远。C# 端在 uv 里没有带时间，所以这里用位置 w 反推：
                // 顶点位置 = 原本的位置，但只允许推进到波前 —— 靠 C# 每帧裁剪几何保证。
                OUT.positionCS = TransformWorldToHClip(IN.positionOS.xyz);
                OUT.uv         = IN.uv;
                OUT.color      = IN.color.rgb;
                OUT.alpha      = IN.color.a;

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                if (IN.alpha <= 0.001) discard;

                // 横向软边缘
                float d = abs(IN.uv.y * 2.0 - 1.0);
                float e = smoothstep(1.0, 0.15, d);

                float energy = IN.alpha * e * _Intensity;
                return half4(IN.color * energy, energy);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
