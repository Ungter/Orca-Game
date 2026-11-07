// Additive glow of a lit window's light spilling onto the wall round it, for WindowLamp. The quad
// covers the window plus a margin; UV0 runs 0-1 across it and UV1.xy is the window's share of the
// quad on each axis. Nothing is added over the window itself; from its edge out the glow fades to
// nothing at the quad's border. It adds light rather than shading the wall, so it shows on dark
// walls too, and it doesn't throw specular highlights off the glass the way a real light did.
Shader "Orca/Window Spill"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 0.68, 0.3, 1)
        _Intensity ("Intensity", Range(0, 2)) = 0.35
        _FadePower ("Fade Outwards", Range(0.5, 6)) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Spill"
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
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 window : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 window : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.uv = input.uv;
                output.window = input.window;
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 0 at the window's edge, 1 at the quad's border, along each axis.
                float2 p = abs(input.uv * 2.0 - 1.0);
                float2 inner = saturate(input.window);
                float2 outside = max(p - inner, 0.0) / max(1.0 - inner, 1e-3);
                half distance = saturate(length(outside));
                half inWindow = (p.x < inner.x && p.y < inner.y) ? 1.0h : 0.0h;
                half glow = pow(1.0h - distance, _FadePower) * (1.0h - inWindow);
                half3 rgb = _Color.rgb * glow * _Intensity;
                // Additive: fog fades the glow to nothing rather than to the fog colour.
                rgb = MixFogColor(rgb, half3(0.0h, 0.0h, 0.0h), input.fog);
                return half4(rgb, 0.0h);
            }
            ENDHLSL
        }
    }
}
