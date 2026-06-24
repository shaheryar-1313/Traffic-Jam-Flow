// ============================================================
//  ToyCarShader.shader
//  Replicates the glossy, vibrant plastic-toy car look from
//  mobile sorting-puzzle games.
//
//  Render Pipeline : Built-in RP
//  Usage           : Drop into Assets/Shaders/ and attach
//                    ToyCarShaderController.cs to your car.
// ============================================================

Shader "Custom/ToyCarShader"
{
    Properties
    {
        // ── Base Colour ──────────────────────────────────────
        [Header(Base Colour)]
        _MainColor      ("Main Colour",   Color) = (0.93, 0.10, 0.10, 1.0)
        _ShadowColor    ("Shadow Colour", Color) = (0.25, 0.00, 0.00, 1.0)

        // ── Specular ─────────────────────────────────────────
        [Header(Specular Plastic Gloss)]
        _HighlightColor     ("Highlight Colour",    Color)             = (1, 1, 1, 1)
        _Glossiness         ("Glossiness",          Range(32, 512))    = 220
        _SpecularStrength   ("Specular Strength",   Range(0, 1))       = 0.95
        _SpecularThreshold  ("Specular Threshold",  Range(0, 0.9))     = 0.35
        _SpecularSmoothness ("Specular Smoothness", Range(0.001, 0.2)) = 0.04

        // ── Rim Light ────────────────────────────────────────
        [Header(Rim Light)]
        _RimColor    ("Rim Colour",   Color)         = (0.80, 0.90, 1.00, 1)
        _RimPower    ("Rim Power",    Range(0.5, 8)) = 3.0
        _RimStrength ("Rim Strength", Range(0, 1.5)) = 0.45

        // ── Fresnel ──────────────────────────────────────────
        [Header(Fresnel)]
        _FresnelPower    ("Fresnel Power",    Range(0.1, 5)) = 1.8
        _FresnelStrength ("Fresnel Strength", Range(0, 0.5)) = 0.20

        // ── Diffuse ──────────────────────────────────────────
        [Header(Diffuse)]
        _DiffuseSteps    ("Diffuse Steps",    Range(1, 8))    = 3
        _SaturationBoost ("Saturation Boost", Range(0.5, 2.5)) = 1.50
        _AmbientStrength ("Ambient Strength", Range(0, 0.5))  = 0.18

        // ── Texture ──────────────────────────────────────────
        [Header(Texture)]
        _MainTex ("Albedo Optional", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 300

        // ════════════════════════════════════════════════════
        //  PASS 1  Forward Base  (main lit pass)
        // ════════════════════════════════════════════════════
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0
            #pragma multi_compile_fwdbase

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            // ── Uniforms ─────────────────────────────────────
            fixed4    _MainColor;
            fixed4    _ShadowColor;
            fixed4    _HighlightColor;
            fixed4    _RimColor;

            float     _Glossiness;
            float     _SpecularStrength;
            float     _SpecularThreshold;
            float     _SpecularSmoothness;

            float     _RimPower;
            float     _RimStrength;

            float     _FresnelPower;
            float     _FresnelStrength;

            float     _DiffuseSteps;
            float     _SaturationBoost;
            float     _AmbientStrength;

            sampler2D _MainTex;
            float4    _MainTex_ST;

            // ── Structs ──────────────────────────────────────
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos         : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                SHADOW_COORDS(3)
            };

            // ── Helpers ──────────────────────────────────────

            // Perceptual saturation boost/reduce (Rec.709 weights)
            float3 AdjustSaturation(float3 col, float sat)
            {
                float lum = dot(col, float3(0.2126, 0.7152, 0.0722));
                return lerp(float3(lum, lum, lum), col, sat);
            }

            // Soft N-band toon diffuse
            float ToonDiffuse(float ndotL01, float steps)
            {
                float stepped = floor(ndotL01 * steps + 0.5) / steps;
                return saturate(stepped);
            }

            // ── Vertex ───────────────────────────────────────
            v2f vert(appdata v)
            {
                v2f o;
                o.pos         = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos    = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv          = TRANSFORM_TEX(v.uv, _MainTex);
                TRANSFER_SHADOW(o);
                return o;
            }

            // ── Fragment ─────────────────────────────────────
            fixed4 frag(v2f i) : SV_Target
            {
                // Vectors
                float3 N = normalize(i.worldNormal);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 H = normalize(L + V);

                // Base colour
                float3 texRGB  = tex2D(_MainTex, i.uv).rgb;
                float3 baseCol = AdjustSaturation(_MainColor.rgb   * texRGB, _SaturationBoost);
                float3 shadCol = AdjustSaturation(_ShadowColor.rgb * texRGB, _SaturationBoost);

                // Diffuse: half-Lambert -> toon bands
                float halfLamb = dot(N, L) * 0.5 + 0.5;
                float toonD    = ToonDiffuse(halfLamb, _DiffuseSteps);

                UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                toonD *= atten;

                float3 diffuse = lerp(shadCol, baseCol * _LightColor0.rgb, toonD);

                // Specular: crisp plastic blob
                float NdotH    = saturate(dot(N, H));
                float rawSpec   = pow(NdotH, _Glossiness);
                float specMask  = smoothstep(
                    _SpecularThreshold - _SpecularSmoothness,
                    _SpecularThreshold + _SpecularSmoothness,
                    rawSpec);
                float3 specular = specMask * _SpecularStrength * _HighlightColor.rgb * atten;

                // Rim light
                float NdotV = saturate(dot(N, V));
                float rim   = pow(1.0 - NdotV, _RimPower);
                float3 rimL = rim * _RimStrength * _RimColor.rgb;

                // Fresnel glow
                float fresnel   = pow(1.0 - NdotV, _FresnelPower);
                float3 fresnelG = fresnel * _FresnelStrength * _HighlightColor.rgb;

                // Ambient
                float3 ambient = UNITY_LIGHTMODEL_AMBIENT.rgb * baseCol * _AmbientStrength;

                float3 finalCol = ambient + diffuse + specular + rimL + fresnelG;
                return fixed4(finalCol, 1.0);
            }
            ENDCG
        }

        // ════════════════════════════════════════════════════
        //  PASS 2  Shadow Caster
        // ════════════════════════════════════════════════════
        Pass
        {
            Name "SHADOW_CASTER"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest  LEqual

            CGPROGRAM
            #pragma vertex   vert_sc
            #pragma fragment frag_sc
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct v2f_sc { V2F_SHADOW_CASTER; };

            v2f_sc vert_sc(appdata_base v)
            {
                v2f_sc o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o);
                return o;
            }

            fixed4 frag_sc(v2f_sc i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }

    FallBack "Mobile/Diffuse"
}
