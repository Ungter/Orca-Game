// Thin marks laid on the island for the lighthouse's final game: the dark seams the island splits
// along (blended), or added light. Unlit, double-sided, drawn just above the ground (depth offset) and
// thinned by the scene's fog like everything else at that distance. Vertex alpha shapes each
// ribbon across its width. _SrcBlend/_DstBlend pick added light (One One) or blending
// (SrcAlpha OneMinusSrcAlpha).
Shader "Orca/Mouse Trap Glow"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 0.8, 0.45, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            Offset -1, -4

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _SrcBlend, _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float fogFactor : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.color = input.color * _Color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Fog thins the mark the way it hides the ground under it.
                half clear = 1.0h;
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                    clear = saturate(ComputeFogIntensity(i.fogFactor));
                #endif
                half alpha = saturate(i.color.a) * clear;
                // Added light carries its strength in the colour; blending in the alpha.
                half3 colour = _DstBlend > 0.5 && _SrcBlend > 0.5 && _SrcBlend < 1.5 ? i.color.rgb * alpha : i.color.rgb;
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
