// Procedural, anti-aliased workspace grid drawn on a single camera-aligned quad.
// Minor lines every _Spacing world units, major lines every _MajorEvery minor cells.
Shader "DigitalLogicSandbox/Grid"
{
    Properties
    {
        _BackColor ("Background", Color) = (0.10, 0.11, 0.13, 1)
        _MinorColor ("Minor Lines", Color) = (0.15, 0.16, 0.19, 1)
        _MajorColor ("Major Lines", Color) = (0.19, 0.21, 0.25, 1)
        _Spacing ("Spacing", Float) = 1
        _MajorEvery ("Major Every", Float) = 5
        _MinorAlpha ("Minor Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Opaque"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 world : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BackColor;
                half4 _MinorColor;
                half4 _MajorColor;
                float _Spacing;
                float _MajorEvery;
                float _MinorAlpha;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.world = world.xy;
                return output;
            }

            // 1 on a grid line (~1px wide, anti-aliased), 0 elsewhere.
            float GridLine(float2 coord)
            {
                float2 width = fwidth(coord);
                float2 distance = abs(frac(coord - 0.5) - 0.5) / max(width, 1e-5);
                return 1.0 - saturate(min(distance.x, distance.y));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 minorCoord = input.world / _Spacing;
                float minor = GridLine(minorCoord) * _MinorAlpha;
                float major = GridLine(minorCoord / _MajorEvery);
                half4 color = lerp(_BackColor, _MinorColor, minor);
                color = lerp(color, _MajorColor, major);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }
    }
}
