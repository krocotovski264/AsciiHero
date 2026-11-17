// Fire as particles: every character is born in the bed of the fire, rises (the flame narrows as it goes up), sways,
// cools from white-yellow through orange to dark red, thins from a dense character to a light one, shrinks and
// fades out smoothly; then it is born again. A few of them are sparks that fly higher. One quad per particle,
// always facing the camera.
Shader "AsciiHero/Fire"
{
    Properties
    {
        _FireSize ("height, glyph width, speed, -", Vector) = (0.7, 0.07, 1, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Fire"
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
                float4 _FireSize;
            CBUFFER_END

            // the story's sight: what is behind a wall from the hero is not drawn (Game: StoryVis, a top-down mask)
            TEXTURE2D(_VisTex); SAMPLER(sampler_VisTex); float4 _VisRect; float _VisOn;
            float VisK(float3 p) { if (_VisOn < 0.5) return 1.0; float2 uv = (p.xz - _VisRect.xy) * _VisRect.zw; if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) return 1.0; return SAMPLE_TEXTURE2D_LOD(_VisTex, sampler_VisTex, uv, 0).r; }
            TEXTURE2D(_AsciiAtlas); SAMPLER(sampler_AsciiAtlas);
            TEXTURE2D(_AsciiLitTex); SAMPLER(sampler_AsciiLitTex);
            TEXTURE2D(_AsciiNDTex); SAMPLER(sampler_AsciiNDTex);
            SAMPLER(ascii_point_clamp_sampler); float _AsciiDepthLit;
            float _AsciiRows, _AsciiTime; float4 _GlyphInk[224];

            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float2 seed : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float3 col : TEXCOORD1; float gi : TEXCOORD2; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float s = i.seed.x, s2 = i.seed.y, t = _AsciiTime;
                float spark = step(0.92, frac(s * 13.7));
                float life = (0.55 + 0.75 * s2) * (1.0 + 0.8 * spark) / max(_FireSize.z, 0.05);
                float ph = frac(t / life + s * 7.31);                       // 0 = born, 1 = gone
                float3 bp = TransformObjectToWorld(i.positionOS.xyz), c = TransformObjectToWorld(float3(0, 0, 0));
                float h = _FireSize.x * (0.35 + 0.65 * s2) * (1.0 + 1.3 * spark);
                float rise = ph * (0.6 + 0.4 * ph);                         // a little faster as it gets lighter
                float3 p = c + (bp - c) * (1.0 - 0.8 * ph) * (1.0 - 0.5 * spark);
                p.y = bp.y + h * rise;
                float sw = _FireSize.x * (0.05 + 0.1 * spark) * ph;
                p.x += sin(t * 6.3 + s * 40.0 + ph * 5.0) * sw; p.z += cos(t * 5.1 + s * 23.0 + ph * 4.0) * sw;
                float3 pVS = TransformWorldToView(p);
                float4 clip = TransformWViewToHClip(pVS);
                float4 sp = ComputeScreenPos(clip); float2 suv = sp.xy / sp.w;
                float dz = _AsciiDepthLit > 0.5 ? abs(SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, ascii_point_clamp_sampler, suv, 0).a) : SAMPLE_TEXTURE2D_LOD(_AsciiNDTex, sampler_AsciiNDTex, suv, 0).b;
                float sceneZ = dz > 1e-4 ? dz * 12.0 : 1e5;
                bool vis = -pVS.z <= sceneZ + 0.06 && all(suv >= 0.0) && all(suv <= 1.0) && VisK(p) > 0.5;
                // colour: white-yellow -> orange -> red -> dark; it appears quickly and fades out slowly
                float3 col = lerp(float3(1.0, 0.95, 0.62), float3(1.0, 0.58, 0.14), saturate(ph / 0.3));
                col = lerp(col, float3(0.85, 0.2, 0.05), saturate((ph - 0.3) / 0.4));
                col = lerp(col, float3(0.3, 0.06, 0.03), saturate((ph - 0.7) / 0.3));
                float fade = smoothstep(0.0, 0.07, ph) * (1.0 - smoothstep(0.5, 1.0, ph));
                o.col = col * fade * (1.45 - 0.3 * spark);
                o.gi = spark > 0.5 ? 5.0 + floor(frac(s * 5.3) * 3.0) : floor(lerp(15.0, 4.0, ph * ph * (3.0 - 2.0 * ph)) + frac(s * 9.1) * 1.5);
                o.gi = clamp(o.gi, 0.0, 15.0);
                float4 ink = _GlyphInk[(int)(9.0 * 16.0 + o.gi + 0.5)];
                float2 lc = lerp(ink.xy, ink.zw, i.corner + 0.5);
                o.local = lc;
                float size = _FireSize.y * (1.0 - 0.45 * ph) * (1.0 - 0.45 * spark);
                pVS.xy += (lc - 0.5) * float2(size, size * 1.65);
                o.positionCS = vis && fade > 0.004 ? TransformWViewToHClip(pVS) : float4(2, 2, 2, 1);
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                float2 l = clamp(i.local, 0.03, 0.97);
                float rows = max(_AsciiRows, 1.0);
                float mk = SAMPLE_TEXTURE2D_BIAS(_AsciiAtlas, sampler_AsciiAtlas, float2((i.gi + l.x) / 16.0, 1.0 - 10.0 / rows + l.y / rows), -0.6).r;   // row 9: the glow ramp
                mk = saturate((mk - 0.22) * 1.9);
                return half4(i.col * mk, 1);
            }
            ENDHLSL
        }
    }
}
