// Base poses for the MakeHuman skeleton, defined as world-space aim directions (Unity coordinates).
// Note: compared with the right-handed web version X is mirrored and every rotation angle changes sign.
using UnityEngine;

namespace AsciiHero
{
    public static class Pose
    {
        static void Aim(Human h, string name, string child, Vector3 dir)
        {
            var b = h.Bone(name); var c = h.Bone(child); if (b == null || c == null) return;
            var cur = (c.position - b.position).normalized;
            b.rotation = Quaternion.FromToRotation(cur, dir.normalized) * b.rotation;
        }
        static void RotWorld(Human h, string name, Vector3 axis, float angRad)
        {
            var b = h.Bone(name); if (b == null) return;
            b.rotation = Quaternion.AngleAxis(angRad * Mathf.Rad2Deg, axis.normalized) * b.rotation;
        }
        public struct HandFrame { public Vector3 W, F3, f, a, n; }
        public static HandFrame Hand(Human h, string s)
        {
            Vector3 W = h.Bone("wrist." + s).position, F2 = h.Bone("finger2-1." + s).position, F3 = h.Bone("finger3-1." + s).position, F5 = h.Bone("finger5-1." + s).position;
            var f = (F3 - W).normalized; var a = (F2 - F5).normalized;
            var nn = Vector3.Cross(f, a).normalized; a = Vector3.Cross(nn, f).normalized;
            return new HandFrame { W = W, F3 = F3, f = f, a = a, n = nn };
        }
        static void Curl(Human h, string s, float amount, float thumb)
        {
            float sign = s == "L" ? -1f : 1f;
            float[] mul = { 0f, 1.0f, 1.3f, 0.9f };
            for (int k = 2; k <= 5; k++)
                for (int j = 1; j <= 3; j++) { var fr = Hand(h, s); RotWorld(h, "finger" + k + "-" + j + "." + s, fr.a, -sign * amount * mul[j]); }
            if (thumb > 0)
            {
                var fr = Hand(h, s);
                RotWorld(h, "finger1-2." + s, fr.f, sign * thumb * 1.3f);
                RotWorld(h, "finger1-3." + s, fr.a, -sign * thumb * 0.7f);
            }
        }
        static Vector3 V(float x, float y, float z) { return new Vector3(-x, y, z).normalized; }   // mirrored from the web version

        public static void Apply(Human h, string pose)
        {
            foreach (var b in h.bones) b.localRotation = Quaternion.identity;
            Vector3 uaL, faL, uaR, faR; float curlL, curlR, thumb, thumbR;
            switch (pose)
            {
                case "grip":
                    uaL = V(0.28f, -1, 0.04f); faL = V(0.16f, -1, 0.32f); uaR = V(-0.3f, -1, 0.1f); faR = V(-0.12f, -0.75f, 0.75f);
                    curlL = 0.35f; curlR = 1.15f; thumb = 0.2f; thumbR = 0.7f; break;
                case "dual":
                    uaL = V(0.3f, -1, 0.1f); faL = V(0.12f, -0.75f, 0.75f); uaR = V(-0.3f, -1, 0.1f); faR = V(-0.12f, -0.75f, 0.75f);
                    curlL = 1.15f; curlR = 1.15f; thumb = 0.7f; thumbR = 0.7f; break;
                case "staff":   // walking staff: the forearm forward and level, the fist upright (thumb up) round the upright staff
                    uaL = V(0.28f, -1, 0.04f); faL = V(0.16f, -1, 0.32f); uaR = V(-0.2f, -0.82f, 0.45f); faR = V(-0.05f, 0.3f, 0.95f);
                    curlL = 0.35f; curlR = 1.15f; thumb = 0.2f; thumbR = 0.75f; break;
                case "shield":
                    uaL = V(0.35f, -1, 0.25f); faL = V(-0.25f, -0.15f, 1); uaR = V(-0.3f, -1, 0.1f); faR = V(-0.12f, -0.75f, 0.75f);
                    curlL = 1.1f; curlR = 1.15f; thumb = 0.6f; thumbR = 0.7f; break;
                default:
                    uaL = V(0.28f, -1, 0.04f); faL = V(0.16f, -1, 0.32f); uaR = V(-0.28f, -1, 0.04f); faR = V(-0.16f, -1, 0.32f);
                    curlL = 0.35f; curlR = 0.35f; thumb = 0.2f; thumbR = 0.2f; break;
            }
            Aim(h, "upperarm01.L", "lowerarm01.L", uaL); Aim(h, "upperarm01.R", "lowerarm01.R", uaR);
            Aim(h, "lowerarm01.L", "wrist.L", faL); Aim(h, "lowerarm01.R", "wrist.R", faR);
            Curl(h, "L", curlL, thumb); Curl(h, "R", curlR, thumbR);
            // natural stance: knees under the hips, ankles under the knees
            Aim(h, "upperleg01.L", "lowerleg01.L", V(-0.012f, -1, 0.07f)); Aim(h, "upperleg01.R", "lowerleg01.R", V(0.012f, -1, 0.07f));
            Aim(h, "lowerleg01.L", "foot.L", V(0, -1, -0.04f)); Aim(h, "lowerleg01.R", "foot.R", V(0, -1, -0.04f));
        }
    }
}
