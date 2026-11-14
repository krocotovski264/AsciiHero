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
        public const string VERSION = "beta 0.25 · Unity";

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
        bool world; Tavern tavern; Field field; Location loc; Character bartenderChar; MotionActor bartender;
        // integrity instead of a health bar: five limbs (head, armL, armR, legL, legR), each worth 20 %
        int hp = 100; readonly bool[] limbs = { true, true, true, true, true }; float respawnT; Vector3 heroTrail;
        int LimbCount() { int n = 0; foreach (var b in limbs) if (b) n++; return n; }
        GameObject editorRoot, worldLightsRoot; Light key, rim, fill, heroLight;
        GameObject floor;
        bool rebuildPending;
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

        // world camera
        // MOBA camera: fixed (no rotation), high and far, looking down at about 55 degrees
        const float ISO_LOD = 2.5f / 1.5f; float isoYaw = 0, isoPitch = 0.96f, isoYawT = 0, isoPitchT = 0.96f; const float ISO_DIST = 7.6f * ISO_LOD;
        Vector3 pos, camTgt; bool lockOn; Vector3? lockFixed; float labelT; string animLabel = "idle";

        // UI
        float k = 1; Rect safe; int tab; string toast; float toastT;
        readonly List<Rect> uiRects = new List<Rect>(); List<Rect> uiRectsPrev = new List<Rect>();
        readonly List<Vector2> taps = new List<Vector2>();
        Rect sliderRect, stickRect; Vector2 stickCenter, stick; float stickR, stickRun; int stickId = int.MinValue;
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

        void Damage(int pct)
        {
            if (hp <= 0 || !ready) return;
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
            if (field == null || loc != field || hp <= 0) return;
            foreach (var p in field.piles)
            {
                if ((new Vector2(pos.x - p.pos.x, pos.z - p.pos.z)).magnitude > 0.95f) continue;
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
            var moon = DirLight("Moon", worldLightsRoot.transform, Mats.Hex(0x8aa6ff), 0.3f, true);
            moon.transform.rotation = Quaternion.LookRotation(new Vector3(1f, -7.5f, 7.5f));
        }
        void SetupField()
        {
            if (worldLightsRoot == null) SetupWorldLights();
            field = Field.Build(mats);
            GlyphLayer.Build(field.root);
            Debug.Log("Поле построено, символов на сцене: " + GlyphLayer.count);
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
        void EnterWorld(bool toField = false)
        {
            SaveCfg();
            try { if (toField) { if (field == null) SetupField(); } else if (tavern == null) SetupWorld(); }
            catch (System.Exception e) { Debug.LogException(e); return; }
            world = true; loc = toField ? (Location)field : tavern;
            if (tavern != null) { tavern.SetActive(!toField); bartenderChar.go.SetActive(!toField); }
            if (field != null) field.SetActive(toField);
            worldLightsRoot.SetActive(true); editorRoot.SetActive(false);
            pos = loc.spawn; heroTrail = pos; player.heading = Mathf.PI; player.ResetSim(); player.noLock = false;
            hero.go.transform.position = pos; hero.go.transform.rotation = Quaternion.Euler(0, 180, 0);
            camTgt = new Vector3(pos.x, 0.6f, pos.z);
            sceneCam.fieldOfView = 30; ascii.exposure = 1.3f; ascii.actorBoost = 1; Shader.SetGlobalFloat("_AsciiLod", ISO_LOD);
            if (toField) SetAmbient(Mats.Hex(0x8fa4d8) * 0.55f, Mats.Hex(0x4a5a58) * 0.55f, Mats.Hex(0x1a2414) * 0.55f);
            else SetAmbient(Mats.Hex(0x50465a) * 0.45f, Mats.Hex(0x342b32) * 0.45f, Mats.Hex(0x18100a) * 0.45f);
            SetShadowDistance(40);
            ShowToast(toField ? "Тестовое поле\nКучи брони возвращают оторванные части" : "Трактир «Пьяный дракон»\nСтик — шаг, палец за край — бег");
        }
        void LeaveWorld()
        {
            world = false;
            if (tavern != null) { tavern.SetActive(false); bartenderChar.go.SetActive(false); }
            if (field != null) field.SetActive(false);
            worldLightsRoot.SetActive(false); editorRoot.SetActive(true);
            Shader.SetGlobalVector("_AsciiHeroA", new Vector4(0, -99, 0, 0)); Shader.SetGlobalVector("_AsciiHeroB", new Vector4(0, -99, 0, 0));
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

        static bool InCircle(Rect r, Vector2 p) { return r.width > 0 && (p - r.center).magnitude <= r.width * 0.56f; }
        bool InUi(Vector2 p) { foreach (var r in uiRectsPrev) if (r.Contains(p)) return true; return false; }

        void HandlePointers(float dt)
        {
            foreach (var p in InputHub.active.Values)
            {
                if (!p.began) continue;
                // floating stick: its centre appears under the finger anywhere in the left part of the screen
                if (world && InCircle(atkRect, p.pos)) { p.owner = "atk"; DoAttack(); }
                else if (world && InCircle(defRect, p.pos)) p.owner = "def";
                else if (world && InCircle(dashRect, p.pos)) { p.owner = "dash"; DoDash(); }
                else if (world && stickId == int.MinValue && !InUi(p.pos) && p.pos.x < Screen.width * 0.45f && p.pos.y > Screen.height * 0.22f)
                { p.owner = "stick"; stickId = p.id; stickCenter = ClampStickCenter(p.pos); }
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
                if (p.id == stickId) { stickId = int.MinValue; stick = Vector2.zero; stickRun = 0; }
            }
            // slider
            foreach (var p in InputHub.active.Values)
                if (p.owner == "slider" && sliderRect.width > 0) SetHeight(150 + 50 * Mathf.Clamp01((p.pos.x - sliderRect.x) / sliderRect.width));
            // stick
            Pointer sp; if (stickId != int.MinValue && InputHub.active.TryGetValue(stickId, out sp))
            {
                Vector2 d = sp.pos - stickCenter; float max = stickR * 0.78f;
                // run: finger pulled past the ring (starts 0.25 cm beyond it, full at 0.7 cm)
                float cm = StickCm();
                stickRun = Mathf.Clamp01((d.magnitude - max - 0.25f * cm) / (0.45f * cm));
                if (d.magnitude > max) d = d.normalized * max;
                stick = d / max;
            }
            // camera
            var camPtrs = new List<Pointer>(); foreach (var p in InputHub.active.Values) if (p.owner == "cam") camPtrs.Add(p);
            float W = Screen.width, H = Screen.height;
            if (world)
            {
                // the world camera is fixed
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
        class Piece { public string kind = ""; public List<Renderer> rs = new List<Renderer>(); public float delay, om = 11, zeta = 0.45f, lift = 0.3f, wob, turns = 1, side; public Vector3 axis = Vector3.up; }
        readonly List<Piece> pieces = new List<Piece>(); MaterialPropertyBlock shiftMpb;
        const float DASH_DIST = 2.0f, DASH_T = 0.1f; float dashAnim = 0.4f, dashSettle = 0;
        float dashT = 9, dashCd, dashMist; Vector3 dashFrom, dashDir; Vector2? moveDir; bool dashShifted;
        Rect atkRect, defRect, dashRect;
        void DoAttack() { if (world && player != null && dashT >= DASH_T) player.StartAttack(); }
        void DoDash()
        {
            if (!world || player == null || dashCd > 0 || !(limbs[3] || limbs[4])) return;
            // moving: where the stick points; standing: where the hero looks
            Vector3 d = moveDir.HasValue ? new Vector3(moveDir.Value.x, 0, moveDir.Value.y) : new Vector3(Mathf.Sin(player.heading), 0, Mathf.Cos(player.heading));
            dashDir = d.normalized; dashT = 0; dashFrom = pos; dashMist = 0;
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
        void SetShift(Piece pc, Vector3 v, Vector4 spin)
        {
            if (shiftMpb == null) shiftMpb = new MaterialPropertyBlock();
            Vector3 piv = pc.rs.Count > 0 && pc.rs[0] != null ? pc.rs[0].bounds.center : pos;     // the piece tumbles about its own centre
            foreach (var r in pc.rs)
            {
                if (r == null) continue; r.GetPropertyBlock(shiftMpb);
                shiftMpb.SetVector("_DashShift", v); shiftMpb.SetVector("_DashSpin", spin); shiftMpb.SetVector("_DashPivot", piv); shiftMpb.SetVector("_DashScatter", Vector4.zero);
                r.SetPropertyBlock(shiftMpb);
            }
        }
        void UpdateDash(float dt)
        {
            dashCd = Mathf.Max(0, dashCd - dt);
            if (dashT > dashSettle)
            {
                if (dashShifted) { foreach (var pc in pieces) SetShift(pc, Vector3.zero, new Vector4(0, 1, 0, 0)); dashShifted = false; }
                return;
            }
            if (dashT < DASH_T)
            {   // the shadow itself crosses the distance in a tenth of a second
                float kk = Mathf.Min(dt, DASH_T - dashT) / DASH_T;
                var dp = loc.Collide(new Vector2(pos.x + dashDir.x * DASH_DIST * kk, pos.z + dashDir.z * DASH_DIST * kk), 0.27f);
                player.Teleport(dp.x - pos.x, dp.y - pos.z, player.heading);
                pos = new Vector3(dp.x, 0, dp.y);
            }
            dashT += dt;
            Vector3 back = dashFrom - pos; dashShifted = true;
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
                if (pc.kind == "robe")
                {
                    float fl = last ? 0 : (1 - sm) * Mathf.Min(1, tau * 6);
                    Vector3 shR = back * (1 - p) + sideV * (pc.side * arc + 0.1f * Mathf.Sin(tau * 9) * fl) + Vector3.up * (pc.lift * arc + 0.07f * Mathf.Sin(tau * 13) * fl);
                    SetShift(pc, shR, new Vector4(sideV.x, 0, sideV.z, tau > 0 && !last ? (0.5f * arc + 0.18f * Mathf.Sin(tau * 11)) * (1 - sm * sm) + 1e-4f : 0));     // it streams out behind and billows
                    continue;
                }
                Vector3 sh = back * (1 - p) + sideV * (pc.side * arc) + Vector3.up * (pc.lift * arc);
                SetShift(pc, sh, new Vector4(pc.axis.x, pc.axis.y, pc.axis.z, tau > 0 && !last ? pc.turns * Mathf.PI * 2 * (1 - sm) + pc.wob * (sm - p) * 3 : 0));
            }
            // mist hangs along the path
            dashMist -= dt;
            if (dashMist <= 0 && dashT < dashAnim) { dashMist = 0.035f; float rr = Random.value; SpawnPuff(new Vector3(pos.x + back.x * rr, 0.35f + Random.value * 1.1f, pos.z + back.z * rr), 0.07f, new Vector3(0, 0.15f, 0)); }
        }

        void WorldUpdate(float dt)
        {
            float ix = stick.x, iy = stick.y;
            if (InputHub.Key("W")) iy = -1; if (InputHub.Key("S")) iy = 1;
            if (InputHub.Key("A")) ix = -1; if (InputHub.Key("D")) ix = 1;
            float l = Mathf.Sqrt(ix * ix + iy * iy), mag = Mathf.Min(1, l / 0.8f); if (mag < 0.12f) mag = 0;   // 80 % of the ring = full walk
            float runIn = Mathf.Max(stickRun, InputHub.Key("Run") ? 1f : 0f);
            if (mag > 0.85f || (mag > 0 && runIn > 0)) mag = 1 + runIn;
            Vector2? dir = null;
            if (mag > 0)
            {
                float ux = ix / l, uy = iy / l, cs = Mathf.Cos(isoYaw), sn = Mathf.Sin(isoYaw);
                // web direction, mirrored on X
                dir = new Vector2(-(ux * cs + uy * sn), -ux * sn + uy * cs);
            }
            Vector2? lockPos = null;
            if (lockOn) { var lp = lockFixed ?? (loc == tavern ? bartenderChar.go.transform.position : loc.spawn); lockPos = new Vector2(lp.x, lp.z); }
            moveDir = dir;
            bool guardPtr = false; foreach (var ap in InputHub.active.Values) if (ap.owner == "def") guardPtr = true;
            player.guardHeld = guardPtr || InputHub.Key("Guard");
            bool atkPtr = false; foreach (var ap in InputHub.active.Values) if (ap.owner == "atk") atkPtr = true;
            player.atkHeld = atkPtr || InputHub.Key("AttackHeld");
            if (InputHub.Key("Attack")) DoAttack();
            if (InputHub.Key("Dash")) DoDash();
            var dd = player.Locomotion(dt, dir, mag, lockPos, new Vector2(pos.x, pos.z), p => loc.Collide(p, 0.27f));
            var np = loc.Collide(new Vector2(pos.x + dd.x, pos.z + dd.y), 0.27f);
            pos = new Vector3(np.x, 0, np.y);
            UpdateDash(dt);
            if (player.trail.Count > 0) { foreach (var q in player.trail) SpawnPuff(q, 0.03f, Vector3.zero, 0.3f); player.trail.Clear(); }
            hero.go.transform.position = pos;
            hero.go.transform.rotation = Quaternion.Euler(0, (player.heading - player.spinYaw) * Mathf.Rad2Deg, 0);      // the full-turn sweep spins the whole figure
            loc.Update(tNow); Pickups(); UpdateDummies(dt, tNow); UpdateDebris(dt); UpdateDanger(tNow);
            if (respawnT > 0) { respawnT -= dt; if (respawnT <= 0) { RestoreBody(); pos = loc.spawn; player.ResetSim(); } }
            // grass: the hero and a point trailing behind him push the blades aside
            heroTrail = Vector3.Lerp(heroTrail, pos, Mathf.Min(1, dt * 2.2f));
            Shader.SetGlobalVector("_AsciiHeroVel", player != null ? (Vector4)player.Velocity : Vector4.zero);
            Shader.SetGlobalVector("_AsciiHeroA", new Vector4(pos.x, 0, pos.z, 0)); Shader.SetGlobalVector("_AsciiHeroB", new Vector4(heroTrail.x, 0, heroTrail.z, 0));
            labelT -= dt; if (labelT <= 0) { labelT = 0.25f; animLabel = player.label; }
            if (loc == tavern)
            {
                var bp = bartenderChar.go.transform.position;
                bartender.Locomotion(dt, null, 0, null, new Vector2(bp.x, bp.z), null); bartender.heading = 0; bartender.noLock = true;
                bartender.Animate(tNow, dt);
            }
            // camera
            isoYaw += (isoYawT - isoYaw) * Mathf.Min(1, dt * 12); isoPitch += (isoPitchT - isoPitch) * Mathf.Min(1, dt * 12);
            Vector3 off = Mirror(new Vector3(Mathf.Sin(isoYaw) * Mathf.Cos(isoPitch), Mathf.Sin(isoPitch), Mathf.Cos(isoYaw) * Mathf.Cos(isoPitch)) * ISO_DIST);
            { var d = new Vector3(off.x, 0, off.z).normalized; heroLight.transform.position = new Vector3(pos.x + d.x * 1.3f - d.z * 0.5f, 2.3f, pos.z + d.z * 1.3f + d.x * 0.5f); }
            loc.Cutaway(off);
            camTgt = Vector3.Lerp(camTgt, new Vector3(pos.x, 0.6f, pos.z), Mathf.Min(1, dt * 5));
            sceneCam.transform.position = camTgt + off; sceneCam.transform.LookAt(camTgt);
            sceneCam.fieldOfView = 30 * (1 - 0.014f * heart);
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
                RenderTexture.active = null;   // the interface always goes to the screen
                if (ascii.final != null) GUI.DrawTexture(vr, ascii.final, ScaleMode.StretchToFill, false);
                else GUI.Label(new Rect(vr.x + 20 * k, vr.center.y, vr.width - 40 * k, 30 * k), "Кадр ASCII ещё не готов", sSmall);
            }
            float L = safe.x + 12 * k, T = safe.y + 8 * k;
            if (world) DrawDanger(W, H);
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
            GUI.Label(new Rect(L, H - (H - safe.yMax) - 52 * k, 320 * k, 50 * k), "<b>" + SEXNAME[si] + "</b> · " + cfg.height + " см · " + Character.BUILDS[cfg.build] + "\n" + an + "\n" + wn, sBadge);
            // view buttons
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
            float bw3 = (cw - 12 * k) / 3;
            if (Button(new Rect(x0, bottom, bw3, 32 * k), "СЛУЧАЙНО")) Randomize();
            if (Button(new Rect(x0 + bw3 + 6 * k, bottom, bw3, 32 * k), "ТРАКТИР ▶", true)) EnterWorld(false);
            if (Button(new Rect(x0 + (bw3 + 6 * k) * 2, bottom, bw3, 32 * k), "ПОЛЕ ▶", true)) EnterWorld(true);
        }

        void WorldUI(float W, float H, float L, float T)
        {
            GUI.Label(new Rect(L, T, 360 * k, 22 * k), loc == field ? "ТЕСТОВОЕ ПОЛЕ" : "«ПЬЯНЫЙ ДРАКОН»", sTitle);
            GUI.Label(new Rect(L, T + 20 * k, 360 * k, 14 * k), (loc == field ? "трава · кучи брони · " : "трактир · ") + VERSION, sSmall);
            int si = cfg.sex == 'f' ? 1 : 0;
            GUI.Label(new Rect(L, T + 38 * k, 420 * k, 50 * k), "<b>" + SEXNAME[si] + "</b> · " + cfg.height + " см\nСтик — шаг, палец за край — бег\nЦелостность: " + hp + "% · " + animLabel, sBadge);
            float R = safe.xMax - 10 * k;
            if (Button(new Rect(R - 96 * k, T, 96 * k, 28 * k), "← ГЕРОЙ")) { LeaveWorld(); return; }
            if (Button(new Rect(R - 200 * k, T + 32 * k, 200 * k, 26 * k), ascii.surfaceGlyphs ? "Aa: НА МОДЕЛЯХ" : "Aa: СЕТКОЙ", ascii.surfaceGlyphs)) ToggleGlyphMode();
            if (Button(new Rect(R - 304 * k, T, 96 * k, 28 * k), "УРОН −10%")) Damage(10);
            if (Button(new Rect(R - 200 * k, T, 96 * k, 28 * k), "ЦЕЛЬ", lockOn))
            {
                lockOn = !lockOn; lockFixed = null;
                if (lockOn && loc == tavern && Vector3.Distance(bartenderChar.go.transform.position, pos) > 10)
                    lockFixed = new Vector3(pos.x + Mathf.Sin(player.heading) * 20, 0, pos.z + Mathf.Cos(player.heading) * 20);
                ShowToast(lockOn ? "Фиксация на цели: трактирщик\nСтик — стрейф 8 направлений" : "Свободное движение");
            }
            // joystick
            stickR = 62 * k;
            if (stickId == int.MinValue) stickCenter = ClampStickCenter(new Vector2(0, Screen.height));   // resting place: lowest spot that still leaves room to run
            var old = GUI.color;
            GUI.color = new Color(0.93f, 0.74f, 0.33f, 0.55f);
            GUI.DrawTexture(new Rect(stickCenter.x - stickR, stickCenter.y - stickR, stickR * 2, stickR * 2), texRing);
            GUI.color = new Color(0.93f, 0.74f, 0.33f, stickId != int.MinValue ? 0.85f : 0.55f);
            var kc = stickCenter + stick * stickR * 0.78f; float kr = stickR * 0.42f;
            GUI.DrawTexture(new Rect(kc.x - kr, kc.y - kr, kr * 2, kr * 2), texDisc);
            GUI.color = old;
            // action buttons, bottom right: attack (big), guard to its left, dash above
            float bx = safe.xMax - 22 * k, by = safe.yMax - 22 * k, ra = 46 * k, rb = 34 * k;
            atkRect = new Rect(bx - ra * 2, by - ra * 2, ra * 2, ra * 2);
            defRect = new Rect(bx - ra * 2 - 14 * k - rb * 2, by - rb * 2, rb * 2, rb * 2);
            dashRect = new Rect(bx - ra - rb, by - ra * 2 - 14 * k - rb * 2, rb * 2, rb * 2);
            bool defOn = player != null && player.guardHeld, atkOn = player != null && player.Attacking, dashOn = dashCd > 0;
            ActionButton(atkRect, "АТАКА", atkOn); ActionButton(defRect, "БЛОК", defOn); ActionButton(dashRect, "РЫВОК", dashOn);
        }
    }
}
