// Signal wires. Every straight segment of a wire is one quad; the fragment shader measures the distance to the
// segment (a capsule signed-distance field) and draws an anti-aliased core of the current half width plus a soft
// glow around it. Segments meet in perfectly rounded joints.
//
// Blending uses BlendOp Max on premultiplied colour, so overlapping shapes form a clean union instead of stacking:
// the two capsules at a bend, and fan-out wires sharing a trunk, never get brighter where they overlap. Wires are
// drawn on the dark workspace below the gates, where Max is indistinguishable from alpha blending.
//
// A signal transition is drawn along the path: path distance <= _Path.x shows the new state (A) with a bright
// front, the rest still shows the old state (B). An optional junction dot (_Junction) marks where a fan-out branch
// leaves the shared trunk.
Shader "DigitalLogicSandbox/Wire"
{
    Properties
    {
        _ColorA ("New / Settled Colour", Color) = (1, 1, 1, 1)
        _ColorB ("Old Colour", Color) = (1, 1, 1, 1)
        _FrontColor ("Front Colour", Color) = (1, 1, 1, 1)
        _Width ("Half Width A, B, Glow A, B", Vector) = (0.03, 0.03, 0, 0)
        _Path ("Front Distance, Front Length, Glow Radius", Vector) = (100000, 0.6, 0.24, 0)
        _Junction ("Junction XY, Radius", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        BlendOp Max
        Blend One One

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 local : TEXCOORD0;   // x: along the segment from its start, y: across (world units)
                float2 segment : TEXCOORD1; // x: path distance at the segment start, y: segment length
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 local : TEXCOORD0;
                float2 segment : TEXCOORD1;
                float2 world : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA;
                half4 _ColorB;
                half4 _FrontColor;
                float4 _Width;
                float4 _Path;
                float4 _Junction;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.local = input.local;
                output.segment = input.segment;
                output.world = world.xy;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float s = input.local.x;
                float segmentLength = input.segment.y;
                float along = s < 0.0 ? -s : max(s - segmentLength, 0.0);
                float distance = sqrt(along * along + input.local.y * input.local.y);
                float pathDistance = input.segment.x + clamp(s, 0.0, segmentLength);
                float pixel = max(fwidth(distance), 1e-5);

                // Which state this point shows: 1 = new state (behind the travelling front), 0 = old state.
                float front = _Path.x;
                float k = saturate((front - pathDistance) / (pixel * 1.5) + 0.5);
                half3 color = lerp(_ColorB.rgb, _ColorA.rgb, k);
                float halfWidth = lerp(_Width.y, _Width.x, k);
                float glow = lerp(_Width.w, _Width.z, k);

                // Bright head of a travelling signal, fading back towards the source.
                float behind = front - pathDistance;
                float head = behind >= 0.0 ? exp(-(behind * behind) / max(_Path.y * _Path.y, 1e-6)) : 0.0;
                color = lerp(color, _FrontColor.rgb, head * k);

                // Junction dot: union of the wire with a disc.
                float shape = distance - halfWidth;
                if (_Junction.z > 0.0)
                {
                    float2 toJunction = input.world - _Junction.xy;
                    shape = min(shape, sqrt(dot(toJunction, toJunction)) - _Junction.z);
                }

                float core = saturate(0.5 - shape / pixel);
                float outside = max(shape, 0.0);
                float falloff = saturate(1.0 - outside / max(_Path.z, 1e-5));
                float halo = glow * falloff * falloff;
                float coverage = max(core, halo);
                return half4(color * coverage, coverage);
            }
            ENDHLSL
        }
    }
}
