// The ocean: the supplied pixel-art water, unlit, tiled in world metres, with scene fog. Far
// out it melts into the sky so the sea never visibly ends: OceanHorizon lays a much bigger
// copy of the ocean under it, and from _HorizonFadeStart to _HorizonFadeEnd of the camera's far
// plane (never nearer than _HorizonFadeMinDistance) the water turns into the night sky's own
// colour for that direction (the skybox's gradient, passed in as _Sky* globals). Where the
// geometry stops at the far plane the skybox carries on with the same colour, so there is no line.
Shader "Orca/Pixel Water"
{
    Properties
    {
        [MainTexture] _BaseMap ("Water Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _TextureScroll ("Texture Drift (tiles per second, X / Z)", Vector) = (0.01, 0.006, 0, 0)
        _RippleAmplitude ("Ripple Amount (texture tiles)", Range(0, 0.1)) = 0.025
        _RippleSpeed ("Ripple Speed", Range(0, 3)) = 1.2
        [Header(Horizon)]
        _HorizonFadeStart ("Fade Into Sky From (share of far plane)", Range(0, 1)) = 0.45
        _HorizonFadeEnd ("Fully Sky At (share of far plane)", Range(0, 1)) = 0.85
        _HorizonFadeMinDistance ("Never Fade Nearer Than (m)", Float) = 150
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST, _TextureScroll;
                float _RippleAmplitude, _RippleSpeed;
                half4 _BaseColor;
                float _HorizonFadeStart, _HorizonFadeEnd, _HorizonFadeMinDistance;
            CBUFFER_END

            // The skybox's gradient (Orca/Night Sky Skybox), set by OceanHorizon.
            half4 _SkyHorizonColor, _SkyZenithColor, _SkyGroundColor;
            float _SkyHorizonSharpness;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 waterUV : TEXCOORD0;
                float fogFactor : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            // Same as the skybox: horizon to zenith above, sinking to the ground colour below.
            half3 SkyColour(float3 direction)
            {
                float up = saturate(direction.y);
                half3 sky = lerp(_SkyHorizonColor.rgb, _SkyZenithColor.rgb, pow(up, 1.0 / max(_SkyHorizonSharpness, 0.01)));
                float below = saturate(-direction.y * 6.0);
                return lerp(sky, _SkyGroundColor.rgb, below);
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionWS = positionWS;
                // Metre-based tiling, independent of the imported ocean's scale and UVs.
                o.waterUV = positionWS.xz * _BaseMap_ST.xy + _BaseMap_ST.zw
                          + frac(_Time.y * _TextureScroll.xy);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // World-aligned ripples move the supplied pixels, including across the far-ocean seam.
                float t = _Time.y * _RippleSpeed;
                float2 ripples = float2(
                    sin(i.positionWS.z * 0.8 + t) + 0.5 * sin(i.positionWS.x * 1.3 - t * 0.7),
                    sin(i.positionWS.x * 0.65 - t * 0.85) + 0.5 * sin(i.positionWS.z * 1.1 + t * 1.2));
                float2 uv = i.waterUV + ripples * _RippleAmplitude;
                half3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb * _BaseColor.rgb;
                color = MixFog(color, i.fogFactor);
                float3 toPixel = i.positionWS - _WorldSpaceCameraPos;
                float dist = length(toPixel);
                float farPlane = _ProjectionParams.z;
                float fadeFrom = max(_HorizonFadeStart * farPlane, _HorizonFadeMinDistance);
                float fadeTo = max(_HorizonFadeEnd * farPlane, fadeFrom + 1.0);
                float horizon = smoothstep(fadeFrom, fadeTo, dist);
                color = lerp(color, SkyColour(toPixel / max(dist, 1e-4)), horizon);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
