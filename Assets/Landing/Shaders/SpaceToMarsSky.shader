Shader "Custom/Landing/SpaceToMarsSky"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("360 Mars panorama", 2D) = "grey" {}
        _Tint ("Panorama tint", Color) = (.5,.5,.5,1)
        _Exposure ("Exposure", Range(0,4)) = 1
        _Rotation ("Panorama rotation", Range(0,360)) = 0
        _HazeColor ("Horizon haze (linear)", Vector) = (.4,.16,.07,1)
        _HazeHeight ("Horizon blend height", Range(.005,.2)) = .055
        _SeamWidth ("Seam feather", Range(0,.04)) = .012
        _AtmosphereBlend ("Atmosphere blend", Range(0,1)) = 0
        _StarVisibility ("Star visibility", Range(0,1)) = 1
        _PlanetVisibility ("Planet visibility", Range(0,1)) = 1
        _PlanetAngularRadius ("Planet angular radius (radians)", Range(.005,1.5)) = .35
        _PlanetDirection ("Planet center (world direction)", Vector) = (0,-.15,1,0)
        _SunDirection ("Direction toward sun (world)", Vector) = (.45,.65,-.6,0)
        _EntryGlow ("Atmospheric entry glow", Range(0,1)) = 0
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
            float4 _Tint, _HazeColor, _PlanetDirection, _SunDirection;
            float _Exposure, _Rotation, _HazeHeight, _SeamWidth;
            float _AtmosphereBlend, _StarVisibility, _PlanetVisibility, _PlanetAngularRadius, _EntryGlow;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 position : SV_POSITION;
                float3 panoramaDirection : TEXCOORD0;
                float3 worldDirection : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float sine, cosine;
                sincos(_Rotation * UNITY_PI / 180, sine, cosine);
                float3 p = v.vertex.xyz;
                float3 rotated = float3(cosine*p.x-sine*p.z,p.y,sine*p.x+cosine*p.z);
                o.position = UnityObjectToClipPos(float4(rotated,1));
                o.panoramaDirection = p;
                // Skybox vertices encode directions, so camera translation never moves the stars.
                // Decouple the world-space planet and stars from the panorama texture rotation.
                o.worldDirection = rotated;
                return o;
            }

            float Hash31(float3 p)
            {
                p = frac(p * .1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Noise3(float3 p)
            {
                float3 cell = floor(p);
                float3 f = frac(p);
                f = f*f*(3-2*f);
                return lerp(
                    lerp(lerp(Hash31(cell),Hash31(cell+float3(1,0,0)),f.x),
                         lerp(Hash31(cell+float3(0,1,0)),Hash31(cell+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(Hash31(cell+float3(0,0,1)),Hash31(cell+float3(1,0,1)),f.x),
                         lerp(Hash31(cell+float3(0,1,1)),Hash31(cell+float3(1,1,1)),f.x),f.y),f.z);
            }

            float3 Panorama(float3 d)
            {
                // Keep the established MarsPanoramicHaze sampling and final colors intact.
                float2 uv = float2(.5-atan2(d.z,d.x)/(2*UNITY_PI),1-acos(clamp(d.y,-1,1))/UNITY_PI);
                float2 dx = ddx(uv), dy = ddy(uv);
                dx.x -= round(dx.x);
                dy.x -= round(dy.x);
                float3 color = tex2Dgrad(_MainTex,uv,dx,dy).rgb;
                float edge = min(uv.x,1-uv.x);
                float blend = .5*(1-smoothstep(0,max(_SeamWidth,.00001),edge));
                color = lerp(color,tex2Dgrad(_MainTex,float2(1-uv.x,uv.y),float2(-dx.x,dx.y),float2(-dy.x,dy.y)).rgb,blend);
                float capV = d.y>0 ? .985 : .015;
                float3 cap = (tex2D(_MainTex,float2(.125,capV)).rgb+tex2D(_MainTex,float2(.375,capV)).rgb+
                              tex2D(_MainTex,float2(.625,capV)).rgb+tex2D(_MainTex,float2(.875,capV)).rgb)*.25;
                color = lerp(color,cap,smoothstep(.98,1,abs(d.y)));
                color *= (_Tint.rgb*unity_ColorSpaceDouble.rgb)*_Exposure;
                return lerp(_HazeColor.rgb,color,smoothstep(0,max(_HazeHeight,.00001),d.y));
            }

            float3 Stars(float3 d)
            {
                // Cube-face coordinates avoid the polar stretching of a latitude/longitude star field.
                float3 a = abs(d);
                float2 uv;
                float face;
                if (a.x >= a.y && a.x >= a.z)
                {
                    uv = d.yz / a.x;
                    face = d.x >= 0 ? 0 : 1;
                }
                else if (a.y >= a.z)
                {
                    uv = d.xz / a.y;
                    face = d.y >= 0 ? 2 : 3;
                }
                else
                {
                    uv = d.xy / a.z;
                    face = d.z >= 0 ? 4 : 5;
                }
                float2 grid = (uv*.5+.5)*260;
                float3 cell = float3(floor(grid),face*97);
                float seed = Hash31(cell);
                float2 center = .2 + .6*float2(Hash31(cell+13.7),Hash31(cell+71.1));
                float distanceToStar = length(frac(grid)-center);
                float radius = lerp(.045,.13,Hash31(cell+41.5));
                float aa = max(fwidth(distanceToStar),.018);
                float starDisc = 1-smoothstep(max(0,radius-aa),radius+aa,distanceToStar);
                // Prevent tiny star discs from becoming disproportionately bright at low resolution.
                starDisc *= saturate(radius/aa);
                float brightness = step(.9968,seed)*starDisc*lerp(.45,2.2,Hash31(cell+6.2));
                float3 tint = lerp(float3(.66,.8,1),float3(1,.83,.63),Hash31(cell+9.3));
                return tint*brightness;
            }

            float3 Space(float3 d)
            {
                float3 color = float3(.0005,.001,.0025) + Stars(d)*saturate(_StarVisibility);
                float3 centerDirection = normalize(_PlanetDirection.xyz + float3(0,0,.000001));
                float radius = clamp(_PlanetAngularRadius,.005,1.5);
                float angle = acos(clamp(dot(d,centerDirection),-1,1));
                float angularAA = max(fwidth(angle),.00015);
                float disc = 1-smoothstep(radius-angularAA,radius+angularAA,angle);

                float3 axis = abs(centerDirection.y)>.95 ? float3(1,0,0) : float3(0,1,0);
                float3 right = normalize(cross(axis,centerDirection));
                float3 up = cross(centerDirection,right);
                float2 projected = float2(dot(d,right),dot(d,up))/sin(radius);
                float3 normal = projected.x*right + projected.y*up - sqrt(saturate(1-dot(projected,projected)))*centerDirection;
                normal = normalize(normal + float3(.000001,0,0));
                float large = Noise3(normal*5.5+4.1);
                float medium = Noise3(normal*23+17.3);
                float detail = Noise3(normal*85+32.9);
                float terrain = saturate(large*.7+medium*.22+detail*.08);
                float3 albedo = lerp(float3(.12,.045,.022),float3(.62,.29,.12),smoothstep(.2,.8,terrain));
                albedo = lerp(albedo,float3(.75,.58,.38),smoothstep(.88,1,abs(normal.y))*.45);
                float sunlight = saturate(dot(normal,normalize(_SunDirection.xyz+float3(0,.000001,0))));
                float3 planet = albedo*(.035+sunlight*1.4)*max(_Exposure,.001);
                float limb = pow(saturate(length(projected)),8)*disc;
                planet += float3(.28,.07,.018)*limb*(.15+.85*sunlight);
                color = lerp(color,planet,disc*saturate(_PlanetVisibility));
                float outerLimb = exp(-max(0,angle-radius)/.009)*(1-disc);
                color += float3(.16,.045,.012)*outerLimb*saturate(_PlanetVisibility);
                return color;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 panoramaDirection = normalize(i.panoramaDirection);
                float3 d = normalize(i.worldDirection);
                float3 surface = Panorama(panoramaDirection);
                float t = saturate(_AtmosphereBlend);
                // Warm atmosphere reaches the horizon before it reaches the zenith.
                float horizonFirst = smoothstep(0,1,saturate(t/(.38+.62*saturate(d.y))));
                float3 color = lerp(Space(d),surface,horizonFirst);
                float horizonGlow = exp(-abs(d.y-.04)*3);
                color += float3(1,.21,.035)*saturate(_EntryGlow)*(1-t)*horizonGlow*.65;
                // At t=1 the result is exactly the original Mars panorama, including horizon haze.
                return half4(color,1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
