Shader "Custom/TerrainColor"
{
    Properties
    {
        _MinHeight   ("Min Height",   Float)  = 0.0
        _MaxHeight   ("Max Height",   Float)  = 15.0
        _BlendRange  ("Blend Range",  Float)  = 0.08

        _BeachColor  ("Beach Color",  Color)  = (0.76, 0.70, 0.50, 1)
        _GrassColor  ("Grass Color",  Color)  = (0.30, 0.55, 0.20, 1)
        _RockColor   ("Rock Color",   Color)  = (0.45, 0.40, 0.35, 1)
        _SnowColor   ("Snow Color",   Color)  = (0.90, 0.93, 0.95, 1)

        // 각 경계의 정규화 높이 (0~1)
        _BeachEnd    ("Beach End",    Range(0,1)) = 0.2
        _GrassEnd    ("Grass End",    Range(0,1)) = 0.5
        _RockEnd     ("Rock End",     Range(0,1)) = 0.75
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float  _MinHeight;
                float  _MaxHeight;
                float  _BlendRange;
                half4  _BeachColor;
                half4  _GrassColor;
                half4  _RockColor;
                half4  _SnowColor;
                float  _BeachEnd;
                float  _GrassEnd;
                float  _RockEnd;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            // 두 색상 사이를 경계 근처에서만 부드럽게 블렌딩
            half3 HeightBlend(half3 colA, half3 colB, float t, float edge, float range)
            {
                float blend = smoothstep(edge - range, edge + range, t);
                return lerp(colA, colB, blend);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 월드 Y를 0~1로 정규화
                float t = saturate((IN.positionWS.y - _MinHeight) / (_MaxHeight - _MinHeight));

                // 구간별 색상 블렌딩
                half3 col = _BeachColor.rgb;
                col = HeightBlend(col, _GrassColor.rgb, t, _BeachEnd, _BlendRange);
                col = HeightBlend(col, _RockColor.rgb,  t, _GrassEnd, _BlendRange);
                col = HeightBlend(col, _SnowColor.rgb,  t, _RockEnd,  _BlendRange);

                // 간단한 Lambert 라이팅
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(normalize(IN.normalWS), mainLight.direction));
                float3 lighting = mainLight.color * (NdotL * 0.8 + 0.2); // 0.2 ambient

                return half4(col * lighting, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionHCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _MainLightPosition.xyz));
                OUT.positionHCS   = positionCS;
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
