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
                half4 color       : COLOR;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float fogFactor   : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.color = input.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // Half-Lambert: light from behind still reaches a droplet, at half strength.
            half Wrap(float3 normal, half3 direction)
            {
                return saturate(dot(normal, direction) * 0.5 + 0.5);
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 texel = SampleAlbedoAlpha(i.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
                Alpha(texel.a * i.color.a, _BaseColor, _Cutoff);
                half3 albedo = texel.rgb * _BaseColor.rgb * i.color.rgb;
                float3 normal = normalize(i.normalWS);

                half3 light = SampleSH(normal);
                Light mainLight = GetMainLight();
                light += mainLight.color * mainLight.distanceAttenuation * Wrap(normal, mainLight.direction);

                #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light lamp = GetAdditionalLight(lightIndex, i.positionWS);
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

                half3 col = albedo * tint * level;
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
