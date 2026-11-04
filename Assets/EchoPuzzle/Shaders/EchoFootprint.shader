// 脚印材质。
//
// 每个脚印是一个朝向相机的四边形，形状在片元里用解析方法画出来：
//   前掌 = 椭圆，后跟 = 较小的椭圆，两者用一段细窄的连接过渡。
// 不透明度来自顶点色 —— 由 CPU 每帧按"存在时长"烘焙进 alpha，
// 所以 fadeSpeed 不需要传进 shader。
//
// 深度状态和光带 / 标记一致（ZTest LEqual、ZWrite Off），
// 所以会被不透明的遮挡墙体挡住。
Shader "EchoPuzzle/Footprint"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 1.6
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
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
            Name "Footprint"
            Tags { "LightMode" = "UniversalForward" }

            Cull   [_Cull]
            ZWrite Off
            ZTest  LEqual
            Blend  One One        // 加法混合，和光带 / 标记一致

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = IN.uv;
                OUT.color      = IN.color;
                return OUT;
            }

            // 椭圆场：在 (p - c) / r 的单位圆里，返回 1 - 距离（> 0 表示在内部）
            float EllipseField(float2 p, float2 c, float2 r)
            {
                float2 d = (p - c) / max(r, 1e-5);
                return 1.0 - length(d);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // UV 空间：x 是脚掌的长轴（0 脚跟 -> 1 脚尖），y 是左右
                float2 p = IN.uv;

                // 前掌（靠脚尖那一段，稍宽）
                float toe  = EllipseField(p, float2(0.66, 0.5), float2(0.30, 0.30));

                // 后跟（靠脚跟，更小更圆）
                float heel = EllipseField(p, float2(0.20, 0.5), float2(0.20, 0.22));

                // 中段连接：一条细窄的带，把前后掌连起来
                float midY  = abs(p.y - 0.5);
                float midX  = step(0.16, p.x) * step(p.x, 0.72);
                float mid   = midX * saturate(1.0 - midY / 0.17);

                float m = max(max(toe, heel), mid);
                if (m <= 0.0) discard;

                // 边缘柔化，避免硬锯齿
                float aa    = fwidth(m) * 1.5 + 1e-4;
                float shape = saturate(m / aa);

                float a = IN.color.a * shape * _Intensity;

                return half4(IN.color.rgb * a, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
