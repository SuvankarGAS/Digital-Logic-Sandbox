// Procedural workspace background drawn on a single camera-aligned quad: a calm dot grid (a soft dot at every
// minor intersection, a slightly larger one every _MajorEvery cells) over a background that is lifted gently in
// the centre and falls off into a vignette at the edges. Dot sizes are in screen pixels, so they stay crisp and
// unobtrusive at every zoom level.
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
        _VignetteColor ("Vignette", Color) = (0.02, 0.025, 0.04, 0.85)
        _MinorDot ("Minor Dot Radius (px)", Float) = 1.0
        _MajorDot ("Major Dot Radius (px)", Float) = 1.5
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
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 world : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BackColor;
                half4 _MinorColor;
                half4 _MajorColor;
                float _Spacing;
                float _MajorEvery;
                float _MinorAlpha;
                half4 _VignetteColor;
                float _MinorDot;
                float _MajorDot;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.world = world.xy;
                output.uv = input.uv;
                return output;
            }

            // Coverage (0..1) of a dot of radiusPx screen pixels centred on every integer point of coord.
            float Dot(float2 coord, float radiusPx)
            {
                float2 nearest = coord - round(coord);
                float pixelsPerUnit = 1.0 / max(length(fwidth(coord)) * 0.7071, 1e-5);
                float distancePx = length(nearest) * pixelsPerUnit;
                return saturate(radiusPx + 0.5 - distancePx);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Background: lifted towards the centre, vignette towards the edges (uv is the quad, i.e. the view).
                float2 centred = input.uv - 0.5;
                float radial = saturate(length(centred * float2(1.25, 1.0)) * 1.55);
                half4 color = _BackColor * (1.0 + 0.22 * (1.0 - radial));
                color.rgb = lerp(color.rgb, _VignetteColor.rgb, _VignetteColor.a * radial * radial);

                float2 minorCoord = input.world / _Spacing;
                float minor = Dot(minorCoord, _MinorDot) * _MinorAlpha;
                float major = Dot(minorCoord / _MajorEvery, _MajorDot);
                // Dots fade with the vignette so the edges stay quiet.
                float fade = 1.0 - radial * 0.6;
                color.rgb = lerp(color.rgb, _MinorColor.rgb, minor * fade);
                color.rgb = lerp(color.rgb, _MajorColor.rgb, major * fade);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }
    }
}
