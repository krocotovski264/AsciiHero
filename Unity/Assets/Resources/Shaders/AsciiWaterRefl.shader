// The water's mirror: drawn on the water surface in the glyph layer. It shows the characters the mirrored cameras
// drew (AsciiRenderer.reflections: the scene reflected in the water plane, at 0.8 of the picture's resolution),
// shaken a little by the waves and the rings of the steps, darker than what it mirrors. Something standing in the
// water in front of it (the hero's legs) hides it.
Shader "AsciiHero/WaterRefl"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "WaterRefl"
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite Off ZTest Always
            BlendOp Max
            Blend One One
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_WaterReflTex); SAMPLER(sampler_WaterReflTex);
            TEXTURE2D(_AsciiLitTex); SAMPLER(ascii_point_clamp_sampler);
            float _WaterReflOn, _AsciiReflPass, _AsciiTime; float4 _WaterRip[8];
            float WHash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float WNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(WHash(i), WHash(i + float2(1, 0)), u.x), lerp(WHash(i + float2(0, 1)), WHash(i + float2(1, 1)), u.x), u.y);
            }
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 pw : TEXCOORD0; float vz : TEXCOORD1; };
            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                o.pw = p; o.vz = -TransformWorldToView(p).z;
                o.positionCS = (_WaterReflOn > 0.5 && _AsciiReflPass < 0.5) ? TransformWorldToHClip(p) : float4(2, 2, 2, 1);
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                // in front of the water? (the scene depth is in the lit colour's alpha)
                float4 sl = SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, ascii_point_clamp_sampler, suv, 0);
                float sz = abs(sl.a) * 12.0;
                if (sz > 1e-3 && sz < i.vz - 0.06) discard;
                // the waves and the rings shake the mirror a little
                float t = _AsciiTime; float2 off = (float2(WNoise(i.pw.xz * 3.1 + t * 0.4), WNoise(i.pw.xz * 3.4 - t * 0.33 + 7.0)) - 0.5) * 0.006; float rg = 0;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float4 r = _WaterRip[k]; float age = t - r.z;
                    if (r.w <= 0.0 || age < 0.0 || age > 3.5) continue;
                    float2 dv = i.pw.xz - r.xy; float d = length(dv), rad = 0.1 + 0.55 * age * (1.0 - 0.07 * age);
                    float x = (d - rad) / (0.045 + 0.02 * age), e = exp(-x * x) * r.w * exp(-0.85 * age);
                    rg += e;
                }
                // the rings do not shift the mirror (that smeared the coloured reflection along them - rainbow rings): the
                // mirror fades where a ring passes, so the ring shows white over the water
                float3 c = SAMPLE_TEXTURE2D(_WaterReflTex, sampler_WaterReflTex, suv + off).rgb;
                return half4(c * 0.62 * (1.0 - saturate(rg * 2.5)), 1);
            }
            ENDHLSL
        }
    }
}
