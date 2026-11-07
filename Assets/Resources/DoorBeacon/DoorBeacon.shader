// Additive column of light for DoorBeacon. The quad always turns its face to the camera about
// the vertical axis (object x = sideways offset, object y = height above the base), so it
// never shows edge-on. Brightest at the ground (UV y = 0), fading out towards the top (y = 1),
// with a bright core and a soft halo across UV x, a slow pulse and faint bands drifting up.
// It is pulled a little towards the camera so the wall behind the door doesn't cut it in half.
Shader "Orca/Door Beacon"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 4)) = 0.7
        _FadePower ("Fade Upwards", Range(0.5, 6)) = 1.6
        _Core ("Core Width", Range(0.05, 1)) = 0.3
        _Pulse ("Pulse Amount", Range(0, 1)) = 0.2
        _PulseSpeed ("Pulse Speed", Range(0, 3)) = 0.5
        _Drift ("Rising Bands", Range(0, 1)) = 0.3
        _DriftSpeed ("Band Speed", Range(0, 3)) = 0.4
        _TowardCamera ("Pull Towards Camera", Float) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Beacon"
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
                half _Core;
                half _Pulse;
                half _PulseSpeed;
                half _Drift;
                half _DriftSpeed;
                float _TowardCamera;
                half _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fog : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 baseWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                // The camera's right, kept level, so the column stays upright.
                float3 right = UNITY_MATRIX_V[0].xyz;
                right.y = 0.0;
                right = dot(right, right) > 1e-6 ? normalize(right) : float3(1.0, 0.0, 0.0);
                float3 positionWS = baseWS + right * input.positionOS.x + float3(0.0, input.positionOS.y, 0.0);
                float3 toCamera = _WorldSpaceCameraPos - positionWS;
                positionWS += toCamera * (_TowardCamera / max(length(toCamera), 1e-4));
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half across = 1.0h - abs(input.uv.x * 2.0h - 1.0h);
                half halo = across * across;
                half core = smoothstep(1.0h - _Core, 1.0h, across);
                half profile = halo * 0.6h + core * 0.4h;
                half along = saturate(input.uv.y);
                // A quick rise off the ground hides where the quad meets it; then the long fade.
                half fade = smoothstep(0.0h, 0.03h, along) * pow(1.0h - along, _FadePower);
                half pulse = 1.0h - _Pulse * (0.5h + 0.5h * sin(_Time.y * _PulseSpeed * 6.2831853h));
                half bands = 1.0h - _Drift * (0.5h + 0.5h * sin((along * 3.0h - _Time.y * _DriftSpeed) * 6.2831853h));
                half amount = profile * fade * pulse * bands * _Intensity * _Opacity;
                half3 rgb = _Color.rgb * amount;
                // Additive: fog fades the beam to nothing rather than to the fog colour.
                rgb = MixFogColor(rgb, half3(0.0h, 0.0h, 0.0h), input.fog);
                return half4(rgb, 0.0h);
            }
            ENDHLSL
        }
    }
}
