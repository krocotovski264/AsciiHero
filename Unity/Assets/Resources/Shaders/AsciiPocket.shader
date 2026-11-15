// The shadow pocket: the hero's menu is a pocket of his own shadow torn open in the middle of the screen. It is drawn
// as ASCII smoke (the smoke row of the atlas, the characters of the dash's puffs): a billowing oval (a third of the
// screen) with a darker hollow inside for the windows, and puffs breaking off its rim, drifting out and thinning
// away. Opening: the smoke gathers from the middle (_PT: 0..0.35 s); closing (_PClose 0..1): it blows apart.
// Outside the oval nothing is drawn - the game goes on behind it.
Shader "Hidden/AsciiHero/Pocket"
{
    Properties { _MainTex ("unused", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Overlay" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _AsciiAtlas; float _AsciiRows;
            float _PT, _PClose;          // seconds since it opened; 0..1 how far it has blown apart (0 = open)
            float4 _PCenter;             // centre (px, y down), screen size (px)
            float4 _PRad;                // the oval's half axes (px), the cell (px)
            float H1(float n) { return frac(sin(n * 127.1) * 43758.5453); }
            float H2(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float N2(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(H2(i), H2(i + float2(1, 0)), u.x), lerp(H2(i + float2(0, 1)), H2(i + float2(1, 1)), u.x), u.y);
            }
            float Fbm(float2 p) { return N2(p) * 0.55 + N2(p * 2.1 + 7.3) * 0.3 + N2(p * 4.3 - 3.1) * 0.15; }
            float4 frag (v2f_img i) : SV_Target
            {
                float2 scr = _PCenter.zw, px = float2(i.uv.x, 1.0 - i.uv.y) * scr;
                float2 cellSz = _PRad.zw;
                float2 cell = floor(px / cellSz), c = (cell + 0.5) * cellSz;
                float2 q = (c - _PCenter.xy) / _PRad.xy;
                float r = length(q), ang = atan2(q.y, q.x), t = _PT;
                // opening: the smoke gathers outwards from the middle (a little past the rim, then back); closing: it swells
                // and thins out
                float g = smoothstep(0.0, 0.35, t); g = g * (1.0 + 0.08 * sin(saturate(t / 0.6) * 3.14159));
                float cl = saturate(_PClose), fade = 1.0 - cl;
                float scale = max(g, 0.02) * (1.0 + 0.45 * cl);
                float2 qs = q / scale; float rs = length(qs);
                // the rim: billows rolling round it (noise along the angle, moving), the outline never quite an oval
                float2 rimN = float2(cos(ang), sin(ang)) * 2.2;
                float wob = Fbm(rimN + float2(t * 0.35, -t * 0.27)) - 0.5;
                float rimR = 0.9 + 0.16 * wob;
                float bill = Fbm(qs * 3.0 + float2(t * 0.5, t * 0.21)) ;
                float rim = exp(-pow((rs - rimR) / (0.1 + 0.06 * bill), 2.0)) * (0.55 + 0.7 * bill);
                // inside: a dark hollow with slow faint swirls (the windows sit on it)
                float inside = smoothstep(rimR + 0.02, rimR - 0.18, rs);
                float2 sw = float2(qs.x * cos(t * 0.05) - qs.y * sin(t * 0.05), qs.x * sin(t * 0.05) + qs.y * cos(t * 0.05));
                float body = inside * (0.06 + 0.22 * smoothstep(0.45, 0.85, Fbm(sw * 2.4 + t * 0.08)));
                float dens = max(rim, body);
                // puffs breaking off the rim: born on it, drifting out, growing and thinning away (as the dash's smoke)
                float puffs = 0;
                [loop] for (int k = 0; k < 20; k++)
                {
                    float h1 = H1(k + 1.3), h2 = H1(k * 3.7 + 9.1), L = 0.8 + 0.7 * h2;
                    float ph = frac(t / L + h1);
                    float a = h1 * 6.2832 + 0.4 * (h2 - 0.5) * ph;
                    float2 dir = float2(cos(a), sin(a));
                    float2 P = dir * (0.95 + (0.35 + 0.45 * h2) * ph) * scale;
                    float sz = (0.06 + 0.1 * ph + 0.06 * cl) * min(_PRad.x, _PRad.y);
                    float d = length((q - P) * _PRad.xy) / sz;
                    float pf = exp(-d * d * 1.6) * pow(1.0 - ph, 1.4) * (0.6 + 0.6 * N2(c / sz * 1.3 + k));
                    puffs = max(puffs, pf);
                }
                dens = max(dens, puffs * (t > 0.2 ? 1.0 : t / 0.2));
                dens *= saturate(g * 1.3) * fade;
                // closing: the hollow empties first
                float bgA = inside * 0.82 * saturate(g * 1.6) * (1.0 - smoothstep(0.0, 0.5, cl));
                // the character: the smoke row of the atlas, denser where the smoke is thicker
                float gi = floor(saturate(dens) * 15.99);
                float2 l = clamp(frac(px / cellSz), 0.04, 0.96); l.y = 1.0 - l.y;
                float rows = max(_AsciiRows, 1.0);
                float ink = tex2D(_AsciiAtlas, float2((gi + l.x) / 16.0, 1.0 - 14.0 / rows + l.y / rows)).r;
                ink = saturate((ink - 0.2) * 1.8) * (gi > 0.5 ? 1.0 : 0.0);
                float3 smoke = lerp(float3(0.16, 0.13, 0.22), float3(0.62, 0.58, 0.74), saturate(dens));
                float3 bg = float3(0.012, 0.01, 0.02);
                float a2 = saturate(max(bgA, ink * saturate(dens * 2.5)));
                float3 col = lerp(bg, smoke, ink * saturate(dens * 2.5) / max(a2, 1e-3));
                return float4(col, a2);
            }
            ENDCG
        }
    }
}
