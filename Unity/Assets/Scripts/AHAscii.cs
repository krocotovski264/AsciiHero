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
        public float exposure = 1f, actorBoost = 0f, bloom = 0.55f;
        public bool surfaceGlyphs = true;
        /// diagnostics: draw the plain 3D scene straight to the screen (no ASCII)
        public bool plain;
        bool builtSurface, builtPlain;
        public RenderTexture final;
        RenderTexture rtCol, rtND, rtAscii, rtB1, rtB2, rtFull;
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
            atlas.Apply(true, true);
            var rampMap = new float[32];
            for (int i = 0; i < MatId.RAMP.Length && i + 1 < 32; i++) rampMap[i + 1] = MatId.RAMP[i];
            asciiMat.SetFloatArray("_RampMap", rampMap);
            asciiMat.SetTexture("_Atlas", atlas);
            asciiMat.SetFloat("_NRows", 13);
            asciiMat.SetFloat("_EdgeK", 1);
            asciiMat.SetFloat("_Linear", linear ? 1 : 0);
            finalMat.SetFloat("_Linear", linear ? 1 : 0);
            Shader.SetGlobalTexture("_AsciiAtlas", atlas);
            Shader.SetGlobalFloat("_AsciiRows", 13);
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
            RenderPipelineManager.endContextRendering += OnEndContext;
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
            Shader.SetGlobalFloat(ND_PASS, c == ndCam ? 1f : 0f);
        }

        public void Dispose() { RenderPipelineManager.beginCameraRendering -= OnBeginCamera; RenderPipelineManager.endContextRendering -= OnEndContext; }

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
            if (plain)
            {
                if (!builtPlain) { builtPlain = true; cam.targetTexture = null; ndCam.enabled = false; glyphCam.enabled = false; cam.depth = 1; Debug.Log("ASCII выключен: обычная 3D-картинка"); }
                cam.rect = new Rect(0, 0, (float)w / Screen.width, 1);
                return;
            }
            if (builtPlain) { builtPlain = false; cam.rect = new Rect(0, 0, 1, 1); cam.depth = -3; ndCam.enabled = true; final = null; }
            if (w == W && h == H && c == cols && r == rows && final != null && builtSurface == surfaceGlyphs) { SyncND(); return; }
            W = w; H = h; cols = c; rows = r; builtSurface = surfaceGlyphs;
            cam.targetTexture = null; ndCam.targetTexture = null; glyphCam.targetTexture = null;
            Free(ref rtCol); Free(ref rtND); Free(ref rtAscii); Free(ref rtB1); Free(ref rtB2); Free(ref final); Free(ref rtFull);
            var half = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            if (surfaceGlyphs)
            {
                int hw = (W + 1) / 2, hh = (H + 1) / 2;
                rtCol = RT(hw, hh, half, FilterMode.Bilinear, 24, RenderTextureReadWrite.Linear);
                rtND = RT(hw, hh, half, FilterMode.Point, 24, RenderTextureReadWrite.Linear);
                rtFull = RT(W, H, half, FilterMode.Bilinear, 0, RenderTextureReadWrite.Linear);
                Shader.SetGlobalTexture("_AsciiLitTex", rtCol); Shader.SetGlobalTexture("_AsciiNDTex", rtND);
            }
            else
            {
                rtCol = RT(cols * 2, rows * 2, half, FilterMode.Bilinear, 24, RenderTextureReadWrite.Linear);
                rtND = RT(cols, rows, half, FilterMode.Point, 24, RenderTextureReadWrite.Linear);
            }
            rtAscii = RT(W, H, RenderTextureFormat.ARGB32, FilterMode.Bilinear, 0, RenderTextureReadWrite.Linear);
            rtB1 = RT((W + 3) / 4, (H + 3) / 4, half, FilterMode.Bilinear, 0, RenderTextureReadWrite.Linear);
            rtB2 = RT((W + 3) / 4, (H + 3) / 4, half, FilterMode.Bilinear, 0, RenderTextureReadWrite.Linear);
            final = RT(W, H, RenderTextureFormat.ARGB32, FilterMode.Point, 0, RenderTextureReadWrite.Default);
            cam.targetTexture = rtCol; ndCam.targetTexture = rtND;
            glyphCam.targetTexture = rtFull; glyphCam.enabled = surfaceGlyphs;
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
            ndCam.fieldOfView = cam.fieldOfView; ndCam.nearClipPlane = cam.nearClipPlane; ndCam.farClipPlane = cam.farClipPlane;
            ndCam.aspect = cam.aspect; ndCam.cullingMask = cam.cullingMask;
            ndCam.transform.localPosition = Vector3.zero; ndCam.transform.localRotation = Quaternion.identity;
            glyphCam.fieldOfView = cam.fieldOfView; glyphCam.nearClipPlane = cam.nearClipPlane; glyphCam.farClipPlane = cam.farClipPlane;
            glyphCam.aspect = cam.aspect;
            glyphCam.transform.localPosition = Vector3.zero; glyphCam.transform.localRotation = Quaternion.identity;
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
            float bw = rtB1.width, bh = rtB1.height;
            blurMat.SetVector("_Dir", new Vector4(1.6f / bw, 0)); Graphics.Blit(src, rtB1, blurMat);
            blurMat.SetVector("_Dir", new Vector4(0, 1.6f / bh)); Graphics.Blit(rtB1, rtB2, blurMat);
            blurMat.SetVector("_Dir", new Vector4(3.2f / bw, 0)); Graphics.Blit(rtB2, rtB1, blurMat);
            blurMat.SetVector("_Dir", new Vector4(0, 3.2f / bh)); Graphics.Blit(rtB1, rtB2, blurMat);
            finalMat.SetTexture("_BloomTex", rtB2); finalMat.SetFloat("_Bloom", bloom);
            Graphics.Blit(src, final, finalMat);
        }
    }
}
