Shader "Terminal/Curved Screen"
{
    Properties { _TerminalTexture ("Terminal Display", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+10" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_TerminalTexture); SAMPLER(sampler_TerminalTexture);
            CBUFFER_START(UnityPerMaterial)
                float4 _ScreenCenter, _ScreenRight, _ScreenUp, _ScreenNormal, _TerminalSurfaceSize;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                positionWS += TransformObjectToWorldNormal(_ScreenNormal.xyz) * _TerminalSurfaceSize.w;
                output.positionCS = TransformWorldToHClip(positionWS);
                float3 local = input.positionOS - _ScreenCenter.xyz;
                output.uv = float2(dot(local, _ScreenRight.xyz) / _TerminalSurfaceSize.x, dot(local, _ScreenUp.xyz) / _TerminalSurfaceSize.y) / _TerminalSurfaceSize.z + 0.5;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                if (any(input.uv < 0) || any(input.uv > 1)) return half4(0, 0, 0, 1);
                return half4(SAMPLE_TEXTURE2D(_TerminalTexture, sampler_TerminalTexture, input.uv).rgb, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
