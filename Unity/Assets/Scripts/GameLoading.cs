// The intro and the loading:
//   - after Unity's own splash the intro plays (Resources/intro.mp4: the helmet's visor slams down, the violet light
//     comes up in its slots), not to be skipped
//   - loading is a bar at the bottom in the middle, in the interface's smoke, with motes rising from it; the bar never
//     jumps: it runs after what is done at a set pace (so a long step makes it run on after it rather than leap),
//     and the screen waits for it to fill. The same bar covers "continue" and the start after the hero is made.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace AsciiHero
{
    public partial class Game
    {
        // ---- the intro ----
        VideoPlayer introVp; RenderTexture introRT; bool introOn;
        IEnumerator PlayIntro()
        {
            VideoClip clip = null;
            try { clip = Resources.Load<VideoClip>("intro"); } catch (System.Exception e) { Debug.LogWarning("Заставка не загрузилась: " + e.Message); }
            if (clip == null) yield break;
            introRT = new RenderTexture((int)clip.width, (int)clip.height, 0) { name = "Intro" }; introRT.Create();
            var go = new GameObject("Intro"); introVp = go.AddComponent<VideoPlayer>();
            introVp.playOnAwake = false; introVp.clip = clip; introVp.renderMode = VideoRenderMode.RenderTexture; introVp.targetTexture = introRT;
            introVp.audioOutputMode = VideoAudioOutputMode.Direct; introVp.isLooping = false; introVp.skipOnDrop = true;
            introVp.Prepare();
            float t0 = Time.realtimeSinceStartup;
            while (!introVp.isPrepared && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            if (!introVp.isPrepared) { Debug.LogWarning("Заставка не подготовилась за 4 с: пропущена"); Destroy(go); yield break; }
            introVp.SetDirectAudioVolume(0, Mathf.Clamp01(setSound / 100f));
            introOn = true; introVp.Play();
            float len = (float)clip.length; t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < len + 1.5f && (introVp.isPlaying || Time.realtimeSinceStartup - t0 < 0.5f)) yield return null;
            introOn = false; Destroy(go); introRT.Release(); introRT = null;
        }
        void IntroGUI(float W, float H)
        {
            Fill(new Rect(0, 0, W, H), Color.black);
            if (introRT == null) return;
            float a = introRT.width / (float)introRT.height, w = W, h = W / a; if (h > H) { h = H; w = H * a; }
            GUI.DrawTexture(new Rect((W - w) / 2, (H - h) / 2, w, h), introRT, ScaleMode.StretchToFill, false);
        }

        // ---- the bar ----
        float loadShown, loadTarget; string loadLabel = "Загрузка"; bool loadBusy; float loadFade;
        class Mote { public Vector2 p, v; public float life, age, size; }
        readonly List<Mote> motes = new List<Mote>(); float moteAcc;
        const float LOAD_PACE = 0.75f;        // (of the bar a second: a full bar takes at least 1.3 s)
        /// the bar runs after what is done (called each frame the bar is shown)
        void LoadStep(float dt)
        {
            dt = Mathf.Min(dt, 1 / 30f);                         // (after a long frame the bar runs on, it does not leap)
            loadShown = Mathf.MoveTowards(loadShown, loadTarget, dt * LOAD_PACE);
        }
        void LoadBar(float W, float H, string label, float p, float alpha = 1)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1 / 20f);
            float pw = Mathf.Min(560 * k, W * 0.52f), ph = 46 * k;
            var panel = new Rect((W - pw) / 2, H - (H - safe.yMax) - ph - 26 * k, pw, ph);
            var old = GUI.color; GUI.color = new Color(1, 1, 1, alpha);
            Smoke(panel, false, 0, 0.95f);
            var bar = new Rect(panel.x + 18 * k, panel.y + ph * 0.62f, pw - 36 * k, Mathf.Max(3, 5 * k));
            GUI.Label(new Rect(panel.x + 18 * k, panel.y + 4 * k, pw - 36 * k, 18 * k), label + "…", sSmall);
            GUI.Label(new Rect(panel.x + 18 * k, panel.y + 4 * k, pw - 36 * k, 18 * k), Mathf.RoundToInt(p * 100) + " %", sRight);
            Fill(bar, new Color(0.16f, 0.13f, 0.24f, 0.9f * alpha));
            float t = Time.unscaledTime, fw = bar.width * Mathf.Clamp01(p);
            // the filled part shimmers, its head glows
            Fill(new Rect(bar.x, bar.y, fw, bar.height), new Color(0.55f + 0.08f * Mathf.Sin(t * 3), 0.38f, 0.95f, alpha));
            if (fw > 2) Fill(new Rect(bar.x + fw - 4 * k, bar.y - 2 * k, 4 * k, bar.height + 4 * k), new Color(0.9f, 0.8f, 1f, 0.9f * alpha));
            // motes: from the head of the bar and, fewer, from along the filled part, rising and drifting off as smoke
            moteAcc += dt * (40 + 30 * Mathf.Abs(loadTarget - loadShown) * 10);
            while (moteAcc >= 1 && motes.Count < 140)
            {
                moteAcc -= 1; bool head = Random.value < 0.6f;
                float x = head ? bar.x + fw + Random.Range(-3f, 2f) * k : bar.x + Random.value * Mathf.Max(1, fw);
                motes.Add(new Mote { p = new Vector2(x, bar.y + Random.Range(-1f, 1f) * k), v = new Vector2(Random.Range(-14f, 6f) * k, -Random.Range(10f, 34f) * k), life = Random.Range(0.6f, 1.4f), size = Random.Range(1.2f, 2.8f) * k });
            }
            if (moteAcc > 1) moteAcc = 1;
            for (int i = motes.Count - 1; i >= 0; i--)
            {
                var m = motes[i]; m.age += dt; if (m.age >= m.life) { motes.RemoveAt(i); continue; }
                m.p += m.v * dt; m.v.x += Mathf.Sin(t * 3 + i) * 6 * k * dt; m.v *= 1 - 0.4f * dt;
                float u = m.age / m.life, a = (1 - u) * (u < 0.15f ? u / 0.15f : 1) * alpha;
                Fill(new Rect(m.p.x - m.size / 2, m.p.y - m.size / 2, m.size, m.size), new Color(0.78f, 0.6f, 1f, 0.85f * a));
            }
            GUI.color = old;
        }
        /// a loading over everything: steps one after another (a frame between them, so the bar is drawn), the bar
        /// running after them; the screen stays until the bar is full, then fades
        void RunLoading(string label, params System.Action[] steps) { if (!loadBusy) StartCoroutine(LoadingCo(label, steps)); }
        IEnumerator LoadingCo(string label, System.Action[] steps)
        {
            loadBusy = true; loadLabel = label; loadShown = 0; loadTarget = 0.06f; loadFade = 1; motes.Clear();
            yield return null; yield return null;            // (the dark and the bar are on the screen before the work begins)
            for (int i = 0; i < steps.Length; i++)
            {
                loadTarget = Mathf.Max(loadTarget, (i + 0.25f) / steps.Length);
                // the bar has caught up with this step before it starts (a long step freezes the frame)
                float w0 = Time.realtimeSinceStartup;
                while (loadShown < loadTarget - 0.01f && Time.realtimeSinceStartup - w0 < 0.6f) yield return null;
                try { steps[i](); } catch (System.Exception e) { Debug.LogException(e); }
                loadTarget = (i + 1f) / steps.Length;
                yield return null;
            }
            while (loadShown < 0.999f) yield return null;
            float f0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - f0 < 0.35f) { loadFade = 1 - (Time.realtimeSinceStartup - f0) / 0.35f; yield return null; }
            loadFade = 0; loadBusy = false;
        }
        void LoadingOverlayGUI(float W, float H)
        {
            if (Event.current.type == EventType.Repaint) LoadStep(Time.unscaledDeltaTime);
            Fill(new Rect(0, 0, W, H), new Color(0.01f, 0.01f, 0.02f, loadFade));
            SmokeLayerDraw();
            LoadBar(W, H, loadLabel, loadShown, loadFade);
        }
    }
}
