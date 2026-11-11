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
        public const string VERSION = "beta 0.9.1 · Unity";

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
        bool world; Tavern tavern; Character bartenderChar; MotionActor bartender;
        GameObject editorRoot, worldLightsRoot; Light key, rim, fill, heroLight;
        GameObject floor;
        bool rebuildPending;
        bool useAnimator; RuntimeAnimatorController locoCtrl;   // Animator Controller mode (Tools -> Patch Project Locomotion)
        float tNow;

        // editor camera (web coordinates; mirrored on X when applied)
        struct OrbitCam { public float yaw, pitch, dist, ty; }
        static readonly OrbitCam CAM0 = new OrbitCam { yaw = 0.5f, pitch = 0.1f, dist = 3.7f, ty = 0.95f };
        OrbitCam cam = CAM0;
        bool autoRotate = true, interacting; float vel, lastInteract, lastTap = -1;
        float pinchD0, pinchDist0, pinchMy0, pinchTy0; bool pinching;

        // world camera
        float isoYaw = Mathf.PI / 4, isoPitch = 0.86f, isoYawT = Mathf.PI / 4, isoPitchT = 0.86f; const float ISO_DIST = 7.6f;
        Vector3 pos, camTgt; bool lockOn; Vector3? lockFixed; float labelT; string animLabel = "idle";

        // UI
        float k = 1; Rect safe; int tab; string toast; float toastT;
        readonly List<Rect> uiRects = new List<Rect>(); List<Rect> uiRectsPrev = new List<Rect>();
        readonly List<Vector2> taps = new List<Vector2>();
        Rect sliderRect, stickRect; Vector2 stickCenter, stick; float stickR; int stickId = int.MinValue;
        Texture2D texWhite, texDisc, texRing; GUIStyle sTitle, sSmall, sBtn, sBtnOn, sMono, sName, sDesc, sBadge, sToast;
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
            Application.targetFrameRate = 60;
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
                ascii = new AsciiRenderer(sceneCam);
                ascii.surfaceGlyphs = PlayerPrefs.GetInt("ascii_glyph_mode", 1) == 1;
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
                useAnimator = PlayerPrefs.GetInt("ascii_anim_mode", 0) == 1;
                if (useAnimator) { locoCtrl = Resources.Load<RuntimeAnimatorController>("Locomotion"); if (locoCtrl == null) useAnimator = false; }
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
            var fm = mats.WithTexture("STONE", c.Texture(false), Vector2.one);
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

        void SetupWorld()
        {
            tavern = Tavern.Build(mats);
            GlyphLayer.Build(tavern.root);
            worldLightsRoot = new GameObject("WorldLights");
            var hl = new GameObject("HeroLight"); hl.transform.SetParent(worldLightsRoot.transform, false);
            heroLight = hl.AddComponent<Light>(); heroLight.type = LightType.Point; heroLight.color = Mats.Hex(0xffe6c8); heroLight.intensity = 1.6f; heroLight.range = 4.2f; heroLight.renderMode = LightRenderMode.ForcePixel;
            var moon = DirLight("Moon", worldLightsRoot.transform, Mats.Hex(0x8aa6ff), 0.3f, true);
            moon.transform.rotation = Quaternion.LookRotation(new Vector3(1f, -7.5f, 7.5f));
            bartenderChar = Character.Build(new CharConfig { sex = 'm', build = 3, height = 178, armor = "cloth", weapon = "none" }, actorMats, "Bartender");
            bartenderChar.go.transform.position = tavern.bartender;
            GlyphLayer.Build(bartenderChar.go);
            bartender = new MotionActor(bartenderChar);
            tavern.colliders.Add(Collider2.Circle(tavern.bartender.x, tavern.bartender.z, 0.3f));
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
        }
        void SaveCfg()
        {
            PlayerPrefs.SetString("ascii_hero3d", cfg.sex + "|" + cfg.build + "|" + cfg.height.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + cfg.armor + "|" + cfg.weapon);
            PlayerPrefs.Save();
        }
        void SetSex(char s) { if (cfg.sex != s) { cfg.sex = s; rebuildPending = true; SaveCfg(); } }
        void SetBuild(int b) { if (cfg.build != b) { cfg.build = b; rebuildPending = true; SaveCfg(); } }
        void SetArmor(string a) { if (cfg.armor != a) { cfg.armor = a; rebuildPending = true; SaveCfg(); } }
        void SetWeapon(string w) { if (cfg.weapon != w) { cfg.weapon = w; rebuildPending = true; SaveCfg(); } }
        void SetHeight(float h)
        {
            h = Mathf.Clamp(Mathf.Round(h), 150, 200); if (h == cfg.height) return;
            cfg.height = h; if (hero != null) { hero.cfg.height = h; hero.ApplyHeight(); } SaveCfg();
        }
        void Randomize()
        {
            cfg.sex = Random.value < 0.5f ? 'm' : 'f';
            cfg.height = Mathf.Round((cfg.sex == 'f' ? 155 : 168) + Random.value * 28);
            cfg.build = Random.Range(0, 4);
            cfg.armor = Character.ARMORS[Random.Range(0, Character.ARMORS.Length)][0];
            cfg.weapon = Character.WEAPONS[Random.Range(0, Character.WEAPONS.Length)][0];
            rebuildPending = true; SaveCfg();
        }

        void Rebuild()
        {
            if (hero != null) Destroy(hero.go);
            hero = Character.Build(cfg, actorMats, "Hero");
            GlyphLayer.Build(hero.go);
            player = new MotionActor(hero);
            if (useAnimator && locoCtrl != null)
            {
                player.driver = AnimatorDriver.Attach(hero.go, locoCtrl);
                player.maxSpeed = Mathf.Max(2.9f, player.driver.sprintSpeed);
            }
            if (world) { hero.go.transform.position = pos; hero.go.transform.rotation = Quaternion.Euler(0, player.heading * Mathf.Rad2Deg, 0); }
        }

        // ---------- modes ----------
        void EnterWorld()
        {
            SaveCfg();
            if (tavern == null) SetupWorld();
            world = true;
            tavern.SetActive(true); worldLightsRoot.SetActive(true); bartenderChar.go.SetActive(true); editorRoot.SetActive(false);
            pos = tavern.spawn; player.heading = Mathf.PI; player.ResetSim(); player.noLock = false;
            hero.go.transform.position = pos; hero.go.transform.rotation = Quaternion.Euler(0, 180, 0);
            camTgt = new Vector3(pos.x, 0.6f, pos.z);
            sceneCam.fieldOfView = 30; ascii.exposure = 1.3f; ascii.actorBoost = 1;
            SetAmbient(Mats.Hex(0x50465a) * 0.45f, Mats.Hex(0x342b32) * 0.45f, Mats.Hex(0x18100a) * 0.45f);
            SetShadowDistance(20);
            ShowToast("Трактир «Пьяный дракон»\nСтик — движение · свайп — камера");
        }
        void LeaveWorld()
        {
            world = false;
            tavern.SetActive(false); worldLightsRoot.SetActive(false); bartenderChar.go.SetActive(false); editorRoot.SetActive(true);
            hero.go.transform.position = Vector3.zero; hero.go.transform.rotation = Quaternion.identity;
            player.speed = 0; player.heading = 0; player.ResetSim(); lockOn = false;
            sceneCam.fieldOfView = 36; ascii.exposure = 1; ascii.actorBoost = 0;
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
        static void SetShadowDistance(float d)
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) urp.shadowDistance = d; else QualitySettings.shadowDistance = d;
        }
        void OnDestroy() { if (ascii != null) ascii.Dispose(); }
        void ShowToast(string t) { toast = t; toastT = 2.6f; }

        // ---------- per frame ----------
        void Update()
        {
            InputHub.Poll();
            LogHub.Pump();
            if (!ready) { HandlePointers(0); return; }
            float dt = Mathf.Min(0.1f, Time.deltaTime); tNow = Time.time;
            if (rebuildPending) { rebuildPending = false; Rebuild(); }
            HandlePointers(dt);
            if (InputHub.Key("Back")) { if (world) LeaveWorld(); else Application.Quit(); }

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
            if (player.driver == null) player.Animate(tNow, dt);   // Animator mode: after the Animator, in LateUpdate
            if (toastT > 0) toastT -= dt;
            fpsAcc += Time.unscaledDeltaTime; fpsN++;
            if (fpsAcc > 1) { fpsText = Mathf.RoundToInt(fpsN / fpsAcc) + " fps · " + (ascii.surfaceGlyphs ? GlyphLayer.count + " симв." : ascii.cols + "×" + ascii.rows); fpsAcc = 0; fpsN = 0; }
        }

        bool InUi(Vector2 p) { foreach (var r in uiRectsPrev) if (r.Contains(p)) return true; return false; }

        void HandlePointers(float dt)
        {
            foreach (var p in InputHub.active.Values)
            {
                if (!p.began) continue;
                if (world && stickRect.Contains(p.pos) && stickId == int.MinValue && (p.pos - stickCenter).magnitude < stickR * 1.25f) { p.owner = "stick"; stickId = p.id; }
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
                if (p.owner == "ui" && (p.pos - p.start).magnitude < 40 * k) taps.Add(p.pos);
                if (p.id == stickId) { stickId = int.MinValue; stick = Vector2.zero; }
            }
            // slider
            foreach (var p in InputHub.active.Values)
                if (p.owner == "slider" && sliderRect.width > 0) SetHeight(150 + 50 * Mathf.Clamp01((p.pos.x - sliderRect.x) / sliderRect.width));
            // stick
            Pointer sp; if (stickId != int.MinValue && InputHub.active.TryGetValue(stickId, out sp))
            {
                Vector2 d = sp.pos - stickCenter; float max = stickR * 0.78f;
                if (d.magnitude > max) d = d.normalized * max;
                stick = d / max;
            }
            // camera
            var camPtrs = new List<Pointer>(); foreach (var p in InputHub.active.Values) if (p.owner == "cam") camPtrs.Add(p);
            float W = Screen.width, H = Screen.height;
            if (world)
            {
                if (camPtrs.Count > 0)
                {
                    var d = camPtrs[0].Delta;
                    isoYawT -= d.x * 5.0f / Mathf.Max(W, 320);
                    isoPitchT = Mathf.Clamp(isoPitchT + d.y * 2.5f / Mathf.Max(H, 240), 0.55f, 1.35f);
                }
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

        static Vector3 Mirror(Vector3 v) { return new Vector3(-v.x, v.y, v.z); }

        void WorldUpdate(float dt)
        {
            float ix = stick.x, iy = stick.y;
            if (InputHub.Key("W")) iy = -1; if (InputHub.Key("S")) iy = 1;
            if (InputHub.Key("A")) ix = -1; if (InputHub.Key("D")) ix = 1;
            float l = Mathf.Sqrt(ix * ix + iy * iy), mag = Mathf.Min(1, l); if (mag < 0.12f) mag = 0;
            Vector2? dir = null;
            if (mag > 0)
            {
                float ux = ix / l, uy = iy / l, cs = Mathf.Cos(isoYaw), sn = Mathf.Sin(isoYaw);
                // web direction, mirrored on X
                dir = new Vector2(-(ux * cs + uy * sn), -ux * sn + uy * cs);
            }
            Vector2? lockPos = null;
            if (lockOn) { var lp = lockFixed ?? bartenderChar.go.transform.position; lockPos = new Vector2(lp.x, lp.z); }
            var dd = player.Locomotion(dt, dir, mag, lockPos, new Vector2(pos.x, pos.z), p => tavern.Collide(p, 0.27f));
            var np = tavern.Collide(new Vector2(pos.x + dd.x, pos.z + dd.y), 0.27f);
            pos = new Vector3(np.x, 0, np.y);
            hero.go.transform.position = pos;
            hero.go.transform.rotation = Quaternion.Euler(0, player.heading * Mathf.Rad2Deg, 0);
            tavern.Update(tNow);
            labelT -= dt; if (labelT <= 0) { labelT = 0.25f; animLabel = player.label; }
            var bp = bartenderChar.go.transform.position;
            bartender.Locomotion(dt, null, 0, null, new Vector2(bp.x, bp.z), null); bartender.heading = 0; bartender.noLock = true;
            bartender.Animate(tNow, dt);
            // camera
            isoYaw += (isoYawT - isoYaw) * Mathf.Min(1, dt * 12); isoPitch += (isoPitchT - isoPitch) * Mathf.Min(1, dt * 12);
            Vector3 off = Mirror(new Vector3(Mathf.Sin(isoYaw) * Mathf.Cos(isoPitch), Mathf.Sin(isoPitch), Mathf.Cos(isoYaw) * Mathf.Cos(isoPitch)) * ISO_DIST);
            { var d = new Vector3(off.x, 0, off.z).normalized; heroLight.transform.position = new Vector3(pos.x + d.x * 1.3f - d.z * 0.5f, 2.3f, pos.z + d.z * 1.3f + d.x * 0.5f); }
            tavern.Cutaway(off);
            camTgt = Vector3.Lerp(camTgt, new Vector3(pos.x, 0.6f, pos.z), Mathf.Min(1, dt * 5));
            sceneCam.transform.position = camTgt + off; sceneCam.transform.LookAt(camTgt);
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
            ascii.depthRef = world ? ISO_DIST : cam.dist;
            ascii.SetSize(Mathf.RoundToInt(vr.width), Mathf.RoundToInt(vr.height), cell * dp);
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
        }

        void ToggleGlyphMode()
        {
            ascii.surfaceGlyphs = !ascii.surfaceGlyphs;
            PlayerPrefs.SetInt("ascii_glyph_mode", ascii.surfaceGlyphs ? 1 : 0); PlayerPrefs.Save();
            ShowToast(ascii.surfaceGlyphs ? "Символы на моделях" : "Символы сеткой экрана (beta 0.8)");
        }

        void ToggleAnimMode()
        {
            if (!useAnimator)
            {
                locoCtrl = Resources.Load<RuntimeAnimatorController>("Locomotion");
                if (locoCtrl == null) { ShowToast("Нет Animator Controller.\nВ редакторе: Tools → Patch Project Locomotion"); return; }
            }
            useAnimator = !useAnimator;
            PlayerPrefs.SetInt("ascii_anim_mode", useAnimator ? 1 : 0); PlayerPrefs.Save();
            float h = player != null ? player.heading : 0;
            Rebuild();
            if (world) { player.heading = h; player.ResetSim(); player.noLock = false; }
            ShowToast(useAnimator ? "Анимация: Animator Controller\n(Blend Tree, повороты, старты/стопы, пивот)" : "Анимация: Motion Matching");
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
        }
        void Fill(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, texWhite); GUI.color = old; }
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
                try { ascii.Composite(); }   // the scene cameras have rendered this frame
                catch (System.Exception e) { Debug.LogException(e); }
                if (ascii.final != null) GUI.DrawTexture(vr, ascii.final, ScaleMode.StretchToFill, false);
                else GUI.Label(new Rect(vr.x + 20 * k, vr.center.y, vr.width - 40 * k, 30 * k), "Кадр ASCII ещё не готов", sSmall);
            }
            float L = safe.x + 12 * k, T = safe.y + 8 * k;
            if (!world) EditorUI(W, H, vr, L, T); else WorldUI(W, H, L, T);
            if (toastT > 0 && !string.IsNullOrEmpty(toast))
            {
                float a = Mathf.Clamp01(toastT / 0.4f); var r = new Rect(vr.center.x - 150 * k, H * 0.18f, 300 * k, 46 * k);
                Fill(r, new Color(0, 0, 0, 0.7f * a)); var old = GUI.color; GUI.color = new Color(1, 1, 1, a); GUI.Label(r, toast, sToast); GUI.color = old;
            }
            GUI.Label(new Rect(vr.xMax - 170 * k, H - 18 * k - (H - safe.yMax), 160 * k, 16 * k), fpsText, sSmall);
            ConsoleUI(W, H, false);
            FinishGui();
        }
        void FinishGui() { taps.Clear(); uiRectsPrev = new List<Rect>(uiRects); }

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
            GUI.Label(new Rect(panel.x + 8 * k, by + 2 * k, panel.width - 350 * k, bh), "Лог · ошибок: " + LogHub.errors + " · " + LogHub.FilePath, sSmall);
            var st = new GUIStyle(sSmall) { wordWrap = true, richText = false, fontSize = Mathf.Max(8, Mathf.RoundToInt(9.5f * k)) };
            if (mono != null) st.font = mono;
            float lineH = st.lineHeight + 1, y = panel.yMax - 6 * k, x = panel.x + 8 * k, w = panel.width - 16 * k;
            for (int i = LogHub.lines.Count - 1; i >= 0 && y > by + bh + 6 * k; i--)
            {
                var l = LogHub.lines[i];
                string text = l.text.Length > 900 ? l.text.Substring(0, 900) + "…" : l.text;
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
            GUI.Label(new Rect(L, H - (H - safe.yMax) - 52 * k, 320 * k, 50 * k), "<b>" + SEXNAME[si] + "</b> · " + cfg.height + " см · " + Character.BUILDS[cfg.build] + "\n" + an + "\n" + wn, sBadge);
            // view buttons
            if (Button(new Rect(vr.xMax - 128 * k, T, 58 * k, 24 * k), "АВТО", autoRotate)) autoRotate = !autoRotate;
            if (Button(new Rect(vr.xMax - 66 * k, T, 58 * k, 24 * k), "СБРОС")) cam = CAM0;
            if (Button(new Rect(vr.xMax - 128 * k, T + 28 * k, 120 * k, 24 * k), useAnimator ? "АНИМ: ANIMATOR" : "АНИМ: MM", useAnimator)) ToggleAnimMode();
            if (Button(new Rect(vr.xMax - 128 * k, T + 56 * k, 120 * k, 24 * k), ascii.surfaceGlyphs ? "Aa: НА МОДЕЛЯХ" : "Aa: СЕТКОЙ", ascii.surfaceGlyphs)) ToggleGlyphMode();

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
            if (tab == 0)
            {
                GUI.Label(new Rect(x0, y, cw, 16 * k), "ПОЛ", sSmall); y += 16 * k;
                float hw = (cw - 6 * k) / 2;
                if (Button(new Rect(x0, y, hw, 30 * k), SEXNAME[0], si == 0)) SetSex('m');
                if (Button(new Rect(x0 + hw + 6 * k, y, hw, 30 * k), SEXNAME[1], si == 1)) SetSex('f');
                y += 40 * k;
                GUI.Label(new Rect(x0, y, cw, 16 * k), "РОСТ · " + cfg.height + " см", sSmall); y += 18 * k;
                if (Button(new Rect(x0, y, 30 * k, 26 * k), "−")) SetHeight(cfg.height - 1);
                if (Button(new Rect(x0 + cw - 30 * k, y, 30 * k, 26 * k), "+")) SetHeight(cfg.height + 1);
                sliderRect = new Rect(x0 + 38 * k, y - 4 * k, cw - 76 * k, 34 * k);
                var track = new Rect(sliderRect.x, y + 11 * k, sliderRect.width, 4 * k); Fill(track, new Color(0.25f, 0.24f, 0.22f));
                float u = (cfg.height - 150) / 50f; Fill(new Rect(track.x, track.y, track.width * u, track.height), new Color(0.93f, 0.74f, 0.33f));
                GUI.DrawTexture(new Rect(track.x + track.width * u - 9 * k, y + 4 * k, 18 * k, 18 * k), texDisc);
                uiRects.Add(sliderRect);
                y += 40 * k;
                GUI.Label(new Rect(x0, y, cw, 16 * k), "ТЕЛОСЛОЖЕНИЕ", sSmall); y += 16 * k;
                for (int b = 0; b < 4; b++)
                {
                    var r = new Rect(x0 + (b % 2) * (hw + 6 * k), y + (b / 2) * 36 * k, hw, 30 * k);
                    if (Button(r, Character.BUILDS[b], cfg.build == b)) SetBuild(b);
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
            if (Button(new Rect(x0, bottom, bw2, 32 * k), "СЛУЧАЙНО")) Randomize();
            if (Button(new Rect(x0 + bw2 + 6 * k, bottom, bw2, 32 * k), "В ТРАКТИР ▶", true)) EnterWorld();
        }

        void WorldUI(float W, float H, float L, float T)
        {
            GUI.Label(new Rect(L, T, 360 * k, 22 * k), "«ПЬЯНЫЙ ДРАКОН»", sTitle);
            GUI.Label(new Rect(L, T + 20 * k, 360 * k, 14 * k), "трактир · " + VERSION, sSmall);
            int si = cfg.sex == 'f' ? 1 : 0;
            GUI.Label(new Rect(L, T + 38 * k, 420 * k, 50 * k), "<b>" + SEXNAME[si] + "</b> · " + cfg.height + " см\nСтик — движение · свайп — камера\nАнимация: " + animLabel, sBadge);
            float R = safe.xMax - 10 * k;
            if (Button(new Rect(R - 96 * k, T, 96 * k, 28 * k), "← ГЕРОЙ")) { LeaveWorld(); return; }
            if (Button(new Rect(R - 200 * k, T + 32 * k, 200 * k, 26 * k), useAnimator ? "АНИМ: ANIMATOR" : "АНИМ: MM", useAnimator)) ToggleAnimMode();
            if (Button(new Rect(R - 200 * k, T + 62 * k, 200 * k, 26 * k), ascii.surfaceGlyphs ? "Aa: НА МОДЕЛЯХ" : "Aa: СЕТКОЙ", ascii.surfaceGlyphs)) ToggleGlyphMode();
            if (Button(new Rect(R - 200 * k, T, 96 * k, 28 * k), "ЦЕЛЬ", lockOn))
            {
                lockOn = !lockOn; lockFixed = null;
                if (lockOn && Vector3.Distance(bartenderChar.go.transform.position, pos) > 10)
                    lockFixed = new Vector3(pos.x + Mathf.Sin(player.heading) * 20, 0, pos.z + Mathf.Cos(player.heading) * 20);
                ShowToast(lockOn ? "Фиксация на цели: трактирщик\nСтик — стрейф 8 направлений" : "Свободное движение");
            }
            // joystick
            stickR = 62 * k;
            stickCenter = new Vector2(safe.x + 26 * k + stickR, safe.yMax - 22 * k - stickR);
            stickRect = new Rect(stickCenter.x - stickR * 1.6f, stickCenter.y - stickR * 1.6f, stickR * 3.2f, stickR * 3.2f);
            var old = GUI.color;
            GUI.color = new Color(0.93f, 0.74f, 0.33f, 0.55f);
            GUI.DrawTexture(new Rect(stickCenter.x - stickR, stickCenter.y - stickR, stickR * 2, stickR * 2), texRing);
            GUI.color = new Color(0.93f, 0.74f, 0.33f, stickId != int.MinValue ? 0.85f : 0.55f);
            var kc = stickCenter + stick * stickR * 0.78f; float kr = stickR * 0.42f;
            GUI.DrawTexture(new Rect(kc.x - kr, kc.y - kr, kr * 2, kr * 2), texDisc);
            GUI.color = old;
        }
    }
}
