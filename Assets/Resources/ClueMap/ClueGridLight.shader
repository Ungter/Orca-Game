// Additive light for the clue hunt: the faint walls standing on the cell borders and the beams
// over the potions. Brightest at the foot (UV y = 0), dispersing upwards (y = 1); UV x across a
// card softens its sides (walls keep x = 0.5, so they run on unbroken). Vertex colour: rgb =
// colour, alpha = brightness. Loaded from Resources by ClueMinigame.
// With _UseCells on (the walls), only the borders round the player light up: full strength on
// their own cell's four sides, _NearGlow on the rest of the 3 x 3 cells round it, nothing beyond.
// A wall knows which pair of cells it parts from its world position and UV1 x (0: a border of
// constant x, 1: of constant z); moving to a new cell cross-fades from the last one.
Shader "Orca/Clue Grid Light"
{
    Properties
    {
        [HDR] _Color ("Tint", Color) = (1, 0.9, 0.7, 1)
        _Intensity ("Intensity", Range(0, 4)) = 0.23
        _FadePower ("Fade Upwards", Range(0.5, 6)) = 2.2
        _EdgeSoftness ("Edge Softness", Range(0.01, 1)) = 0.6
        _Fade ("Fade", Range(0, 1)) = 1
        [ToggleUI] _UseCells ("Light Only The Cells Round The Player", Float) = 0
        _Grid ("Grid Origin (xy) And Cell Size (z)", Vector) = (0, 0, 1, 0)
        _Cells ("Player Cell (xy) And Previous Cell (zw)", Vector) = (0, 0, 0, 0)
        _CellBlend ("Previous To Player Cell", Range(0, 1)) = 1
        _NearGlow ("Neighbouring Cells", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "ClueGridLight"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _FadePower;
                half _EdgeSoftness;
                half _Fade;
                half _UseCells;
                float4 _Grid;
                float4 _Cells;
                half _CellBlend;
                half _NearGlow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 side : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                float side : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.uv = input.uv;
                output.side = input.side.x;
                output.color = input.color;
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            // 1 for a border of the player's cell, _NearGlow for the rest of the 3 x 3, else 0.
            half CellGlow(float2 a, float2 b, float2 player)
            {
                float2 toA = abs(a - player);
                float2 toB = abs(b - player);
                float rings = min(max(toA.x, toA.y), max(toB.x, toB.y));
                return rings < 0.5 ? 1.0h : (rings < 1.5 ? _NearGlow : 0.0h);
            }

            half4 frag(Varyings input) : SV_Target
            {
                half up = saturate(input.uv.y);
                // A quick rise hides where the foot meets the ground, then a long fade upwards.
                half rise = smoothstep(0.0h, 0.05h, up);
                half fade = rise * pow(1.0h - up, _FadePower);
                half across = 1.0h - abs(input.uv.x * 2.0h - 1.0h);
                half edge = smoothstep(0.0h, _EdgeSoftness, across);

                half cells = 1.0h;
                if (_UseCells > 0.5h)
                {
                    // The two cells this bit of wall parts, in grid coordinates.
                    float2 grid = (input.positionWS.xz - _Grid.xy) / _Grid.z;
                    float2 a, b;
                    if (input.side < 0.5)
                    {
                        float border = round(grid.x);
                        a = float2(border - 1.0, floor(grid.y));
                        b = float2(border, floor(grid.y));
                    }
                    else
                    {
                        float border = round(grid.y);
                        a = float2(floor(grid.x), border - 1.0);
                        b = float2(floor(grid.x), border);
                    }
                    cells = lerp(CellGlow(a, b, _Cells.zw), CellGlow(a, b, _Cells.xy), _CellBlend);
                }

                half amount = fade * edge * cells * input.color.a * _Intensity * _Fade;
                half3 rgb = input.color.rgb * _Color.rgb * amount;
                // Additive: fog fades the light to nothing rather than to the fog colour.
                rgb = MixFogColor(rgb, half3(0.0h, 0.0h, 0.0h), input.fog);
                return half4(rgb, 0.0h);
            }
            ENDHLSL
        }
    }
}
