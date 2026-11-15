// The shadow pocket: the hero's menu is torn out of his own shadow - the same living darkness as his body and the
// smoke of his dash: black, outlined along its edge, with smoke characters (the atlas' smoke row) drifting in it and
// flecks of characters coming off it. Two clouds run together: a smaller one on the left (the hero), a larger one on
// the right (the backpack); their rims billow, and separate puffs break off and fly out, shrinking away, as the dash's.
// Opening: it gathers (_PT 0..0.35 s); closing (_PClose 0..1): it swells and comes apart.
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
            float _PT, _PClose;
            float4 _PRect;      // the drawn rectangle on the screen (px, y down): x, y, w, h
            float4 _PC1, _PC2;  // the two clouds: centre x, y, radius (px)
            float4 _PCell;      // the character cell (px) w, h; outline width (px); unused
            float4 _PRim[44], _PFly[16];
            float H1(float n) { return frac(sin(n * 127.1) * 43758.5453); }
            float H2(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float N2(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(H2(i), H2(i + float2(1, 0)), u.x), lerp(H2(i + float2(0, 1)), H2(i + float2(1, 1)), u.x), u.y);
            }
            float Smin(float a, float b, float k) { float h = saturate(0.5 + 0.5 * (b - a) / k); return lerp(b, a, h) - k * h * (1.0 - h); }
            // a cloud: a circle whose edge rolls (noise along the angle, moving)
            float Cloud(float2 p, float4 C, float s, float t)
            {
                float2 d = p - C.xy; float a = atan2(d.y, d.x);
                float w = N2(float2(a * 2.4 + t * 0.45, t * 0.3 + C.x * 0.01)) * 0.5 + N2(float2(a * 5.7 - t * 0.7, C.y * 0.01)) * 0.3 + N2(float2(a * 17.0 + t * 1.3, 4.1)) * 0.2;
                return length(d) - C.z * s * (0.9 + 0.12 * w);
            }
            float4 frag (v2f_img i) : SV_Target
            {
                float2 px = _PRect.xy + float2(i.uv.x, 1.0 - i.uv.y) * _PRect.zw;
                float t = _PT, cl = saturate(_PClose);
                float g = smoothstep(0.0, 0.35, t); g = g * (1.0 + 0.06 * sin(saturate(t / 0.7) * 3.14159));
                float s = max(g, 0.02) * (1.0 + 0.3 * cl);
                float rr = min(_PC1.z, _PC2.z);
                // the two clouds, run together
                float F = Smin(Cloud(px, _PC1, s, t), Cloud(px, _PC2, s, t * 1.1 + 3.0), rr * 0.35);
                // the rim is made of puffs: many small round billows sitting on the edge (placed on the CPU, _PRim), each
                // its own bump, breathing - the edge reads as smoke, not as a line
                [loop] for (int k = 0; k < 44; k++)
                {
                    float4 B = _PRim[k]; if (B.z <= 0.0) continue;
                    F = Smin(F, length(px - B.xy) - B.z, B.z * 0.35);
                }
                // puffs breaking off and drifting out, shrinking away (_PFly: x, y, radius, age 0..1)
                float Fly = 1e5, flyK = 0;
                [loop] for (int q = 0; q < 16; q++)
                {
                    float4 B = _PFly[q]; if (B.z <= 0.0) continue;
                    float dd = length(px - B.xy) - B.z;
                    Fly = min(Fly, dd);
                    // as it goes it blows away into violet characters, a cloud of them round it growing as it shrinks
                    flyK = max(flyK, saturate(1.0 - dd / (rr * (0.04 + 0.16 * B.w))) * B.w * (1.0 - B.w) * 4.0);
                }
                float G = min(F, Fly);
                // closing: it comes apart - holes open in it by noise
                float hole = N2(px / (rr * 0.18) + t) * 0.8 + N2(px / (rr * 0.07)) * 0.2;
                float alive = 1.0 - smoothstep(hole - 0.08, hole + 0.08, cl * 1.15);
                // the character in this cell (smoke row), denser near the edge; flecks of characters just outside
                float2 cellSz = _PCell.xy, cell = floor(px / cellSz);
                float2 l = clamp(frac(px / cellSz), 0.04, 0.96); l.y = 1.0 - l.y;
                float edgeIn = saturate(1.0 - (-G) / (rr * 0.22));
                float swirl = N2(cell * 0.23 + float2(t * 0.6, -t * 0.4)) * 0.7 + N2(cell * 0.61 - t * 0.9) * 0.3;
                float dens = G < 0.0 ? saturate(edgeIn * (0.35 + 0.65 * swirl) + 0.12 * smoothstep(0.6, 0.85, swirl)) : 0.0;
                float fleck = 0.0;
                if (G > 0.0)
                {   // violet characters coming off the edge and off the puffs, flickering, thinning out with distance
                    float hh = H2(cell + floor(t * 9.0) * float2(1.7, 3.3));
                    float nearEdge = saturate(1.0 - G / (rr * 0.22));
                    float k2 = max(nearEdge * 0.8, saturate(flyK));
                    fleck = hh > 1.0 - 0.22 * k2 ? (0.35 + 0.65 * k2) : 0.0;
                }
                float gd = max(dens, fleck);
                float gi = floor(saturate(gd) * 15.99);
                float rows = max(_AsciiRows, 1.0);
                float ink = tex2D(_AsciiAtlas, float2((gi + l.x) / 16.0, 1.0 - 14.0 / rows + l.y / rows)).r;
                ink = saturate((ink - 0.2) * 1.8) * (gi > 0.5 ? 1.0 : 0.0);
                // the outline along the edge (the shadow body's: a thin cool line)
                float ow = _PCell.z;
                float edgeL = 1.0 - smoothstep(ow * 0.5, ow * 1.3, abs(G));
                float inside = 1.0 - smoothstep(-0.8, 0.8, G);
                float3 black = float3(0.006, 0.004, 0.011);
                float3 glyphC = lerp(float3(0.12, 0.1, 0.17), float3(0.42, 0.38, 0.52), gd);
                float3 lineC = float3(0.46, 0.42, 0.58);
                float3 violet = lerp(float3(0.36, 0.16, 0.6), float3(0.78, 0.5, 1.0), saturate(fleck));
                float3 col = lerp(black, G > 0.0 ? violet : glyphC, ink);
                col = lerp(col, lineC, edgeL);
                float a = saturate(max(inside, max(edgeL, ink * saturate(fleck * 1.8))));
                return float4(col, a * alive * saturate(g * 2.0));
            }
            ENDCG
        }
    }
}
