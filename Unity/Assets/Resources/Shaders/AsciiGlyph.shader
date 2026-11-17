// One character per surface point. Each glyph quad carries its anchor (all 4 corners share it, so skinning moves
// the whole glyph with the body) and is expanded in view space: the character is always upright and undistorted,
// like letters projected from the 3D model. Brightness comes from the lit scene at the anchor (light, shadow and
// nearer = brighter); points that the camera does not see (back faces, skin under armour) are dropped.
Shader "AsciiHero/Glyph"
{
    Properties
    {
        _AsciiRow ("Glyph ramp row", Float) = 0
        _AsciiActor ("Actor boost", Float) = 0
        _GlyphSmoke ("Smoke body", Float) = 0
        _GlyphCloth ("Cloth", Float) = 0
        _GlyphSee ("See-through wall", Float) = 0
        _GlyphFade ("Dissolved (0 whole .. 1 gone)", Float) = 0
        _GlyphCell ("Glyph size (m)", Vector) = (0.017, 0.028, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Glyphs"
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
                float _AsciiRow;
                float _AsciiActor;
                float _GlyphSmoke;
                float4 _GlyphCell;
                float _GlyphCloth;
                float _GlyphSee;
                float _GlyphFade;
            CBUFFER_END
            TEXTURE2D(_AsciiAtlas); SAMPLER(sampler_AsciiAtlas);
            // see-through (same as AsciiHero/Lit): the characters of a wall in front of the hero thin out and dim
            float4 _SeeHero; float _SeeFade;
            float SeeK(float3 pWS)
            {
                if (_SeeHero.w <= 0.0 || _SeeFade <= 0.001) return 0.0;
                float4 hc = TransformWorldToHClip(_SeeHero.xyz), pc = TransformWorldToHClip(pWS);
                float2 d = (pc.xy / pc.w - hc.xy / hc.w) * 0.5; d.x *= _ScreenParams.x / _ScreenParams.y;
                float hz = -TransformWorldToView(_SeeHero.xyz).z, pz = -TransformWorldToView(pWS).z;
                float front = saturate((hz - pz - 0.25) / 0.6);
                return front * (1.0 - smoothstep(_SeeHero.w, _SeeHero.w * 1.4, length(d))) * _SeeFade;      // fully clear inside the circle, a gradient at its rim
            }
            TEXTURE2D(_AsciiLitTex); SAMPLER(sampler_AsciiLitTex);
            TEXTURE2D(_AsciiNDTex); SAMPLER(sampler_AsciiNDTex);
            SAMPLER(ascii_point_clamp_sampler);
            float _AsciiDepthLit;   // 1: the scene depth is in the alpha of the lit colour (no ND camera)
            float4 _GlyphInk[224];  // per atlas glyph (row * 16 + column): the box its ink occupies inside the cell (x0, y0, x1, y1)
            float _AsciiActorLod;   // characters: 1 = every glyph, 1.41 = every second (checkerboard), 2 = every fourth; the glyphs grow to match
            float _AsciiRows, _AsciiExposure, _AsciiLinear, _AsciiDepthRef;
            float4 _DashShift;   // per renderer (world): during the dash the armour pieces lag behind the body
                float4 _DashSpin;    // per renderer: xyz = axis, w = angle; the piece tumbles about _DashPivot while it flies
                float4 _DashPivot;
                float4 _DashScatter;  // per renderer: chain mail coming apart into its links during the dash (w = time, 0 = off)
                float4 _DashSmoke;    // per renderer: the shadow body as smoke during the dash (xyz = back to where it was, w = time, 0 = off)
            // obstacles near the dash path (set when it starts): circles (x, z, r) and boxes (min x, min z, max x, max z)
            float4 _DashObst[8]; float4 _DashBox[4]; float _DashObstN, _DashBoxN;
            // cloth (the robe): the hem trails behind the motion and flutters; in the dash flight it billows harder
            float _ClothDash;      // per renderer (MaterialPropertyBlock)
            float4 _AsciiHeroVel; float _AsciiTime;
            // Cloth (the robe's skirt): simulated on the CPU (AHCloth) as a grid of particles - Verlet integration, distance
            // constraints relaxed a few times per step, collisions projected out of the legs and hips, the top ring pinned to the
            // waist. Here every vertex of the skirt is moved by the grid's displacement from its rest place (bilinear, wrapping
            // round the skirt). Foliage (flag 2) sways in the wind.
            #define CLOTH_COLS 12
            #define CLOTH_ROWS 8
            float4 _ClothGrid[144];       // world displacement of each particle from its rest place
            float4 _ClothNrm[144];        // the cloak's simulated normals (world)
            float4 _ClothMap2;            // the cloak (_ClothMap.w = 1): width at the top and at the hem, columns, rows
            float4 _ClothMap;             // skirt mesh: x = centre z, y = top y, z = bottom y, w = 1 for the cloak
            float _ClothSim;              // per renderer: 1 = this is the simulated skirt
            float4 _AsciiWind;            // xy = direction (world xz), z = strength
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
            float3 ClothWS(float3 ws, float3 os)
            {
                if (_GlyphCloth > 1.5)
                {   // leaves: the crown leans downwind with the gusts, every bit of it flutters
                    float2 wd = _AsciiWind.xy; float st = _AsciiWind.z, t = _AsciiTime;
                    float gust = WindGust(ws.xz);     // the same gusts as over the grass
                    float hk = saturate(ws.y / 3.5);
                    float3 sway = float3(wd.x, 0, wd.y) * st * (0.04 + 0.11 * gust) * hk;
                    float3 fl = float3(sin(t * 5.3 + dot(ws, float3(4.1, 2.3, 3.7))), 0.6 * sin(t * 6.1 + dot(ws, float3(2.9, 3.3, 1.7))), cos(t * 4.7 + dot(ws, float3(1.3, 3.9, 2.1))));
                    return ws + sway + fl * (0.012 + 0.025 * st * gust) * hk;
                }
                if (_GlyphCloth < 0.5 || _ClothSim < 0.5) return ws;
                if (_ClothMap.w > 0.5)
                {   // the cloak: a sheet, columns across (x grows with the width row by row), rows down
                    int C = (int)_ClothMap2.z, R = (int)_ClothMap2.w;
                    float v01 = saturate((_ClothMap.y - os.y) / max(_ClothMap.y - _ClothMap.z, 1e-3));
                    float W = lerp(_ClothMap2.x, _ClothMap2.y, v01);
                    float uu = saturate(os.x / max(W, 1e-3) + 0.5) * (C - 1), vv = v01 * (R - 1);
                    int a0 = min((int)floor(uu), C - 2), b0 = min((int)floor(vv), R - 2); float fa = saturate(uu - a0), fb = saturate(vv - b0);
                    float3 e00 = _ClothGrid[b0 * C + a0].xyz, e01 = _ClothGrid[b0 * C + a0 + 1].xyz, e10 = _ClothGrid[(b0 + 1) * C + a0].xyz, e11 = _ClothGrid[(b0 + 1) * C + a0 + 1].xyz;
                    return ws + lerp(lerp(e00, e01, fa), lerp(e10, e11, fa), fb);
                }
                float u = frac(atan2(os.z - _ClothMap.x, os.x) / 6.2831853 + 1.0) * CLOTH_COLS;
                float v = saturate((_ClothMap.y - os.y) / max(_ClothMap.y - _ClothMap.z, 1e-3)) * (CLOTH_ROWS - 1);
                int c0 = (int)floor(u) % CLOTH_COLS, c1 = (c0 + 1) % CLOTH_COLS; float fu = frac(u);
                int r0 = min((int)floor(v), CLOTH_ROWS - 2); float fv = saturate(v - r0);
                float3 d00 = _ClothGrid[r0 * CLOTH_COLS + c0].xyz, d01 = _ClothGrid[r0 * CLOTH_COLS + c1].xyz;
                float3 d10 = _ClothGrid[(r0 + 1) * CLOTH_COLS + c0].xyz, d11 = _ClothGrid[(r0 + 1) * CLOTH_COLS + c1].xyz;
                return ws + lerp(lerp(d00, d01, fu), lerp(d10, d11, fu), fv);
            }
                float3 DashWS(float3 ws)
            {
                if (_DashSpin.w != 0.0)
                {
                        float3 v = ws - _DashPivot.xyz; float cs = cos(_DashSpin.w), sn = sin(_DashSpin.w);
                    ws = _DashPivot.xyz + v * cs + cross(_DashSpin.xyz, v) * sn + _DashSpin.xyz * dot(_DashSpin.xyz, v) * (1.0 - cs);
                }
                return ws + _DashShift.xyz;
            }
            float _AsciiLod;      // far camera: fewer, larger characters (how many times further than the close-up distance)
            float _GlyphPuff;     // per renderer: 0..1, a burst of smoke flying apart
            float _GlyphBurn;     // per renderer: 0..1, a torn-off limb burning away
            float _LimbGone[6];   // per renderer: 1 = this limb is torn off (index from seed.y)
            #define GLYPH_SCALE 1.35
            #define MIN_GLYPH 5.0

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 corner : TEXCOORD0; float2 seed : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float3 col : TEXCOORD1; float gi : TEXCOORD2; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                // smoke: each glyph of the shadow body keeps drifting off the surface and upwards, fading as it goes
                float ph = frac(_AsciiTime * 0.45 + i.seed.x * 7.31);
                float3 pOS = i.positionOS.xyz;
                // a third of the glyphs are long wisps: they rise much higher and trail behind the moving body
                float wisp = step(0.62, frac(i.seed.x * 13.7));
                if (_GlyphSmoke > 0.5) pOS += i.normalOS * (0.004 + (0.07 + 0.09 * wisp) * ph * ph);
                // puff (a burst of smoke where a limb is torn off): the glyphs fly apart from the centre, rise and thin out
                if (_GlyphPuff > 0.0) pOS += i.normalOS * _GlyphPuff * (0.18 + 0.5 * i.seed.x);
                float3 pWS = DashWS(ClothWS(TransformObjectToWorld(pOS), i.positionOS.xyz));
                // dash, chain mail: every character (a link) flies on its own, a little later or sooner than its neighbours,
                // on its own wobbling path, and they gather on the body again
                // dash: the shadow does not move as a body. Each glyph lingers a moment where the body was, then drifts as
                // smoke - curling round the line of the dash, swelling out and rising a little - and settles back on the
                // body at its new place, one after another, so the smoke gathers into the figure again
                float smkA = 0.0;
                if (_DashSmoke.w > 0.0)
                {
                    float dl = frac(i.seed.x * 17.3) * 0.3, us = saturate((_DashSmoke.w - dl) / 0.42), es = us * us * (3.0 - 2.0 * us);
                    smkA = sin(3.14159 * es);
                    float3 dB = normalize(float3(_DashSmoke.x, 0, _DashSmoke.z) + float3(1e-4, 0, 0)), sB = float3(-dB.z, 0, dB.x);
                    float an = i.seed.x * 61.0 + es * 7.0, rad = (0.1 + 0.32 * frac(i.seed.x * 5.7)) * smkA;
                    pWS += _DashSmoke.xyz * (1.0 - es) + (sB * cos(an) + float3(0, 1, 0) * sin(an)) * rad
                         + float3(0, 0.2 * smkA, 0) + dB * (0.3 * smkA * (frac(i.seed.x * 3.1) - 0.5));
                }
                bool scat = _DashScatter.w > 0.0;
                if (scat)
                {
                    float dl = frac(i.seed.x * 17.3) * 0.42, u2 = saturate((_DashScatter.w - dl) / 0.4), e2 = u2 * u2 * (3.0 - 2.0 * u2), ar = sin(3.14159 * e2);
                    float3 jit = (float3(frac(i.seed.x * 7.1), frac(i.seed.x * 3.7), frac(i.seed.x * 11.9)) - 0.5) * 0.9;
                    pWS += _DashScatter.xyz * (1.0 - e2) + jit * ar + float3(0, 0.3 * ar, 0);
                    // a link does not go into a tree or a rock: it slides round it
                    for (int k = 0; k < 8; k++)
                    {
                        if (k >= (int)_DashObstN) break;
                        float4 ob = _DashObst[k]; float2 dd = pWS.xz - ob.xy; float L = length(dd), R = ob.z + 0.12;
                        if (L < R) pWS.xz = ob.xy + dd / max(L, 1e-4) * R;
                    }
                    for (int b = 0; b < 4; b++)
                    {
                        if (b >= (int)_DashBoxN) break;
                        float4 bx = _DashBox[b] + float4(-0.12, -0.12, 0.12, 0.12);
                        if (pWS.x > bx.x && pWS.x < bx.z && pWS.z > bx.y && pWS.z < bx.w)
                        {
                            float4 dist = float4(pWS.x - bx.x, pWS.z - bx.y, bx.z - pWS.x, bx.w - pWS.z); float m = min(min(dist.x, dist.y), min(dist.z, dist.w));
                            if (m == dist.x) pWS.x = bx.x; else if (m == dist.z) pWS.x = bx.z; else if (m == dist.y) pWS.z = bx.y; else pWS.z = bx.w;
                        }
                    }
                }
                float lod = max(1.0, _AsciiLod);
                bool lodKeep = lod < 1.01 || _AsciiActor > 0.5 || (i.seed.y < 0.0 ? -i.seed.y < 1.12 / (lod * lod) : frac(i.seed.x * 31.7 + 0.37) < 1.7 / (lod * lod));   // scenery: ordered dither on the surface grid (even lattice)     // (characters are thinned evenly on the CPU, see GlyphLayer.SetActorLod: no bald patches)
                if (_GlyphSmoke > 0.5) pWS.y += (0.15 + 0.4 * wisp) * ph * ph;
                if (_GlyphSmoke > 0.5 && _GlyphPuff <= 0.0) pWS -= _AsciiHeroVel.xyz * (0.05 + 0.2 * wisp) * ph * ph;
                if (_GlyphPuff > 0.0) pWS.y += _GlyphPuff * _GlyphPuff * (0.15 + 0.5 * frac(i.seed.x * 5.3));
                bool gone = _LimbGone[(int)(i.seed.y + 0.5)] > 0.5 || dot(i.normalOS, i.normalOS) < 0.09;
                // burning: glyphs turn to embers one by one, float up and go out
                float ember = _GlyphBurn > 0.0 ? saturate((_GlyphBurn * 1.3 - i.seed.x) / 0.3) : 0.0;
                float3 pVS = TransformWorldToView(pWS);
                pVS.y += ember * ember * 0.35;
                float3 nVS = normalize(TransformWorldToViewDir(TransformObjectToWorldNormal(i.normalOS), true));
                float3 toCam = normalize(-pVS);
                float4 clip = TransformWViewToHClip(pVS);
                float4 sp = ComputeScreenPos(clip);
                float2 suv = sp.xy / sp.w;
                float dz = _AsciiDepthLit > 0.5 ? abs(SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, ascii_point_clamp_sampler, suv, 0).a) : SAMPLE_TEXTURE2D_LOD(_AsciiNDTex, sampler_AsciiNDTex, suv, 0).b;
                float sceneZ = dz > 1e-4 ? dz * 12.0 : 1e5;
                float myZ = -pVS.z;
                bool inside = all(suv >= 0.0) && all(suv <= 1.0);
                float seeK = _GlyphSee > 0.5 ? SeeK(pWS) : 0.0;
                bool seeGone = frac(i.seed.x * 13.37 + 0.21) < seeK * 1.02 || frac(i.seed.x * 7.13 + 0.53) < _GlyphFade * 1.02;      // (_GlyphFade: dissolving away)
                bool vis = !seeGone && ((scat || smkA > 0.03) ? (lodKeep && !gone && inside) : lodKeep && !gone && ember < 0.999 && dot(nVS, toCam) > -0.05 - ember - _GlyphPuff * 4.0 - (_DashSpin.w != 0.0 ? 4.0 : 0.0) && myZ <= sceneZ + 0.008 + (0.0035 + 0.007 * (lod - 1.0)) * myZ && inside);
                float3 lit = SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, sampler_AsciiLitTex, suv, 0).rgb;
                float3 col = _AsciiLinear > 0.5 ? pow(max(lit, 0.0), 1.0 / 2.2) : max(lit, 0.0);
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                float depthK = clamp(1.0 - (myZ - _AsciiDepthRef) * 0.09, 0.5, 1.2);
                lum = saturate((lum * 1.15 * _AsciiExposure * (1.0 + 0.55 * _AsciiActor) + 0.06 * _AsciiActor) * depthK);
                // dense glyphs only (no '.' or ' '): darkness is carried by colour, so the surface stays filled
                float gi = MIN_GLYPH + floor(saturate(pow(lum, 0.85) + (i.seed.x - 0.5) * 0.09) * (15.99 - MIN_GLYPH));
                float m = max(max(col.r, col.g), col.b);
                o.col = (_AsciiRow > 8.5 && _AsciiRow < 9.5) ? col * 1.3 + 0.15 : (col / max(m, 1e-3)) * (0.10 + 0.95 * lum);
                if (_GlyphSmoke > 0.5)
                {
                    gi = floor(lerp(15.0, MIN_GLYPH, ph));
                    o.col = lerp(float3(0.42, 0.27, 0.80), float3(0.10, 0.06, 0.24), ph) * (1.0 - ph * (0.8 - 0.25 * wisp)) * (0.75 + 0.25 * depthK);
                    if (_GlyphPuff > 0.0) { gi = floor(lerp(15.0, MIN_GLYPH, _GlyphPuff)); o.col = lerp(float3(0.55, 0.36, 0.95), float3(0.10, 0.06, 0.22), _GlyphPuff) * (1.0 - _GlyphPuff) * (1.0 - _GlyphPuff * 0.5); }
                }
                if (smkA > 0.0)
                {   // in flight the glyphs are thin, dark violet smoke
                    o.col = lerp(o.col, lerp(float3(0.45, 0.30, 0.85), float3(0.12, 0.07, 0.28), frac(i.seed.x * 7.7)), smkA) * (1.0 - 0.3 * smkA);
                    gi = floor(lerp(gi, MIN_GLYPH + 3.0 * frac(i.seed.x * 4.3), smkA));
                }
                if (scat) { o.col = float3(0.62, 0.67, 0.76) * (0.5 + 0.5 * frac(i.seed.x * 5.1)); gi = MIN_GLYPH + floor(frac(i.seed.x * 9.7) * (15.99 - MIN_GLYPH)); }
                o.col *= 1.0 - 0.5 * seeK;
                if (ember > 0.0) { o.col = lerp(o.col, float3(1.6, 0.62, 0.12), min(1.0, ember * 2.0)) * (1.0 - ember * ember); gi = floor(lerp(gi, 2.0, ember)); }
                o.gi = gi;
                // the quad covers only the part of the cell the character's ink is in: the empty margins of a cell were most
                // of the pixels this layer had to blend (a '.' fills a tenth of its cell)
                float4 ink = _GlyphInk[(int)(clamp(_AsciiRow, 0.0, 13.0) * 16.0 + clamp(gi, 0.0, 15.0) + 0.5)];
                float2 lc = lerp(ink.xy, ink.zw, i.corner + 0.5);
                o.local = lc;
                pVS.xy += (lc - 0.5) * _GlyphCell.xy * GLYPH_SCALE * (_AsciiActor > 0.5 ? 1.18 * max(1.0, _AsciiActorLod) : lod);   // upright character in the view plane; on characters dense and overlapping so no gaps show
                o.positionCS = vis ? TransformWViewToHClip(pVS) : float4(2, 2, 2, 1);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 l = clamp(i.local, 0.03, 0.97);
                float rows = max(_AsciiRows, 1.0);
                // sharp characters: a crisper mip and a hard threshold on the glyph's coverage (no soft, washed-out edges)
                float mk = SAMPLE_TEXTURE2D_BIAS(_AsciiAtlas, sampler_AsciiAtlas, float2((i.gi + l.x) / 16.0, 1.0 - (_AsciiRow + 1.0) / rows + l.y / rows), -0.6).r;
                mk = saturate((mk - 0.22) * 1.9);
                return half4(i.col * mk, 1);
            }
            ENDHLSL
        }
    }
}
