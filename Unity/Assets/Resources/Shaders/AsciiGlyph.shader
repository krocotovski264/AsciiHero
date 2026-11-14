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
        _GlyphSmoke ("Smoke body", Float) = 0
        _GlyphCloth ("Cloth", Float) = 0
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
                float _GlyphSmoke;
                float4 _GlyphCell;
                float _GlyphCloth;
            CBUFFER_END
            TEXTURE2D(_AsciiAtlas); SAMPLER(sampler_AsciiAtlas);
            TEXTURE2D(_AsciiLitTex); SAMPLER(sampler_AsciiLitTex);
            TEXTURE2D(_AsciiNDTex); SAMPLER(sampler_AsciiNDTex);
            float _AsciiRows, _AsciiExposure, _AsciiLinear, _AsciiDepthRef;
            float4 _DashShift;   // per renderer (world): during the dash the armour pieces lag behind the body
                float4 _DashSpin;    // per renderer: xyz = axis, w = angle; the piece tumbles about _DashPivot while it flies
                float4 _DashPivot;
                float4 _DashScatter;  // per renderer: chain mail coming apart into its links during the dash (w = time, 0 = off)
            // cloth (the robe): the hem trails behind the motion and flutters; in the dash flight it billows harder
            float _ClothDash;      // per renderer (MaterialPropertyBlock)
            float4 _AsciiHeroVel; float _AsciiTime;
            float3 ClothWS(float3 ws, float3 os)
            {
                if (_GlyphCloth < 0.5) return ws;
                float w = saturate((1.05 - os.y) / 0.8); w *= w;      // the hem moves most, the shoulders hardly at all
                float sp = length(_AsciiHeroVel.xz), t = _AsciiTime, f = 1.0 + 0.8 * _ClothDash;
                float amp = (0.012 + 0.016 * sp) * (1.0 + 4.0 * _ClothDash);
                float3 fl = float3(sin(t * 7.0 * f + os.y * 11.0 + os.x * 6.0), 0.35 * sin(t * 5.3 * f + os.x * 9.0), cos(t * 6.3 * f + os.y * 9.0 + os.z * 7.0));
                return ws - _AsciiHeroVel.xyz * (0.08 * w) + fl * amp * w + float3(0, 0.03 * w * min(sp, 3.0) / 3.0, 0);
            }
                float3 DashWS(float3 ws)
            {
                if (_DashSpin.w != 0.0)
                {
                        float3 v = ws - _DashPivot.xyz; float cs = cos(_DashSpin.w), sn = sin(_DashSpin.w);
                    ws = _DashPivot.xyz + v * cs + cross(_DashSpin.xyz, v) * sn + _DashSpin.xyz * dot(_DashSpin.xyz, v) * (1.0 - cs);
                }
                return ws + _DashShift.xyz;
            }
            float _AsciiLod;      // far camera: fewer, larger characters (how many times further than the close-up distance)
            float _GlyphPuff;     // per renderer: 0..1, a burst of smoke flying apart
            float _GlyphBurn;     // per renderer: 0..1, a torn-off limb burning away
            float _LimbGone[6];   // per renderer: 1 = this limb is torn off (index from seed.y)
            #define GLYPH_SCALE 1.35
            #define MIN_GLYPH 5.0

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 corner : TEXCOORD0; float2 seed : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 local : TEXCOORD0; float3 col : TEXCOORD1; float gi : TEXCOORD2; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                // smoke: each glyph of the shadow body keeps drifting off the surface and upwards, fading as it goes
                float ph = frac(_AsciiTime * 0.45 + i.seed.x * 7.31);
                float3 pOS = i.positionOS.xyz;
                // a third of the glyphs are long wisps: they rise much higher and trail behind the moving body
                float wisp = step(0.62, frac(i.seed.x * 13.7));
                if (_GlyphSmoke > 0.5) pOS += i.normalOS * (0.004 + (0.07 + 0.09 * wisp) * ph * ph);
                // puff (a burst of smoke where a limb is torn off): the glyphs fly apart from the centre, rise and thin out
                if (_GlyphPuff > 0.0) pOS += i.normalOS * _GlyphPuff * (0.18 + 0.5 * i.seed.x);
                float3 pWS = DashWS(ClothWS(TransformObjectToWorld(pOS), i.positionOS.xyz));
                // dash, chain mail: every character (a link) flies on its own, a little later or sooner than its neighbours,
                // on its own wobbling path, and they gather on the body again
                bool scat = _DashScatter.w > 0.0;
                if (scat)
                {
                    float dl = frac(i.seed.x * 17.3) * 0.42, u2 = saturate((_DashScatter.w - dl) / 0.4), e2 = u2 * u2 * (3.0 - 2.0 * u2), ar = sin(3.14159 * e2);
                    float3 jit = (float3(frac(i.seed.x * 7.1), frac(i.seed.x * 3.7), frac(i.seed.x * 11.9)) - 0.5) * 0.9;
                    pWS += _DashScatter.xyz * (1.0 - e2) + jit * ar + float3(0, 0.3 * ar, 0);
                }
                float lod = max(1.0, _AsciiLod);
                bool lodKeep = lod < 1.01 || frac(i.seed.x * 31.7 + 0.37) < 1.7 / (lod * lod);
                if (_GlyphSmoke > 0.5) pWS.y += (0.15 + 0.4 * wisp) * ph * ph;
                if (_GlyphSmoke > 0.5 && _GlyphPuff <= 0.0) pWS -= _AsciiHeroVel.xyz * (0.05 + 0.2 * wisp) * ph * ph;
                if (_GlyphPuff > 0.0) pWS.y += _GlyphPuff * _GlyphPuff * (0.15 + 0.5 * frac(i.seed.x * 5.3));
                bool gone = _LimbGone[(int)(i.seed.y + 0.5)] > 0.5 || dot(i.normalOS, i.normalOS) < 0.09;
                // burning: glyphs turn to embers one by one, float up and go out
                float ember = _GlyphBurn > 0.0 ? saturate((_GlyphBurn * 1.3 - i.seed.x) / 0.3) : 0.0;
                float3 pVS = TransformWorldToView(pWS);
                pVS.y += ember * ember * 0.35;
                float3 nVS = normalize(TransformWorldToViewDir(TransformObjectToWorldNormal(i.normalOS), true));
                float3 toCam = normalize(-pVS);
                float4 clip = TransformWViewToHClip(pVS);
                float4 sp = ComputeScreenPos(clip);
                float2 suv = sp.xy / sp.w;
                float4 nd = SAMPLE_TEXTURE2D_LOD(_AsciiNDTex, sampler_AsciiNDTex, suv, 0);
                float sceneZ = nd.b > 1e-4 ? nd.b * 12.0 : 1e5;
                float myZ = -pVS.z;
                bool inside = all(suv >= 0.0) && all(suv <= 1.0);
                bool vis = scat ? (lodKeep && !gone && inside) : lodKeep && !gone && ember < 0.999 && dot(nVS, toCam) > -0.05 - ember - _GlyphPuff * 4.0 - (_DashSpin.w != 0.0 ? 4.0 : 0.0) && myZ <= sceneZ + 0.008 + 0.0035 * myZ && inside;
                float3 lit = SAMPLE_TEXTURE2D_LOD(_AsciiLitTex, sampler_AsciiLitTex, suv, 0).rgb;
                float3 col = _AsciiLinear > 0.5 ? pow(max(lit, 0.0), 1.0 / 2.2) : max(lit, 0.0);
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                float depthK = clamp(1.0 - (myZ - _AsciiDepthRef) * 0.09, 0.5, 1.2);
                lum = saturate((lum * 1.15 * _AsciiExposure * (1.0 + 0.55 * _AsciiActor) + 0.06 * _AsciiActor) * depthK);
                // dense glyphs only (no '.' or ' '): darkness is carried by colour, so the surface stays filled
                float gi = MIN_GLYPH + floor(saturate(pow(lum, 0.85) + (i.seed.x - 0.5) * 0.09) * (15.99 - MIN_GLYPH));
                float m = max(max(col.r, col.g), col.b);
                o.col = (_AsciiRow > 8.5 && _AsciiRow < 9.5) ? col * 1.3 + 0.15 : (col / max(m, 1e-3)) * (0.10 + 0.95 * lum);
                if (_GlyphSmoke > 0.5)
                {
                    gi = floor(lerp(15.0, MIN_GLYPH, ph));
                    o.col = lerp(float3(0.42, 0.27, 0.80), float3(0.10, 0.06, 0.24), ph) * (1.0 - ph * (0.8 - 0.25 * wisp)) * (0.75 + 0.25 * depthK);
                    if (_GlyphPuff > 0.0) { gi = floor(lerp(15.0, MIN_GLYPH, _GlyphPuff)); o.col = lerp(float3(0.55, 0.36, 0.95), float3(0.10, 0.06, 0.22), _GlyphPuff) * (1.0 - _GlyphPuff) * (1.0 - _GlyphPuff * 0.5); }
                }
                if (scat) { o.col = float3(0.62, 0.67, 0.76) * (0.5 + 0.5 * frac(i.seed.x * 5.1)); gi = MIN_GLYPH + floor(frac(i.seed.x * 9.7) * (15.99 - MIN_GLYPH)); }
                if (ember > 0.0) { o.col = lerp(o.col, float3(1.6, 0.62, 0.12), min(1.0, ember * 2.0)) * (1.0 - ember * ember); gi = floor(lerp(gi, 2.0, ember)); }
                o.gi = gi;
                o.local = i.corner + 0.5;
                pVS.xy += i.corner * _GlyphCell.xy * GLYPH_SCALE * lod;   // upright character in the view plane, slight overlap
                o.positionCS = vis ? TransformWViewToHClip(pVS) : float4(2, 2, 2, 1);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float2 l = clamp(i.local, 0.03, 0.97);
                float rows = max(_AsciiRows, 1.0);
                // sharp characters: a crisper mip and a hard threshold on the glyph's coverage (no soft, washed-out edges)
                float mk = SAMPLE_TEXTURE2D_BIAS(_AsciiAtlas, sampler_AsciiAtlas, float2((i.gi + l.x) / 16.0, 1.0 - (_AsciiRow + 1.0) / rows + l.y / rows), -0.6).r;
                mk = saturate((mk - 0.22) * 1.9);
                return half4(i.col * mk, 1);
            }
            ENDHLSL
        }
    }
}
