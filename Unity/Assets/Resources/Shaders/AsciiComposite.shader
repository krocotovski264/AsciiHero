// Scene colour + normal/depth/id buffers -> coloured glyphs. Port of the web build's GLSL shader.
Shader "Hidden/AsciiHero/Composite"
{
    Properties { _MainTex ("unused", 2D) = "black" {} }
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
            sampler2D _ColTex, _NDTex, _Atlas;
            float4 _Grid;      // cols, rows
            float4 _CellPx;    // cell width, height (px)
            float4 _OutPx;     // output width, height (px)
            float _NRows, _Exposure, _ActorBoost, _EdgeK, _Linear;
            float _RampMap[32];
            float lumOf(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
            float4 frag (v2f_img i) : SV_Target
            {
                float2 fc = i.uv * _OutPx.xy;
                float2 cell = floor(fc / _CellPx.xy);
                float2 grid = _Grid.xy;
                float2 uv = (cell + 0.5) / grid;
                float4 c = tex2Dlod(_ColTex, float4(uv, 0, 0));
                float4 nd = tex2Dlod(_NDTex, float4(uv, 0, 0));
                float id = floor(nd.a * 255.0 + 0.5);
                float3 col = _Linear > 0.5 ? pow(max(c.rgb, 0.0), 1.0 / 2.2) : max(c.rgb, 0.0);
                if (id < 0.5) return float4(0, 0, 0, 1);
                float isA = step(127.5, id); id -= 128.0 * isA; float actor = isA * _ActorBoost;
                float row = _RampMap[(int)id];
                float lum = saturate(lumOf(col) * 1.15 * _Exposure * (1.0 + 0.55 * actor) + 0.06 * actor);
                float2 px = 1.0 / grid;
                float d0 = nd.b;
                float4 ndl = tex2Dlod(_NDTex, float4(uv - float2(px.x, 0), 0, 0));
                float4 ndr = tex2Dlod(_NDTex, float4(uv + float2(px.x, 0), 0, 0));
                float4 ndd = tex2Dlod(_NDTex, float4(uv - float2(0, px.y), 0, 0));
                float4 ndu = tex2Dlod(_NDTex, float4(uv + float2(0, px.y), 0, 0));
                float dl = ndl.b < 1e-4 ? 1.0 : ndl.b, dr = ndr.b < 1e-4 ? 1.0 : ndr.b;
                float dd = ndd.b < 1e-4 ? 1.0 : ndd.b, du = ndu.b < 1e-4 ? 1.0 : ndu.b;
                float gx = dr - dl, gy = du - dd;
                float lap = abs(dl + dr - 2.0 * d0) + abs(du + dd - 2.0 * d0);
                float e = lap / max(d0, 1e-3);
                float2 n0xy = nd.rg * 2 - 1;  float3 n0 = float3(n0xy, sqrt(saturate(1 - dot(n0xy, n0xy))));
                float2 nlxy = ndl.rg * 2 - 1; float3 nl = float3(nlxy, sqrt(saturate(1 - dot(nlxy, nlxy))));
                float2 nuxy = ndu.rg * 2 - 1; float3 nu = float3(nuxy, sqrt(saturate(1 - dot(nuxy, nuxy))));
                float crease = (1.0 - dot(n0, nl)) + (1.0 - dot(n0, nu));
                float gi;
                bool isEdge = (e > 0.12 || crease > 1.1) && _EdgeK > 0.5 && row < 9.5 && abs(row - 11.0) > 0.5;
                if (isEdge)
                {
                    row = 12.0;
                    float ax = abs(gx), ay = abs(gy);
                    if (e <= 0.12) { float3 nn = n0 - nl; ax = abs(nn.x); ay = abs(nn.y); gx = nn.x; gy = nn.y; }
                    if (ax > ay * 2.2) gi = 0.0; else if (ay > ax * 2.2) gi = 1.0; else gi = (gx * gy > 0.0) ? 3.0 : 2.0;
                    lum = max(lum, 0.72 + 0.2 * actor);
                }
                else
                {
                    float hsh = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
                    gi = floor(saturate(pow(lum, 0.85) + (hsh - 0.5) * 0.09) * 15.99);
                    if (row < 9.5 && abs(row - 11.0) > 0.5 && gi < 1.0 && lum > 0.02) gi = 1.0;
                }
                float2 local = clamp(frac(fc / _CellPx.xy), 0.08, 0.92);
                float2 auv = float2((gi + local.x) / 16.0, 1.0 - (row + 1.0) / _NRows + local.y / _NRows);
                float mask = tex2Dlod(_Atlas, float4(auv, 0, 0)).r;
                float m = max(max(col.r, col.g), col.b);
                float3 hue = col / max(m, 1e-3);
                float3 outc = hue * (0.30 + 0.85 * lum);
                if (row > 8.5 && row < 9.5) outc = col * 1.3 + 0.15;
                return float4(outc * mask, 1);
            }
            ENDCG
        }
    }
}
