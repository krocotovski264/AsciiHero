// Small moving characters that are not surfaces: one camera-facing quad per point.
//  _MoteMode 0 - dust in a beam of light: the points fill the beam (local space: a unit column along +y, radius 1;
//                seed.x = distance from the axis 0..1), drift slowly and glint; they fade towards the beam's edge
//  _MoteMode 1 - fireflies: each wanders around its point and blinks now and then
//  _MoteMode 2 - drops falling from the ceiling: each falls from _MoteParams.y to its point and is gone; one drop per
//                _MoteParams.z seconds per point, the phase from seed.x (the game makes the ring on the water at the
//                same moment)
//  _MoteMode 4 - falling leaves: each drifts down from the top of its column (local y 0..1) swaying side to side,
//                turning (, ` ' ~), and starts again from the top
//  _MoteMode 3 - the shaft of a beam: faint streaks along the beam (/ | \ - by its direction on the screen), brightest
//                on its axis, shimmering slowly as if dust drifted through it
Shader "AsciiHero/Motes"
{
    Properties
    {
        _MoteMode ("mode", Float) = 0
        _MoteColor ("colour", Color) = (1, 0.95, 0.8, 1)
        _MoteParams ("glyph size, drop height, drop period, wander", Vector) = (0.05, 2.8, 2.0, 0.6)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Motes"
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
                float _MoteMode; float4 _MoteColor; float4 _MoteParams;
            CBUFFER_END
            TEXTURE2D(_AsciiAtlas); SAMPLER(sampler_AsciiAtlas);
            TEXTURE2D(_AsciiLitTex); TEXTURE2D(_AsciiNDTex); SAMPLER(sampler_AsciiNDTex);
            SAMPLER(ascii_point_clamp_sampler); float _AsciiDepthLit;
            float _AsciiRows, _AsciiTime; float4 _GlyphInk[224];

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float2 seed : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float3 col : TEXCOORD1; float2 glyph : TEXCOORD2; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float t = _AsciiTime, s = i.seed.y, k = i.seed.x;
                float3 lp = i.positionOS.xyz; float bright = 1.0; float row = 9.0, gi = 1.0;
                if (_MoteMode < 0.5)
                {   // dust in a beam
                    lp.y = frac(lp.y + t * (0.01 + 0.02 * frac(s * 7.3)) * (frac(s * 3.1) > 0.5 ? 1.0 : -1.0));
                    lp.x += sin(t * 0.4 + s * 31.0) * 0.08; lp.z += cos(t * 0.33 + s * 17.0) * 0.08;
                    float glint = 0.55 + 0.45 * sin(t * (1.0 + 2.0 * frac(s * 5.7)) + s * 40.0);
                    bright = (1.0 - k * k) * glint * smoothstep(0.0, 0.12, lp.y) * smoothstep(1.0, 0.85, lp.y);
                    row = 1.0; gi = frac(s * 9.1) > 0.7 ? 3.0 : (frac(s * 9.1) > 0.35 ? 2.0 : 1.0);    // . ` '
                }
                else if (_MoteMode < 1.5)
                {   // fireflies
                    float w = _MoteParams.w;
                    lp += float3(sin(t * 0.37 + s * 11.0) + 0.5 * sin(t * 0.91 + s * 3.0), 0.5 * sin(t * 0.53 + s * 7.0), cos(t * 0.29 + s * 13.0) + 0.5 * cos(t * 0.77 + s * 5.0)) * w;
                    float ph = frac(t / (2.2 + 2.0 * frac(s * 4.3)) + s * 9.7);
                    bright = pow(saturate(sin(ph * 3.14159)), 6.0) * 0.9 + 0.08;
                    row = 9.0; gi = bright > 0.5 ? 2.0 : 1.0;          // * .
                }
                else if (_MoteMode > 3.5)
                {   // a falling leaf
                    float P = 7.0 + 6.0 * frac(s * 3.7), ph = frac(t / P + k);
                    lp.y = 1.0 - ph;
                    float sw = sin(t * (1.1 + frac(s * 5.3)) + s * 17.0);
                    lp.x += sw * 0.35 + ph * 0.6 * (frac(s * 2.9) - 0.5); lp.z += cos(t * 0.9 + s * 9.0) * 0.25;
                    bright = smoothstep(0.0, 0.08, ph) * smoothstep(1.0, 0.92, ph) * (0.6 + 0.4 * frac(s * 7.7));
                    row = 1.0; gi = sw > 0.5 ? 4.0 : sw > -0.2 ? 3.0 : sw > -0.7 ? 2.0 : 6.0;      // , ' ` ~
                }
                else if (_MoteMode > 2.5)
                {   // the shaft of a beam
                    float3 ax = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                    float2 sd = TransformWorldToViewDir(ax).xy; float ang = atan2(sd.y, sd.x);            // the beam on the screen
                    float a8 = frac(ang / 3.14159 + 1.0);                                                  // 0..1 over half a turn
                    gi = a8 < 0.125 || a8 >= 0.875 ? 1.0 : a8 < 0.375 ? 2.0 : a8 < 0.625 ? 0.0 : 3.0;     // - / | \ (row 12)
                    row = 12.0;
                    float shimmer = 0.75 + 0.25 * sin(t * (0.6 + frac(s * 3.3)) + s * 23.0 + lp.y * 9.0);
                    bright = pow(saturate(1.0 - k), 1.6) * shimmer * smoothstep(0.0, 0.18, lp.y) * smoothstep(1.0, 0.7, lp.y) * (0.55 + 0.45 * lp.y);
                }
                else
                {   // a falling drop
                    float P = max(_MoteParams.z, 0.3), ph = frac(t / P + k);
                    float fall = 0.55, u = saturate(ph * P / fall);        // it falls in a little over half a second
                    float top = _MoteParams.y;
                    lp.y = lerp(top, 0.0, u * u);
                    bright = ph * P < fall ? 1.0 : 0.0;
                    row = 1.0; gi = 10.0;                                // |
                }
                float3 p = TransformObjectToWorld(lp);
                float3 pVS = TransformWorldToView(p);
                float4 clip = TransformWViewToHClip(pVS);
                float4 sp = ComputeScreenPos(clip); float2 suv = sp.xy / sp.w;
                float dz = _AsciiDepthLit > 0.5 ? abs(SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, ascii_point_clamp_sampler, suv, 0).a) : SAMPLE_TEXTURE2D_LOD(_AsciiNDTex, sampler_AsciiNDTex, suv, 0).b;
                float sceneZ = dz > 1e-4 ? dz * 12.0 : 1e5;
                bool vis = -pVS.z <= sceneZ + 0.05 && all(suv >= 0.0) && all(suv <= 1.0) && bright > 0.01;
                o.col = _MoteColor.rgb * bright;
                o.glyph = float2(row, gi);
                float4 ink = _GlyphInk[(int)(row * 16.0 + gi + 0.5)];
                float2 lc = lerp(ink.xy, ink.zw, i.corner + 0.5);
                o.local = lc;
                float sz = _MoteParams.x;
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
