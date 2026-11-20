// Characters thrown by a script (the cinematics' dragon fire, its smoke, the dust): every particle is one quad whose
// place, colour, size and character the script writes each frame (position; colour rgb + size in a; uv1: character,
// glyph row; uv2.x: how much the smoke hides behind it). Always facing the camera, hidden behind what stands nearer (the
// scene's depth). The fire is max-blended (light over light); the smoke is laid over (alpha), its cell darkened, so it hides.
Shader "AsciiHero/Breath"
{
    Properties { [Enum(UnityEngine.Rendering.BlendOp)] _Op ("Op", Float) = 4 [Enum(UnityEngine.Rendering.BlendMode)] _Src ("Src", Float) = 1 [Enum(UnityEngine.Rendering.BlendMode)] _Dst ("Dst", Float) = 1 _Smoke ("Smoke", Float) = 0 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Breath"
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite Off ZTest Always
            BlendOp [_Op]
            Blend [_Src] [_Dst]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_AsciiAtlas); SAMPLER(sampler_AsciiAtlas);
            TEXTURE2D(_AsciiLitTex); SAMPLER(sampler_AsciiLitTex);
            TEXTURE2D(_AsciiNDTex); SAMPLER(sampler_AsciiNDTex);
            SAMPLER(ascii_point_clamp_sampler); float _AsciiDepthLit;
            float _AsciiRows; float4 _GlyphInk[224]; float _Smoke;

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float2 glyph : TEXCOORD1; float2 extra : TEXCOORD2; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float3 col : TEXCOORD1; float2 g : TEXCOORD2; float2 c : TEXCOORD3; float op : TEXCOORD4; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                float3 pVS = TransformWorldToView(p);
                float4 clip = TransformWViewToHClip(pVS);
                float4 sp = ComputeScreenPos(clip); float2 suv = sp.xy / sp.w;
                float dz = _AsciiDepthLit > 0.5 ? abs(SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, ascii_point_clamp_sampler, suv, 0).a) : SAMPLE_TEXTURE2D_LOD(_AsciiNDTex, sampler_AsciiNDTex, suv, 0).b;
                float sceneZ = dz > 1e-4 ? dz * 12.0 : 1e5;
                bool vis = -pVS.z <= sceneZ + 0.3 + 0.02 * (-pVS.z) && all(suv >= -0.05) && all(suv <= 1.05) && i.color.a > 0.0005;
                o.col = i.color.rgb;
                o.g = i.glyph;
                float4 ink = _GlyphInk[(int)(i.glyph.y * 16.0 + i.glyph.x + 0.5)];
                float2 cc = i.corner + 0.5;
                float2 lc = _Smoke > 0.5 ? cc : lerp(ink.xy, ink.zw, cc);          // (the smoke covers its whole cell)
                o.local = lc; o.c = cc; o.op = i.extra.x;
                float sz = min(i.color.a, -pVS.z * 0.012);          // (near the camera no larger than a character of the scene)
                pVS.xy += (lc - 0.5) * float2(sz, sz * 1.65);
                o.positionCS = vis ? TransformWViewToHClip(pVS) : float4(2, 2, 2, 1);
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                float2 l = clamp(i.local, 0.03, 0.97);
                float rows = max(_AsciiRows, 1.0);
                float mk = SAMPLE_TEXTURE2D_BIAS(_AsciiAtlas, sampler_AsciiAtlas, float2((i.g.x + l.x) / 16.0, 1.0 - (i.g.y + 1.0) / rows + l.y / rows), -0.6).r;
                mk = saturate((mk - 0.22) * 1.9);
                if (_Smoke > 0.5)
                {
                    float2 e = abs(i.c - 0.5) * 2; float soft = saturate((1 - max(e.x, e.y)) * 4);     // (soft cell edges)
                    float a = saturate(max(mk, 0.55 * soft) * i.op);
                    return half4(lerp(i.col * 0.3, i.col, mk), a);
                }
                return half4(i.col * mk, 1);
            }
            ENDHLSL
        }
    }
}
