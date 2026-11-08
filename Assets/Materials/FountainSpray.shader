// The fountain's droplets and splashes: alpha-clipped pixel-art billboards lit by the scene.
// The pale droplet art took on the full colour of the warm lamps and turned yellow, so here
// the lights mostly set how bright the water is and keep little of their own hue, and a
// lamp close by can't burn the droplets past _MaxLight. Light wraps round the droplets, as
// water scatters it, so a lamp behind the spray still lights it.
Shader "Orca/Fountain Spray"
{
    Properties
    {
        [MainTexture] _BaseMap ("Droplet Sheet", 2D) = "white" {}
        [MainColor] _BaseColor ("Water Tint", Color) = (0.52, 0.76, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _LightTint ("Light Colour Share", Range(0, 1)) = 0.2
        _Knee ("Full Light Below", Range(0, 2)) = 0.6
        _MaxLight ("Brightest", Range(0.5, 3)) = 1.1
    }

    HLSLINCLUDE
    #define _ALPHATEST_ON 1
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half _Cutoff;
        half _LightTint;
        half _Knee;
        half _MaxLight;
    CBUFFER_END

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4 color       : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                // Particle colour with the lighting already multiplied in.
                half4 color       : COLOR;
                float fogFactor   : TEXCOORD1;
            };

            // Half-Lambert: light from behind still reaches a droplet, at half strength.
            half Wrap(float3 normal, half3 direction)
            {
                return saturate(dot(normal, direction) * 0.5 + 0.5);
            }

            // The light on a droplet. Lit per vertex, not per pixel: a droplet is a few pixels
            // across and its four corners see the same lamps, while thousands of overlapping
            // alpha-clipped quads walking the light list for every pixel cost a big share of the
            // frame wherever the fountain is on screen.
            half3 DropletLight(float3 positionWS, float3 normal, float4 positionCS)
            {
                half3 light = SampleSH(normal);
                Light mainLight = GetMainLight();
                light += mainLight.color * mainLight.distanceAttenuation * Wrap(normal, mainLight.direction);

                #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                // The same screen UV the pixel shader would get, which picks the light cluster.
                float4 ndc = positionCS * 0.5;
                ndc.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
                inputData.normalizedScreenSpaceUV = ndc.xy / positionCS.w;
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light lamp = GetAdditionalLight(lightIndex, positionWS);
                    light += lamp.color * lamp.distanceAttenuation * Wrap(normal, lamp.direction);
                LIGHT_LOOP_END
                #endif

                // Brightness from the lights, with only a share of their colour.
                half brightness = Luminance(light);
                half3 hue = brightness > 1e-4 ? light / brightness : half3(1, 1, 1);
                half3 tint = lerp(half3(1, 1, 1), hue, _LightTint);
                // Linear up to the knee, then easing towards _MaxLight.
                half headroom = max(_MaxLight - _Knee, 1e-3);
                half level = brightness <= _Knee ? brightness :
                    _Knee + headroom * (1.0 - exp(-(brightness - _Knee) / headroom));
                return tint * level;
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                float3 normal = normalize(TransformObjectToWorldNormal(input.normalOS));
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.color = half4(input.color.rgb * DropletLight(positionWS, normal, o.positionCS), input.color.a);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 texel = SampleAlbedoAlpha(i.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
                Alpha(texel.a * i.color.a, _BaseColor, _Cutoff);
                half3 col = texel.rgb * _BaseColor.rgb * i.color.rgb;
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
