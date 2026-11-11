// The lighthouse's aiming beam in its final game (LighthouseMouseTrap): a shaft of light from the
// lantern whose foot is exactly the aimed hex, so where it lands is never in doubt. Unlike the
// turning beam's soft round cone (Orca/Lighthouse Beam) it has hard, flat sides.
//
// The mesh is a six-sided pyramid in world space from the lantern through the hex's corners, a
// little past the ground. It is drawn as a volume: only its far side is drawn (back faces), and
// each pixel adds light for the length of the eye's ray inside the pyramid (between its eight
// planes, _HexBeamPlanes: six sides and two caps, normals outward) up to whatever stands in the way
// (the scene's depth), so the shaft stops on the ground and things in front of it hide it. Even
// light all through (_Density per metre), faded where the ray meets the shaft close to the camera
// (_NearFade), so the stretch by the lantern, just over the player's head, doesn't fill the view.
// The scene's fog leaves it alone.
Shader "Orca/Hex Beam"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 0.72, 0.32, 1)
        _Density ("Density (per metre)", Float) = 0.004
        _NearFade ("Fade Near The Camera (m)", Float) = 35
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Beam"
            Blend One One
            ZWrite Off
            Cull Front
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Density;
                float _NearFade;
            CBUFFER_END

            // World-space planes bounding the shaft: xyz the outward normal, w so that
            // dot(normal, p) + w is the signed distance of p (positive outside).
            float4 _HexBeamPlanes[8];

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 origin = _WorldSpaceCameraPos;
                float3 toFar = input.positionWS - origin;
                float exitAt = length(toFar);
                float3 ray = toFar / max(exitAt, 1e-4);
                float entryAt = 0.0;
                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float4 plane = _HexBeamPlanes[i];
                    float facing = dot(plane.xyz, ray);
                    float outside = dot(plane.xyz, origin) + plane.w;
                    if (abs(facing) < 1e-6)
                    {
                        if (outside > 0.0) return half4(0.0h, 0.0h, 0.0h, 0.0h);
                    }
                    else
                    {
                        float at = -outside / facing;
                        if (facing < 0.0) entryAt = max(entryAt, at);
                        else exitAt = min(exitAt, at);
                    }
                }

                // Anything standing in the way cuts the ray short: the ground under the hex ends it.
                if (unity_OrthoParams.w == 0.0)
                {
                    float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                    float eye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                    float3 forward = -UNITY_MATRIX_V[2].xyz;
                    exitAt = min(exitAt, eye / max(dot(ray, forward), 1e-4));
                }
                if (exitAt <= entryAt) return half4(0.0h, 0.0h, 0.0h, 0.0h);

                float through = exitAt - entryAt;
                float near = smoothstep(_NearFade * 0.3, _NearFade, entryAt);
                half3 light = _Color.rgb * (1.0 - exp(-through * _Density)) * near;
                return half4(light, 0.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
