// Draws a rendered picture on the interface without its background: the dark ground of the ASCII frame becomes see-
// through (the projection of the hero standing on the shadow pocket's darkness, with nothing around him).
Shader "Hidden/AsciiHero/Key"
{
    Properties { _MainTex ("Picture", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float _KeyAlpha;
            float4 frag (v2f_img i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                float m = max(c.r, max(c.g, c.b));
                return float4(c, smoothstep(0.1, 0.2, m) * _KeyAlpha);
            }
            ENDCG
        }
    }
}
