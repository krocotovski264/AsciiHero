// Rain and snow as characters in the world (not a layer over the screen): each drop or flake has its own place in
// world space and falls through a box around what the camera looks at. When the box moves with the camera, a drop
// that leaves it on one side comes back in on the other (its place is wrapped, not carried), so the rain does not
// follow the hero: he runs through it. Drops are hidden behind whatever stands in front of them (the scene's depth).
//   rain - a streak of three characters along its fall (| / \ by its direction on the screen), fading upward; at the
//          bottom of its fall a splash (, .)
//   snow - one flake (* near, . far), drifting with the wind and swaying
// Per quad: positionOS = random 0..1 in the box, uv0 = corner, uv1 = (seed, trail index 0..2)
Shader "AsciiHero/Precip"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Precip"
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
            TEXTURE2D(_AsciiLitTex); TEXTURE2D(_AsciiNDTex); SAMPLER(sampler_AsciiNDTex);
            SAMPLER(ascii_point_clamp_sampler); float _AsciiDepthLit;
            float _AsciiRows, _AsciiTime; float4 _GlyphInk[224];
            float4 _AsciiPrecipW;       // x rain 0..1, y snow 0..1, z daylight 0..1
            float4 _PrecipBox;          // xyz = centre (ground under what the camera looks at), w = width
            float4 _AsciiWind;          // xy = direction (world xz), z = strength

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float2 seed : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float3 col : TEXCOORD1; float2 glyph : TEXCOORD2; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float t = _AsciiTime, s = i.seed.x, trail = i.seed.y;
                float3 u = i.positionOS.xyz;
                float rainA = _AsciiPrecipW.x, snowA = _AsciiPrecipW.y;
                bool snow = snowA > rainA;
                float amount = max(rainA, snowA);
                float S = _PrecipBox.w, Hh = 11.0;
                float2 wd = _AsciiWind.xy * (0.4 + 1.6 * _AsciiWind.z);
                float bright = step(s, amount) * (snow ? step(trail, 0.5) : 1.0);
                float3 p = 0, vel = 0; float row = 1.0, gi = 10.0;
                if (!snow)
                {
                    float sp = 7.5 + 2.5 * frac(s * 17.3);
                    vel = float3(wd.x * 1.8, -sp, wd.y * 1.8);
                    float fy = frac(u.y - t * sp / Hh);
                    p.xz = u.xz * S + vel.xz * (t * 0.25) - vel.xz / sp * trail * 0.2;
                    p.y = fy * Hh + trail * 0.2;                    // the trail: the characters above and behind the head
                    bright *= (1.0 - 0.32 * trail) * (0.55 + 0.45 * frac(s * 7.1));
                    if (fy * Hh < 0.15 && trail < 0.5) { row = 1.0; gi = frac(s * 3.3 + floor(t * 3.0) * 0.17) > 0.5 ? 4.0 : 3.0; p.y = 0.04; }   // the splash: , '
                    else if (fy * Hh < 0.15) bright = 0;
                }
                else
                {
                    float sp = 0.7 + 0.5 * frac(s * 9.1);
                    float fy = frac(u.y - t * sp / Hh);
                    p = float3(u.x * S, fy * Hh, u.z * S);
                    p.xz += wd * t * 0.6 + float2(sin(t * (0.8 + frac(s * 5.3)) + s * 40.0), cos(t * (0.7 + frac(s * 3.7)) + s * 23.0)) * 0.25;
                    vel = float3(wd.x, -sp, wd.y);
                    row = frac(s * 11.3) > 0.55 ? 9.0 : 1.0; gi = row > 5.0 ? 2.0 : 1.0;      // * .
                    bright *= 0.65 + 0.35 * frac(s * 5.9);
                    bright *= smoothstep(0.0, 0.25, p.y);
                }
                // wrap round the box (each drop keeps its place in the world while the box moves)
                float2 b0 = _PrecipBox.xz - S * 0.5;
                p.xz = b0 + frac((p.xz - b0) / S) * S;
                p.y += _PrecipBox.y;
                // fades out toward the box's sides (no edge where drops pop in)
                float2 e = abs(p.xz - _PrecipBox.xz) / (S * 0.5);
                bright *= saturate((1.0 - max(e.x, e.y)) * 5.0);
                float3 pVS = TransformWorldToView(p);
                if (!snow && p.y > 0.2)
                {   // the streak's character by its direction on the screen
                    float2 sd = TransformWorldToViewDir(normalize(vel)).xy; float ang = atan2(sd.y, sd.x);
                    float a8 = frac(ang / 3.14159 + 1.0);
                    row = 12.0; gi = a8 < 0.125 || a8 >= 0.875 ? 1.0 : a8 < 0.375 ? 2.0 : a8 < 0.625 ? 0.0 : 3.0;     // - / | \ (row 12)
                }
                float4 clip = TransformWViewToHClip(pVS);
                float4 spos = ComputeScreenPos(clip); float2 suv = spos.xy / spos.w;
                float dz = _AsciiDepthLit > 0.5 ? abs(SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, ascii_point_clamp_sampler, suv, 0).a) : SAMPLE_TEXTURE2D_LOD(_AsciiNDTex, sampler_AsciiNDTex, suv, 0).b;
                float sceneZ = dz > 1e-4 ? dz * 12.0 : 1e5;
                bool vis = -pVS.z > 0.3 && -pVS.z <= sceneZ + 0.1 && all(suv >= 0.0) && all(suv <= 1.0) && bright > 0.01;
                float3 colr = snow ? float3(0.85, 0.89, 0.98) : float3(0.56, 0.64, 0.8);
                o.col = colr * bright * (0.75 + 0.35 * _AsciiPrecipW.z);
                o.glyph = float2(row, gi);
                float4 ink = _GlyphInk[(int)(row * 16.0 + gi + 0.5)];
                float2 lc = lerp(ink.xy, ink.zw, i.corner + 0.5);
                o.local = lc;
                float sz = snow ? 0.09 : 0.12;
                pVS.xy += (lc - 0.5) * float2(sz, sz * 1.65);
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
