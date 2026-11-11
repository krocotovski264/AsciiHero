// GPU ASCII renderer for URP.
// Two cameras render every frame into low-resolution targets:
//   colour camera -> rtCol (2x the glyph grid, lit scene)
//   ND camera     -> rtND  (glyph grid: view normal xy, depth / 12, material id) — AsciiHero/Lit switches its output
//                    through the global _AsciiNDPass, set in RenderPipelineManager.beginCameraRendering
// Composite() then turns them into coloured glyphs, adds bloom and produces `final` (drawn by the UI).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AsciiHero
{
    public class AsciiRenderer
    {
        public int cols, rows, W, H;
        public float exposure = 1f, actorBoost = 0f, bloom = 0.55f;
        public RenderTexture final;
        RenderTexture rtCol, rtND, rtAscii, rtB1, rtB2;
        readonly Material asciiMat, blurMat, finalMat; readonly Texture2D atlas;
        readonly Camera cam, ndCam;
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

            Setup(cam, -3, true);
            ndCam = new GameObject("NDCamera").AddComponent<Camera>();
            ndCam.transform.SetParent(cam.transform, false);
            Setup(ndCam, -2, false);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
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

        public void Dispose() { RenderPipelineManager.beginCameraRendering -= OnBeginCamera; }

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
            if (w == W && h == H && c == cols && r == rows && final != null) { SyncND(); return; }
            W = w; H = h; cols = c; rows = r;
            cam.targetTexture = null; ndCam.targetTexture = null;
            Free(ref rtCol); Free(ref rtND); Free(ref rtAscii); Free(ref rtB1); Free(ref rtB2); Free(ref final);
            var half = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            rtCol = RT(cols * 2, rows * 2, half, FilterMode.Bilinear, 24, RenderTextureReadWrite.Linear);
            rtND = RT(cols, rows, half, FilterMode.Point, 24, RenderTextureReadWrite.Linear);
            rtAscii = RT(W, H, RenderTextureFormat.ARGB32, FilterMode.Bilinear, 0, RenderTextureReadWrite.Linear);
            rtB1 = RT((W + 3) / 4, (H + 3) / 4, half, FilterMode.Bilinear, 0, RenderTextureReadWrite.Linear);
            rtB2 = RT((W + 3) / 4, (H + 3) / 4, half, FilterMode.Bilinear, 0, RenderTextureReadWrite.Linear);
            final = RT(W, H, RenderTextureFormat.ARGB32, FilterMode.Point, 0, RenderTextureReadWrite.Default);
            cam.targetTexture = rtCol; ndCam.targetTexture = rtND;
            asciiMat.SetVector("_Grid", new Vector4(cols, rows, 0, 0));
            asciiMat.SetVector("_CellPx", new Vector4((float)W / cols, (float)H / rows, 0, 0));
            asciiMat.SetVector("_OutPx", new Vector4(W, H, 0, 0));
            cam.aspect = (float)W / H;
            SyncND();
        }

        // the ND camera follows the colour camera exactly
        void SyncND()
        {
            ndCam.fieldOfView = cam.fieldOfView; ndCam.nearClipPlane = cam.nearClipPlane; ndCam.farClipPlane = cam.farClipPlane;
            ndCam.aspect = cam.aspect; ndCam.cullingMask = cam.cullingMask;
            ndCam.transform.localPosition = Vector3.zero; ndCam.transform.localRotation = Quaternion.identity;
        }

        /// glyphs + bloom; call after the cameras have rendered this frame (OnGUI repaint)
        public void Composite()
        {
            if (final == null) return;
            asciiMat.SetTexture("_ColTex", rtCol); asciiMat.SetTexture("_NDTex", rtND);
            asciiMat.SetFloat("_Exposure", exposure); asciiMat.SetFloat("_ActorBoost", actorBoost);
            Graphics.Blit(null, rtAscii, asciiMat);
            float bw = rtB1.width, bh = rtB1.height;
            blurMat.SetVector("_Dir", new Vector4(1.6f / bw, 0)); Graphics.Blit(rtAscii, rtB1, blurMat);
            blurMat.SetVector("_Dir", new Vector4(0, 1.6f / bh)); Graphics.Blit(rtB1, rtB2, blurMat);
            blurMat.SetVector("_Dir", new Vector4(3.2f / bw, 0)); Graphics.Blit(rtB2, rtB1, blurMat);
            blurMat.SetVector("_Dir", new Vector4(0, 3.2f / bh)); Graphics.Blit(rtB1, rtB2, blurMat);
            finalMat.SetTexture("_BloomTex", rtB2); finalMat.SetFloat("_Bloom", bloom);
            Graphics.Blit(rtAscii, final, finalMat);
        }
    }
}
