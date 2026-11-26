// Scene material for URP: PBR lighting + ASCII material id. It is never shown directly: the colour camera
// renders lit colour and the ND camera (global _AsciiNDPass = 1) renders view normal xy, depth / 12 and the
// material id. Glyph mode reads both per character (AsciiHero/Glyph); grid mode turns them into a screen grid.
// _AsciiRow / _GlyphCell / _AsciiActor describe the characters built on this material's surface.
Shader "AsciiHero/Lit"
{
    Properties
    {
        _RoadBlend ("Road blend", Float) = 0
        _RoadGround ("Road ground", Color) = (0,0,0,1)
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.4
        _Metallic ("Metallic", Range(0,1)) = 0
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        _AsciiId ("ASCII id (id/255)", Float) = 0
        _AsciiRow ("Glyph ramp row", Float) = 0
        _AsciiActor ("Actor boost", Float) = 0
        _GlyphSmoke ("Smoke body", Float) = 0
        _GlyphCell ("Glyph cell (m)", Vector) = (0.03, 0.05, 0, 0)
        _Cloth ("Cloth", Float) = 0
        _AsciiFlat ("Wall or floor (no outline)", Float) = 0
        _SeeThrough ("Shows the hero through (cave walls)", Float) = 0
        _Fade ("Dissolved (0 whole .. 1 gone)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "AsciiWeather.hlsl"
        float4 _WaterRip[8];
        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _MainTex_ST;
            float _Glossiness;
            float _Metallic;
            float4 _EmissionColor;
            float _AsciiId;
            float _Cull;
            float _AsciiRow;
            float _RoadBlend; float4 _RoadGround;
            float _AsciiActor;
            float _GlyphSmoke;
            float4 _GlyphCell;
            float _Cloth;
            float _AsciiFlat;
            float _SeeThrough;
            float _Fade;
        CBUFFER_END

            // the story's sight: what is behind a wall from the hero is not drawn (Game: StoryVis, a top-down mask)
            TEXTURE2D(_VisTex); SAMPLER(sampler_VisTex); float4 _VisRect; float _VisOn;
            float VisK(float3 p) { if (_VisOn < 0.5) return 1.0; float2 uv = (p.xz - _VisRect.xy) * _VisRect.zw; if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) return 1.0; return SAMPLE_TEXTURE2D_LOD(_VisTex, sampler_VisTex, uv, 0).r; }
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        float _AsciiNDPass;
        float4 _AsciiSeason; float _AsciiWet;      // (the sky: x autumn, y winter, z spring, w snow on the ground; wet ground)
        // ---- see-through: a wall in front of the hero dissolves in a circle around him on the screen (dithered, so it
        // thins out gradually toward the middle instead of vanishing; _SeeFade brings the effect in and out over time) ----
        float4 _SeeHero;   // xyz = the hero's chest (world), w = circle radius in screen heights (0 = off)
        float _SeeFade;
        float SeeK(float3 pWS)
        {
            if (_SeeHero.w <= 0.0 || _SeeFade <= 0.001) return 0.0;
            float4 hc = TransformWorldToHClip(_SeeHero.xyz), pc = TransformWorldToHClip(pWS);
            float2 d = (pc.xy / pc.w - hc.xy / hc.w) * 0.5; d.x *= _ScreenParams.x / _ScreenParams.y;
            float hz = -TransformWorldToView(_SeeHero.xyz).z, pz = -TransformWorldToView(pWS).z;
            float front = saturate((hz - pz - 0.25) / 0.6);
            return front * (1.0 - smoothstep(_SeeHero.w, _SeeHero.w * 1.4, length(d))) * _SeeFade;      // fully clear inside the circle, a gradient at its rim
        }
        float Bayer4(float2 pix)
        {
            static const float M[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            int2 q = int2(fmod(floor(pix), 4.0));
            return (M[q.y * 4 + q.x] + 0.5) / 16.0;
        }
        void SeeClip(float3 pWS, float2 pix) { if (_VisOn > 0.5) clip(VisK(pWS) - 0.02 - Bayer4(pix) * 0.96); if (_SeeThrough > 0.5) clip(Bayer4(pix) - SeeK(pWS) * 1.02); if (_Fade > 0.001) clip(Bayer4(pix) - _Fade * 1.02); }      // (_Fade: a piece dissolving away - the cave's roof)
        float4 _DashShift;   // per renderer (world): during the dash the armour pieces lag behind the body
        float4 _DashSpin;    // per renderer: xyz = axis, w = angle; the piece tumbles about _DashPivot while it flies
        float4 _DashPivot;
        // cloth (the robe): the hem trails behind the motion and flutters; in the dash flight it billows harder
        float _ClothDash;      // per renderer (MaterialPropertyBlock)
        float4 _AsciiHeroVel; float _AsciiTime;
        // Cloth (the robe's skirt): simulated on the CPU (AHCloth) as a grid of particles - Verlet integration, distance
        // constraints relaxed a few times per step, collisions projected out of the legs and hips, the top ring pinned to the
        // waist. Here every vertex of the skirt is moved by the grid's displacement from its rest place (bilinear, wrapping
        // round the skirt). Foliage (flag 2) sways in the wind.
        #define CLOTH_COLS 12
        #define CLOTH_ROWS 8
        float4 _ClothGrid[144];       // world displacement of each particle from its rest place
            float4 _ClothNrm[144];        // the cloak's simulated normals (world)
            float4 _ClothMap2, _ClothMap3; // the cloak (_ClothMap.w = 1, x = the body's axis z): angles round the neck, over the shoulders, behind the arms, the shoulders' v; the elbows' v, 1/exponent of v, columns, rows
        float4 _ClothMap;             // skirt mesh: x = centre z, y = top y, z = bottom y, w = 1 for the cloak
        float _ClothSim;              // per renderer: 1 = this is the simulated skirt
        float4 _AsciiWind; float4 _AsciiWindOff;   // xy: how far the air has carried the gusts (world xz), zw: the place's reference point            // xy = direction (world xz), z = strength
        float WHash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
        float WNoise(float2 p)      // smooth value noise, 0..1
        {
            float2 i = floor(p), f = frac(p); float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(WHash(i), WHash(i + float2(1, 0)), u.x), lerp(WHash(i + float2(0, 1)), WHash(i + float2(1, 1)), u.x), u.y);
        }
        // the wind field (shared by grass, leaves): a coarse gust pattern carried downwind, with finer detail scrolling
        // faster over it (as in the 'Ghost of Tsushima' grass); _AsciiWind: xy direction, z strength (with lulls, from
        // the CPU), w the distance the air has travelled
        float WindGust(float2 p)
        {
                float2 r = p - _AsciiWindOff.zw - _AsciiWindOff.xy;
                return smoothstep(0.38, 0.78, WNoise(r * 0.09 + 3.1));
            }
        float3 ClothWS(float3 ws, float3 os)
        {
            if (_Cloth > 1.5)
            {   // leaves: the crown leans downwind with the gusts, every bit of it flutters
                float2 wd = _AsciiWind.xy; float st = _AsciiWind.z, t = _AsciiTime;
                float gust = WindGust(ws.xz);     // the same gusts as over the grass
                float hk = saturate(ws.y / 3.5);
                float3 sway = float3(wd.x, 0, wd.y) * st * (0.04 + 0.11 * gust) * hk;
                float3 fl = float3(sin(t * 5.3 + dot(ws, float3(4.1, 2.3, 3.7))), 0.6 * sin(t * 6.1 + dot(ws, float3(2.9, 3.3, 1.7))), cos(t * 4.7 + dot(ws, float3(1.3, 3.9, 2.1))));
                return ws + sway + fl * (0.012 + 0.025 * st * gust) * hk;
            }
            if (_Cloth < 0.5 || _ClothSim < 0.5) return ws;
            if (_ClothMap.w > 0.5)
            {   // the cloak: a sheet, columns across (x grows with the width row by row), rows down
                int C = (int)_ClothMap3.z, R = (int)_ClothMap3.w;
                float v01 = pow(saturate((_ClothMap.y - os.y) / max(_ClothMap.y - _ClothMap.z, 1e-3)), _ClothMap3.y);
                float Th = v01 < _ClothMap2.w ? lerp(_ClothMap2.x, _ClothMap2.y, v01 / _ClothMap2.w) : lerp(_ClothMap2.y, _ClothMap2.z, smoothstep(_ClothMap2.w, _ClothMap3.x, v01));
                float th = atan2(os.x, -(os.z - _ClothMap.x));        // the angle from the back, round the body's axis
                float uu = saturate(th / (2.0 * Th) + 0.5) * (C - 1), vv = v01 * (R - 1);
                int a0 = min((int)floor(uu), C - 2), b0 = min((int)floor(vv), R - 2); float fa = saturate(uu - a0), fb = saturate(vv - b0);
                float3 e00 = _ClothGrid[b0 * C + a0].xyz, e01 = _ClothGrid[b0 * C + a0 + 1].xyz, e10 = _ClothGrid[(b0 + 1) * C + a0].xyz, e11 = _ClothGrid[(b0 + 1) * C + a0 + 1].xyz;
                return ws + lerp(lerp(e00, e01, fa), lerp(e10, e11, fa), fb);
            }
            float u = frac(atan2(os.z - _ClothMap.x, os.x) / 6.2831853 + 1.0) * CLOTH_COLS;
            float v = saturate((_ClothMap.y - os.y) / max(_ClothMap.y - _ClothMap.z, 1e-3)) * (CLOTH_ROWS - 1);
            int c0 = (int)floor(u) % CLOTH_COLS, c1 = (c0 + 1) % CLOTH_COLS; float fu = frac(u);
            int r0 = min((int)floor(v), CLOTH_ROWS - 2); float fv = saturate(v - r0);
            float3 d00 = _ClothGrid[r0 * CLOTH_COLS + c0].xyz, d01 = _ClothGrid[r0 * CLOTH_COLS + c1].xyz;
            float3 d10 = _ClothGrid[(r0 + 1) * CLOTH_COLS + c0].xyz, d11 = _ClothGrid[(r0 + 1) * CLOTH_COLS + c1].xyz;
            return ws + lerp(lerp(d00, d01, fu), lerp(d10, d11, fu), fv);
        }
        // the cloak's normal, from the simulated grid (its folds and billows light up and fall into shade)
        float3 ClothNrm(float3 os, float3 nW)
        {
            if (_Cloth < 0.5 || _Cloth > 1.5 || _ClothSim < 0.5 || _ClothMap.w < 0.5) return nW;
            int C = (int)_ClothMap3.z, R = (int)_ClothMap3.w;
            float v01 = pow(saturate((_ClothMap.y - os.y) / max(_ClothMap.y - _ClothMap.z, 1e-3)), _ClothMap3.y);
            float Th = v01 < _ClothMap2.w ? lerp(_ClothMap2.x, _ClothMap2.y, v01 / _ClothMap2.w) : lerp(_ClothMap2.y, _ClothMap2.z, smoothstep(_ClothMap2.w, _ClothMap3.x, v01));
            float th = atan2(os.x, -(os.z - _ClothMap.x));        // the angle from the back, round the body's axis
            float uu = saturate(th / (2.0 * Th) + 0.5) * (C - 1), vv = v01 * (R - 1);
            int a0 = min((int)floor(uu), C - 2), b0 = min((int)floor(vv), R - 2); float fa = saturate(uu - a0), fb = saturate(vv - b0);
            float3 n = lerp(lerp(_ClothNrm[b0 * C + a0].xyz, _ClothNrm[b0 * C + a0 + 1].xyz, fa), lerp(_ClothNrm[(b0 + 1) * C + a0].xyz, _ClothNrm[(b0 + 1) * C + a0 + 1].xyz, fa), fb);
            return dot(n, n) > 1e-6 ? normalize(n) : nW;
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
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float viewZ : TEXCOORD3;
            };

            Varyings vert (Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 pWS = DashWS(ClothWS(TransformObjectToWorld(i.positionOS.xyz), i.positionOS.xyz));
                VertexNormalInputs n = GetVertexNormalInputs(i.normalOS);
                o.positionCS = TransformWorldToHClip(pWS);
                o.positionWS = pWS;
                o.normalWS = ClothNrm(i.positionOS.xyz, n.normalWS);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.viewZ = -TransformWorldToView(pWS).z;
                return o;
            }

            half4 frag (Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                SeeClip(i.positionWS, i.positionCS.xy);
                float3 nWS = normalize(i.normalWS);
                if (_Cloth > 0.5 && _Cloth < 1.5 && _ClothSim > 0.5 && _ClothMap.w > 0.5) { if (dot(nWS, GetWorldSpaceNormalizeViewDir(i.positionWS)) < 0) nWS = -nWS; }      // the cloak: lit on the side we see
                else if (!front) nWS = -nWS;
                if (_AsciiNDPass > 0.5)
                {
                    float3 nVS = TransformWorldToViewDir(nWS, true);
                    if (nVS.z < 0) nVS = -nVS;
                    return half4(nVS.xy * 0.5 + 0.5, i.viewZ / 12.0, _AsciiId);
                }
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color;
                if (_RoadBlend > 0.5) albedo.rgb = lerp(_RoadGround.rgb, albedo.rgb, smoothstep(0.0, 1.0, i.uv.x));
                // World-space surface texture: independent of mesh UVs and tile seams.
                float matId = floor(_AsciiId * 255.0 + 0.5) - 1.0;
                if (_AsciiActor < 0.5 && nWS.y > 0.5)
                {
                    float2 p = i.positionWS.xz;
                    float grain = WNoise(p * 19.0);
                    float broad = WNoise(p * 2.7);
                    if (matId == 30.0 || matId == 36.0 || matId == 50.0 || matId == 59.0)
                    {
                        float grit = smoothstep(0.72, 0.92, grain);
                        albedo.rgb *= 0.72 + 0.38 * broad + 0.25 * grain + 0.20 * grit;
                    }
                    else if (matId == 16.0 || matId == 32.0 || matId == 34.0 || matId == 35.0)
                    {
                        float vein = smoothstep(0.04, 0.15, abs(WNoise(p * 4.0 + broad) - 0.5));
                        albedo.rgb *= (0.62 + 0.48 * broad + 0.20 * grain) * lerp(0.56, 1.0, vein);
                    }
                    else if (matId == 60.0)
                    {
                        float2 brick = p / float2(0.42, 0.22);
                        brick.x += 0.5 * fmod(floor(brick.y) + 10000.0, 2.0);
                        float2 f = frac(brick);
                        float seam = min(min(f.x, 1.0-f.x), min(f.y, 1.0-f.y));
                        float mortar = smoothstep(0.035, 0.10, seam);
                        albedo.rgb *= lerp(0.36, 0.80 + 0.40 * WHash(floor(brick)) + 0.12 * grain, mortar);
                    }
                }
                float gloss = _Glossiness;
                if (_AsciiFlat > 0.5 && nWS.y > 0.5)
                {   // the ground under the sky: white with snow (frost, then drifts; trodden paths greyer), dark and
                    // glinting when wet, puddles (rings from the rain and from steps), ice
                    float up = saturate((nWS.y - 0.55) * 3.0), flatK = saturate((nWS.y - 0.82) * 12.0);
                    float2 sp = i.positionWS.xz;
                    float n = frac(sin(dot(floor(sp * 3.0), float2(12.9898, 78.233))) * 43758.5453);
                    float sn = up * saturate(_AsciiSeason.w * 1.25 - 0.25 * n);
                    float tr = WxTrail(sp);
                    sn = max(sn, WxDrift(sp) * flatK);
                    albedo.rgb = lerp(albedo.rgb, lerp(half3(0.82, 0.86, 0.94), half3(0.55, 0.59, 0.67), tr), sn);
                    float wt = _AsciiWet * up * (1.0 - sn);
                    albedo.rgb *= 1.0 - 0.35 * wt; gloss = lerp(gloss, 0.8, wt);
                    float pn = WxNoise(sp);
                    float pd = saturate((_WxGround.x - pn) / 0.025) * flatK * (1.0 - sn);
                    float ic = saturate((_WxGround.y - pn) / 0.025) * flatK * (1.0 - sn);
                    if (pd > 0.001)
                    {
                        float2 cg = floor(sp * 2.0); float ph = frac(_AsciiTime * 1.1 + WxHash((int2)cg));
                        float rip = _WxState.y * exp(-abs(length(frac(sp * 2.0) - 0.5) - ph * 0.45) * 40.0) * (1.0 - ph);
                        for (int k = 0; k < 8; k++)
                        {
                            float4 r = _WaterRip[k]; float age = _AsciiTime - r.z;
                            if (age < 0.0 || age > 1.6 || r.w <= 0.0) continue;
                            rip += exp(-abs(length(sp - r.xy) - age * 0.7 * r.w) * 25.0) * (1.0 - age / 1.6) * 0.8;
                        }
                        albedo.rgb = lerp(albedo.rgb, half3(0.07, 0.09, 0.12) + rip * 0.35, pd); gloss = lerp(gloss, 0.95, pd);
                    }
                    albedo.rgb = lerp(albedo.rgb, half3(0.6, 0.7, 0.8), ic * 0.85); gloss = lerp(gloss, 0.9, ic);
                }
                else if (_AsciiActor > 0.5)
                {   // a creature out in the weather: soaked (darker, glossier), snow settling on what faces up
                    albedo.rgb *= 1.0 - 0.28 * _WxActor.x; gloss = lerp(gloss, 0.75, _WxActor.x * 0.8);
                    albedo.rgb = lerp(albedo.rgb, half3(0.85, 0.88, 0.95), saturate((nWS.y - 0.6) * 4.0) * _WxActor.y);
                }

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = nWS;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = 0;
                d.vertexLighting = half3(0, 0, 0);
                d.bakedGI = SampleSH(nWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo.rgb;
                s.metallic = _Metallic;
                s.specular = half3(0, 0, 0);
                s.smoothness = gloss;
                s.normalTS = half3(0, 0, 1);
                s.emission = _EmissionColor.rgb;
                s.occlusion = 1;
                s.alpha = 1;

                half4 c = UniversalFragmentPBR(d, s);
                // alpha: the view depth / 12 (what the characters test their visibility against, so no second "depth"
                // camera is needed), negative on walls and floors (they get no outline)
                return half4(c.rgb, (_AsciiFlat > 0.5 ? -1.0 : 1.0) * i.viewZ / 12.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert (Attributes i)
            {
                Varyings o;
                float3 positionWS = DashWS(ClothWS(TransformObjectToWorld(i.positionOS.xyz), i.positionOS.xyz));
                float3 normalWS = TransformObjectToWorldNormal(i.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                return o;
            }
            half4 frag (Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings vert (Attributes i) { Varyings o; o.positionWS = DashWS(ClothWS(TransformObjectToWorld(i.positionOS.xyz), i.positionOS.xyz)); o.positionCS = TransformWorldToHClip(o.positionWS); return o; }
            half frag (Varyings i) : SV_Target { SeeClip(i.positionWS, i.positionCS.xy); return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings vert (Attributes i) { Varyings o; o.positionWS = DashWS(ClothWS(TransformObjectToWorld(i.positionOS.xyz), i.positionOS.xyz)); o.positionCS = TransformWorldToHClip(o.positionWS); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag (Varyings i) : SV_Target { SeeClip(i.positionWS, i.positionCS.xy); return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
