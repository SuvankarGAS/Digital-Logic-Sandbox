// Start-up splash ("power-on" intro), drawn as one full-screen UGUI quad.
//
// Everything is procedural and driven by two uniforms set from SplashScreen.cs:
//   _T     seconds since the splash started (the whole timeline below is a pure function of it)
//   _Exit  0 -> 1 during the closing radial dissolve
//
// Layers, back to front:
//   1. Workspace-coloured background, faint dot grid, vignette and film grain.
//   2. Procedural circuit-board traces (45-degree Truchet tiles plus straight runs, pads on dangling ends). A
//      power-on wave reveals the board from the centre, signal pulses stream inwards along the traces and, when the
//      logo gate fires, a shockwave rolls back out across the board.
//   3. The logo: an AND gate drawn as a signed-distance field. Its outline traces itself, the two inputs switch
//      on one after the other, the body energises and the output fires with a bloom flash.
//   4. Exit: a noisy radial dissolve with a glowing edge cuts the overlay away to reveal the editor underneath.
//
// Coordinates are in "cells": the screen is 18 cells tall, so traces keep their proportions at any resolution and
// SplashScreen.cs can place the title text relative to the logo using the same unit.
Shader "DigitalLogicSandbox/Splash"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        _T ("Time (s)", Float) = 0
        _Exit ("Exit (0-1)", Range(0, 1)) = 0
        _Seed ("Seed", Float) = 17
        _Background ("Background", Color) = (0.051, 0.059, 0.082, 1)
        _Grid ("Grid", Color) = (0.145, 0.165, 0.227, 1)
        _Low ("Signal Low", Color) = (0.196, 0.220, 0.286, 1)
        _High ("Signal High", Color) = (0.361, 0.910, 0.816, 1)
        _Accent ("Accent", Color) = (0.561, 0.639, 1, 1)
        _GateFill ("Gate Fill", Color) = (0.094, 0.106, 0.145, 1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha // premultiplied: lets the dissolve edge glow over the revealed editor
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            float _T;
            float _Exit;
            float _Seed;
            fixed4 _Background;
            fixed4 _Grid;
            fixed4 _Low;
            fixed4 _High;
            fixed4 _Accent;
            fixed4 _GateFill;

            static const float CellsTall = 18.0;
            static const float2 LogoCenter = float2(0.0, 2.3);

            // Logo timeline (seconds).
            static const float OutlineStart = 0.40;
            static const float OutlineEnd = 0.95;
            static const float InputAStart = 0.85;
            static const float InputBStart = 1.02;
            static const float InputDuration = 0.30;
            static const float BodyStart = 1.28;
            static const float BodyEnd = 1.55;
            static const float OutputStart = 1.48;
            static const float OutputDuration = 0.30;
            static const float FireTime = 1.74;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            // ------------------------------------------------------------------------------------------------
            // Helpers
            // ------------------------------------------------------------------------------------------------

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Segment(float2 p, float2 a, float2 b, out float along)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                along = h * length(ba);
                return length(pa - ba * h);
            }

            float Segment(float2 p, float2 a, float2 b)
            {
                float along;
                return Segment(p, a, b, along);
            }

            float Sq(float x)
            {
                return x * x; // pow(x, 2) is undefined for negative x
            }

            float EaseOut(float x)
            {
                x = saturate(x);
                return 1.0 - (1.0 - x) * (1.0 - x) * (1.0 - x);
            }

            // AND gate body: flat back at x = -w, semicircular front of radius h centred on the origin (needs w >= h).
            float AndGate(float2 q, float w, float h)
            {
                float2 boxCenter = float2(-w * 0.5, 0.0);
                float2 d = abs(q - boxCenter) - float2(w * 0.5, h);
                float box = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
                // The full disc lies inside the box for x < 0 (w >= h), so a plain union is exact and has no seam.
                return min(box, length(q) - h);
            }

            // ------------------------------------------------------------------------------------------------
            // Circuit-board tiles
            // ------------------------------------------------------------------------------------------------

            // Tile type of a cell: -1 empty, 0 '/' diagonals, 1 '\' diagonals, 2 horizontal run, 3 vertical run.
            int TileType(float2 cell)
            {
                float h = Hash21(cell + _Seed);
                if (h > 0.58)
                {
                    return -1;
                }

                float k = h / 0.58;
                return k < 0.3 ? 0 : (k < 0.6 ? 1 : (k < 0.8 ? 2 : 3));
            }

            // Edges a tile type connects (edge midpoints): 1 left, 2 right, 4 bottom, 8 top.
            int TileEdges(int type)
            {
                return type < 0 ? 0 : (type <= 1 ? 15 : (type == 2 ? 3 : 12));
            }

            float TileDistance(float2 q, int type)
            {
                if (type == 0)
                {
                    return min(Segment(q, float2(0.0, 0.5), float2(0.5, 1.0)), Segment(q, float2(0.5, 0.0), float2(1.0, 0.5)));
                }

                if (type == 1)
                {
                    return min(Segment(q, float2(0.0, 0.5), float2(0.5, 0.0)), Segment(q, float2(0.5, 1.0), float2(1.0, 0.5)));
                }

                if (type == 2)
                {
                    return abs(q.y - 0.5);
                }

                if (type == 3)
                {
                    return abs(q.x - 0.5);
                }

                return 1e5;
            }

            // Pad on an edge midpoint used by exactly one of the two cells sharing it (a dangling trace end).
            float PadDistance(float2 q, float2 cell, int edges, float2 offset, int bit, int oppositeBit, float2 midpoint)
            {
                int mine = (edges & bit) != 0 ? 1 : 0;
                int theirs = (TileEdges(TileType(cell + offset)) & oppositeBit) != 0 ? 1 : 0;
                return mine != theirs ? length(q - midpoint) : 1e5;
            }

            // Inward-streaming pulse: bright front with an exponential tail trailing outwards.
            float StreamPulse(float r, float t, float offset)
            {
                float d = frac((r + 5.5 * t) / 7.0 + offset);
                return exp(-d * 9.0) * smoothstep(1.0, 0.975, d);
            }

            // ------------------------------------------------------------------------------------------------
            // Logo wires
            // ------------------------------------------------------------------------------------------------

            // Draws a wire from a to b whose HIGH state travels from a with a bright head. progress 0..1 of the
            // travel time (the head runs a little past the end so it leaves cleanly).
            void Wire(float2 p, float2 a, float2 b, float progress, float halfWidth, float px,
                inout float3 color, inout float coverage, inout float3 glow)
            {
                float along;
                float d = Segment(p, a, b, along);
                float len = length(b - a);
                float front = progress * (len + 0.8);
                float lit = saturate((front - along) / px + 0.5) * step(0.0001, progress);
                float shape = d - lerp(halfWidth, halfWidth * 1.35, lit);
                float c = saturate(0.5 - shape / px);
                float behind = front - along;
                float head = behind >= 0.0 ? exp(-(behind * behind) / 0.12) : 0.0;
                head *= lit * (1.0 - smoothstep(len, len + 0.8, front));

                float3 wireColor = lerp(_Low.rgb * 1.25, _High.rgb, lit) + head * 0.85;
                color = lerp(color, wireColor, c);
                coverage = max(coverage, c);
                float outside = max(shape, 0.0);
                glow += _High.rgb * (lit * 0.35 + head * 1.4) * exp(-outside * 7.0);
            }

            void Pin(float2 p, float2 center, float on, float px, inout float3 color, inout float coverage, inout float3 glow)
            {
                float d = length(p - center) - 0.17;
                float c = saturate(0.5 - d / px);
                float ring = saturate(0.5 - (abs(d + 0.05) - 0.035) / px);
                float3 pinColor = lerp(lerp(_GateFill.rgb, _Low.rgb * 1.4, ring), _High.rgb, on);
                color = lerp(color, pinColor, c);
                coverage = max(coverage, c);
                glow += _High.rgb * on * 0.5 * exp(-max(d, 0.0) * 6.0);
            }

            // ------------------------------------------------------------------------------------------------
            // Fragment
            // ------------------------------------------------------------------------------------------------

            fixed4 Frag(Varyings input) : SV_Target
            {
                float t = _T;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 uvc = input.uv - 0.5;
                float2 p = uvc * float2(aspect, 1.0) * CellsTall;
                float r = length(p);
                float px = max(fwidth(p.y), 1e-4);

                float fire = smoothstep(FireTime - 0.02, FireTime + 0.05, t);
                float sinceFire = max(t - FireTime, 0.0);
                float flash = fire * exp(-sinceFire * 3.2);

                // --- 1. Background --------------------------------------------------------------------------
                float3 color = _Background.rgb;
                float revealR = max(t - 0.08, 0.0) * 19.0;
                float revealed = smoothstep(revealR, revealR - 2.5, r);
                float waveRing = exp(-Sq(r - revealR) * 0.9) * (1.0 - smoothstep(0.9, 1.6, t));

                // Soft central light behind the logo; swells when the gate fires.
                float2 toLogo = p - LogoCenter;
                float centerLight = exp(-dot(toLogo, toLogo) * 0.035);
                color += _Accent.rgb * centerLight * (0.035 * smoothstep(0.0, 1.0, t) + 0.10 * flash);
                color += _High.rgb * centerLight * 0.05 * fire;

                // Dot grid at cell corners.
                float2 gridQ = abs(frac(p + 0.5) - 0.5);
                float dotD = length(gridQ) - 0.035;
                float gridDot = saturate(0.5 - dotD / px) * revealed;
                color = lerp(color, _Grid.rgb * 1.35 + _Accent.rgb * waveRing * 0.6, gridDot * 0.75);

                // --- 2. Circuit-board traces ----------------------------------------------------------------
                float2 cell = floor(p);
                float2 q = p - cell;
                int type = TileType(cell);
                int edges = TileEdges(type);

                float traceD = TileDistance(q, type) - 0.045;
                float padD = 1e5;
                padD = min(padD, PadDistance(q, cell, edges, float2(-1.0, 0.0), 1, 2, float2(0.0, 0.5)));
                padD = min(padD, PadDistance(q, cell, edges, float2(1.0, 0.0), 2, 1, float2(1.0, 0.5)));
                padD = min(padD, PadDistance(q, cell, edges, float2(0.0, -1.0), 4, 8, float2(0.5, 0.0)));
                padD = min(padD, PadDistance(q, cell, edges, float2(0.0, 1.0), 8, 4, float2(0.5, 1.0)));
                float padRing = abs(padD - 0.11) - 0.035;
                float shape = min(traceD, padRing);
                float traceCoverage = saturate(0.5 - shape / px);
                float padHole = saturate(0.5 - (padD - 0.075) / px);

                // Keep the board calm behind the logo and title, and let it fall off towards the edges.
                float2 textSpace = (p - float2(0.0, 0.9)) / float2(10.0, 4.4);
                float calm = lerp(0.12, 1.0, smoothstep(0.85, 1.35, length(textSpace)));
                float edgeFade = 1.0 - smoothstep(0.55, 1.05, length(uvc * float2(1.0, 1.25)) * 1.6);
                float boardMask = revealed * calm * lerp(0.35, 1.0, edgeFade);

                // Signal pulses: inward streams on some traces, then the outward shockwave when the gate fires.
                float angle = atan2(p.y, p.x) / 6.2831853 + 0.5;
                float sector = floor(angle * 30.0);
                float carries = step(0.42, Hash21(floor(cell / 2.0) + 11.0 + _Seed));
                float streams = StreamPulse(r, t, Hash21(float2(sector, 7.1))) * carries;
                streams *= smoothstep(0.2, 0.7, t) * (1.0 - 0.6 * smoothstep(FireTime, FireTime + 0.4, t));
                float shock = fire * exp(-Sq(r - sinceFire * 17.0) * 0.55) * exp(-sinceFire * 0.9);
                float energy = saturate(streams + shock * 1.4 + waveRing * 0.45);

                float hue = Hash21(cell * 0.37 + 3.0);
                float3 pulseColor = lerp(_High.rgb, _Accent.rgb, step(0.78, hue));
                float3 traceColor = lerp(_Low.rgb, pulseColor * 1.15, energy);
                color = lerp(color, traceColor, traceCoverage * boardMask);
                color = lerp(color, _Background.rgb * 0.7, padHole * boardMask * 0.8);
                float traceGlow = exp(-max(shape, 0.0) * 9.0) * energy * boardMask;
                color += pulseColor * traceGlow * 0.32;

                // --- 3. Logo gate ---------------------------------------------------------------------------
                float2 g = p - LogoCenter - float2(0.05, 0.0);
                float3 logoColor = color;
                float logoCoverage = 0.0;
                float3 glow = 0.0;

                float wiresIn = smoothstep(0.45, 0.8, t);
                float progressA = saturate((t - InputAStart) / InputDuration);
                float progressB = saturate((t - InputBStart) / InputDuration);
                float progressOut = saturate((t - OutputStart) / OutputDuration);
                float3 wireColor = logoColor;
                float wireCoverage = 0.0;
                Wire(g, float2(-3.2, 0.65), float2(-1.4, 0.65), progressA, 0.06, px, wireColor, wireCoverage, glow);
                Wire(g, float2(-3.2, -0.65), float2(-1.4, -0.65), progressB, 0.06, px, wireColor, wireCoverage, glow);
                Wire(g, float2(1.3, 0.0), float2(3.2, 0.0), progressOut, 0.06, px, wireColor, wireCoverage, glow);
                Pin(g, float2(-3.2, 0.65), step(InputAStart, t), px, wireColor, wireCoverage, glow);
                Pin(g, float2(-3.2, -0.65), step(InputBStart, t), px, wireColor, wireCoverage, glow);
                Pin(g, float2(3.2, 0.0), step(OutputStart + OutputDuration * 0.75, t), px, wireColor, wireCoverage, glow);
                logoColor = lerp(logoColor, wireColor, wireCoverage * wiresIn);
                logoCoverage = max(logoCoverage, wireCoverage * wiresIn);
                glow *= wiresIn;

                float body = AndGate(g, 1.4, 1.3);
                float energised = EaseOut((t - BodyStart) / (BodyEnd - BodyStart));
                float bodyIn = smoothstep(0.3, 0.75, t);

                // Fill: dark gate face with an inner rim light once energised.
                float inside = saturate(0.5 - body / px) * bodyIn;
                float innerRim = exp(body * 4.0) * energised;
                float scan = 0.5 + 0.5 * sin((g.y * 9.0 - t * 6.0));
                float3 fillColor = _GateFill.rgb + _High.rgb * (innerRim * 0.22 + energised * 0.05 * scan) + _High.rgb * flash * 0.35;
                logoColor = lerp(logoColor, fillColor, inside);
                logoCoverage = max(logoCoverage, inside);

                // Outline that draws itself around the gate, then turns HIGH.
                float sweep = EaseOut((t - OutlineStart) / (OutlineEnd - OutlineStart));
                float outlineAngle = frac(atan2(g.y, -g.x) / 6.2831853 + 0.5 + 0.25);
                float drawn = saturate((sweep * 1.04 - outlineAngle) / 0.03);
                float outline = abs(body) - lerp(0.05, 0.075, energised);
                float outlineCoverage = saturate(0.5 - outline / px) * drawn;
                float tip = exp(-Sq((outlineAngle - sweep) * 26.0)) * (1.0 - step(1.0, sweep)) * drawn;
                float3 outlineColor = lerp(lerp(_Low.rgb * 1.6, _Accent.rgb * 0.8, 0.35), _High.rgb, energised) + tip * 0.9;
                logoColor = lerp(logoColor, outlineColor, outlineCoverage);
                logoCoverage = max(logoCoverage, outlineCoverage);
                glow += (_High.rgb * energised * 0.55 + _Accent.rgb * tip * 0.8) * exp(-max(outline, 0.0) * 5.0) * drawn;
                glow += _High.rgb * flash * 0.9 * exp(-max(body, 0.0) * 1.6);

                // Expanding ring when the output fires.
                float ringR = sinceFire * 9.0 + 1.3;
                float burst = exp(-Sq(length(g) - ringR) * 6.0) * fire * exp(-sinceFire * 2.8);
                glow += _High.rgb * burst * 0.8;

                color = logoColor + glow;

                // --- Finish: vignette and grain -------------------------------------------------------------
                float vignette = 1.0 - 0.55 * dot(uvc * float2(1.1, 1.3), uvc * float2(1.1, 1.3)) * 1.6;
                color *= vignette;
                float grain = Hash21(input.uv * _ScreenParams.xy + frac(t * 7.13) * 91.7) - 0.5;
                color += grain * 0.011;

                // --- 4. Exit dissolve -----------------------------------------------------------------------
                float alpha = 1.0;
                float3 edgeGlow = 0.0;
                if (_Exit > 0.0)
                {
                    float maxR = length(float2(aspect, 1.0)) * CellsTall * 0.5;
                    float exitEase = _Exit * _Exit * (3.0 - 2.0 * _Exit);
                    float radius = exitEase * (maxR + 4.0) - 1.5;
                    float n = (ValueNoise(p * 0.9 + 3.1) - 0.5) * 2.2 + (ValueNoise(p * 3.1) - 0.5) * 0.6;
                    float dd = r + n - radius;
                    alpha = smoothstep(-0.02, 0.25, dd);
                    float edge = exp(-dd * dd * 5.0) * (1.0 - smoothstep(0.85, 1.0, _Exit));
                    edgeGlow = lerp(_High.rgb, _Accent.rgb, saturate(n * 0.5 + 0.5)) * edge * 0.9;
                }

                float a = alpha * input.color.a;
                float3 premultiplied = saturate(color) * a + edgeGlow * input.color.a;
                return fixed4(premultiplied, saturate(a));
            }
            ENDCG
        }
    }
}
