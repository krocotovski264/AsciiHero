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

            float WHash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float WNoise(float2 p)      // smooth value noise, 0..1
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(WHash(i), WHash(i + float2(1, 0)), u.x), lerp(WHash(i + float2(0, 1)), WHash(i + float2(1, 1)), u.x), u.y);
            }
            // the wind field (shared by grass, leaves): a coarse gust pattern carried downwind, with finer detail scrolling
            // faster over it (as in the 'Ghost of Tsushima' grass); _AsciiWind: xy direction, z strength (with lulls, from
            // the CPU), w the distance the air has travelled
            float WindGust(float2 p)
            {
                float2 wd = _AsciiWind.xy, wp = float2(-wd.y, wd.x); float run = _AsciiWind.w;
                float2 q = float2(dot(p, wd) - run, dot(p, wp));
                float g = WNoise(q * float2(0.17, 0.11) + 3.1);                    // big gusts: long downwind, broad across
                g = smoothstep(0.32, 0.82, g);
                float d = WNoise(q * float2(0.6, 0.45) - float2(run * 0.7, 0) + 11.7);   // smaller ripples on top, faster
                return saturate(g * (0.7 + 0.6 * d));
            }

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float2 seed : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float3 col : TEXCOORD1; float gi : TEXCOORD2; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 a = TransformObjectToWorld(i.positionOS.xyz);
                float s = i.seed.x;
                float h = 0.165 + 0.225 * s;          // (one and a half times the old blades)
                // wind (main bending along the wind, detail flutter per blade, after Crysis and Ghost of Tsushima):
                //   * the gust pattern travels downwind across the field, so waves of pressed-down grass roll over it, in
                //     patches and bands rather than a regular sine, with calmer gaps between them;
                //   * every blade responds with its own stiffness (a little late) and springs back past upright after a
                //     gust has gone over it, then settles;
                //   * a small flutter of its own, faster and stronger the harder the wind blows;
                //   * the strength rises and falls on the CPU with lulls (pauses) between gusts.
                float2 wd = _AsciiWind.xy; float ws = _AsciiWind.z;
                float stiff = 0.6 + 0.8 * frac(s * 7.77);
                float gNow = WindGust(a.xz - wd * (0.18 * stiff)), gPrev = WindGust(a.xz + wd * (0.9 + 0.5 * stiff));   // (what passed a moment ago is downwind now)
                float wave = gNow * saturate(ws * 1.25);
                float recoil = max(0.0, gPrev - gNow) * saturate(ws * 1.25);
                float fl = sin(_AsciiTime * (3.0 + 4.0 * ws) * (0.8 + 0.4 * frac(s * 3.3)) + s * 40.0);
                float2 wind = wd * ((0.05 + 0.1 * ws) + 0.85 * wave / stiff - 0.28 * recoil * sin(_AsciiTime * 5.0 + s * 9.0 + 1.5))
                            + float2(-wd.y, wd.x) * fl * (0.015 + 0.06 * ws * (0.3 + wave)) + wd * fl * 0.03 * ws * wave;
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
                o.col *= 1.0 + 0.5 * wave;       // a pressed-down band shows lighter (the blades turn their sides to the light)
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
