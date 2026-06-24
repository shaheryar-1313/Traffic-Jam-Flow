Shader "Custom/CartoonShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 0.5, 0.2, 1)
        _ShadowColor ("Shadow Color", Color) = (0.3, 0.1, 0.5, 1)
        _RimColor ("Rim Color", Color) = (1, 1, 0.3, 1)
        _RimPower ("Rim Power", Range(1, 8)) = 3.0
        _OutlineColor ("Outline Color", Color) = (0.05, 0.02, 0.1, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.5)) = 0.045
        _Bands ("Light Bands", Range(2, 5)) = 3
        _Saturation ("Saturation Boost", Range(1, 3)) = 3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        // ── OUTLINE PASS (back-face hull) ──────────────────────
        Pass
        {
            Name "Outline"
            Cull Front

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            struct Attributes { float4 posOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 posCS : SV_POSITION; };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT;
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float3 posWS    = TransformObjectToWorld(IN.posOS.xyz);
                posWS          += normalWS * _OutlineWidth;
                OUT.posCS       = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 OutlineFrag(Varyings IN) : SV_Target
            {
                return half4(_OutlineColor.rgb, 1);
            }
            ENDHLSL
        }

        // ── MAIN CARTOON PASS ──────────────────────────────────
        Pass
        {
            Name "CartoonLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BaseColor, _ShadowColor, _RimColor;
                float  _RimPower, _Bands, _Saturation;
            CBUFFER_END

            struct Attributes
            {
                float4 posOS   : POSITION;
                float3 normalOS : NORMAL;
                float2 uv      : TEXCOORD0;
            };

            struct Varyings
            {
                float4 posCS   : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 posWS   : TEXCOORD1;
                float2 uv      : TEXCOORD2;
            };

            // Boost color saturation
            float3 Saturate(float3 col, float amount)
            {
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                return lerp(float3(luma, luma, luma), col, amount);
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.posCS    = TransformObjectToHClip(IN.posOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.posWS    = TransformObjectToWorld(IN.posOS.xyz);
                OUT.uv       = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // ── Lighting ──
                Light mainLight = GetMainLight();
                float3 N = normalize(IN.normalWS);
                float3 L = normalize(mainLight.direction);
                float3 V = normalize(GetCameraPositionWS() - IN.posWS);

                float NdotL = dot(N, L) * 0.5 + 0.5;   // half-lambert

                // Quantize into N bands (cel shading)
                float bands  = floor(_Bands);
                float celLight = floor(NdotL * bands) / bands;

                // ── Rim ──
                float rim     = 1.0 - saturate(dot(N, V));
                rim           = pow(rim, _RimPower);

                // ── Base color from texture × tint ──
                float4 texCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float3 baseCol = texCol.rgb * _BaseColor.rgb;

                // ── Combine: shadow → base → rim ──
                float3 col = lerp(_ShadowColor.rgb, baseCol, celLight);
                col        = lerp(col, col + _RimColor.rgb, rim * 0.6);

                // ── Saturation boost for that cartoon pop ──
                col = Saturate(col, _Saturation);

                // ── Lighten shadows so they stay colorful not muddy ──
                col = max(col, baseCol * 0.25);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}