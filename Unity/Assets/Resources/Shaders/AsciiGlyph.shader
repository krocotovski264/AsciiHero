// One character per surface point. Each glyph quad carries its anchor (all 4 corners share it, so skinning moves
// the whole glyph with the body) and is expanded in view space: the character is always upright and undistorted,
// like letters projected from the 3D model. Brightness comes from the lit scene at the anchor (light, shadow and
// nearer = brighter); points that the camera does not see (back faces, skin under armour) are dropped.
Shader "AsciiHero/Glyph"
{
    Properties
    {
        _AsciiRow ("Glyph ramp row", Float) = 0
        _AsciiActor ("Actor boost", Float) = 0
        _GlyphCell ("Glyph size (m)", Vector) = (0.017, 0.028, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Glyphs"
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite Off ZTest Always
            BlendOp Max
            Blend One One
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _AsciiRow;
                float _AsciiActor;
                float4 _GlyphCell;
            CBUFFER_END
            TEXTURE2D(_AsciiAtlas); SAMPLER(sampler_AsciiAtlas);
            TEXTURE2D(_AsciiLitTex); SAMPLER(sampler_AsciiLitTex);
            TEXTURE2D(_AsciiNDTex); SAMPLER(sampler_AsciiNDTex);
            float _AsciiRows, _AsciiExposure, _AsciiLinear, _AsciiDepthRef;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 corner : TEXCOORD0; float2 seed : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float3 col : TEXCOORD1; float gi : TEXCOORD2; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 pWS = TransformObjectToWorld(i.positionOS.xyz);
                float3 pVS = TransformWorldToView(pWS);
                float3 nVS = normalize(TransformWorldToViewDir(TransformObjectToWorldNormal(i.normalOS), true));
                float3 toCam = normalize(-pVS);
                float4 clip = TransformWViewToHClip(pVS);
                float4 sp = ComputeScreenPos(clip);
                float2 suv = sp.xy / sp.w;
                float4 nd = SAMPLE_TEXTURE2D_LOD(_AsciiNDTex, sampler_AsciiNDTex, suv, 0);
                float sceneZ = nd.b > 1e-4 ? nd.b * 12.0 : 1e5;
                float myZ = -pVS.z;
                bool inside = all(suv >= 0.0) && all(suv <= 1.0);
                bool vis = dot(nVS, toCam) > 0.03 && myZ <= sceneZ + 0.004 + 0.0015 * myZ && inside;
                float3 lit = SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, sampler_AsciiLitTex, suv, 0).rgb;
                float3 col = _AsciiLinear > 0.5 ? pow(max(lit, 0.0), 1.0 / 2.2) : max(lit, 0.0);
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                float depthK = clamp(1.0 - (myZ - _AsciiDepthRef) * 0.09, 0.5, 1.2);
                lum = saturate((lum * 1.15 * _AsciiExposure * (1.0 + 0.55 * _AsciiActor) + 0.06 * _AsciiActor) * depthK);
                float gi = floor(saturate(pow(lum, 0.85) + (i.seed.x - 0.5) * 0.09) * 15.99);
                if (gi < 1.0 && lum > 0.008) gi = 1.0;
                if (gi < 0.5) vis = false;
                float m = max(max(col.r, col.g), col.b);
                o.col = (_AsciiRow > 8.5 && _AsciiRow < 9.5) ? col * 1.3 + 0.15 : (col / max(m, 1e-3)) * (0.32 + 0.85 * lum);
                o.gi = gi;
                o.local = i.corner + 0.5;
                pVS.xy += i.corner * _GlyphCell.xy;          // upright character in the view plane
                o.positionCS = vis ? TransformWViewToHClip(pVS) : float4(2, 2, 2, 1);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 l = clamp(i.local, 0.03, 0.97);
                float rows = max(_AsciiRows, 1.0);
                float mk = SAMPLE_TEXTURE2D(_AsciiAtlas, sampler_AsciiAtlas, float2((i.gi + l.x) / 16.0, 1.0 - (_AsciiRow + 1.0) / rows + l.y / rows)).r;
                return half4(i.col * mk, 1);
            }
            ENDHLSL
        }
    }
}
