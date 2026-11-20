// Grass, rebuilt after the stylised grass of Binary Lunar ("Stylized Grass with Wind Effect Shader Graph"), told in
// characters: every blade is a strip of two segments drawn with the stroke of '|', bending from the root.
//   * depth colour: near blades warmer and lighter, far ones cooler (smoothstep over the distance from the camera);
//   * height blend: a bottom colour at the root, the top colour at the tip;
//   * terrain colour: the root takes the colour of the ground under it (here the lit scene under the blade);
//   * shadow: where the ground is in shadow the blade goes towards a shadow colour;
//   * sway: a noise over the world, carried with the air, bends the upper part of the blade a little;
//   * wind waves: a second, larger noise scrolling downwind (the lesson's wind texture), with a contrast step; it
//     bends the upper part along the wind and lights the tips where it passes;
//   * the hero and a point trailing behind him press the blades aside (as before).
// Mesh: per blade 6 vertices (3 rows x 2 sides): POSITION = the root, uv0 = (side -0.5/0.5, t 0..1), uv1.x = seed.
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
            float4 _GlyphInk[224];
            float _AsciiRows, _AsciiExposure, _AsciiLinear, _AsciiDepthRef, _AsciiTime;
            float4 _GrassA, _GrassB; float _GrassH;
            float4 _AsciiPush[4];
            float _AsciiGrassThin;
            float4 _AsciiWind;         // xy direction (world xz), z strength 0..1.5, w the distance the air has run
            float4 _AsciiSeason;

            // ---- the lesson's settings (fixed here) ----
            #define NEAR_FAR      float2(4.0, 20.0)     // depth colour: where the near colour ends and the far begins
            #define HEIGHT_BLEND  0.85                  // how high the bottom colour reaches up the blade
            #define TERRAIN_K     0.45                  // how much of the ground's colour the blade takes
            #define SHADOW_COL    float3(0.42, 0.52, 0.85)
            #define SWAY_SCALE    0.55                  // the sway noise (per metre)
            #define SWAY_K        0.10                  // the sway's bend
            #define WAVE_SCALE    float2(0.075, 0.11)   // the wind waves' noise: long downwind, narrower across
            #define WAVE_CONTRAST float2(0.38, 0.78)    // smoothstep edges of the wave
            #define WAVE_K        0.75                  // how far a wave bends the blade over
            #define WAVE_LIGHT    float3(0.20, 0.22, 0.12)

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
            float WNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(WHash(i), WHash(i + float2(1, 0)), u.x), lerp(WHash(i + float2(0, 1)), WHash(i + float2(1, 1)), u.x), u.y);
            }

            struct Attributes { float4 positionOS : POSITION; float2 bt : TEXCOORD0; float2 seed : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 col : TEXCOORD1; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 a = TransformObjectToWorld(i.positionOS.xyz);
                float s = i.seed.x, side = i.bt.x, t = i.bt.y;
                float h = (0.18 + 0.25 * s) * _GrassH;

                // the air: both noises live in the wind's own frame and run downwind with it
                float2 wd = _AsciiWind.xy, wp = float2(-wd.y, wd.x); float ws = _AsciiWind.z, run = _AsciiWind.w;
                float2 q = float2(dot(a.xz, wd) - run, dot(a.xz, wp));
                // wind waves (the wind texture): a contrast step over a large noise
                float wave = smoothstep(WAVE_CONTRAST.x, WAVE_CONTRAST.y, WNoise(q * WAVE_SCALE + 3.1)) * saturate(ws * 1.2);
                // sway (the gradient noise): a small bend in any direction, the same for neighbours
                float2 sw = float2(WNoise(q * SWAY_SCALE + 21.3), WNoise(q * SWAY_SCALE + 7.7)) - 0.5;
                float stiff = 0.75 + 0.5 * frac(s * 7.77);
                float2 lean = (wd * (0.06 + 0.1 * ws + WAVE_K * wave) + (sw.x * wd + sw.y * wp) * SWAY_K * 2.0) / stiff;
                // the hero presses the blades aside
                float push = 0;
                for (int k = 0; k < 4; k++)
                {
                    float4 P = _AsciiPush[k]; if (P.w < 0.01) continue;
                    float2 d = a.xz - P.xz; float nearK = saturate(1.0 - (P.y - h) / 0.35);
                    float pk = smoothstep(P.w, P.w * 0.2, length(d)) * nearK;
                    lean += normalize(d + 1e-4) * pk * 1.2; push = max(push, pk);
                }
                float ll = length(lean); if (ll > 1.1) lean *= 1.1 / ll;
                // the blade bends from the root (the lesson masks the bend by height): the point at t is pushed by t^2 of
                // the lean and drops so that the blade keeps its length
                float b = t * t;
                float3 p = a + float3(lean.x * b * h, h * t * rsqrt(1.0 + dot(lean, lean) * b), lean.y * b * h);

                // seen? (the scene's depth at the root: a blade behind a wall or a body is not drawn)
                float4 rclip = TransformWorldToHClip(a);
                float4 sp = ComputeScreenPos(rclip); float2 suv = sp.xy / sp.w;
                float dz = _AsciiDepthLit > 0.5 ? abs(SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, ascii_point_clamp_sampler, suv, 0).a) : SAMPLE_TEXTURE2D_LOD(_AsciiNDTex, sampler_AsciiNDTex, suv, 0).b;
                float sceneZ = dz > 1e-4 ? dz * 12.0 : 1e5;
                float3 rVS = TransformWorldToView(a);
                float myZ = -rVS.z;
                bool vis = myZ <= sceneZ + 0.3 && all(suv >= 0.0) && all(suv <= 1.0) && frac(s * 7.31) >= _AsciiGrassThin && frac(s * 5.17) >= _AsciiSeason.w * 0.7;

                // the strip: as wide as the stroke of '|' in a character of the blade's height, narrowing to the tip;
                // across the screen (it always faces the camera, as a character does)
                float3 pVS = TransformWorldToView(p);
                float3 nVS = TransformWorldToView(a + float3(lean.x * 2.0 * t * h, h, lean.y * 2.0 * t * h) * 0.1) - TransformWorldToView(a);
                float2 dir = normalize(nVS.xy + float2(0, 1e-4));
                float2 perp = float2(dir.y, -dir.x);
                float4 ink = _GlyphInk[(int)(12.0 * 16.0 + 0.5)];
                float wRoot = h * 0.44 * max(ink.z - ink.x, 0.12) * 1.15;
                pVS.xy += perp * side * wRoot * lerp(1.0, 0.35, t);
                o.uv = float2(lerp(ink.x, ink.z, side + 0.5), lerp(ink.y, ink.w, t));

                // ---- colour ----
                float3 lit = SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, sampler_AsciiLitTex, suv, 0).rgb;     // the ground under the root
                lit = _AsciiLinear > 0.5 ? pow(max(lit, 0.0), 1.0 / 2.2) : max(lit, 0.0);
                float lum = saturate(dot(lit, float3(0.299, 0.587, 0.114)) * 2.6 * _AsciiExposure);
                float3 baseC = SeasonGreen(lerp(_GrassA.rgb, _GrassB.rgb, frac(s * 13.7)), s);
                // depth colour (near / far)
                float nf = smoothstep(NEAR_FAR.x, NEAR_FAR.y, myZ);
                float3 nearC = baseC * float3(1.15, 1.08, 0.75) + float3(0.04, 0.035, 0.0);
                float3 farC = baseC * float3(0.80, 0.93, 1.12);
                float3 topC = lerp(nearC, farC, nf);
                // terrain colour, then the shadow over it
                float3 ground = lit / max(lum, 0.08) * 0.5;            // the ground's hue, without its brightness
                float3 terr = lerp(topC, ground * 0.6 + topC * 0.4, TERRAIN_K);
                float shadowAtt = saturate(lum * 1.8);
                terr = lerp(terr * SHADOW_COL, terr, shadowAtt);
                // height blend: the bottom colour (darker, of the ground) at the root
                float3 bottomC = lerp(baseC * 0.3, ground * 0.45, 0.6);
                float3 c = lerp(bottomC, terr, smoothstep(0.0, HEIGHT_BLEND, t));
                // the light of the scene
                c *= 0.25 + 0.95 * lum;
                // the wind waves light the upper part of the blade
                c += WAVE_LIGHT * wave * smoothstep(0.3, 1.0, t) * (0.35 + lum);
                float depthK = clamp(1.0 - (myZ - _AsciiDepthRef) * 0.09, 0.5, 1.2);
                c *= depthK * (1.0 - 0.3 * push);
                float l0 = dot(c, float3(0.3, 0.5, 0.2)); c = max(0.0, lerp(l0.xxx, c, 1.18));        // a little more colour
                o.col = c;
                o.positionCS = vis ? TransformWViewToHClip(pVS) : float4(2, 2, 2, 1);
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                float rows = max(_AsciiRows, 1.0);
                float2 l = clamp(i.uv, 0.02, 0.98);
                float mk = SAMPLE_TEXTURE2D_BIAS(_AsciiAtlas, sampler_AsciiAtlas, float2(l.x / 16.0, 1.0 - 13.0 / rows + l.y / rows), -0.6).r;   // row 12, '|'
                mk = saturate((mk - 0.22) * 1.9);
                return half4(i.col * mk, 1);
            }
            ENDHLSL
        }
    }
}
