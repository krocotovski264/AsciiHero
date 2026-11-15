// The lake, in characters. One camera-facing quad per glyph cell of the water surface; the vertex shader decides what
// each cell shows:
//  - the night sky mirrored in the water: the view ray is reflected off the (rippled) surface; where it meets the
//    moon the cell shows the moon ('@' 'O'), round it a halo, elsewhere the stars ('*' '+' '.'), which shiver as the
//    waves bend the reflection; the reflection is stronger the flatter the view (Fresnel)
//  - waves carried by the same wind as the grass (_AsciiWind): long swells downwind, a finer chop over them, the gust
//    patches running across the lake; crests show as '~' '=' '-', troughs as '.' ','
//  - rings spreading from the hero's steps (_WaterRip: x, z, birth time, strength), fading as they grow
//  - lines of foam at the shore that run in towards the middle
Shader "AsciiHero/Water"
{
    Properties
    {
        _WaterCell ("glyph width, height, -, -", Vector) = (0.1, 0.16, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Water"
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite Off ZTest Always
            BlendOp Max
            Blend One One
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _WaterCell;
            CBUFFER_END
            TEXTURE2D(_AsciiAtlas); SAMPLER(sampler_AsciiAtlas);
            TEXTURE2D(_AsciiLitTex); SAMPLER(sampler_AsciiLitTex);
            TEXTURE2D(_AsciiNDTex); SAMPLER(sampler_AsciiNDTex);
            SAMPLER(ascii_point_clamp_sampler); float _AsciiDepthLit;
            float _AsciiRows, _AsciiTime; float4 _GlyphInk[224];
            float4 _AsciiWind; float4 _WaterRip[8]; float4 _MoonDir;

            float WHash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float WNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(WHash(i), WHash(i + float2(1, 0)), u.x), lerp(WHash(i + float2(0, 1)), WHash(i + float2(1, 1)), u.x), u.y);
            }

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float2 seed : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float3 col : TEXCOORD1; float2 glyph : TEXCOORD2; };

            // the water height at p (m, small) and its slope (d/dx, d/dz)
            float3 Waves(float2 p, float t)
            {
                float2 wd = _AsciiWind.xy, wp = float2(-wd.y, wd.x); float str = _AsciiWind.z, run = _AsciiWind.w;
                float2 q = float2(dot(p, wd), dot(p, wp));
                // gust patches carried downwind: where the air hits, the chop is stronger
                float g = smoothstep(0.25, 0.85, WNoise(float2(q.x - run, q.y) * float2(0.35, 0.25) + 5.3));
                float amp = (0.15 + 0.85 * str) * (0.35 + 0.9 * g);
                // a long swell downwind and two shorter trains a little across it, each moving with its own speed
                float k1 = 2.4, k2 = 4.3, k3 = 6.1;
                float2 d2 = normalize(wd + wp * 0.45), d3 = normalize(wd - wp * 0.6);
                float a1 = dot(p, wd) * k1 - t * 1.9 - run * 0.6, a2 = dot(p, d2) * k2 - t * 2.6 - run * 0.9, a3 = dot(p, d3) * k3 - t * 3.1;
                float h = 0.5 * sin(a1) + 0.3 * sin(a2) + 0.2 * sin(a3);
                float2 s = 0.5 * k1 * cos(a1) * wd + 0.3 * k2 * cos(a2) * d2 + 0.2 * k3 * cos(a3) * d3;
                return float3(h, s) * amp;
            }

            Varyings vert (Attributes i)
            {
                Varyings o;
                float t = _AsciiTime, rs = i.seed.y;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                float shore = i.seed.x;                          // 0 in the middle .. 1 at the shore
                float3 w = Waves(p.xz, t) * (1.0 - 0.6 * shore);
                // rings from the hero's steps
                float ring = 0; float2 rsl = 0;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float4 r = _WaterRip[k]; float age = t - r.z;
                    if (r.w <= 0.0 || age < 0.0 || age > 3.5) continue;
                    float2 dv = p.xz - r.xy; float d = length(dv), rad = 0.12 + 0.62 * age * (1.0 - 0.08 * age);
                    float x = (d - rad) / (0.07 + 0.03 * age), e = exp(-x * x) * r.w * exp(-1.25 * age);
                    ring += e; rsl += -x * e * dv / max(d, 1e-3) * 3.0;
                }
                // foam lines at the shore running inwards
                float foam = shore > 0.7 ? saturate(sin((shore * 2.6 + t * 0.35) * 6.2832 + rs * 1.5) * 1.6 - 0.6) * smoothstep(0.7, 1.0, shore) : 0;
                // the reflection of the sky
                float3 V = normalize(p - _WorldSpaceCameraPos);
                float3 N = normalize(float3(-(w.y * 0.22 + rsl.x * 0.25), 1.0, -(w.z * 0.22 + rsl.y * 0.25)));
                float3 R = reflect(V, N);
                float fres = 0.35 + 0.65 * pow(1.0 - saturate(dot(-V, N)), 3.0);
                float3 M = normalize(_MoonDir.xyz);
                float md = acos(clamp(dot(R, M), -1.0, 1.0));
                float moon = 1.0 - smoothstep(0.05, 0.075, md), halo = (1.0 - smoothstep(0.075, 0.24, md)) * (1.0 - moon);
                // stars: the reflected direction picks a cell of a star map; a star shows in few of them, and it twinkles
                float2 sd = R.xz / max(R.y, 0.15) * 52.0;
                float2 sc = floor(sd), sf = frac(sd);
                float sh = WHash(sc + 17.0), tw = 0.55 + 0.45 * sin(t * (2.0 + 3.0 * WHash(sc + 3.0)) + sh * 40.0);
                float star = sh > 0.955 && R.y > 0.05 && length(sf - 0.5) < 0.42 ? tw * (0.5 + 0.5 * WHash(sc + 9.0)) : 0;
                // what stands in the water (the hero) hides the cells behind it
                float3 pVS = TransformWorldToView(p);
                float4 clip = TransformWViewToHClip(pVS);
                float4 spp = ComputeScreenPos(clip); float2 suv = spp.xy / spp.w;
                float4 sl = SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, ascii_point_clamp_sampler, suv, 0);
                float dz = _AsciiDepthLit > 0.5 ? abs(sl.a) : SAMPLE_TEXTURE2D_LOD(_AsciiNDTex, sampler_AsciiNDTex, suv, 0).b;
                float3 lit = max(sl.rgb, 0);      // the lake's own lit colour (the hero's lantern on the water)
                // which character, in which row of the atlas
                float row, gi; float3 col;
                float3 deep = float3(0.09, 0.17, 0.34), crest = float3(0.33, 0.52, 0.78);
                float lvl = saturate(0.42 + w.x * 4.2 + ring * 1.3 + foam * 0.8);
                if (moon > 0.5)
                {
                    row = 9.0; gi = md < 0.03 ? 7.0 : 5.0;
                    col = float3(1.0, 0.96, 0.82) * (1.1 + 0.5 * fres);
                }
                else if (halo > 0.25 && WHash(p.xz * 13.0 + floor(t * 3.0)) < halo * 1.2)
                {
                    row = 9.0; gi = halo > 0.6 ? 3.0 : (halo > 0.4 ? 2.0 : 1.0);
                    col = float3(0.85, 0.86, 0.95) * (0.35 + 0.6 * halo) * fres;
                }
                else if (star > 0.15 && ring < 0.25)
                {
                    row = 9.0; gi = star > 0.7 ? 2.0 : (star > 0.4 ? 3.0 : 1.0);
                    col = float3(0.82, 0.88, 1.0) * (0.5 + 0.7 * star) * fres;
                }
                else
                {
                    row = 11.0;
                    gi = lvl < 0.22 ? 1.0 : lvl < 0.38 ? 3.0 : lvl < 0.55 ? 8.0 : lvl < 0.75 ? 9.0 : 10.0;
                    if (ring > 0.35) gi = 9.0;
                    col = lerp(deep, crest, lvl) * (0.55 + 0.6 * lvl) + lit * 1.6;
                    col += float3(0.5, 0.55, 0.65) * (saturate(ring) * 0.55 + foam * 0.45);
                    col *= 0.75 + 0.35 * fres;
                }
                float sceneZ = dz > 1e-4 ? dz * 12.0 : 1e5;
                bool vis = -pVS.z <= sceneZ + 0.08 && all(suv >= 0.0) && all(suv <= 1.0) && shore < 1.02;
                o.col = col * (1.0 - smoothstep(0.93, 1.02, shore) * 0.5);
                o.glyph = float2(row, gi);
                float4 ink = _GlyphInk[(int)(row * 16.0 + gi + 0.5)];
                float2 lc = lerp(ink.xy, ink.zw, i.corner + 0.5);
                o.local = lc;
                pVS.xy += (lc - 0.5) * _WaterCell.xy;
                o.positionCS = vis ? TransformWViewToHClip(pVS) : float4(2, 2, 2, 1);
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                float2 l = clamp(i.local, 0.03, 0.97);
                float rows = max(_AsciiRows, 1.0);
                float mk = SAMPLE_TEXTURE2D_BIAS(_AsciiAtlas, sampler_AsciiAtlas, float2((i.glyph.y + l.x) / 16.0, 1.0 - (i.glyph.x + 1.0) / rows + l.y / rows), -0.6).r;
                mk = saturate((mk - 0.22) * 1.9);
                return half4(i.col * mk, 1);
            }
            ENDHLSL
        }
    }
}
