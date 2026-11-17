// glyphs + bloom + dark vignette background
Shader "Hidden/AsciiHero/Final"
{
    Properties { _MainTex ("Glyphs", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _BloomTex, _AsciiLitTex; float _Bloom, _Linear, _Outline; float4 _LitPx;
            // retyping (a door between places): the old picture, how far the front has run, where from, the glyph grid
            sampler2D _TransTex, _AsciiAtlas; float _TransT, _AsciiRows, _AsciiTime; float4 _TransO, _TransGrid;
            // the sky: the tint of the time of day and the weather (a: saturation), the rain and the snow over the screen
            float4 _AsciiGrade, _AsciiPrecip, _AsciiPrecipWind, _GlyphGrid; float _WeatherOn, _AsciiDayK;
            // hashes without sin() (its precision on phones turns big arguments into noise)
            float H1(float n) { n = frac(n * 0.1031); n *= n + 33.33; n *= n + n; return frac(n); }
            float H2(float2 p) { float3 q = frac(float3(p.xyx) * 0.1031); q += dot(q, q.yzx + 33.33); return frac((q.x + q.y) * q.z); }
            float Glyph(float row, float g, float2 lc)
            {
                float rows = max(_AsciiRows, 1.0);
                return tex2Dlod(_AsciiAtlas, float4((g + clamp(lc.x, 0.04, 0.96)) / 16.0, 1.0 - (row + 1.0) / rows + clamp(lc.y, 0.04, 0.96) / rows, 0, 0)).r;
            }
            // rain: streaks of three depths fall down columns of the glyph grid, the columns leaning with the wind
            // (a lean draws '/' or '\'); drops splash ',' on the lower half. Snow: flakes ('*' near, '.' far) drifting
            // down, swaying. x = the glyph's ink (0..1), yzw = its colour
            float4 Precip(float2 uv)
            {
                float2 grid = max(_GlyphGrid.xy, float2(8, 8));
                float2 gp = uv * grid, cell = floor(gp), lc = frac(gp);
                float t = _AsciiTime, R = grid.y; float4 o = 0;
                float rainA = _AsciiPrecip.x, snowA = _AsciiPrecip.y, sl = clamp(_AsciiPrecipWind.x, -0.8, 0.8);
                if (rainA > 0.01)
                {
                    float gch = abs(sl) < 0.18 ? 10.0 : (sl > 0.0 ? 9.0 : 8.0);         // | \ /
                    [unroll] for (int L = 0; L < 3; L++)
                    {
                        float lean = sl * (0.7 + 0.25 * L);
                        float id = cell.x + floor(cell.y * lean + 0.5) + 517.0 * L;
                        float spd = R * (0.75 + 0.35 * L) * (0.85 + 0.3 * rainA), len = 2.0 + L + floor(rainA * 2.0);
                        float cyc = R * (0.6 + 0.5 * H1(id * 3.7 + 11.0));
                        float pos = fmod(t, 600.0) * spd + H1(id * 1.7 + 5.0) * cyc;
                        float n = floor(pos / cyc), head = R - (pos - n * cyc) * (R + len + 2.0) / cyc + len;
                        float on = step(H1(id * 0.731 + n * 13.1 + 3.0), rainA * (0.28 + 0.14 * L));
                        float d = cell.y - floor(head);
                        if (on > 0.0 && d >= 0.0 && d < len)
                        {
                            float b = (1.0 - 0.6 * d / len) * (0.45 + 0.2 * L);
                            float m = Glyph(1.0, gch, lc) * b;
                            if (m > o.x) o = float4(m, float3(0.62, 0.7, 0.86) * (0.7 + 0.3 * L));
                        }
                    }
                    if (cell.y < R * 0.5)
                    {
                        float tb = floor(t * 7.0 + H2(cell) * 5.0);
                        if (H2(cell + tb * 0.37) < rainA * 0.03)
                        {
                            float m = Glyph(1.0, H2(cell * 1.3 + tb) > 0.5 ? 4.0 : 3.0, lc) * 0.6;
                            if (m > o.x) o = float4(m, 0.66, 0.74, 0.88);
                        }
                    }
                }
                if (snowA > 0.01)
                {
                    [unroll] for (int L = 0; L < 3; L++)
                    {
                        float sw = sin(cell.y * 0.31 + t * (0.7 + 0.2 * L) + L * 2.1) * (1.0 + 0.4 * L) + sl * cell.y * 0.4;
                        float id = cell.x + floor(sw + 0.5) + 313.0 * L;
                        float spd = R * (0.1 + 0.05 * L), cyc = R * (0.5 + 0.4 * H1(id * 2.3 + 7.0));
                        float pos = fmod(t, 600.0) * spd + H1(id * 1.3 + 2.0) * cyc;
                        float n = floor(pos / cyc), head = R - (pos - n * cyc) * (R + 2.0) / cyc + 1.0;
                        float on = step(H1(id * 0.917 + n * 7.7 + 9.0), snowA * (0.3 + 0.15 * L));
                        if (on > 0.0 && abs(cell.y - floor(head)) < 0.5)
                        {
                            float m = (L == 2 ? Glyph(9.0, 2.0, lc) : Glyph(1.0, 1.0, lc)) * (0.55 + 0.2 * L);
                            if (m > o.x) o = float4(m, 0.9, 0.93, 1.0);
                        }
                    }
                }
                return o;
            }
            float4 frag (v2f_img i) : SV_Target
            {
                float3 a = tex2D(_MainTex, i.uv).rgb, b = tex2D(_BloomTex, i.uv).rgb;
                float3 bg = lerp(float3(0.018, 0.02, 0.032), float3(0.05, 0.055, 0.08), smoothstep(1.1, 0.0, length(i.uv - float2(0.5, 0.55)) * 1.6));
                // outline: a thin line along the edges of objects and creatures. The lit colour's alpha holds the depth
                // (negative on walls and floors, 0 = nothing): an object's pixel next to the background, a wall, the
                // floor or something clearly further away is on its edge. The line takes the object's own hue.
                if (_Outline > 0.5)
                {
                    float2 uvc = (floor(i.uv / _LitPx.xy) + 0.5) * _LitPx.xy;
                    float4 cc = tex2Dlod(_AsciiLitTex, float4(uvc, 0, 0));
                    if (cc.a > 1e-4)
                    {
                        float thr = cc.a + 0.02 + 0.015 * cc.a;
                        // neighbours 1.5 texels away from this pixel (each snapped to its texel's centre): the line is
                        // one and a half texels wide on the object's side of the edge
                        float2 ox = float2(_LitPx.x * 1.5, 0), oy = float2(0, _LitPx.y * 1.5);
                        #define SNAP(u) float4((floor((u) / _LitPx.xy) + 0.5) * _LitPx.xy, 0, 0)
                        float n1 = tex2Dlod(_AsciiLitTex, SNAP(i.uv + ox)).a, n2 = tex2Dlod(_AsciiLitTex, SNAP(i.uv - ox)).a;
                        float n3 = tex2Dlod(_AsciiLitTex, SNAP(i.uv + oy)).a, n4 = tex2Dlod(_AsciiLitTex, SNAP(i.uv - oy)).a;
                        float lo = min(min(n1, n2), min(n3, n4)), hi = max(max(n1, n2), max(n3, n4));
                        if (lo <= 1e-4 || hi > thr)
                        {
                            float3 oc = max(cc.rgb, 0); if (_Linear > 0.5) oc = pow(oc, 1.0 / 2.2);
                            float m = max(oc.r, max(oc.g, oc.b));
                            oc = lerp(float3(0.62, 0.6, 0.7), oc / max(m, 1e-3), saturate(m * 5.0)) * 0.6;
                            a = max(a, oc);
                        }
                    }
                }
                if (_WeatherOn > 0.5)
                {
                    float la = dot(a, float3(0.3, 0.5, 0.2));
                    a = lerp(la.xxx, a, _AsciiGrade.w) * _AsciiGrade.rgb;
                    bg = lerp(bg, float3(0.05, 0.06, 0.08), _AsciiDayK * 0.6) + _AsciiPrecip.w * float3(0.12, 0.13, 0.16);
                    float4 pr = Precip(i.uv);
                    a = lerp(a, pr.yzw, saturate(pr.x * 1.6));          // over the picture (it shows on a bright day too)
                }
                float3 c = bg + a + b * _Bloom * 1.6;
                // values are display-referred; in a Linear project convert so the sRGB output shows them unchanged
                if (_Linear > 0.5) c = pow(max(c, 0), 2.2);
                if (_TransT < 1.6)
                {
                    float d = length((i.uv - _TransO.xy) * float2(_TransO.z, 1.0)) / max(_TransO.w, 1e-3);
                    const float BAND = 0.17;
                    float3 old = tex2D(_TransTex, i.uv).rgb;
                    if (d > _TransT + BAND) c = old;
                    else if (d > _TransT)
                    {
                        // the band: every cell flicks through characters, in the colour of what is there, a little green
                        float2 cell = floor(i.uv * _TransGrid.xy), lc = frac(i.uv * _TransGrid.xy);
                        float h = frac(sin(dot(cell + floor(_AsciiTime * 26.0) * float2(3.1, 7.7), float2(12.9898, 78.233))) * 43758.5453);
                        float g = 2.0 + floor(h * 13.99), rows = max(_AsciiRows, 1.0);
                        float mk = tex2D(_AsciiAtlas, float2((g + clamp(lc.x, 0.05, 0.95)) / 16.0, 1.0 - 3.0 / rows + clamp(lc.y, 0.05, 0.95) / rows)).r;
                        float k = (d - _TransT) / BAND;
                        float3 base = lerp(c, old, k);
                        float lum = max(dot(base, float3(0.3, 0.5, 0.2)), 0.06);
                        float3 tint = lerp(base / max(max(base.r, max(base.g, base.b)), 1e-3), float3(0.45, 1.0, 0.6), 0.55) * (0.25 + 1.6 * lum);
                        c = lerp(base * 0.2, tint, saturate(mk * 1.8 - 0.3));
                    }
                }
                return float4(c, 1);
            }
            ENDCG
        }
    }
}
