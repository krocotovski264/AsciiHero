// Clips for the story's scenes, separate from the motion-matching base (Resources/cuts.bytes, made by the pipeline
// from CMU motion capture, free to use): waking up (lying on the back, getting up), picking something up from the
// ground, looking at what is in the hands, talking with the hands. Same bones and handedness as the base.
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AsciiHero
{
    public static class CutLib
    {
        public class Clip { public string name; public int a, n; }
        public static readonly Dictionary<string, Clip> clips = new Dictionary<string, Clip>();
        public static int fps = 30, nb; static int[] toDb; static float[] Q, RO; static bool loaded;
        public static void Load()
        {
            if (loaded) return; loaded = true;
            var ta = Resources.Load<TextAsset>("cuts"); if (ta == null) { Debug.LogWarning("Нет клипов сцен (cuts)"); return; }
            var b = ta.bytes; int mlen = System.BitConverter.ToInt32(b, 0);
            var meta = MiniJson.Parse(Encoding.UTF8.GetString(b, 4, mlen)) as Dictionary<string, object>;
            int n = (int)MiniJson.N(meta, "n"); nb = (int)MiniJson.N(meta, "nb"); fps = (int)MiniJson.N(meta, "fps", 30);
            var bones = MiniJson.L(meta, "bones");
            MotionDB.Load(); toDb = new int[nb];
            for (int k = 0; k < nb; k++) toDb[k] = System.Array.IndexOf(MotionDB.bones, (string)bones[k]);
            foreach (Dictionary<string, object> c in MiniJson.L(meta, "clips")) clips[MiniJson.S(c, "name")] = new Clip { name = MiniJson.S(c, "name"), a = (int)MiniJson.N(c, "a"), n = (int)MiniJson.N(c, "n") };
            int o = 4 + mlen; while (o % 2 != 0) o++;
            Q = new float[n * nb * 4]; RO = new float[n * 3];
            for (int f = 0; f < n; f++) for (int k = 0; k < nb; k++)
                {
                    int s = o + ((f * nb + k) * 3) * 2;
                    float x = System.BitConverter.ToInt16(b, s) / 32767f, y = System.BitConverter.ToInt16(b, s + 2) / 32767f, z = System.BitConverter.ToInt16(b, s + 4) / 32767f;
                    float w = Mathf.Sqrt(Mathf.Max(0, 1 - x * x - y * y - z * z));
                    int d = (f * nb + k) * 4; Q[d] = x; Q[d + 1] = -y; Q[d + 2] = -z; Q[d + 3] = w;      // right-handed -> Unity, as MotionDB
                }
            int ro = o + n * nb * 3 * 2;
            for (int f = 0; f < n; f++)
            {
                RO[f * 3] = -System.BitConverter.ToInt16(b, ro + f * 6) / 4000f; RO[f * 3 + 1] = System.BitConverter.ToInt16(b, ro + f * 6 + 2) / 4000f; RO[f * 3 + 2] = System.BitConverter.ToInt16(b, ro + f * 6 + 4) / 4000f;
            }
        }
        /// the pose at frame fr of a clip into oq (motion-base bone order); the root offset into ro
        public static void Sample(Clip c, float fr, Quaternion[] oq, ref Vector3 ro)
        {
            fr = Mathf.Clamp(fr, 0, c.n - 1); int f0 = c.a + Mathf.FloorToInt(fr), f1 = Mathf.Min(c.a + c.n - 1, f0 + 1); float u = fr - Mathf.Floor(fr);
            for (int k = 0; k < nb; k++)
            {
                int db = toDb[k]; if (db < 0 || db >= oq.Length) continue;
                int d0 = (f0 * nb + k) * 4, d1 = (f1 * nb + k) * 4;
                oq[db] = Quaternion.Slerp(new Quaternion(Q[d0], Q[d0 + 1], Q[d0 + 2], Q[d0 + 3]), new Quaternion(Q[d1], Q[d1 + 1], Q[d1 + 2], Q[d1 + 3]), u);
            }
            ro = Vector3.Lerp(new Vector3(RO[f0 * 3], RO[f0 * 3 + 1], RO[f0 * 3 + 2]), new Vector3(RO[f1 * 3], RO[f1 * 3 + 1], RO[f1 * 3 + 2]), u);
        }
    }
}
