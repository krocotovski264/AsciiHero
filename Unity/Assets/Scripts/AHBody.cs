// Body customisation: the parameters of the hero's body and the deformation of the MakeHuman mesh and skeleton.
//   * build: the weight picks a blend of the baked builds (thin, normal, athletic, large), continuously
//   * lengths, widths and girths: every bone gets a scale along itself (length) and across (girth, width/depth for the
//     trunk), a rotation for the collarbone slope and the breast lift; joints are moved down the hierarchy and every
//     vertex follows its bones with the skinning weights, so the result stays smooth and the skeleton fits the mesh
//   * fat: an inflation along the surface normal, distributed as body fat is (belly and waist first, hips and thighs,
//     more on the hips, thighs and breasts of a woman; little on forearms, shins, hands and feet)
//   * muscle: girth of the shoulders, arms, legs, neck, chest and back
//   * belly, buttocks: soft-tissue shapes on the front of the abdomen and the back of the pelvis
//   * breasts: a woman's grow in volume like a ball about their centre, a man's chest muscles thicken forward as a
//     flat block (no stretching along a bone)
// The feet are kept on the ground; the total height is set afterwards by the model scale (Character.ApplyHeight).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public static class Body
    {
        public class P
        {
            public string key, label, unit; public int group; public float min, max, defM, defF;
            public bool factor;       // the value multiplies a measured size (shown in cm); else it is shown as it is
            public bool girth;        // measured round the body (scaled with the body's width rather than its height)
        }
        public static readonly string[] GROUPS = { "ГАБАРИТЫ", "ТОРС И ШЕЯ", "ТАЗ И КОНЕЧНОСТИ" };
        static P F(string k, string l, int g, float min, float max, bool girth = false) { return new P { key = k, label = l, unit = "см", group = g, min = min, max = max, defM = 1, defF = 1, factor = true, girth = girth }; }
        static P V(string k, string l, string u, int g, float min, float max, float dm, float df) { return new P { key = k, label = l, unit = u, group = g, min = min, max = max, defM = dm, defF = df }; }
        public static readonly P[] ALL = {
            V("height", "Общий рост", "см", 0, 145, 215, 180, 167),
            V("weight", "Базовый вес тела", "кг", 0, 40, 150, 75, 60),
            V("muscle", "Мышечная масса", "%", 0, 15, 70, 40, 32),
            V("fat", "Жировая ткань", "%", 0, 4, 55, 18, 26),
            F("neckLen", "Длина шеи", 1, 0.7f, 1.45f),
            F("neckGirth", "Толщина шеи", 1, 0.75f, 1.45f, true),
            F("shoulderW", "Ширина плеч", 1, 0.8f, 1.25f),
            V("shoulderSlope", "Наклон плеч", "°", 1, -15, 15, 0, 0),
            F("chest", "Объём грудной клетки", 1, 0.85f, 1.3f, true),
            V("breastSize", "Размер груди", "%", 1, 0, 220, 100, 100),
            V("breastShape", "Форма груди (подъём)", "%", 1, 0, 100, 50, 50),
            F("backW", "Ширина верхней спины", 1, 0.85f, 1.3f, true),
            F("torsoLen", "Длина туловища", 1, 0.85f, 1.2f),
            F("waistW", "Ширина талии", 1, 0.75f, 1.4f, true),
            F("waistD", "Глубина талии", 1, 0.75f, 1.45f, true),
            V("belly", "Выступ живота", "см", 1, 0, 16, 0, 0),
            V("bellyShape", "Форма живота (высота)", "%", 1, 0, 100, 50, 50),
            F("pelvisW", "Ширина тазовых костей", 2, 0.85f, 1.3f, true),
            V("glutes", "Объём ягодиц", "%", 2, 30, 220, 100, 100),
            V("glutesShape", "Форма ягодиц (подъём)", "%", 2, 0, 100, 50, 50),
            F("armLen", "Длина рук", 2, 0.85f, 1.15f),
            F("legLen", "Длина ног", 2, 0.85f, 1.15f),
            F("biceps", "Объём бицепса", 2, 0.75f, 1.45f, true),
            F("forearm", "Объём предплечья", 2, 0.8f, 1.4f, true),
            F("thigh", "Толщина бёдер", 2, 0.8f, 1.4f, true),
            F("calf", "Толщина икр", 2, 0.8f, 1.4f, true),
            F("hand", "Размер кистей", 2, 0.85f, 1.2f),
            F("foot", "Размер стоп", 2, 0.85f, 1.2f),
            F("fingers", "Длина пальцев рук", 2, 0.8f, 1.25f),
        };
        public static readonly int N = ALL.Length;
        // ---------- presets ----------
        public static readonly string[] PRESETS = { "Спортивный", "Худой", "Обычный", "Толстый" };
        /// a preset body for a sex and height (the weight follows the height)
        public static float[] Preset(int k, char sex, float height)
        {
            var v = Defaults(sex); v[0] = height; float w0 = DefaultWeight(sex, height); bool f = sex == 'f';
            Action<string, float> S = (key, val) => v[IX[key]] = val;
            switch (k)
            {
                case 0:   // athletic: muscle, broad shoulders and back, narrow waist
                    S("weight", w0 * (f ? 1.05f : 1.1f)); S("muscle", f ? 42 : 55); S("fat", f ? 18 : 11);
                    S("shoulderW", f ? 1.03f : 1.08f); S("chest", f ? 1.02f : 1.08f); S("backW", f ? 1.06f : 1.12f);
                    S("waistW", 0.9f); S("waistD", 0.92f); S("biceps", f ? 1.08f : 1.18f); S("forearm", f ? 1.05f : 1.12f);
                    S("thigh", f ? 1.06f : 1.1f); S("calf", f ? 1.08f : 1.1f); S("neckGirth", f ? 1.02f : 1.1f);
                    S("glutes", f ? 120 : 105); S("breastSize", f ? 95 : 125); break;
                case 1:   // thin
                    S("weight", w0 * (f ? 0.82f : 0.8f)); S("muscle", f ? 26 : 30); S("fat", f ? 16 : 8);
                    S("chest", 0.93f); S("waistW", f ? 0.86f : 0.88f); S("waistD", 0.88f); S("biceps", 0.89f); S("forearm", 0.9f);
                    S("thigh", 0.88f); S("calf", 0.9f); S("neckGirth", 0.92f); S("breastSize", f ? 75 : 80); S("glutes", 80); S("pelvisW", f ? 0.96f : 1); break;
                case 3:   // heavy
                    S("weight", w0 * 1.4f); S("muscle", f ? 28 : 34); S("fat", f ? 42 : 34); S("belly", f ? 6 : 9);
                    S("waistW", 1.25f); S("waistD", f ? 1.25f : 1.3f); S("chest", 1.1f); S("neckGirth", 1.15f);
                    S("thigh", f ? 1.25f : 1.15f); S("calf", f ? 1.15f : 1.08f); S("biceps", f ? 1.15f : 1.1f);
                    S("glutes", f ? 160 : 130); S("breastSize", f ? 160 : 150); S("pelvisW", f ? 1.1f : 1.04f); break;
                default:  // normal
                    S("weight", w0); break;
            }
            return v;
        }
        static readonly Dictionary<string, int> IX = new Dictionary<string, int>();
        static Body() { for (int i = 0; i < ALL.Length; i++) IX[ALL[i].key] = i; }
        public static int Ix(string key) { return IX[key]; }
        public static float Def(int i, char sex) { return sex == 'f' ? ALL[i].defF : ALL[i].defM; }
        public static float[] Defaults(char sex) { var v = new float[N]; for (int i = 0; i < N; i++) v[i] = Def(i, sex); return v; }
        /// developer limits: up to fifty times beyond the normal range
        public static float DevMin(int i) { var p = ALL[i]; return p.min > 0 ? p.min / 50f : p.min * 50f; }
        public static float DevMax(int i) { return ALL[i].max * 50f; }

        // ---------- build (weight) ----------
        static readonly float[] KG_M = { 60, 74, 84, 100 }, KG_F = { 48, 60, 67, 82 };
        /// default weight for a height (the normal build, same body mass index)
        public static float DefaultWeight(char sex, float h) { return (sex == 'f' ? 60f : 74f) * Mathf.Pow(h / (sex == 'f' ? 166f : 178f), 2); }
        /// the weight as a (fractional) build: 0 thin .. 3 large, past either end extrapolated
        public static float BuildOf(char sex, float kg, float h)
        {
            var t = sex == 'f' ? KG_F : KG_M; float wn = kg * Mathf.Pow((sex == 'f' ? 166f : 178f) / Mathf.Max(h, 50), 2);
            if (wn <= t[0]) return (wn - t[0]) / (t[1] - t[0]);
            if (wn >= t[3]) return 3 + (wn - t[3]) / (t[3] - t[2]);
            int k = 0; while (k < 2 && wn > t[k + 1]) k++;
            return k + (wn - t[k]) / (t[k + 1] - t[k]);
        }

        // ---------- deformation ----------
        static Quaternion Q(Vector3 axis, float rad) { return Quaternion.AngleAxis(rad * Mathf.Rad2Deg, axis); }
        public static void Morph(Human h, float[] v)
        {
            float G(string k) => v[IX[k]];
            int NB = HumanDB.bones.Length; char sex = h.sex; bool fem = sex == 'f';
            var Hd = h.heads; var Tl = h.tails; var B = HumanDB.bones; var PAR = HumanDB.parents;
            float[] L = new float[NB], G1 = new float[NB], G2 = new float[NB]; var R = new Quaternion[NB];
            for (int i = 0; i < NB; i++) { L[i] = G1[i] = G2[i] = 1; R[i] = Quaternion.identity; }
            Action<Func<string, bool>, float, float> setG = (sel, a, b) => { for (int i = 0; i < NB; i++) if (sel(B[i])) { G1[i] *= a; G2[i] *= b; } };
            Action<Func<string, bool>, float> setL = (sel, a) => { for (int i = 0; i < NB; i++) if (sel(B[i])) L[i] *= a; };
            Func<string, Func<string, bool>> pre = p => (n => n.StartsWith(p));
            float cl(float x) => Mathf.Clamp(x, 0.02f, 60f);

            float m = (G("muscle") - Def(IX["muscle"], sex)) / Def(IX["muscle"], sex);
            float mg(float k) => cl(1 + k * m);
            setG(pre("shoulder01"), mg(0.3f), mg(0.3f)); setG(pre("upperarm"), mg(0.28f), mg(0.28f)); setG(pre("lowerarm"), mg(0.2f), mg(0.2f));
            setG(pre("upperleg"), mg(0.2f), mg(0.2f)); setG(pre("lowerleg"), mg(0.18f), mg(0.18f)); setG(pre("neck"), mg(0.2f), mg(0.2f));
            setG(n => n == "spine01" || n == "spine02", mg(0.12f), mg(0.08f));
            setL(pre("neck"), cl(G("neckLen"))); setG(pre("neck"), cl(G("neckGirth")), cl(G("neckGirth")));
            setL(n => n.StartsWith("clavicle") || n.StartsWith("shoulder01"), cl(G("shoulderW")));
            setG(n => n == "spine01" || n == "spine02", cl(G("chest")), cl(G("chest")));
            setG(n => n == "spine01", cl(G("backW")), 1); setG(n => n == "spine02", cl((1 + G("backW")) / 2), 1);
            setL(n => n.StartsWith("spine0"), cl(G("torsoLen")));
            setG(n => n == "spine03" || n == "spine04", cl(G("waistW")), cl(G("waistD")));
            setG(n => n == "spine05", cl((1 + G("waistW")) / 2), cl((1 + G("waistD")) / 2));
            setG(n => n == "root", cl(G("pelvisW")), 1); setL(pre("pelvis."), cl(G("pelvisW")));
            setL(n => n.StartsWith("upperarm") || n.StartsWith("lowerarm"), cl(G("armLen")));
            setL(n => n.StartsWith("upperleg") || n.StartsWith("lowerleg"), cl(G("legLen")));
            setG(pre("upperarm"), cl(G("biceps")), cl(G("biceps"))); setG(pre("lowerarm"), cl(G("forearm")), cl(G("forearm")));
            setG(pre("upperleg"), cl(G("thigh")), cl(G("thigh"))); setG(pre("lowerleg"), cl(G("calf")), cl(G("calf")));
            float hs = cl(G("hand")), fs = cl(G("foot"));
            for (int i = 0; i < NB; i++)
            {
                if (Under(i, "wrist.L") || Under(i, "wrist.R")) { L[i] *= hs; G1[i] *= hs; G2[i] *= hs; }
                if (Under(i, "foot.L") || Under(i, "foot.R")) { L[i] *= fs; G1[i] *= fs; G2[i] *= fs; }
            }
            setL(pre("finger"), cl(G("fingers")));
            float bs = Mathf.Max(0.02f, G("breastSize") / 100f), bl = (G("breastShape") - 50) / 50f;
            foreach (var s in new[] { "L", "R" })
            {
                // shoulder slope: the collarbone turns about the forward axis (the outer end up or down)
                int c = HumanDB.BI["clavicle." + s]; float side = Mathf.Sign(Tl[c].x - Hd[c].x);
                R[c] = Q(Vector3.forward, side * G("shoulderSlope") * Mathf.Deg2Rad);
            }
            // per-bone transforms and the new joints
            var M = new Matrix4x4[NB];
            for (int i = 0; i < NB; i++)
            {
                Vector3 d = Tl[i] - Hd[i]; d = d.sqrMagnitude > 1e-10f ? d.normalized : Vector3.up;
                Vector3 e1 = Vector3.right - d * d.x; if (e1.magnitude < 0.3f) e1 = Vector3.forward - d * d.z; e1.Normalize();
                Vector3 e2 = Vector3.Cross(d, e1);
                var S = Outer(d, L[i]); Add(ref S, Outer(e1, G1[i])); Add(ref S, Outer(e2, G2[i])); S.m33 = 1;
                M[i] = Matrix4x4.Rotate(R[i]) * S;
            }
            var NH = new Vector3[NB];
            for (int i = 0; i < NB; i++) { int p = PAR[i]; NH[i] = p < 0 ? Hd[i] : NH[p] + M[p].MultiplyVector(Hd[i] - Hd[p]); }
            var NT = new Vector3[NB]; for (int i = 0; i < NB; i++) NT[i] = NH[i] + M[i].MultiplyVector(Tl[i] - Hd[i]);
            int n = h.n; var P0 = h.pos; var P = new Vector3[n];
            var SI = HumanDB.skinIndex; var SWt = HumanDB.skinWeight;
            for (int i = 0; i < n; i++)
            {
                Vector3 o = Vector3.zero; float ws = 0;
                for (int k = 0; k < 4; k++)
                {
                    int b = SI[i * 4 + k]; float w = SWt[i * 4 + k] / 255f; if (w <= 0) continue;
                    o += w * (NH[b] + M[b].MultiplyVector(P0[i] - Hd[b])); ws += w;
                }
                P[i] = ws > 0 ? o / ws : P0[i];
            }
            var Nn = Human.Normals(P, h.partTris[0], n);
            // masks on the rest shape
            float yNavel = Hd[HumanDB.BI["spine05"]].y * 0.45f + Hd[HumanDB.BI["spine03"]].y * 0.55f, zSp = Hd[HumanDB.BI["spine03"]].z;
            float yHip = Hd[HumanDB.BI["upperleg01.L"]].y;
            float bellyY = yNavel + (G("bellyShape") - 50) / 50f * 0.07f;
            float f = (G("fat") - Def(IX["fat"], sex)) / Def(IX["fat"], sex);
            float gv = G("glutes") / 100f - 1, gl = (G("glutesShape") - 50) / 50f, belly = G("belly") / 100f;
            int bL = HumanDB.BI["breast.L"], bR = HumanDB.BI["breast.R"];
            // the breasts' centres (on the new skeleton): a point inside each, a little past the middle of its bone
            Vector3 cL = NH[bL] + (NT[bL] - NH[bL]) * 0.4f, cR = NH[bR] + (NT[bR] - NH[bR]) * 0.4f;
            float bk = fem ? Mathf.Max(0.45f, Mathf.Sqrt(bs)) : 1; var lift = Q(Vector3.right, -0.32f * bl * (fem ? 1 : 0.3f));
            var reg = h.region;
            for (int i = 0; i < n; i++)
            {
                if (HumanDB.vpart[i] != 0) continue;
                Vector3 r0 = P0[i]; float x = Mathf.Abs(r0.x), y = r0.y, z = r0.z;
                float front = Mathf.Clamp01((z - zSp) / 0.06f), back = Mathf.Clamp01((zSp - 0.04f - z) / 0.05f);
                float bm = Mathf.Exp(-Sq((y - bellyY) / 0.1f) - Sq(x / 0.14f)) * front;
                float gm = Mathf.Exp(-Sq((x - 0.085f) / 0.08f) - Sq((y - (yHip - 0.03f)) / 0.09f)) * back;
                float bw = 0, bwL = 0; for (int k = 0; k < 4; k++) { int b = SI[i * 4 + k]; float w = SWt[i * 4 + k] / 255f; if (b == bL || b == bR) bw += w; if (b == bL) bwL += w; }
                if (bw > 0.001f)
                {
                    Vector3 c = bwL >= bw * 0.5f ? cL : cR;
                    if (fem)
                    {   // a woman's breast: scaled about its centre (volume like a ball) and lifted
                        Vector3 q = lift * ((P[i] - c) * bk);
                        P[i] += (c + q - P[i]) * bw;
                    }
                    else
                    {   // a man's chest muscle: thicker forward as a flat block, the same over its whole face
                        P[i] += (Vector3.forward * ((bs - 1) * 0.04f) + (lift * (P[i] - c) - (P[i] - c))) * bw;
                    }
                }
                float fm = FatW(reg[i], fem) + bm * 0.9f + gm * (fem ? 0.8f : 0.4f) + bw * (fem ? 0.8f : 0.3f);
                float infl = Mathf.Clamp(0.011f * f * fm, -0.009f, 0.5f);
                Vector3 p = P[i] + Nn[i] * infl;
                p.z += bm * belly;
                p += Nn[i] * (gm * 0.035f * gv); p.y += gm * 0.025f * gl;
                P[i] = p;
            }
            // the soles stay on the ground
            float lo0 = 1e9f, lo1 = 1e9f;
            for (int i = 0; i < n; i++) if (HumanDB.vpart[i] == 0 && reg[i] == Region.Foot) { lo0 = Mathf.Min(lo0, P0[i].y); lo1 = Mathf.Min(lo1, P[i].y); }
            float dy = lo0 < 1e8f ? lo0 - lo1 : 0;
            for (int i = 0; i < n; i++) P[i].y += dy;
            for (int i = 0; i < NB; i++) { NH[i].y += dy; NT[i].y += dy; }
            float top = -1e9f, bot = 1e9f; for (int i = 0; i < n; i++) if (HumanDB.vpart[i] == 0) { top = Mathf.Max(top, P[i].y); bot = Mathf.Min(bot, P[i].y); }
            h.pos = P; h.heads = NH; h.tails = NT; h.nor = Human.Normals(P, h.partTris[0], n); h.height = top - bot;
        }
        static bool Under(int i, string root)
        {
            int r = HumanDB.BI[root];
            for (int j = i; j >= 0; j = HumanDB.parents[j]) if (j == r) return true;
            return false;
        }
        static float Sq(float x) { return x * x; }
        static Matrix4x4 Outer(Vector3 a, float s)
        {
            var m = Matrix4x4.zero;
            m.m00 = a.x * a.x * s; m.m01 = a.x * a.y * s; m.m02 = a.x * a.z * s;
            m.m10 = a.y * a.x * s; m.m11 = a.y * a.y * s; m.m12 = a.y * a.z * s;
            m.m20 = a.z * a.x * s; m.m21 = a.z * a.y * s; m.m22 = a.z * a.z * s;
            return m;
        }
        static void Add(ref Matrix4x4 a, Matrix4x4 b) { for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) a[r, c] += b[r, c]; }
        static float FatW(Region r, bool fem)
        {
            switch (r)
            {
                case Region.Torso: return 1f;
                case Region.Pelvis: return fem ? 1.25f : 1f;
                case Region.Thigh: return fem ? 1.1f : 0.7f;
                case Region.UpperArm: return 0.7f;
                case Region.Neck: return 0.5f;
                case Region.Head: return 0.25f;
                case Region.ForeArm: case Region.Shin: return 0.3f;
                case Region.Hand: return 0.08f;
                default: return 0.05f;
            }
        }
        // ---------- sizes shown next to the sliders ----------
        /// the size the parameter stands for, measured on the built (morphed) body, in metres (model units)
        public static float Measure(Human h, int ix)
        {
            Func<string, Vector3> Hh = b => h.heads[HumanDB.BI[b]];
            switch (ALL[ix].key)
            {
                case "neckLen": return (Hh("head") - Hh("neck01")).magnitude;
                case "neckGirth": return Girth(h, Region.Neck, -1, 2, Hh("neck01"), Hh("head"));
                case "shoulderW": return (Hh("upperarm01.L") - Hh("upperarm01.R")).magnitude + 0.08f;
                case "chest": return Perimeter(h, Hh("spine01").y - 0.02f);
                case "backW": return Width(h, Hh("spine01").y);
                case "torsoLen": return Hh("neck01").y - Hh("root").y;
                case "waistW": return Width(h, Hh("spine03").y);
                case "waistD": return Depth(h, Hh("spine03").y);
                case "pelvisW": return Width(h, Hh("upperleg01.L").y + 0.02f);
                case "armLen": return (Hh("upperarm01.L") - Hh("wrist.L")).magnitude;
                case "legLen": return Hh("upperleg01.L").y - Hh("foot.L").y + 0.07f;
                case "biceps": return Girth(h, Region.UpperArm, 0.18f, 0.32f, Hh("upperarm01.L"), Hh("lowerarm01.L"));
                case "forearm": return Girth(h, Region.ForeArm, 0.58f, 0.72f, Hh("lowerarm01.L"), Hh("wrist.L"));
                case "thigh": return Girth(h, Region.Thigh, 0.08f, 0.2f, Hh("upperleg01.L"), Hh("lowerleg01.L"));
                case "calf": return Girth(h, Region.Shin, 0.55f, 0.68f, Hh("lowerleg01.L"), Hh("foot.L"));
                case "hand": return (Hh("wrist.L") - h.tails[HumanDB.BI["finger3-3.L"]]).magnitude;
                case "foot": return FootLen(h);
            }
            return 0;
        }
        // mean distance of a band of the limb's surface from its axis, as a circumference (left side)
        static float Girth(Human h, Region rg, float t0, float t1, Vector3 a, Vector3 b)
        {
            Vector3 d = (b - a).normalized; float len = (b - a).magnitude, sum = 0; int cnt = 0;
            for (int i = 0; i < h.n; i++)
            {
                if (HumanDB.vpart[i] != 0 || h.region[i] != rg || h.pos[i].x * Mathf.Sign(a.x) <= 0) continue;
                Vector3 q = h.pos[i] - a; float t = Vector3.Dot(q, d) / len;
                if (t0 >= 0 && (t < t0 || t > t1)) continue; if (t0 < 0 && (t < 0.2f || t > 0.8f)) continue;
                sum += (q - d * Vector3.Dot(q, d)).magnitude; cnt++;
            }
            return cnt > 0 ? 2 * Mathf.PI * sum / cnt : 0;
        }
        static void Band(Human h, float y, out float xm, out float z0, out float z1)
        {
            xm = 0; z0 = 1e9f; z1 = -1e9f;
            for (int i = 0; i < h.n; i++)
            {
                if (HumanDB.vpart[i] != 0) continue; var rg = h.region[i];
                if (rg != Region.Torso && rg != Region.Pelvis) continue;
                if (Mathf.Abs(h.pos[i].y - y) > 0.012f) continue;
                xm = Mathf.Max(xm, Mathf.Abs(h.pos[i].x)); z0 = Mathf.Min(z0, h.pos[i].z); z1 = Mathf.Max(z1, h.pos[i].z);
            }
            if (z0 > z1) { z0 = z1 = 0; }
        }
        static float Width(Human h, float y) { float xm, z0, z1; Band(h, y, out xm, out z0, out z1); return 2 * xm; }
        static float Depth(Human h, float y) { float xm, z0, z1; Band(h, y, out xm, out z0, out z1); return z1 - z0; }
        static float Perimeter(Human h, float y)
        {
            float xm, z0, z1; Band(h, y, out xm, out z0, out z1); float a = xm, b = (z1 - z0) / 2;
            return Mathf.PI * (3 * (a + b) - Mathf.Sqrt((3 * a + b) * (a + 3 * b)));
        }
        static float FootLen(Human h)
        {
            float z0 = 1e9f, z1 = -1e9f;
            for (int i = 0; i < h.n; i++)
                if (HumanDB.vpart[i] == 0 && h.region[i] == Region.Foot && h.pos[i].x * Mathf.Sign(h.heads[HumanDB.BI["foot.L"]].x) > 0)
                { z0 = Mathf.Min(z0, h.pos[i].z); z1 = Mathf.Max(z1, h.pos[i].z); }
            return z1 > z0 ? z1 - z0 : 0;
        }
    }
}
