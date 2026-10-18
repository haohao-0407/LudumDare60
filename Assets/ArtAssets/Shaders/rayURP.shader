
Shader "X-Ray/Effects/XRay"
{
    Properties
    {
        [Header(Visible_Lines)]
        [HDR] _VisibleColor ("Visible Color", Color) = (0.25, 1.0, 0.85, 1)
        _VisibleWidth ("Visible Half Width In Pixels", Range(0.25, 12)) = 1.4
        _VisibleOpacity ("Visible Opacity", Range(0, 1)) = 1

        [Header(Hidden_Lines)]
        [HDR] _HiddenColor ("Hidden Color", Color) = (1.0, 0.6, 0.12, 1)
        _HiddenWidth ("Hidden Half Width In Pixels", Range(0.25, 12)) = 1
        _HiddenOpacity ("Hidden Opacity", Range(0, 1)) = 0.35
        [Toggle] _ShowHidden ("Show Hidden Lines", Float) = 1
        _DepthTolerance ("Depth Tolerance In View Units", Range(0, 0.5)) = 0.025
        [Toggle] _UseSceneDepth ("Use Scene Occlusion", Float) = 1
        [HideInInspector] _UseEdgeMask ("Use Generated Edge Mask", Float) = 0

        [Header(Line_Style)]
        _Softness ("Edge Softness In Pixels", Range(0.05, 3)) = 0.7
        _MinorLineScale ("Minor Line Width Scale", Range(0.05, 1)) = 0.45
        [Toggle] _Dashes ("Use Dashes", Float) = 0
        _DashCount ("Dashes Per Triangle Edge", Range(1, 40)) = 8
        _DashDuty ("Dash Fill Ratio", Range(0.05, 1)) = 0.58
        _DashSpeed ("Dash Speed", Range(-10, 10)) = 0
        _PulseStrength ("Pulse Strength", Range(0, 1)) = 0
        _PulseSpeed ("Pulse Cycles Per Second", Range(0, 8)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+50"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "XRayWireframe"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM

            #pragma target 4.0
            #pragma require geometry
            #pragma vertex Vert
            #pragma geometry Geom
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _VisibleColor;
                float4 _HiddenColor;

                float _VisibleWidth;
                float _HiddenWidth;

                float _VisibleOpacity;
                float _HiddenOpacity;

                float _ShowHidden;
                float _DepthTolerance;
                float _UseSceneDepth;
                float _UseEdgeMask;

                float _Softness;
                float _MinorLineScale;
                float _Dashes;
                float _DashCount;
                float _DashDuty;
                float _DashSpeed;

                float _PulseStrength;
                float _PulseSpeed;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 edgeMask : TEXCOORD3;
            };

            struct VertexToGeometry
            {
                float3 positionOS : TEXCOORD0;
                float3 edgeMask : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                noperspective float3 barycentric : TEXCOORD0;
                nointerpolation float3 edgeMask : TEXCOORD1;
            };

            VertexToGeometry Vert(Attributes input)
            {
                VertexToGeometry output;
                output.positionOS = input.positionOS;
                output.edgeMask = input.edgeMask;
                return output;
            }

            // 为每个三角形生成重心坐标
            [maxvertexcount(3)]
            void Geom(
                triangle VertexToGeometry input[3],
                inout TriangleStream<Varyings> stream
            )
            {
                Varyings output;

                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    output.positionCS =
                        TransformObjectToHClip(input[i].positionOS);

                    output.barycentric =
                        (i == 0) ? float3(1, 0, 0) :
                        (i == 1) ? float3(0, 1, 0) :
                                   float3(0, 0, 1);

                    output.edgeMask = _UseEdgeMask > 0.5 ? input[0].edgeMask : float3(1, 1, 1);

                    stream.Append(output);
                }

                stream.RestartStrip();
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 计算每个像素距离三角形边缘的距离
                float3 bary = saturate(input.barycentric);

                float3 dx = ddx(bary);
                float3 dy = ddy(bary);
                float3 pixelDerivative = max(sqrt(dx * dx + dy * dy), float3(1e-5, 1e-5, 1e-5));

                float3 edgeDistanceInPixels =
                    bary / pixelDerivative;

                // edgeMask 现在是逐边强度：0 = 不画，0~1 = 该边的权重（折角越大越粗）。
                // 老模式写入 0/1 时行为与原先完全一致。
                float3 edgeStrength = saturate(input.edgeMask);

                // Invisible triangulation diagonals must not participate in nearest-edge selection.
                edgeDistanceInPixels = lerp(float3(1e6, 1e6, 1e6), edgeDistanceInPixels, step(0.02, edgeStrength));

                float nearestEdge = min(
                    edgeDistanceInPixels.x,
                    min(
                        edgeDistanceInPixels.y,
                        edgeDistanceInPixels.z
                    )
                );

                // 读取 URP 场景深度，判断是否被遮挡
                float2 screenUV =
                    input.positionCS.xy / _ScaledScreenParams.xy;

                float hidden = 0;
                if (_UseSceneDepth > 0.5)
                {
                    float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    float wireEyeDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                    hidden = step(sceneEyeDepth + _DepthTolerance, wireEyeDepth);
                }

                // 区分可见线条与隐藏线条
                float4 lineColor = lerp(
                    _VisibleColor,
                    _HiddenColor,
                    hidden
                );

                float width = lerp(
                    _VisibleWidth,
                    _HiddenWidth,
                    hidden
                );

                float opacity = lerp(
                    _VisibleOpacity,
                    _HiddenOpacity * _ShowHidden,
                    hidden
                );

                // 线条宽度和抗锯齿：逐边按强度调制，强折角更粗、弱结构更细
                float edgeAA = max(_Softness, 0.05);

                float3 perEdgeWidth = width * lerp(_MinorLineScale, 1.0, pow(edgeStrength, 0.7));

                float3 coverage3 = 1.0 - smoothstep(
                    max(0.0, perEdgeWidth - edgeAA),
                    perEdgeWidth + edgeAA,
                    edgeDistanceInPixels
                );

                float edgeCoverage = max(coverage3.x, max(coverage3.y, coverage3.z));

                // 可选虚线效果
                float dash = 1.0;

                if (_Dashes > 0.5)
                {
                    float edgeParameter;

                    if (edgeDistanceInPixels.x <= edgeDistanceInPixels.y && edgeDistanceInPixels.x <= edgeDistanceInPixels.z)
                        edgeParameter =
                            bary.z / max(1e-5, bary.y + bary.z);

                    else if (edgeDistanceInPixels.y <= edgeDistanceInPixels.z)
                        edgeParameter =
                            bary.x / max(1e-5, bary.z + bary.x);

                    else
                        edgeParameter =
                            bary.y / max(1e-5, bary.x + bary.y);

                    float cycle = frac(
                        edgeParameter * _DashCount +
                        _Time.y * _DashSpeed
                    );

                    dash = 1.0 - step(_DashDuty, cycle);
                }

                // 呼吸动画
                float pulse = lerp(
                    1.0,
                    0.5 + 0.5 * sin(
                        _Time.y * _PulseSpeed * 6.28318530718
                    ),
                    _PulseStrength
                );

                float alpha = saturate(
                    lineColor.a *
                    opacity *
                    edgeCoverage *
                    dash *
                    pulse
                );

                clip(alpha - 0.001);

                return half4(lineColor.rgb, alpha);
            }

            ENDHLSL
        }
    }

    Fallback Off
}
