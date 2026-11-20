// The cinematic "dragon" (?cine=dragon): the demon dragon flies over the story's open land, lands, breathes fire in front
// of itself; when the smoke clears the camera pulls back over the knight's shoulder: the hero, his cloak in the wind,
// facing the beast, takes his stance, the sword in both hands. All of it on the game's engine and its characters.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace AsciiHero
{
    public partial class Game
    {
        const float DRG_K = 12f;               // the dragon's size: 1.25 units long in its file -> 15 m, wings 30 m
        CineDragon drg; CineParticles fireP, smokeP;
        Light fireLight;

        struct Part { public Vector3 p, v; public float age, life, size, heat; public int kind; }    // kind 0 fire, 1 ember, 2 smoke, 3 dust
        readonly List<Part> fireL = new List<Part>(), smokeL = new List<Part>();
        System.Random cr = new System.Random(7);
        float Rnd() { return (float)cr.NextDouble(); }
        Vector3 RndSphere() { Vector3 v; do v = new Vector3(Rnd() * 2 - 1, Rnd() * 2 - 1, Rnd() * 2 - 1); while (v.sqrMagnitude > 1); return v; }

        static string CineBase()
        {
            string u = Application.absoluteURL; int q = u.IndexOf('?'); if (q >= 0) u = u.Substring(0, q);
            return u.Substring(0, u.LastIndexOf('/') + 1);
        }

        IEnumerator CineLoadDragon()
        {
            string b = CineBase();
            var r1 = UnityWebRequest.Get(b + "cine/demon.bin"); yield return r1.SendWebRequest();
            var r2 = UnityWebRequestTexture.GetTexture(b + "cine/demon_base.png", false); yield return r2.SendWebRequest();
            if (r1.result != UnityWebRequest.Result.Success || r2.result != UnityWebRequest.Result.Success) { CineLog("cine: dragon not loaded " + r1.error + " " + r2.error); yield break; }
            var tex = DownloadHandlerTexture.GetContent(r2); tex.wrapMode = TextureWrapMode.Repeat; tex.filterMode = FilterMode.Bilinear;
            drg = CineDragon.Load(r1.downloadHandler.data, tex, actorMats["LEATHER2"], DRG_K);
            GlyphLayer.Build(drg.root);
            CineLog("cine: dragon " + drg.nb + " bones, glyphs on the scene " + GlyphLayer.count);
        }

        /// an open piece of the land: the hero stands at H looking along f; 34 m before him and 15 m to each side of the
        /// dragon's place nothing stands in the way
        bool CineArena(StoryLocation L, out Vector3 H, out Vector3 f)
        {
            Vector3 s0 = L.SpawnW; H = s0; f = Vector3.forward; float best = 1e9f; bool ok = false;
            for (int ring = 0; ring <= 40; ring++)
            {
                for (int k = 0; k < Mathf.Max(1, ring * 8); k++)
                {
                    float a0 = k * Mathf.PI * 2 / Mathf.Max(1, ring * 8);
                    var c = s0 + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * ring * 5f;
                    for (int h = 0; h < 12; h++)
                    {
                        float a = h * Mathf.PI / 6; var ff = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)); var sd = new Vector3(ff.z, 0, -ff.x);
                        bool good = true;
                        for (float d = -10; d <= 42 && good; d += 2f)
                        {
                            float w = d < 8 ? 4 : 16;
                            for (float x = -w; x <= w && good; x += 2f) if (!L.Walk(L.ToMap(c + ff * d + sd * x))) good = false;
                        }
                        if (good && ring * 5f < best) { best = ring * 5f; H = c; f = ff; ok = true; }
                    }
                }
                if (ok) break;
            }
            return ok;
        }

        // ---------------- particles ----------------
        void CineFx(float dt, Vector3 ground, Vector3 drgCentre)
        {
            float gy = ground.y;
            for (int i = fireL.Count - 1; i >= 0; i--)
            {
                var q = fireL[i]; q.age += dt;
                if (q.age >= q.life) { fireL[i] = fireL[fireL.Count - 1]; fireL.RemoveAt(fireL.Count - 1); continue; }
                if (q.kind == 0)
                {
                    q.v *= Mathf.Exp(-dt * 1.6f); q.v.y += 3.2f * dt;                     // drag, the hot gas rising
                    q.v += RndSphere() * 9f * dt;                                         // turbulence
                    if (q.p.y < gy + 0.25f && q.v.y < 0)
                    {                                                                      // it hits the ground: spreads, rolls on
                        var hv = new Vector3(q.v.x, 0, q.v.z); q.v = hv * 0.75f + RndSphere().Flat() * hv.magnitude * 0.35f; q.v.y = Mathf.Abs(q.v.y) * 0.1f + 1.2f;
                        q.p.y = gy + 0.25f;
                        if (Rnd() < 0.5f) SmokeAt(q.p + Vector3.up * 0.4f, q.v * 0.3f + Vector3.up * 1.8f, 0.75f);
                    }
                    q.heat = Mathf.Clamp01(1 - q.age / q.life * 1.15f);
                    q.size = 0.11f + 0.26f * Mathf.Sqrt(q.age / q.life);
                    if (q.age > q.life * 0.7f && Rnd() < dt * 6) SmokeAt(q.p, q.v * 0.3f + Vector3.up, 0.8f);
                }
                else
                {
                    q.v *= Mathf.Exp(-dt * 0.8f); q.v.y += 1.2f * dt; q.v += RndSphere() * 3f * dt;
                    q.heat = Mathf.Clamp01(1 - q.age / q.life);
                }
                q.p += q.v * dt; fireL[i] = q;
            }
            for (int i = smokeL.Count - 1; i >= 0; i--)
            {
                var q = smokeL[i]; q.age += dt;
                if (q.age >= q.life) { smokeL[i] = smokeL[smokeL.Count - 1]; smokeL.RemoveAt(smokeL.Count - 1); continue; }
                q.v *= Mathf.Exp(-dt * (q.kind == 3 ? 1.6f : 0.9f));
                q.v.y += (q.kind == 3 ? -0.6f : 0.9f) * dt; q.v += RndSphere() * 1.4f * dt + new Vector3(0.35f, 0, 0.15f) * dt;      // (a light wind)
                if (q.p.y < gy + 0.1f) { q.p.y = gy + 0.1f; q.v.y = Mathf.Abs(q.v.y) * 0.3f; }
                q.p += q.v * dt; q.size += dt * (q.kind == 3 ? 0.1f : 0.16f); smokeL[i] = q;
            }
        }
        void FireAt(Vector3 p, Vector3 v, float life, int kind = 0) { fireL.Add(new Part { p = p, v = v, life = life, kind = kind, size = 0.13f, heat = 1 }); }
        void SmokeAt(Vector3 p, Vector3 v, float sz, int kind = 2, float life = -1)
        {
            if (smokeL.Count > 6800) return;
            smokeL.Add(new Part { p = p + RndSphere() * 0.3f, v = v, life = life > 0 ? life : 3.2f + Rnd() * 2.6f, kind = kind, size = sz * (0.7f + Rnd() * 0.6f) });
        }
        void CineFxDraw(Vector3 eye, float fireGlow)
        {
            fireP.Begin();
            foreach (var q in fireL)
            {
                float h = q.heat; Color c;
                if (q.kind == 1) { c = new Color(1.0f, 0.55f, 0.15f) * (0.6f + h); fireP.Add(q.p, c, 0.09f, h > 0.5f ? 2 : 1, 9); continue; }
                if (h > 0.66f) c = Color.Lerp(new Color(1.0f, 0.55f, 0.12f), new Color(1.0f, 0.86f, 0.45f), (h - 0.66f) / 0.34f);
                else if (h > 0.3f) c = Color.Lerp(new Color(0.75f, 0.16f, 0.03f), new Color(1.0f, 0.55f, 0.12f), (h - 0.3f) / 0.36f);
                else c = Color.Lerp(new Color(0.14f, 0.03f, 0.01f), new Color(0.75f, 0.16f, 0.03f), h / 0.3f);
                int g = Mathf.Clamp(Mathf.RoundToInt(2 + h * 10 + Rnd() * 2), 1, 15);
                fireP.Add(q.p, c, q.size, g, 9);
            }
            fireP.End();
            // the smoke from the farthest to the nearest (each laid over what is behind it)
            smokeL.Sort((a, b) => (b.p - eye).sqrMagnitude.CompareTo((a.p - eye).sqrMagnitude));
            smokeP.Begin();
            foreach (var q in smokeL)
            {
                float u = q.age / q.life;
                float op = Mathf.Clamp01(u * 6) * Mathf.Clamp01((1 - u) / 0.45f);
                Color c = q.kind == 3 ? new Color(0.55f, 0.47f, 0.36f) : new Color(0.42f, 0.40f, 0.39f);
                c += new Color(0.9f, 0.35f, 0.08f) * fireGlow * Mathf.Clamp01(1 - u * 1.8f);           // lit from below by the fire
                int g = Mathf.Clamp(Mathf.RoundToInt(3 + op * 10), 1, 15);
                smokeP.Add(q.p, c, q.size, g, 4, op * (q.kind == 3 ? 0.75f : 0.95f));
            }
            smokeP.End();
        }

        // ---------------- the scene ----------------
        static float Smooth(float a, float b, float t) { t = Mathf.Clamp01((t - a) / (b - a)); return t * t * (3 - 2 * t); }
        static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u) { float v = 1 - u; return v * v * v * a + 3 * v * v * u * b + 3 * v * u * u * c + u * u * u * d; }

        IEnumerator CineDragonScene()
        {
            yield return CineLoadDragon(); if (drg == null) yield break;
            var L = sMain;
            if (!CineArena(L, out var H, out var f)) CineLog("cine: no open place found, using the spawn");
            var side = new Vector3(f.z, 0, -f.x);
            CineLog("cine: arena " + H + " f " + f + " biome " + L.BiomeAtW(H) + " / " + L.BiomeAtW(H + f * 20));
            float heading = Mathf.Atan2(f.x, f.z);
            StoryGo(L, H, heading); pos = H;
            // late afternoon, clear: the low warm sun (the sky eases into it over the frames before the recording)
            gameClock = Sky.DAY_S * 0.455f; wxMain.auto = false; wxMain.Stop(); wxMain.wet = 0; wxMain.puddle = 0;
            // nothing standing in the arena: the trees and bushes there are put away (their characters with them)
            {
                int hid = 0; var sideH = new Vector3(f.z, 0, -f.x);
                foreach (var r in L.root.GetComponentsInChildren<Renderer>(false))
                {
                    var nm = r.gameObject.name; if (nm == "Ground" || nm == "Grass" || nm == "Path" || nm == "Glyphs" || r.gameObject.layer == GlyphLayer.LAYER) continue;
                    var b = r.bounds; if (b.size.x > 25 || b.size.z > 25) continue;
                    var d = b.center - H; float along = Vector3.Dot(d, f), across = Mathf.Abs(Vector3.Dot(d, sideH));
                    float half = Mathf.Max(b.extents.x, b.extents.z);
                    bool inArena = along > -14 - half && along < 50 + half && across < (along < 6 ? 7 : 20) + half;
                    bool inSkyShot = (b.center - (H + f * 52 + sideH * 26)).magnitude < 9 + half;        // (the first shot's camera)
                    if (inArena || inSkyShot) { r.gameObject.SetActive(false); hid++; }
                }
                CineLog("cine: put away " + hid + " things from the arena");
            }
            for (int i = 0; i < 150; i++) { pos = H; player.heading = heading; Shader.SetGlobalFloat("_VisOn", 0); yield return null; }      // (5 s for the light to settle; nothing recorded)
            CineLog("cine: sky tod " + skyS.tod + " [" + string.Join(",", skyS.todT) + "] sunK " + skyS.sunK + " ambK " + skyS.ambK + " exp " + ascii.exposure + " clock " + gameClock + " outdoor " + L.outdoor + " moon " + (moonLight != null ? moonLight.intensity + " " + moonLight.enabled : "none"));
            SetShadowDistance(90);
            var D = H + f * 26f;                                   // where the dragon lands (its pelvis over this point)
            float gy = H.y; var ground = new Vector3(0, gy, 0);
            fireP = new CineParticles(9000); smokeP = new CineParticles(7000, true);
            fireLight = new GameObject("FireLight").AddComponent<Light>(); fireLight.type = LightType.Point; fireLight.range = 30; fireLight.intensity = 0; fireLight.color = new Color(1f, 0.55f, 0.2f); fireLight.shadows = LightShadows.None;
            int pel = drg.Bone("pelvis"), fb = drg.Bone("fireBreath"), jaw = drg.Bone("jaw_01"), head = drg.Bone("head");
            int n4 = drg.Bone("neck_04"), n8 = drg.Bone("neck_08");
            float idlePel = drg.animP["idle"][pel].y;               // (its height over the feet, already in metres)
            // flight: from far beyond the landing place, high, towards it (and towards the hero)
            var P0 = D + f * 140 - side * 70 + Vector3.up * 62;
            var P1 = D + f * 34 + side * 6 + Vector3.up * 30;
            const float T_A = 4.5f, T_LAND = 9.5f, T_FIRE0 = 10.9f, T_FIRE1 = 13.4f, T_PULL0 = 15.6f, T_PULL1 = 20.0f, T_STANCE = 18.6f, T_END = 23f;
            float fps = CineFps(); float dt = 1f / fps;
            cineCamOn = true; cineRec = true;
            float t = 0, idleT = 0, flyT = 0; Vector3 prevC = P0, shakeK = Vector3.zero; bool landed = false;
            prevFace = (P1 - P0).Flat().normalized;
            while (t < T_END)
            {
                // ---- the dragon's body ----
                Vector3 C; Quaternion rot; float w;
                if (t < T_A)
                {
                    float u = t / T_A; C = Vector3.Lerp(P0, P1, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 6;
                    w = 0;
                }
                else
                {
                    float u = Mathf.Clamp01((t - T_A) / (T_LAND - T_A)); float s = 1 - (1 - u) * (1 - u) * (1 - u * 0.35f);
                    var v1 = (P1 - P0).normalized * 30f;
                    C = Bez(P1, P1 + v1 * 1.1f * 0.3f + Vector3.down * 2, D + f * 7 + Vector3.up * 14, D + Vector3.up * idlePel, Mathf.Clamp01(s));
                    w = Smooth(0.62f, 1.0f, u);
                }
                var vel = (C - prevC) / dt; if (t == 0) vel = (P1 - P0) / T_A;
                var hv = new Vector3(vel.x, 0, vel.z);
                Vector3 faceDir = Vector3.Slerp(hv.sqrMagnitude > 1 ? hv.normalized : -f, -f, Smooth(T_A, T_LAND - 0.8f, t));
                float bank = t < T_LAND ? Mathf.Clamp(Vector3.Dot(Vector3.Cross(prevFace, faceDir), Vector3.up) * 900f, -28, 28) : 0;
                float pitch = t < T_LAND ? Mathf.Clamp(-vel.y * 0.9f, -14, 14) * (1 - w) : 0;
                rot = Quaternion.LookRotation(-faceDir) * Quaternion.Euler(-pitch, 0, -bank);
                prevFace = faceDir;
                // the flying loop slows into the landing; then the idle
                flyT += dt * (1 - 0.35f * w); if (w > 0) idleT += dt;
                drg.Pose("flying", flyT, "idle", idleT, w);
                // head and jaw: the breath
                float raise = Smooth(T_FIRE0 - 1.1f, T_FIRE0 - 0.35f, t) * (1 - Smooth(T_FIRE0 - 0.35f, T_FIRE0, t));     // draws back
                float down = Smooth(T_FIRE0 - 0.35f, T_FIRE0, t) * (1 - Smooth(T_FIRE1, T_FIRE1 + 0.9f, t));                // throws it forward and down
                float pitchNeck = raise * 16 - down * 17;
                drg.Turn(n4, Quaternion.AngleAxis(pitchNeck * 0.35f, Vector3.right));
                drg.Turn(n8, Quaternion.AngleAxis(pitchNeck * 0.35f, Vector3.right));
                drg.Turn(head, Quaternion.AngleAxis(pitchNeck * 0.3f, Vector3.right));
                float open = raise * 10 + down * 30 + Smooth(T_PULL0 + 1.5f, T_PULL0 + 2.0f, t) * (1 - Smooth(T_PULL0 + 3.2f, T_PULL0 + 3.8f, t)) * 22;   // (a growl at the knight)
                drg.Turn(jaw, Quaternion.AngleAxis(-open, Vector3.right));
                drg.Apply();
                // the pelvis over C
                drg.root.transform.rotation = rot;
                var pelW = rot * drg.BoneP(pel);
                drg.root.transform.position = C - pelW;
                if (!landed && t >= T_LAND - 0.15f)
                {
                    landed = true; shakeK = new Vector3(0.5f, 0.6f, 0);
                    for (int i = 0; i < 900; i++)
                    {
                        float a = Rnd() * Mathf.PI * 2; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        var at = new Vector3(D.x, gy, D.z) + dir * (2.5f + Rnd() * 4.5f);
                        SmokeAt(at + Vector3.up * 0.2f, dir * (5 + Rnd() * 9) + Vector3.up * (0.5f + Rnd() * 2), 0.4f, 3, 1.6f + Rnd() * 1.6f);
                    }
                }
                // ---- the breath ----
                var mouthDir = rot * (drg.BoneR(fb) * Vector3.right);
                var mouth = drg.root.transform.position + rot * drg.BoneP(fb) + mouthDir * 0.085f * DRG_K;
                float glow = 0;
                if (t > T_FIRE0 && t < T_FIRE1)
                {
                    float ramp = Smooth(T_FIRE0, T_FIRE0 + 0.25f, t) * (1 - Smooth(T_FIRE1 - 0.3f, T_FIRE1, t));
                    int nEmit = Mathf.RoundToInt(300 * ramp * dt * 30);
                    for (int i = 0; i < nEmit; i++)
                    {
                        var d = (mouthDir + RndSphere() * 0.09f).normalized;
                        FireAt(mouth + d * Rnd() * 0.8f, d * (24 + Rnd() * 8), 0.75f + Rnd() * 0.55f);
                    }
                    glow = ramp;
                }
                // what still burns on the ground, the embers
                float after = Smooth(T_FIRE0 + 0.6f, T_FIRE0 + 1.0f, t) * (1 - Smooth(T_FIRE1 + 0.3f, T_PULL0 + 2.5f, t));
                for (int i = 0; i < Mathf.RoundToInt(28 * after * dt * 30); i++)
                {
                    var at = new Vector3(D.x, gy, D.z) - f * (10 + Rnd() * 8) + side * (Rnd() * 2 - 1) * (2 + Rnd() * 3.5f);
                    FireAt(at + Vector3.up * 0.2f, Vector3.up * (1.5f + Rnd() * 2) + RndSphere() * 0.8f, 0.4f + Rnd() * 0.5f);
                    if (Rnd() < 0.35f) FireAt(at + Vector3.up * 0.5f, Vector3.up * (2 + Rnd() * 3) + RndSphere() * 1.5f, 1.5f + Rnd(), 1);
                    if (Rnd() < 0.4f) SmokeAt(at + Vector3.up * 1f, Vector3.up * 1.6f + RndSphere() * 0.6f, 0.45f);
                }
                glow = Mathf.Max(glow, after * 0.45f);
                CineFx(dt, ground, C);
                if (Mathf.Repeat(t, 1f) < dt * 0.999f && t > T_LAND)
                {
                    Vector3 mn = Vector3.one * 1e9f, mx = -mn; foreach (var q in fireL) { mn = Vector3.Min(mn, q.p); mx = Vector3.Max(mx, q.p); }
                    CineLog("cine: t " + t.ToString("0.0") + " mouth " + (mouth - H) + " dir " + mouthDir + " fire " + fireL.Count + " box " + (mn - H) + ".." + (mx - H) + " smoke " + smokeL.Count + " D-H " + (D - H) + " root " + (drg.root.transform.position - H));
                }
                fireLight.transform.position = new Vector3(D.x, gy + 2.5f, D.z) - f * 9;
                fireLight.intensity = glow * (5f + 1.5f * Mathf.PerlinNoise(t * 9, 0.3f));

                // ---- the hero: still, then the stance ----
                pos = H; player.heading = heading;
                if (t >= T_STANCE) player.combatT = MotionActor.COMBAT_HOLD;
                if (Mathf.Abs(t - T_PULL1) < dt * 0.5f)
                {
                    int on = 0; foreach (var r in hero.go.GetComponentsInChildren<Renderer>(false)) if (r.enabled) on++;
                    CineLog("cine: hero at " + hero.go.transform.position + " active " + hero.go.activeInHierarchy + " renderers on " + on + " pos " + pos + " H " + H);
                }

                // ---- the camera ----
                Vector3 eye, at2; float fov;
                if (t < T_A)
                {   // low on the field, looking up as it comes over
                    float u = t / T_A;
                    eye = D + f * 30 + side * 26 + Vector3.up * 1.6f;
                    at2 = Vector3.Lerp(C, C + vel * 0.25f, 0.5f); fov = Mathf.Lerp(34, 46, u);
                }
                else if (t < T_LAND + 1.3f)
                {   // wide, from the side: the descent, the landing
                    float u = Mathf.Clamp01((t - T_A) / (T_LAND + 1.3f - T_A));
                    eye = D + side * (44 - u * 6) - f * 4 + Vector3.up * (4.5f - u * 2.5f);
                    at2 = Vector3.Lerp(C, D + Vector3.up * 4, Smooth(0.6f, 1f, u)); fov = 40;
                }
                else if (t < T_FIRE1 + 0.25f)
                {   // three quarters from the side, low: the breath crosses the frame and hits the ground
                    float u = Mathf.Clamp01((t - T_LAND - 1.3f) / (T_FIRE1 + 0.25f - T_LAND - 1.3f));
                    eye = D - f * (8.5f + u * 1.0f) + side * (29f - u * 2.0f) + Vector3.up * (1.6f + u * 0.3f);
                    at2 = new Vector3(D.x, gy, D.z) - f * 7.5f + Vector3.up * 3.0f; fov = 50;
                }
                else if (t < T_PULL0)
                {   // in front, low, before the jaws, behind the smoke (the hero is behind the camera)
                    float u = Mathf.Clamp01((t - T_LAND - 1.3f) / (T_PULL0 - T_LAND - 1.3f));
                    eye = D - f * (20f - u * 1.0f) + side * 1.8f + Vector3.up * (1.25f + u * 0.2f);
                    at2 = new Vector3(D.x, gy, D.z) + Vector3.up * (3.0f - u * 0.5f) - f * 4; fov = 44;
                }
                else
                {   // the pull back: over the knight's head to behind his shoulder
                    float u = Smooth(T_PULL0, T_PULL1, t);
                    var e0 = D - f * 19f + side * 1.8f + Vector3.up * 1.45f;
                    var e1 = H - f * 4.3f + side * 1.15f + Vector3.up * 1.95f;
                    eye = Vector3.Lerp(e0, e1, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 1.4f;
                    at2 = Vector3.Lerp(new Vector3(D.x, gy, D.z) + Vector3.up * 2.5f - f * 4, new Vector3(D.x, gy, D.z) + Vector3.up * 3.4f - f * 6, u);
                    fov = Mathf.Lerp(44, 40, u);
                    eye += side * Mathf.Sin(t * 0.7f) * 0.05f + Vector3.up * Mathf.Sin(t * 0.9f) * 0.04f;     // (a hand-held drift)
                }
                // the ground shakes
                shakeK *= Mathf.Exp(-dt * 4.5f);
                eye += new Vector3(Mathf.PerlinNoise(t * 23, 1) - 0.5f, Mathf.PerlinNoise(t * 21, 2) - 0.5f, 0) * shakeK.y;
                cineEye = eye; cineAt = at2; cineFov = fov; cineRoll = (Mathf.PerlinNoise(t * 19, 3) - 0.5f) * shakeK.x * 6;
                CineFxDraw(eye, glow);
                Shader.SetGlobalFloat("_VisOn", 0);          // (no fog of war: the whole field in view)
                prevC = C; t += dt;
                yield return null;
            }
            cineRec = false;
        }
        Vector3 prevFace = Vector3.forward;

        static float CineFps()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string u = Application.absoluteURL; int i = u.IndexOf("fps=");
            if (i >= 0) { string s = u.Substring(i + 4); int a = s.IndexOfAny(new[] { '&', '#' }); if (a >= 0) s = s.Substring(0, a); if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0) return v; }
#endif
            return 30;
        }
    }

    static class V3Ext { public static Vector3 Flat(this Vector3 v) { v.y = 0; return v; } }
}
