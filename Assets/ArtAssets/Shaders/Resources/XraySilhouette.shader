Shader "Hidden/XRay/Silhouette"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        // All parts write into one mask. Internal overlaps are a solid union, so they add no extra contours.
        Pass
        {
            Name "OriginalSilhouetteMask"
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4x4 _XrayViewProjection;
            struct Attributes { float3 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = mul(_XrayViewProjection, float4(TransformObjectToWorld(input.positionOS), 1));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return 1; }
            ENDHLSL
        }
        Pass
        {
            Name "SilhouetteBorderOnly"
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D_X(_XrayMask);
            float4 _OutlineColor;
            float4 _OutlineStep;
            struct Attributes { uint vertexID : SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }
            half Mask(float2 uv) { return SAMPLE_TEXTURE2D_X(_XrayMask, sampler_LinearClamp, uv).r; }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half center = Mask(input.uv);
                half expanded = center;
                // Circular dilation; the center is subtracted so the model interior remains transparent.
                [unroll] for (int i = 0; i < 16; i++)
                {
                    float angle = i * (6.28318530718 / 16.0);
                    float2 offset = float2(cos(angle), sin(angle)) * _OutlineStep.xy;
                    expanded = max(expanded, Mask(input.uv + offset));
                }
                half coverage = saturate(expanded - center);
                clip(coverage - 0.001);
                return half4(_OutlineColor.rgb, _OutlineColor.a * coverage);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
