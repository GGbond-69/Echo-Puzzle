// 玩家放置的标记：一个发光白点 + 一圈扩散的脉冲环。
// 世界空间广告牌，顶点色里带"放置时间"，所以落下去那一下的脉冲扩散完全在 GPU 上算。
Shader "EchoPuzzle/Marker"
{
    Properties
    {
        _Intensity ("Glow Intensity", Range(0, 8)) = 2.0
        _PulseRadius ("Pulse Max Radius", Range(0, 1)) = 1.0
        _CoreScale ("Core Radius Scale", Range(0, 1)) = 1.0
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
            Name "Marker"
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
                float _PulseRadius;
                float _CoreScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;   // xyz = 世界坐标
                float2 uv         : TEXCOORD0;  // 0..1 覆盖整个广告牌
                float4 color      : COLOR;      // rgb = 颜色, a = 基础不透明度
                float2 misc       : TEXCOORD1;  // x = 放置时间, y = 脉冲时长
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 color      : TEXCOORD1;
                float  alpha      : TEXCOORD2;
                float  pulseT     : TEXCOORD3;  // 0..1，脉冲进度；>=1 表示脉冲已结束
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                OUT.positionCS = TransformWorldToHClip(IN.positionOS.xyz);
                OUT.uv         = IN.uv;
                OUT.color      = IN.color.rgb;
                OUT.alpha      = IN.color.a;

                float duration = max(IN.misc.y, 0.0001);
                OUT.pulseT     = saturate((_Time.y - IN.misc.x) / duration);

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // uv 0..1 -> 以圆心为原点的半径
                float2 d  = IN.uv * 2.0 - 1.0;
                float  r  = length(d);

                // 1) 中心那个实心亮点：半径随 _CoreScale 缩放，物理大小只由 size 决定
                float core = 1.0 - smoothstep(0.10 * _CoreScale, 0.34 * _CoreScale, r);

                // 2) 刚放下去那一下向外扩散的脉冲环
                float pulse = 0.0;
                if (IN.pulseT < 1.0)
                {
                    float ringR = IN.pulseT * _PulseRadius;        // 环从圆心扩到"最大半径比例"处
                    float w     = 0.10;
                    float ring  = 1.0 - smoothstep(0.0, w, abs(r - ringR));
                    pulse = ring * (1.0 - IN.pulseT);              // 越扩越淡
                }

                float energy = (core + pulse * 0.9) * IN.alpha * _Intensity;
                if (energy <= 0.001) discard;

                return half4(IN.color * energy, energy);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
