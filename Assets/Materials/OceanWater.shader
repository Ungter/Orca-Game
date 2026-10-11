Shader "Orca/Ocean Water"
{
    Properties
    {
        [Header(Color)]
        _DeepColor ("Deep Color", Color) = (0.01, 0.09, 0.2, 1)
        _ShallowColor ("Shallow / Crest Color", Color) = (0.05, 0.42, 0.5, 1)
        _HorizonColor ("Horizon Tint", Color) = (0.35, 0.55, 0.7, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 10)) = 5
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.6
        _Smoothness ("Smoothness", Range(0, 1)) = 0.92

        [Header(Waves)]
        _WaveSpeed ("Wave Speed", Range(0, 3)) = 1
        _WaveHeight ("Wave Height", Range(0, 3)) = 1
        _WaveA ("Wave A (dirX, dirZ, steepness, wavelength)", Vector) = (1, 0.3, 0.22, 24)
        _WaveB ("Wave B (dirX, dirZ, steepness, wavelength)", Vector) = (0.6, 1, 0.18, 14)
        _WaveC ("Wave C (dirX, dirZ, steepness, wavelength)", Vector) = (-0.4, 0.8, 0.14, 8)
        _WaveD ("Wave D (dirX, dirZ, steepness, wavelength)", Vector) = (0.9, -0.5, 0.1, 4.5)

        [Header(Wave Variation)]
        _SecondaryWaves ("Secondary Wave Strength", Range(0, 1)) = 0.35
        _WaveVariation ("Regional Wave Variation", Range(0, 1)) = 0.6
        _WaveVariationScale ("Variation Scale", Range(5, 300)) = 60

        [Header(Ripples)]
        _RippleScale ("Ripple Scale", Range(0.05, 4)) = 0.6
        _RippleStrength ("Ripple Strength", Range(0, 2)) = 0.45
        _RippleSpeed ("Ripple Speed", Range(0, 3)) = 0.6

        [Header(Lighting)]
        _SpecularIntensity ("Specular Intensity", Range(0, 10)) = 3
        _SpecularPower ("Specular Power", Range(8, 1024)) = 256
        _ScatterColor ("Subsurface Scatter Color", Color) = (0.1, 0.6, 0.55, 1)
        _ScatterStrength ("Subsurface Scatter Strength", Range(0, 3)) = 1

        [Header(Sky Reflection)]
        _SkyReflection ("Sky Reflection (stars and aurora)", Range(0, 2)) = 0.6
        _SkyReflectionDistortion ("Ripple Distortion", Range(0, 1)) = 0.5
        _SkyRotation ("Sky Rotation (match skybox)", Range(0, 360)) = 0
        _StarDensity ("Star Density", Range(20, 400)) = 160
        _StarAmount ("Star Amount", Range(0, 1)) = 0.35
        _StarSize ("Star Size", Range(0.01, 0.3)) = 0.09
        _StarBrightness ("Star Brightness", Range(0, 20)) = 6
        _StarGlow ("Star Glow", Range(0, 1)) = 0.35
        _TwinkleSpeed ("Twinkle Speed", Range(0, 10)) = 2.5
        _TwinkleAmount ("Twinkle Amount", Range(0, 1)) = 0.7
        _AuroraColorA ("Aurora Low Color", Color) = (0.1, 1.0, 0.45, 1)
        _AuroraColorB ("Aurora High Color", Color) = (0.45, 0.2, 1.0, 1)
        _AuroraIntensity ("Aurora Intensity", Range(0, 3)) = 0.45
        _AuroraSpeed ("Aurora Drift Speed", Range(0, 2)) = 0.25
        _AuroraScale ("Aurora Scale", Range(0.1, 4)) = 1
        _AuroraFadePeriod ("Aurora Fade Period (s)", Range(5, 120)) = 28
        _AuroraMinVisibility ("Aurora Min Visibility", Range(0, 1)) = 0.05

        [Header(Foam)]
        _FoamColor ("Foam Color", Color) = (0.92, 0.96, 1, 1)
        _CrestFoamThreshold ("Crest Foam Threshold", Range(0, 1)) = 0.6
        _CrestFoamAmount ("Crest Foam Amount", Range(0, 1)) = 0.6
        _ShoreFoamDistance ("Shore Foam Distance", Range(0, 10)) = 1.5
        _ShoreFoamAmount ("Shore Foam Amount", Range(0, 1)) = 0.8
        _FoamScale ("Foam Noise Scale", Range(0.1, 10)) = 1.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor, _ShallowColor, _HorizonColor, _ScatterColor, _FoamColor;
                half _FresnelPower, _ReflectionStrength, _Smoothness;
                float _WaveSpeed, _WaveHeight;
                float4 _WaveA, _WaveB, _WaveC, _WaveD;
                float _SecondaryWaves, _WaveVariation, _WaveVariationScale;
                float _RippleScale, _RippleStrength, _RippleSpeed;
                half _SpecularIntensity, _SpecularPower, _ScatterStrength;
                half _CrestFoamThreshold, _CrestFoamAmount, _ShoreFoamDistance, _ShoreFoamAmount, _FoamScale;
                float _SkyReflection, _SkyReflectionDistortion, _SkyRotation;
                float _StarDensity, _StarAmount, _StarSize, _StarBrightness, _StarGlow, _TwinkleSpeed, _TwinkleAmount;
                half4 _AuroraColorA, _AuroraColorB;
                float _AuroraIntensity, _AuroraSpeed, _AuroraScale, _AuroraFadePeriod, _AuroraMinVisibility;
            CBUFFER_END

            // The reflection is blurred by the waves anyway, so fewer aurora layers are enough.
            #define NIGHT_SKY_AURORA_STEPS 8
            #include "NightSky.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 baseXZ     : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
            };

            float3 Gerstner(float4 w, float amp, float2 p, float t, inout float3 tangent, inout float3 binormal)
            {
                float steep = w.z * _WaveHeight * amp;
                float k = TWO_PI / max(w.w, 0.01);
                float c = sqrt(9.8 / k);
                float2 d = normalize(w.xy + 1e-5);
                float f = k * (dot(d, p) - c * t);
                float a = steep / k;
                float s = sin(f), co = cos(f);
                tangent  += float3(-d.x * d.x * steep * s, d.x * steep * co, -d.x * d.y * steep * s);
                binormal += float3(-d.x * d.y * steep * s, d.y * steep * co, -d.y * d.y * steep * s);
                return float3(d.x * a * co, a * s, d.y * a * co);
            }

            // A weaker copy of a base wave, turned and stretched by irrational factors so the
            // combined pattern never lines up into a repeating tile.
            float4 SecondaryWave(float4 w, float angle, float lengthScale)
            {
                float2 d = normalize(w.xy + 1e-5);
                float s = sin(angle), c = cos(angle);
                return float4(c * d.x - s * d.y, s * d.x + c * d.y, w.z * _SecondaryWaves, w.w * lengthScale);
            }

            // Each wave fades in and out over large, slowly drifting regions, so calm and rough
            // patches wander across the sea. Only ever weakens a wave, so crests can't loop over.
            float WaveRegion(float2 p, float seed, float t)
            {
                float2 q = p / max(_WaveVariationScale, 1.0) + float2(seed * 17.31, seed * -9.73) + t * 0.004;
                float n = ns_noise2(q) * 0.65 + ns_noise2(q * 2.17 + 5.1) * 0.35;
                return 1.0 - _WaveVariation * smoothstep(0.25, 0.75, n);
            }

            float3 Waves(float2 p, out float3 normal)
            {
                float t = _Time.y * _WaveSpeed;
                float3 tangent = float3(1, 0, 0);
                float3 binormal = float3(0, 0, 1);
                float3 offset = 0;
                offset += Gerstner(_WaveA, WaveRegion(p, 0, t), p, t, tangent, binormal);
                offset += Gerstner(_WaveB, WaveRegion(p, 1, t), p, t, tangent, binormal);
                offset += Gerstner(_WaveC, WaveRegion(p, 2, t), p, t, tangent, binormal);
                offset += Gerstner(_WaveD, WaveRegion(p, 3, t), p, t, tangent, binormal);
                offset += Gerstner(SecondaryWave(_WaveA, 0.83, 0.618), WaveRegion(p, 4, t), p, t, tangent, binormal);
                offset += Gerstner(SecondaryWave(_WaveB, -1.21, 1.371), WaveRegion(p, 5, t), p, t, tangent, binormal);
                offset += Gerstner(SecondaryWave(_WaveC, 2.37, 0.773), WaveRegion(p, 6, t), p, t, tangent, binormal);
                offset += Gerstner(SecondaryWave(_WaveD, -2.69, 1.229), WaveRegion(p, 7, t), p, t, tangent, binormal);
                normal = normalize(cross(binormal, tangent));
                return offset;
            }

            // Float-safe hash; the old frac(p * 123.34) one repeated every 50 cells.
            float Hash(float2 p)
            {
                return ns_hash12(p);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float RippleHeight(float2 p)
            {
                float t = _Time.y * _RippleSpeed;
                float h = ValueNoise(p + float2(t, t * 0.6)) * 0.5;
                h += ValueNoise(p * 2.1 + float2(-t * 1.3, t * 0.8)) * 0.3;
                h += ValueNoise(p * 4.3 + float2(t * 0.7, -t * 1.6)) * 0.2;
                return h;
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 n;
                float2 baseXZ = posWS.xz;
                posWS += Waves(baseXZ, n);
                o.positionWS = posWS;
                o.baseXZ = baseXZ;
                o.positionCS = TransformWorldToHClip(posWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 normal;
                float3 offset = Waves(i.baseXZ, normal);
                float3 waveNormal = normal;

                float2 rp = i.baseXZ * _RippleScale;
                float e = 0.05;
                float h0 = RippleHeight(rp);
                float hx = RippleHeight(rp + float2(e, 0));
                float hz = RippleHeight(rp + float2(0, e));
                float3 rippleN = float3(-(hx - h0) / e, 0, -(hz - h0) / e) * _RippleStrength;
                normal = normalize(normal + rippleN);

                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float NdotV = saturate(dot(normal, viewDir));
                float fresnel = pow(1.0 - NdotV, _FresnelPower);

                float maxAmp = 0.0;
                maxAmp += _WaveA.z * _WaveA.w;
                maxAmp += _WaveB.z * _WaveB.w;
                maxAmp += _WaveC.z * _WaveC.w;
                maxAmp += _WaveD.z * _WaveD.w;
                maxAmp += _SecondaryWaves * (_WaveA.z * _WaveA.w * 0.618 + _WaveB.z * _WaveB.w * 1.371
                                           + _WaveC.z * _WaveC.w * 0.773 + _WaveD.z * _WaveD.w * 1.229);
                maxAmp = max(maxAmp * _WaveHeight / TWO_PI, 1e-3);
                float crest = saturate(offset.y / maxAmp * 0.5 + 0.5);

                half3 waterCol = lerp(_DeepColor.rgb, _ShallowColor.rgb, crest * crest);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half atten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half3 L = mainLight.direction;

                half3 ambient = SampleSH(normal);
                half3 diffuse = waterCol * (mainLight.color * saturate(dot(normal, L)) * atten * 0.5 + ambient);

                half scatter = pow(saturate(dot(viewDir, -L)), 4.0) * crest * _ScatterStrength;
                diffuse += _ScatterColor.rgb * mainLight.color * scatter * atten;

                float3 reflDir = reflect(-viewDir, normal);
                half3 envRefl = GlossyEnvironmentReflection(reflDir, 1.0 - _Smoothness, 1.0);
                // The horizon tint is scaled by the scene's ambient light so it can't glow on its own in the dark.
                half envLevel = saturate(Luminance(SampleSH(half3(0, 1, 0))) * 4.0);
                envRefl = lerp(_HorizonColor.rgb * envLevel, envRefl, 0.7);
                half3 col = lerp(diffuse, envRefl, saturate(fresnel * _ReflectionStrength + 0.04));

                // Stars and aurora mirrored on the surface. Ripples are partly smoothed out of the
                // lookup so the reflection reads as wobbling lights instead of pure noise.
                float3 skyN = normalize(lerp(waveNormal, normal, _SkyReflectionDistortion));
                float3 skyDir = reflect(-viewDir, skyN);
                skyDir = normalize(float3(skyDir.x, max(skyDir.y, 0.0), skyDir.z));
                half3 skyLights = NightSkyLights(ns_RotateY(skyDir, _SkyRotation), _Time.y);
                col += skyLights * _SkyReflection * saturate(fresnel + 0.25);

                float3 H = normalize(L + viewDir);
                half spec = pow(saturate(dot(normal, H)), _SpecularPower) * _SpecularIntensity;
                col += mainLight.color * spec * atten;

                // Point lights (the lamps): a soft diffuse pool plus a broad glint on the waves.
                #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, i.positionWS);
                    half3 radiance = light.color * light.distanceAttenuation * light.shadowAttenuation;
                    col += waterCol * radiance * saturate(dot(normal, light.direction)) * 0.5;
                    half3 halfDir = normalize(light.direction + viewDir);
                    col += radiance * pow(saturate(dot(normal, halfDir)), _SpecularPower * 0.25) * _SpecularIntensity * 0.25;
                LIGHT_LOOP_END
                #endif

                float foamNoise = ValueNoise(i.baseXZ * _FoamScale + _Time.y * 0.3) * 0.6
                                + ValueNoise(i.baseXZ * _FoamScale * 2.7 - _Time.y * 0.5) * 0.4;

                float crestFoam = smoothstep(_CrestFoamThreshold, 1.0, crest) * _CrestFoamAmount;
                crestFoam *= smoothstep(0.35, 0.65, foamNoise);

                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfDepth = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float depthDiff = sceneDepth - surfDepth;
                float shore = depthDiff > 0 ? 1.0 - saturate(depthDiff / max(_ShoreFoamDistance, 1e-3)) : 0;
                float shoreBands = smoothstep(0.4, 0.6, frac(shore * 3.0 - _Time.y * 0.4) * foamNoise + shore * 0.5);
                float shoreFoam = saturate(shore * shoreBands) * _ShoreFoamAmount;

                float foam = saturate(crestFoam + shoreFoam);
                half3 foamLit = _FoamColor.rgb * (mainLight.color * atten * 0.7 + ambient);
                col = lerp(col, foamLit, foam);

                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
