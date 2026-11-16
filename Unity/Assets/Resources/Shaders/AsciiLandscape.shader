// The main menu's background: a night landscape in characters, alive. From the top: a night sky with twinkling stars,
// drifting clouds and the moon; far mountains with snow on their peaks; hills of forest swaying, fireflies over them; a
// lake with the moon's path shimmering on it; grass in the foreground bending in waves of wind. Each layer moves at
// its own pace (the far ones slower), so the picture breathes.
Shader "Hidden/AsciiHero/Landscape"
{
    Properties { _MainTex ("unused", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _AsciiAtlas; float _AsciiRows;
            float4 _LScreen;     // screen w, h (px); cell w, h (px)
            float _LT;
            float H1(float n) { return frac(sin(n * 127.1) * 43758.5453); }
            float H2(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float N1(float x) { float i = floor(x), f = frac(x); f = f * f * (3 - 2 * f); return lerp(H1(i), H1(i + 1), f); }
            float F1(float x) { return N1(x) * 0.55 + N1(x * 2.3 + 7.1) * 0.3 + N1(x * 5.1 + 3.3) * 0.15; }
            float N2(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(H2(i), H2(i + float2(1, 0)), u.x), lerp(H2(i + float2(0, 1)), H2(i + float2(1, 1)), u.x), u.y);
            }
            float4 frag (v2f_img i) : SV_Target
            {
                float2 scr = _LScreen.xy, cs = _LScreen.zw;
                float2 px = i.uv * scr, cell = floor(px / cs), cc = (cell + 0.5) * cs;
                float asp = scr.x / scr.y, x = cc.x / scr.y, y = cc.y / scr.y;         // (y: 0 at the bottom, 1 at the top)
                float t = _LT, h = H2(cell);
                float2 l = clamp(frac(px / cs), 0.04, 0.96);
                float row = -1, col = 0; float3 c = 0; float b = 0;
                // the sky behind everything: deep blue going violet towards the top
                float3 bg = lerp(float3(0.012, 0.014, 0.03), float3(0.03, 0.02, 0.06), y);
                float2 moon = float2(asp * 0.76, 0.8);
                float hm = 0.43 + 0.17 * F1(x * 1.2 + t * 0.006);                 // the far mountains
                float hh = 0.29 + 0.06 * F1(x * 3.1 + 10.0 + t * 0.014);         // the forest hills
                float wl = 0.19, gl = 0.11 + 0.025 * F1(x * 6.0 + 4.0);           // the lake, the grass
                if (y > hm)
                {
                    float md = length(float2(x, y) - moon);
                    if (md < 0.065) { row = 2; col = md < 0.045 ? 14 : 11; c = float3(1.0, 0.94, 0.78) * (1.1 - md * 6.0); }
                    else
                    {
                        float cl = N2(float2(x * 3.0 - t * 0.03, y * 9.0));          // clouds drifting
                        if (cl > 0.66 && y > 0.6) { row = 1; col = cl > 0.74 ? 6 : 1; c = float3(0.32, 0.3, 0.42) * (cl - 0.5); }
                        else if (h > 0.982)
                        {   // a star, twinkling
                            float tw = 0.5 + 0.5 * sin(t * (1.5 + 3.0 * h) + h * 40.0);
                            row = 2; col = h > 0.996 ? 8 : 1; c = float3(0.75, 0.78, 1.0) * (0.35 + 0.8 * tw);
                        }
                        bg += float3(0.06, 0.05, 0.03) * saturate(0.25 - md) * 2.0;     // the moon's halo
                    }
                }
                else if (y > hh)
                {   // the mountains: peaks and slopes in strokes, snow on top, darker rock below
                    float sl = F1((x + 0.01) * 1.2 + t * 0.006) - F1((x - 0.01) * 1.2 + t * 0.006);
                    float top = hm - y;
                    if (top < 0.022) { row = 1; col = abs(sl) < 0.004 ? 7 : (sl > 0 ? 8 : 9); c = float3(0.85, 0.88, 0.95); }
                    else { row = 2; col = h < 0.4 ? 3 : h < 0.7 ? 11 : 0; c = lerp(float3(0.16, 0.17, 0.26), float3(0.3, 0.32, 0.42), saturate(1.0 - top * 6.0)) * (top < 0.07 ? 1.6 : 1.0); }
                }
                else if (y > wl)
                {   // the forest: crowns swaying, fireflies above it
                    float sw = sin(t * 1.2 + x * 7.0) * 0.004;
                    float d = hh - y;
                    row = 2; col = N2(float2((x + sw) * 40.0, y * 30.0)) > 0.45 ? 13 : 12;
                    c = lerp(float3(0.05, 0.16, 0.08), float3(0.12, 0.32, 0.14), saturate(1.0 - d * 9.0));
                    float ff = H2(floor(float2(x * 60.0 + sin(t * 0.7 + h * 6.0) * 2.0, y * 40.0 + t * 0.3)));
                    if (ff > 0.993 && d < 0.05) { col = 1; c = float3(0.75, 1.0, 0.4) * (0.6 + 0.4 * sin(t * 4.0 + h * 20.0)); }
                }
                else if (y > gl)
                {   // the lake: ripples, the moon's path shimmering on it, the hills mirrored darkly
                    float wv = sin(x * 30.0 + t * 1.6 + y * 80.0) * 0.5 + 0.5;
                    row = 1; col = wv > 0.55 ? 6 : 2;
                    c = float3(0.08, 0.12, 0.26) * (0.6 + 0.6 * wv);
                    float mp = abs(x - moon.x) - 0.03 * (wl - y) * 6.0;
                    if (mp < 0.02 + 0.02 * N2(float2(y * 80.0, t * 2.0))) { row = 2; col = wv > 0.5 ? 6 : 5; c = float3(1.0, 0.92, 0.7) * (0.5 + 0.6 * wv); }
                }
                else
                {   // the grass: blades bending as waves of wind run along
                    float wind = sin(x * 9.0 - t * 2.2) * 0.5 + 0.5 + 0.3 * sin(x * 23.0 - t * 3.7);
                    row = 12; col = wind > 0.8 ? 2 : wind < 0.25 ? 3 : 0;
                    c = lerp(float3(0.1, 0.3, 0.12), float3(0.28, 0.55, 0.22), saturate(wind)) * (0.6 + 0.6 * h);
                }
                float3 o = bg;
                if (row >= 0)
                {
                    float rows = max(_AsciiRows, 1.0);
                    float ink = tex2D(_AsciiAtlas, float2((col + l.x) / 16.0, 1.0 - (row + 1.0) / rows + l.y / rows)).r;
                    ink = saturate((ink - 0.2) * 1.8);
                    o = lerp(bg, c, ink);
                }
                return float4(o, 1);
            }
            ENDCG
        }
    }
}
