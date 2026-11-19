// The story's weather in the game (Weather.cs holds the rules): it drives the sky, lays its state out for the shaders,
// keeps the trail trodden into the snow, and answers the feet - a splash and rings in a puddle, a kick of snow and
// sinking into a drift (slower in deep snow), sliding on ice.
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public partial class Game
    {
        readonly Weather wxMain = new Weather(), wxMount = new Weather { mountain = true };
        /// the weather where the hero is: the snowy mountains of the test world have their own
        Weather wx => !story && loc is Mockup mk && mk.kind == "snow" ? wxMount : wxMain;
        float precipS = 24, lakeIce;

        /// each frame of the story (outside: under the sky; in a cave or an inn the weather goes on outside)
        void StoryWeather(float dt, bool outside) { WeatherFrame(dt, outside); }
        /// each frame in a world with a sky (the story's or the test world's)
        void WeatherFrame(float dt, bool outside)
        {
            var w = wx;
            wxMain.Update(dt, skyS.season, skyS.tod, outside && w == wxMain);
            if (!story) wxMount.Update(dt, skyS.season, skyS.tod, outside && w == wxMount);
            skyS.precip = w.Falling; skyS.level = Mathf.Max(0, w.level); skyS.fallSet = w.snowFalls ? 1 : 0;
            skyS.wetSet = w.wet; skyS.coverSet = Mathf.Clamp01(w.white * 0.9f);
            // the lakes: in winter the ice creeps in from the shores over three minutes, by spring it thaws
            bool winter = skyS.season == 3;
            lakeIce = Mathf.MoveTowards(lakeIce, winter ? 1 : 0, dt / (winter ? 180f : skyS.IsDay ? 240f : 1e9f));
            if (!story) foreach (var l in outdoor) l.iceK = l is Mockup m && m.kind == "snow" ? 1 : lakeIce;
            if (!outside)
            {
                if (player != null) { player.slip = 0; player.speedK = 1; }
                Shader.SetGlobalVector("_WxGround", new Vector4(-1, -1, -1, 0)); Shader.SetGlobalVector("_WxState", Vector4.zero);
                Shader.SetGlobalVector("_WxActor", Vector4.zero); Shader.SetGlobalVector("_WxTrailRect", Vector4.zero); Shader.SetGlobalVector("_WxIce", Vector4.zero);
                return;
            }
            Shader.SetGlobalVector("_WxGround", new Vector4(Weather.Threshold(w.puddle), Weather.Threshold(w.ice), Weather.Threshold(w.snow), w.DriftH));
            Shader.SetGlobalVector("_WxState", new Vector4(w.wet, skyS.Rain, w.white, 0));
            Shader.SetGlobalVector("_WxActor", new Vector4(w.actorWet, w.actorSnow, 0, 0));
            Shader.SetGlobalVector("_WxIce", new Vector4(loc != null ? loc.iceK : 0, Mathf.Clamp01(w.white), 0, 0));
            Shader.SetGlobalVectorArray("_WaterRip", ripples);
            Trail(dt);
            // the hero's footing
            if (player != null)
            {
                float depth; int s = w.SurfaceAt(pos, TrailAt(pos), out depth);
                bool ice = (s == 2 || loc != null && loc.FrozenW(pos)) && w.snow < 0.15f;
                player.slip = Mathf.MoveTowards(player.slip, ice ? 1 : 0, dt * 3);
                player.speedK = depth > 0.25f ? 1 - 0.3f * Mathf.Clamp01((depth - 0.25f) / 0.65f) : 1;
            }
            // the box the rain and snow fall through: 120 % of what the camera sees, wider on a run or a dash, pushed
            // upwind so the windward edge is never empty
            if (sceneCam != null)
            {
                float camD = (sceneCam.transform.position - camTgt).magnitude, vh = 2 * camD * Mathf.Tan(sceneCam.fieldOfView * 0.5f * Mathf.Deg2Rad), vw = vh * sceneCam.aspect;
                bool fast = player != null && (player.Velocity.magnitude > 4.5f || dashT < DASH_T);
                float S = Mathf.Clamp(Mathf.Max(vw, vh) * (fast ? 1.45f : 1.2f), 12, 40);
                precipS += (S - precipS) * Mathf.Min(1, dt * 3);
                var wd = new Vector2(Mathf.Cos(windA), Mathf.Sin(windA));
                Vector2 c = new Vector2(camTgt.x, camTgt.z) - wd * (0.35f * vw * Mathf.Clamp01(windStr));
                Shader.SetGlobalVector("_PrecipBox", new Vector4(c.x, 0, c.y, precipS));
            }
            WxDrops(dt);
        }
        /// the wind follows the weather: stronger while it falls, a storm blows past 80 %
        float WeatherWind(float target)
        {
            var w = wx;
            return Mathf.Max(target, w.Falling ? 0.25f + 0.12f * wx.level : 0, w.storm * (0.88f + 0.12f * Mathf.Sin(tNow * 0.8f)));
        }
        void WeatherOff()
        {
            skyS.fallSet = -1; skyS.wetSet = -1; skyS.coverSet = -1; skyS.precip = false;
            Shader.SetGlobalVector("_WxGround", new Vector4(-1, -1, -1, 0)); Shader.SetGlobalVector("_WxState", Vector4.zero);
            Shader.SetGlobalVector("_WxActor", Vector4.zero); Shader.SetGlobalVector("_WxTrailRect", Vector4.zero); Shader.SetGlobalVector("_WxIce", Vector4.zero);
            if (player != null) { player.slip = 0; player.speedK = 1; }
        }
        /// the world is entered: the lakes as the season has them (frozen in winter)
        void WeatherEnter() { lakeIce = skyS.season == 3 ? 1 : 0; }
        /// a creature's footing (rats slide on the ice and wade through drifts too)
        void FoeFooting(MotionActor a, Vector3 p)
        {
            if (wx.ice <= 0 && wx.snow <= 0) { a.slip = 0; a.speedK = 1; return; }
            float depth; int s = wx.SurfaceAt(p, TrailAt(p), out depth);
            a.slip = s == 2 && wx.snow < 0.15f ? 1 : 0;
            a.speedK = depth > 0.25f ? 1 - 0.3f * Mathf.Clamp01((depth - 0.25f) / 0.65f) : 1;
        }

        // ---- the trail in the snow: a 32 m square of 0.25 m cells round the hero, pressed by the feet, filled again
        // by falling snow ----
        const int TR_N = 128; const float TR_C = 0.25f;
        Texture2D trTex; readonly byte[] trBuf = new byte[TR_N * TR_N]; Vector2 trO; bool trDirty; float trUpT, trFillT;
        float TrailAt(Vector3 p)
        {
            if (trTex == null) return 0;
            int x = Mathf.FloorToInt((p.x - trO.x) / TR_C), y = Mathf.FloorToInt((p.z - trO.y) / TR_C);
            return x < 0 || y < 0 || x >= TR_N || y >= TR_N ? 0 : trBuf[y * TR_N + x] / 255f;
        }
        void TrailStamp(Vector3 p, float r)
        {
            int cx = Mathf.FloorToInt((p.x - trO.x) / TR_C), cy = Mathf.FloorToInt((p.z - trO.y) / TR_C), rr = Mathf.CeilToInt(r / TR_C);
            for (int y = cy - rr; y <= cy + rr; y++)
                for (int x = cx - rr; x <= cx + rr; x++)
                {
                    if (x < 0 || y < 0 || x >= TR_N || y >= TR_N) continue;
                    float d = new Vector2(x + 0.5f - cx - 0.5f, y + 0.5f - cy - 0.5f).magnitude * TR_C / r; if (d > 1) continue;
                    byte v = (byte)(255 * Mathf.Clamp01(1.3f - d)); int i = y * TR_N + x;
                    if (trBuf[i] < v) { trBuf[i] = v; trDirty = true; }
                }
        }
        void Trail(float dt)
        {
            if (wx.snow <= 0.002f && wx.white <= 0.01f)
            {
                if (trTex != null && trDirty) { System.Array.Clear(trBuf, 0, trBuf.Length); trTex.SetPixelData(trBuf, 0); trTex.Apply(false); trDirty = false; }
                Shader.SetGlobalVector("_WxTrailRect", Vector4.zero); return;
            }
            if (trTex == null)
            {
                trTex = new Texture2D(TR_N, TR_N, TextureFormat.R8, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "SnowTrail" };
                trO = new Vector2(Mathf.Floor(pos.x / TR_C) * TR_C - TR_N * TR_C / 2, Mathf.Floor(pos.z / TR_C) * TR_C - TR_N * TR_C / 2); trDirty = true;
            }
            // the square follows the hero (moved by whole cells, the trail kept where it lies)
            Vector2 cen = trO + Vector2.one * (TR_N * TR_C / 2);
            if (Mathf.Abs(pos.x - cen.x) > 4 || Mathf.Abs(pos.z - cen.y) > 4)
            {
                var nO = new Vector2(Mathf.Floor(pos.x / TR_C) * TR_C - TR_N * TR_C / 2, Mathf.Floor(pos.z / TR_C) * TR_C - TR_N * TR_C / 2);
                int dx = Mathf.RoundToInt((nO.x - trO.x) / TR_C), dy = Mathf.RoundToInt((nO.y - trO.y) / TR_C);
                var old = (byte[])trBuf.Clone();
                for (int y = 0; y < TR_N; y++) for (int x = 0; x < TR_N; x++) { int ox = x + dx, oy = y + dy; trBuf[y * TR_N + x] = ox < 0 || oy < 0 || ox >= TR_N || oy >= TR_N ? (byte)0 : old[oy * TR_N + ox]; }
                trO = nO; trDirty = true;
            }
            // pressed by the hero's feet (as they touch the ground) and by the creatures walking about
            if (hero != null && player != null)
            {
                var hb = hero.human;
                for (int si = 0; si < 2; si++)
                {
                    var fb = hb.Bone(si == 0 ? "foot.L" : "foot.R");
                    if (fb != null && limbs[3 + si] && player.FootLift(si) < 0.07f) TrailStamp(fb.position, 0.2f);
                }
            }
            var L = loc as StoryLocation;
            foreach (var f in foes) if (f.L == L && !f.Dead && f.ch.go.activeSelf && f.act.Velocity.magnitude > 0.2f) TrailStamp(f.p, 0.26f);
            // falling snow fills it in again (in 2 minutes when light, in half a minute when heavy)
            trFillT -= dt;
            if (trFillT <= 0)
            {
                trFillT = 0.5f;
                if (wx.Snowing)
                {
                    int dec = Mathf.Max(1, Mathf.RoundToInt(255 * 0.5f / (wx.level == 2 ? 30f : wx.level == 1 ? 60f : 120f)));
                    for (int i = 0; i < trBuf.Length; i++) if (trBuf[i] > 0) { trBuf[i] = (byte)Mathf.Max(0, trBuf[i] - dec); trDirty = true; }
                }
            }
            trUpT -= dt;
            if (trDirty && trUpT <= 0) { trTex.SetPixelData(trBuf, 0); trTex.Apply(false); trDirty = false; trUpT = 0.08f; }
            Shader.SetGlobalTexture("_WxTrail", trTex);
            Shader.SetGlobalVector("_WxTrailRect", new Vector4(trO.x, trO.y, 1f / (TR_N * TR_C), 1));
        }

        // ---- splashes from the puddles, snow kicked up by a run ----
        readonly List<Drop> wxDrops = new List<Drop>(), wxFlakes = new List<Drop>(); int wxDi, wxFi; GameObject wxRoot;
        void WxSpray(bool snow, float sp)
        {
            if (wxRoot == null)
            {
                wxRoot = new GameObject("WeatherSpray");
                var wm = new Material(mats["WATER"]) { name = "PuddleSpray" }; wm.color = new Color(0.62f, 0.72f, 0.82f); wm.SetColor("_EmissionColor", new Color(0.16f, 0.2f, 0.26f));
                for (int i = 0; i < 32; i++)
                {
                    var go = MeshGen.Obj(MeshGen.Sphere(0.028f, 6, 4), wm, wxRoot.transform, Vector3.zero, Quaternion.identity, false, "Drop"); GlyphLayer.Build(go); go.SetActive(false); wxDrops.Add(new Drop { go = go });
                    var g2 = MeshGen.Obj(MeshGen.Sphere(0.035f, 6, 4), mats["SNOW"], wxRoot.transform, Vector3.zero, Quaternion.identity, false, "Flake"); GlyphLayer.Build(g2); g2.SetActive(false); wxFlakes.Add(new Drop { go = g2 });
                }
            }
            var hv = player.Velocity; Vector3 fw = hv.sqrMagnitude > 0.01f ? new Vector3(hv.x, 0, hv.z).normalized : new Vector3(Mathf.Sin(player.heading), 0, Mathf.Cos(player.heading)), side = new Vector3(fw.z, 0, -fw.x);
            var list = snow ? wxFlakes : wxDrops; int n = snow ? 5 : 4;
            for (int k = 0; k < n; k++)
            {
                var d = list[snow ? wxFi : wxDi]; if (snow) wxFi = (wxFi + 1) % list.Count; else wxDi = (wxDi + 1) % list.Count;
                float lr = Random.Range(-1f, 1f);
                d.go.transform.position = pos + side * lr * 0.15f + Vector3.up * 0.05f;
                // thrown back, away from the way he runs
                d.v = -fw * Random.Range(0.6f, 1.4f) * (0.5f + 0.15f * sp) + side * lr * Random.Range(0.3f, 0.9f) + Vector3.up * Random.Range(snow ? 1.0f : 1.4f, snow ? 2.0f : 2.6f);
                d.go.SetActive(true);
            }
            if (!snow) { ripples[rippleIx] = new Vector4(pos.x, pos.z, tNow, 1.2f); rippleIx = (rippleIx + 1) % ripples.Length; }
        }
        void WxDrops(float dt)
        {
            foreach (var l in new[] { wxDrops, wxFlakes })
                foreach (var d in l)
                {
                    if (!d.go.activeSelf) continue;
                    d.v.y -= (l == wxFlakes ? 6f : 9.8f) * dt; var q = d.go.transform.position + d.v * dt; d.go.transform.position = q;
                    if (q.y < -0.02f) d.go.SetActive(false);
                }
        }
        /// what the feet step on out on the meadow
        string WeatherSurface()
        {
            float depth; var w = wx; int s = w.SurfaceAt(pos, TrailAt(pos), out depth);
            if (loc != null && loc.FrozenW(pos)) return w.snow > 0.15f ? "snow" : "stone";
            return s == 1 ? "puddle" : s == 2 ? "stone" : s == 3 ? "snow" : "grass";
        }

        // ---- the developer's sky buttons in the story: they move the clock, start and stop the weather ----
        void StorySetSeason(int i)
        {
            int s0 = (1 + GameDay / Sky.SEASON_DAYS) % 4; int ds = (i - s0 + 4) % 4;
            gameClock += ds * Sky.SEASON_DAYS * Sky.DAY_S;
        }
        void StorySetTod(int i)
        {
            float[] key = { 0.12f, 0.35f, 0.62f, 0.85f }; float lf = Sky.LIGHT_S / Sky.DAY_S, kp = key[Mathf.Clamp(i, 0, 3)];
            float ph = kp < 0.667f ? kp / 0.667f * lf : lf + (kp - 0.667f) / 0.333f * (1 - lf);
            float day = Mathf.Floor(gameClock / Sky.DAY_S); float t = (day + ph) * Sky.DAY_S; if (t < gameClock) t += Sky.DAY_S;
            gameClock = t;
        }
        void WeatherDevRow(float x1, ref float y, float bw, float bh, float gap)
        {
            if (Button(new Rect(x1 - 3 * (bw + gap) + gap, y, bw, bh), "Накопить", false))
            {   // what lies after a long fall, at once (to look at it without waiting)
                if (wx.snowFalls || skyS.season == 3) { wx.snow = 0.4f; wx.white = 1; } else { wx.puddle = 0.4f; wx.wet = 1; }
            }
            if (Button(new Rect(x1 - 2 * (bw + gap) + gap, y, bw, bh), "Сухо", false)) { wx.puddle = wx.snow = wx.ice = wx.wet = wx.white = 0; }
            if (Button(new Rect(x1 - 1 * (bw + gap) + gap, y, bw, bh), wx.auto ? "[x] Само" : "[ ] Само", wx.auto)) wx.auto = !wx.auto;
            y += bh + gap;
        }
    }
}
