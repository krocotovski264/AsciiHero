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
            float4 _AsciiPush[4];      // points of the body that press the grass: xyz, radius (0 = none)
            float _AsciiGrassThin;     // settings: the share of blades that is not drawn (0 = all)
            float4 _AsciiWind;         // wind: xy = direction (world xz), z = strength 0..1.5, w = unused

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float2 seed : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float3 col : TEXCOORD1; float gi : TEXCOORD2; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 a = TransformObjectToWorld(i.positionOS.xyz);
                float s = i.seed.x;
                float h = 0.165 + 0.225 * s;          // (one and a half times the old blades)
                // wind: the blades lean downwind, and gusts roll across the field as waves; each blade also flutters a little
                float2 wd = _AsciiWind.xy; float ws = _AsciiWind.z;
                float2 wp = float2(-wd.y, wd.x);
                float gust = 0.5 + 0.5 * sin(dot(a.xz, wd) * 0.85 - _AsciiTime * 2.3 + 0.6 * sin(dot(a.xz, wp) * 0.4 + _AsciiTime * 0.5));
                gust *= gust;
                float2 wind = wd * ws * (0.1 + 0.42 * gust)
                            + float2(sin(_AsciiTime * 4.3 + s * 40.0 + a.x * 3.0), cos(_AsciiTime * 3.7 + s * 23.0 + a.z * 3.0)) * (0.025 + 0.05 * ws * gust);
                float2 lean = wind; float push = 0;
                for (int k = 0; k < 4; k++)
                {
                    float4 P = _AsciiPush[k]; if (P.w < 0.01) continue;
                    float2 d = a.xz - P.xz; float near = saturate(1.0 - (P.y - h) / 0.35);       // only what is down among the blades presses them
                    float pk = smoothstep(P.w, P.w * 0.2, length(d)) * near;
                    lean += normalize(d + 1e-4) * pk * 0.95; push = max(push, pk);
                }
                float ll = length(lean); if (ll > 0.96) { lean *= 0.96 / ll; ll = 0.96; }
                float3 tip = a + float3(lean.x, sqrt(1.0 - ll * ll), lean.y) * h;
                float3 pVS = TransformWorldToView((a + tip) * 0.5);
                float4 bclip = TransformWorldToHClip(a);
                float4 sp = ComputeScreenPos(bclip);
                float2 suv = sp.xy / sp.w;
                float4 nd = SAMPLE_TEXTURE2D_LOD(_AsciiNDTex, sampler_AsciiNDTex, suv, 0);
                float sceneZ = nd.b > 1e-4 ? nd.b * 12.0 : 1e5;
                float myZ = -pVS.z;
                bool vis = myZ <= sceneZ + 0.3 && all(suv >= 0.0) && all(suv <= 1.0) && frac(s * 7.31) >= _AsciiGrassThin;
                float lv = TransformWorldToViewDir(float3(lean.x, 0, lean.y), false).x;
                o.gi = push > 0.8 ? 4.0 : (lv > 0.2 ? 2.0 : (lv < -0.2 ? 3.0 : 0.0));
                float3 lit = SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, sampler_AsciiLitTex, suv, 0).rgb;
                lit = _AsciiLinear > 0.5 ? pow(max(lit, 0.0), 1.0 / 2.2) : max(lit, 0.0);
                float lum = saturate(dot(lit, float3(0.299, 0.587, 0.114)) * 2.6 * _AsciiExposure);
                float depthK = clamp(1.0 - (myZ - _AsciiDepthRef) * 0.09, 0.5, 1.2);
                float3 green = lerp(float3(0.16, 0.50, 0.24), float3(0.50, 0.78, 0.36), frac(s * 13.7));
                o.col = (green * (0.22 + 0.95 * lum) + lit * 0.5) * depthK * (1.0 - 0.35 * push);
                o.local = i.corner + 0.5;
                pVS.xy += i.corner * float2(h * 0.44, h * 1.1);       // the characters keep their width, the blades are taller
                o.positionCS = vis ? TransformWViewToHClip(pVS) : float4(2, 2, 2, 1);
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                float2 l = clamp(i.local, 0.03, 0.97);
                float rows = max(_AsciiRows, 1.0);
                float mk = SAMPLE_TEXTURE2D_BIAS(_AsciiAtlas, sampler_AsciiAtlas, float2((i.gi + l.x) / 16.0, 1.0 - 13.0 / rows + l.y / rows), -0.6).r;   // row 12
                mk = saturate((mk - 0.22) * 1.9);
                return half4(i.col * mk, 1);
            }
            ENDHLSL
        }
    }
}
