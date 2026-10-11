Shader "Orca/Night Sky Skybox"
{
    Properties
    {
        [Header(Sky)]
        _ZenithColor ("Zenith Color", Color) = (0.005, 0.008, 0.03, 1)
        _HorizonColor ("Horizon Color", Color) = (0.03, 0.06, 0.13, 1)
        _GroundColor ("Below Horizon Color", Color) = (0.004, 0.006, 0.015, 1)
        _HorizonSharpness ("Horizon Sharpness", Range(0.5, 8)) = 2.5
        _Rotation ("Rotation", Range(0, 360)) = 0

        [Header(Stars)]
        _StarDensity ("Star Density", Range(20, 400)) = 160
        _StarAmount ("Star Amount", Range(0, 1)) = 0.35
        _StarSize ("Star Size", Range(0.01, 0.3)) = 0.09
        _StarBrightness ("Star Brightness", Range(0, 20)) = 6
        _StarGlow ("Star Glow", Range(0, 1)) = 0.35
        _TwinkleSpeed ("Twinkle Speed", Range(0, 10)) = 2.5
        _TwinkleAmount ("Twinkle Amount", Range(0, 1)) = 0.7

        [Header(Aurora)]
        _AuroraColorA ("Aurora Low Color", Color) = (0.1, 1.0, 0.45, 1)
        _AuroraColorB ("Aurora High Color", Color) = (0.45, 0.2, 1.0, 1)
        _AuroraIntensity ("Aurora Intensity", Range(0, 3)) = 0.45
        _AuroraSpeed ("Aurora Drift Speed", Range(0, 2)) = 0.25
        _AuroraScale ("Aurora Scale", Range(0.1, 4)) = 1
        _AuroraFadePeriod ("Aurora Fade Period (s)", Range(5, 120)) = 28
        _AuroraMinVisibility ("Aurora Min Visibility", Range(0, 1)) = 0.05
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            half4 _ZenithColor, _HorizonColor, _GroundColor;
            float _HorizonSharpness, _Rotation;
            float _StarDensity, _StarAmount, _StarSize, _StarBrightness, _StarGlow, _TwinkleSpeed, _TwinkleAmount;
            half4 _AuroraColorA, _AuroraColorB;
            float _AuroraIntensity, _AuroraSpeed, _AuroraScale, _AuroraFadePeriod, _AuroraMinVisibility;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;
                return o;
            }

            #include "NightSky.hlsl"

            half4 frag(v2f i) : SV_Target
            {
                float3 dir = ns_RotateY(normalize(i.direction), _Rotation);
                float t = _Time.y;

                float up = saturate(dir.y);
                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(up, 1.0 / _HorizonSharpness));
                float below = saturate(-dir.y * 6.0);
                sky = lerp(sky, _GroundColor.rgb, below);

                return half4(sky + NightSkyLights(dir, t), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
