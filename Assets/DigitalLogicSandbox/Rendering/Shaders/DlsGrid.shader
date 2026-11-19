// Procedural workspace background drawn on a single camera-aligned quad: a calm dot grid (a soft dot at every
// minor intersection, a larger one every _MajorEvery cells) over a background that is lifted gently in the centre
// and falls off into a vignette at the edges.
//
// Everything adapts to the on-screen cell size (pixels per cell), so the look is independent of window resolution:
//  - dot radius grows with the cell size between a minimum and a maximum radius in screen pixels, so dots stay
//    crisp when zoomed out and remain clearly visible when zoomed far in;
//  - sparse dots (large cells) are drawn brighter, while dense dots stay quiet;
//  - minor dots fade out once they get too dense to read (a level-of-detail fade, no moire).
Shader "DigitalLogicSandbox/Grid"
{
    Properties
    {
        _BackColor ("Background", Color) = (0.10, 0.11, 0.13, 1)
        _MinorColor ("Minor Dots", Color) = (0.15, 0.16, 0.19, 1)
        _MajorColor ("Major Dots", Color) = (0.19, 0.21, 0.25, 1)
        _Spacing ("Spacing", Float) = 1
        _MajorEvery ("Major Every", Float) = 5
        _MinorAlpha ("Minor Alpha", Range(0, 1)) = 1
        _VignetteColor ("Vignette", Color) = (0.02, 0.025, 0.04, 0.85)
        _MinorDot ("Minor Dot Min Radius (px)", Float) = 1.0
        _MinorDotMax ("Minor Dot Max Radius (px)", Float) = 2.6
        _MajorDot ("Major Dot Min Radius (px)", Float) = 1.5
        _MajorDotMax ("Major Dot Max Radius (px)", Float) = 3.6
        _DotScale ("Dot Radius per Cell Pixel", Float) = 0.012
        _ZoomBoost ("Sparse Dot Brightness Boost", Float) = 1.8
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
                float _MinorDotMax;
                float _MajorDot;
                float _MajorDotMax;
                float _DotScale;
                float _ZoomBoost;
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

            // Coverage (0..1) of an anti-aliased dot of radiusPx screen pixels centred on every integer point of coord,
            // where cellPx is the on-screen size of one unit of coord.
            float Dot(float2 coord, float cellPx, float radiusPx)
            {
                float distancePx = length(coord - round(coord)) * cellPx;
                return 1.0 - smoothstep(radiusPx - 0.6, radiusPx + 0.6, distancePx);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Background: lifted towards the centre, vignette towards the edges (uv is the quad, i.e. the view).
                float2 centred = input.uv - 0.5;
                float radial = saturate(length(centred * float2(1.25, 1.0)) * 1.55);
                half4 color = _BackColor * (1.0 + 0.22 * (1.0 - radial));
                color.rgb = lerp(color.rgb, _VignetteColor.rgb, _VignetteColor.a * radial * radial);

                float2 minorCoord = input.world / _Spacing;
                // Screen pixels per minor cell (the view is orthographic and axis-aligned, so one axis is enough).
                float cellPx = 1.0 / max(fwidth(minorCoord.x), 1e-6);
                float majorCellPx = cellPx * _MajorEvery;

                float minorRadius = clamp(cellPx * _DotScale, _MinorDot, _MinorDotMax);
                float majorRadius = clamp(majorCellPx * _DotScale, _MajorDot, _MajorDotMax);

                // Level of detail: minor dots vanish below ~5 px spacing and are fully shown from ~11 px.
                float minorFade = saturate((cellPx - 5.0) / 6.0) * _MinorAlpha;
                // Major dots stay a step larger than visible minor dots, and soften when they become dense themselves.
                majorRadius = max(majorRadius, minorRadius + 0.75 * minorFade);
                float majorFade = lerp(0.55, 1.0, saturate((majorCellPx - 10.0) / 30.0));
                float minor = Dot(minorCoord, cellPx, minorRadius) * minorFade;
                float major = Dot(minorCoord / _MajorEvery, majorCellPx, majorRadius) * majorFade;

                // Sparse dots can afford more contrast without making the workspace noisy.
                float boost = lerp(1.0, _ZoomBoost, saturate((cellPx - 24.0) / 120.0));
                half3 minorColor = saturate(_MinorColor.rgb * boost);
                half3 majorColor = saturate(_MajorColor.rgb * boost);

                // Dots fade slightly with the vignette so the edges stay quiet.
                float fade = 1.0 - radial * 0.45;
                color.rgb = lerp(color.rgb, minorColor, minor * fade);
                color.rgb = lerp(color.rgb, majorColor, major * fade);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }
    }
}
