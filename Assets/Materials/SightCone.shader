// The cleared view down to the villain when something on the island stands between him and the
// lighthouse (MouseTrapSightline): a second camera draws the island without what is in the way,
// and this lays its picture over the screen in a soft-edged circle round him, with a faint warm
// ring like lantern light at the rim. Drawn from OnGUI with Graphics.DrawTexture (premultiplied).
Shader "Orca/Sight Cone"
{
    Properties
    {
        _MainTex ("View", 2D) = "black" {}
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Inner ("Fully Clear Within (share of radius)", Range(0, 1)) = 0.72
        [HDR] _RingColor ("Ring Colour", Color) = (1, 0.78, 0.4, 1)
        _Ring ("Ring Strength", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "IgnoreProjector"="True" }

        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Opacity, _Inner, _Ring;
                half4 _RingColor;
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
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // The picture is square round the villain: 0 at the middle, 1 at the circle's edge.
                float d = length(i.uv - 0.5) * 2.0;
                half mask = 1.0h - smoothstep(_Inner, 0.96, d);
                half ring = exp(-pow((d - 0.93) / 0.03, 2.0)) * _Ring;
                half3 view = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                half3 colour = (view * mask + _RingColor.rgb * ring) * _Opacity;
                return half4(colour, mask * _Opacity);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
