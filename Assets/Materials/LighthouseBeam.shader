// Additive beam of light for the lighthouse (LighthouseLights): a closed cone running along the
// object's +z from the lantern, _StartRadius wide there and _EndRadius wide _Length further on,
// capped at both ends.
//
// It is drawn as a volume, not a surface. Only the cone's far side is drawn (its back faces, the
// caps included, so every ray that passes through the cone, from any side, is drawn exactly once
// where it leaves), and each pixel adds up the light along the eye's ray from where it came into
// the cone to where it leaves, or to whatever stands in the way first (the scene's depth), so
// trees, hills and the tower hide only the part of the beam behind them. The samples are spaced
// by the beam's width (close by the lantern, apart where it has spread) and dithered per pixel,
// so neither banding nor speckle shows.
//
// The cone is only the room the light has: the light itself is a soft core, _Core of the cone's
// width, thickest on the axis and gone, smoothly, by the cone's side. Going out, the core spreads
// wider (_Spread, dimmer the wider it gets) and fades, then thins away to nothing from _Tail on,
// gone nine tenths of the way along, so the beam diffuses into the night instead of stopping at
// the cone's end, from whichever side it is seen. Looking up the beam towards the lantern the eye
// looks through more of it, so it glows brighter, up to _MaxGlow. It fades away right in front of
// the camera so it never fills the screen. The scene's distance fog leaves it alone, so it still
// sweeps bright across the water when seen from the main island.
Shader "Orca/Lighthouse Beam"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 0.6, 0.12, 1)
        _Intensity ("Intensity", Range(0, 8)) = 0.8
        _Falloff ("Fade Along Length", Range(0, 6)) = 1.6
        _Tail ("Thins Away From (share of length)", Range(0, 0.85)) = 0.4
        _Spread ("Spreads Out Towards The End", Range(1, 1.6)) = 1.5
        _MaxGlow ("Brightest, Looking Up The Beam", Range(1, 20)) = 5
        _NearFade ("Fade Near The Camera (m)", Float) = 8
        // The cone's size, and how much of it the light fills, which LighthouseLights sets to
        // match the mesh.
        _Length ("Length", Float) = 70
        _StartRadius ("Start Radius", Float) = 2
        _EndRadius ("End Radius", Float) = 18
        _Core ("Light's Share Of The Cone", Range(0.1, 0.65)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Beam"
            Blend One One
            ZWrite Off
            // The far side only, drawn even behind things: the scene's depth cuts each ray short instead.
            Cull Front
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #define SAMPLES 16

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _Falloff;
                half _Tail;
                half _Spread;
                half _MaxGlow;
                float _NearFade;
                float _Length;
                float _StartRadius;
                float _EndRadius;
                float _Core;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 cameraOS : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.cameraOS = TransformWorldToObject(_WorldSpaceCameraPos);
                return output;
            }

            // Where the ray, ray * s from origin, comes into the capped cone, given that it leaves
            // at exitAt: the latest of where it crosses into the slab between the caps, where it
            // comes in through the cone's side, and the camera itself.
            float Entry(float3 origin, float3 ray, float exitAt)
            {
                float entry = 0.0;
                if (abs(ray.z) > 1e-5)
                {
                    float atStart = -origin.z / ray.z;
                    float atEnd = (_Length - origin.z) / ray.z;
                    entry = max(entry, min(atStart, atEnd));
                }
                // Inside the cone's side where x^2 + y^2 - (r0 + k z)^2 = a s^2 + b s + c <= 0.
                float k = (_EndRadius - _StartRadius) / _Length;
                float rz = _StartRadius + k * origin.z;
                float a = ray.x * ray.x + ray.y * ray.y - k * k * ray.z * ray.z;
                float b = 2.0 * (origin.x * ray.x + origin.y * ray.y - k * ray.z * rz);
                float c = origin.x * origin.x + origin.y * origin.y - rz * rz;
                float disc = b * b - 4.0 * a * c;
                if (abs(a) > 1e-6 && disc >= 0.0)
                {
                    float root = sqrt(disc);
                    float s1 = (-b - root) / (2.0 * a), s2 = (-b + root) / (2.0 * a);
                    float low = min(s1, s2), high = max(s1, s2);
                    // Shallower than the side: inside between the two crossings. Steeper and heading
                    // out from the lantern: inside after the second (the first is the cone's mirror
                    // image behind its tip). Steeper and heading back: the caps bound it.
                    if (a > 0.0) entry = max(entry, low);
                    else if (ray.z > 0.0) entry = max(entry, high);
                }
                else if (abs(a) <= 1e-6 && b < -1e-6)
                {
                    // Parallel to the side, crossing it once on the way in.
                    entry = max(entry, -c / b);
                }
                return min(entry, exitAt);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 origin = input.cameraOS;
                float3 toExit = input.positionOS - origin;
                float exitAt = length(toExit);
                float3 ray = toExit / max(exitAt, 1e-4);
                float entryAt = Entry(origin, ray, exitAt);

                // Anything standing in the way cuts the ray short. The depth is along the camera's
                // view; turned into distance along this ray, then into the cone's own units.
                if (unity_OrthoParams.w == 0.0)
                {
                    float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                    float eye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                    float3 rayWS = normalize(input.positionWS - _WorldSpaceCameraPos);
                    float3 forward = -UNITY_MATRIX_V[2].xyz;
                    float sceneWS = eye / max(dot(rayWS, forward), 1e-4);
                    float worldPerUnit = max(length(TransformObjectToWorldDir(ray, false)), 1e-4);
                    exitAt = min(exitAt, sceneWS / worldPerUnit);
                }
                if (exitAt <= entryAt) return half4(0.0h, 0.0h, 0.0h, 0.0h);

                // Add up the light along the way: a soft core, spreading and fading as it goes,
                // thinned away to nothing by the cone's side and end, and faded right by the camera.
                // Looking along the beam, the samples go as far apart as the beam is wide: close
                // together by the lantern, where the core is narrow and brightest, further apart
                // where it has spread. The cone's width changes in step along any ray, so the
                // samples are spaced evenly in the log of it.
                float k = (_EndRadius - _StartRadius) / _Length;
                float widening = k * ray.z;
                float widthIn = max(_StartRadius + k * (origin.z + ray.z * entryAt), 1e-3);
                float widthOut = max(_StartRadius + k * (origin.z + ray.z * exitAt), 1e-3);
                float logRatio = log(widthOut / widthIn);
                bool byWidth = abs(logRatio) > 0.05 && abs(widening) > 1e-6;
                float dither = frac(52.9829189 * frac(dot(input.positionCS.xy, float2(0.06711056, 0.00583715))));
                float light = 0.0;
                [unroll]
                for (int i = 0; i < SAMPLES; i++)
                {
                    float t = (i + dither) / SAMPLES;
                    float s, ds;
                    if (byWidth)
                    {
                        float width = widthIn * exp(logRatio * t);
                        s = entryAt + (width - widthIn) / widening;
                        ds = logRatio * width / widening / SAMPLES;
                    }
                    else
                    {
                        s = lerp(entryAt, exitAt, t);
                        ds = (exitAt - entryAt) / SAMPLES;
                    }
                    float3 p = origin + ray * s;
                    float along = p.z / _Length;
                    float within = step(0.0, along) * step(along, 1.0);
                    along = saturate(along);
                    float radius = max(_StartRadius + k * p.z, 1e-3);
                    float core = radius * _Core * lerp(1.0, _Spread, along);
                    float across = dot(p.xy, p.xy);
                    // Gone, and smoothly, by the cone's side and well before its end, so neither shows.
                    float wall = saturate(1.0 - across / (radius * radius));
                    float edge = exp(-2.5 * across / (core * core)) * wall * wall;
                    // In from the lantern's middle (hidden in the glass), out again by the far end.
                    float fade = smoothstep(0.0, 0.015, along) * exp(-_Falloff * along) * (1.0 - smoothstep(_Tail, 0.9, along));
                    light += within * edge * fade * saturate(s / max(_NearFade, 1e-3)) / core * ds;
                }
                // Brighter the more of it the eye looks through, easing off towards _MaxGlow.
                light = _MaxGlow * (1.0 - exp(-light / _MaxGlow));

                return half4(_Color.rgb * (half)(light * _Intensity), 0.0h);
            }
            ENDHLSL
        }
    }
}
