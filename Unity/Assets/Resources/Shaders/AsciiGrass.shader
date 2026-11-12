// Grass: one character per blade. | upright, / or \ when it leans across the screen, _ when trodden flat.
// Blades sway in the wind and are pushed aside by the hero (_AsciiHeroA) and by a point trailing behind him
// (_AsciiHeroB), so the grass parts in front of the feet and closes again a moment later.
Shader "AsciiHero/Grass"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Grass"
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite Off ZTest Always
            BlendOp Max
            Blend One One
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_AsciiAtlas); SAMPLER(sampler_AsciiAtlas);
            TEXTURE2D(_AsciiLitTex); SAMPLER(sampler_AsciiLitTex);
            TEXTURE2D(_AsciiNDTex); SAMPLER(sampler_AsciiNDTex);
            float _AsciiRows, _AsciiExposure, _AsciiLinear, _AsciiDepthRef, _AsciiTime;
            float4 _AsciiHeroA, _AsciiHeroB;

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float2 seed : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float3 col : TEXCOORD1; float gi : TEXCOORD2; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 a = TransformObjectToWorld(i.positionOS.xyz);
                float s = i.seed.x;
                float h = 0.11 + 0.15 * s;
                float2 wind = float2(sin(_AsciiTime * 1.1 + a.x * 0.9 + a.z * 0.6), cos(_AsciiTime * 0.8 + a.z * 1.1 + s * 6.0)) * 0.13;
                float2 dA = a.xz - _AsciiHeroA.xz, dB = a.xz - _AsciiHeroB.xz;
                float pa = smoothstep(0.75, 0.12, length(dA)), pb = smoothstep(0.6, 0.1, length(dB)) * 0.75;
                float2 lean = wind + normalize(dA + 1e-4) * pa * 0.95 + normalize(dB + 1e-4) * pb * 0.6;
                float ll = length(lean); if (ll > 0.96) { lean *= 0.96 / ll; ll = 0.96; }
                float push = max(pa, pb);
                float3 tip = a + float3(lean.x, sqrt(1.0 - ll * ll), lean.y) * h;
                float3 pVS = TransformWorldToView((a + tip) * 0.5);
                float4 bclip = TransformWorldToHClip(a);
                float4 sp = ComputeScreenPos(bclip);
                float2 suv = sp.xy / sp.w;
                float4 nd = SAMPLE_TEXTURE2D_LOD(_AsciiNDTex, sampler_AsciiNDTex, suv, 0);
                float sceneZ = nd.b > 1e-4 ? nd.b * 12.0 : 1e5;
                float myZ = -pVS.z;
                bool vis = myZ <= sceneZ + 0.3 && all(suv >= 0.0) && all(suv <= 1.0);
                float lv = TransformWorldToViewDir(float3(lean.x, 0, lean.y), false).x;
                o.gi = push > 0.8 ? 4.0 : (lv > 0.2 ? 2.0 : (lv < -0.2 ? 3.0 : 0.0));
                float3 lit = SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, sampler_AsciiLitTex, suv, 0).rgb;
                lit = _AsciiLinear > 0.5 ? pow(max(lit, 0.0), 1.0 / 2.2) : max(lit, 0.0);
                float lum = saturate(dot(lit, float3(0.299, 0.587, 0.114)) * 2.6 * _AsciiExposure);
                float depthK = clamp(1.0 - (myZ - _AsciiDepthRef) * 0.09, 0.5, 1.2);
                float3 green = lerp(float3(0.16, 0.50, 0.24), float3(0.50, 0.78, 0.36), frac(s * 13.7));
                o.col = (green * (0.22 + 0.95 * lum) + lit * 0.5) * depthK * (1.0 - 0.35 * push);
                o.local = i.corner + 0.5;
                pVS.xy += i.corner * float2(h * 0.66, h * 1.1);
                o.positionCS = vis ? TransformWViewToHClip(pVS) : float4(2, 2, 2, 1);
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                float2 l = clamp(i.local, 0.03, 0.97);
                float rows = max(_AsciiRows, 1.0);
                float mk = SAMPLE_TEXTURE2D(_AsciiAtlas, sampler_AsciiAtlas, float2((i.gi + l.x) / 16.0, 1.0 - 13.0 / rows + l.y / rows)).r;   // row 12
                return half4(i.col * mk, 1);
            }
            ENDHLSL
        }
    }
}
