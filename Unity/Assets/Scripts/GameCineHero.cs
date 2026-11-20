// The cinematic "hero" (?cine=hero&view=front|side|back): the game's own hero (plate, cloak, sword) on the open land,
// with the game's own animations: he stands, the sword low in his hand, the cloak in a wind from in front of him; then
// he takes the fighting stance (the game's two-handed stance, as when a fight starts). The camera from one side.
using System.Collections;
using UnityEngine;

namespace AsciiHero
{
    public partial class Game
    {
        bool cineWindOn; float cineWindStr, cineWindA;
        static string CineParam(string key, string def)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string u = Application.absoluteURL; int i = u.IndexOf(key + "=");
            if (i < 0) return def;
            string s = u.Substring(i + key.Length + 1); int a = s.IndexOfAny(new[] { '&', '#' }); if (a >= 0) s = s.Substring(0, a);
            return s;
#else
            return def;
#endif
        }

        IEnumerator CineHeroScene()
        {
            var L = sMain;
            if (!CineArena(L, out var H, out var f)) CineLog("cine: no open place found, using the spawn");
            float heading = Mathf.Atan2(f.x, f.z);
            StoryGo(L, H, heading); pos = H;
            gameClock = Sky.DAY_S * 0.455f; wxMain.auto = false; wxMain.Stop(); wxMain.wet = 0; wxMain.puddle = 0;
            var side = new Vector3(f.z, 0, -f.x);
            {   // nothing standing round him
                int hid = 0;
                foreach (var r in L.root.GetComponentsInChildren<Renderer>(false))
                {
                    var nm = r.gameObject.name; if (nm == "Ground" || r.transform.parent != null && r.transform.parent.name == "Ground") continue;          // (only the bare ground: the grass would hide his feet)
                    var b = r.bounds; if (b.size.x > 60 || b.size.z > 60) continue;
                    if ((new Vector3(b.center.x, H.y, b.center.z) - H).magnitude < 400) { r.gameObject.SetActive(false); hid++; }      // (an empty field: only the ground)
                }
                CineLog("cine: put away " + hid);
            }
            // the wind: from in front of him, so the cloak streams back
            cineWindOn = true; cineWindA = Mathf.Atan2(-f.z, -f.x);
            string view = CineParam("view", "front");
            const float T_STANCE = 2.7f, T_END = 6.5f;
            float fps = CineFps(), dt = 1f / fps, t = -5f;            // (5 s before the recording: the light eases in, the cloak settles in the wind)
            player.combatT = 0; player.guardHeld = false;
            cineCamOn = true;
            while (t < T_END)
            {
                CineClearFolk(H);
                Shader.SetGlobalFloat("_VisOn", 0);
                if (CineParam("close", "1") == "1") { ascii.actorBoost = 0; Shader.SetGlobalFloat("_AsciiLod", 1); }     // (a close-up: the characters as the dressing room shows them)
                pos = H; player.heading = heading;
                // gusts
                cineWindStr = 0.75f + 0.35f * Mathf.PerlinNoise(t * 0.7f, 0.2f) + 0.25f * Mathf.Sin(t * 2.3f);
                if (t >= T_STANCE) player.combatT = MotionActor.COMBAT_HOLD;
                // the camera
                Vector3 c = H + Vector3.up * 1.0f, eye;
                switch (view)
                {
                    case "side": eye = c + side * 5.6f + Vector3.up * 0.1f; break;
                    case "back": eye = c - f * 5.6f + side * 0.6f + Vector3.up * 0.25f; break;
                    default: eye = c + f * 5.6f + Vector3.up * 0.1f; break;
                }
                cineEye = eye; cineAt = c - Vector3.up * 0.08f; cineFov = 32; cineRoll = 0;
                cineRec = t >= 0;
                if (Mathf.Abs(t) < dt * 0.5f)
                {
                    var sb = new System.Text.StringBuilder("cine: near:");
                    foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                        var b = r.bounds; if ((b.center - H).magnitude > 4 || b.size.magnitude > 60 || b.size.y < 0.9f) continue;
                        var p = r.transform; string path = p.name; for (int k = 0; k < 3 && p.parent != null; k++) { p = p.parent; path = p.name + "/" + path; }
                        sb.Append(" [").Append(path).Append(" ").Append(b.size.ToString("0.0")).Append(" @").Append((b.center - H).ToString("0.0")).Append(" L").Append(r.gameObject.layer).Append("]");
                    }
                    CineLog(sb.ToString());
                }
                if (Mathf.Abs(t) < dt * 0.5f) CineLog("cine: sky tod " + skyS.tod + " sunK " + skyS.sunK + " exp " + ascii.exposure);
                t += dt;
                yield return null;
            }
            cineRec = false; cineWindOn = false;
        }
    }
}
