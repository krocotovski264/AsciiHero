// The interface's smoke, pass 2 (on the screen, under the interface's text): from the distance field of pass 1 -
// a black body, a thin cool outline, smoke characters drifting in it near the edge, violet flecks of characters
// coming off the edge and off the breaking-off puffs; a piece that is on glows violet on its rim.
Shader "Hidden/AsciiHero/SmokeComp"
{
    Properties { _MainTex ("distance field", 2D) = "white" {} }
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
            sampler2D _MainTex, _AsciiAtlas; float _AsciiRows;
            float4 _SCScreen;       // screen w, h; character cell w, h (px)
            float _SCT, _SCLine, _SCSz;
            float H2(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float N2(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(H2(i), H2(i + float2(1, 0)), u.x), lerp(H2(i + float2(0, 1)), H2(i + float2(1, 1)), u.x), u.y);
            }
            float4 frag (v2f_img i) : SV_Target
            {
                float2 px = float2(i.uv.x, 1.0 - i.uv.y) * _SCScreen.xy;
                float4 s = tex2D(_MainTex, i.uv);
                float sd = s.r;
                if (sd > _SCSz * 3.0 && s.g < 0.01) return 0;
                float2 cellSz = _SCScreen.zw, cell = floor(px / cellSz);
                float2 cuv = (cell + 0.5) * cellSz / _SCScreen.xy; cuv.y = 1.0 - cuv.y;
                float4 sc = tex2Dlod(_MainTex, float4(cuv, 0, 0));
                float csd = sc.r, t = _SCT;
                float2 l = clamp(frac(px / cellSz), 0.04, 0.96); l.y = 1.0 - l.y;
                // smoke characters inside, thicker near the edge; violet flecks just outside and round the puffs
                float edgeIn = saturate(1.0 + csd / max(10.0, _SCSz * 1.4));
                float swirl = N2(cell * 0.31 + float2(t * 0.7, -t * 0.5)) * 0.7 + N2(cell * 0.77 - t) * 0.3;
                float dens = csd < 0.0 ? saturate(edgeIn * (0.25 + 0.6 * swirl)) : 0.0;
                float fleck = 0.0;
                if (csd > 0.0)
                {
                    float hh = H2(cell + floor(t * 10.0) * float2(1.7, 3.3));
                    float k2 = max(saturate(1.0 - csd / (_SCSz * 1.6)), saturate(sc.g));
                    fleck = hh > 1.0 - 0.18 * k2 ? (0.35 + 0.65 * k2) : 0.0;
                }
                float gd = max(dens, fleck);
                float gi = floor(saturate(gd) * 15.99), rows = max(_AsciiRows, 1.0);
                float ink = tex2D(_AsciiAtlas, float2((gi + l.x) / 16.0, 1.0 - 14.0 / rows + l.y / rows)).r;
                ink = saturate((ink - 0.2) * 1.8) * (gi > 0.5 ? 1.0 : 0.0);
                float on = s.b;
                float ow = _SCLine * (1.0 + on);
                float edgeL = 1.0 - smoothstep(ow * 0.5, ow * 1.3, abs(sd));
                float inside = 1.0 - smoothstep(-0.8, 0.8, sd);
                float3 black = float3(0.006, 0.004, 0.011) + float3(0.05, 0.02, 0.08) * on;
                float3 glyphC = lerp(float3(0.12, 0.1, 0.17), float3(0.42, 0.38, 0.52), gd);
                float3 violet = lerp(float3(0.36, 0.16, 0.6), float3(0.78, 0.5, 1.0), saturate(fleck));
                float3 lineC = lerp(float3(0.46, 0.42, 0.58), float3(0.8, 0.55, 1.0), saturate(on));
                float3 col = lerp(black, csd > 0.0 ? violet : glyphC, ink);
                col = lerp(col, lineC, edgeL);
                float a = saturate(max(inside * 0.94, max(edgeL, ink * saturate(fleck * 1.8 + dens))));
                return float4(col, a * saturate(s.a));
            }
            ENDCG
        }
    }
}
