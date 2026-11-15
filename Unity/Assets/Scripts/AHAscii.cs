// GPU ASCII renderer for URP. Two looks:
// surfaceGlyphs = true (default): models are BUILT from characters (GlyphLayer). The colour and ND cameras render
//   the lit scene and its depth at half resolution; the glyph camera draws only the character layer at full
//   resolution, each character reading its brightness and visibility from those two targets.
// surfaceGlyphs = false (beta 0.8 screen grid): two cameras render every frame into low-resolution targets:
//   colour camera -> rtCol (2x the glyph grid, lit scene)
//   ND camera     -> rtND  (glyph grid: view normal xy, depth / 12, material id) — AsciiHero/Lit switches its output
//                    through the global _AsciiNDPass, set in RenderPipelineManager.beginCameraRendering
// Composite() then turns them into coloured glyphs, adds bloom and produces `final` (drawn by the UI).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AsciiHero
{
    public class AsciiRenderer
    {
        public int cols, rows, W, H;
        public float exposure = 1f, actorBoost = 0f, bloom = 0.32f;
        public bool surfaceGlyphs = true;
        /// high quality: the lit colour the characters take their brightness from is rendered at full resolution (else half)
        public bool fullLit; bool builtFull;
        /// the lit colour (what the characters take their brightness, depth and the outline from) is rendered at 1/litDiv of
        /// the output size. Measured on a Galaxy A55: at full size it cost about a quarter of the GPU frame for no visible gain
        public int litDiv = 2; int builtDiv;
        /// diagnostics: draw the plain 3D scene straight to the screen (no ASCII)
        public bool plain;
        bool builtSurface, builtPlain;
        /// outline along the edges of objects and creatures (needs the depth in the lit colour's alpha)
        public bool outline = true;
        /// the scene depth is taken from the alpha of the lit colour, so the separate depth ("ND") camera is not
        /// rendered in the glyph look. Checked once on the device (SelfCheck); if the alpha does not hold depth there,
        /// the ND camera is used as before.
        public bool depthFromLit = true; int checkFrames = -1, checksLeft = 3;
        /// diagnostics (performance test)
        public bool dbgNoGlyphs, dbgNoBloom, dbgForceND;
        public string formats = "";
        public RenderTexture final;
        RenderTexture rtCol, rtND, rtAscii, rtB1, rtB2, rtFull;
        /// planar reflection in the water: the scene mirrored in the plane y = waterY, drawn in characters by a second pair
        /// of cameras into rtReflFull (0.8 of the output size); the water's surfaces show it (AsciiHero/WaterRefl)
        public bool reflections; public bool waterOn; public float waterY;
        Camera reflCam, reflGlyphCam; RenderTexture rtReflCol, rtReflFull;
        /// "retyping": the last picture is kept and a front runs out from a point of the screen; behind it the new place,
        /// on it a band of characters flicking through the alphabet, ahead of it the old picture (doors between places)
        RenderTexture rtTrans; float transT = 9f; Vector2 transO;
        public void StartReprint(Vector2 originUV)
        {
            if (final == null) return;
            if (rtTrans == null || rtTrans.width != final.width || rtTrans.height != final.height)
            { Free(ref rtTrans); rtTrans = RT(final.width, final.height, RenderTextureFormat.ARGB32, FilterMode.Point, 0, RenderTextureReadWrite.Default); }
            var prev = RenderTexture.active; Graphics.Blit(final, rtTrans); RenderTexture.active = prev;
            transT = -0.04f; transO = originUV;
        }
        static readonly int REFL_PASS = Shader.PropertyToID("_AsciiReflPass");
        readonly Material asciiMat, blurMat, finalMat; readonly Texture2D atlas;
        readonly Camera cam, ndCam, glyphCam;
        public float depthRef = 4f;
        readonly bool linear;
        static readonly int ND_PASS = Shader.PropertyToID("_AsciiNDPass");

        public AsciiRenderer(Camera sceneCam)
        {
            cam = sceneCam;
            linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            asciiMat = new Material(Load("Hidden/AsciiHero/Composite"));
            blurMat = new Material(Load("Hidden/AsciiHero/Blur"));
            finalMat = new Material(Load("Hidden/AsciiHero/Final"));
            atlas = new Texture2D(2, 2, TextureFormat.RGBA32, true, true);
            atlas.LoadImage(Resources.Load<TextAsset>("glyphs").bytes, false);
            atlas.filterMode = FilterMode.Trilinear; atlas.wrapMode = TextureWrapMode.Clamp;
            InkBoxes();
            atlas.Apply(true, true);
            var rampMap = new float[32];
            for (int i = 0; i < MatId.RAMP.Length && i + 1 < 32; i++) rampMap[i + 1] = MatId.RAMP[i];
            asciiMat.SetFloatArray("_RampMap", rampMap);
            asciiMat.SetTexture("_Atlas", atlas);
            asciiMat.SetFloat("_NRows", 14);
            asciiMat.SetFloat("_EdgeK", 1);
            asciiMat.SetFloat("_Linear", linear ? 1 : 0);
            finalMat.SetFloat("_Linear", linear ? 1 : 0);
            Shader.SetGlobalTexture("_AsciiAtlas", atlas);
            Shader.SetGlobalFloat("_AsciiRows", 14);
            Shader.SetGlobalFloat("_AsciiLinear", linear ? 1 : 0);

            Setup(cam, -3, true);
            ndCam = new GameObject("NDCamera").AddComponent<Camera>();
            ndCam.transform.SetParent(cam.transform, false);
            Setup(ndCam, -2, false);
            glyphCam = new GameObject("GlyphCamera").AddComponent<Camera>();
            glyphCam.transform.SetParent(cam.transform, false);
            Setup(glyphCam, -1, false);
            glyphCam.cullingMask = 1 << GlyphLayer.LAYER;
            cam.cullingMask = ~(1 << GlyphLayer.LAYER);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
            RenderPipelineManager.endContextRendering += OnEndContext;
        }

        // Where the ink of every atlas glyph lies inside its cell. The glyph shaders draw only that box: the empty margins
        // of the cells were about half of all the pixels the glyph layer blended (measured on the atlas: the surface
        // glyphs fill 52 % of their cells on average with the safety margin, a '.' a tenth).
        void InkBoxes()
        {
            const int COLS = 16, ROWS = 14; var ink = new Vector4[COLS * ROWS];
            for (int k = 0; k < ink.Length; k++) ink[k] = new Vector4(0, 0, 1, 1);                 // (whole cells if the atlas cannot be read)
            try
            {
                var px = atlas.GetPixels32(); int AW = atlas.width, AH = atlas.height, cw = AW / COLS, chh = AH / ROWS; int empty = 0; float area = 0;
                for (int r = 0; r < ROWS; r++)
                    for (int c = 0; c < COLS; c++)
                    {
                        int x0 = cw, x1 = -1, y0 = chh, y1 = -1, by = AH - (r + 1) * chh, bx = c * cw;      // (texture rows run upwards)
                        for (int y = 0; y < chh; y++)
                            for (int x = 0; x < cw; x++)
                                if (px[(by + y) * AW + bx + x].r > 30) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
                        if (x1 < 0) { ink[r * COLS + c] = new Vector4(0.5f, 0.5f, 0.5f, 0.5f); empty++; continue; }
                        var b = new Vector4(Mathf.Max(0, x0 - 4) / (float)cw, Mathf.Max(0, y0 - 5) / (float)chh, Mathf.Min(cw, x1 + 5) / (float)cw, Mathf.Min(chh, y1 + 6) / (float)chh);
                        ink[r * COLS + c] = b; area += (b.z - b.x) * (b.w - b.y);
                    }
                // sanity: a readable atlas has a few blank glyphs (the spaces) and plenty of ink; anything else -> whole cells
                if (empty > 60 || area < 20f) { for (int k = 0; k < ink.Length; k++) ink[k] = new Vector4(0, 0, 1, 1); Debug.LogWarning("Атлас символов прочитан странно (пустых " + empty + "): символы рисуются целыми клетками"); }
                else Debug.Log("Символы рисуются по границам своих штрихов: в среднем " + (area / (ink.Length - empty) * 100).ToString("0") + " % клетки");
            }
            catch (System.Exception e) { Debug.LogWarning("Границы символов не посчитаны (" + e.Message + "): символы рисуются целыми клетками"); }
            Shader.SetGlobalVectorArray("_GlyphInk", ink);
        }

        // Glyphs + bloom are composed right after the cameras have rendered, outside of OnGUI:
        // Graphics.Blit switches the active render target, and inside OnGUI that sent the whole
        // interface into the texture instead of the screen (black screen on device).
        int composedFrame = -1;
        void OnEndContext(ScriptableRenderContext ctx, List<Camera> cameras)
        {
            if (plain || final == null || composedFrame == Time.frameCount || !cameras.Contains(cam)) return;
            composedFrame = Time.frameCount;
            var prev = RenderTexture.active;
            try { Composite(); }
            catch (System.Exception e) { Debug.LogException(e); }
            RenderTexture.active = prev;
        }

        static void Setup(Camera c, float depth, bool shadows)
        {
            c.depth = depth; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0, 0, 0, 0);
            c.allowHDR = false; c.allowMSAA = false;
            var d = c.GetUniversalAdditionalCameraData();
            d.renderShadows = shadows; d.renderPostProcessing = false; d.antialiasing = AntialiasingMode.None;
            d.requiresColorOption = CameraOverrideOption.Off; d.requiresDepthOption = CameraOverrideOption.Off;
            d.stopNaN = false; d.dithering = false;
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c != null && (c == reflCam || c == reflGlyphCam))
            {   // the mirrored cameras: their own lit target, and the winding flips in a mirror
                GL.invertCulling = true;
                Shader.SetGlobalFloat(ND_PASS, 0f); Shader.SetGlobalFloat(REFL_PASS, 1f);
                Shader.SetGlobalTexture("_AsciiLitTex", rtReflCol); Shader.SetGlobalFloat("_AsciiDepthLit", 1);
                Shader.SetGlobalFloat("_AsciiExposure", exposure); Shader.SetGlobalFloat("_AsciiDepthRef", depthRef);
                return;
            }
            if (c != cam && c != ndCam && c != glyphCam) return;       // another renderer's camera (the backpack's projection has its own)
            GL.invertCulling = false;
            Shader.SetGlobalFloat(REFL_PASS, 0f);
            Shader.SetGlobalFloat(ND_PASS, c == ndCam ? 1f : 0f);
            // what the shaders read of this renderer: its targets and settings (two renderers can draw in one frame)
            if (builtSurface && rtCol != null) { Shader.SetGlobalTexture("_AsciiLitTex", rtCol); Shader.SetGlobalTexture("_AsciiNDTex", rtND); }
            Shader.SetGlobalFloat("_AsciiDepthLit", depthFromLit && !dbgForceND && builtSurface ? 1 : 0);
            Shader.SetGlobalFloat("_AsciiExposure", exposure); Shader.SetGlobalFloat("_AsciiDepthRef", depthRef);
        }
        void OnEndCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c != null && (c == reflCam || c == reflGlyphCam)) { GL.invertCulling = false; Shader.SetGlobalFloat(REFL_PASS, 0f); }
        }
        /// the mirrored cameras follow the main one each frame (reflected in the water plane, clipped at it)
        void UpdateReflection()
        {
            bool on = reflections && waterOn && enabledAll && builtSurface && rtReflFull != null && !plain;
            Shader.SetGlobalFloat("_WaterReflOn", on ? 1 : 0);
            if (reflCam == null) { if (!on) return; MakeReflectionCameras(); }
            reflCam.enabled = on; reflGlyphCam.enabled = on && glyphCam.enabled;
            if (!on) return;
            Shader.SetGlobalTexture("_WaterReflTex", rtReflFull);
            var R = Matrix4x4.identity; R.m11 = -1; R.m13 = 2 * waterY;            // the mirror y -> 2h - y
            var view = cam.worldToCameraMatrix * R;
            var cp = cam.transform.position; cp.y = 2 * waterY - cp.y;
            foreach (var rc in new[] { reflCam, reflGlyphCam })
            {
                rc.transform.position = cp;
                rc.fieldOfView = cam.fieldOfView; rc.aspect = cam.aspect; rc.nearClipPlane = cam.nearClipPlane; rc.farClipPlane = cam.farClipPlane;
                rc.worldToCameraMatrix = view; rc.ResetProjectionMatrix();
                // an oblique near plane on the water: nothing under the surface shows in the mirror
                Vector3 pos = view.MultiplyPoint(new Vector3(0, waterY + 0.015f, 0)), n = view.MultiplyVector(Vector3.up).normalized;
                rc.projectionMatrix = rc.CalculateObliqueMatrix(new Vector4(n.x, n.y, n.z, -Vector3.Dot(pos, n)));
            }
            reflCam.cullingMask = cam.cullingMask; reflGlyphCam.cullingMask = glyphCam.cullingMask;
        }
        void MakeReflectionCameras()
        {
            reflCam = new GameObject("ReflCamera").AddComponent<Camera>(); Setup(reflCam, -5, false);
            reflGlyphCam = new GameObject("ReflGlyphCamera").AddComponent<Camera>(); Setup(reflGlyphCam, -4, false);
            reflCam.targetTexture = rtReflCol; reflGlyphCam.targetTexture = rtReflFull;
        }
        /// switch the whole renderer on or off (its cameras stop rendering)
        public void SetEnabled(bool on)
        {
            if (enabledAll == on) return; enabledAll = on;
            cam.enabled = on; if (on) SyncND(); else { ndCam.enabled = false; glyphCam.enabled = false; }
        }
        bool enabledAll = true;

        public void Dispose() { RenderPipelineManager.beginCameraRendering -= OnBeginCamera; RenderPipelineManager.endCameraRendering -= OnEndCamera; RenderPipelineManager.endContextRendering -= OnEndContext; }

        static Shader Load(string n) { var s = Shader.Find(n); if (s == null) Debug.LogError("Shader not found: " + n); return s; }

        static RenderTexture RT(int w, int h, RenderTextureFormat f, FilterMode fm, int depth, RenderTextureReadWrite rw)
        {
            var r = new RenderTexture(Mathf.Max(1, w), Mathf.Max(1, h), depth, f, rw) { filterMode = fm, wrapMode = TextureWrapMode.Clamp, useMipMap = false, antiAliasing = 1 };
            r.Create(); return r;
        }
        static void Free(ref RenderTexture r) { if (r != null) { r.Release(); Object.Destroy(r); r = null; } }

        /// size of the output in pixels and the width of one glyph cell in pixels (height = width / 0.6)
        public void SetSize(int w, int h, float cellPx)
        {
            w = Mathf.Max(8, w); h = Mathf.Max(8, h);
            float cw = Mathf.Max(3f, cellPx), chh = cw / 0.6f;
            int c = Mathf.Max(8, Mathf.FloorToInt(w / cw)), r = Mathf.Max(8, Mathf.FloorToInt(h / chh));
            Shader.SetGlobalFloat("_AsciiExposure", exposure);
            Shader.SetGlobalFloat("_AsciiDepthRef", depthRef);
            Shader.SetGlobalFloat("_AsciiTime", Time.time);
            if (plain)
            {
                if (!builtPlain) { builtPlain = true; cam.targetTexture = null; ndCam.enabled = false; glyphCam.enabled = false; cam.depth = 1; Debug.Log("ASCII выключен: обычная 3D-картинка"); }
                cam.rect = new Rect(0, 0, (float)w / Screen.width, 1);
                return;
            }
            if (builtPlain) { builtPlain = false; cam.rect = new Rect(0, 0, 1, 1); cam.depth = -3; final = null; }
            if (w == W && h == H && c == cols && r == rows && final != null && builtSurface == surfaceGlyphs && builtDiv == litDiv) { SyncND(); return; }
            W = w; H = h; cols = c; rows = r; builtSurface = surfaceGlyphs; builtFull = fullLit; builtDiv = litDiv;
            cam.targetTexture = null; ndCam.targetTexture = null; glyphCam.targetTexture = null;
            if (reflCam != null) { reflCam.targetTexture = null; reflGlyphCam.targetTexture = null; }
            Free(ref rtCol); Free(ref rtND); Free(ref rtAscii); Free(ref rtB1); Free(ref rtB2); Free(ref final); Free(ref rtFull); Free(ref rtReflCol); Free(ref rtReflFull);
            var half = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            var lite = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float) && SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.RGB111110Float)
                ? RenderTextureFormat.RGB111110Float : half;
            if (surfaceGlyphs)
            {
                int dv = Mathf.Clamp(litDiv, 2, 4), hw = (W + dv - 1) / dv, hh = (H + dv - 1) / dv;
                rtCol = RT(hw, hh, half, FilterMode.Bilinear, 24, RenderTextureReadWrite.Linear);
                rtND = RT(hw, hh, half, FilterMode.Point, 24, RenderTextureReadWrite.Linear);
                // a camera target must have a depth buffer in URP's render graph, even though glyphs don't test depth.
                // The glyph layer is the full-resolution target every character is blended into: a packed 32-bit float
                // format halves the memory traffic of the 64-bit one where the device can blend into it
                rtFull = RT(W, H, lite, FilterMode.Bilinear, 16, RenderTextureReadWrite.Linear);
                if (reflections)
                {   // the mirror at 0.8 of the picture's resolution (its lit colour at 0.8 of the lit one)
                    rtReflCol = RT(Mathf.Max(8, hw * 4 / 5), Mathf.Max(8, hh * 4 / 5), half, FilterMode.Bilinear, 24, RenderTextureReadWrite.Linear);
                    rtReflFull = RT(Mathf.Max(8, W * 4 / 5), Mathf.Max(8, H * 4 / 5), lite, FilterMode.Bilinear, 16, RenderTextureReadWrite.Linear);
                    if (reflCam != null) { reflCam.targetTexture = rtReflCol; reflGlyphCam.targetTexture = rtReflFull; }
                }
                formats = "глифы " + lite + ", свет " + half + " " + hw + "×" + hh; checkFrames = 12;
                Shader.SetGlobalTexture("_AsciiLitTex", rtCol); Shader.SetGlobalTexture("_AsciiNDTex", rtND);
            }
            else
            {
                rtCol = RT(cols * 2, rows * 2, half, FilterMode.Bilinear, 24, RenderTextureReadWrite.Linear);
                rtND = RT(cols, rows, half, FilterMode.Point, 24, RenderTextureReadWrite.Linear);
            }
            rtAscii = RT(W, H, RenderTextureFormat.ARGB32, FilterMode.Bilinear, 0, RenderTextureReadWrite.Linear);
            // (the glow is blurred at a sixth of the size: the same reach on screen for under half the work of a quarter)
            rtB1 = RT((W + 5) / 6, (H + 5) / 6, lite, FilterMode.Bilinear, 0, RenderTextureReadWrite.Linear);
            rtB2 = RT((W + 5) / 6, (H + 5) / 6, lite, FilterMode.Bilinear, 0, RenderTextureReadWrite.Linear);
            final = RT(W, H, RenderTextureFormat.ARGB32, FilterMode.Point, 0, RenderTextureReadWrite.Default);
            cam.targetTexture = rtCol; ndCam.targetTexture = rtND;
            glyphCam.targetTexture = rtFull; glyphCam.enabled = surfaceGlyphs;
            finalMat.SetVector("_LitPx", new Vector4(1f / rtCol.width, 1f / rtCol.height, 0, 0));
            asciiMat.SetVector("_Grid", new Vector4(cols, rows, 0, 0));
            asciiMat.SetVector("_CellPx", new Vector4((float)W / cols, (float)H / rows, 0, 0));
            asciiMat.SetVector("_OutPx", new Vector4(W, H, 0, 0));
            cam.aspect = (float)W / H;
            Debug.Log("ASCII " + (surfaceGlyphs ? "символы на моделях" : "сетка экрана") + ": вывод " + W + "×" + H + ", сетка " + cols + "×" + rows + ", формат " + half);
            SyncND();
        }

        // the ND camera follows the colour camera exactly
        void SyncND()
        {
            UpdateReflection();
            bool lit = depthFromLit && !dbgForceND && builtSurface;
            ndCam.enabled = !lit && enabledAll; glyphCam.enabled = builtSurface && !dbgNoGlyphs && enabledAll;
            Shader.SetGlobalFloat("_AsciiDepthLit", lit ? 1 : 0);
            finalMat.SetFloat("_Outline", lit && outline ? 1 : 0);
            ndCam.fieldOfView = cam.fieldOfView; ndCam.nearClipPlane = cam.nearClipPlane; ndCam.farClipPlane = cam.farClipPlane;
            ndCam.aspect = cam.aspect; ndCam.cullingMask = cam.cullingMask;
            ndCam.transform.localPosition = Vector3.zero; ndCam.transform.localRotation = Quaternion.identity;
            glyphCam.fieldOfView = cam.fieldOfView; glyphCam.nearClipPlane = cam.nearClipPlane; glyphCam.farClipPlane = cam.farClipPlane;
            glyphCam.aspect = cam.aspect;
            glyphCam.transform.localPosition = Vector3.zero; glyphCam.transform.localRotation = Quaternion.identity;
        }

        /// Once after the targets are built: does the alpha of the lit colour really hold the depth on this device?
        /// (It does when URP draws straight into the target; if some pass in between rewrote the alpha, every sample
        /// would be the same.) Call from LateUpdate.
        public void SelfCheck()
        {
            if (!depthFromLit || !builtSurface || rtCol == null || checkFrames < 0 || checksLeft <= 0) return;
            if (checkFrames-- > 0) return;
            checksLeft--;
            RenderTexture small = null; Texture2D tex = null; var prev = RenderTexture.active;
            try
            {
                small = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Graphics.Blit(rtCol, small);
                tex = new Texture2D(32, 32, TextureFormat.RGBAHalf, false, true);
                RenderTexture.active = small; tex.ReadPixels(new Rect(0, 0, 32, 32), 0, 0, false); tex.Apply(false);
                var px = tex.GetPixels(); float lo = 1e9f, hi = -1e9f; int non0 = 0;
                foreach (var c in px) { float a = Mathf.Abs(c.a); if (a > 1e-4f) { non0++; lo = Mathf.Min(lo, a); hi = Mathf.Max(hi, a); } }
                if (non0 < 8) { if (checksLeft > 0) checkFrames = 30; else Fail("в кадре не нашлось поверхностей"); }
                else if (hi - lo < 1e-3f) Fail("альфа одинакова по всему кадру (" + lo.ToString("0.###") + ")");
                else { checksLeft = 0; Debug.Log("Глубина сцены берётся из альфы цвета (отдельная камера глубины выключена): проверка пройдена, " + non0 + " точек, " + (lo * 12).ToString("0.0") + "–" + (hi * 12).ToString("0.0") + " м"); }
            }
            catch (System.Exception e) { Fail(e.Message); }
            RenderTexture.active = prev;
            if (small != null) RenderTexture.ReleaseTemporary(small);
            if (tex != null) Object.Destroy(tex);
        }
        void Fail(string why)
        {
            depthFromLit = false; checksLeft = 0;
            Debug.LogWarning("Глубина из альфы цвета не подтвердилась (" + why + "): включена отдельная камера глубины, обводка выключена");
        }

        /// glyphs + bloom; runs after the cameras have rendered this frame
        void Composite()
        {
            if (final == null) return;
            RenderTexture src = rtAscii;
            if (builtSurface) src = rtFull;
            else
            {
                asciiMat.SetTexture("_ColTex", rtCol); asciiMat.SetTexture("_NDTex", rtND);
                asciiMat.SetFloat("_Exposure", exposure); asciiMat.SetFloat("_ActorBoost", actorBoost);
                Graphics.Blit(null, rtAscii, asciiMat);
            }
            float bw = rtB1.width, bh = rtB1.height; bool glow = bloom > 0.001f && !dbgNoBloom;
            if (glow)
            {
                blurMat.SetVector("_Dir", new Vector4(1.1f / bw, 0)); Graphics.Blit(src, rtB1, blurMat);
                blurMat.SetVector("_Dir", new Vector4(0, 1.1f / bh)); Graphics.Blit(rtB1, rtB2, blurMat);
                blurMat.SetVector("_Dir", new Vector4(2.2f / bw, 0)); Graphics.Blit(rtB2, rtB1, blurMat);
                blurMat.SetVector("_Dir", new Vector4(0, 2.2f / bh)); Graphics.Blit(rtB1, rtB2, blurMat);
            }
            finalMat.SetTexture("_BloomTex", rtB2); finalMat.SetFloat("_Bloom", glow ? bloom : 0);
            if (transT < 1.6f && rtTrans != null)
            {
                transT += Time.unscaledDeltaTime / 0.85f;
                float asp = (float)W / Mathf.Max(1, H), md = 0;
                foreach (var c in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) }) md = Mathf.Max(md, new Vector2((c.x - transO.x) * asp, c.y - transO.y).magnitude);
                finalMat.SetTexture("_TransTex", rtTrans); finalMat.SetFloat("_TransT", transT);
                finalMat.SetVector("_TransO", new Vector4(transO.x, transO.y, asp, md)); finalMat.SetVector("_TransGrid", new Vector4(cols, rows, 0, 0));
            }
            else finalMat.SetFloat("_TransT", 9f);
            Graphics.Blit(src, final, finalMat);
        }
    }
}
