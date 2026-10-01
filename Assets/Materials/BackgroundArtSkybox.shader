Shader "Orca/Background Art Skybox"
{
    Properties
    {
        _MainTex ("Panorama", 2D) = "white" {}
        _Exposure ("Exposure", Range(0, 8)) = 1
        _Rotation ("Rotation", Range(0, 360)) = 0
        _VerticalOffset ("Vertical Offset (positive raises image)", Range(-0.5, 0.5)) = 0
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
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Exposure;
            float _Rotation;
            float _VerticalOffset;

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 direction = normalize(i.direction);
                // Longitude repeats once around the viewer; latitude spans pole to pole.
                float2 uv = float2(
                    0.5 - atan2(direction.z, direction.x) / (2.0 * UNITY_PI) + _Rotation / 360.0,
                    1.0 - acos(clamp(direction.y, -1.0, 1.0)) / UNITY_PI);
                uv.y = saturate(uv.y - _VerticalOffset);
                return half4(tex2D(_MainTex, uv).rgb * _Exposure, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
