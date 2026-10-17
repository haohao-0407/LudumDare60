// A small vertex-color shader with no render-pipeline package dependencies.
// Supported paths: Universal Render Pipeline and the Built-in Render Pipeline.
// This intentionally uses the legacy draw path rather than the SRP Batcher.
Shader "AssemblyDemo/WhiteboxVertexColor"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    struct Attributes
    {
        float4 positionOS : POSITION;
        half4 color : COLOR;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        half4 color : COLOR;
    };

    half4 _BaseColor;

    Varyings vert(Attributes input)
    {
        Varyings output;
        output.positionCS = UnityObjectToClipPos(input.positionOS);
        output.color = input.color * _BaseColor;
        return output;
    }

    half4 frag(Varyings input) : SV_Target
    {
        return input.color;
    }
    ENDCG

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "WhiteboxUniversal"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = ""
        }

        Pass
        {
            Name "WhiteboxBuiltIn"
            Tags { "LightMode" = "Always" }
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }

    Fallback Off
}

