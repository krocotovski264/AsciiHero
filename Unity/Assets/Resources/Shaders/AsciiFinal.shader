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
            sampler2D _MainTex, _BloomTex; float _Bloom, _Linear;
            float4 frag (v2f_img i) : SV_Target
            {
                float3 a = tex2D(_MainTex, i.uv).rgb, b = tex2D(_BloomTex, i.uv).rgb;
                float3 bg = lerp(float3(0.018, 0.02, 0.032), float3(0.05, 0.055, 0.08), smoothstep(1.1, 0.0, length(i.uv - float2(0.5, 0.55)) * 1.6));
                float3 c = bg + a + b * _Bloom * 1.6;
                // values are display-referred; in a Linear project convert so the sRGB output shows them unchanged
                if (_Linear > 0.5) c = pow(max(c, 0), 2.2);
                return float4(c, 1);
            }
            ENDCG
        }
    }
}
