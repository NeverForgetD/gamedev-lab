Shader "Custom/DensityGizmo"
{
    Properties
    {
        _Size("Gizmo Size", Float) = 0.1
        _InsideColor("Inside Color", Color) = (0, 1, 0, 0.8)
        _OutsideColor("Outside Color", Color) = (1, 0, 0, 0.3)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma multi_compile_instancing
            #pragma vertex vert
            #pragma fragment frag
            #pragma instancing_options procedural:setup
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct GizmoData {
                float3 position;
                float density;
            };

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float density : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            half _Size;
            half4 _InsideColor;
            half4 _OutsideColor;

#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
            StructuredBuffer<GizmoData> _GizmoBuffer;

            void setup()
            {
                float3 pos = _GizmoBuffer[unity_InstanceID].position;

                float4x4 dataMatrix = float4x4(
                    _Size, 0, 0, pos.x,
                    0, _Size, 0, pos.y,
                    0, 0, _Size, pos.z,
                    0, 0, 0, 1
                );

                unity_ObjectToWorld = dataMatrix;

                float invSize = 1.0f / _Size;
                float4x4 invMatrix = float4x4(
                    invSize, 0, 0, -pos.x * invSize,
                    0, invSize, 0, -pos.y * invSize,
                    0, 0, invSize, -pos.z * invSize,
                    0, 0, 0, 1
                );

                unity_WorldToObject = invMatrix;
            }
#endif

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);

#if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                OUT.density = _GizmoBuffer[unity_InstanceID].density;
#else
                OUT.density = 0;
#endif
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                return IN.density < 0 ? _InsideColor : _OutsideColor;
            }
            ENDHLSL
        }
    }
}
