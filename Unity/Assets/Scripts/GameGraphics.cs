// Graphics presets: what each of Low / Medium / High sets, which one suits this phone (chosen on its own on the first
// launch and after each update, and the player is told), and what the load test recommends at its end (apply it or
// keep what is set).
//   Low    - for phones like the Redmi Note 8 Pro (Mali-G76): the scenery's characters laid out 1.45x sparser (about half
//            as many, each as much larger), thinned out sooner in the distance, half the grass, 67 % resolution, no
//            glow, no outlines, 30 frames a second (steady rather than jumping between 20 and 35)
//   Medium - scenery 1.15x sparser, 75 % grass, 75 % resolution, 60 frames a second
//   High   - everything as designed (A55 and up: 60 everywhere in the load test)
using System.Text.RegularExpressions;
using UnityEngine;

namespace AsciiHero
{
    public partial class Game
    {
        static readonly float[] SCENERY_K = { 1.45f, 1.15f, 1f };
        float GlyphFar => setQuality == 0 ? GLYPH_FAR * 0.65f : GLYPH_FAR;
        bool worldBuilt;        // (the scenery's density is laid out when the world is built: a change shows from the next start)

        /// a preset over all the graphics settings
        void ApplyPreset(int p)
        {
            setQuality = Mathf.Clamp(p, 0, 2);
            if (setQuality == 0) { setGrass = 2; setRes = 1; setFps = 0; setBloom = false; setOutline = false; }
            else if (setQuality == 1) { setGrass = 3; setRes = 2; setFps = 1; setBloom = true; setOutline = true; }
            else { setGrass = 4; setRes = 3; setFps = 1; setBloom = true; setOutline = true; }
            ApplySettings();
        }
        /// the density of the scenery's characters and how soon they thin out, for the quality set (ApplySettings)
        void ApplyGlyphQuality()
        {
            if (!worldBuilt) GlyphLayer.sceneryK = SCENERY_K[Mathf.Clamp(setQuality, 0, 2)];
            Shader.SetGlobalFloat("_GlyphSceneK", GlyphLayer.sceneryK);
            if (world) Shader.SetGlobalFloat("_GlyphFarRef", GlyphFar);
        }

        /// the preset this device should start on, from its graphics chip and its memory
        static int RecommendPreset(out string why)
        {
            string g = SystemInfo.graphicsDeviceName ?? ""; int ram = SystemInfo.systemMemorySize;
            why = g + ", " + Mathf.RoundToInt(ram / 1024f) + " ГБ";
            if (!Application.isMobilePlatform) return 2;
            int s = 1;
            var m = Regex.Match(g, @"Mali-G(\d+)");
            var a = Regex.Match(g, @"Adreno[^\d]*(\d{3})");
            if (m.Success) { int n = int.Parse(m.Groups[1].Value); s = n < 77 ? 0 : n < 100 ? 1 : n >= 710 ? 2 : 1; }        // G52..G76: 0; G77, G78: 1; G610: 1; G710 and later: 2
            else if (a.Success) { int n = int.Parse(a.Groups[1].Value); s = n < 640 ? 0 : n < 720 ? 1 : 2; }
            else if (g.Contains("Xclipse") || g.Contains("Immortalis")) s = 2;
            else if (g.Contains("PowerVR") || g.Contains("Mali-T")) s = 0;
            if (ram < 4500) s = 0; else if (ram < 6500) s = Mathf.Min(s, 1);
            return s;
        }
        /// on the first launch and after each update: the preset for this phone, and a word about it
        void AutoPreset()
        {
            if (PlayerPrefs.GetString("gfx_auto_ver", "") == VERSION) return;
            bool first = !PlayerPrefs.HasKey("gfx_auto_ver");
            PlayerPrefs.SetString("gfx_auto_ver", VERSION);
            string why; int p = RecommendPreset(out why);
            ApplyPreset(p);
            gfxDlg = new GfxDialog
            {
                title = first ? "Графика настроена" : "Графика настроена заново (обновление)",
                text = "Для этого телефона выбрано качество «" + QUALITY[p] + "»\n(" + why + ").\nИзменить можно в Настройках, там же есть тест нагрузки.",
                ok = "ПОНЯТНО"
            };
        }

        // ---- a dialog over everything: a word about the graphics, or the load test's recommendation ----
        class GfxDialog { public string title, text, ok, apply; public int preset = -1; }
        GfxDialog gfxDlg;
        void GfxDialogUI(float W, float H)
        {
            var d = gfxDlg; float pw = Mathf.Min(560 * k, W - 40 * k), ph = 210 * k;
            var panel = new Rect((W - pw) / 2, (H - ph) / 2, pw, ph); Smoke(panel, false, 0, 0.97f);
            GUI.Label(new Rect(panel.x + 16 * k, panel.y + 12 * k, pw - 32 * k, 24 * k), d.title, sTitle);
            GUI.Label(new Rect(panel.x + 16 * k, panel.y + 42 * k, pw - 32 * k, ph - 100 * k), d.text, sDesc);
            float bw2 = 220 * k, by = panel.yMax - 50 * k;
            if (d.apply != null)
            {
                if (Button(new Rect(panel.x + 20 * k, by, bw2, 36 * k), d.apply, true)) { ApplyPreset(d.preset); if (worldBuilt && d.preset != prevQuality) ShowToast("Плотность символов на пейзаже изменится после перезапуска игры"); gfxDlg = null; }
                if (Button(new Rect(panel.xMax - 20 * k - bw2, by, bw2, 36 * k), d.ok)) gfxDlg = null;
            }
            else if (Button(new Rect(panel.center.x - bw2 / 2, by, bw2, 36 * k), d.ok, true)) gfxDlg = null;
        }
        int prevQuality;

        /// the end of the load test: what to set from what it measured
        void PerfRecommend()
        {
            int n = 0, ok60 = 0, ok30 = 0; var ms = new System.Collections.Generic.List<float>();
            foreach (var r in perfRows) { n++; ms.Add(r.ms); if (r.ms <= 17.6f && r.miss < 12) ok60++; if (r.ms <= 33.5f) ok30++; }
            if (n == 0) return;
            ms.Sort(); float med = ms[ms.Count / 2];
            int cur = setQuality, rec = cur;
            if (ok60 >= n * 0.9f) rec = Mathf.Min(2, cur + 1);              // 60 nearly everywhere: one step up (if there is one)
            else if (med > 45f) rec = 0;                                    // far under 30: the lowest
            else if (ok30 < n * 0.75f) rec = Mathf.Max(0, cur - 1);         // under 30 in a quarter of the places: one step down
            string res = "60 к/с держат " + ok60 + " мест из " + n + ", 30 к/с — " + ok30 + " из " + n + " (обычно " + Mathf.RoundToInt(1000f / med) + " к/с).";
            prevQuality = cur;
            if (rec == cur)
                gfxDlg = new GfxDialog { title = "Тест завершён", text = res + "\nТекущее качество «" + QUALITY[cur] + "» подходит этому телефону.\nЛог для разработчиков открыт: нажми «Копировать».", ok = "ПОНЯТНО" };
            else
                gfxDlg = new GfxDialog
                {
                    title = "Тест завершён: рекомендуем «" + QUALITY[rec] + "»", preset = rec,
                    text = res + "\nСейчас стоит «" + QUALITY[cur] + "». " + (rec < cur ? "На «" + QUALITY[rec] + "» игра пойдёт ровнее." : "Телефон потянет «" + QUALITY[rec] + "»: картинка будет плотнее."),
                    apply = "ПРИМЕНИТЬ", ok = "ОСТАВИТЬ КАК ЕСТЬ"
                };
        }
    }
}
