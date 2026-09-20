Shader "Bytesized/GrassURP"
{
    Properties
    {
        [Header(Shading)]
        _TopColor("Top Color", Color) = (0.57, 0.84, 0.32, 1.0)
        _BottomColor("Bottom Color", Color) = (0.0625, 0.375, 0.07, 1.0)
        _TranslucentGain("Translucent Gain", Range(0,1)) = 0.5

        [Header(Wind)]
        _WindStrength("Wind Strength", Range(0.0001, 1)) = 0.3

        [Header(Spacing)]
        _ViewLOD("View Radius", Float) = 48
        _MaxStages("Max Stages", Range(2, 64)) = 7
        _BaseStages("Base Stages", Range(-64, 64)) = -0.5

        [Header(Grass Blades)]
        _BladeWidth("Blade Width", Range(0, 0.4)) = 0.05
        _BladeWidthRandom("Blade Width Random", Range(0, 0.4)) = 0.02
        _BladeHeight("Blade Height", Float) = 0.5
        _BladeHeightRandom("Blade Height Random", Float) = 0.3
        _BladeForward("Blade Stiffness Amount", Range(0, 1)) = 0.38
        _BladeCurve("Blade Curvature Amount", Range(1, 4)) = 2
        _BendRotationRandom("Bend Rotation Random", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.6
            #pragma require geometry
            #pragma require tessellation
            #pragma vertex vert
            #pragma hull hull
            #pragma domain domain
            #pragma geometry geo
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "GrassHelpers.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor;
                float4 _BottomColor;
                float _TranslucentGain;
                float _WindStrength;
                float _ViewLOD;
                float _MaxStages;
                float _BaseStages;
                float _BladeWidth;
                float _BladeWidthRandom;
                float _BladeHeight;
                float _BladeHeightRandom;
                float _BladeForward;
                float _BladeCurve;
                float _BendRotationRandom;
            CBUFFER_END

            #include "GrassTessellationURP.hlsl"

            struct GeometryOutput
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : NORMAL;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            GeometryOutput VertexOutput(float3 positionOS, float3 normalOS, float2 uv)
            {
                GeometryOutput o;
                VertexPositionInputs posInputs = GetVertexPositionInputs(positionOS);
                o.positionCS = posInputs.positionCS;
                o.positionWS = posInputs.positionWS;
                o.normalWS = GrassSafeNormalize(
                    mul(normalOS, (float3x3)UNITY_MATRIX_I_M),
                    float3(0, 1, 0));
                o.uv = uv;
                return o;
            }

            GeometryOutput GenerateGrassVertex(
                float3 vertexPosition,
                float width,
                float height,
                float forward,
                float2 uv,
                float3x3 transformMatrix)
            {
                float3 worldPos = TransformObjectToWorld(vertexPosition);
                float distanceFromCamera = 1.0 +
                    max(0.0, min(1.0, distance(worldPos, _WorldSpaceCameraPos) / _ViewLOD)) * 2.0;

                float3 tangentPoint = float3(width, forward, height);
                float3 tangentNormal = GrassSafeNormalize(float3(0, -1, forward), float3(0, -1, 0));
                float3 localPosition = vertexPosition + mul(transformMatrix, tangentPoint) * distanceFromCamera;
                float3 localNormal = GrassSafeNormalize(mul(transformMatrix, tangentNormal), float3(0, 1, 0));
                return VertexOutput(localPosition, localNormal, uv);
            }

            void EmitBlade(
                float3 pos,
                float3 normalOS,
                float4 tangentOS,
                inout TriangleStream<GeometryOutput> triStream)
            {
                float3x3 facingRotationMatrix = RotationMatrix(rand(pos) * 6.2831853, float3(0, 0, 1));
                float3x3 bendRotationMatrix = RotationMatrix(
                    rand(pos.zzx) * _BendRotationRandom * 3.14159265 * 0.5,
                    float3(-1, 0, 0));

                float2 windValue = float2(
                    cos(_TimeParameters.x + pos.x + pos.z),
                    sin(_TimeParameters.x + pos.x + pos.z)) * _WindStrength * 0.25;
                float windAngle = length(windValue) * 3.14159265;
                float3x3 windRotation = RotationMatrix(
                    windAngle,
                    float3(windValue.x, windValue.y, 0.001));

                float3x3 tangentToLocal = TangentToLocal(normalOS, tangentOS);
                float3x3 transformationMatrix = mul(
                    mul(mul(tangentToLocal, windRotation), facingRotationMatrix),
                    bendRotationMatrix);
                float3x3 transformationMatrixWithoutBending = mul(tangentToLocal, facingRotationMatrix);

                float height = (rand(pos.zyx) * 2.0 - 1.0) * _BladeHeightRandom + _BladeHeight;
                float width = (rand(pos.xzy) * 2.0 - 1.0) * _BladeWidthRandom + _BladeWidth;
                float forward = rand(pos.yyz) * _BladeForward;

                [unroll]
                for (int i = 0; i < 3; i++)
                {
                    float t = i / 3.0;
                    float segmentHeight = height * t;
                    float segmentWidth = width * (1.0 - t);
                    float segmentForward = pow(t, _BladeCurve) * forward;
                    float3x3 transformMatrix = i == 0
                        ? transformationMatrixWithoutBending
                        : transformationMatrix;

                    triStream.Append(GenerateGrassVertex(
                        pos, segmentWidth, segmentHeight, segmentForward, float2(0, t), transformMatrix));
                    triStream.Append(GenerateGrassVertex(
                        pos, -segmentWidth, segmentHeight, segmentForward, float2(1, t), transformMatrix));
                }

                triStream.Append(GenerateGrassVertex(
                    pos, 0, height, forward, float2(0.5, 1), transformationMatrix));
                triStream.RestartStrip();
            }

            // D3D12: tessellator emits triangles — GS must take triangle (not point).
            // One blade at the centroid; tessellation densifies near the camera.
            [maxvertexcount(7)]
            void geo(triangle TessVertexOutput IN[3], inout TriangleStream<GeometryOutput> triStream)
            {
                float3 pos = (IN[0].positionOS.xyz + IN[1].positionOS.xyz + IN[2].positionOS.xyz) * (1.0 / 3.0);
                float3 normalOS = GrassSafeNormalize(
                    IN[0].normalOS + IN[1].normalOS + IN[2].normalOS,
                    float3(0, 1, 0));
                float4 tangentOS = (IN[0].tangentOS + IN[1].tangentOS + IN[2].tangentOS) * (1.0 / 3.0);
                tangentOS.xyz = GrassSafeNormalize(tangentOS.xyz, float3(1, 0, 0));
                EmitBlade(pos, normalOS, tangentOS, triStream);
            }

            half4 frag(GeometryOutput i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                float3 normalWS = GrassSafeNormalize(
                    frontFace ? i.normalWS : -i.normalWS,
                    float3(0, 1, 0));

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 lightDir = GrassSafeNormalize(mainLight.direction, float3(0, 1, 0));

                // Soft wrap lighting — keep translucent gain modest so night moon stays dim.
                float wrap = saturate(dot(normalWS, lightDir) * 0.5 + 0.5);
                float diffuse = lerp(wrap, saturate(dot(normalWS, lightDir)), 0.35);
                diffuse = saturate(diffuse + _TranslucentGain * 0.15);
                diffuse *= mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                float3 ambient = SampleSH(normalWS) * 0.35;
                float3 lighting = diffuse * mainLight.color + ambient;
                float3 albedo = lerp(_BottomColor.rgb, _TopColor.rgb, i.uv.y);
                // Soft clamp — stop blades from reading as emissive neon green at night.
                float3 lit = min(albedo * lighting, albedo * 1.1);
                return half4(lit, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
