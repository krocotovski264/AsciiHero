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
