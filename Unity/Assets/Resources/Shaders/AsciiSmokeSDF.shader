// The interface's smoke, pass 1 (at a quarter of the screen): the distance to the edge of all the smoke at once.
// Every piece (a button, a panel, a cloud of the pocket) is a rounded box whose edge rolls; the pieces run into each
// other where they meet (a smooth union), the billows sitting on their edges join them, and the puffs breaking off
// stay apart. Out: R the distance to the edge (px, < 0 inside), G how close a breaking-off puff is (for its
// characters), B the violet glow of a piece that is on, A the opacity of the nearest piece.
Shader "Hidden/AsciiHero/SmokeSDF"
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
            #pragma target 3.5
            #include "UnityCG.cginc"
            float4 _SSA[48], _SSB[48];       // pieces: centre x, y, half w, h (px) | corner radius, on, burst, alpha
            float4 _SBL[224], _SFL[128];      // billows: x, y, r | breaking-off puffs: x, y, r, age 0..1
            float _SSN, _SBN, _SFN, _SST, _SSMerge, _SSSz;
            float4 _SSScreen;                // screen w, h (px)
            float H2(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float N2(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(H2(i), H2(i + float2(1, 0)), u.x), lerp(H2(i + float2(0, 1)), H2(i + float2(1, 1)), u.x), u.y);
            }
            float Smin(float a, float b, float k) { float h = saturate(0.5 + 0.5 * (b - a) / k); return lerp(b, a, h) - k * h * (1.0 - h); }
            float4 frag (v2f_img i) : SV_Target
            {
                float2 p = float2(i.uv.x, 1.0 - i.uv.y) * _SSScreen.xy;
                float F = 1e5, best = 1e5, alpha = 0, on = 0, t = _SST;
                [loop] for (int s = 0; s < 48; s++)
                {
                    if (s >= (int)_SSN) break;
                    float4 A = _SSA[s], B = _SSB[s];
                    float2 d = p - A.xy, hs = A.zw; float rad = min(B.x, min(hs.x, hs.y));
                    float2 q = abs(d) - (hs - rad); float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - rad;
                    if (sd > 260.0) { continue; }
                    float sz = min(hs.y, _SSSz + hs.y * 0.06);
                    float2 e = normalize(d / max(hs, 1.0) + 1e-5);
                    float per = clamp((hs.x + hs.y) * 0.010, 1.4, 7.0), seed = s * 7.31;
                    float wob = (N2(e * per + float2(t * 0.45, seed)) - 0.5) * 0.9 + (N2(e * per * 2.7 + float2(-t * 0.8, seed + 7.0)) - 0.5) * 0.5;
                    sd -= wob * sz * (0.35 + 0.8 * B.z);
                    F = Smin(F, sd, _SSMerge);
                    if (sd < best) { best = sd; alpha = B.w; }
                    on = max(on, B.y * saturate(1.0 - sd / 12.0));
                }
                [loop] for (int b = 0; b < 224; b++)
                {
                    if (b >= (int)_SBN) break;
                    float4 C = _SBL[b]; float pd = length(p - C.xy) - C.z;
                    if (pd > 120.0) continue;
                    F = Smin(F, pd, C.z * 0.35);
                }
                float fly = 1e5, flyK = 0;
                [loop] for (int f = 0; f < 128; f++)
                {
                    if (f >= (int)_SFN) break;
                    float4 C = _SFL[f]; float pd = length(p - C.xy) - C.z;
                    fly = min(fly, pd);
                    flyK = max(flyK, saturate(1.0 - pd / (_SSSz * (0.4 + 0.8 * C.w))) * C.w * (1.0 - C.w) * 4.0);
                }
                float G = min(F, fly);
                if (alpha <= 0 && fly < 1e4) alpha = 1;
                return float4(G, flyK, on, alpha);
            }
            ENDCG
        }
    }
}
