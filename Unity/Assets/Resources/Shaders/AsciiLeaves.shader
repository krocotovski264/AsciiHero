// Leaves: the crowns of the trees, the bushes and the ferns are not models but clouds of characters - one per leaf
// cluster, scattered over the surface of the crown's model (offline mesh, sampled once per model). The character goes
// with the light: , ; in the shade and low, * % & higher up towards the moon. They sway in the gusts (the same wind
// field as the grass), the higher the more, and a bush parts a little where the hero pushes through it.
Shader "AsciiHero/Leaves"
{
    Properties
    {
        _GrassA ("shade colour", Color) = (0.10, 0.30, 0.14, 1)
        _GrassB ("light colour", Color) = (0.36, 0.62, 0.26, 1)
        _GrassH ("character size", Float) = 1
        _LeafSee ("a tree's crown: cleared over the screen while the hero is under the trees", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Leaves"
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
            float4 _GrassA, _GrassB; float _GrassH; float _LeafSee;
            float _AsciiLeafClear;     // 0..1: the hero is in the forest - the crowns clear from the whole screen but a 15 % rim       // per field of blades: its two colours and height (grass, wheat)
            float4 _AsciiPush[4];      // points of the body that press the grass: xyz, radius (0 = none)
            float _AsciiGrassThin;     // settings: the share of blades that is not drawn (0 = all)
            float4 _AsciiWind; float4 _AsciiWindOff;   // xy: how far the air has carried the gusts (world xz), zw: the place's reference point         // wind: xy = direction (world xz), z = strength 0..1.5, w = unused

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
                float2 r = p - _AsciiWindOff.zw - _AsciiWindOff.xy;
                return smoothstep(0.38, 0.78, WNoise(r * 0.09 + 3.1));
            }

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float2 seed : TEXCOORD1; float2 nrm : TEXCOORD2; float3 dir : TEXCOORD3; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float3 col : TEXCOORD1; float2 gi : TEXCOORD2; };     // gi: column, row

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 a = TransformObjectToWorld(i.positionOS.xyz);
                float s = i.seed.x, hk = i.seed.y, ny = i.nrm.x; bool wood = i.nrm.y > 0.5;     // wood: a bush's stem and branches          // seed, height in the crown 0..1, normal's up part
                // sway: the whole crown leans with the gusts (more at the top), each cluster flutters on its own
                float2 wd = _AsciiWind.xy; float ws = _AsciiWind.z;
                float g = WindGust(a.xz) * saturate(ws * 1.2);
                float fl = sin(_AsciiTime * (1.2 + 0.5 * frac(s * 3.3)) + s * 40.0);
                float2 sway = wd * (0.012 * saturate(ws) + 0.035 * g) * (0.3 + hk) + float2(-wd.y, wd.x) * fl * (0.008 * saturate(ws));
                float push = 0;
                for (int k = 0; k < 4; k++)
                {
                    float4 P = _AsciiPush[k]; if (P.w < 0.01) continue;
                    float2 d = a.xz - P.xz; float near = saturate(1.0 - (a.y - P.y - 0.2) / 0.6);
                    float pk = smoothstep(P.w * 1.6, P.w * 0.3, length(d)) * near;
                    sway += normalize(d + 1e-4) * pk * 0.22; push = max(push, pk);
                }
                a.xz += sway;
                float3 pVS = TransformWorldToView(a);
                float4 clip = TransformWorldToHClip(a);
                float4 sp = ComputeScreenPos(clip); float2 suv = sp.xy / sp.w;
                float dz = _AsciiDepthLit > 0.5 ? abs(SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, ascii_point_clamp_sampler, suv, 0).a) : SAMPLE_TEXTURE2D_LOD(_AsciiNDTex, sampler_AsciiNDTex, suv, 0).b;
                float sceneZ = dz > 1e-4 ? dz * 12.0 : 1e5;
                float myZ = -pVS.z;
                // under the trees the crowns are gone from the middle of the screen: they show only along its edges, fading in
                // over the outer 15 % (fully clear inside it, no dither)
                float edge = min(min(suv.x, 1.0 - suv.x), min(suv.y, 1.0 - suv.y));
                float keep = 1.0 - _LeafSee * _AsciiLeafClear * smoothstep(0.0, 0.15, edge);
                bool vis = myZ <= sceneZ + 0.35 && all(suv >= -0.05) && all(suv <= 1.05) && keep > 0.02;
                float3 lit = SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, sampler_AsciiLitTex, suv, 0).rgb;
                lit = _AsciiLinear > 0.5 ? pow(max(lit, 0.0), 1.0 / 2.2) : max(lit, 0.0);
                float lum = saturate(dot(lit, float3(0.299, 0.587, 0.114)) * 2.6 * _AsciiExposure);
                // how much light the cluster gets: the top of the crown and the leaves facing up, a little of the ground's light
                float t = saturate(0.15 + 0.45 * hk + 0.3 * saturate(ny) + 0.25 * frac(s * 13.7) + 0.35 * lum - 0.15);
                float row = 2.0;
                float gi = t < 0.25 ? 2.0 : t < 0.4 ? 4.0 : t < 0.55 ? 8.0 : t < 0.7 ? 12.0 : t < 0.86 ? 13.0 : 11.0;     // , ; * % & #
                float depthK = clamp(1.0 - (myZ - _AsciiDepthRef) * 0.08, 0.5, 1.2);
                float3 green = SeasonGreen(lerp(_GrassA.rgb, _GrassB.rgb, t), s);
                if (!wood && frac(s * 3.3) < _AsciiSeason.y * 0.75) vis = false;          // winter: most leaves are gone
                if (wood)
                {   // the stem and the branches: line characters along them as they lie on the screen, in bark brown
                    float3 dv = TransformWorldToViewDir(i.dir, true);
                    float an = atan2(dv.y, dv.x); an = an < 0.0 ? an + 3.14159 : an;
                    row = 12.0; gi = an < 0.39 || an > 2.75 ? 1.0 : an < 1.18 ? 2.0 : an < 1.96 ? 0.0 : 3.0;      // - / | \
                    green = float3(0.30, 0.20, 0.12) * (0.8 + 0.5 * t);
                    keep = 1.0;
                }
                o.gi = float2(gi, row);
                o.col = (green * (0.35 + 0.9 * lum) + lit * 0.35) * depthK * (1.0 + 0.4 * g * hk) * (1.0 - 0.3 * push) * keep;
                float4 ink = _GlyphInk[(int)(row * 16.0 + gi + 0.5)];
                float2 lc = lerp(ink.xy, ink.zw, i.corner + 0.5);
                o.local = lc;
                pVS.xy += (lc - 0.5) * float2(0.15, 0.25) * _GrassH;
                o.positionCS = vis ? TransformWViewToHClip(pVS) : float4(2, 2, 2, 1);
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                float2 l = clamp(i.local, 0.03, 0.97);
                float rows = max(_AsciiRows, 1.0);
                float mk = SAMPLE_TEXTURE2D_BIAS(_AsciiAtlas, sampler_AsciiAtlas, float2((i.gi.x + l.x) / 16.0, 1.0 - (i.gi.y + 1.0) / rows + l.y / rows), -0.6).r;
                mk = saturate((mk - 0.22) * 1.9);
                return half4(i.col * mk, 1);
            }
            ENDHLSL
        }
    }
}
