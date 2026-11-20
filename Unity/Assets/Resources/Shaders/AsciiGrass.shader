// Grass: one character per blade. | upright, / or \ when it leans across the screen, _ when trodden flat.
// Blades sway in the wind and are pushed aside by the hero (_AsciiHeroA) and by a point trailing behind him
// (_AsciiHeroB), so the grass parts in front of the feet and closes again a moment later.
Shader "AsciiHero/Grass"
{
    Properties
    {
        _GrassA ("colour A", Color) = (0.16, 0.50, 0.24, 1)
        _GrassB ("colour B", Color) = (0.50, 0.78, 0.36, 1)
        _GrassH ("height", Float) = 1
    }
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
            SAMPLER(ascii_point_clamp_sampler); float _AsciiDepthLit;
            float4 _GlyphInk[224];   // per atlas glyph: the box its ink occupies inside the cell
            float _AsciiRows, _AsciiExposure, _AsciiLinear, _AsciiDepthRef, _AsciiTime;
            float4 _GrassA, _GrassB; float _GrassH;       // per field of blades: its two colours and height (grass, wheat)
            float4 _AsciiPush[4];      // points of the body that press the grass: xyz, radius (0 = none)
            float _AsciiGrassThin;     // settings: the share of blades that is not drawn (0 = all)
            float4 _AsciiWind;         // wind: xy = direction (world xz), z = strength 0..1.5, w = unused

            // the season over the foliage (_AsciiSeason: x autumn, y winter, z spring, w snow lying)
            float4 _AsciiSeason;
            float3 SeasonGreen(float3 g, float s)
            {
                float l = dot(g, float3(0.3, 0.5, 0.2));
                float3 aut = lerp(float3(0.85, 0.40, 0.10), float3(0.80, 0.62, 0.14), frac(s * 5.3)) * (l * 2.0 + 0.08);
                if (frac(s * 2.7) > 0.8) aut = float3(0.62, 0.18, 0.08) * (l * 2.0 + 0.08);
                g = lerp(g, aut, _AsciiSeason.x);
                float3 win = lerp(float3(0.42, 0.40, 0.36) * (l * 1.6 + 0.06), float3(0.86, 0.9, 0.98) * (l * 1.2 + 0.22), _AsciiSeason.w * step(0.45, frac(s * 4.1)));
                g = lerp(g, win, _AsciiSeason.y);
                float3 spr = frac(s * 9.7) > 0.86 ? float3(0.95, 0.62, 0.78) * (l * 2.0 + 0.12) : g * 1.12 + float3(0.02, 0.05, 0.0);
                return lerp(g, spr, _AsciiSeason.z);
            }
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
                float g = WNoise(q * float2(0.09, 0.08) + 3.1);                    // big gusts: long downwind, broad across
                g = smoothstep(0.15, 0.95, g);            // (soft edges: a gust leans the blades over gradually)
                float d = WNoise(q * float2(0.35, 0.3) - float2(run * 0.3, 0) + 11.7);   // smaller ripples on top, faster
                return saturate(g * (0.7 + 0.6 * d));
            }

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float2 seed : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float3 col : TEXCOORD1; float gi : TEXCOORD2; float3 colB : TEXCOORD3; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 a = TransformObjectToWorld(i.positionOS.xyz);
                float s = i.seed.x;
                float h = (0.165 + 0.225 * s) * _GrassH;          // (one and a half times the old blades)
                // wind (main bending along the wind, detail flutter per blade, after Crysis and Ghost of Tsushima):
                //   * the gust pattern travels downwind across the field, so waves of pressed-down grass roll over it, in
                //     patches and bands rather than a regular sine, with calmer gaps between them;
                //   * every blade responds with its own stiffness (a little late) and springs back past upright after a
                //     gust has gone over it, then settles;
                //   * a small flutter of its own, faster and stronger the harder the wind blows;
                //   * the strength rises and falls on the CPU with lulls (pauses) between gusts.
                float2 wd = _AsciiWind.xy; float ws = _AsciiWind.z;
                float stiff = 0.6 + 0.8 * frac(s * 7.77);
                float gNow = WindGust(a.xz - wd * (0.18 * stiff)), gPrev = WindGust(a.xz + wd * (2.0 + 0.5 * stiff));   // (what passed a moment ago is downwind now)
                float gMid = WindGust(a.xz + wd * 1.1); gNow = (gNow + gMid) * 0.5;      // (eased: the gust is felt over a stretch of the field)
                float wave = gNow * saturate(ws * 1.25);
                float recoil = max(0.0, gPrev - gNow) * saturate(ws * 1.25);
                float fl = sin(_AsciiTime * (2.0 + 2.0 * ws) * (0.8 + 0.4 * frac(s * 3.3)) + s * 40.0);
                // (the lean as deep as it was; the recoil springs back slowly, the flutter stays small)
                float2 wind = wd * ((0.05 + 0.1 * ws) + 0.85 * wave / stiff - 0.18 * recoil * sin(_AsciiTime * 1.6 + s * 9.0 + 1.5))
                            + float2(-wd.y, wd.x) * fl * (0.012 + 0.03 * ws * (0.3 + wave)) + wd * fl * 0.015 * ws * wave;
                // a slow sway of its own even in still air (a noise over the field, so neighbours sway together)
                // (small: it bends the blade a little but never turns its character, or the whole field flickers | / \ at once)
                float2 idle = float2(WNoise(a.xz * 0.6 + _AsciiTime * 0.12) - 0.5, WNoise(a.xz * 0.6 + 7.3 - _AsciiTime * 0.1) - 0.5);
                float2 sway = idle * (0.05 + 0.03 * frac(s * 4.3)) * (1.0 - 0.5 * saturate(ws));
                float2 lean = wind; float push = 0;
                for (int k = 0; k < 4; k++)
                {
                    float4 P = _AsciiPush[k]; if (P.w < 0.01) continue;
                    float2 d = a.xz - P.xz; float near = saturate(1.0 - (P.y - h) / 0.35);       // only what is down among the blades presses them
                    float pk = smoothstep(P.w, P.w * 0.2, length(d)) * near;
                    lean += normalize(d + 1e-4) * pk * 0.95; push = max(push, pk);
                }
                float ll = length(lean); if (ll > 0.96) { lean *= 0.96 / ll; ll = 0.96; }
                float2 leanS = lean + sway; float lls = min(length(leanS), 0.96);
                float3 tip = a + float3(leanS.x, sqrt(1.0 - lls * lls), leanS.y) * h;
                float3 pVS = TransformWorldToView((a + tip) * 0.5);
                float4 bclip = TransformWorldToHClip(a);
                float4 sp = ComputeScreenPos(bclip);
                float2 suv = sp.xy / sp.w;
                float dz = _AsciiDepthLit > 0.5 ? abs(SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, ascii_point_clamp_sampler, suv, 0).a) : SAMPLE_TEXTURE2D_LOD(_AsciiNDTex, sampler_AsciiNDTex, suv, 0).b;
                float sceneZ = dz > 1e-4 ? dz * 12.0 : 1e5;
                float myZ = -pVS.z;
                bool vis = myZ <= sceneZ + 0.3 && all(suv >= 0.0) && all(suv <= 1.0) && frac(s * 7.31) >= _AsciiGrassThin && frac(s * 5.17) >= _AsciiSeason.w * 0.7;
                float lv = TransformWorldToViewDir(float3(lean.x, 0, lean.y), false).x;
                o.gi = push > 0.8 ? 4.0 : (lv > 0.22 ? 2.0 : (lv < -0.22 ? 3.0 : 0.0));
                float3 lit = SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, sampler_AsciiLitTex, suv, 0).rgb;
                lit = _AsciiLinear > 0.5 ? pow(max(lit, 0.0), 1.0 / 2.2) : max(lit, 0.0);
                float lum = saturate(dot(lit, float3(0.299, 0.587, 0.114)) * 2.6 * _AsciiExposure);
                float depthK = clamp(1.0 - (myZ - _AsciiDepthRef) * 0.09, 0.5, 1.2);
                float3 green = SeasonGreen(lerp(_GrassA.rgb, _GrassB.rgb, frac(s * 13.7)), s);
                // near and far (the stylised grass of Binary Lunar): close blades warmer and yellower, far ones cooler, bluer
                float nf = smoothstep(4.0, 20.0, myZ);
                green = lerp(green * float3(1.12, 1.07, 0.78) + float3(0.035, 0.03, 0.0), green * float3(0.82, 0.94, 1.12), nf);
                float3 top = green * (0.22 + 0.95 * lum) + lit * 0.35;
                // in shadow the tips go towards a cool shadow colour, not just darker
                float shd = saturate(1.0 - lum * 1.7);
                top = lerp(top, top * float3(0.55, 0.66, 1.0) + float3(0.0, 0.01, 0.03), shd * 0.6);
                // a pressed-down band of the wind shows lighter on the tips (the blades turn their sides to the light)
                top = top * (1.0 + 0.3 * wave) + float3(0.16, 0.18, 0.1) * wave * (0.4 + lum);
                // the root: dark and of the ground's own colour, so the grass grows out of the earth instead of lying on it
                float3 root = lerp(green * 0.35, lit * 1.15, 0.55) * (0.35 + 0.65 * lum);
                float kk = depthK * (1.0 - 0.35 * push);
                o.col = top * kk; o.colB = root * kk;
                // a little more colour (as the saturation of the lesson's post), the grey of the scene stays as it was
                float lc0 = dot(o.col, float3(0.3, 0.5, 0.2)); o.col = max(0.0, lerp(lc0.xxx, o.col, 1.18));
                // the quad covers only the part of the cell the stroke is in (an upright '|' is a third of its cell, a
                // trodden '_' a fifth): the many blades lying over each other fill far fewer pixels
                float4 ink = _GlyphInk[(int)(12.0 * 16.0 + o.gi + 0.5)];
                float2 lc = lerp(ink.xy, ink.zw, i.corner + 0.5);
                o.local = lc;
                pVS.xy += (lc - 0.5) * float2(h * 0.44, h * 1.1);       // the characters keep their width, the blades are taller
                o.positionCS = vis ? TransformWViewToHClip(pVS) : float4(2, 2, 2, 1);
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                float2 l = clamp(i.local, 0.03, 0.97);
                float rows = max(_AsciiRows, 1.0);
                float mk = SAMPLE_TEXTURE2D_BIAS(_AsciiAtlas, sampler_AsciiAtlas, float2((i.gi + l.x) / 16.0, 1.0 - 13.0 / rows + l.y / rows), -0.6).r;   // row 12
                mk = saturate((mk - 0.22) * 1.9);
                float3 c = lerp(i.colB, i.col, i.gi > 3.5 ? 0.7 : smoothstep(0.05, 0.8, l.y));      // root to tip (a trodden blade lies flat: its own colour)
                return half4(c * mk, 1);
            }
            ENDHLSL
        }
    }
}
