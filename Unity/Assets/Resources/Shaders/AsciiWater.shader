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
        _WaterMode ("x: underground (no sky, no wind), yz: flow (m/s), w: depth (darker)", Vector) = (0, 0, 0, 0)
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
                float4 _WaterCell; float4 _WaterMode;
                float4 _WaterTintA, _WaterTintB;     // another water (w > 0): its deep and crest colours - a swamp's murk, acid ooze
            CBUFFER_END
            TEXTURE2D(_AsciiAtlas); SAMPLER(sampler_AsciiAtlas);
            TEXTURE2D(_AsciiLitTex); SAMPLER(sampler_AsciiLitTex);
            TEXTURE2D(_AsciiNDTex); SAMPLER(sampler_AsciiNDTex);
            SAMPLER(ascii_point_clamp_sampler); float _AsciiDepthLit;
            float _AsciiRows, _AsciiTime; float4 _GlyphInk[224];
            float4 _AsciiWind; float4 _WaterRip[8]; float4 _MoonDir; float4 _AsciiGrade; float _AsciiDayK;
            float4 _WaterBody[16]; float _WaterBodyN; float4 _WaterTrees[8]; float _WaterTreeN;
            float _WaterReflOn, _AsciiReflPass; TEXTURE2D(_WaterReflTex); SAMPLER(sampler_WaterReflTex);
            TEXTURE2D(_AsciiSkyMap); SAMPLER(sampler_AsciiSkyMap);

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
                float2 wd = _AsciiWind.xy, wp; float str = _AsciiWind.z, run = _AsciiWind.w;
                if (_WaterMode.x > 0.5)
                {   // underground: no wind; a stream runs along its flow, still water only trembles
                    float fl = length(_WaterMode.yz); wd = fl > 1e-3 ? _WaterMode.yz / fl : float2(1, 0);
                    str = 0.05 + 0.5 * saturate(fl); run = t * (0.3 + 2.5 * fl);
                }
                wp = float2(-wd.y, wd.x);
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
                float ring = 0, best = 0; float2 rsl = 0, bdv = float2(1, 0);
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float4 r = _WaterRip[k]; float age = t - r.z;
                    if (r.w <= 0.0 || age < 0.0 || age > 3.5) continue;
                    float2 dv = p.xz - r.xy; float d = length(dv), rad = 0.1 + 0.55 * age * (1.0 - 0.07 * age);
                    float x = (d - rad) / (0.045 + 0.02 * age), e = exp(-x * x) * r.w * exp(-0.85 * age);
                    ring += e; rsl += -x * e * dv / max(d, 1e-3) * 3.0;
                    if (e > best) { best = e; bdv = dv; }
                }
                // foam lines at the shore running inwards
                float foam = shore > 0.7 ? saturate(sin((shore * 2.6 + t * 0.35) * 6.2832 + rs * 1.5) * 1.6 - 0.6) * smoothstep(0.7, 1.0, shore) : 0;
                // the reflection of the sky
                float3 V = normalize(p - _WorldSpaceCameraPos);
                // the mirror is tilted only a little by the waves, so the moon and the stars stay sharp (the waves show in the
                // characters of the water itself)
                float3 N = normalize(float3(-w.y * 0.045, 1.0, -w.z * 0.045));     // (the rings do not tilt it: that swept the reflected ray across the coloured sky - rainbow rings)
                float3 R = reflect(V, N);
                float fres = 0.35 + 0.65 * pow(1.0 - saturate(dot(-V, N)), 3.0);
                // the sky in the reflection: looked up in the night-sky map by the reflected direction (the moon, its halo,
                // the stars and the Milky Way are painted in it)
                float2 skyUV = float2(atan2(R.x, R.z) / 6.2832 + 0.5, saturate(asin(saturate(R.y)) / 1.5708));
                float3 sky = SAMPLE_TEXTURE2D_LOD(_AsciiSkyMap, sampler_AsciiSkyMap, skyUV, 0.6).rgb;
                {   // by day the water shows a pale sky (grey under clouds) instead of the night one
                    float sh = saturate(R.y);
                    float3 daySky = lerp(float3(0.30, 0.38, 0.50), float3(0.12, 0.22, 0.42), sh) * lerp(1.0, 0.55, saturate((1.0 - _AsciiGrade.w) * 3.0));
                    sky = lerp(sky, daySky, _AsciiDayK);
                }
                if (_WaterMode.x > 0.5) sky = float3(0.004, 0.005, 0.008);       // a cave: the vault above is dark
                // what else the reflected ray meets first: the hero (spheres along his bones) and the trees (trunk and two
                // spheres of crown each). Seen from the water, a crown is seen from below: leaves with gaps of sky in them.
                float hitT = 1e5; int hitK = 0; float3 hitP = 0;
                [loop] for (int b = 0; b < 16; b++)
                {
                    if (b >= (int)_WaterBodyN || _WaterReflOn > 0.5) break;
                    float4 sp = _WaterBody[b]; if (sp.w <= 0.0) continue;
                    float3 oc = sp.xyz - p; float tc = dot(oc, R); if (tc <= 0.0) continue;
                    float d2 = dot(oc, oc) - tc * tc; if (d2 > sp.w * sp.w) continue;
                    float th = tc - sqrt(sp.w * sp.w - d2); if (th > 0.0 && th < hitT) { hitT = th; hitK = 1; }
                }
                [loop] for (int q = 0; q < 8; q++)
                {
                    if (q >= (int)_WaterTreeN || _WaterReflOn > 0.5) break;
                    float4 tr = _WaterTrees[q];            // x, z, trunk height, crown radius
                    // trunk: a vertical cylinder
                    float2 o2 = p.xz - tr.xy, d2v = R.xz; float a2 = dot(d2v, d2v), b2 = dot(o2, d2v), c2 = dot(o2, o2) - 0.03;
                    float disc = b2 * b2 - a2 * c2;
                    if (disc > 0.0 && a2 > 1e-5) { float tt = (-b2 - sqrt(disc)) / a2; float yy = p.y + tt * R.y; if (tt > 0.0 && yy < tr.z + 0.3 && tt < hitT) { hitT = tt; hitK = 2; } }
                    // crown: two spheres, the lower one wider
                    [unroll] for (int c = 0; c < 2; c++)
                    {
                        float3 cc = float3(tr.x, tr.z + (c == 0 ? 0.65 : 1.35), tr.y); float rr = tr.w * (c == 0 ? 1.0 : 0.72);
                        float3 oc = cc - p; float tc = dot(oc, R); if (tc <= 0.0) continue;
                        float d2 = dot(oc, oc) - tc * tc; if (d2 > rr * rr) continue;
                        float th = tc - sqrt(rr * rr - d2);
                        float3 hp = p + R * th;
                        float leaf = WNoise(hp.xz * 7.0 + hp.y * 3.0) * 0.6 + WNoise(hp.xz * 17.0 - hp.y * 5.0) * 0.4;
                        if (leaf > 0.42 && th > 0.0 && th < hitT) { hitT = th; hitK = 3; hitP = hp; }       // (a gap in the leaves lets the sky through)
                    }
                }
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
                if (_WaterMode.x > 0.5) { deep = float3(0.05, 0.09, 0.11) * (1.0 - 0.5 * _WaterMode.w); crest = float3(0.2, 0.3, 0.34); }
                if (_WaterTintA.w > 0.5) { deep = _WaterTintA.rgb; crest = _WaterTintB.rgb; }
                float lvl = saturate(0.42 + w.x * 4.2 + ring * 1.3 + foam * 0.8);
                float skyL = dot(sky, float3(0.3, 0.5, 0.2));
                float3 wcol = lerp(deep, crest, lvl) * (0.55 + 0.6 * lvl) + lit * 1.6;
                wcol += sky * 0.5 * fres; wcol *= 0.75 + 0.35 * fres;
                if (best > 0.26)
                {
                    // a ring: drawn with line characters following its curve, as a circle would be drawn in ASCII
                    float2 tg = float2(-bdv.y, bdv.x);                       // tangent on the ground
                    float2 sc2 = float2(-tg.x, -tg.y * 0.82);                  // on the screen (x right, y up; the camera looks along -z)
                    float an = atan2(sc2.y, sc2.x); an = an < 0.0 ? an + 3.14159 : an;     // 0 .. pi
                    row = 12.0; gi = an < 0.39 || an > 2.75 ? 1.0 : an < 1.18 ? 2.0 : an < 1.96 ? 0.0 : 3.0;     // - / | \
                    // white, half see-through: the water's own brightness shows through it, but none of its colour
                    float wl = dot(wcol, float3(0.3, 0.5, 0.2));
                    col = (wl * 0.78 + 0.21 + 0.09 * saturate(best)) * float3(0.95, 0.97, 1.0);      // (half as opaque as it was)
                }
                else if (hitK == 1)
                {   // the hero, upside down in the water: darker than himself, his armour catching a little light
                    row = 2.0; gi = 11.0 + floor(WHash(floor(p.xz * 9.0)) * 2.0);
                    col = float3(0.32, 0.34, 0.42) * (0.75 + 0.35 * fres) + lit * 0.6;
                }
                else if (hitK == 2)
                {
                    row = 7.0; gi = 3.0;                                  // '|'
                    col = float3(0.30, 0.19, 0.11) * (0.8 + 0.3 * fres);
                }
                else if (hitK == 3)
                {
                    row = 11.0; gi = 13.0 + floor(WHash(floor(p.xz * 11.0)) * 2.0);      // '%' '#'
                    col = float3(0.10, 0.26, 0.13) * (0.8 + 0.4 * fres) + sky * 0.15;
                }
                else if (skyL > 0.5 && ring < 0.3)
                {   // the moon
                    row = 9.0; gi = skyL > 0.85 ? 7.0 : skyL > 0.7 ? 6.0 : 5.0;
                    col = sky * (1.05 + 0.45 * fres);
                }
                else if (skyL > 0.09 && ring < 0.25 && best < 0.26)
                {   // stars, the halo, the brighter parts of the Milky Way
                    row = 9.0; gi = skyL > 0.32 ? 3.0 : skyL > 0.17 ? 2.0 : 1.0;
                    col = sky * (1.4 + 0.8 * fres);
                }
                else
                {
                    row = 11.0;
                    gi = lvl < 0.22 ? 1.0 : lvl < 0.38 ? 3.0 : lvl < 0.55 ? 8.0 : lvl < 0.75 ? 9.0 : 10.0;
                    if (ring > 0.35) gi = 9.0;
                    col = lerp(deep, crest, lvl) * (0.55 + 0.6 * lvl) + lit * 1.6;
                    col += float3(0.5, 0.5, 0.5) * (saturate(ring) * 0.3 + foam * 0.45);
                    col += sky * 0.5 * fres;
                    col *= 0.75 + 0.35 * fres;
                    float rk = saturate(ring * 1.6);          // where a ring passes: whiter, no tint
                    col = lerp(col, dot(col, float3(0.3, 0.5, 0.2)) * 0.8 + 0.15, rk * 0.4);
                }
                if (_WaterReflOn > 0.5)
                {
                    float3 rf = SAMPLE_TEXTURE2D_LOD(_WaterReflTex, sampler_WaterReflTex, suv, 0).rgb;
                    col *= 1.0 - 0.7 * saturate(dot(rf, float3(0.3, 0.5, 0.2)) * 2.5);
                }
                float sceneZ = dz > 1e-4 ? dz * 12.0 : 1e5;
                bool vis = _AsciiReflPass < 0.5 && -pVS.z <= sceneZ + 0.08 && all(suv >= 0.0) && all(suv <= 1.0) && shore < 1.02;
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
