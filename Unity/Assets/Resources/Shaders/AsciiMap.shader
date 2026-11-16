// The map in the shadow pocket: the world drawn in characters. The map texture holds, per point of the world (3 per
// metre), a colour and a class (alpha: ground, grass, water, path, tree, bush, rock, building, cliff, crystal, inn);
// every character cell of the map on the screen looks up the point under its middle and shows the class' character
// in its colour. Zoomed in, a cell covers less ground and the map shows more. Up is north, right is west - as the
// camera sees the world.
Shader "Hidden/AsciiHero/Map"
{
    Properties { _MainTex ("map", 2D) = "black" {} }
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
            float4 _MRect;      // the map's rectangle on the screen: w, h (px); cell w, h (px)
            float4 _MView;      // the world point at its middle (x, z); metres per pixel; alpha
            float4 _MWorld;     // the texture's world rectangle: x0, z0, width, height (m)
            float _MRound, _MTime;      // 1: fade out towards an oval edge (the small map)
            float H2(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float4 frag (v2f_img i) : SV_Target
            {
                float2 px = float2(i.uv.x, 1.0 - i.uv.y) * _MRect.xy;
                float2 cellSz = _MRect.zw, cell = floor(px / cellSz), cc = (cell + 0.5) * cellSz - _MRect.xy * 0.5;
                // screen right = world -x, screen down = world +z
                float2 w = float2(_MView.x - cc.x * _MView.z, _MView.y + cc.y * _MView.z);
                float2 uv = (w - _MWorld.xy) / _MWorld.zw;
                if (any(uv < 0.0) || any(uv > 1.0)) return 0;
                float4 m = tex2Dlod(_MainTex, float4(uv, 0, 0));
                int cls = (int)(m.a * 16.0 + 0.5);
                if (cls == 0) return 0;
                float h = H2(floor(w * 2.0));
                // the character of the class: row, column of the atlas
                float row = 2, col = 1;
                if (cls == 1) { row = 2; col = h < 0.5 ? 1 : 0; }               // ground: . or nothing
                else if (cls == 2) { row = 2; col = h < 0.5 ? 2 : 4; }          // grass , ;
                else if (cls == 3) { row = 1; col = 6; }                        // water ~
                else if (cls == 4) { row = 2; col = 3; }                        // path :
                else if (cls == 5) { row = 2; col = h < 0.6 ? 13 : 12; }        // tree & %
                else if (cls == 6) { row = 2; col = 8; }                        // bush *
                else if (cls == 7) { row = 4; col = h < 0.5 ? 5 : 6; }          // rock o 8
                else if (cls == 8) { row = 2; col = 11; }                       // building #
                else if (cls == 9) { row = 5; col = 10; }                       // cliff M
                else if (cls == 10) { row = 8; col = 3; }                       // crystal *
                else if (cls == 11) { row = 5; col = 8; }                       // the inn H
                float rows = max(_AsciiRows, 1.0);
                float2 l = clamp(frac(px / cellSz), 0.04, 0.96); l.y = 1.0 - l.y;
                float ink = tex2D(_AsciiAtlas, float2((col + l.x) / 16.0, 1.0 - (row + 1.0) / rows + l.y / rows)).r;
                ink = saturate((ink - 0.2) * 1.8);
                if (cls == 3) ink *= 0.75 + 0.25 * sin(_MTime * 2.0 + w.x * 1.3 + w.y * 0.7);      // the water ripples
                float3 c = m.rgb * (0.8 + 0.35 * h);
                float a = ink * _MView.w;
                if (_MRound > 0.5) { float2 e = (px / _MRect.xy - 0.5) * 2.0; a *= 1.0 - smoothstep(0.75, 1.0, length(e)); }
                return float4(c, a);
            }
            ENDCG
        }
    }
}
