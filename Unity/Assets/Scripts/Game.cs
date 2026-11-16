// Game bootstrap: character editor + tavern, cameras, lights, IMGUI interface, touch controls.
// Nothing has to be set up in the scene: the game creates itself after any scene loads.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AsciiHero
{
    public class Game : MonoBehaviour
    {
        public const string VERSION = "beta 0.62 · Unity";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            LogHub.Install();
            if (FindAny<Game>() != null) return;
            // the default scene's camera and light are not used
            foreach (var c in FindAll<Camera>()) c.gameObject.SetActive(false);
            foreach (var l in FindAll<Light>()) l.gameObject.SetActive(false);
            new GameObject("AsciiHero").AddComponent<Game>();
        }
        static T FindAny<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindAnyObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }
        static T[] FindAll<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<T>();
#endif
        }

        // ---------- state ----------
        CharConfig cfg = new CharConfig { sex = 'm', height = 180, build = 1, armor = "chain", weapon = "shield" };
        Camera display, sceneCam; AsciiRenderer ascii;
        Mats mats, actorMats;
        Character hero; MotionActor player;
        bool world; Tavern tavern; Field field; Cave cave; Forest forest; Location loc; readonly List<Location> outdoor = new List<Location>(); readonly List<Mockup> mockups = new List<Mockup>(); Rect lastView = new Rect(-1e5f, -1e5f, 2e5f, 2e5f); Location waterSeen; readonly Plane[] mirPlanes = new Plane[6]; float doorCd, leafClear; Light moonLight; Character bartenderChar; MotionActor bartender;
        // integrity instead of a health bar: five limbs (head, armL, armR, legL, legR), each worth 20 %
        readonly Vector4[] pushPts = new Vector4[4]; static readonly Vector4 FAR = new Vector4(9999, 0, 9999, 0);
        int hp = 100; readonly bool[] limbs = { true, true, true, true, true }; float respawnT; Vector3 heroTrail;
        int LimbCount() { int n = 0; foreach (var b in limbs) if (b) n++; return n; }
        GameObject editorRoot, worldLightsRoot; Light key, rim, fill, heroLight;
        GameObject floor;
        bool rebuildPending;
        // body customisation UI
        Rect bodyList; readonly List<Rect> bodyTracks = new List<Rect>(); readonly List<int> bodyTrackIx = new List<int>(); Rect bodyTrackHit;
        float bodyScroll, bodyScrollMax, bodyBuiltAt; bool bodyDirty, setDev, presetOpen; int presetIx = 2; readonly string[] devText = new string[64]; readonly string[] devShown = new string[64];
        readonly List<Rect> bodyFields = new List<Rect>(); TouchScreenKeyboard kb; int kbIx = -1;
        bool InField(Vector2 p) { foreach (var r in bodyFields) if (r.Contains(p)) return true; return false; }
        void OpenKeyboard(int ix)
        {
            if (!TouchScreenKeyboard.isSupported) { ShowToast("Ввод чисел работает на телефоне (экранная клавиатура)"); return; }
            float raw = ix == 0 ? cfg.height : cfg.body[ix];
            kb = TouchScreenKeyboard.Open(raw.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), TouchScreenKeyboardType.NumbersAndPunctuation, false, false, false, false, Body.ALL[ix].label);
            kbIx = ix;
        }
        void KeyboardUpdate()
        {
            if (kb == null) return;
            if (kb.status == TouchScreenKeyboard.Status.Done)
            {
                float fv;
                if (kbIx >= 0 && float.TryParse((kb.text ?? "").Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fv))
                    SetBodyValue(kbIx, Mathf.Clamp(fv, Body.DevMin(kbIx), Body.DevMax(kbIx)));
                kb = null;
            }
            else if (kb.status == TouchScreenKeyboard.Status.Canceled || kb.status == TouchScreenKeyboard.Status.LostFocus) kb = null;
        }
        void SetBodyValue(int ix, float val)
        {
            if (ix == 0) { SetHeight(val); return; }
            if (cfg.body == null) ResetBody();
            if (cfg.body[ix] == val) return;
            cfg.body[ix] = val; bodyDirty = true; presetIx = -1;
        }
        /// the number shown next to a slider: the size it stands for (cm on the built body) or the value itself
        string BodyText(int ix, out float shown)
        {
            var bp = Body.ALL[ix]; float v = ix == 0 ? cfg.height : cfg.body[ix];
            if (bp.factor && hero != null && hero.meas != null && hero.built != null && hero.built[ix] != 0)
            {
                float s = cfg.height / 100f / Mathf.Max(0.5f, hero.baseHeight), sc = bp.girth ? 1 + (s - 1) * 0.45f : s;
                shown = hero.meas[ix] * (v / hero.built[ix]) * sc * 100f;
                return shown.ToString("0.#") + " см";
            }
            shown = v; return (bp.unit == "°" && v > 0 ? "+" : "") + v.ToString("0.#") + " " + bp.unit;
        }
        bool useAnimator; RuntimeAnimatorController locoCtrl;
        int animMode;   // 0 = phase-matched loops (default), 1 = motion matching, 2 = Animator Controller
        static readonly string[] ANIM_NAME = { "АНИМ: ПЛАВНАЯ", "АНИМ: MM", "АНИМ: ANIMATOR" };   // Animator Controller mode (Tools -> Patch Project Locomotion)
        float tNow;

        // editor camera (web coordinates; mirrored on X when applied)
        struct OrbitCam { public float yaw, pitch, dist, ty; }
        static readonly OrbitCam CAM0 = new OrbitCam { yaw = 0.5f, pitch = 0.1f, dist = 3.7f, ty = 0.95f };
        OrbitCam cam = CAM0;
        bool autoRotate = true, interacting; float vel, lastInteract, lastTap = -1;
        float pinchD0, pinchDist0, pinchMy0, pinchTy0; bool pinching;
        // world view: two fingers (or the wheel) zoom in up to twice as close
        float worldZoom = 1, worldZoomT = 1, wPinchD0, wPinchZ0; bool wPinching; readonly List<Pointer> camPtrs = new List<Pointer>();

        // world camera
        // MOBA camera: fixed (no rotation), high and far, looking down at about 55 degrees
        const float ISO_LOD = 2.5f / 1.5f; float isoYaw = 0, isoPitch = 0.96f, isoYawT = 0, isoPitchT = 0.96f; const float ISO_DIST = 7.6f * ISO_LOD;
        Vector3 pos, camTgt; bool lockOn; float labelT; string animLabel = "idle";
        // controls (0.38): left half - an invisible stick that appears under the finger, a double tap (tap, then press
        // and drag) runs; right half - a short tap attacks, holding blocks, a swipe dashes that way; a tap on the hero
        // opens the backpack. In a fight the hero turns to the nearest creature within 5 m by himself.
        const float AIM_R = 5f;
        float lastLeftTapT = -9; Vector2 lastLeftTapPos; bool stickRunArmed;
        bool invOpen, menuFromWorld; int saveMode;          // saves page: 0 both, 1 save, 2 load
        readonly List<string> inventory = new List<string>();
        readonly string[] saveInfo = new string[SAVE_SLOTS]; readonly bool[] saveHas = new bool[SAVE_SLOTS]; bool saveInfoDirty = true; const int SAVE_SLOTS = 3;
        // the lake: rings from the hero's steps (x, z, birth time, strength)
        readonly Vector4[] ripples = new Vector4[8]; int rippleIx; float rippleT;
        GUIStyle sDoll, sBag;
        float invT, heroSink;
        // the shadow pocket (the hero's menu): time since it opened, how far it has blown apart closing (-1 = open), the
        // window shown (0 the hero's state, 1 the backpack); the interface's opacity while it appears
        float pocketT, pocketClose = -1; int pocketTab; Material pocketMat; float uiAlpha = 1;      // time the backpack has been open; how deep the hero stands in the lake
        // the backpack shows a projection of the hero: a still copy of him in a "studio" far outside the world, filmed by
        // its own camera through its own ASCII renderer
        static readonly Vector3 STUDIO = new Vector3(600, 0, 600);
        GameObject studio; Camera projCam; AsciiRenderer projAscii; Character projHero; MotionActor projActor; string projSig; float projDist = 4;

        // UI
        float k = 1; Rect safe; int tab; string toast; float toastT;
        readonly List<Rect> uiRects = new List<Rect>(); List<Rect> uiRectsPrev = new List<Rect>();
        readonly List<Vector2> taps = new List<Vector2>();
        Rect sliderRect, stickRect; Vector2 stickCenter, stick; float stickR, stickRun; int stickId = int.MinValue;
        Texture2D texWhite, texDisc, texRing; GUIStyle sBig, sRight, sRightSmall, sTitle, sSmall, sBtn, sBtnOn, sMono, sName, sDesc, sBadge, sToast;
        Font mono; float fpsAcc; int fpsN; string fpsText = "";

        static readonly string[] SEX = { "m", "f" }; static readonly string[] SEXNAME = { "Мужчина", "Женщина" };
        static readonly Dictionary<string, string> ICON = new Dictionary<string, string> {
            { "cloth", " _ _ \n/| |\\\n |_| \n | | " }, { "leather", " ___ \n/|#|\\\n |=| \n | | " },
            { "chain", " ooo \n/o8o\\\n o8o \n | | " }, { "plate", " [=] \n/[#]\\\n [#] \n || " },
            { "robe", "  ^  \n /o\\ \n/   \\\n/___\\" },
            { "none", "     \n  o  \n /|\\ \n  ^  " }, { "sword", "  |  \n  |  \n -+- \n  o  " },
            { "shield", " /-\\ \n |+|-\n \\_/|\n    o" }, { "axe", " <|  \n  |  \n  |  \n  |  " },
            { "spear", "  ^  \n  |  \n  |  \n  |  " }, { "staff", "  @  \n  Y  \n  |  \n  |  " } };

        // ---------- setup ----------
        // ---------- loading (one step per frame, so the loading screen and the log stay visible) ----------
        bool ready, failed; string stage = "Запуск"; int stageIndex, stageCount = 8; float loadT0;
        bool consoleOpen; int seenErrors; Vector2 consoleScroll;

        bool Step(string name, System.Action a)
        {
            stage = name; stageIndex++;
            float t0 = Time.realtimeSinceStartup;
            try { a(); }
            catch (System.Exception e)
            {
                failed = true; stage = name;
                Debug.LogError("Загрузка остановилась на шаге «" + name + "»: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                return false;
            }
            Debug.Log("✓ " + name + " (" + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " мс)");
            return true;
        }

        System.Collections.IEnumerator Start()
        {
            loadT0 = Time.realtimeSinceStartup;
            LoadSettings(); Application.targetFrameRate = FPS[Mathf.Clamp(setFps, 0, FPS.Length - 1)];
            QualitySettings.skinWeights = SkinWeights.FourBones;   // mobile quality levels default to 2 bones, which breaks this 163-bone rig
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.autorotateToPortrait = false; Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true; Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
            display = new GameObject("DisplayCamera").AddComponent<Camera>();
            display.cullingMask = 0; display.clearFlags = CameraClearFlags.SolidColor; display.backgroundColor = Color.black; display.depth = 0;
            { var dd = display.GetUniversalAdditionalCameraData(); dd.renderShadows = false; dd.renderPostProcessing = false; }
            if (!Step("Интерфейс", MakeUiAssets)) yield break;
            yield return null;
            if (!Step("Камеры и ASCII-рендер", () =>
            {
                var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                Debug.Log("Render pipeline: " + (urp != null ? urp.name : "нет (Built-in)"));
                if (urp != null) { urp.maxAdditionalLightsCount = 8; urp.shadowCascadeCount = 1; }
                SetShadowDistance(14);
                sceneCam = new GameObject("SceneCamera").AddComponent<Camera>();
                sceneCam.nearClipPlane = 0.05f; sceneCam.farClipPlane = 40; sceneCam.fieldOfView = 36; sceneCam.allowMSAA = false; sceneCam.allowHDR = false;
                ascii = new AsciiRenderer(sceneCam); ascii.reflections = true;
                ascii.surfaceGlyphs = PlayerPrefs.GetInt("ascii_glyph_mode", 1) == 1;
                ApplySettings(false);
            })) yield break;
            yield return null;
            if (!Step("Свет и окружение", () => { SetupEnvironment(); ApplyEditorAmbient(); })) yield break;
            yield return null;
            if (!Step("Материалы", () => { mats = new Mats(false); actorMats = new Mats(true); })) yield break;
            yield return null;
            if (!Step("Сцена редактора", () =>
            {
                SetupEditorScene();
                LoadCfg();
                animMode = 0; useAnimator = false;   // only the phase-matched mode is offered (motion matching still serves lock-on strafing)
                if (useAnimator) { locoCtrl = Resources.Load<RuntimeAnimatorController>("Locomotion"); if (locoCtrl == null) { useAnimator = false; animMode = 0; } }
            })) yield break;
            yield return null;
            if (!Step("Тело MakeHuman", HumanDB.Load)) yield break;
            yield return null;
            if (!Step("База движений (mocap)", MotionDB.Load)) yield break;
            yield return null;
            if (!Step("Герой и символы", () => { Rebuild(); Debug.Log("Символов на сцене: " + GlyphLayer.count); })) yield break;
            ready = true;
            Debug.Log("Загрузка завершена за " + (Time.realtimeSinceStartup - loadT0).ToString("0.0") + " с");
        }

        void SetupEnvironment()
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            // reflection probe substitute: small procedural cubemap (dark blue room with a warm and a cold soft box)
            int S = 32; var cube = new Cubemap(S, TextureFormat.RGBA32, false);
            Vector3 kl = new Vector3(-4, 6, 5).normalized, bl = new Vector3(6, 3, -4).normalized;
            Color baseC = Mats.Hex(0x223040), warm = Mats.Hex(0xfff2dd), cold = Mats.Hex(0x88aaff);
            for (int f = 0; f < 6; f++)
            {
                var px = new Color[S * S];
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2 - 1, v = (y + 0.5f) / S * 2 - 1; Vector3 d;
                    switch (f)
                    {
                        case 0: d = new Vector3(1, -v, -u); break;
                        case 1: d = new Vector3(-1, -v, u); break;
                        case 2: d = new Vector3(u, 1, v); break;
                        case 3: d = new Vector3(u, -1, -v); break;
                        case 4: d = new Vector3(u, -v, 1); break;
                        default: d = new Vector3(-u, -v, -1); break;
                    }
                    d.Normalize();
                    float a = Mathf.Pow(Mathf.Max(0, Vector3.Dot(d, kl)), 18), b = Mathf.Pow(Mathf.Max(0, Vector3.Dot(d, bl)), 10);
                    px[y * S + x] = baseC * (0.6f + 0.4f * Mathf.Max(0, d.y)) + warm * a * 1.6f + cold * b * 0.9f;
                }
                cube.SetPixels(px, (CubemapFace)f);
            }
            cube.Apply();
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
#if UNITY_2022_1_OR_NEWER
            RenderSettings.customReflectionTexture = cube;
#else
            RenderSettings.customReflection = cube;
#endif
            RenderSettings.reflectionIntensity = 0.9f;
        }

        Light DirLight(string name, Transform parent, Color c, float intensity, bool shadows)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var l = go.AddComponent<Light>(); l.type = LightType.Directional; l.color = c; l.intensity = intensity;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None; l.shadowBias = 0.03f; l.shadowNormalBias = 0.2f;
            return l;
        }

        void SetupEditorScene()
        {
            editorRoot = new GameObject("Editor");
            key = DirLight("Key", editorRoot.transform, Mats.Hex(0xfff0dc), 0.95f, true);
            rim = DirLight("Rim", editorRoot.transform, Mats.Hex(0x9ab8ff), 0.7f, false);
            fill = DirLight("Fill", editorRoot.transform, Mats.Hex(0xffd0a0), 0.16f, false);
            // stone floor disc
            var c = new Canvas2D(512, 512, Color.black); float cx = 256, R = 256, rp = R * 0.36f;
            Color g0 = Canvas2D.RGB(0x5a, 0x54, 0x4a), g1 = Canvas2D.RGB(0x3a, 0x35, 0x2e), line = Canvas2D.RGB(0x2a, 0x26, 0x20);
            c.Disc(cx, cx, rp, t => Color.Lerp(g0, g1, t));
            foreach (float rr in new[] { 0.33f, 0.2f }) c.Ring(cx, cx, R * rr, 3, line);
            for (int a = 0; a < 12; a++) { float t = a / 12f * Mathf.PI * 2; c.Line(cx + Mathf.Cos(t) * R * 0.2f, cx + Mathf.Sin(t) * R * 0.2f, cx + Mathf.Cos(t) * R * 0.33f, cx + Mathf.Sin(t) * R * 0.33f, 3, line); }
            for (int a = 0; a < 6; a++) { float t = a / 6f * Mathf.PI * 2 + 0.3f; c.Line(cx, cx, cx + Mathf.Cos(t) * R * 0.2f, cx + Mathf.Sin(t) * R * 0.2f, 3, line); }
            c.Ring(cx, cx, rp - 3, 5, Canvas2D.RGB(0x8a, 0x7c, 0x60));
            var fm = mats.WithTexture("STONE", c.Texture(false), Vector2.one); fm.SetFloat("_AsciiFlat", 1);
            floor = MeshGen.Obj(DiscMesh(2.6f, 64), fm, editorRoot.transform, Vector3.zero, Quaternion.identity, false, "Floor");
            GlyphLayer.Build(floor);
        }
        static Mesh DiscMesh(float r, int seg)
        {
            var v = new List<Vector3> { Vector3.zero }; var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) }; var t = new List<int>();
            for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2 / seg; v.Add(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r)); uv.Add(new Vector2(0.5f - Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f)); }
            for (int i = 1; i <= seg; i++) { t.Add(0); t.Add(i + 1); t.Add(i); }
            var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            if (m.normals[0].y < 0) { for (int i = 0; i < t.Count; i += 3) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; } m.SetTriangles(t, 0); m.RecalculateNormals(); }
            MeshGen.AddGlyphUV(m, false);
            return m;
        }

        void Damage(int pct)
        {
            if (hp <= 0 || !ready) return;
            if (player != null) player.PokeIdle();
            hp = Mathf.Max(0, hp - pct);
            string lost = "";
            while (LimbCount() > Mathf.CeilToInt(hp / 20f))
            {
                var have = new List<int>(); for (int l = 0; l < 5; l++) if (limbs[l]) have.Add(l);
                int pick = have[Random.Range(0, have.Count)]; SpawnDebris(pick); limbs[pick] = false;
                lost += (lost.Length > 0 ? ", " : "") + Character.LIMB_NAME[pick];
            }
            player.SetLimbs(limbs);
            if (hp <= 0) { ShowToast("Доспех разрушен — тень рассеялась"); respawnT = 1.8f; }
            else ShowToast("Урон −" + pct + "% · целостность " + hp + "%" + (lost.Length > 0 ? "\nОторвано: " + lost : ""));
        }
        // a torn-off limb flies off, tumbles, lies for a moment and burns away
        readonly List<Debris> debris = new List<Debris>();
        // a torn-off limb bursts out of the body in a cloud of smoke and comes apart at the joints: every piece flies
        // its own way, tumbles, bounces, lies for a moment and burns away
        class Debris { public GameObject go; public Vector3 v, w; public float r, t, trail = 0.12f, life = 5f; public bool rest, spark; public List<Renderer> glyphs = new List<Renderer>(); public MaterialPropertyBlock mpb = new MaterialPropertyBlock(); }
        class Puff { public GameObject go; public Vector3 v; public float t, life; public List<Renderer> glyphs = new List<Renderer>(); public MaterialPropertyBlock mpb = new MaterialPropertyBlock(); }
        readonly List<Puff> puffs = new List<Puff>();
        struct Seg { public string a, b; public float r; public int kind; public Seg(string a, string b, float r, int kind) { this.a = a; this.b = b; this.r = r; this.kind = kind; } }
        static Seg[] ArmSegs(string s) { return new[] { new Seg("upperarm01." + s, "lowerarm01." + s, 0.05f, 0), new Seg("lowerarm01." + s, "wrist." + s, 0.042f, 0), new Seg("wrist." + s, null, 0.03f, 2) }; }
        static Seg[] LegSegs(string s) { return new[] { new Seg("upperleg01." + s, "lowerleg01." + s, 0.078f, 0), new Seg("lowerleg01." + s, "foot." + s, 0.058f, 0), new Seg("foot." + s, null, 0.04f, 3) }; }
        static readonly Seg[][] DEBRIS_SEGS = { new[] { new Seg("head", null, 0.13f, 1) }, ArmSegs("L"), ArmSegs("R"), LegSegs("L"), LegSegs("R") };
        static void GlyphsOf(GameObject go, List<Renderer> into) { foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (r.gameObject.layer == GlyphLayer.LAYER) into.Add(r); }
        // ---- splashes: running through shallow water throws up drops (a small pool of them, made once) ----
        class Drop { public GameObject go; public Vector3 v; }
        readonly List<Drop> drops = new List<Drop>(); GameObject dropRoot; float splashT; int dropIx;
        void UpdateSplash(float dt, float sp)
        {
            if (dropRoot == null)
            {
                dropRoot = new GameObject("Splashes");
                var m = new Material(mats["WATER"]) { name = "Spray" }; m.color = new Color(0.62f, 0.72f, 0.82f); m.SetColor("_EmissionColor", new Color(0.16f, 0.2f, 0.26f));
                for (int i = 0; i < 48; i++)
                {
                    var go = MeshGen.Obj(MeshGen.Sphere(0.03f, 6, 4), m, dropRoot.transform, Vector3.zero, Quaternion.identity, false, "Drop");
                    GlyphLayer.Build(go); go.SetActive(false); drops.Add(new Drop { go = go });
                }
            }
            var hv = player.Velocity;
            if (loc.InWaterW(pos) && sp > 1.8f && heroSink < 0.5f)
            {
                splashT -= dt;
                if (splashT <= 0)
                {
                    splashT = 0.09f;
                    Vector3 fw = new Vector3(hv.x, 0, hv.z).normalized, side = new Vector3(fw.z, 0, -fw.x);
                    for (int k = 0; k < 4; k++)
                    {
                        var d = drops[dropIx]; dropIx = (dropIx + 1) % drops.Count;
                        float lr = Random.Range(-1f, 1f);
                        d.go.transform.position = pos + fw * Random.Range(-0.05f, 0.25f) + side * lr * 0.18f + Vector3.up * 0.04f;
                        d.v = new Vector3(hv.x, 0, hv.z) * Random.Range(0.25f, 0.55f) + side * lr * Random.Range(0.4f, 1.1f) + Vector3.up * Random.Range(1.4f, 2.7f);
                        d.go.SetActive(true);
                    }
                }
            }
            foreach (var d in drops)
            {
                if (!d.go.activeSelf) continue;
                d.v.y -= 9.8f * dt; var q = d.go.transform.position + d.v * dt; d.go.transform.position = q;
                if (q.y < -0.02f) d.go.SetActive(false);
            }
        }
        void SpawnPuff(Vector3 p, float size, Vector3 v, float life = 0)
        {
            if (puffs.Count > 90) return;
            var go = MeshGen.Obj(MeshGen.Sphere(size, 10, 7), actorMats["SHADOW"], null, p, Quaternion.identity, false, "Puff");
            GlyphLayer.Build(go);
            var f = new Puff { go = go, v = v, life = life > 0 ? life : 0.9f + size * 4 }; GlyphsOf(go, f.glyphs);
            f.mpb.SetFloat("_GlyphPuff", 0.001f); foreach (var r in f.glyphs) if (r != null) r.SetPropertyBlock(f.mpb);
            puffs.Add(f);
        }
        void SpawnDebris(int l)
        {
            if (hero == null || !world) return;
            var segs = DEBRIS_SEGS[l];
            var b0 = hero.human.Bone(segs[0].a); if (b0 == null) return;
            Vector3 p0 = b0.position, hv = player != null ? player.Velocity : Vector3.zero;
            // outwards from the body
            Vector3 o = new Vector3(p0.x - pos.x, 0, p0.z - pos.z); if (o.sqrMagnitude < 4e-3f) o = new Vector3(Random.value - 0.5f, 0, Random.value - 0.5f + 0.01f); o.Normalize();
            SpawnPuff(p0, l == 0 ? 0.11f : l <= 2 ? 0.085f : 0.11f, hv * 0.5f + new Vector3(o.x * 0.5f, 0.5f, o.z * 0.5f));
            foreach (var sg in segs)
            {
                var ba = hero.human.Bone(sg.a); if (ba == null) continue;
                var bb = sg.b != null ? hero.human.Bone(sg.b) : null;
                Vector3 c = ba.position; Quaternion q = Quaternion.identity; Mesh m;
                if (bb != null)
                {
                    Vector3 dv = bb.position - ba.position; float len = Mathf.Max(0.08f, dv.magnitude);
                    m = MeshGen.Cylinder(sg.r * 0.82f, sg.r, len, 8); c += dv * 0.5f;
                    if (dv.sqrMagnitude > 1e-8f) q = Quaternion.FromToRotation(Vector3.down, dv.normalized);
                }
                else if (sg.kind == 1) m = MeshGen.Sphere(0.13f, 10, 8);
                else if (sg.kind == 2) { m = MeshGen.Box(0.085f, 0.16f, 0.04f); c.y -= 0.07f; }
                else { m = MeshGen.Box(0.095f, 0.075f, 0.25f); c.y -= 0.04f; }
                var go = MeshGen.Obj(m, actorMats["PLATE"], null, c, q, true, "Debris");
                GlyphLayer.Build(go);
                var d = new Debris { go = go, r = sg.r, life = 5f + Random.value * 0.8f }; GlyphsOf(go, d.glyphs);
                // each piece gets its own direction (a wide fan around "outwards"), speed and spin
                float ang = (Random.value - 0.5f) * 2.2f, cs = Mathf.Cos(ang), sn = Mathf.Sin(ang), sp = 1.2f + Random.value * 2.2f;
                d.v = new Vector3((o.x * cs - o.z * sn) * sp, 2.2f + Random.value * 2.6f + (sg.kind == 1 ? 1.2f : 0f), (o.x * sn + o.z * cs) * sp) + hv * 0.6f;
                d.w = Random.onUnitSphere * (5f + Random.value * 11f);        // rad/s around a random axis
                debris.Add(d);
            }
        }
        void UpdateDebris(float dt)
        {
            for (int i = debris.Count - 1; i >= 0; i--)
            {
                var d = debris[i]; d.t += dt; var tr = d.go.transform;
                if (!d.rest)
                {
                    d.v.y -= 9.8f * dt; d.v *= 1 - 0.25f * dt; tr.position += d.v * dt;
                    float wl = d.w.magnitude; if (wl > 1e-3f) tr.rotation = Quaternion.AngleAxis(wl * dt * Mathf.Rad2Deg, d.w / wl) * tr.rotation;
                    if (tr.position.y < d.r)
                    {
                        var q = tr.position; q.y = d.r; tr.position = q;
                        if (d.v.y < 0) d.v.y *= -0.38f; d.v.x *= 0.62f; d.v.z *= 0.62f;
                        // on the ground the spin follows the roll and dies out
                        float rr = Mathf.Max(d.r, 0.04f); d.w = new Vector3(d.v.z / rr, d.w.y * 0.4f, -d.v.x / rr) * 0.5f;
                        if (Mathf.Abs(d.v.y) < 0.5f && new Vector2(d.v.x, d.v.z).magnitude < 0.25f) { d.rest = true; d.v = Vector3.zero; }
                    }
                    // smoke keeps pouring out of the piece while it flies
                    d.trail -= dt; if (d.trail <= 0 && d.t < 0.75f) { d.trail = 0.22f; SpawnPuff(tr.position, 0.05f, d.v * 0.25f); }
                }
                d.mpb.SetFloat("_GlyphBurn", d.spark ? 0.3f + 0.7f * d.t / d.life : Mathf.Clamp01((d.t - 1.6f) / (d.life - 2f)));     // lies for a moment, then burns for about three seconds
                foreach (var r in d.glyphs) if (r != null) r.SetPropertyBlock(d.mpb);
                if (d.t > d.life) { Destroy(d.go); debris.RemoveAt(i); }
            }
            for (int i = puffs.Count - 1; i >= 0; i--)
            {
                var f = puffs[i]; f.t += dt; f.v *= Mathf.Max(0f, 1 - 2.5f * dt); f.go.transform.position += f.v * dt;
                float u = f.t / f.life;
                f.mpb.SetFloat("_GlyphPuff", Mathf.Max(0.001f, 1 - (1 - u) * (1 - u)));          // fast at first, then hanging in the air
                foreach (var r in f.glyphs) if (r != null) r.SetPropertyBlock(f.mpb);
                if (u >= 1) { Destroy(f.go); puffs.RemoveAt(i); }
            }
        }
        // sparks: a handful of glowing bits thrown off where a blow lands on the guard
        void SpawnSparks(Vector3 p, Vector3 away)
        {
            for (int i = 0; i < 12; i++)
            {
                var go = MeshGen.Obj(MeshGen.Box(0.05f, 0.05f, 0.05f), actorMats["GOLD"], null, p, Random.rotation, false, "Spark");
                GlyphLayer.Build(go);
                var d = new Debris { go = go, r = 0.02f, life = 0.35f + Random.value * 0.25f, spark = true, trail = 9 }; GlyphsOf(go, d.glyphs);
                d.v = new Vector3(Random.value - 0.5f, Random.value * 0.9f + 0.1f, Random.value - 0.5f).normalized * (2f + Random.value * 3.5f) + away * 1.5f;
                d.w = Random.onUnitSphere * 12f;
                d.mpb.SetFloat("_GlyphBurn", 0.3f); foreach (var r in d.glyphs) if (r != null) r.SetPropertyBlock(d.mpb);
                debris.Add(d);
            }
        }

        // ---------- training dummies ----------
        // A post with a straw body, a crossbar for arms, a head under an old helmet. Pieces break off as it loses hit
        // points (helmet and a shoulder at 75, shield and the other shoulder at 50, the head at 25); at 0 it falls apart
        // and is rebuilt three seconds later. The second dummy turns to the hero and swings a wooden sword for 5 points.
        const int DUMMY_HIT = 20, DUMMY_DMG = 5;
        class DummyPart { public GameObject go; public int stage; }
        class Dummy { public Vector3 p; public bool attacker; public GameObject root; public Transform top; public List<DummyPart> parts = new List<DummyPart>();
            public int hp = 100; public float wob, dead, swing = -1, cd = 1, yaw; public bool struck; }
        readonly List<Dummy> dummies = new List<Dummy>();
        void BuildDummy(Dummy d)
        {
            d.root = new GameObject("Dummy"); d.root.transform.SetParent(field.root.transform, false); d.root.transform.localPosition = d.p;
            d.top = new GameObject("Top").transform; d.top.SetParent(d.root.transform, false);
            d.parts.Clear(); d.hp = 100; d.wob = 0; d.dead = 0; d.swing = -1; d.cd = 1; d.struck = false;
            void P(Transform parent, Mesh m, string mat, float x, float y, float z, int stage, float rx = 0, float rz = 0)
            { d.parts.Add(new DummyPart { go = MeshGen.Obj(m, actorMats[mat], parent, new Vector3(x, y, z), Quaternion.Euler(rx, 0, rz), true, "DummyPart"), stage = stage }); }
            Transform g = d.root.transform, top = d.top;
            P(g, MeshGen.Cylinder(0.08f, 0.06f, 1.0f, 8), "WOOD", 0, 0.5f, 0, 0);
            P(g, MeshGen.Cylinder(0.26f, 0.22f, 0.08f, 10), "WOOD", 0, 0.04f, 0, 0);
            P(top, MeshGen.Cylinder(0.17f, 0.21f, 0.62f, 10), "LINEN", 0, 1.16f, 0, 0);
            P(top, MeshGen.Cylinder(0.2f, 0.2f, 0.07f, 10), "LEATHER2", 0, 1.0f, 0, 0);
            P(top, MeshGen.Cylinder(0.035f, 0.035f, 0.96f, 8), "WOOD", 0, 1.36f, 0, 0, 0, 90);
            P(top, MeshGen.Sphere(0.15f, 10, 8), "LINEN", 0, 1.63f, 0, 25);
            P(top, MeshGen.Sphere(0.17f, 10, 6, Mathf.PI / 2), "PLATE", 0, 1.66f, 0, 75);
            P(top, MeshGen.Sphere(0.11f, 8, 6), "PLATE", 0.25f, 1.43f, 0, 75);
            P(top, MeshGen.Sphere(0.11f, 8, 6), "PLATE", -0.25f, 1.43f, 0, 50);
            P(top, MeshGen.Cylinder(0.24f, 0.24f, 0.035f, 12), "WOOD", 0.5f, 1.3f, 0.08f, 50, 90);
            if (d.attacker) P(top, MeshGen.Box(0.05f, 0.035f, 0.95f), "WOOD", -0.5f, 1.36f, 0.45f, 0);
            else P(top, MeshGen.Sphere(0.1f, 8, 6), "LINEN", -0.5f, 1.36f, 0, 50);
            top.localRotation = Quaternion.Euler(0, d.yaw * Mathf.Rad2Deg, 0);
            GlyphLayer.Build(d.root);
        }
        void DummyBreak(Dummy d, DummyPart part, float power)
        {
            var go = part.go; part.go = null; go.transform.SetParent(null, true);
            var b = new Debris { go = go, r = 0.07f, life = 4f + Random.value, trail = 9 }; GlyphsOf(go, b.glyphs);
            Vector3 q = go.transform.position, o = new Vector3(q.x - pos.x, 0, q.z - pos.z); if (o.sqrMagnitude < 1e-4f) o = Vector3.forward; o.Normalize();
            float ang = (Random.value - 0.5f) * 1.6f, cs = Mathf.Cos(ang), sn = Mathf.Sin(ang), sp = power * (0.8f + Random.value);
            b.v = new Vector3((o.x * cs - o.z * sn) * sp, 1.5f + Random.value * 2.5f, (o.x * sn + o.z * cs) * sp);
            b.w = Random.onUnitSphere * (4f + Random.value * 9f);
            debris.Add(b);
        }
        void DummyDamage(Dummy d, int dmg)
        {
            if (d.dead > 0) return;
            d.hp = Mathf.Max(0, d.hp - dmg); d.wob = 1;
            SpawnPuff(d.p + Vector3.up * 1.2f, 0.05f, new Vector3(0, 0.4f, 0), 0.4f);
            foreach (var part in d.parts) if (part.go != null && (d.hp <= 0 || (part.stage > 0 && d.hp <= part.stage))) DummyBreak(d, part, d.hp <= 0 ? 2.2f : 1.6f);
            if (d.hp <= 0) { d.dead = 3; Destroy(d.root); d.root = null; }
        }
        void UpdateDummies(float dt, float t)
        {
            if (loc != field || field == null) return;
            if (dummies.Count == 0) for (int i = 0; i < Field.DUMMY_SPOTS.Length; i++) dummies.Add(new Dummy { p = Field.DUMMY_SPOTS[i], attacker = i == 1 });
            for (int i = 0; i < dummies.Count; i++)
            {
                var d = dummies[i];
                if (d.dead > 0) { d.dead -= dt; if (d.dead <= 0) BuildDummy(d); continue; }
                if (d.root == null) BuildDummy(d);
                float dx = pos.x - d.p.x, dz = pos.z - d.p.z, dist = Mathf.Sqrt(dx * dx + dz * dz), inv = 1f / Mathf.Max(dist, 1e-3f);
                float fx = Mathf.Sin(player.heading), fz = Mathf.Cos(player.heading), facing = (-dx * fx - dz * fz) * inv;
                // the hero's cut lands once, while the edge is travelling, on a dummy in reach and in front of him
                if (player.Cutting && !player.atkHits.Contains(i) && dist < player.Reach && (player.Around || facing > 0.3f))
                { player.atkHits.Add(i); DummyDamage(d, DUMMY_HIT); if (d.dead > 0) continue; }
                // the worse its state, the more it leans; a hit makes it shudder and rock for a moment
                d.wob *= Mathf.Exp(-dt * 6);
                float lean = (1 - d.hp / 100f) * 0.22f, swing = 0;
                d.root.transform.localPosition = d.p + new Vector3(d.wob * 0.012f * Mathf.Sin(t * 61), 0, d.wob * 0.012f * Mathf.Sin(t * 53));
                if (d.attacker)
                {
                    // it turns to face the hero and, when he is in reach, winds up and swings
                    if (dist < 4.5f && d.swing < 0) { float e = Mathf.DeltaAngle(d.yaw * Mathf.Rad2Deg, Mathf.Atan2(dx, dz) * Mathf.Rad2Deg) * Mathf.Deg2Rad; d.yaw += Mathf.Clamp(e, -3 * dt, 3 * dt); }
                    d.cd -= dt; if (d.swing < 0 && d.cd <= 0 && dist < 2.0f && hp > 0) { d.swing = 0; d.struck = false; }
                    if (d.swing >= 0)
                    {
                        d.swing += dt; float u = d.swing;
                        swing = u < 0.5f ? -0.9f * (u / 0.5f) * (2 - u / 0.5f) : u < 0.64f ? -0.9f + 2.2f * (u - 0.5f) / 0.14f : 1.3f * Mathf.Max(0, 1 - (u - 0.64f) / 0.4f);
                        if (!d.struck && u >= 0.58f)
                        {
                            d.struck = true;
                            if (dist < 2.15f)
                            {
                                // blocked if the guard is up and the hero faces the dummy
                                player.combatT = MotionActor.COMBAT_HOLD;
                                if (player.Guard > 0.5f && facing > 0.2f) SpawnSparks(new Vector3(pos.x - dx * inv * 0.45f, 1.15f, pos.z - dz * inv * 0.45f), new Vector3(dx * inv, 0, dz * inv));
                                else Damage(DUMMY_DMG);
                            }
                        }
                        if (u > 1.04f) { d.swing = -1; d.cd = 1.3f; }
                    }
                }
                // the sword sticks out on its right side, so the arm sweeps from there across the front (Unity: mirrored)
                float yawDeg = d.attacker ? (d.yaw + 0.35f - swing) * Mathf.Rad2Deg : d.yaw * Mathf.Rad2Deg;
                d.top.localRotation = Quaternion.Euler(d.wob * 0.2f * Mathf.Sin(t * 27) * Mathf.Rad2Deg, yawDeg, (lean + d.wob * 0.13f * Mathf.Sin(t * 33 + 1)) * Mathf.Rad2Deg);
            }
        }
        // low integrity: a red glow creeps in from the edges of the screen; when it is critical the view pulses like a heartbeat
        float heart; Texture2D texVig;
        float DangerK() { return world ? Mathf.Clamp01((50 - hp) / 50f) : 0; }
        void UpdateDanger(float t)
        {
            float ph = (t % 0.9f) / 0.9f, a = (ph - 0.08f) / 0.05f, b = (ph - 0.32f) / 0.055f;
            heart = world && hp <= 20 && hp > 0 ? Mathf.Exp(-a * a) + 0.6f * Mathf.Exp(-b * b) : 0;
        }
        void DrawDanger(float W, float H)
        {
            float kd = DangerK(); if (kd <= 0) return;
            if (texVig == null)
            {
                const int S = 64; texVig = new Texture2D(S, S, TextureFormat.RGBA32, false); texVig.wrapMode = TextureWrapMode.Clamp; var px = new Color[S * S];
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float ex = Mathf.Min(x, S - 1 - x) / (S * 0.5f), ey = Mathf.Min(y, S - 1 - y) / (S * 0.5f);     // 0 at the edge .. 1 in the middle
                    float e = Mathf.Clamp01(1 - Mathf.Min(ex / 0.42f, ey / 0.55f)); px[y * S + x] = new Color(1, 1, 1, e * e);
                }
                texVig.SetPixels(px); texVig.Apply();
            }
            var old = GUI.color; GUI.color = new Color(0.85f, 0.06f, 0.06f, Mathf.Clamp01(kd * (0.35f + 0.45f * kd) + heart * 0.14f * kd));
            GUI.DrawTexture(new Rect(0, 0, W, H), texVig, ScaleMode.StretchToFill, true); GUI.color = old;
        }
        void RestoreBody() { hp = 100; for (int l = 0; l < 5; l++) limbs[l] = true; if (player != null) player.SetLimbs(limbs); }
        void Pickups()
        {
            if (loc == null || hp <= 0) return;
            foreach (var p in loc.piles)
            {
                if ((new Vector2(pos.x - p.pos.x - loc.origin.x, pos.z - p.pos.z - loc.origin.z)).magnitude > 0.95f) continue;
                string got = "";
                for (int l = 0; l < 5; l++)
                    if (!limbs[l] && p.Take(l)) { limbs[l] = true; hp = Mathf.Min(100, hp + 20); got += (got.Length > 0 ? ", " : "") + Character.LIMB_NAME[l]; }
                if (got.Length > 0) { player.SetLimbs(limbs); ShowToast("Подобрано: " + got + "\nЦелостность " + hp + "%"); }
            }
        }

        void SetupWorldLights()
        {
            worldLightsRoot = new GameObject("WorldLights");
            var hl = new GameObject("HeroLight"); hl.transform.SetParent(worldLightsRoot.transform, false);
            heroLight = hl.AddComponent<Light>(); heroLight.type = LightType.Point; heroLight.color = Mats.Hex(0xffe6c8); heroLight.intensity = 1.6f; heroLight.range = 4.2f; heroLight.renderMode = LightRenderMode.ForcePixel;
            var moon = DirLight("Moon", worldLightsRoot.transform, Mats.Hex(0x8aa6ff), 0.3f, true); moonLight = moon;
            // the moon stands 52 degrees up, a little beyond the field (seen from the camera): high enough to show in the
            // lake below the camera's view; its light comes from the same place
            Vector3 md = new Vector3(-0.12f, 0.788f, -0.604f).normalized;
            moon.transform.rotation = Quaternion.LookRotation(-md);
            Shader.SetGlobalVector("_MoonDir", md);
            Shader.SetGlobalVectorArray("_WaterRip", ripples);
            Shader.SetGlobalVectorArray("_WaterBody", waterBody); Shader.SetGlobalFloat("_WaterBodyN", 0);
            Shader.SetGlobalVectorArray("_WaterTrees", waterTrees); Shader.SetGlobalFloat("_WaterTreeN", 0);
            // the night sky the lake mirrors (see pipeline/sky/make_sky.py): azimuth across, elevation up
            try
            {
                var ta = Resources.Load<TextAsset>("skymap");
                var sky = new Texture2D(2, 2, TextureFormat.RGBA32, true, true);
                if (ta != null && sky.LoadImage(ta.bytes, false)) { sky.wrapModeU = TextureWrapMode.Repeat; sky.wrapModeV = TextureWrapMode.Clamp; sky.filterMode = FilterMode.Trilinear; sky.Apply(true, true); Shader.SetGlobalTexture("_AsciiSkyMap", sky); }
                else Debug.LogWarning("Карта неба не загрузилась");
            }
            catch (System.Exception e) { Debug.LogException(e); }
        }
        // what the lake mirrors besides the sky: the hero (spheres along his bones) and the trees
        readonly Vector4[] waterBody = new Vector4[16], waterTrees = new Vector4[8];
        static readonly string[] WB_BONES = { "head", "spine03", "spine05", "root", "lowerarm01.L", "lowerarm01.R", "wrist.L", "wrist.R",
                                              "lowerleg01.L", "lowerleg01.R", "foot.L", "foot.R", "upperarm01.L", "upperarm01.R", "upperleg01.L", "upperleg01.R" };
        static readonly float[] WB_R = { 0.12f, 0.19f, 0.17f, 0.17f, 0.07f, 0.07f, 0.06f, 0.06f, 0.09f, 0.09f, 0.08f, 0.08f, 0.08f, 0.08f, 0.11f, 0.11f };
        static readonly int[] WB_LIMB = { 0, -1, -1, -1, 1, 2, 1, 2, 3, 4, 3, 4, 1, 2, 3, 4 };
        void UpdateWaterMirror()
        {
            int nb = 0, nt = 0;
            if (loc != null && loc.HasWater && hero != null)
            {
                var hb = hero.human; float s = cfg.height / 180f;
                for (int i = 0; i < WB_BONES.Length; i++)
                {
                    var b = hb.Bone(WB_BONES[i]); int l = WB_LIMB[i];
                    if (b == null || (l >= 0 && !limbs[l])) { waterBody[i] = Vector4.zero; continue; }
                    Vector3 c = b.position;
                    // the upper arm and thigh spheres sit half way down the bone
                    if (i >= 12) { var ch = b.childCount > 0 ? b.GetChild(0) : null; if (ch != null) c = (c + ch.position) * 0.5f; }
                    waterBody[i] = new Vector4(c.x, c.y, c.z, WB_R[i] * s);
                }
                nb = WB_BONES.Length;
            }
            Shader.SetGlobalVectorArray("_WaterBody", waterBody); Shader.SetGlobalFloat("_WaterBodyN", nb);
            Shader.SetGlobalVectorArray("_WaterTrees", waterTrees); Shader.SetGlobalFloat("_WaterTreeN", nt);
        }
        // ---- the open world: field (at the origin), forest west of it, cave north of the forest; all built at once, the
        // ones the hero is near are switched on, crossing a seam changes the current location and blends its light ----
        static readonly Vector3 FOREST_O = new Vector3(-20, 0, 0), CAVE_O = new Vector3(-20, 0, -18);
        float worldBuildMs = -1;
        void SetupOutdoor()
        {
            long tB = System.Diagnostics.Stopwatch.GetTimestamp(); bool fresh = field == null || mockups.Count == 0;
            try { SetupOutdoorInner(); } finally { if (fresh) { worldBuildMs = (float)Ms(tB); System.GC.Collect(); Debug.Log("Мир построен за " + (worldBuildMs / 1000f).ToString("0.0") + " с"); } }      // (the building's scraps cleared at once, not over the next frames)
        }
        void SetupOutdoorInner()
        {
            if (worldLightsRoot == null) SetupWorldLights();
            if (field == null) { field = Field.Build(mats); GlyphLayer.Build(field.root); }
            if (forest == null) { forest = Forest.Build(mats, 20261116); GlyphLayer.Build(forest.root); forest.PlaceAt(FOREST_O); }
            if (cave == null)
            {
                cave = Cave.Build(mats); GlyphLayer.Build(cave.root); cave.PlaceAt(CAVE_O);
                cave.addRipple = (x, z, a) => AddRipple(x + CAVE_O.x, z + CAVE_O.z, a);
            }
            outdoor.Clear(); outdoor.Add(field); outdoor.Add(forest); outdoor.Add(cave);
            if (mockups.Count == 0)
            {
                // the other biomes, south of the meadow and the forest (see AHBiomes): kind, place, open sides N S W E, neighbours
                void Mk(string kind, float x, float z, bool n, bool s2, bool w, bool e, string nN, string nS, string nW, string nE)
                {
                    var o = new Vector3(x, 0, z);
                    var m = Mockup.Build(mats, kind, kind.GetHashCode() ^ 0x5eed, o, n, s2, w, e, new[] { nN, nS, nW, nE });
                    GlyphLayer.Build(m.root); m.PlaceAt(o); mockups.Add(m);
                }
                Mk("swamp", -20, 20, true, true, true, false, "forest", "autumn", "toxic", null);
                Mk("toxic", -40, 20, false, false, false, true, null, null, null, "swamp");
                Mk("bright", 0, 20, true, true, false, false, "field", "grim", null, null);
                Mk("autumn", -20, 40, true, false, true, false, "swamp", null, "snow", null);
                Mk("snow", -40, 40, false, false, false, true, null, null, null, "autumn");
                Mk("grim", 0, 40, true, false, false, true, "bright", null, null, "wastes");
                Mk("wastes", 20, 40, false, true, true, false, null, "crystal", "grim", null);
                Mk("crystal", 20, 60, true, false, false, false, "wastes", null, null, null);
                foreach (var m in mockups) m.SetActive(false);
                Debug.Log("Макеты биомов построены: " + mockups.Count + ", символов на сцене: " + GlyphLayer.count);
            }
            foreach (var m in mockups) outdoor.Add(m);
            if (!outdoorGlows) { outdoorGlows = true; foreach (var l in outdoor) CollectGlows(l.root); }
            Debug.Log("Мир построен (поляна, лес, пещера), символов на сцене: " + GlyphLayer.count);
        }
        void SetupField() { SetupOutdoor(); }
        void SetupCave() { SetupOutdoor(); }
        /// each outdoor location's own ground in world coordinates (without the strips shared with its neighbours)
        Rect Core(Location l)
        {
            if (l == field) return Rect.MinMaxRect(-10, -10, 11.5f, 10);
            if (l == forest) return Rect.MinMaxRect(FOREST_O.x - 10, FOREST_O.z - 10, FOREST_O.x + 10, FOREST_O.z + 10);
            if (l == cave) return Rect.MinMaxRect(CAVE_O.x - 10, CAVE_O.z - 8, CAVE_O.x + 10, CAVE_O.z + 8);
            if (l is Mockup) return Rect.MinMaxRect(l.origin.x - 10, l.origin.z - 10, l.origin.x + 10, l.origin.z + 10);
            return l.WorldRect;
        }
        static float RectDist(Rect r, Vector2 p) { float dx = Mathf.Max(r.xMin - p.x, 0, p.x - r.xMax), dz = Mathf.Max(r.yMin - p.y, 0, p.y - r.yMax); return Mathf.Sqrt(dx * dx + dz * dz); }
        /// collide with the current location, and with the trees and rocks of a neighbour near the seam
        Vector2 WCollide(Vector2 p, float r)
        {
            var q = loc.CollideW(p, r);
            if (outdoor.Contains(loc))
                foreach (var n in outdoor)
                    if (n != loc && n.Active && RectDist(Core(n), q) < 2.5f) q = n.CollideNoClampW(q, r);
            return q;
        }
        // ---- glowing things (lamps, torches, fires, glowing mushrooms, warm windows): their light and their flames are
        // only on while they are on the screen or within 2 m past its edge; further out nothing of them is drawn or lit ----
        struct Glow { public Light l; public Renderer r; public Transform t; }
        readonly List<Glow> glows = new List<Glow>(); readonly Plane[] frustum = new Plane[6]; int glowsOn; bool outdoorGlows;
        void CollectGlows(GameObject root)
        {
            foreach (var l in root.GetComponentsInChildren<Light>(true))
                if (l.type == LightType.Point && l != heroLight) glows.Add(new Glow { l = l, t = l.transform });
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var m = r.sharedMaterial; if (m == null || m.shader == null) continue;
                if (m.shader.name == "AsciiHero/Fire") glows.Add(new Glow { r = r, t = r.transform });
            }
        }
        void UpdateGlows()
        {
            if (glows.Count == 0 || sceneCam == null) return;
            GeometryUtility.CalculateFrustumPlanes(sceneCam, frustum); glowsOn = 0;
            foreach (var g in glows)
            {
                if (g.t == null) continue;
                Vector3 p = g.r != null ? g.r.bounds.center : g.t.position; bool on = true;
                for (int i = 0; i < 4; i++) if (frustum[i].GetDistanceToPoint(p) < -2f) { on = false; break; }     // (the four side planes, 2 m slack)
                if (g.l != null) { if (g.l.enabled != on) g.l.enabled = on; } else if (g.r.enabled != on) g.r.enabled = on;
                if (on) glowsOn++;
            }
        }
        /// the water deeper than the waist cannot be waded into: the step slides along that line or stops
        Vector2 NoDeeper(Vector2 q)
        {
            if (loc == null) return q;
            float d0 = loc.DepthW(pos);
            bool Bad(Vector2 v) { float d = loc.DepthW(new Vector3(v.x, 0, v.y)); return d > WaterDepth.WAIST && d > d0 + 1e-4f; }
            if (!Bad(q)) return q;
            var a = new Vector2(q.x, pos.z); if (!Bad(a)) return a;
            var b = new Vector2(pos.x, q.y); if (!Bad(b)) return b;
            return new Vector2(pos.x, pos.z);
        }
        /// which location the hero is in, which ones are on, how the light blends across a seam
        void UpdateOutdoor(bool force = false)
        {
            if (!outdoor.Contains(loc)) return;
            var p2 = new Vector2(pos.x, pos.z);
            if (!Core(loc).Contains(p2))
                foreach (var n in outdoor)
                    if (n != loc && Core(n).Contains(p2))
                    {
                        loc = n; force = true;
                        ShowToast(n == field ? "Поляна" : n == forest ? "Лес" : n is Mockup ? ((Mockup)n).title : "Пещера");
                        break;
                    }
            leafClear = Mathf.MoveTowards(leafClear, loc == forest ? 1f : 0f, Time.deltaTime * 1.5f);      // under the trees: the crowns clear off the screen but its rim
            Shader.SetGlobalFloat("_AsciiLeafClear", Mathf.SmoothStep(0, 1, leafClear));
            // the ground the camera sees (its four corner rays meeting y = 0), 3 m wider all round
            Rect viewRect = new Rect(p2.x - 14, p2.y - 14, 28, 28);
            if (sceneCam != null)
            {
                float x0 = 1e9f, x1 = -1e9f, z0 = 1e9f, z1 = -1e9f;
                foreach (var c in new[] { new Vector3(0, 0), new Vector3(1, 0), new Vector3(0, 1), new Vector3(1, 1) })
                {
                    var ray = sceneCam.ViewportPointToRay(c); float t = ray.direction.y < -0.01f ? -ray.origin.y / ray.direction.y : 40f;
                    var g = ray.origin + ray.direction * Mathf.Min(t, 40f);
                    x0 = Mathf.Min(x0, g.x); x1 = Mathf.Max(x1, g.x); z0 = Mathf.Min(z0, g.z); z1 = Mathf.Max(z1, g.z);
                }
                viewRect = Rect.MinMaxRect(x0 - 3, z0 - 3, x1 + 3, z1 + 3);
            }
            lastView = viewRect;
            // the mirror: only for a lake in the camera's view (puddles and swamp murk keep their own sheen without it)
            waterSeen = null;
            if (sceneCam != null) GeometryUtility.CalculateFrustumPlanes(sceneCam, mirPlanes);
            foreach (var n in outdoor)
            {
                if (!(n == loc || Core(n).Overlaps(viewRect)) || !n.HasWater) continue;
                var mw = n.MirrorWater(); bool seen = false;
                if (mw == null) seen = n.WaterIn(viewRect);
                else foreach (var b in mw) if (sceneCam == null || GeometryUtility.TestPlanesAABB(mirPlanes, b)) { seen = true; break; }
                if (seen) { waterSeen = n; if (n == loc) break; }
            }
            foreach (var n in outdoor)
            {
                bool on = n == loc || Core(n).Overlaps(viewRect);          // (only what can come into view: each location switched on costs its shadows and its characters)
                if (n.Active != on) n.SetActive(on);
            }
            // the light: each location weighs by how deep the hero is in it (a 4 m blend across each seam)
            float wsum = 0; Color sky = Color.black, eq = Color.black, gnd = Color.black; float moon = 0, hl = 0, ex = 0;
            foreach (var n in outdoor)
            {
                float d = RectDist(Core(n), p2), inside = Core(n).Contains(p2) ? Mathf.Min(Mathf.Min(p2.x - Core(n).xMin, Core(n).xMax - p2.x), Mathf.Min(p2.y - Core(n).yMin, Core(n).yMax - p2.y)) : -d;
                float w = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-2f, 2f, inside));
                if (w <= 0) continue;
                wsum += w; sky += n.ambSky * w; eq += n.ambEq * w; gnd += n.ambGround * w; moon += n.moonK * w; hl += n.heroLightK * w; ex += n.exposure * w;
            }
            if (wsum > 0)
            {
                sky /= wsum; eq /= wsum; gnd /= wsum; moon /= wsum; hl /= wsum; ex /= wsum;
                SetAmbient(sky, eq, gnd);
                if (moonLight != null) { moonLight.intensity = 0.3f * moon; moonLight.enabled = moon > 0.02f; }
                heroLight.intensity = hl; ascii.exposure = ex;
            }
        }
        /// all the armour lies in new places every time the hero comes into the world (the places themselves stay)
        void RespawnPiles()
        {
            var R = new System.Random(System.Environment.TickCount);
            foreach (var l in outdoor)
            {
                var L = l;
                NatureLib.RespawnPiles(L, mats, 4f, (x, z, r) =>
                {
                    foreach (var c in L.colliders)
                        if (c.circle ? (new Vector2(x - c.x, z - c.z)).magnitude < c.r + r + 0.3f : (x > c.minx - r && x < c.maxx + r && z > c.minz - r && z < c.maxz + r)) return false;
                    if (L.InWater(x, z) || L.SpeedK(x, z) < 0.9f) return false;
                    var w = new Vector2(x + L.origin.x, z + L.origin.z);
                    return Core(L).Contains(w) && RectDist(new Rect(Core(L).xMin + 1, Core(L).yMin + 1, Core(L).width - 2, Core(L).height - 2), w) <= 0;
                }, R);
            }
        }
        /// through the inn's door: the picture is "retyped" from the door outwards, the other place comes in behind it
        void Reprint(System.Action go)
        {
            Vector2 uv = new Vector2(0.5f, 0.5f);
            if (sceneCam != null) { var v = sceneCam.WorldToViewportPoint(pos + Vector3.up * 0.9f); uv = new Vector2(v.x, v.y); }
            if (ascii != null) ascii.StartReprint(uv);
            go();
        }
        void Doors(float dt)
        {
            if (doorCd > 0) { doorCd -= dt; return; }
            if (loc == field && pos.x > Field.DOOR_X - 0.1f && Mathf.Abs(pos.z) < 1.1f)
            { doorCd = 1.5f; Reprint(() => { EnterWorld(0, new Vector3(3.7f, 0, -2.6f)); player.heading = 0; ShowToast("Трактир «Пьяный дракон»"); }); }
            else if (loc == tavern && (new Vector2(pos.x - 3.7f, pos.z + 3.6f)).magnitude < 0.75f)
            { doorCd = 1.5f; Reprint(() => { EnterWorld(1, new Vector3(Field.DOOR_X - 1.4f, 0, 0)); player.heading = -Mathf.PI / 2; ShowToast("Поляна"); }); }
        }
        void SetupWorld()
        {
            tavern = Tavern.Build(mats);
            GlyphLayer.Build(tavern.root);
            if (worldLightsRoot == null) SetupWorldLights();
            bartenderChar = Character.Build(new CharConfig { sex = 'm', build = 3, height = 178, armor = "cloth", weapon = "none" }, actorMats, "Bartender");
            bartenderChar.go.transform.position = tavern.bartender;
            GlyphLayer.Build(bartenderChar.go);
            bartender = new MotionActor(bartenderChar);
            tavern.colliders.Add(Collider2.Circle(tavern.bartender.x, tavern.bartender.z, 0.3f));
            CollectGlows(tavern.root);
        }

        // ---------- config ----------
        void LoadCfg()
        {
            var s = PlayerPrefs.GetString("ascii_hero3d", "");
            var p = s.Split('|');
            if (p.Length == 5)
            {
                cfg.sex = p[0] == "f" ? 'f' : 'm'; int b; if (int.TryParse(p[1], out b)) cfg.build = Mathf.Clamp(b, 0, 3);
                float h; if (float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out h)) cfg.height = Mathf.Clamp(h, 150, 200);
                foreach (var a in Character.ARMORS) if (a[0] == p[3]) cfg.armor = p[3];
                foreach (var w in Character.WEAPONS) if (w[0] == p[4]) cfg.weapon = p[4];
            }
            // body customisation: "sex;v0;v1;..." (dropped if it was saved for the other sex or another parameter set)
            cfg.body = null;
            var bs = PlayerPrefs.GetString("ascii_hero_body", "").Split(';');
            if (bs.Length == Body.N + 1 && bs[0] == cfg.sex.ToString())
            {
                var b = new float[Body.N]; bool ok = true;
                for (int i = 0; i < Body.N; i++) ok &= float.TryParse(bs[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out b[i]);
                if (ok) cfg.body = b;
            }
            if (cfg.body == null) ResetBody();
            cfg.body[Body.Ix("height")] = cfg.height;
        }
        void ResetBody()
        {
            cfg.body = Body.Defaults(cfg.sex); cfg.body[Body.Ix("height")] = cfg.height;
            cfg.body[Body.Ix("weight")] = Mathf.Round(Body.DefaultWeight(cfg.sex, cfg.height));
        }
        void SaveCfg()
        {
            PlayerPrefs.SetString("ascii_hero3d", cfg.sex + "|" + cfg.build + "|" + cfg.height.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + cfg.armor + "|" + cfg.weapon);
            if (cfg.body != null)
            {
                var sb = new System.Text.StringBuilder(cfg.sex.ToString());
                foreach (var x in cfg.body) sb.Append(';').Append(x.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                PlayerPrefs.SetString("ascii_hero_body", sb.ToString());
            }
            PlayerPrefs.Save();
        }
        void SetSex(char s) { if (cfg.sex != s) { cfg.sex = s; cfg.height = s == 'f' ? 167 : 180; ResetBody(); presetIx = 2; rebuildPending = true; SaveCfg(); } }
        void SetBuild(int b) { if (cfg.build != b) { cfg.build = b; rebuildPending = true; SaveCfg(); } }
        void SetArmor(string a) { if (cfg.armor != a) { cfg.armor = a; rebuildPending = true; SaveCfg(); } }
        void SetWeapon(string w) { if (cfg.weapon != w) { cfg.weapon = w; rebuildPending = true; SaveCfg(); } }
        void SetHeight(float h)
        {
            h = setDev ? Mathf.Clamp(Mathf.Round(h), Body.DevMin(0), Body.DevMax(0)) : Mathf.Clamp(Mathf.Round(h), Body.ALL[0].min, Body.ALL[0].max);
            if (h == cfg.height) return;
            cfg.height = h; if (cfg.body != null) cfg.body[0] = h;
            if (hero != null) { hero.cfg.height = h; hero.ApplyHeight(); }
            bodyDirty = true;      // (the weight maps to the build relative to the height)
        }
        void Randomize()
        {
            cfg.sex = Random.value < 0.5f ? 'm' : 'f';
            cfg.height = Mathf.Round((cfg.sex == 'f' ? 155 : 168) + Random.value * 28);
            cfg.build = Random.Range(0, 4);
            ResetBody();
            for (int i = 1; i < Body.N; i++)
            {   // a plausible person: around the defaults, now and then further out
                var bp = Body.ALL[i]; float d = Body.Def(i, cfg.sex), g = (Random.value + Random.value + Random.value - 1.5f) / 1.5f;
                cfg.body[i] = Mathf.Clamp(d + g * (g > 0 ? bp.max - d : d - bp.min) * 0.6f, bp.min, bp.max);
            }
            cfg.body[Body.Ix("weight")] = Mathf.Round(Body.DefaultWeight(cfg.sex, cfg.height) * (0.8f + 0.5f * Random.value)); presetIx = -1;
            cfg.armor = Character.ARMORS[Random.Range(0, Character.ARMORS.Length)][0];
            cfg.weapon = Character.WEAPONS[Random.Range(0, Character.WEAPONS.Length)][0];
            rebuildPending = true; SaveCfg();
        }

        void Rebuild()
        {
            if (hero != null) Destroy(hero.go);
            var hc = cfg.Clone(); hc.shadow = true;       // the hero is a shadow wearing armour
            hero = Character.Build(hc, actorMats, "Hero");
            hero.glyphs = GlyphLayer.Build(hero.go, Character.LimbOfBone());
            // armour pieces (every shell, the weapon, the shield...) can be offset on their own: the dash sends the shadow ahead and the armour after it
            pieces.Clear();
            {
                var byKey = new Dictionary<Object, Piece>();
                foreach (var kv in GlyphLayer.pairs)
                {
                    var r = kv.Key; var sm = r.sharedMaterial;
                    if (sm == actorMats["SHADOW"] || sm == actorMats["EYE"]) continue;
                    Object key = r is SkinnedMeshRenderer ? (Object)r : r.transform.parent;
                    string kind = sm == actorMats["CHAIN"] ? "chain" : cfg.armor == "robe" && (sm == actorMats["ROBE"] || sm == actorMats["ROBED"]) ? "robe" : "";
                    Piece pc; if (!byKey.TryGetValue(key, out pc)) { pc = new Piece { kind = kind }; byKey[key] = pc; pieces.Add(pc); }
                    pc.rs.Add(r); if (kv.Value != null) pc.rs.Add(kv.Value);
                }
            }
            // the robe's skirt is simulated cloth
            cloak = null; cloakRs.Clear();
            if (hero.skirt != null)
            {
                cloak = new Cloak(hero);
                var sr = hero.skirt.GetComponent<Renderer>(); Renderer gr;
                if (sr != null) { cloakRs.Add(sr); if (GlyphLayer.pairs.TryGetValue(sr, out gr) && gr != null) cloakRs.Add(gr); }
            }
            player = new MotionActor(hero);
            player.useMM = animMode == 1;
            player.SetLimbs(limbs);
            if (useAnimator && locoCtrl != null)
            {
                player.driver = AnimatorDriver.Attach(hero.go, locoCtrl);
                player.maxSpeed = Mathf.Max(3.0f, player.driver.sprintSpeed);
            }
            if (world) { hero.go.transform.position = pos; hero.go.transform.rotation = Quaternion.Euler(0, player.heading * Mathf.Rad2Deg, 0); }
        }

        // ---------- modes ----------
        void EnterWorld(bool toField = false) { EnterWorld(toField ? 1 : 0); }
        /// 0 the tavern, 1 the test field, 2 the cave
        /// where: 0 tavern, 1 field, 2 cave, 3 forest; at: where the hero appears (world), else the location's spawn
        void EnterWorld(int where) { EnterWorld(where, null); }
        void EnterWorld(int where, Vector3? at)
        {
            SaveCfg();
            try
            {
                if (where == 0) { if (tavern == null) SetupWorld(); }
                else SetupOutdoor();
            }
            catch (System.Exception e) { Debug.LogException(e); return; }
            bool wasOut = world && outdoor.Contains(loc);
            world = true; loc = where == 1 ? (Location)field : where == 2 ? (Location)cave : where == 3 ? (Location)forest : tavern;
            if (tavern != null) { tavern.SetActive(where == 0); bartenderChar.go.SetActive(where == 0); }
            foreach (var l in outdoor) l.SetActive(false);
            if (where != 0 && !wasOut) RespawnPiles();
            worldLightsRoot.SetActive(true); editorRoot.SetActive(false);
            pos = at ?? loc.SpawnW; heroTrail = pos; player.heading = Mathf.PI; player.ResetSim(); player.noLock = false;
            hero.go.transform.position = pos; hero.go.transform.rotation = Quaternion.Euler(0, 180, 0);
            camTgt = new Vector3(pos.x, 0.6f, pos.z);
            sceneCam.fieldOfView = 30; ascii.exposure = 1.3f; ascii.actorBoost = 1; Shader.SetGlobalFloat("_AsciiLod", ISO_LOD);
            SetShadowDistance(26);      // (the view reaches about 21 m from the camera; a shorter range keeps the shadow map sharp at half its old size)
            if (where == 0)
            {
                if (moonLight != null) { moonLight.enabled = true; moonLight.intensity = 0.3f; }
                heroLight.intensity = 1.6f;
                SetAmbient(Mats.Hex(0x50465a) * 0.45f, Mats.Hex(0x342b32) * 0.45f, Mats.Hex(0x18100a) * 0.45f);
                if (at == null) ShowToast("Трактир «Пьяный дракон»");
            }
            else
            {
                UpdateOutdoor(true);
                if (at == null) ShowToast(where == 1 ? "Поляна\nНа востоке трактир, на западе лес" : where == 2 ? "Пещеры\nВыход на юг, в лес" : "Лес\nНа севере вход в пещеру, на востоке поляна");
            }
        }
        void AddRipple(float x, float z, float a) { ripples[rippleIx] = new Vector4(x, z, tNow, a); rippleIx = (rippleIx + 1) % ripples.Length; }
        void LeaveWorld()
        {
            world = false;
            if (tavern != null) { tavern.SetActive(false); bartenderChar.go.SetActive(false); }
            foreach (var l in outdoor) l.SetActive(false);
            worldLightsRoot.SetActive(false); editorRoot.SetActive(true);
            Shader.SetGlobalVector("_AsciiHeroA", new Vector4(0, -99, 0, 0)); Shader.SetGlobalVector("_AsciiHeroB", new Vector4(0, -99, 0, 0)); Shader.SetGlobalVector("_AsciiHeroVel", Vector4.zero);
            for (int i = 0; i < 4; i++) pushPts[i] = FAR; Shader.SetGlobalVectorArray("_AsciiPush", pushPts);
            hero.go.transform.position = Vector3.zero; hero.go.transform.rotation = Quaternion.identity;
            player.speed = 0; player.heading = 0; player.ResetSim(); lockOn = false;
            sceneCam.fieldOfView = 36; ascii.exposure = 1; ascii.actorBoost = 0; Shader.SetGlobalFloat("_AsciiLod", 1);
            ApplyEditorAmbient();
            SetShadowDistance(14);
        }
        void ApplyEditorAmbient() { SetAmbient(Mats.Hex(0xc8d4ff) * 0.3f, Mats.Hex(0x7c7c8c) * 0.3f, Mats.Hex(0x302418) * 0.3f); }
        static void SetAmbient(Color sky, Color equator, Color ground)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator; RenderSettings.ambientGroundColor = ground;
            // URP reads the ambient probe; build it from the three colours (linear values)
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(((sky + equator + ground) / 3f).linear);
            sh.AddDirectionalLight(Vector3.up, ((sky - ground) * 0.5f).linear, 1f);
            RenderSettings.ambientProbe = sh;
        }
        static float shadowBase = 14, shadowScale = 1;
        static void SetShadowDistance(float d)
        {
            shadowBase = d; d *= shadowScale;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) urp.shadowDistance = d; else QualitySettings.shadowDistance = d;
        }

        // ---------- main menu and settings ----------
        bool inMenu = true; int menuPage;   // 0 main, 1 settings, 2 developers
        int setQuality, setGrass, setRes, setFps, setMusic, setSound; bool setBloom, setShowFps, setOutline = true;
        static readonly string[] QUALITY = { "Низкое", "Среднее", "Высокое" };
        static readonly int[] GRASS = { 0, 25, 50, 75, 100 }, RES = { 50, 67, 75, 85, 100 }, FPS = { 30, 60, 90, 120 };
        static readonly string[] DEVELOPERS = { "krocotovski264", "Тёма — программист", "", "Анимация ходьбы и передвижения в бою: набор 100STYLE —", "I. Mason, S. Starke, T. Komura (CC BY 4.0)", "", "Скелеты в пещере: BodyParts3D, DBCLS —", "© The Database Center for Life Science (CC BY 4.0)" };
        void LoadSettings()
        {
            bool phone = Application.isMobilePlatform;
            setQuality = PlayerPrefs.GetInt("set_quality", phone ? 1 : 2); setGrass = PlayerPrefs.GetInt("set_grass", 4); setRes = PlayerPrefs.GetInt("set_res", 4);
            setFps = PlayerPrefs.GetInt("set_fps", 1); setBloom = PlayerPrefs.GetInt("set_bloom", 1) == 1; setShowFps = PlayerPrefs.GetInt("set_showfps", 0) == 1;
            setMusic = PlayerPrefs.GetInt("set_music", 70); setSound = PlayerPrefs.GetInt("set_sound", 80); setDev = PlayerPrefs.GetInt("set_dev", 0) == 1; setOutline = PlayerPrefs.GetInt("set_outline", 1) == 1;
        }
        void ApplySettings(bool save = true)
        {
            setQuality = Mathf.Clamp(setQuality, 0, 2); setGrass = Mathf.Clamp(setGrass, 0, GRASS.Length - 1); setRes = Mathf.Clamp(setRes, 0, RES.Length - 1);
            setFps = Mathf.Clamp(setFps, 0, FPS.Length - 1); setMusic = Mathf.Clamp(setMusic, 0, 100); setSound = Mathf.Clamp(setSound, 0, 100);
            if (save)
            {
                PlayerPrefs.SetInt("set_quality", setQuality); PlayerPrefs.SetInt("set_grass", setGrass); PlayerPrefs.SetInt("set_res", setRes); PlayerPrefs.SetInt("set_fps", setFps);
                PlayerPrefs.SetInt("set_bloom", setBloom ? 1 : 0); PlayerPrefs.SetInt("set_showfps", setShowFps ? 1 : 0);
                PlayerPrefs.SetInt("set_music", setMusic); PlayerPrefs.SetInt("set_sound", setSound); PlayerPrefs.SetInt("set_dev", setDev ? 1 : 0); PlayerPrefs.SetInt("set_outline", setOutline ? 1 : 0); PlayerPrefs.Save();
            }
            Application.targetFrameRate = FPS[setFps];
            Shader.SetGlobalFloat("_AsciiGrassThin", 1 - GRASS[setGrass] / 100f);
            if (ascii != null) { ascii.bloom = setBloom ? 0.32f : 0f; ascii.fullLit = false; ascii.litDiv = setQuality == 0 ? 3 : 2; ascii.outline = setOutline; }
            shadowScale = setQuality == 0 ? 0f : setQuality == 1 ? 0.85f : 1f; SetShadowDistance(shadowBase);
            AudioListener.volume = setSound / 100f;      // (the game has no sounds or music yet: the levels are kept for them)
            // rendering resolution: a share of the display's own resolution (100 % = the most the device can show)
            int sw = Display.main.systemWidth, sh = Display.main.systemHeight; if (sw <= 0 || sh <= 0) { sw = Screen.currentResolution.width; sh = Screen.currentResolution.height; }
            bool land = Screen.width >= Screen.height; int big = Mathf.Max(sw, sh), small = Mathf.Min(sw, sh);
            float f = RES[setRes] / 100f; int tw = Mathf.RoundToInt((land ? big : small) * f), th = Mathf.RoundToInt((land ? small : big) * f);
            if (Mathf.Abs(Screen.width - tw) > 2 || Mathf.Abs(Screen.height - th) > 2) Screen.SetResolution(tw, th, FullScreenMode.FullScreenWindow);
        }
        void MenuUI(float W, float H)
        {
            Fill(new Rect(0, 0, W, H), new Color(0.01f, 0.01f, 0.02f, menuPage == 0 ? 0.45f : 0.7f));
            float cx = W / 2, top = safe.y + 30 * k, bottom = safe.yMax - 10 * k;
            GUI.Label(new Rect(cx - 250 * k, top, 500 * k, 50 * k), "ASCII HERO", sBig);
            if (menuPage == 0)
            {
                float bw = 230 * k, bh = 40 * k, y = top + 80 * k;
                if (Button(new Rect(cx - bw / 2, y, bw, bh), "ИГРАТЬ")) inMenu = false;
                if (Button(new Rect(cx - bw / 2, y + bh + 12 * k, bw, bh), "СОХРАНЕНИЯ")) { menuPage = 3; saveMode = 0; saveInfoDirty = true; }
                if (Button(new Rect(cx - bw / 2, y + 2 * (bh + 12 * k), bw, bh), "НАСТРОЙКИ")) menuPage = 1;
                if (Button(new Rect(cx - bw / 2, y + 3 * (bh + 12 * k), bw, bh), "ВЫХОД")) Application.Quit();
                if (Button(new Rect(safe.x + 10 * k, bottom - 22 * k, 104 * k, 22 * k), "Разработчики")) menuPage = 2;
                GUI.Label(new Rect(safe.xMax - 210 * k, bottom - 22 * k, 200 * k, 22 * k), VERSION, sRight);
                return;
            }
            float pw = 460 * k, x0 = cx - pw / 2, y0 = top + 56 * k;
            if (menuPage == 3) { SavesUI(cx, top, bottom); return; }
            if (menuPage == 2)
            {
                GUI.Label(new Rect(x0, y0, pw, 22 * k), "РАЗРАБОТЧИКИ", sTitle);
                for (int i = 0; i < DEVELOPERS.Length; i++) GUI.Label(new Rect(x0, y0 + (28 + 24 * i) * k, pw, 22 * k), DEVELOPERS[i], sBadge);
                if (Button(new Rect(cx - 70 * k, bottom - 36 * k, 140 * k, 30 * k), "НАЗАД")) menuPage = 0;
                return;
            }
            y0 = top + 46 * k;
            GUI.Label(new Rect(x0, y0, pw, 22 * k), "НАСТРОЙКИ", sTitle);
            if (Button(new Rect(x0 + pw - 100 * k, y0 - 3 * k, 100 * k, 24 * k), "НАЗАД")) { menuPage = 0; menuFromWorld = false; }
            float rh = Mathf.Min(25 * k, (bottom - 34 * k - (y0 + 26 * k)) / 11f), y1 = y0 + 26 * k; int row = 0; bool changed = false;
            void Row(string name, string value, System.Action dec, System.Action inc)
            {
                float yy = y1 + row * rh; row++;
                GUI.Label(new Rect(x0, yy + 2 * k, 210 * k, rh), name, sBadge);
                if (Button(new Rect(x0 + 220 * k, yy, 34 * k, rh - 3 * k), "<")) { dec(); changed = true; }
                GUI.Label(new Rect(x0 + 258 * k, yy, 160 * k, rh - 3 * k), value, sBtn);
                if (Button(new Rect(x0 + 422 * k, yy, 34 * k, rh - 3 * k), ">")) { inc(); changed = true; }
            }
            Row("Качество изображения", QUALITY[setQuality], () => setQuality--, () => setQuality++);
            Row("Растительность", GRASS[setGrass] + " %", () => setGrass--, () => setGrass++);
            Row("Разрешение", RES[setRes] + " %" + (setRes == RES.Length - 1 ? " (макс.)" : ""), () => setRes--, () => setRes++);
            Row("Свечение", setBloom ? "вкл" : "выкл", () => setBloom = !setBloom, () => setBloom = !setBloom);
            Row("Обводка предметов", setOutline ? "вкл" : "выкл", () => setOutline = !setOutline, () => setOutline = !setOutline);
            Row("Ограничение кадров", FPS[setFps] + " к/с", () => setFps--, () => setFps++);
            Row("Показывать FPS и время кадра", setShowFps ? "да" : "нет", () => setShowFps = !setShowFps, () => setShowFps = !setShowFps);
            Row("Громкость музыки", setMusic + " %", () => setMusic -= 10, () => setMusic += 10);
            Row("Громкость звуков", setSound + " %", () => setSound -= 10, () => setSound += 10);
            Row("Режим разработчика", setDev ? "вкл" : "выкл", () => setDev = !setDev, () => setDev = !setDev);
            if (changed) ApplySettings();
            if (Button(new Rect(x0, y1 + row * rh + 4 * k, pw, 26 * k), "ТЕСТ НАГРУЗКИ ПО ВСЕМ БИОМАМ (около 2 мин, результат — в логе)")) StartPerfTest();
        }

        // ---------- the load test ----------
        // Runs on the device in two parts.
        //   1. Every biome in turn (the meadow, the forest, the cave, the tavern and the eight others): the hero is put
        //      at its spawn and walks a small circle (the camera, the animation, the grass and the water all move)
        //      with everything on; for each: mean and worst frame, the share of frames over 1/60 s, CPU and GPU times,
        //      the characters in the scene and how many locations are switched on round it.
        //   2. In the heaviest biome: everything on, then one part off at a time - what each part of the frame costs.
        // At the end a table: every biome with its result and whether it holds 60 and 30 frames a second.
        static readonly string[] PERF = { "всё включено", "без травы", "без символов (только 3D-сцена)", "без героя", "без анимации и ткани", "без теней", "без свечения",
            "без интерфейса", "без обводки", "с отдельной камерой глубины (как до 0.35)", "герой: все символы (как в 0.34)", "разрешение 67 %", "герой без слоя символов", "без деревьев, камней и брони (только земля и трава)" };
        bool perfRun, perfNoAnim, perfNoGui, perfFullActors, perfWalk; int perfIx; float perfT, perfBase, pWorst; int pN, pFt, savedVsync;
        double pSum, pMain, pRender, pGpu, accU, accA, accG; readonly FrameTiming[] ftBuf = new FrameTiming[1];
        double fMain, fGpu; int fFt;
        static double Ms(long t0) { return (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }
        // part 1: the biomes; part 2: the stages in the heaviest
        readonly List<Location> perfLocs = new List<Location>(); int perfPhase, perfLocIx; Location perfHeavy; float perfHeavyMs;
        class PerfRow { public string name; public float ms, worst, miss, gpu, main; public int glyphs, active; }
        readonly List<PerfRow> perfRows = new List<PerfRow>();
        string LocTitle(Location l)
        {
            if (l == tavern) return "Трактир"; if (l == field) return "Поляна"; if (l == forest) return "Лес"; if (l == cave) return "Пещера";
            var m = l as Mockup; return m != null ? m.title : "?";
        }
        /// put the hero in a location (at its spawn), switch on what is round it
        void PerfGo(Location l)
        {
            if (l == tavern) { EnterWorld(0); return; }
            if (!world || loc == tavern || !outdoor.Contains(loc)) EnterWorld(1);
            pos = l.SpawnW; heroTrail = pos; player.ResetSim();
            hero.go.transform.position = pos; camTgt = new Vector3(pos.x, 0.6f, pos.z);
            loc = l; UpdateOutdoor(true);
        }
        void StartPerfTest()
        {
            if (perfRun) return;
            inMenu = false; invOpen = false; menuFromWorld = false;
            // everything is built first (the world, the tavern): the building must not fall into a measurement
            try { SetupOutdoor(); if (tavern == null) SetupWorld(); } catch (System.Exception e) { Debug.LogException(e); }
            if (!world || loc != field) EnterWorld(true);
            if (!world) return;
            worldZoomT = 1; perfRun = true; perfIx = 0; PerfReset();
            savedVsync = QualitySettings.vSyncCount; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 240;
            Debug.Log("ТЕСТ НАГРУЗКИ · " + VERSION + " · " + SystemInfo.deviceModel + " · " + SystemInfo.graphicsDeviceName + " · экран " + Screen.width + "×" + Screen.height +
                      " @" + Screen.currentResolution.refreshRateRatio.value.ToString("0") + " Гц · качество " + QUALITY[setQuality] + ", трава " + GRASS[setGrass] + " %, разрешение " + RES[setRes] + " %, ограничение в игре " + FPS[setFps] + " к/с (тест идёт без ограничения)");
            if (worldBuildMs > 0) Debug.Log("ТЕСТ · мир (все биомы) строился " + (worldBuildMs / 1000f).ToString("0.0") + " с · память: " + (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576) + " МБ занято, " + (UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576) + " МБ взято у системы, графика " + (UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576) + " МБ");
            perfLocs.Clear(); perfRows.Clear();
            foreach (var l in outdoor) perfLocs.Add(l);
            perfLocs.Add(null);         // (null: the tavern, built when it is reached)
            // part 0: a quick round of every place first, not measured - the first time anything is shown the device
            // prepares its shaders and uploads its meshes (that cost 0.2 s a frame in the first place measured)
            perfPhase = 0; perfLocIx = 0; perfWalk = true; perfHeavy = null; perfHeavyMs = 0;
            PerfGo(perfLocs[0]);
        }
        int pMiss, pHitch; float perfBaseGpu;
        const float PERF_T1 = 5.5f, PERF_T2 = 4.2f;      // seconds per biome, per stage (the first 1.5 s settle and are not counted)
        void PerfReset() { pMiss = 0; pHitch = 0; perfT = 0; pSum = pMain = pRender = pGpu = accU = accA = accG = 0; pN = pFt = 0; pWorst = 0; }
        void PerfSet(int ix, bool on)
        {
            switch (ix)
            {
                case 1: Shader.SetGlobalFloat("_AsciiGrassThin", on ? 1f : 1 - GRASS[setGrass] / 100f); break;
                case 2: ascii.dbgNoGlyphs = on; break;
                case 3: if (hero != null) hero.go.SetActive(!on); break;
                case 4: perfNoAnim = on; break;
                case 5: if (on) { shadowScale = 0; SetShadowDistance(shadowBase); } else ApplySettings(false); break;
                case 6: ascii.dbgNoBloom = on; break;
                case 7: perfNoGui = on; break;
                case 8: ascii.outline = on ? false : setOutline; break;
                case 9: ascii.dbgForceND = on; break;
                case 10: perfFullActors = on; break;
                case 11: if (on) { int old = setRes; setRes = 1; ApplySettings(false); setRes = old; } else ApplySettings(false); break;
                case 12: if (hero != null) foreach (var r in hero.glyphs) if (r != null) r.enabled = !on; break;
                case 13: if (loc != null && loc.root != null) foreach (Transform c in loc.root.transform) if (c.name != "Ground" && c.name != "Grass" && c.name != "Path") c.gameObject.SetActive(!on); break;
            }
            if (perfRun) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 240; }
        }
        void PerfUpdate()
        {
            if (!perfRun) return;
            float udt = Time.unscaledDeltaTime; perfT += udt;
            if (perfT > 1.5f && udt > 0.5f) pHitch++;      // (a hitch of half a second or more: a switch or a load, not the scene's cost)
            else if (perfT > 1.5f)
            {
                pSum += udt * 1000.0; pN++; pWorst = Mathf.Max(pWorst, udt * 1000f); if (udt > 0.0185f) pMiss++;
                if (fFtFrame) { pMain += ftBuf[0].cpuMainThreadFrameTime; pRender += ftBuf[0].cpuRenderThreadFrameTime; pGpu += ftBuf[0].gpuFrameTime; pFt++; }
            }
            else { accU = accA = accG = 0; }
            if (perfPhase == 0)
            {   // the warm-up round: 0.8 s in each place
                if (perfT < 0.8f) return;
                perfLocIx++;
                if (perfLocIx < perfLocs.Count) { var nx = perfLocs[perfLocIx] ?? tavern; PerfGo(nx); PerfReset(); return; }
                System.GC.Collect();
                Debug.Log("ТЕСТ · прогрев: все места показаны по разу (шейдеры и сетки подготовлены), замеры начинаются");
                perfPhase = 1; perfLocIx = 0; PerfGo(perfLocs[0]); PerfReset(); return;
            }
            if (perfT < (perfPhase == 1 ? PERF_T1 : PERF_T2) || pN == 0) return;
            float ms = (float)(pSum / pN), miss = 100f * pMiss / pN;
            if (perfPhase == 1)
            {
                // ---- part 1: one biome done ----
                GlyphLayer.Recount();
                int act = 0; foreach (var l in outdoor) if (l.Active) act++;
                var row = new PerfRow { name = LocTitle(loc), ms = ms, worst = pWorst, miss = miss, gpu = pFt > 0 ? (float)(pGpu / pFt) : -1, main = pFt > 0 ? (float)(pMain / pFt) : -1, glyphs = GlyphLayer.count, active = loc == tavern ? 1 : act };
                perfRows.Add(row);
                string line = "ТЕСТ · " + row.name + ": " + ms.ToString("0.0") + " мс (" + (1000f / ms).ToString("0") + " к/с), худший кадр " + pWorst.ToString("0") + " мс, кадров дольше 1/60 с: " + miss.ToString("0") + " %";
                if (pFt > 0) line += " · главный поток " + (pMain / pFt).ToString("0.0") + ", поток отрисовки " + (pRender / pFt).ToString("0.0") + ", GPU " + (pGpu / pFt).ToString("0.0") + " мс";
                line += " · символов " + row.glyphs + " · включено локаций " + row.active + " · зеркало воды " + (ascii.waterOn ? "да" : "нет") + " · скрипты " + (accU / pN).ToString("0.00") + " мс";
                if (pHitch > 0) line += " · рывков от 0,5 с (не в счёт): " + pHitch;
                Debug.Log(line);
                if (ms > perfHeavyMs && loc != tavern) { perfHeavyMs = ms; perfHeavy = loc; }
                perfLocIx++;
                if (perfLocIx < perfLocs.Count)
                {
                    var next = perfLocs[perfLocIx];
                    if (next == null) { if (tavern == null) SetupWorld(); next = tavern; }
                    PerfGo(next); PerfReset(); return;
                }
                // part 2: the parts of the frame, in the heaviest biome
                Debug.Log("ТЕСТ · самый тяжёлый биом — " + LocTitle(perfHeavy) + ": по частям кадра");
                perfPhase = 2; perfIx = 0; PerfGo(perfHeavy); PerfReset(); return;
            }
            // ---- part 2: one stage done ----
            if (perfIx == 0) perfBase = ms;
            string ln = "ТЕСТ · " + LocTitle(perfHeavy) + " · " + PERF[perfIx] + ": " + ms.ToString("0.0") + " мс (" + (1000f / ms).ToString("0") + " к/с), худший кадр " + pWorst.ToString("0") + " мс";
            ln += ", кадров дольше 1/60 с: " + miss.ToString("0") + " %";
            if (perfIx > 0) ln += ", к базе " + (ms - perfBase).ToString("+0.0;-0.0") + " мс";
            if (pFt > 0) { float g = (float)(pGpu / pFt); if (perfIx == 0) perfBaseGpu = g; else ln += " (GPU " + (g - perfBaseGpu).ToString("+0.0;-0.0") + ")"; }
            if (pFt > 0) ln += " · главный поток " + (pMain / pFt).ToString("0.0") + ", поток отрисовки " + (pRender / pFt).ToString("0.0") + ", GPU " + (pGpu / pFt).ToString("0.0") + " мс";
            ln += " · скрипты: Update " + (accU / pN).ToString("0.00") + " (анимация и ткань " + (accA / pN).ToString("0.00") + "), интерфейс " + (accG / pN).ToString("0.00") + " мс";
            Debug.Log(ln);
            PerfSet(perfIx, false); perfIx++;
            if (perfIx < PERF.Length) { PerfGo(perfHeavy); PerfSet(perfIx, true); PerfReset(); return; }      // (back to the same spot for every stage)
            // ---- the end: the table ----
            perfRun = false; perfWalk = false; QualitySettings.vSyncCount = savedVsync; ApplySettings(false);
            var sb = new System.Text.StringBuilder("ТЕСТ · ИТОГ по биомам (60 к/с = кадр до 16,7 мс; 30 к/с = до 33,3 мс):");
            int ok60 = 0, ok30 = 0;
            foreach (var r in perfRows)
            {
                bool a60 = r.ms <= 17.2f && r.miss < 5, a30 = r.ms <= 33.5f;      // (60: the mean within a hair of 1/60 s, hardly a frame missed)
                if (a60) ok60++; if (a30) ok30++;
                sb.Append("\n  ").Append(a60 ? "[60] " : a30 ? "[30] " : "[<30] ").Append(r.name).Append(": ").Append(r.ms.ToString("0.0")).Append(" мс, ").Append((1000f / r.ms).ToString("0")).Append(" к/с, худший ").Append(r.worst.ToString("0"))
                  .Append(" мс, >1/60: ").Append(r.miss.ToString("0")).Append(" %").Append(r.gpu >= 0 ? ", GPU " + r.gpu.ToString("0.0") + " мс" : "").Append(", символов ").Append(r.glyphs);
            }
            sb.Append("\n  держат 60 к/с: ").Append(ok60).Append(" из ").Append(perfRows.Count).Append(", 30 к/с: ").Append(ok30).Append(" из ").Append(perfRows.Count);
            Debug.Log(sb.ToString());
            Debug.Log("ТЕСТ ЗАВЕРШЁН · «к базе» со знаком минус — сколько миллисекунд кадра стоит выключенная часть");
            EnterWorld(1);
            consoleOpen = true; ShowToast("Тест завершён: открой лог, нажми «Копировать» и пришли его");
        }
        bool fFtFrame;
        void FrameTimes()
        {   // CPU / GPU frame times from the engine (when the device reports them)
            fFtFrame = false;
            if (!perfRun && !setShowFps) return;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ftBuf) > 0 && ftBuf[0].cpuFrameTime > 0) { fFtFrame = true; fMain += ftBuf[0].cpuMainThreadFrameTime; fGpu += ftBuf[0].gpuFrameTime; fFt++; }
        }
        void OnDestroy() { if (ascii != null) ascii.Dispose(); }
        void ShowToast(string t) { toast = t; toastT = 2.6f; }

        // ---------- per frame ----------
        void Update()
        {
            long tU = System.Diagnostics.Stopwatch.GetTimestamp();
            InputHub.Poll();
            LogHub.Pump();
            if (!ready) { HandlePointers(0); return; }
            float dt = Mathf.Min(0.1f, Time.deltaTime); tNow = Time.time;
            KeyboardUpdate(); FrameTimes(); PerfUpdate();
            if (rebuildPending) { rebuildPending = false; Rebuild(); }
            HandlePointers(dt);
            if (InputHub.Key("Back"))
            {
                if (world) { if (menuFromWorld) menuFromWorld = false; else if (invOpen) ClosePocket(); else OpenBackpack(); }
                else Application.Quit();
            }

            if (world) WorldUpdate(dt);
            else
            {
                if (!interacting)
                {
                    if (Mathf.Abs(vel) > 1e-4f) { cam.yaw += vel; vel *= Mathf.Pow(0.04f, dt); }
                    else if (autoRotate && Time.time - lastInteract > 1.5f) cam.yaw += dt * 0.4f;
                }
                player.HoldInPlace(dt);
            }
            if (world && invOpen)
            {
                invT += dt; pocketT += dt;
                if (pocketClose >= 0) { pocketClose += dt / 0.35f; if (pocketClose >= 1) { invOpen = false; pocketClose = -1; } }
            }
            if (player.driver == null && !perfNoAnim) { long tA = System.Diagnostics.Stopwatch.GetTimestamp(); player.Animate(tNow, dt); accA += Ms(tA); }   // Animator mode: after the Animator, in LateUpdate
            if (toastT > 0) toastT -= dt;
            fpsAcc += Time.unscaledDeltaTime; fpsN++;
            if (fpsAcc > 1)
            {
                if (setShowFps && ascii.surfaceGlyphs) GlyphLayer.Recount();
                fpsText = Mathf.RoundToInt(fpsN / fpsAcc) + " fps · " + (ascii.surfaceGlyphs ? GlyphLayer.count + " симв." : ascii.cols + "×" + ascii.rows);
                if (fFt > 0) fpsText += " · CPU " + (fMain / fFt).ToString("0.0") + " · GPU " + (fGpu / fFt).ToString("0.0") + " мс";
                fpsAcc = 0; fpsN = 0; fMain = fGpu = 0; fFt = 0;
            }
            accU += Ms(tU);
        }

        // a second finger coming down next to one that is on the free part of the view (or that has only just touched
        // the stick area and not moved yet) makes a pinch, not a stick
        bool PinchPartner(Pointer p)
        {
            foreach (var q in InputHub.active.Values)
            {
                if (q == p) continue;
                if (q.owner == "cam") return true;
                if (q.id == stickId && Time.unscaledTime - q.t0 < 0.4f && (q.pos - q.start).magnitude < 0.35f * StickCm())
                { q.owner = "cam"; stickId = int.MinValue; stick = Vector2.zero; stickRun = 0; stickRunArmed = false; return true; }
                if (q.owner == "rt" && Time.unscaledTime - q.t0 < 0.4f && (q.pos - q.start).magnitude < 0.35f * StickCm()) { q.owner = "cam"; return true; }
            }
            return false;
        }
        static bool InCircle(Rect r, Vector2 p) { return r.width > 0 && (p - r.center).magnitude <= r.width * 0.56f; }
        bool InUi(Vector2 p) { foreach (var r in uiRectsPrev) if (r.Contains(p)) return true; return false; }

        void HandlePointers(float dt)
        {
            foreach (var p in InputHub.active.Values)
            {
                if (!p.began) continue;
                // floating stick: its centre appears under the finger anywhere in the left part of the screen
                if (world && (invOpen || menuFromWorld || perfRun || InUi(p.pos))) p.owner = "ui";      // the log and its buttons come before the controls
                else if (world && PinchPartner(p)) p.owner = "cam";
                else if (world && p.pos.x < Screen.width * 0.5f && stickId == int.MinValue)
                {   // the stick: invisible, its centre where the finger came down; a tap just before (close by) means run
                    p.owner = "stick"; stickId = p.id; stickCenter = p.pos;
                    stickRunArmed = Time.unscaledTime - lastLeftTapT < 0.38f && (p.pos - lastLeftTapPos).magnitude < 1.5f * StickCm();
                    lastLeftTapT = -9;
                }
                else if (world) p.owner = "rt";      // a gesture: tap, hold or swipe (decided below)
                else if (!world && !inMenu && tab == 0 && setDev && InField(p.pos)) p.owner = "ui";      // a number box: a tap opens the keyboard
                else if (!world && !inMenu && tab == 0 && bodyList.Contains(p.pos))
                {   // body sliders: a finger on a track drags its value, anywhere else in the list scrolls it
                    p.owner = "blist";
                    for (int i = 0; i < bodyTracks.Count; i++) if (bodyTracks[i].Contains(p.pos)) { p.owner = "bs:" + bodyTrackIx[i]; bodyTrackHit = bodyTracks[i]; break; }
                }
                else if (!world && sliderRect.Contains(p.pos)) p.owner = "slider";
                else if (InUi(p.pos)) p.owner = "ui";
                else
                {
                    p.owner = "cam";
                    if (!world)
                    {
                        interacting = true; vel = 0;
                        if (Time.time - lastTap < 0.3f) cam = CAM0;
                        lastTap = Time.time;
                    }
                }
            }
            foreach (var p in InputHub.ended)
            {
                if (p.owner == "ui" && (p.pos - p.start).magnitude < 40 * k)
                {
                    if (world && invOpen && !menuFromWorld && !InPocket(p.pos)) ClosePocket();      // a tap outside the smoke closes the pocket
                    else taps.Add(p.pos);
                }
                if (world && (p.owner == "stick" || p.owner == "rt"))
                {
                    bool tap = Time.unscaledTime - p.t0 < 0.25f && (p.pos - p.start).magnitude < 0.35f * StickCm();
                    if (tap && OnHero(p.start)) OpenBackpack();
                    else if (tap && p.owner == "stick") { lastLeftTapT = Time.unscaledTime; lastLeftTapPos = p.pos; }
                    else if (tap) DoAttack();
                }
                if (p.id == stickId) { stickId = int.MinValue; stick = Vector2.zero; stickRun = 0; stickRunArmed = false; }
            }
            // right-hand gestures: moved far enough soon enough - a swipe (dash that way); kept still - a hold (block)
            if (world)
                foreach (var p in InputHub.active.Values)
                {
                    if (p.owner != "rt") continue;
                    float el = Time.unscaledTime - p.t0; Vector2 mv = p.pos - p.start;
                    if (mv.magnitude > 0.55f * StickCm()) { p.owner = "rt:done"; if (el < 0.45f) DoDash(mv); }
                    else if (el > 0.22f) p.owner = "rt:hold";
                }
            // slider
            bool dragging = false;
            foreach (var p in InputHub.active.Values)
            {
                if (p.owner == "blist") bodyScroll = Mathf.Clamp(bodyScroll - p.Delta.y, 0, bodyScrollMax);
                if (p.owner != null && p.owner.StartsWith("bs:"))
                {
                    int ix = int.Parse(p.owner.Substring(3)); var bp = Body.ALL[ix]; dragging = true;
                    var tr = bodyTrackHit; float u = Mathf.Clamp01((p.pos.x - tr.x) / Mathf.Max(1, tr.width));
                    float val = bp.min + (bp.max - bp.min) * u;
                    val = bp.factor ? Mathf.Round(val * 100) / 100 : Mathf.Round(val);
                    SetBodyValue(ix, val);
                }
            }
            // the body is rebuilt while a slider is dragged (a few times a second) and once more when it is let go
            if (bodyDirty && (!dragging || Time.time - bodyBuiltAt > 0.35f)) { bodyDirty = false; bodyBuiltAt = Time.time; rebuildPending = true; SaveCfg(); }
            // stick
            Pointer sp; if (stickId != int.MinValue && InputHub.active.TryGetValue(stickId, out sp))
            {
                Vector2 d = sp.pos - stickCenter; float max = Mathf.Max(stickR, 62 * k) * 0.78f;
                // the centre follows a finger that goes past the edge, so turning back answers at once
                if (d.magnitude > max) { stickCenter += d.normalized * (d.magnitude - max); d = d.normalized * max; }
                stickRun = stickRunArmed ? 1 : 0;
                stick = d / max;
            }
            // camera
            camPtrs.Clear(); foreach (var p in InputHub.active.Values) if (p.owner == "cam") camPtrs.Add(p);
            float W = Screen.width, H = Screen.height;
            if (world)
            {
                // the world camera keeps its angle; two fingers zoom it in (x1 .. x2)
                if (camPtrs.Count >= 2)
                {
                    float d = Mathf.Max(20, (camPtrs[0].pos - camPtrs[1].pos).magnitude);
                    if (!wPinching) { wPinching = true; wPinchD0 = d; wPinchZ0 = worldZoomT; }
                    worldZoomT = Mathf.Clamp(wPinchZ0 * d / wPinchD0, 1f, 2f);
                }
                else wPinching = false;
                if (InputHub.scroll != 0) worldZoomT = Mathf.Clamp(worldZoomT * (1 + InputHub.scroll * 0.1f), 1f, 2f);
            }
            else
            {
                float vw = ViewRect().width;
                if (camPtrs.Count == 1)
                {
                    pinching = false;
                    var d = camPtrs[0].Delta; float kk = 6.0f / Mathf.Max(vw, 320);
                    cam.yaw -= d.x * kk; vel = -d.x * kk; cam.pitch = Mathf.Clamp(cam.pitch + d.y * kk * 0.6f, -0.2f, 0.75f);
                    lastInteract = Time.time;
                }
                else if (camPtrs.Count >= 2)
                {
                    Vector2 a = camPtrs[0].pos, b = camPtrs[1].pos; float d = (a - b).magnitude, my = (a.y + b.y) / 2;
                    if (!pinching) { pinching = true; pinchD0 = d; pinchDist0 = cam.dist; pinchMy0 = my; pinchTy0 = cam.ty; }
                    cam.dist = Mathf.Clamp(pinchDist0 * pinchD0 / Mathf.Max(20, d), 0.9f, 6);
                    cam.ty = Mathf.Clamp(pinchTy0 + (my - pinchMy0) / H * cam.dist * 0.8f, 0.2f, 1.9f);
                    lastInteract = Time.time; vel = 0;
                }
                else { pinching = false; if (interacting) { interacting = false; lastInteract = Time.time; } }
                if (InputHub.scroll != 0) cam.dist = Mathf.Clamp(cam.dist * (1 - InputHub.scroll * 0.1f), 0.9f, 6);
            }
        }

        float StickCm() { return Screen.dpi > 0 ? Screen.dpi / 2.54f : 63f * k; }
        // keep the centre far enough from the screen edges that a full run pull (ring + 0.7 cm) fits in every direction, also downwards
        Vector2 ClampStickCenter(Vector2 p)
        {
            float r = Mathf.Max(62 * k, 1f), reach = r * 0.78f + 0.95f * StickCm();
            return new Vector2(Mathf.Max(p.x, safe.x + reach), Mathf.Min(p.y, safe.yMax - reach));
        }

        static Vector3 Mirror(Vector3 v) { return new Vector3(-v.x, v.y, v.z); }

        // ---------- actions: attack, guard, dash ----------
        Cloak cloak; readonly List<Renderer> cloakRs = new List<Renderer>(); MaterialPropertyBlock clothMpb;
        Vector3 windV; float windA = 0.7f, windStr = 0.3f, windTarget = 0.4f, windPhaseT, windRun; int windPhase = 1;
        class Piece { public string kind = ""; public List<Renderer> rs = new List<Renderer>(); public float delay, om = 11, zeta = 0.45f, lift = 0.3f, wob, turns = 1, side; public Vector3 axis = Vector3.up; }
        readonly List<Piece> pieces = new List<Piece>(); MaterialPropertyBlock shiftMpb;
        const float DASH_DIST = 2.0f, DASH_T = 0.1f; float dashAnim = 0.4f, dashSettle = 0;
        float dashT = 9, dashCd, dashMist, dashLen = 2; readonly Vector4[] dashObst = new Vector4[8], dashBox = new Vector4[4]; Vector3 dashFrom, dashDir; Vector2? moveDir; bool dashShifted;
        Rect atkRect, defRect, dashRect;
        void DoAttack() { if (world && player != null && dashT >= DASH_T) player.StartAttack(); }
        void DoDash(Vector2? swipe = null)
        {
            if (!world || player == null || dashCd > 0 || !(limbs[3] || limbs[4])) return;
            if (player.IdleBusy) { player.PokeIdle(); return; }      // sitting: he gets up first
            // moving: where the stick points; standing: where the hero looks
            Vector3 d = moveDir.HasValue ? new Vector3(moveDir.Value.x, 0, moveDir.Value.y) : new Vector3(Mathf.Sin(player.heading), 0, Mathf.Cos(player.heading));
            if (swipe.HasValue && swipe.Value.sqrMagnitude > 1e-4f) { var w = ScreenToWorldDir(swipe.Value); d = new Vector3(w.x, 0, w.y); }      // a swipe: where the finger went
            dashDir = d.normalized; dashT = 0; dashFrom = pos; dashMist = 0; player.StartLeap(); player.PokeIdle();
            // the shadow passes straight through trees, rocks and furniture; it only has to come out on free ground:
            // if the end of the dash is inside something it goes on until it is clear (or, failing that, stops short)
            dashLen = DASH_DIST;
            // the obstacles near the path, for the chain-mail links (they are moved in the glyph shader)
            {
                int nc = 0, nbx = 0; Vector2 a0 = new Vector2(pos.x, pos.z), a1 = a0 + new Vector2(dashDir.x, dashDir.z) * (DASH_DIST + 1.5f);
                foreach (var c in loc.colliders)
                {
                    Vector2 cc = (c.circle ? new Vector2(c.x, c.z) : new Vector2((c.minx + c.maxx) / 2, (c.minz + c.maxz) / 2)) + new Vector2(loc.origin.x, loc.origin.z);
                    float rr = c.circle ? c.r : 0.5f * new Vector2(c.maxx - c.minx, c.maxz - c.minz).magnitude;
                    Vector2 ab = a1 - a0; float tt = Mathf.Clamp01(Vector2.Dot(cc - a0, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                    if ((a0 + ab * tt - cc).magnitude > rr + 1.6f) continue;
                    if (c.circle && nc < 8) dashObst[nc++] = new Vector4(c.x, c.z, c.r, 0);
                    else if (!c.circle && nbx < 4) dashBox[nbx++] = new Vector4(c.minx, c.minz, c.maxx, c.maxz);
                }
                Shader.SetGlobalVectorArray("_DashObst", dashObst); Shader.SetGlobalVectorArray("_DashBox", dashBox);
                Shader.SetGlobalFloat("_DashObstN", nc); Shader.SetGlobalFloat("_DashBoxN", nbx);
            }
            System.Func<float, bool> free = l => { var q = new Vector2(pos.x + dashDir.x * l, pos.z + dashDir.z * l); return (WCollide(q, 0.27f) - q).sqrMagnitude < 1e-6f; };
            if (!free(dashLen)) { float l = dashLen; while (l < DASH_DIST + 1.5f && !free(l)) l += 0.1f; if (free(l)) dashLen = l; else { l = DASH_DIST; while (l > 0 && !free(l)) l -= 0.1f; dashLen = Mathf.Max(0, l); } }
            // the armour follows piece by piece, one after another, like a suit assembling itself. Every piece is pulled in
            // by its own spring: it accelerates gently, overshoots its place, swings back and only then settles; it tumbles
            // on the way. Order, timing, stiffness, path and spin are drawn anew for every dash.
            var order = new List<Piece>(pieces);
            for (int i = order.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); var tmp = order[i]; order[i] = order[j]; order[j] = tmp; }
            float gap = Mathf.Min(0.05f, 0.5f / Mathf.Max(1, order.Count)), at = DASH_T * 0.7f;
            foreach (var pc in order)
            {
                pc.delay = at; at += gap * (0.5f + Random.value);
                pc.om = 9f + Random.value * 5f; pc.zeta = 0.34f + Random.value * 0.22f;
                pc.axis = Random.onUnitSphere; pc.turns = (Random.value < 0.5f ? -1 : 1) * (Random.value < 0.65f ? 1 : 2);
                pc.side = (Random.value - 0.5f) * 0.9f; pc.lift = 0.12f + Random.value * 0.4f; pc.wob = (Random.value - 0.5f) * 1.6f;
            }
            // a robe is not thrown about: it flies after the shadow in one piece, like a cloak with a will of its own - a slow
            // sweep up and over, no tumbling. Chain mail leaves at once and comes apart into its links (see the glyph shader).
            foreach (var pc in pieces)
            {
                if (pc.kind == "robe") { pc.delay = DASH_T * 0.9f; pc.om = 6.5f; pc.zeta = 0.62f; pc.turns = 0; pc.wob = 0; pc.side = 0.25f; pc.lift = 0.55f; }
                if (pc.kind == "chain") pc.delay = DASH_T * 0.5f;
            }
            dashAnim = at + 0.3f; dashSettle = at + 1.3f; dashCd = dashAnim + 0.5f;
            player.Teleport(0, 0, Mathf.Atan2(dashDir.x, dashDir.z));
        }
        // a flying piece of armour does not go into a tree or a rock: it slides round it
        Vector3 Around(Piece pc, Vector3 sh)
        {
            if (pc.rs.Count == 0 || pc.rs[0] == null) return sh;
            Vector3 c = pc.rs[0].bounds.center + sh; var q = new Vector2(c.x, c.z); var q2 = WCollide(q, 0.16f);
            return sh + new Vector3(q2.x - q.x, 0, q2.y - q.y);
        }
        // the shadow's own body (the smoke-material glyphs of the hero) during the dash, see AsciiGlyph
        void SetSmoke(Vector3 back, float t)
        {
            if (shiftMpb == null) shiftMpb = new MaterialPropertyBlock();
            foreach (var r in hero.glyphs)
            {
                if (r == null || r.sharedMaterial == null || !r.sharedMaterial.HasProperty("_GlyphSmoke") || r.sharedMaterial.GetFloat("_GlyphSmoke") < 0.5f) continue;
                r.GetPropertyBlock(shiftMpb); shiftMpb.SetVector("_DashSmoke", new Vector4(back.x, back.y, back.z, t)); r.SetPropertyBlock(shiftMpb);
            }
        }
        void SetShift(Piece pc, Vector3 v, Vector4 spin, float cloth = 0)
        {
            if (shiftMpb == null) shiftMpb = new MaterialPropertyBlock();
            Vector3 piv = pc.rs.Count > 0 && pc.rs[0] != null ? pc.rs[0].bounds.center : pos;     // the piece tumbles about its own centre
            foreach (var r in pc.rs)
            {
                if (r == null) continue; r.GetPropertyBlock(shiftMpb);
                shiftMpb.SetVector("_DashShift", v); shiftMpb.SetVector("_DashSpin", spin); shiftMpb.SetVector("_DashPivot", piv); shiftMpb.SetVector("_DashScatter", Vector4.zero); shiftMpb.SetFloat("_ClothDash", cloth);
                r.SetPropertyBlock(shiftMpb);
            }
        }
        void UpdateDash(float dt)
        {
            dashCd = Mathf.Max(0, dashCd - dt);
            if (dashT > dashSettle)
            {
                if (dashShifted) { foreach (var pc in pieces) SetShift(pc, Vector3.zero, new Vector4(0, 1, 0, 0)); SetSmoke(Vector3.zero, 0); dashShifted = false; }
                return;
            }
            if (dashT < DASH_T)
            {   // the shadow itself crosses the distance in a tenth of a second
                float kk = Mathf.Min(dt, DASH_T - dashT) / DASH_T;
                var dp = new Vector2(pos.x + dashDir.x * dashLen * kk, pos.z + dashDir.z * dashLen * kk);      // no collision on the way
                player.Teleport(dp.x - pos.x, dp.y - pos.z, player.heading);
                pos = new Vector3(dp.x, 0, dp.y);
            }
            dashT += dt;
            Vector3 back = dashFrom - pos; dashShifted = true;
            SetSmoke(back, dashT < 0.8f ? Mathf.Max(dashT, 1e-4f) : 0);        // the shadow's body flows over as smoke
            Vector3 sideV = new Vector3(-dashDir.z, 0, dashDir.x);
            bool last = dashT >= dashSettle;
            foreach (var pc in pieces)
            {
                float tau = Mathf.Max(0, dashT - pc.delay), wd = pc.om * Mathf.Sqrt(1 - pc.zeta * pc.zeta), T1 = Mathf.PI / wd;
                // spring response: 0 -> past 1 -> back -> 1
                float p = last ? 1 : 1 - Mathf.Exp(-pc.zeta * pc.om * tau) * (Mathf.Cos(wd * tau) + pc.zeta * pc.om / wd * Mathf.Sin(wd * tau));
                float u = Mathf.Min(1, tau / T1), sm = last ? 1 : u * u * u * (u * (u * 6 - 15) + 10), arc = Mathf.Sin(Mathf.PI * sm);
                if (pc.kind == "chain")
                {
                    bool on = tau > 0 && tau < 0.86f && !last;
                    if (shiftMpb == null) shiftMpb = new MaterialPropertyBlock();
                    foreach (var r in pc.rs)
                    {
                        if (r == null) continue; bool gl = r.gameObject.layer == GlyphLayer.LAYER; r.GetPropertyBlock(shiftMpb);
                        // while the links fly, the solid mail is taken away (it only feeds the glyphs their light)
                        shiftMpb.SetVector("_DashShift", on ? (gl ? Vector3.zero : new Vector3(0, -60, 0)) : tau > 0 ? Vector3.zero : back);
                        shiftMpb.SetVector("_DashScatter", on && gl ? new Vector4(back.x, back.y, back.z, Mathf.Max(tau, 1e-4f)) : Vector4.zero);
                        shiftMpb.SetVector("_DashSpin", new Vector4(0, 1, 0, 0)); r.SetPropertyBlock(shiftMpb);
                    }
                    continue;
                }
                if (pc.kind == "robe") { SetShift(pc, Vector3.zero, new Vector4(0, 1, 0, 0)); continue; }     // the robe stays on the body: the cloth simulation streams the skirt out behind
                Vector3 sh = Around(pc, back * (1 - p) + sideV * (pc.side * arc) + Vector3.up * (pc.lift * arc));
                SetShift(pc, sh, new Vector4(pc.axis.x, pc.axis.y, pc.axis.z, tau > 0 && !last ? pc.turns * Mathf.PI * 2 * (1 - sm) + pc.wob * (sm - p) * 3 : 0));
            }
            // mist hangs along the path
            dashMist -= dt;
            if (dashMist <= 0 && dashT < dashAnim) { dashMist = 0.035f; float rr = Random.value; SpawnPuff(new Vector3(pos.x + back.x * rr, 0.35f + Random.value * 1.1f, pos.z + back.z * rr), 0.07f, new Vector3(0, 0.15f, 0)); }
        }

        void WorldUpdate(float dt)
        {
            float ix = stick.x, iy = stick.y;
            if (perfRun && perfWalk) { float a = perfT * Mathf.PI * 2 / (perfPhase <= 1 ? PERF_T1 : PERF_T2); ix = Mathf.Cos(a) * 0.62f; iy = Mathf.Sin(a) * 0.62f; }      // (the test: he walks a small circle)
            if (InputHub.Key("W")) iy = -1; if (InputHub.Key("S")) iy = 1;
            if (InputHub.Key("A")) ix = -1; if (InputHub.Key("D")) ix = 1;
            float l = Mathf.Sqrt(ix * ix + iy * iy), mag = Mathf.Min(1, l / 0.8f); if (mag < 0.12f) mag = 0;   // 80 % of the ring = full walk
            float runIn = Mathf.Max(stickRun, InputHub.Key("Run") ? 1f : 0f);
            if (mag > 0.85f || (mag > 0 && runIn > 0)) mag = 1 + runIn;
            { float sk = loc.SpeedKW(pos); if (sk != 1 && mag > sk) mag = sk; }        // sticky mud: a slow wade
            {   // water: the deeper, the slower - down to the walk (WaterDepth.WadeK); the walk itself is not slowed
                float wd = loc.DepthW(pos); player.wadeDepth = wd;
                player.speedCap = wd <= 0.05f ? 99f : Mathf.Max(1.05f, WaterDepth.WadeK(wd) * (player.maxSpeed + 0.5f));
            }
            Vector2? dir = null;
            if (mag > 0) dir = ScreenToWorldDir(new Vector2(ix, iy));
            // aim: in a fight (a cut, a block or a blow in the last few seconds) the hero faces the nearest creature
            // within 5 m; out of the fight or out of reach he moves freely
            Vector2? lockPos = null;
            bool inCombat = player.combatT > 0 || player.Attacking || player.guardHeld;
            if (inCombat) { var tg = NearestEntity(AIM_R); if (tg.HasValue) lockPos = tg; }
            moveDir = dir;
            bool guardPtr = false; foreach (var ap in InputHub.active.Values) if (ap.owner == "rt:hold") guardPtr = true;
            player.guardHeld = guardPtr || InputHub.Key("Guard");
            bool atkPtr = false;
            player.atkHeld = InputHub.Key("AttackHeld");
            if (dir.HasValue || guardPtr || atkPtr || InputHub.Key("Attack") || InputHub.Key("Guard") || InputHub.Key("Dash")) player.PokeIdle();
            if (player.IdleBusy) { dir = null; mag = 0; }          // getting up from the ground first
            if (InputHub.Key("Attack")) DoAttack();
            if (InputHub.Key("Dash")) DoDash();
            var dd = player.Locomotion(dt, dir, mag, lockPos, new Vector2(pos.x, pos.z), p => NoDeeper(WCollide(p, 0.27f)));
            var np = NoDeeper(WCollide(new Vector2(pos.x + dd.x, pos.z + dd.y), 0.27f));
            pos = new Vector3(np.x, 0, np.y);
            UpdateDash(dt);
            if (player.trail.Count > 0) { foreach (var q in player.trail) SpawnPuff(q, 0.03f, Vector3.zero, 0.3f); player.trail.Clear(); }
            // in the lake the hero sinks a little (up to 18 cm in the middle; the feet and shins are under the surface)
            {
                float want = loc.WaterSinkW(pos);
                heroSink += (want - heroSink) * Mathf.Min(1, dt * 6);
            }
            hero.go.transform.position = pos + Vector3.down * heroSink;
            UpdateOutdoor(); Doors(dt); UpdateGlows();
            float idleYaw = 0;
            if (player.idleYawW > 0) { Vector3 co = Mirror(new Vector3(Mathf.Sin(isoYaw), 0, Mathf.Cos(isoYaw))); idleYaw = Mathf.DeltaAngle(player.heading * Mathf.Rad2Deg, Mathf.Atan2(co.x, co.z) * Mathf.Rad2Deg) * Mathf.Deg2Rad * player.idleYawW; }   // waving: turned to the camera
            hero.go.transform.rotation = Quaternion.Euler(0, (player.heading - player.spinYaw + idleYaw) * Mathf.Rad2Deg, 0);      // the full-turn sweep spins the whole figure
            loc.Update(tNow); Pickups(); UpdateDummies(dt, tNow); UpdateDebris(dt); UpdateDanger(tNow);
            // the lake: every step in the water sends out a ring (standing still, now and then a small one)
            if (loc.HasWater)
            {
                float sp = player.Velocity.magnitude; rippleT -= dt;
                if (loc.InWaterW(pos) && rippleT <= 0)
                {
                    ripples[rippleIx] = new Vector4(pos.x, pos.z, tNow, sp > 0.3f ? 1.15f + 0.15f * Mathf.Min(sp, 4f) : 0.7f); rippleIx = (rippleIx + 1) % ripples.Length;
                    rippleT = sp > 0.3f ? Mathf.Lerp(0.42f, 0.24f, Mathf.Clamp01(sp / 4f)) : 1.1f + Random.value;
                }
                Shader.SetGlobalVectorArray("_WaterRip", ripples);
                UpdateSplash(dt, sp);
                UpdateWaterMirror();
            }
            if (respawnT > 0) { respawnT -= dt; if (respawnT <= 0) { RestoreBody(); pos = loc.SpawnW; player.ResetSim(); } }
            // grass: the hero and a point trailing behind him push the blades aside
            heroTrail = Vector3.Lerp(heroTrail, pos, Mathf.Min(1, dt * 2.2f));
            Shader.SetGlobalVector("_AsciiHeroVel", player != null ? (Vector4)player.Velocity : Vector4.zero);
            // wind: calm spells (pauses), breezes and gusts follow each other at random, each a few seconds long; the
            // strength eases from one to the next, the direction wanders a little (and swings more in a gust); the air
            // runs downwind faster the harder it blows, carrying the gust pattern over the grass and the trees
            {
                windPhaseT -= dt;
                if (windPhaseT <= 0)
                {
                    float r = Random.value;
                    if (windPhase == 2 || r < 0.3f) { windPhase = 0; windTarget = 0.05f + 0.12f * Random.value; windPhaseT = 2f + 4f * Random.value; }        // lull
                    else if (r < 0.7f) { windPhase = 1; windTarget = 0.4f + 0.3f * Random.value; windPhaseT = 3f + 5f * Random.value; }                   // breeze
                    else { windPhase = 2; windTarget = 0.9f + 0.5f * Random.value; windPhaseT = 1.5f + 2.5f * Random.value; }                            // gust
                }
                windStr += (windTarget - windStr) * (1 - Mathf.Exp(-dt / (windTarget > windStr ? 0.7f : 1.6f)));        // rises quicker than it dies away
                windA += dt * (0.03f * Mathf.Sin(tNow * 0.031f) + (windPhase == 2 ? 0.06f * Mathf.Sin(tNow * 0.9f) : 0));
                windRun += dt * (0.8f + 3.2f * windStr);
                Vector2 wd = new Vector2(Mathf.Cos(windA), Mathf.Sin(windA));
                Shader.SetGlobalVector("_AsciiWind", new Vector4(wd.x, wd.y, windStr, windRun));
                windV = new Vector3(wd.x, 0, wd.y) * (windStr * 2.4f);
            }
            if (cloak != null && !perfNoAnim) { long tC = System.Diagnostics.Stopwatch.GetTimestamp(); cloak.Update(dt, windV); accA += Ms(tC); if (clothMpb == null) clothMpb = new MaterialPropertyBlock(); cloak.Apply(clothMpb, cloakRs); }
            // grass is pressed down by the body itself - the feet, the pelvis over it, a weapon swept low - not by a collision circle
            {
                var hb = hero.human; Transform fL = hb.Bone("foot.L"), fR = hb.Bone("foot.R"), rt = hb.Bone("root");
                pushPts[0] = fL != null && limbs[3] ? new Vector4(fL.position.x, fL.position.y, fL.position.z, 0.24f) : FAR;
                pushPts[1] = fR != null && limbs[4] ? new Vector4(fR.position.x, fR.position.y, fR.position.z, 0.24f) : FAR;
                pushPts[2] = rt != null ? new Vector4(rt.position.x, rt.position.y - 0.55f, rt.position.z, 0.3f) : FAR;
                Vector3 tip = hero.weapon != null ? hero.weapon.TransformPoint(0, 0.7f, 0) : Vector3.down * 99;
                pushPts[3] = tip.y < 0.5f ? new Vector4(tip.x, tip.y, tip.z, 0.16f) : FAR;
                Shader.SetGlobalVectorArray("_AsciiPush", pushPts);
            }
            labelT -= dt; if (labelT <= 0) { labelT = 0.25f; animLabel = player.label; }
            if (loc == tavern)
            {
                var bp = bartenderChar.go.transform.position;
                bartender.Locomotion(dt, null, 0, null, new Vector2(bp.x, bp.z), null); bartender.heading = 0; bartender.noLock = true;
                bartender.Animate(tNow, dt);
            }
            // camera
            isoYaw += (isoYawT - isoYaw) * Mathf.Min(1, dt * 12); isoPitch += (isoPitchT - isoPitch) * Mathf.Min(1, dt * 12);
            worldZoom += (worldZoomT - worldZoom) * Mathf.Min(1, dt * 12);
            Shader.SetGlobalFloat("_AsciiLod", 1f);      // every character at every zoom (thinning them out when zoomed out read as characters vanishing)
            Vector3 off = Mirror(new Vector3(Mathf.Sin(isoYaw) * Mathf.Cos(isoPitch), Mathf.Sin(isoPitch), Mathf.Cos(isoYaw) * Mathf.Cos(isoPitch)) * (ISO_DIST / worldZoom));
            { var d = new Vector3(off.x, 0, off.z).normalized; heroLight.transform.position = new Vector3(pos.x + d.x * 1.3f - d.z * 0.5f, 2.3f, pos.z + d.z * 1.3f + d.x * 0.5f); }
            loc.focus = pos - loc.origin; loc.Cutaway(off);
            camTgt = Vector3.Lerp(camTgt, new Vector3(pos.x, 0.6f, pos.z), Mathf.Min(1, dt * 5));
            sceneCam.transform.position = camTgt + off; sceneCam.transform.LookAt(camTgt);
            sceneCam.fieldOfView = 30 * (1 - 0.014f * heart);
        }

        /// a direction on the screen (x right, y down) as a direction on the ground (web direction, mirrored on X)
        Vector2 ScreenToWorldDir(Vector2 v)
        {
            float l = v.magnitude; if (l < 1e-5f) return Vector2.zero;
            float ux = v.x / l, uy = v.y / l, cs = Mathf.Cos(isoYaw), sn = Mathf.Sin(isoYaw);
            return new Vector2(-(ux * cs + uy * sn), -ux * sn + uy * cs);
        }
        /// the nearest creature (training dummies, the bartender) within r metres, or null
        Vector2? NearestEntity(float r)
        {
            Vector2? best = null; float bd = r;
            if (loc == field)
                foreach (var d in dummies)
                {
                    if (d.root == null || d.dead > 0) continue;
                    float dd = new Vector2(d.p.x - pos.x, d.p.z - pos.z).magnitude; if (dd < bd) { bd = dd; best = new Vector2(d.p.x, d.p.z); }
                }
            if (loc == tavern && bartenderChar != null)
            {
                var bp = bartenderChar.go.transform.position; float dd = new Vector2(bp.x - pos.x, bp.z - pos.z).magnitude;
                if (dd < bd) { bd = dd; best = new Vector2(bp.x, bp.z); }
            }
            return best;
        }
        /// is this point of the screen (GUI coordinates) on the hero?
        bool OnHero(Vector2 g)
        {
            if (!world || hero == null || sceneCam == null) return false;
            float W = Screen.width, H = Screen.height;
            Vector3 a = sceneCam.WorldToViewportPoint(pos), b = sceneCam.WorldToViewportPoint(pos + Vector3.up * (cfg.height / 100f));
            if (a.z <= 0 || b.z <= 0) return false;
            Vector2 fa = new Vector2(a.x * W, (1 - a.y) * H), fb = new Vector2(b.x * W, (1 - b.y) * H);
            float hgt = Mathf.Abs(fa.y - fb.y), half = Mathf.Max(hgt * 0.32f, 0.45f * StickCm()), m = 0.3f * StickCm();
            return g.x > fa.x - half && g.x < fa.x + half && g.y > Mathf.Min(fa.y, fb.y) - m && g.y < Mathf.Max(fa.y, fb.y) + m;
        }
        void MakeProjection()
        {
            var sb = new System.Text.StringBuilder(); sb.Append(cfg.sex).Append(cfg.build).Append(cfg.height).Append(cfg.armor).Append(cfg.weapon);
            if (cfg.body != null) foreach (var x in cfg.body) sb.Append(',').Append(x.ToString("0.###"));
            string sig = sb.ToString();
            if (studio == null)
            {
                studio = new GameObject("Studio"); studio.transform.position = STUDIO;
                var lg = new GameObject("StudioKey"); lg.transform.SetParent(studio.transform, false); lg.transform.localPosition = new Vector3(0.8f, 2.4f, 2.2f);
                var key = lg.AddComponent<Light>(); key.type = LightType.Point; key.color = Mats.Hex(0xffe6c8); key.intensity = 2.2f; key.range = 7f; key.renderMode = LightRenderMode.ForcePixel;
                var lg2 = new GameObject("StudioRim"); lg2.transform.SetParent(studio.transform, false); lg2.transform.localPosition = new Vector3(-1.2f, 2.0f, -1.6f);
                var rimL = lg2.AddComponent<Light>(); rimL.type = LightType.Point; rimL.color = Mats.Hex(0x8aa6ff); rimL.intensity = 1.4f; rimL.range = 6f;
                projCam = new GameObject("ProjectionCamera").AddComponent<Camera>(); projCam.transform.SetParent(studio.transform, false);
                projCam.nearClipPlane = 0.05f; projCam.farClipPlane = 20; projCam.fieldOfView = 30; projCam.allowMSAA = false; projCam.allowHDR = false;
                projAscii = new AsciiRenderer(projCam); projAscii.surfaceGlyphs = true; projAscii.actorBoost = 1;
            }
            studio.SetActive(true);
            if (projHero == null || projSig != sig)
            {
                if (projHero != null) Destroy(projHero.go);
                var hc = cfg.Clone(); hc.shadow = true;
                projHero = Character.Build(hc, actorMats, "Projection");
                projHero.glyphs = GlyphLayer.Build(projHero.go, Character.LimbOfBone());
                projHero.go.transform.SetParent(studio.transform, false);
                projActor = new MotionActor(projHero); projSig = sig;
            }
            projHero.go.transform.localPosition = Vector3.zero; projHero.go.transform.localRotation = Quaternion.identity;
            projActor.SetLimbs(limbs);
            // it stands at ease (the first seconds of the idle loop), then stays like that: a model on a stand
            projActor.ResetSim(); projActor.heading = 0;
            for (int i = 0; i < 45; i++) { projActor.Locomotion(1 / 30f, null, 0, null, new Vector2(STUDIO.x, STUDIO.z), null); projActor.Animate(i / 30f, 1 / 30f); }
            float hm = cfg.height / 100f; projDist = hm / (0.8f * 2 * Mathf.Tan(15f * Mathf.Deg2Rad));
            projCam.transform.localPosition = new Vector3(0, hm * 0.56f, projDist);
            projCam.transform.LookAt(STUDIO + Vector3.up * hm * 0.5f);
        }
        // while the pocket is open the hero in the world is not drawn (nothing of him shows round the smoke)
        readonly List<Renderer> heroRs = new List<Renderer>(); bool heroHidden; Character heroRsFor;
        void HideHeroBehindPocket(bool hide)
        {
            if (hero == null || hide == heroHidden) return;
            if (heroRsFor != hero) { heroRsFor = hero; heroRs.Clear(); }
            if (hide) { heroRs.Clear(); foreach (var r in hero.go.GetComponentsInChildren<Renderer>(false)) if (r.enabled) { r.enabled = false; heroRs.Add(r); } }
            else { foreach (var r in heroRs) if (r != null) r.enabled = true; heroRs.Clear(); }
            heroHidden = hide;
        }
        void ClosePocket() { if (invOpen && pocketClose < 0) pocketClose = 0; }
        // the projection in the pocket is the hero himself, live: every bone of the copy takes the pose of the hero's,
        // every part shown or hidden as his (so it stands, walks, fights and is missing what he is missing)
        Transform[] mirSrc, mirDst; Character mirFor; int mirLimbs = -1;
        void MirrorPose()
        {
            if (projHero == null || hero == null) return;
            int ls0 = 0; for (int l = 0; l < 5; l++) if (limbs[l]) ls0 |= 1 << l;
            if (ls0 != mirLimbs && projActor != null) { mirLimbs = ls0; projActor.SetLimbs(limbs); }
            if (mirFor != projHero || mirSrc == null)
            {
                mirFor = projHero; var map = new Dictionary<string, Transform>();
                string Path(Transform t, Transform root) { var sb = new System.Text.StringBuilder(); for (var x = t; x != null && x != root; x = x.parent) sb.Insert(0, "/" + x.name); return sb.ToString(); }
                foreach (var t in projHero.go.GetComponentsInChildren<Transform>(true)) if (t != projHero.go.transform) map[Path(t, projHero.go.transform)] = t;
                var ls = new List<Transform>(); var ld = new List<Transform>();
                foreach (var t in hero.go.GetComponentsInChildren<Transform>(true))
                {
                    if (t == hero.go.transform) continue; Transform d;
                    if (map.TryGetValue(Path(t, hero.go.transform), out d)) { ls.Add(t); ld.Add(d); }
                }
                mirSrc = ls.ToArray(); mirDst = ld.ToArray();
            }
            for (int i = 0; i < mirSrc.Length; i++)
            {
                var a = mirSrc[i]; var b = mirDst[i]; if (a == null || b == null) continue;
                b.localPosition = a.localPosition; b.localRotation = a.localRotation; b.localScale = a.localScale;
                if (b.gameObject.activeSelf != a.gameObject.activeSelf) b.gameObject.SetActive(a.gameObject.activeSelf);
            }
        }
        void OpenBackpack()
        {
            if (!world || perfRun) return;
            if (invOpen && pocketClose >= 0) { pocketClose = -1; return; }      // (tapped again while it was closing: it stays)
            invOpen = true; menuFromWorld = false; invT = 0; pocketT = 0; pocketClose = -1;
            try { MakeProjection(); } catch (System.Exception e) { Debug.LogException(e); }
            stickId = int.MinValue; stick = Vector2.zero; stickRun = 0; stickRunArmed = false;
            foreach (var q in InputHub.active.Values) q.owner = "ui";
        }

        void ActionButton(Rect r, string text, bool on)
        {
            var old = GUI.color;
            GUI.color = new Color(0.93f, 0.74f, 0.33f, on ? 0.45f : 0.16f); GUI.DrawTexture(r, texDisc);
            GUI.color = new Color(0.93f, 0.74f, 0.33f, 0.85f); GUI.DrawTexture(r, texRing);
            GUI.color = old; GUI.Label(r, text, sBtn);
        }

        Rect ViewRect()
        {
            float W = Screen.width, H = Screen.height;
            if (world) return new Rect(0, 0, W, H);
            float pw = Mathf.Min(W * 0.48f, 330 * k + (W - safe.xMax));
            return new Rect(0, 0, W - pw, H);
        }

        void LateUpdate()
        {
            if (!ready || ascii == null) return;
            k = Mathf.Max(0.5f, Mathf.Min(Screen.height / 390f, Screen.width / 844f));
            safe = Screen.safeArea; safe.y = Screen.height - safe.yMax;   // to GUI coordinates
            var vr = ViewRect();
            float dp = Screen.dpi > 0 ? Mathf.Clamp(Screen.dpi / 160f, 1f, 4f) : Mathf.Max(1f, k);
            float wcss = vr.width / dp;
            float cell = Mathf.Max(4f / dp, world ? Mathf.Min(1.7f, wcss / 540f) : Mathf.Min(2.0f, wcss / 310f));
            float viewDist = world ? ISO_DIST / worldZoom : cam.dist;
            ascii.depthRef = viewDist;
            // characters: how many of their glyphs are drawn follows their size on the screen (pixels per glyph cell)
            {
                float pxCell = vr.height / (2 * viewDist * Mathf.Tan(sceneCam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 0.0135f;
                int lv = GlyphLayer.actorLod;
                if (perfFullActors || (world && invOpen)) lv = 0;
                else if (lv == 0) { if (pxCell < 3.3f) lv = pxCell < 1.7f ? 2 : 1; }
                else if (lv == 1) { if (pxCell > 3.9f) lv = 0; else if (pxCell < 1.6f) lv = 2; }
                else if (pxCell > 1.9f) lv = pxCell > 3.9f ? 0 : 1;
                GlyphLayer.SetActorLod(lv);
            }
            ascii.SelfCheck();
            {   // the mirror only with water in sight (it draws the scene a second time)
                var wl = loc != null && outdoor.Contains(loc) ? waterSeen : loc;
                ascii.waterOn = world && wl != null && wl.HasWater; if (wl != null) ascii.waterY = wl.WaterY;
            }
            ascii.SetSize(Mathf.RoundToInt(vr.width), Mathf.RoundToInt(vr.height), cell * dp);
            // the backpack's projection of the hero: its own small ASCII render of the figure in the studio
            if (projAscii != null)
            {
                bool on = world && invOpen && projHero != null;
                projAscii.SetEnabled(on);
                if (!on && studio != null && studio.activeSelf) studio.SetActive(false);
                if (on)
                {
                    Rect lp = PocketProjRect(Screen.width, Screen.height);
                    int pw = Mathf.Max(16, Mathf.RoundToInt(lp.width)), ph = Mathf.Max(16, Mathf.RoundToInt(lp.height));
                    projCam.aspect = (float)pw / ph;
                    projAscii.exposure = ascii.exposure; projAscii.bloom = ascii.bloom; projAscii.outline = ascii.outline; projAscii.litDiv = ascii.litDiv;
                    projAscii.depthRef = projDist;
                    projAscii.SetSize(pw, ph, cell * dp);
                    projAscii.SelfCheck();
                }
            }
            if (!world)
            {
                float cp = Mathf.Cos(cam.pitch), spch = Mathf.Sin(cam.pitch);
                var cpos = new Vector3(Mathf.Sin(cam.yaw) * cp * cam.dist, cam.ty + spch * cam.dist, Mathf.Cos(cam.yaw) * cp * cam.dist);
                sceneCam.transform.position = Mirror(cpos); sceneCam.transform.LookAt(new Vector3(0, cam.ty, 0));
                float yaw = cam.yaw;
                System.Func<float, float, float, Vector3> rot = (x, y, z) => Mirror(new Vector3(x * Mathf.Cos(yaw) + z * Mathf.Sin(yaw), y, -x * Mathf.Sin(yaw) + z * Mathf.Cos(yaw)));
                key.transform.rotation = Quaternion.LookRotation(new Vector3(0, 0.9f, 0) - rot(2.2f, 4.0f, 2.6f));
                rim.transform.rotation = Quaternion.LookRotation(-rot(-2.5f, 2.2f, -3.0f));
                fill.transform.rotation = Quaternion.LookRotation(-rot(-2.5f, 0.8f, 2.0f));
            }
            if (player != null && player.driver != null) player.Animate(tNow, Mathf.Min(0.1f, Time.deltaTime));
            if (world && invOpen) MirrorPose();
            HideHeroBehindPocket(world && invOpen && pocketT > 0.3f && pocketClose < 0.3f);
        }

        void ToggleGlyphMode()
        {
            ascii.surfaceGlyphs = !ascii.surfaceGlyphs;
            PlayerPrefs.SetInt("ascii_glyph_mode", ascii.surfaceGlyphs ? 1 : 0); PlayerPrefs.Save();
            ShowToast(ascii.surfaceGlyphs ? "Символы на моделях" : "Символы сеткой экрана (beta 0.8)");
        }

        void ToggleAnimMode()
        {
            int next = (animMode + 1) % 3;
            if (next == 2)
            {
                locoCtrl = Resources.Load<RuntimeAnimatorController>("Locomotion");
                if (locoCtrl == null) next = 0;
            }
            animMode = next; useAnimator = animMode == 2;
            PlayerPrefs.SetInt("ascii_anim_mode2", animMode); PlayerPrefs.Save();
            float h = player != null ? player.heading : 0;
            Rebuild();
            if (world) { player.heading = h; player.ResetSim(); player.noLock = false; }
            ShowToast(animMode == 0 ? "Анимация: плавная\n(циклы шага и бега, смешанные по скорости)" : animMode == 1 ? "Анимация: Motion Matching" : "Анимация: Animator Controller");
        }

        // ---------- UI ----------
        static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
        static Texture2D Circle(int S, bool ring)
        {
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false); var px = new Color[S * S]; float r = S / 2f;
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                float a = ring ? Mathf.Clamp01(1 - Mathf.Abs(d - (r - 3)) / 1.6f) + Mathf.Clamp01((r - 3 - d) / r) * 0.18f : Mathf.Clamp01(r - d);
                px[y * S + x] = new Color(1, 1, 1, a);
            }
            t.SetPixels(px); t.Apply(); return t;
        }
        void MakeUiAssets()
        {
            texWhite = Solid(Color.white); texDisc = Circle(128, false); texRing = Circle(256, true);
            mono = Font.CreateDynamicFontFromOSFont(new[] { "Droid Sans Mono", "Roboto Mono", "Noto Sans Mono", "DejaVu Sans Mono", "Consolas", "Courier New", "monospace" }, 14);
        }
        void Styles()
        {
            var gold = new Color(0.93f, 0.74f, 0.33f); var ink = new Color(0.86f, 0.84f, 0.78f);
            int fs(float s) { return Mathf.Max(8, Mathf.RoundToInt(s * k)); }
            sTitle = new GUIStyle(GUI.skin.label) { fontSize = fs(15), fontStyle = FontStyle.Bold, richText = true }; sTitle.normal.textColor = gold;
            sSmall = new GUIStyle(GUI.skin.label) { fontSize = fs(10), richText = true, wordWrap = true }; sSmall.normal.textColor = new Color(0.6f, 0.6f, 0.65f);
            sBadge = new GUIStyle(GUI.skin.label) { fontSize = fs(11), richText = true, wordWrap = false }; sBadge.normal.textColor = ink;
            sBtn = new GUIStyle(GUI.skin.label) { fontSize = fs(12), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true }; sBtn.normal.textColor = ink;
            sBtnOn = new GUIStyle(sBtn); sBtnOn.normal.textColor = new Color(0.1f, 0.08f, 0.04f);
            sMono = new GUIStyle(GUI.skin.label) { fontSize = fs(8.5f), alignment = TextAnchor.MiddleCenter, wordWrap = false }; sMono.normal.textColor = gold; if (mono != null) sMono.font = mono;
            sName = new GUIStyle(GUI.skin.label) { fontSize = fs(12), fontStyle = FontStyle.Bold }; sName.normal.textColor = ink;
            sDesc = new GUIStyle(GUI.skin.label) { fontSize = fs(9.5f), wordWrap = true }; sDesc.normal.textColor = new Color(0.62f, 0.6f, 0.56f);
            sToast = new GUIStyle(GUI.skin.label) { fontSize = fs(12), alignment = TextAnchor.MiddleCenter, wordWrap = true }; sToast.normal.textColor = Color.white;
            sBig = new GUIStyle(GUI.skin.label) { fontSize = fs(34), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; sBig.normal.textColor = gold;
            sRight = new GUIStyle(sSmall) { alignment = TextAnchor.LowerRight, wordWrap = false };
            sRightSmall = new GUIStyle(sSmall) { alignment = TextAnchor.UpperRight, wordWrap = false }; sRightSmall.normal.textColor = new Color(0.93f, 0.74f, 0.33f);
        }
        void Fill(Rect r, Color c) { var old = GUI.color; c.a *= uiAlpha; GUI.color = c; GUI.DrawTexture(r, texWhite); GUI.color = old; }
        void Frame(Rect r, Color c, float w = 1)
        {
            Fill(new Rect(r.x, r.y, r.width, w), c); Fill(new Rect(r.x, r.yMax - w, r.width, w), c);
            Fill(new Rect(r.x, r.y, w, r.height), c); Fill(new Rect(r.xMax - w, r.y, w, r.height), c);
        }
        bool Tapped(Rect r)
        {
            for (int i = 0; i < taps.Count; i++) if (r.Contains(taps[i])) { taps.RemoveAt(i); return true; }
            return false;
        }
        bool Button(Rect r, string text, bool on = false, GUIStyle st = null)
        {
            uiRects.Add(r);
            var gold = new Color(0.93f, 0.74f, 0.33f);
            Fill(r, on ? gold : new Color(0.12f, 0.12f, 0.15f, 0.92f));
            Frame(r, on ? gold : new Color(0.32f, 0.3f, 0.26f, 1), Mathf.Max(1, k));
            GUI.Label(r, text, st ?? (on ? sBtnOn : sBtn));
            return Tapped(r);
        }
        bool Option(Rect r, string id, string name, string desc, bool on)
        {
            uiRects.Add(r);
            var gold = new Color(0.93f, 0.74f, 0.33f);
            Fill(r, on ? new Color(0.25f, 0.2f, 0.1f, 0.95f) : new Color(0.1f, 0.1f, 0.13f, 0.92f));
            Frame(r, on ? gold : new Color(0.25f, 0.24f, 0.22f), Mathf.Max(1, k));
            float iw = 46 * k;
            string ico; ICON.TryGetValue(id, out ico);
            GUI.Label(new Rect(r.x + 2 * k, r.y, iw, r.height), ico ?? "", sMono);
            GUI.Label(new Rect(r.x + iw + 6 * k, r.y + 3 * k, r.width - iw - 10 * k, 18 * k), name, sName);
            GUI.Label(new Rect(r.x + iw + 6 * k, r.y + 19 * k, r.width - iw - 10 * k, r.height - 20 * k), desc, sDesc);
            return Tapped(r);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            long tG = System.Diagnostics.Stopwatch.GetTimestamp();
            LogHub.Pump();
            k = Mathf.Max(0.5f, Mathf.Min(Screen.height / 390f, Screen.width / 844f));
            safe = Screen.safeArea; safe.y = Screen.height - safe.yMax;
            if (texWhite == null) MakeUiAssets();
            if (sTitle == null || Mathf.Abs(sTitle.fontSize - Mathf.Max(8, Mathf.RoundToInt(15 * k))) > 0) Styles();
            uiRects.Clear();
            float W = Screen.width, H = Screen.height;
            if (LogHub.errors > seenErrors) { seenErrors = LogHub.errors; consoleOpen = true; }
            if (!ready || ascii == null) { LoadingScreen(W, H); ConsoleUI(W, H, true); FinishGui(); return; }
            var vr = ViewRect();
            if (!ascii.plain)
            {
                RenderTexture.active = null;   // the interface always goes to the screen
                if (ascii.final != null) GUI.DrawTexture(vr, ascii.final, ScaleMode.StretchToFill, false);
                else GUI.Label(new Rect(vr.x + 20 * k, vr.center.y, vr.width - 40 * k, 30 * k), "Кадр ASCII ещё не готов", sSmall);
            }
            float L = safe.x + 12 * k, T = safe.y + 8 * k;
            if (perfRun)
            {   // the test: only the picture and a line of progress
                if (!perfNoGui) GUI.Label(new Rect(L, T, 600 * k, 22 * k), (perfPhase == 0 ? "Тест нагрузки: прогрев " + (perfLocIx + 1) + " из " + perfLocs.Count : perfPhase == 1 ? "Тест нагрузки: биом " + (perfLocIx + 1) + " из " + perfLocs.Count + " · " + LocTitle(loc) : "Тест нагрузки: " + LocTitle(loc) + " · " + (perfIx + 1) + " из " + PERF.Length + " · " + PERF[perfIx]), sTitle);
                FinishGui(); accG += Ms(tG); return;
            }
            if (world) DrawDanger(W, H);
            if (inMenu && !world) { MenuUI(W, H); ConsoleUI(W, H, false); FinishGui(); return; }
            if (world && menuFromWorld) { MenuUI(W, H); ConsoleUI(W, H, false); FinishGui(); accG += Ms(tG); return; }
            if (!world) EditorUI(W, H, vr, L, T); else WorldUI(W, H, L, T);
            if (toastT > 0 && !string.IsNullOrEmpty(toast) && (!world || invOpen))
            {
                float a = Mathf.Clamp01(toastT / 0.4f); var r = new Rect(vr.center.x - 150 * k, H * 0.18f, 300 * k, 46 * k);
                Fill(r, new Color(0, 0, 0, 0.7f * a)); var old = GUI.color; GUI.color = new Color(1, 1, 1, a); GUI.Label(r, toast, sToast); GUI.color = old;
            }
            if (setShowFps) GUI.Label(new Rect(vr.xMax - 330 * k, H - 18 * k - (H - safe.yMax), 320 * k, 16 * k), fpsText, sRight);
            ConsoleUI(W, H, false);
            FinishGui(); accG += Ms(tG);
        }
        void FinishGui() { taps.Clear(); uiRectsPrev.Clear(); uiRectsPrev.AddRange(uiRects); }

        // ---------- loading screen and on-screen log ----------
        void LoadingScreen(float W, float H)
        {
            Fill(new Rect(0, 0, W, H), new Color(0.02f, 0.02f, 0.03f, 1));
            float x = safe.x + 24 * k, y = safe.y + 24 * k, w = Mathf.Min(520 * k, W - 48 * k);
            GUI.Label(new Rect(x, y, w, 26 * k), "ASCII HERO", sTitle);
            GUI.Label(new Rect(x, y + 26 * k, w, 18 * k), Game.VERSION, sSmall);
            string st = failed ? "Ошибка на шаге: " + stage : "Загрузка: " + stage + "…";
            var stStyle = new GUIStyle(sBadge); stStyle.normal.textColor = failed ? new Color(1f, 0.45f, 0.4f) : new Color(0.86f, 0.84f, 0.78f);
            GUI.Label(new Rect(x, y + 56 * k, w, 22 * k), st, stStyle);
            var bar = new Rect(x, y + 84 * k, w, 6 * k);
            Fill(bar, new Color(0.2f, 0.19f, 0.17f));
            Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(stageIndex / (float)stageCount), bar.height), failed ? new Color(0.9f, 0.3f, 0.25f) : new Color(0.93f, 0.74f, 0.33f));
            GUI.Label(new Rect(x, y + 96 * k, w, 18 * k), (Time.realtimeSinceStartup - loadT0).ToString("0.0") + " с · шаг " + stageIndex + " из " + stageCount, sSmall);
            if (!consoleOpen) consoleOpen = failed;
        }

        void ConsoleUI(float W, float H, bool loading)
        {
            float top = safe.y + (loading ? 140 : 40) * k;
            if (!consoleOpen)
            {
                string tag = LogHub.errors > 0 ? "LOG · " + LogHub.errors + " ош." : "LOG";
                if (Button(new Rect(safe.x + 8 * k, H - (H - safe.yMax) - 30 * k, 96 * k, 24 * k), tag, LogHub.errors > 0)) consoleOpen = true;
                return;
            }
            var panel = new Rect(safe.x + 8 * k, top, safe.width - 16 * k, H - (H - safe.yMax) - top - 8 * k);
            Fill(panel, new Color(0, 0, 0, 0.86f)); Frame(panel, new Color(0.4f, 0.36f, 0.26f), Mathf.Max(1, k));
            uiRects.Add(panel);
            float bx = panel.xMax - 8 * k, by = panel.y + 6 * k, bh = 26 * k;
            if (Button(new Rect(bx - 90 * k, by, 90 * k, bh), "Скрыть")) consoleOpen = false;
            if (Button(new Rect(bx - 200 * k, by, 104 * k, bh), "Копировать")) { GUIUtility.systemCopyBuffer = LogHub.AllText(); ShowToast("Лог скопирован"); }
            if (ready && ascii != null && Button(new Rect(bx - 330 * k, by, 124 * k, bh), ascii.plain ? "Вернуть ASCII" : "Без ASCII", ascii.plain)) ascii.plain = !ascii.plain;
            if (ready && world && Button(new Rect(bx - 440 * k, by, 104 * k, bh), "Урон −10%")) Damage(10);
            if (ready && Button(new Rect(bx - 540 * k, by, 94 * k, bh), "Починить")) RestoreBody();
            GUI.Label(new Rect(panel.x + 8 * k, by + 2 * k, panel.width - 560 * k, bh), "Лог · ошибок: " + LogHub.errors + " · " + LogHub.FilePath, sSmall);
            var st = new GUIStyle(sSmall) { wordWrap = true, richText = false, fontSize = Mathf.Max(8, Mathf.RoundToInt(9.5f * k)) };
            if (mono != null) st.font = mono;
            float lineH = st.lineHeight + 1, y = panel.yMax - 6 * k, x = panel.x + 8 * k, w = panel.width - 16 * k;
            for (int i = LogHub.lines.Count - 1; i >= 0 && y > by + bh + 6 * k; i--)
            {
                var l = LogHub.lines[i];
                string text = (l.count > 1 ? "(×" + l.count + ") " : "") + (l.text.Length > 900 ? l.text.Substring(0, 900) + "…" : l.text);
                float h = st.CalcHeight(new GUIContent(text), w);
                y -= h + 2 * k; if (y < by + bh + 6 * k) break;
                bool err = l.type == LogType.Error || l.type == LogType.Exception || l.type == LogType.Assert;
                st.normal.textColor = err ? new Color(1f, 0.5f, 0.45f) : l.type == LogType.Warning ? new Color(0.95f, 0.8f, 0.4f) : new Color(0.8f, 0.8f, 0.78f);
                GUI.Label(new Rect(x, y, w, h), text, st);
            }
        }

        void EditorUI(float W, float H, Rect vr, float L, float T)
        {
            GUI.Label(new Rect(L, T, 320 * k, 22 * k), "СОЗДАНИЕ ГЕРОЯ", sTitle);
            GUI.Label(new Rect(L, T + 20 * k, 320 * k, 14 * k), VERSION + " · свайп — вращать, щипок — зум", sSmall);
            int si = cfg.sex == 'f' ? 1 : 0;
            string an = "", wn = "";
            foreach (var a in Character.ARMORS) if (a[0] == cfg.armor) an = a[1];
            foreach (var w in Character.WEAPONS) if (w[0] == cfg.weapon) wn = w[1];
            GUI.Label(new Rect(L, H - (H - safe.yMax) - 52 * k, 320 * k, 50 * k), "<b>" + SEXNAME[si] + "</b> · " + cfg.height + " см · " + (cfg.body != null ? Mathf.Round(cfg.body[1]) + " кг" : Character.BUILDS[cfg.build]) + "\n" + an + "\n" + wn, sBadge);
            // view buttons
            if (Button(new Rect(vr.xMax - 196 * k, T, 62 * k, 24 * k), "МЕНЮ")) { inMenu = true; menuPage = 0; }
            if (Button(new Rect(vr.xMax - 128 * k, T, 58 * k, 24 * k), "АВТО", autoRotate)) autoRotate = !autoRotate;
            if (Button(new Rect(vr.xMax - 66 * k, T, 58 * k, 24 * k), "СБРОС")) cam = CAM0;
            if (Button(new Rect(vr.xMax - 128 * k, T + 28 * k, 120 * k, 24 * k), ascii.surfaceGlyphs ? "Aa: НА МОДЕЛЯХ" : "Aa: СЕТКОЙ", ascii.surfaceGlyphs)) ToggleGlyphMode();

            // panel
            float px = vr.xMax, pw = W - px, right = safe.xMax;
            var panel = new Rect(px, 0, pw, H); Fill(panel, new Color(0.055f, 0.055f, 0.07f, 0.97f)); Fill(new Rect(px, 0, Mathf.Max(1, k), H), new Color(0.3f, 0.26f, 0.18f));
            uiRects.Add(panel);
            float x0 = px + 10 * k, cw = right - x0 - 10 * k, y = safe.y + 8 * k;
            string[] tabs = { "ТЕЛО", "БРОНЯ", "ОРУЖИЕ" };
            float tw = (cw - 8 * k) / 3;
            for (int i = 0; i < 3; i++) if (Button(new Rect(x0 + i * (tw + 4 * k), y, tw, 28 * k), tabs[i], tab == i)) tab = i;
            y += 36 * k;
            float bottom = H - (H - safe.yMax) - 8 * k - 34 * k;
            sliderRect = Rect.zero;
            bodyList = Rect.zero; bodyTracks.Clear(); bodyTrackIx.Clear(); bodyFields.Clear();
            if (tab == 0)
            {
                float hw = (cw - 6 * k) / 2;
                if (Button(new Rect(x0, y, hw, 28 * k), SEXNAME[0], si == 0)) SetSex('m');
                if (Button(new Rect(x0 + hw + 6 * k, y, hw, 28 * k), SEXNAME[1], si == 1)) SetSex('f');
                y += 34 * k;
                // presets for the sex: a drop-down list
                string pn = presetIx >= 0 ? Body.PRESETS[presetIx] : "свой";
                if (Button(new Rect(x0, y, cw, 26 * k), "ТЕЛОСЛОЖЕНИЕ: " + pn.ToUpper() + (presetOpen ? "  ▴" : "  ▾"), presetOpen)) presetOpen = !presetOpen;
                y += 30 * k;
                if (presetOpen)
                {
                    for (int pi = 0; pi < Body.PRESETS.Length; pi++)
                        if (Button(new Rect(x0 + 8 * k, y + pi * 30 * k, cw - 16 * k, 26 * k), Body.PRESETS[pi], presetIx == pi))
                        {
                            cfg.body = Body.Preset(pi, cfg.sex, cfg.height); presetIx = pi; presetOpen = false; bodyDirty = true;
                        }
                    y += Body.PRESETS.Length * 30 * k + 4 * k;
                }
                if (cfg.body == null) ResetBody();
                // the list of sliders, scrolled by dragging
                bodyList = new Rect(x0, y, cw, bottom - 6 * k - y); uiRects.Add(bodyList);
                float rowH = 40 * k, headH = 22 * k, yy = 0; int grp = -1;
                GUI.BeginGroup(bodyList);
                for (int ix = 0; ix < Body.N; ix++)
                {
                    var bp = Body.ALL[ix];
                    if (bp.group != grp) { grp = bp.group; float hy = yy - bodyScroll; if (hy > -headH && hy < bodyList.height) GUI.Label(new Rect(0, hy + 4 * k, cw, 16 * k), Body.GROUPS[grp], sTitle); yy += headH; }
                    float ry = yy - bodyScroll; yy += rowH;
                    if (ry < -rowH || ry > bodyList.height) continue;
                    float shown; string txt = BodyText(ix, out shown);
                    GUI.Label(new Rect(0, ry, cw * 0.62f, 16 * k), bp.label, sSmall);
                    if (setDev)
                    {   // developer: tap the number to type any value, up to fifty times past the slider's range
                        float raw = ix == 0 ? cfg.height : cfg.body[ix];
                        var tf = new Rect(cw * 0.72f, ry - 1 * k, cw * 0.28f, 19 * k);
                        Fill(tf, new Color(0.12f, 0.12f, 0.15f, 0.95f)); Frame(tf, new Color(0.93f, 0.74f, 0.33f, 0.8f), Mathf.Max(1, k));
                        GUI.Label(new Rect(tf.x, tf.y + 2 * k, tf.width - 4 * k, tf.height), raw.ToString("0.###"), sRightSmall);
                        if (bp.factor) GUI.Label(new Rect(cw * 0.4f, ry, cw * 0.3f, 16 * k), txt, sRightSmall);
                        var absF = new Rect(bodyList.x + tf.x, bodyList.y + tf.y, tf.width, tf.height);
                        if (absF.yMax > bodyList.y && absF.y < bodyList.yMax) { bodyFields.Add(absF); if (Tapped(absF)) OpenKeyboard(ix); }
                    }
                    else GUI.Label(new Rect(cw * 0.62f, ry, cw * 0.38f, 16 * k), txt, sRightSmall);
                    // slider
                    float v = ix == 0 ? cfg.height : cfg.body[ix], u = Mathf.Clamp01((v - bp.min) / (bp.max - bp.min));
                    var track = new Rect(8 * k, ry + 25 * k, cw - 16 * k, 4 * k);
                    Fill(track, new Color(0.25f, 0.24f, 0.22f)); Fill(new Rect(track.x, track.y, track.width * u, track.height), new Color(0.93f, 0.74f, 0.33f));
                    float d0 = Body.Def(ix, cfg.sex); if (ix == 1) d0 = Body.DefaultWeight(cfg.sex, cfg.height); if (ix == 0) d0 = cfg.sex == 'f' ? 167 : 180;
                    Fill(new Rect(track.x + track.width * Mathf.Clamp01((d0 - bp.min) / (bp.max - bp.min)) - k, track.y - 3 * k, 2 * k, 10 * k), new Color(0.6f, 0.58f, 0.5f));   // the default
                    bool outside = v < bp.min || v > bp.max;
                    GUI.color = outside ? new Color(1f, 0.45f, 0.35f) : Color.white;
                    GUI.DrawTexture(new Rect(track.x + track.width * u - 8 * k, track.y - 7 * k, 16 * k, 16 * k), texDisc); GUI.color = Color.white;
                    var abs = new Rect(bodyList.x + track.x - 8 * k, bodyList.y + ry + 14 * k, track.width + 16 * k, 24 * k);
                    if (abs.yMax > bodyList.y && abs.y < bodyList.yMax) { bodyTracks.Add(abs); bodyTrackIx.Add(ix); }
                }
                GUI.EndGroup();
                bodyScrollMax = Mathf.Max(0, yy - bodyList.height); bodyScroll = Mathf.Clamp(bodyScroll, 0, bodyScrollMax);
                if (bodyScrollMax > 0)
                {   // scroll bar
                    float bh = bodyList.height * bodyList.height / (bodyList.height + bodyScrollMax);
                    Fill(new Rect(bodyList.xMax + 3 * k, bodyList.y + (bodyList.height - bh) * bodyScroll / bodyScrollMax, 3 * k, bh), new Color(0.93f, 0.74f, 0.33f, 0.6f));
                }
            }
            else
            {
                var list = tab == 1 ? Character.ARMORS : Character.WEAPONS;
                float oh = Mathf.Min(48 * k, (bottom - 6 * k - y) / list.Length - 4 * k);
                for (int i = 0; i < list.Length; i++)
                {
                    var r = new Rect(x0, y + i * (oh + 4 * k), cw, oh);
                    bool on = tab == 1 ? cfg.armor == list[i][0] : cfg.weapon == list[i][0];
                    if (Option(r, list[i][0], list[i][1], list[i][2], on)) { if (tab == 1) SetArmor(list[i][0]); else SetWeapon(list[i][0]); }
                }
            }
            float bw2 = (cw - 6 * k) / 2;
            float bw3 = (cw - 24 * k) / 5;
            if (Button(new Rect(x0, bottom, bw3, 32 * k), "СЛУЧ.")) Randomize();
            if (Button(new Rect(x0 + bw3 + 6 * k, bottom, bw3, 32 * k), "БАР ▶", true)) EnterWorld(0);
            if (Button(new Rect(x0 + (bw3 + 6 * k) * 2, bottom, bw3, 32 * k), "ПОЛЕ ▶", true)) EnterWorld(1);
            if (Button(new Rect(x0 + (bw3 + 6 * k) * 3, bottom, bw3, 32 * k), "ЛЕС ▶", true)) EnterWorld(3);
            if (Button(new Rect(x0 + (bw3 + 6 * k) * 4, bottom, bw3, 32 * k), "ПЕЩЕРА ▶", true)) EnterWorld(2);
        }

        // the world: no interface on the screen at all (the controls are invisible); a tap on the hero opens the backpack
        void WorldUI(float W, float H, float L, float T)
        {
            stickR = 62 * k;
            if (invOpen) PocketUI(W, H);
        }

        // ---------- backpack ----------
        // After the classic paper doll of RPGs: the hero himself in the left window (the camera turns to him, he stands
        // still); what is missing of him is outlined in red dots on the model; the weapon slots beside his hands. On the
        // right the backpack, drawn in characters, with its grid of cells.
        Rect BackpackLayout(float W, float H, out Rect btn, out Rect rp)
        {
            float bw = 150 * k, bx = safe.x + 14 * k, R = safe.xMax - 12 * k, T = safe.y + 46 * k;
            btn = new Rect(bx, 0, bw, H);
            float px0 = bx + bw + 16 * k, pw = R - px0, ph = safe.yMax - 10 * k - T, lw = pw * 0.5f;
            rp = new Rect(px0 + lw + 10 * k, T, pw - lw - 10 * k, ph);
            return new Rect(px0, T, lw, ph);
        }
        string ArmorName() { foreach (var x in Character.ARMORS) if (x[0] == cfg.armor) return x[1]; return "—"; }
        string WeaponName() { foreach (var x in Character.WEAPONS) if (x[0] == cfg.weapon) return x[1]; return "—"; }
        static readonly string[] SHIELD_ICON = { " /-\\ ", " |+| ", " \\_/ ", "     " };
        string[] SlotArt(string id)
        {
            string[] art = null;
            if (id == "shieldonly") art = SHIELD_ICON;
            else if (id != null) { string ic; if (ICON.TryGetValue(id, out ic)) art = ic.Split('\n'); }
            var o = new string[6]; o[0] = ".-------."; o[5] = "'-------'";
            for (int i = 0; i < 4; i++) { string l = art != null && i < art.Length ? art[i] : ""; l = (l + "     ").Substring(0, 5); o[i + 1] = "| " + l + " |"; }
            return o;
        }
        // ---------- the shadow pocket ----------
        // A tap on the hero tears a pocket of his shadow open in the middle of the screen: ASCII smoke gathers into an
        // oval a third of the screen in size (0.35 s), puffs break off its rim and drift away, and the windows come up on
        // its dark hollow (0.65 s). The game goes on around it; a tap outside the smoke closes it (the smoke blows apart).
        // Two windows: the hero's state (a live projection of him - as he stands, moves, with the limbs he has - and his
        // integrity, limbs, armour, weapons) and the backpack.
        // the two clouds: a smaller one on the left (the hero), a larger one on the right (the backpack), together about
        // 60 % of the screen (capped by its height; then a little wider than round)
        void PocketClouds(float W, float H, out Vector3 c1, out Vector3 c2)
        {
            float r2 = Mathf.Min(H * 0.47f, Mathf.Sqrt(0.6f * W * H / (Mathf.PI * (1 + 0.62f * 0.62f)))), r1 = r2 * 0.62f;
            float overlap = r1 * 0.45f, total = 2 * r1 + 2 * r2 - overlap;
            if (total > W * 0.96f) { float f = W * 0.96f / total; r1 *= f; r2 *= f; overlap *= f; total *= f; }
            float x0 = W / 2 - total / 2, cy = H / 2;
            c1 = new Vector3(x0 + r1, cy + r2 * 0.12f, r1); c2 = new Vector3(x0 + 2 * r1 - overlap + r2, cy, r2);
        }
        bool InPocket(Vector2 p)
        {
            Vector3 a, b; PocketClouds(Screen.width, Screen.height, out a, out b);
            return (p - new Vector2(a.x, a.y)).magnitude < a.z * 1.05f || (p - new Vector2(b.x, b.y)).magnitude < b.z * 1.05f;
        }
        Rect PocketProjRect(float W, float H)
        {
            Vector3 a, b; PocketClouds(W, H, out a, out b);
            float h = a.z * 1.55f, w = h * 0.75f; return new Rect(a.x - w / 2, a.y - h / 2, w, h);
        }
        Rect PocketGrid(float W, float H, out float cell)
        {
            Vector3 a, b; PocketClouds(W, H, out a, out b);
            // 5 x 5 in sight (of 5 x 10), as big as fits in the cloud with the buttons under it
            float side = b.z * 1.3f; cell = Mathf.Floor(Mathf.Min(side / 5f, (side - 30 * k) / 5f));
            float gw = cell * 5, gh = cell * 5; return new Rect(b.x - gw / 2, b.y - gh / 2 - 12 * k, gw, gh);
        }
        float pocketScroll; Material keyMat;
        readonly Vector4[] pRim = new Vector4[44], pFly = new Vector4[16];
        static float Hh(float n) { float x = Mathf.Sin(n * 127.1f) * 43758.5453f; return x - Mathf.Floor(x); }
        /// the puffs of the pocket's smoke, on the CPU: the billows that make up the rims (more on the larger cloud, by its
        /// length of edge) and the puffs that break off - slowly, not far - and shrink away
        void PocketPuffs(Vector3 c1, Vector3 c2)
        {
            float t = pocketT, cl = pocketClose < 0 ? 0 : pocketClose;
            float g = Mathf.SmoothStep(0, 1, t / 0.35f) * (1 + 0.06f * Mathf.Sin(Mathf.Clamp01(t / 0.7f) * Mathf.PI)), s = Mathf.Max(g, 0.02f) * (1 + 0.3f * cl);
            float rr = Mathf.Min(c1.z, c2.z), share1 = c1.z / (c1.z + c2.z);
            for (int k = 0; k < pRim.Length; k++)
            {
                bool left = k < Mathf.RoundToInt(pRim.Length * share1); var C = left ? c1 : c2;
                int n = left ? Mathf.RoundToInt(pRim.Length * share1) : pRim.Length - Mathf.RoundToInt(pRim.Length * share1), ix = left ? k : k - (pRim.Length - n);
                float h1 = Hh(k + 0.7f), h2 = Hh(k * 5.3f + 2.1f);
                float a = (ix + 0.35f * h1) / n * Mathf.PI * 2 + t * 0.05f * (h2 - 0.5f);
                float ph = Mathf.Repeat(t / (1.8f + 1.4f * h2) + h1, 1f), sw = Mathf.Sin(ph * Mathf.PI);
                float rad = rr * s * (0.07f + 0.07f * sw + 0.04f * h2);
                pRim[k] = new Vector4(C.x + Mathf.Cos(a) * C.z * s * (0.93f + 0.05f * sw), C.y + Mathf.Sin(a) * C.z * s * (0.93f + 0.05f * sw), rad, 0);
            }
            for (int q = 0; q < pFly.Length; q++)
            {
                float h1 = Hh(q * 2.9f + 11.3f), h2 = Hh(q * 7.1f + 4.4f), L = 1.6f + 1.2f * h2;
                float ph = Mathf.Repeat(t / L + h1, 1f); var C = h1 < share1 ? c1 : c2;
                float a = h2 * Mathf.PI * 2 + 0.2f * ph;
                float dist = C.z * s * (0.95f + (0.1f + 0.18f * h1) * ph);          // (slow and not far)
                float rad = rr * s * (0.08f + 0.07f * h2) * (1 - ph) * Mathf.Clamp01(t / 0.25f);
                pFly[q] = new Vector4(C.x + Mathf.Cos(a) * dist, C.y + Mathf.Sin(a) * dist, rad, ph);
            }
        }
        void PocketUI(float W, float H)
        {
            Vector3 c1, c2; PocketClouds(W, H, out c1, out c2);
            // the smoke
            if (pocketMat == null) { var sh = Shader.Find("Hidden/AsciiHero/Pocket"); if (sh != null) pocketMat = new Material(sh); }
            float cellW = ascii != null && ascii.cols > 0 ? W / ascii.cols : 8 * k, cellH = ascii != null && ascii.rows > 0 ? H / ascii.rows : 14 * k;
            if (pocketMat != null)
            {
                float m = c2.z * 0.9f;
                var R = Rect.MinMaxRect(Mathf.Max(0, c1.x - c1.z - m), Mathf.Max(0, c2.y - c2.z - m), Mathf.Min(W, c2.x + c2.z + m), Mathf.Min(H, c2.y + c2.z + m));
                pocketMat.SetFloat("_PT", pocketT); pocketMat.SetFloat("_PClose", pocketClose < 0 ? 0 : pocketClose);
                pocketMat.SetVector("_PRect", new Vector4(R.x, R.y, R.width, R.height));
                pocketMat.SetVector("_PC1", c1); pocketMat.SetVector("_PC2", c2);
                PocketPuffs(c1, c2); pocketMat.SetVectorArray("_PRim", pRim); pocketMat.SetVectorArray("_PFly", pFly);
                pocketMat.SetVector("_PCell", new Vector4(cellW, cellH, Mathf.Max(0.6f, 0.45f * k), 0));      // (a thin outline)
                Graphics.DrawTexture(R, texWhite, pocketMat);
            }
            // what lies on it fades in after the smoke has gathered, and goes first when it closes
            float ua = Mathf.SmoothStep(0, 1, (pocketT - 0.35f) / 0.65f);
            if (pocketClose >= 0) ua *= 1 - Mathf.SmoothStep(0, 1, pocketClose / 0.45f);
            if (ua <= 0.01f) return;
            uiAlpha = ua; var oldC = GUI.color; GUI.color = new Color(1, 1, 1, ua);
            bool live = ua > 0.6f;
            if (!live) taps.Clear();
            try
            {
                // left: the hero, live, standing on the darkness (no frame, no background)
                var lp = PocketProjRect(W, H); uiRects.Add(lp);
                if (keyMat == null) { var sh = Shader.Find("Hidden/AsciiHero/Key"); if (sh != null) keyMat = new Material(sh); }
                if (projAscii != null && projAscii.final != null && keyMat != null) { keyMat.SetFloat("_KeyAlpha", ua); Graphics.DrawTexture(lp, projAscii.final, keyMat); }
                MissingLimbDots(lp);
                // right: the backpack - 10 x 10 cells, 10 x 5 in sight, scrolled by dragging
                float cell; var gr = PocketGrid(W, H, out cell); uiRects.Add(gr);
                foreach (var pt in InputHub.active.Values)
                    if (pt.owner == "ui" && gr.Contains(pt.start) && live) pocketScroll -= pt.Delta.y / cell;
                pocketScroll = Mathf.Clamp(pocketScroll, 0, 5);      // (10 rows, 5 in sight)
                var lineC = new Color(0.4f, 0.36f, 0.52f, 0.9f); var fillC = new Color(0.03f, 0.025f, 0.05f, 0.85f);
                int fs = Mathf.Max(8, Mathf.RoundToInt(cell * 0.5f));
                if (sBag == null || sBag.fontSize != fs) { sBag = new GUIStyle(sMono) { richText = false, alignment = TextAnchor.MiddleCenter, fontSize = fs, wordWrap = false }; sBag.normal.textColor = new Color(0.91f, 0.71f, 0.26f); }
                GUI.BeginGroup(gr);
                int r0 = Mathf.FloorToInt(pocketScroll); float off = (pocketScroll - r0) * cell;
                for (int r = r0; r < Mathf.Min(10, r0 + 6); r++)
                    for (int cc = 0; cc < 5; cc++)
                    {
                        var cr = new Rect(cc * cell + 2, (r - r0) * cell - off + 2, cell - 4, cell - 4);
                        Fill(cr, fillC); Frame(cr, lineC, Mathf.Max(1, k));
                        int ii = r * 5 + cc;
                        if (ii < inventory.Count && inventory[ii].Length > 0) GUI.Label(cr, inventory[ii].Substring(0, 1), sBag);
                    }
                GUI.EndGroup();
                // the scroll: a thin bar beside the cells
                var sb = new Rect(gr.xMax + 4 * k, gr.y, 2 * k, gr.height); Fill(sb, new Color(0.25f, 0.22f, 0.32f, 0.6f));
                Fill(new Rect(sb.x, gr.y + gr.height * (pocketScroll / 10f), sb.width, gr.height * 0.5f), lineC);
                // the game's own buttons: small, under the cells
                float bw = gr.width / 4 - 4 * k, bh = 18 * k, by = gr.yMax + 6 * k;
                if (Button(new Rect(gr.x, by, bw, bh), "сохранить", false, sSmallBtn()) && live) { saveMode = 1; menuPage = 3; menuFromWorld = true; saveInfoDirty = true; }
                if (Button(new Rect(gr.x + (bw + 5 * k), by, bw, bh), "загрузить", false, sSmallBtn()) && live) { saveMode = 2; menuPage = 3; menuFromWorld = true; saveInfoDirty = true; }
                if (Button(new Rect(gr.x + (bw + 5 * k) * 2, by, bw, bh), "настройки", false, sSmallBtn()) && live) { menuPage = 1; menuFromWorld = true; }
                if (Button(new Rect(gr.x + (bw + 5 * k) * 3, by, bw, bh), "выйти", false, sSmallBtn()) && live) { invOpen = false; pocketClose = -1; LeaveWorld(); inMenu = true; menuPage = 0; return; }
                if (setDev && Button(new Rect(lp.x, lp.yMax - 18 * k, lp.width, 18 * k), "урон −10%", false, sSmallBtn()) && live) { Damage(10); if (projActor != null) projActor.SetLimbs(limbs); }
            }
            finally { GUI.color = oldC; uiAlpha = 1; }
        }
        GUIStyle sSmallBtnS;
        GUIStyle sSmallBtn()
        {
            int fs = Mathf.Max(7, Mathf.RoundToInt(10 * k));
            if (sSmallBtnS == null || sSmallBtnS.fontSize != fs) { sSmallBtnS = new GUIStyle(sBtn) { fontSize = fs }; }
            return sSmallBtnS;
        }
        /// what the hero is missing: its outline in red dots where it should be on the projection
        void MissingLimbDots(Rect lp)
        {
            if (projHero == null || projCam == null) return;
            int fs = Mathf.Max(7, Mathf.RoundToInt(11 * k));
            if (sDoll == null || sDoll.fontSize != fs) { sDoll = new GUIStyle(sMono) { richText = true, alignment = TextAnchor.UpperLeft, fontSize = fs, wordWrap = false }; sDoll.normal.textColor = Color.white; }
            float cw = fs * 0.62f, lh = fs * 1.18f;
            Vector2 Scr(Vector3 w) { var v = projCam.WorldToViewportPoint(w); return new Vector2(lp.x + v.x * lp.width, lp.y + (1 - v.y) * lp.height); }
            var red = new Color(0.9f, 0.25f, 0.22f, 0.95f * uiAlpha);
            void Dot(Vector3 w) { var q = Scr(w); var old = GUI.color; GUI.color = red; GUI.Label(new Rect(q.x - cw * 0.5f, q.y - lh * 0.55f, cw * 2, lh), ":", sDoll); GUI.color = old; }
            var hb = projHero.human; Transform root = projHero.go.transform; float hm = cfg.height / 100f; Vector3 side = root.right;
            for (int l = 0; l < 5; l++)
            {
                if (limbs[l]) continue;
                var b = hb.Bone(Character.LIMB_BONE[l]); if (b == null) continue; Vector3 o = b.position;
                if (l == 0) { Vector3 cc = o + Vector3.up * (0.11f * hm / 1.8f); float r = 0.11f * hm / 1.8f; for (int a = 0; a < 14; a++) { float t = a / 14f * Mathf.PI * 2; Dot(cc + side * (Mathf.Cos(t) * r) + Vector3.up * (Mathf.Sin(t) * r * 1.2f)); } }
                else
                {
                    bool arm = l <= 2; float len = (arm ? 0.6f : 0.86f) * hm / 1.8f, wdt = (arm ? 0.05f : 0.075f) * hm / 1.8f;
                    Vector3 dirD = (Vector3.down * 0.97f + side * (arm ? (l == 1 ? -1 : 1) * 0.06f * hm / 1.8f : 0)).normalized;
                    for (int a = 0; a <= 9; a++) { Vector3 p0 = o + dirD * (len * a / 9f); Dot(p0 + side * wdt); Dot(p0 - side * wdt); }
                }
            }
        }

        // ---------- saves ----------
        [System.Serializable] class SaveData
        {
            public string version, date, loc, sex, armor, weapon; public float x, z, heading, height; public int hp, build;
            public bool[] limbs; public float[] body; public string[] items;
        }
        static string SavePath(int i) { return System.IO.Path.Combine(Application.persistentDataPath, "save" + (i + 1) + ".json"); }
        void RefreshSaveInfo()
        {
            saveInfoDirty = false;
            for (int i = 0; i < SAVE_SLOTS; i++)
            {
                saveHas[i] = false; saveInfo[i] = "пусто";
                try
                {
                    if (!System.IO.File.Exists(SavePath(i))) continue;
                    var d = JsonUtility.FromJson<SaveData>(System.IO.File.ReadAllText(SavePath(i)));
                    if (d == null) continue;
                    saveHas[i] = true;
                    saveInfo[i] = d.date + " · " + LocName(d.loc) + " · " + (d.sex == "f" ? "женщина" : "мужчина") + ", " + d.height.ToString("0") + " см · целостность " + d.hp + " %";
                }
                catch (System.Exception e) { saveInfo[i] = "не читается (" + e.GetType().Name + ")"; }
            }
        }
        static string LocName(string id)
        {
            switch (id)
            {
                case "tavern": return "трактир"; case "cave": return "пещера"; case "forest": return "лес"; case "swamp": return "болото"; case "toxic": return "ядовитое болото";
                case "bright": return "светлый город"; case "grim": return "трущобы"; case "wastes": return "пустоши"; case "crystal": return "кристальные копи";
                case "autumn": return "осенний лес"; case "snow": return "высокогорье"; default: return "поляна";
            }
        }
        void SaveGame(int i)
        {
            bool tav = loc != null && loc == tavern, cv = loc != null && loc == cave, fo = loc != null && loc == forest;
            var d = new SaveData
            {
                version = VERSION, date = System.DateTime.Now.ToString("dd.MM.yyyy HH:mm"), loc = tav ? "tavern" : cv ? "cave" : fo ? "forest" : loc is Mockup ? ((Mockup)loc).kind : "field",
                x = loc != null ? pos.x : 0, z = loc != null ? pos.z : 1.5f, heading = player != null ? player.heading : Mathf.PI, hp = hp,
                limbs = (bool[])limbs.Clone(), sex = cfg.sex.ToString(), build = cfg.build, height = cfg.height, armor = cfg.armor, weapon = cfg.weapon,
                body = cfg.body != null ? (float[])cfg.body.Clone() : null, items = inventory.ToArray()
            };
            try { System.IO.File.WriteAllText(SavePath(i), JsonUtility.ToJson(d)); ShowToast("Сохранено: ячейка " + (i + 1)); Debug.Log("Игра сохранена: " + SavePath(i)); }
            catch (System.Exception e) { ShowToast("Не удалось сохранить"); Debug.LogException(e); }
            saveInfoDirty = true;
        }
        void LoadGame(int i)
        {
            SaveData d;
            try { d = JsonUtility.FromJson<SaveData>(System.IO.File.ReadAllText(SavePath(i))); }
            catch (System.Exception e) { ShowToast("Не удалось загрузить"); Debug.LogException(e); return; }
            if (d == null) return;
            cfg.sex = d.sex == "f" ? 'f' : 'm'; cfg.build = Mathf.Clamp(d.build, 0, 3); cfg.height = Mathf.Clamp(d.height, Body.DevMin(0), Body.DevMax(0));
            foreach (var a in Character.ARMORS) if (a[0] == d.armor) cfg.armor = d.armor;
            foreach (var w in Character.WEAPONS) if (w[0] == d.weapon) cfg.weapon = d.weapon;
            cfg.body = d.body != null && d.body.Length == Body.N ? (float[])d.body.Clone() : null;
            if (cfg.body == null) ResetBody();
            cfg.body[Body.Ix("height")] = cfg.height;
            SaveCfg();
            for (int l = 0; l < 5; l++) limbs[l] = d.limbs == null || l >= d.limbs.Length || d.limbs[l];
            hp = Mathf.Clamp(d.hp, 0, 100); if (hp <= 0) { hp = 100; for (int l = 0; l < 5; l++) limbs[l] = true; }
            inventory.Clear(); if (d.items != null) inventory.AddRange(d.items);
            invOpen = false; menuFromWorld = false; inMenu = false; respawnT = 0; dashT = 99; presetIx = -1;
            Rebuild();
            EnterWorld(d.loc == "tavern" ? 0 : d.loc == "cave" ? 2 : d.loc == "forest" ? 3 : 1);
            pos = new Vector3(d.x, 0, d.z); heroTrail = pos; player.heading = d.heading; player.ResetSim();
            hero.go.transform.position = pos; hero.go.transform.rotation = Quaternion.Euler(0, d.heading * Mathf.Rad2Deg, 0);
            camTgt = new Vector3(pos.x, 0.6f, pos.z);
            ShowToast("Загружено: ячейка " + (i + 1));
        }
        void SavesUI(float cx, float top, float bottom)
        {
            if (saveInfoDirty) RefreshSaveInfo();
            float pw = Mathf.Min(620 * k, Screen.width - 40 * k), x0 = cx - pw / 2, y0 = top + 56 * k;
            GUI.Label(new Rect(x0, y0, pw, 22 * k), saveMode == 1 ? "СОХРАНИТЬ ИГРУ" : saveMode == 2 ? "ЗАГРУЗИТЬ ИГРУ" : "СОХРАНЕНИЯ", sTitle);
            if (Button(new Rect(x0 + pw - 100 * k, y0 - 3 * k, 100 * k, 24 * k), "НАЗАД")) { menuPage = 0; menuFromWorld = false; return; }
            float rh = Mathf.Min(54 * k, (bottom - y0 - 40 * k) / SAVE_SLOTS - 6 * k);
            for (int i = 0; i < SAVE_SLOTS; i++)
            {
                var r = new Rect(x0, y0 + 32 * k + i * (rh + 6 * k), pw, rh);
                Fill(r, new Color(0.08f, 0.08f, 0.1f, 0.95f)); Frame(r, new Color(0.32f, 0.3f, 0.26f, 1), Mathf.Max(1, k));
                float bwid = 120 * k, nb = (saveMode == 0 ? 2 : 1);
                GUI.Label(new Rect(r.x + 10 * k, r.y + 4 * k, r.width - nb * (bwid + 8 * k) - 20 * k, 20 * k), "Ячейка " + (i + 1), sName);
                GUI.Label(new Rect(r.x + 10 * k, r.y + 24 * k, r.width - nb * (bwid + 8 * k) - 20 * k, rh - 26 * k), saveInfo[i], sDesc);
                float bx2 = r.xMax - 8 * k - bwid, bby = r.y + (rh - 30 * k) / 2;
                if (saveMode != 1)
                {
                    if (saveHas[i]) { if (Button(new Rect(bx2, bby, bwid, 30 * k), "ЗАГРУЗИТЬ")) { LoadGame(i); return; } }
                    else GUI.Label(new Rect(bx2, bby, bwid, 30 * k), "—", sBtn);
                    bx2 -= bwid + 8 * k;
                }
                if (saveMode != 2 && Button(new Rect(bx2, bby, bwid, 30 * k), saveHas[i] ? "ПЕРЕЗАПИСАТЬ" : "СОХРАНИТЬ")) SaveGame(i);
            }
        }
    }
}
