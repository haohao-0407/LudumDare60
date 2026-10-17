Shader "MarsBase/PanoramicHaze"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("360 panorama", 2D) = "grey" {}
        _Tint ("Tint", Color) = (.5,.5,.5,1)
        _Exposure ("Exposure", Range(0,4)) = 1
        _Rotation ("Rotation", Range(0,360)) = 0
        _HazeColor ("Horizon haze (linear)", Vector) = (.4,.16,.07,1)
        _HazeHeight ("Horizon blend height", Range(.005,.2)) = .055
        _SeamWidth ("Seam feather", Range(0,.04)) = .012
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
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Tint, _HazeColor;
            float _Exposure, _Rotation, _HazeHeight, _SeamWidth;
            struct appdata { float4 vertex:POSITION; };
            struct v2f { float4 position:SV_POSITION; float3 direction:TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                float a = _Rotation * UNITY_PI / 180;
                float sine, cosine; sincos(a, sine, cosine);
                float3 p = v.vertex.xyz;
                float3 rotated = float3(cosine*p.x-sine*p.z,p.y,sine*p.x+cosine*p.z);
                o.position = UnityObjectToClipPos(float4(rotated,1));
                o.direction = p;
                return o;
            }
            half4 frag(v2f i):SV_Target
            {
                float3 d=normalize(i.direction);
                float2 uv=float2(.5-atan2(d.z,d.x)/(2*UNITY_PI),1-acos(clamp(d.y,-1,1))/UNITY_PI);
                // Unwrap screen derivatives at atan2's longitude discontinuity.
                // Otherwise mip selection creates a fine line even when edge colors match.
                float2 dx=ddx(uv), dy=ddy(uv);
                dx.x-=round(dx.x);
                dy.x-=round(dy.x);
                float3 color=tex2Dgrad(_MainTex,uv,dx,dy).rgb;
                // Join both texture edges in a narrow strip; the source pixels remain untouched.
                float edge=min(uv.x,1-uv.x);
                float blend=.5*(1-smoothstep(0,max(_SeamWidth,.00001),edge));
                color=lerp(color,tex2Dgrad(_MainTex,float2(1-uv.x,uv.y),float2(-dx.x,dx.y),float2(-dy.x,dy.y)).rgb,blend);
                // A small polar cap avoids pinching nonuniform generated cloud detail at the pole.
                float capV=d.y>0?.985:.015;
                float3 cap=(tex2D(_MainTex,float2(.125,capV)).rgb+tex2D(_MainTex,float2(.375,capV)).rgb+
                            tex2D(_MainTex,float2(.625,capV)).rgb+tex2D(_MainTex,float2(.875,capV)).rgb)*.25;
                color=lerp(color,cap,smoothstep(.98,1,abs(d.y)));
                color*=(_Tint.rgb*unity_ColorSpaceDouble.rgb)*_Exposure;
                color=lerp(_HazeColor.rgb,color,smoothstep(0,_HazeHeight,d.y));
                return half4(color,1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
