Shader "Orca/Pixel Water"
{
    Properties
    {
        [MainTexture] _BaseMap ("Water Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _TextureScroll ("Texture Drift (tiles per second, X / Z)", Vector) = (0.01, 0.006, 0, 0)
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
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 waterUV : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                // Metre-based tiling, independent of the imported ocean's scale and UVs.
                o.waterUV = positionWS.xz * _BaseMap_ST.xy + _BaseMap_ST.zw
                          + frac(_Time.y * _TextureScroll.xy);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // The supplied art is the water, with no procedural water mixed over it.
                half3 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.waterUV).rgb * _BaseColor.rgb;
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
