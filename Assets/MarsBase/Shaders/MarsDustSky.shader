Shader "MarsBase/DustSky"
{
    Properties
    {
        _TopColor ("Dusty zenith", Color) = (0.42,0.23,0.15,1)
        _HorizonColor ("Sunset horizon", Color) = (0.85,0.48,0.26,1)
        _GroundColor ("Ground haze", Color) = (0.25,0.12,0.07,1)
        _SunDirection ("Sun direction", Vector) = (-0.5,0.3,0.8,0)
        [HDR] _SunColor ("Sun radiance", Color) = (5,3.5,2,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };
            float4 _TopColor, _HorizonColor, _GroundColor, _SunDirection, _SunColor;
            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.direction);
                float h = saturate(d.y);
                float3 sky = lerp(_HorizonColor.rgb, _TopColor.rgb, pow(h,0.55));
                sky = lerp(sky, _GroundColor.rgb, saturate(-d.y * 4));
                float sunDot = dot(d,normalize(_SunDirection.xyz));
                float disc = smoothstep(0.99968,0.99982,sunDot);
                float glow = pow(saturate(sunDot),42) * 0.13;
                sky += _SunColor.rgb * (disc + glow);
                return half4(sky,1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
