// A piece of the interface in the hero's shadow-smoke: a button, a panel, a row. Drawn into the rectangle given (with
// room round it for the smoke): a black body whose edge rolls and billows, a thin cool outline, smoke characters
// drifting in it near the edge, violet flecks of characters coming off it. _SBOn: the button is on (a violet glow on
// its rim); _SBBurst 0..1: it was just pressed - the smoke bursts off its edge and settles.
Shader "Hidden/AsciiHero/SmokeBox"
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
            float4 _SBRect;     // the drawn rectangle (px): w, h; the body's half size inside it (px): hw, hh
            float4 _SBCell;     // character cell (px) w, h; outline (px); seed
            float _SBT, _SBOn, _SBBurst, _SBAlpha, _SBFill, _SBSz, _SBRad;
            float H2(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float N2(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(H2(i), H2(i + float2(1, 0)), u.x), lerp(H2(i + float2(0, 1)), H2(i + float2(1, 1)), u.x), u.y);
            }
            float4 frag (v2f_img i) : SV_Target
            {
                float2 px = float2(i.uv.x, 1.0 - i.uv.y) * _SBRect.xy, c = _SBRect.xy * 0.5, d = px - c;
                float2 hs = _SBRect.zw; float rad = min(min(hs.x, hs.y) * 0.9, _SBRad);
                // a rounded box whose edge rolls (noise round the edge - taken on a circle, so it closes on itself with no seam)
                float2 q = abs(d) - (hs - rad); float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - rad;
                float2 e = normalize(d / max(hs, 1.0) + 1e-5);
                float per = clamp((hs.x + hs.y) * 0.012, 1.2, 6.0);
                float t = _SBT, seed = _SBCell.w, burst = _SBBurst, sz = min(hs.y, _SBSz);
                float wob = (N2(e * per + float2(t * 0.45, seed)) - 0.5) * 0.9 + (N2(e * per * 2.7 + float2(-t * 0.8, seed + 7.0)) - 0.5) * 0.5;
                sd -= wob * sz * (0.35 + 0.8 * burst);
                // billows: round puffs sitting on the edge, swelling and settling
                float k2s = sz * 0.35;
                [unroll] for (int b = 0; b < 8; b++)
                {
                    float h1 = frac(sin((b + 1.3) * 91.7 + seed) * 4375.85), h2 = frac(sin((b + 7.1) * 47.3 + seed) * 2375.17);
                    float a = h1 * 6.2832 + t * 0.06 * (h2 - 0.5);
                    float ph = frac(t / (1.6 + 1.4 * h2) + h1), swl = sin(ph * 3.14159);
                    float2 P = float2(cos(a), sin(a)) * (hs + sz * 0.1);
                    float r = sz * (0.22 + 0.25 * swl);
                    float pd = length(d - P) - r;
                    float hh = saturate(0.5 + 0.5 * (pd - sd) / k2s); sd = lerp(pd, sd, hh) - k2s * hh * (1.0 - hh);
                }
                // puffs breaking off the edge and drifting away, shrinking; a press throws many more, faster and further
                float fly = 1e5, flyK = 0;
                [unroll] for (int f = 0; f < 10; f++)
                {
                    float h1 = frac(sin((f + 2.9) * 63.1 + seed) * 5375.3), h2 = frac(sin((f + 5.3) * 29.9 + seed) * 7375.9);
                    bool extra = f >= 5;                   // (the second half only while bursting)
                    float L = extra ? 0.45 + 0.3 * h2 : 1.6 + 1.2 * h2;
                    float ph = frac(t / L + h1);
                    float a = h2 * 6.2832 + 0.3 * ph;
                    float2 dir = float2(cos(a), sin(a));
                    float reach = sz * (extra ? 1.6 + 1.6 * h1 : 0.5 + 0.6 * h1) * (1.0 + 1.5 * burst);
                    float2 P = dir * (hs + sz * 0.2) + dir * reach * ph;
                    float r = sz * (extra ? 0.3 : 0.22 + 0.12 * h2) * (1.0 - ph) * (extra ? burst : 1.0);
                    if (r <= 0.3) continue;
                    float pd = length(d - P) - r;
                    fly = min(fly, pd);
                    flyK = max(flyK, saturate(1.0 - pd / (sz * (0.3 + 0.6 * ph))) * ph * (1.0 - ph) * 4.0);
                }
                sd = min(sd, fly);
                // the character cells
                float2 cellSz = _SBCell.xy, cell = floor(px / cellSz);
                float2 l = clamp(frac(px / cellSz), 0.04, 0.96); l.y = 1.0 - l.y;
                float edgeIn = saturate(1.0 + sd / max(8.0, min(hs.x, hs.y) * 0.55));
                float swirl = N2(cell * 0.31 + float2(t * 0.7, -t * 0.5) + seed) * 0.7 + N2(cell * 0.77 - t) * 0.3;
                float dens = sd < 0.0 ? saturate(edgeIn * (0.25 + 0.6 * swirl)) : 0.0;
                // flecks coming off the edge; a burst throws many, further
                float fleck = 0.0;
                if (sd > 0.0)
                {
                    float reach = sz * (1.0 + 2.5 * burst);
                    float hh = H2(cell + floor(t * (9.0 + 20.0 * burst)) * float2(1.7, 3.3) + seed);
                    float k2 = max(saturate(1.0 - sd / reach), saturate(flyK));
                    fleck = hh > 1.0 - (0.14 + 0.4 * burst) * k2 ? (0.35 + 0.65 * k2) : 0.0;
                }
                float gd = max(dens, fleck);
                float gi = floor(saturate(gd) * 15.99), rows = max(_AsciiRows, 1.0);
                float ink = tex2D(_AsciiAtlas, float2((gi + l.x) / 16.0, 1.0 - 14.0 / rows + l.y / rows)).r;
                ink = saturate((ink - 0.2) * 1.8) * (gi > 0.5 ? 1.0 : 0.0);
                float ow = _SBCell.z * (1.0 + _SBOn);
                float edgeL = 1.0 - smoothstep(ow * 0.5, ow * 1.3, abs(sd));
                float inside = 1.0 - smoothstep(-0.8, 0.8, sd);
                float3 black = float3(0.006, 0.004, 0.011);
                float3 glyphC = lerp(float3(0.12, 0.1, 0.17), float3(0.42, 0.38, 0.52), gd);
                float3 violet = lerp(float3(0.36, 0.16, 0.6), float3(0.78, 0.5, 1.0), saturate(fleck));
                float3 lineC = lerp(float3(0.46, 0.42, 0.58), float3(0.8, 0.55, 1.0), saturate(_SBOn + burst));
                float3 col = lerp(black + float3(0.05, 0.02, 0.08) * _SBOn, sd > 0.0 ? violet : glyphC, ink);
                col = lerp(col, lineC, edgeL);
                float aIn = inside * _SBFill;
                float al = saturate(max(aIn, max(edgeL, ink * saturate(fleck * 1.8 + dens))));
                return float4(col, al * _SBAlpha);
            }
            ENDCG
        }
    }
}
