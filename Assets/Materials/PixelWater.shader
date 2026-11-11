// The ocean: the supplied pixel-art water, unlit, tiled in world metres, with scene fog. Far
// out it melts into the sky so the sea never visibly ends: OceanHorizon lays a much bigger
// copy of the ocean under it, and from _HorizonFadeStart to _HorizonFadeEnd of the camera's far
// plane (never nearer than _HorizonFadeMinDistance) the water turns into the night sky's own
// colour for that direction (the skybox's gradient, passed in as _Sky* globals). Where the
// geometry stops at the far plane the skybox carries on with the same colour, so there is no line.
//
// Round the camera the sea is low-poly waves (OceanWaves): one connected faceted surface, like
// the shore rocks, made of a grid of points whose vertices say so (uv5.x = 1). Here each point is
// nudged off the grid (the same way every time for the same spot of sea), lifted by the waves, and
// the grid carried along with the camera, snapped two points at a time; the waves die down with
// distance from the camera itself, so nothing jumps as it moves. Each flat facet is lit by how it
// tilts against the moon and a little lighter on crests. Round the hull riding the waves (the
// starting boat, _OceanHull) the water is held down below it and eases back up out from its
// sides, so none stands up inside it or over its sides. The flat sea (_OceanFlatCut, set on the
// map's ocean and the Far Ocean only) is cut away under the waves so the troughs show. With no
// OceanWaves in the scene the _Ocean* globals are zero and the water is flat as before.
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
            #pragma target 3.0
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

            // The waves, set by OceanWaves (all zero without it).
            float4 _OceanVoxel;            // point spacing, nudge off the grid (share of it), sea level, wave time
            float4 _OceanWaveA, _OceanWaveB, _OceanWaveC; // direction x, z, wavenumber, angular speed
            float4 _OceanWaveShape;        // share of waves A, B, C; height
            float4 _OceanReach;            // full waves within, flat beyond, flat sea cut within, hull margin
            float4 _OceanCalm[8];          // calm areas: middle x, z, half size x, z
            float4 _OceanCalmShape;        // how many, share of the waves left by them, falloff
            float4 _OceanVoxelLook;        // how much the facets' tilt shows, crests' lightening
            float4 _OceanHull;             // the hull riding the waves: middle x, z, heading x, z
            float4 _OceanHullSize;         // half its length and width, highest water under it, rise per metre out (0: none)
            // Per renderer (a property block on the flat sea only): cut away under the waves.
            float _OceanFlatCut;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 kind : TEXCOORD5;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 waterUV : TEXCOORD0;
                float fogFactor : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                // 1 on the flat sea, 0 on the wave surface.
                float flat : TEXCOORD3;
                // How high the wave stands against its full height: one value per facet, like the
                // shore rocks' flat tones.
                nointerpolation float crest : TEXCOORD4;
            };

            // Same as the skybox: horizon to zenith above, sinking to the ground colour below.
            half3 SkyColour(float3 direction)
            {
                float up = saturate(direction.y);
                half3 sky = lerp(_SkyHorizonColor.rgb, _SkyZenithColor.rgb, pow(up, 1.0 / max(_SkyHorizonSharpness, 0.01)));
                float below = saturate(-direction.y * 6.0);
                return lerp(sky, _SkyGroundColor.rgb, below);
            }

            // The middle of the wave surface round this camera, snapped two points at a time so its
            // triangles always fall the same way on the same spot of sea.
            float2 PatchCentre()
            {
                float pair = max(_OceanVoxel.x, 1e-3) * 2.0;
                return floor(_WorldSpaceCameraPos.xz / pair + 0.5) * pair;
            }

            float Wave(float4 w, float share, float2 p)
            {
                return share * sin(w.z * dot(w.xy, p) - w.w * _OceanVoxel.w);
            }

            // Height of the waves above the sea at p, dying down out from reachFrom (the camera); the
            // same sums as OceanWaves.HeightAt.
            float WaveHeight(float2 p, float2 reachFrom)
            {
                float h = Wave(_OceanWaveA, _OceanWaveShape.x, p) + Wave(_OceanWaveB, _OceanWaveShape.y, p)
                        + Wave(_OceanWaveC, _OceanWaveShape.z, p);
                float nearest = 1e9;
                float count = _OceanCalmShape.x;
                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float2 d = max(abs(p - _OceanCalm[i].xy) - _OceanCalm[i].zw, 0.0);
                    nearest = min(nearest, i < count ? length(d) : 1e9);
                }
                float calm = count < 0.5 ? 1.0 : lerp(_OceanCalmShape.y, 1.0, smoothstep(0.0, max(_OceanCalmShape.z, 1e-5), nearest));
                float reach = 1.0 - smoothstep(_OceanReach.x, max(_OceanReach.y, _OceanReach.x + 1e-5), distance(p, reachFrom));
                return h * _OceanWaveShape.w * calm * reach;
            }

            // The surface's height at a point: the waves, held down round the hull riding them: below
            // it under it and for the hull margin out, then easing back up, the same as
            // OceanWaves.SurfaceHeightAt, so the water moves smoothly as the hull does.
            float SurfaceHeight(float2 p, float2 reachFrom)
            {
                float h = WaveHeight(p, reachFrom);
                if (_OceanHullSize.w > 0.0)
                {
                    float2 offset = p - _OceanHull.xy;
                    float along = max(abs(dot(offset, _OceanHull.zw)) - _OceanHullSize.x, 0.0);
                    float across = max(abs(offset.x * _OceanHull.w - offset.y * _OceanHull.z) - _OceanHullSize.y, 0.0);
                    float outside = sqrt(along * along + across * across);
                    h = min(h, _OceanHullSize.z + max(outside - _OceanReach.w, 0.0) * _OceanHullSize.w);
                }
                return h;
            }

            // Where a grid point is nudged to, the same every time for the same spot of sea.
            float2 Nudge(float2 spot)
            {
                float2 index = floor(spot / max(_OceanVoxel.x, 1e-3) + 0.5);
                float2 random = frac(sin(float2(dot(index, float2(127.1, 311.7)), dot(index, float2(269.5, 183.3)))) * 43758.5453);
                return (random - 0.5) * 2.0 * _OceanVoxel.y * _OceanVoxel.x;
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS;
                o.flat = 1.0;
                o.crest = 0.0;
                if (input.kind.x > 0.5)
                {
                    float2 spot = input.positionOS.xz + PatchCentre();
                    spot += Nudge(spot);
                    float h = SurfaceHeight(spot, _WorldSpaceCameraPos.xz);
                    positionWS = float3(spot.x, _OceanVoxel.z + h, spot.y);
                    o.flat = 0.0;
                    o.crest = clamp(h / max(_OceanWaveShape.w, 1e-3), -1.0, 1.0);
                }
                else
                {
                    positionWS = TransformObjectToWorld(input.positionOS.xyz);
                }
                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionWS = positionWS;
                // Metre-based tiling, independent of the imported ocean's scale and UVs.
                o.waterUV = positionWS.xz * _BaseMap_ST.xy + _BaseMap_ST.zw + frac(_Time.y * _TextureScroll.xy);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // The flat sea gives way to the wave surface round the camera.
                if (_OceanFlatCut > 0.5 && i.flat > 0.5 && _OceanReach.z > 0.0)
                    clip(distance(i.positionWS.xz, PatchCentre()) - _OceanReach.z);

                // Each facet of the wave surface lit by how it tilts against the moon (the main
                // light), lighter on crests; level water, as the flat sea is, stays as it is.
                half shade = 1.0;
                if (i.flat < 0.5)
                {
                    float3 normal = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                    normal *= normal.y < 0.0 ? -1.0 : 1.0;
                    float3 moon = _MainLightPosition.xyz;
                    moon = dot(moon, moon) > 1e-4 ? normalize(moon) : float3(0.45, 0.75, -0.5);
                    shade = 1.0 + _OceanVoxelLook.x * (dot(normal, moon) - moon.y) + _OceanVoxelLook.y * i.crest;
                }

                // World-aligned ripples move the supplied pixels, including across the far-ocean seam.
                float t = _Time.y * _RippleSpeed;
                float2 ripples = float2(
                    sin(i.positionWS.z * 0.8 + t) + 0.5 * sin(i.positionWS.x * 1.3 - t * 0.7),
                    sin(i.positionWS.x * 0.65 - t * 0.85) + 0.5 * sin(i.positionWS.z * 1.1 + t * 1.2));
                float2 uv = i.waterUV + ripples * _RippleAmplitude;
                half3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb * _BaseColor.rgb * max(shade, 0.0);
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
