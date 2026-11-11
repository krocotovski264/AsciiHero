Shader "Hidden/AsciiHero/Blur"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _Dir;
            float4 frag (v2f_img i) : SV_Target
            {
                float2 uv = i.uv, d = _Dir.xy;
                float3 s = tex2D(_MainTex, uv).rgb * 0.227;
                s += (tex2D(_MainTex, uv + d * 1.384).rgb + tex2D(_MainTex, uv - d * 1.384).rgb) * 0.316;
                s += (tex2D(_MainTex, uv + d * 3.230).rgb + tex2D(_MainTex, uv - d * 3.230).rgb) * 0.070;
                return float4(s, 1);
            }
            ENDCG
        }
    }
}
