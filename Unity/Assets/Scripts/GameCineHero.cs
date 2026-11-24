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
            gameClock = Sky.DAY_S * 0.60f; wxMain.auto = false; wxMain.Stop(); wxMain.wet = 0; wxMain.puddle = 0;
            var side = new Vector3(f.z, 0, -f.x);
            {   // nothing standing round him
                int hid = 0;
                foreach (var r in L.root.GetComponentsInChildren<Renderer>(false))
                {
                    var nm = r.gameObject.name; if (nm == "Ground" || nm == "Grass" || r.transform.parent != null && (r.transform.parent.name == "Ground" || r.transform.parent.name == "Grass")) continue;
                    var b = r.bounds; if (b.size.x > 25 || b.size.z > 25) continue;
                    if ((new Vector3(b.center.x, H.y, b.center.z) - H).magnitude < 6 + Mathf.Max(b.extents.x, b.extents.z)) { r.gameObject.SetActive(false); hid++; }     // (the land as it is: only what stands on him put away)
                }
                CineLog("cine: put away " + hid);
            }
            // the wind: from in front of him, so the cloak streams back
            cineWindOn = true; cineWindA = Mathf.Atan2(-f.z, -f.x);
            string view = CineParam("view", "front");
            const float T_STANCE = 2.7f, T_END = 6.5f;
            float fps = CineFps(), dt = 1f / fps, t = -5f;            // (5 s before the recording: the light eases in, the cloak settles in the wind)
            player.combatT = 0; player.guardHeld = false;
            cineCamOn = true; bool skyMade = false;
            while (t < T_END)
            {
                CineClearFolk(H);
                Shader.SetGlobalFloat("_VisOn", 0);
                if (CineParam("close", "1") == "1") { ascii.actorBoost = 0; Shader.SetGlobalFloat("_AsciiLod", 1); }     // (a close-up: the characters as the dressing room shows them)
                pos = H; player.heading = heading;
                // gusts
                cineWindStr = 1.0f + 0.4f * Mathf.PerlinNoise(t * 0.7f, 0.2f) + 0.3f * Mathf.Sin(t * 2.3f);
                if (t >= T_STANCE) player.combatT = MotionActor.COMBAT_HOLD;
                // the camera
                Vector3 c = H + Vector3.up * 1.0f, eye;
                switch (view)
                {
                    case "side": eye = c + side * 9.5f + Vector3.up * 1.7f; break;
                    case "back": eye = c - f * 9.5f + side * 1.0f + Vector3.up * 1.9f; break;
                    default: eye = c + f * 9.5f + Vector3.up * 1.7f; break;
                }
                if (!skyMade) { skyMade = true; CineSkyInit(eye, (c + Vector3.up * 0.6f) - eye, 30); }
                CineSky(t + 5f, skyS.sunDir.y > 0.02f ? skyS.sunDir.normalized : new Vector3(skyS.sunDir.x, 0.06f, skyS.sunDir.z).normalized);
                cineEye = eye; cineAt = c + Vector3.up * 0.6f; cineFov = 30;          // (the hero low in the frame, the sky above him) cineRoll = 0;
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
