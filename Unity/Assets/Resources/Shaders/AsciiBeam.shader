// A shaft of light (the cheap "fake volumetric" cone used in mobile games): an open cone from the break in the
// rock (v = 1) to the floor (v = 0), added to the lit colour only. Its edges fade where the view grazes the cone,
// slow streaks of dust drift down it, it fades in a little below the source and out on the floor. The characters
// of whatever is behind the beam take their brightness from the lit colour, so the shaft shows through them; the
// dust motes and the line streaks of AsciiHero/Motes draw it in the air. The alpha (the depth the characters test)
// is left alone, and the depth camera never draws it.
Shader "AsciiHero/Beam"
{
    Properties
    {
        _BeamColor ("colour * strength", Color) = (1, 0.95, 0.8, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Beam"
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite Off ZTest LEqual
            Blend One One
            ColorMask RGB
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BeamColor;
            CBUFFER_END
            float _AsciiNDPass, _AsciiTime;
            float BHash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float BNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(BHash(i), BHash(i + float2(1, 0)), u.x), lerp(BHash(i + float2(0, 1)), BHash(i + float2(1, 1)), u.x), u.y);
            }
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 nW : TEXCOORD0; float3 pW : TEXCOORD1; float2 uv : TEXCOORD2; };
            Varyings vert (Attributes i)
            {
                Varyings o; float3 p = TransformObjectToWorld(i.positionOS.xyz);
                o.pW = p; o.nW = TransformObjectToWorldNormal(i.normalOS); o.uv = i.uv;
                o.positionCS = _AsciiNDPass > 0.5 ? float4(2, 2, 2, 1) : TransformWorldToHClip(p);
                return o;
            }
            half4 frag (Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.pW), N = normalize(i.nW); if (!front) N = -N;
                float edge = pow(saturate(abs(dot(N, V))), 1.6);                       // thick where we look through the middle
                float v = i.uv.y, t = _AsciiTime;
                float along = smoothstep(0.0, 0.12, v) * smoothstep(1.0, 0.82, v) * (0.45 + 0.55 * v);
                float streak = 0.55 + 0.6 * BNoise(float2(i.uv.x * 14.0, v * 2.0 + t * 0.06)) * BNoise(float2(i.uv.x * 5.0 + 3.1, v * 0.7 - t * 0.03));
                return half4(_BeamColor.rgb * edge * along * streak, 0);
            }
            ENDHLSL
        }
    }
}
