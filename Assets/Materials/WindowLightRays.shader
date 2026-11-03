// Additive light shafts for WindowGlow. Each card is brightest at the glass (UV y = 0) and
// fades out towards its tip (y = 1), with soft sides across UV x. Cards seen edge-on fade
// away so no shaft ever shows as a hard line, and every shaft slowly brightens and dims on
// its own phase (UV z). Vertex colour: rgb = light colour, alpha = the shaft's brightness.
Shader "Orca/Window Light Rays"
{
    Properties
    {
        [HDR] _Color ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 4)) = 0.45
        _EdgeSoftness ("Edge Softness", Range(0.01, 1)) = 0.7
        _FadePower ("Fade Along Length", Range(0.5, 6)) = 1.8
        _Shimmer ("Shimmer Amount", Range(0, 1)) = 0.5
        _ShimmerSpeed ("Shimmer Speed", Range(0, 3)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "LightRays"
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
                half _EdgeSoftness;
                half _FadePower;
                half _Shimmer;
                half _ShimmerSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;
                half4 color : COLOR;
                half facing : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 viewWS = normalize(_WorldSpaceCameraPos - position.positionWS);
                output.facing = abs(dot(normalWS, viewWS));
                output.uv = input.uv;
                output.color = input.color;
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half across = 1.0h - abs(input.uv.x * 2.0h - 1.0h);
                half edge = smoothstep(0.0h, _EdgeSoftness, across);
                half along = saturate(input.uv.y);
                // A quick rise off the glass hides where the card starts; then a long fade.
                half fade = smoothstep(0.0h, 0.06h, along) * pow(1.0h - along, _FadePower);
                half wave = 0.5h + 0.5h * sin(_Time.y * _ShimmerSpeed * 6.2831853h + input.uv.z);
                half shimmer = 1.0h - _Shimmer * wave * wave;
                half facing = smoothstep(0.05h, 0.55h, input.facing);
                half amount = edge * fade * shimmer * facing * input.color.a * _Intensity;
                half3 rgb = input.color.rgb * _Color.rgb * amount;
                // Additive: fog fades the shafts to nothing rather than to the fog colour.
                rgb = MixFogColor(rgb, half3(0.0h, 0.0h, 0.0h), input.fog);
                return half4(rgb, 0.0h);
            }
            ENDHLSL
        }
    }
}
