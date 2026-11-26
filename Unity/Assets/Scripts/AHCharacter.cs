// Full character: MakeHuman body + body-fitted garment shells + rigid gear + weapon.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    [Serializable]
    public class CharConfig
    {
        public char sex = 'm'; public int build = 1; public float height = 178f;
        public string armor = "plate"; public string weapon = "sword";
        public string blade;  // how the blade looks: null as the weapon says, "dagger" (a short sword), "sabre" (curved, for the two-handed set)
        public bool shadow;   // the hero: a body of living darkness inside the armour
        public float[] body;  // the body customisation (Body.ALL; the height is 'height' above), null = defaults
        public CharConfig Clone() { var c = (CharConfig)MemberwiseClone(); if (body != null) c.body = (float[])body.Clone(); return c; }
    }

    public partial class Character
    {
        public static readonly string[] BUILDS = { "Худое", "Обычное", "Атлетичное", "Крупное" };
        // (only the knight's plate and the sword are left to choose; the rest of the code still dresses townsfolk in plain clothes)
        public static readonly string[][] ARMORS = {
            new[] { "plate", "Рыцарские латы", "Гравированные латы, закрытый шлем и плащ" },
        };
        public static readonly string[][] WEAPONS = {
            new[] { "sword", "Меч", "Одноручный стальной клинок" },
        };

        public GameObject go;           // root (move/rotate this)
        public Transform model;         // scaled for height
        public Human human;
        public CharConfig cfg;
        public Vector3 weaponHome, weaponAlt; public Quaternion weaponHomeRot, weaponAltRot; public bool hasAlt, weaponInLeft;
        public Transform weapon, weaponL, orb, shield, weaponSlide, skirt; public Vector3[] skirtRest; public int skirtRings, skirtSegs; public float weaponSlideBase = -0.5f;   // shield: local z = its face, local y = its top
        public List<Renderer> glyphs = new List<Renderer>();   // skinned glyph renderers (limb hiding)
        public static readonly string[] LIMBS = { "head", "armL", "armR", "legL", "legR" };
        public static readonly string[] LIMB_NAME = { "голова", "левая рука", "правая рука", "левая нога", "правая нога" };
        public static readonly string[] LIMB_BONE = { "head", "upperarm01.L", "upperarm01.R", "upperleg01.L", "upperleg01.R" };
        /// bone index -> limb number 1..5 (0 = body)
        public static int[] LimbOfBone()
        {
            HumanDB.Load(); int nb = HumanDB.bones.Length; var r = new int[nb];
            for (int i = 0; i < nb; i++)
                for (int b = i; b >= 0 && r[i] == 0; b = HumanDB.parents[b])
                    for (int l = 0; l < 5; l++) if (HumanDB.bones[b] == LIMB_BONE[l]) { r[i] = l + 1; break; }
            return r;
        }
        public float baseHeight; public float[] meas, built;
        Mats mats;

        public static Character Build(CharConfig cfg, Mats mats, string name = "Character")
        {
            var c = new Character { cfg = cfg.Clone(), mats = mats };
            c.go = new GameObject(name);
            c.model = new GameObject("Model").transform; c.model.SetParent(c.go.transform, false);
            c.Construct();
            c.ApplyHeight();
            return c;
        }

        public void ApplyHeight()
        {
            float s = cfg.height / 100f / Mathf.Max(0.5f, baseHeight);
            float sxz = 1 + (s - 1) * 0.45f;
            model.localScale = new Vector3(sxz, s, sxz);
        }

        void Construct()
        {
            Human h;
            if (cfg.body != null)
            {
                float bf = Body.BuildOf(cfg.sex, cfg.body[Body.Ix("weight")], cfg.height);
                h = human = new Human(cfg.sex, bf, cfg.body); cfg.build = h.build;
                // the sizes shown next to the sliders, measured on this body
                meas = new float[Body.N]; built = (float[])cfg.body.Clone();
                for (int i = 0; i < Body.N; i++) if (Body.ALL[i].factor) meas[i] = Body.Measure(h, i);
            }
            else h = human = new Human(cfg.sex, cfg.build);
            baseHeight = h.height;
            h.MakeSkeleton(model);
            var R = h.region; var T = h.limbT; var P = h.pos;
            Func<int, float> X = i => P[i].x, Y = i => P[i].y, Z = i => P[i].z;
            Func<int, bool> isArm = i => R[i] == Region.UpperArm || R[i] == Region.ForeArm || R[i] == Region.Hand;
            Func<int, bool> isLeg = i => R[i] == Region.Thigh || R[i] == Region.Shin || R[i] == Region.Foot;
            Func<int, bool> torso = i => R[i] == Region.Torso || R[i] == Region.Pelvis;
            float hipY = h.Head("upperleg01.L").y, neckY = h.Head("neck01").y; Vector3 headJ = h.Head("head");
            float eyeY = (h.Head("eye.L").y + h.Head("eye.R").y) / 2;
            float headTop = h.Tail("head").y;
            float waistY = (h.Head("spine03").y + h.Head("spine04").y) / 2;
            Func<int, bool> isFace = i => R[i] == Region.Head && Z(i) > headJ.z + 0.035f && Y(i) < eyeY + 0.035f && Mathf.Abs(X(i)) < 0.075f;
            Func<int, bool> scalpCut = i => Y(i) > eyeY + 0.045f - Mathf.Max(0, -(Z(i) - headJ.z)) * 1.1f;

            AddSkinned(h.PartMesh(0), cfg.shadow ? "SHADOW" : "SKIN", "Body");
            AddSkinned(h.PartMesh(1), "EYE", "Eyes");

            Action<Func<int, bool>, float, string, int, float> shell = (pred, t, mat, smooth, reinf) =>
                AddSkinned(h.Shell(pred, t, new Human.ShellOpts { smooth = smooth, reinflate = reinf }), mat, mat);

            string A = cfg.armor; bool helmet = A == "plate", hooded = A == "robe" || A == "chain";

            // ---- hair ----
            if (!helmet && !cfg.shadow)
            {
                shell(i => R[i] == Region.Head && scalpCut(i) && !(Mathf.Abs(X(i)) > 0.068f && Y(i) < eyeY + 0.06f && Z(i) > headJ.z - 0.03f), 0.011f, "HAIR", 2, 0);
                if (cfg.sex == 'f' && !hooded) LongHair();
                if (cfg.sex == 'm' && cfg.build >= 2)
                    shell(i => R[i] == Region.Head && Y(i) < eyeY - 0.045f && Z(i) > headJ.z && !(Y(i) < eyeY - 0.06f && Y(i) > eyeY - 0.075f && Mathf.Abs(X(i)) < 0.028f && Z(i) > headJ.z + 0.07f), 0.008f, "HAIR", 1, 0);
            }

            Action<float, string, float> boots = (fromT, mat, t) => shell(i => (R[i] == Region.Shin && T[i] > fromT) || R[i] == Region.Foot, t, mat, 2, 0);
            Action<string> pants = mat => shell(i => (isLeg(i) && T[i] < 0.9f && R[i] != Region.Foot) || R[i] == Region.Pelvis || (R[i] == Region.Torso && Y(i) < waistY), 0.005f, mat, 1, 0);
            Action<float, string, float, float> belt = (y, mat, t, hw) => shell(i => torso(i) && Mathf.Abs(Y(i) - y) < hw, t, mat, 2, 0);
            float beltY = hipY + 0.085f;

            switch (A)
            {
                case "cloth":
                    pants("PANTS");
                    shell(i => (torso(i) && Y(i) > hipY + 0.02f) || (R[i] == Region.Neck && Y(i) < neckY + 0.02f) || (R[i] == Region.UpperArm && T[i] < 0.5f), 0.008f, "LINEN", 1, 0);
                    boots(0.55f, "LEATHER", 0.013f); belt(beltY, "LEATHER2", 0.02f, 0.022f);
                    Buckle(beltY, 0.03f);
                    break;
                case "leather":
                    pants("PANTS");
                    shell(i => isArm(i) && T[i] < 0.95f && R[i] != Region.Hand, 0.007f, "LINEN", 1, 0);
                    shell(i => (torso(i) && Y(i) > hipY - 0.04f) || (R[i] == Region.Neck && Y(i) < neckY + 0.03f) || (R[i] == Region.UpperArm && T[i] < 0.18f), 0.017f, "LEATHER", 3, 0);
                    shell(i => R[i] == Region.ForeArm && T[i] > 0.58f && T[i] < 0.98f, 0.016f, "LEATHER2", 2, 0);
                    shell(i => R[i] == Region.UpperArm && T[i] < 0.3f, 0.03f, "LEATHER2", 4, 0);
                    shell(i => R[i] == Region.Hand, 0.004f, "LEATHER2", 0, 0);
                    boots(0.35f, "LEATHER2", 0.015f); belt(beltY, "DARK", 0.026f, 0.022f);
                    Buckle(beltY, 0.036f);
                    // diagonal strap (X is mirrored relative to the web build)
                    shell(i => torso(i) && Mathf.Abs((Y(i) - hipY) - (-X(i) + 0.2f) * 1.4f) < 0.025f && Y(i) > hipY, 0.024f, "LEATHER2", 1, 0);
                    break;
                case "chain":
                    pants("PANTS");
                    shell(i => torso(i) || R[i] == Region.Neck || R[i] == Region.UpperArm || (R[i] == Region.ForeArm && T[i] < 0.6f) || (R[i] == Region.Thigh && T[i] < 0.42f), 0.012f, "CHAIN", 1, 0);
                    shell(i => R[i] == Region.Head && !isFace(i) && Y(i) > eyeY - 0.12f, 0.014f, "CHAIN", 2, 0);
                    shell(i => (torso(i) || (R[i] == Region.Thigh && T[i] < 0.4f)) && Mathf.Abs(X(i)) < 0.115f && Y(i) < neckY - 0.06f, 0.03f, "RED", 4, 0);
                    shell(i => (R[i] == Region.ForeArm && T[i] > 0.6f) || R[i] == Region.Hand, 0.01f, "LEATHER2", 1, 0);
                    boots(0.5f, "LEATHER", 0.013f); belt(beltY + 0.01f, "LEATHER2", 0.04f, 0.02f);
                    Buckle(beltY + 0.01f, 0.05f);
                    break;
                case "plate":
                {
                    pants("PANTS");
                    shell(i => (torso(i) && Y(i) > hipY - 0.05f) || (R[i] == Region.UpperArm && T[i] < 0.5f) || R[i] == Region.Neck, 0.008f, "LINEN", 1, 0);
                    DressKnight(hipY, neckY);
                    Cape();
                    break;
                }
                case "robe":
                    pants("ROBED");
                    shell(i => (torso(i) && Y(i) > hipY - 0.05f) || R[i] == Region.Neck || (isArm(i) && T[i] < 0.86f && R[i] != Region.Hand), 0.018f, "ROBE", 3, 0);
                    shell(i => R[i] == Region.ForeArm && T[i] > 0.72f && T[i] < 0.86f, 0.03f, "GOLD", 2, 0);
                    shell(i => R[i] == Region.Head && !isFace(i) && Y(i) > eyeY - 0.14f, 0.03f, "ROBED", 5, 0);
                    RobeSkirt(hipY + 0.1f);
                    belt(beltY, "GOLD", 0.045f, 0.014f);
                    boots(0.8f, "LEATHER2", 0.01f);
                    break;
            }

            string W = cfg.weapon;
            Pose.Apply(h, W == "shield" ? "shield" : W == "none" ? "relaxed" : W == "axe" ? "dual" : W == "staff" ? "staff" : "grip");
            if (W != "none") AddWeapon(W);
            if (W == "shield") AddShield();
        }

        SkinnedMeshRenderer AddSkinned(Mesh m, string mat, string name)
        {
            var g = new GameObject(name); g.transform.SetParent(model, false);
            var r = g.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = m; r.bones = human.bones; r.rootBone = human.root; r.sharedMaterial = mats[mat];
            r.updateWhenOffscreen = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
            return r;
        }

        // place obj at a pose given in model space (rest), then parent it to a bone keeping the pose
        void Attach(string bone, Transform obj, Vector3 pos, Quaternion rot)
        {
            obj.SetParent(model, false); obj.localPosition = pos; obj.localRotation = rot; obj.localScale = Vector3.one;
            obj.SetParent(human.Bone(bone), true);
        }
        Transform Group(string name) { return new GameObject(name).transform; }
        GameObject Part(Mesh m, string mat, Transform parent, Vector3 p, Quaternion r) { return MeshGen.Obj(m, mats[mat], parent, p, r); }
        GameObject Part(Mesh m, string mat, Transform parent, Vector3 p) { return MeshGen.Obj(m, mats[mat], parent, p, Quaternion.identity); }

        void Buckle(float y, float outOff)
        {
            var h = human; float zmax = -1;
            for (int i = 0; i < h.n; i++) if (HumanDB.vpart[i] == 0 && Mathf.Abs(h.pos[i].y - y) < 0.01f && Mathf.Abs(h.pos[i].x) < 0.03f) zmax = Mathf.Max(zmax, h.pos[i].z);
            var g = Group("Buckle");
            var fr = Part(MeshGen.Torus(0.024f, 0.006f, 14, 6), "GOLD", g, Vector3.zero); fr.transform.localScale = new Vector3(1, 0.8f, 1);
            Part(MeshGen.Box(0.004f, 0.03f, 0.006f), "GOLD", g, Vector3.zero);
            Attach("spine05", g, new Vector3(0, y, zmax + outOff * 0.9f), Quaternion.identity);
        }

        void Plume(float top)
        {
            var g = Group("Plume");
            var crest = new List<Vector3>();
            for (int k = 0; k <= 20; k++) { float u = Mathf.PI * 0.9f * k / 20f + Mathf.PI * 0.05f; crest.Add(new Vector3(0, -0.08f + Mathf.Sin(u) * 0.13f, -0.01f - Mathf.Cos(u) * 0.13f)); }
            Part(MeshGen.Tube(crest, 0.012f, 6), "PLATE", g, Vector3.zero);
            for (int k = 0; k < 9; k++)
            {
                float t = k / 8f, a = Mathf.PI * (0.15f + 0.75f * t);
                var f = Part(MeshGen.Sphere(0.035f + 0.02f * Mathf.Sin(t * Mathf.PI), 8, 6), "RED", g,
                    new Vector3(0, -0.08f + Mathf.Sin(a) * 0.17f, -0.01f - Mathf.Cos(a) * 0.17f), Quaternion.Euler((-a + Mathf.PI / 2) * Mathf.Rad2Deg, 0, 0));
                f.transform.localScale = new Vector3(0.45f, 1.4f, 1);
            }
            var hc = human.Head("head");
            Attach("head", g, new Vector3(0, top + 0.02f, hc.z - 0.01f), Quaternion.identity);
        }

        void LongHair()
        {
            var h = human; Vector3 hc = h.Head("head"); float top = h.Tail("head").y; Vector3 nk = h.Head("neck01");
            float rx = 0, rz = 0;
            for (int i = 0; i < h.n; i++) if (h.region[i] == Region.Head && Mathf.Abs(h.pos[i].y - (top - 0.1f)) < 0.01f)
                { rx = Mathf.Max(rx, Mathf.Abs(h.pos[i].x)); rz = Mathf.Max(rz, Mathf.Abs(h.pos[i].z - hc.z)); }
            int rows = 26, segs = 36; float y0 = top - 0.06f, y1 = nk.y - 0.26f;
            var v = new List<Vector3>(); var idx = new List<int>();
            for (int r = 0; r <= rows; r++)
            {
                float t = r / (float)rows, y = y0 + (y1 - y0) * t;
                float flare = 1 + 0.25f * Mathf.Min(1, t * 2) + 0.1f * t;
                for (int s = 0; s <= segs; s++)
                {
                    float a = Mathf.PI * (-0.1f + 1.2f * s / segs);
                    float wav = 1 + 0.06f * Mathf.Sin(s * 1.9f + t * 7) + 0.04f * Mathf.Sin(s * 5.3f);
                    float x = Mathf.Cos(a) * (rx + 0.022f) * flare * wav, z = hc.z - 0.008f - Mathf.Sin(a) * (rz + 0.03f) * flare * wav * (1 - 0.25f * t);
                    v.Add(new Vector3(-x, y - 0.03f * Mathf.Sin(s * 2.7f) * t, z));
                }
            }
            for (int r = 0; r < rows; r++) for (int s = 0; s < segs; s++)
                { int a = r * (segs + 1) + s, b = a + segs + 1; idx.Add(a); idx.Add(a + 1); idx.Add(b); idx.Add(a + 1); idx.Add(b + 1); idx.Add(b); }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(idx, 0); m.RecalculateNormals(); m.RecalculateBounds();
            MeshGen.AddGlyphUV(m, false);
            var g = Group("LongHair"); Part(m, "HAIR2", g, Vector3.zero);
            Attach("head", g, Vector3.zero, Quaternion.identity);
        }

        /// the helmet: a bascinet with its visor down (KayKit Adventurers' knight, CC0), stretched to this head; returns its top (or -1)
        float KnightHelm()
        {
            var ta = Resources.Load<TextAsset>("knighthelm"); if (ta == null) return -1;
            var b = ta.bytes; int nv = BitConverter.ToInt32(b, 0), ni = BitConverter.ToInt32(b, 4), o = 8;
            var h = human; Vector3 lo = Vector3.one * 1e5f, hi = -Vector3.one * 1e5f;
            for (int i = 0; i < h.n; i++) if (HumanDB.vpart[i] == 0 && h.region[i] == Region.Head) { lo = Vector3.Min(lo, h.pos[i]); hi = Vector3.Max(hi, h.pos[i]); }
            if (hi.x < lo.x) return -1;
            Vector3 c = (lo + hi) / 2, hs = (hi - lo) / 2;
            hs = new Vector3(hs.x * 1.3f, hs.y * 1.12f, hs.z * 1.22f); c.y += hs.y * 0.05f; c.z -= hs.z * 0.03f;
            var vH = new List<Vector3>(); var vV = new List<Vector3>(); var map = new int[nv]; var part = new float[nv];
            for (int i = 0; i < nv; i++)
            {
                var pn = new Vector3(BitConverter.ToSingle(b, o + i * 12), BitConverter.ToSingle(b, o + i * 12 + 4), BitConverter.ToSingle(b, o + i * 12 + 8));
                part[i] = BitConverter.ToSingle(b, o + nv * 12 + i * 4);
                var p = c + Vector3.Scale(pn, hs);
                if (part[i] < 0.5f) { map[i] = vH.Count; vH.Add(p); } else { map[i] = vV.Count; vV.Add(p); }
            }
            var iH = new List<int>(); var iV = new List<int>(); int io = o + nv * 16;
            for (int t = 0; t + 2 < ni; t += 3)
            {
                int a0 = BitConverter.ToInt32(b, io + t * 4), a1 = BitConverter.ToInt32(b, io + t * 4 + 4), a2 = BitConverter.ToInt32(b, io + t * 4 + 8);
                var l = part[a0] < 0.5f ? iH : iV; l.Add(map[a0]); l.Add(map[a1]); l.Add(map[a2]);
            }
            var g = Group("Helmet");
            foreach (var (vs, ix, mat) in new[] { (vH, iH, "PLATE"), (vV, iV, "STEEL") })
            {
                var m = new Mesh(); m.SetVertices(vs); m.SetTriangles(ix, 0); m.RecalculateNormals(); m.RecalculateBounds();
                MeshGen.AddGlyphUV(m, false);
                Part(m, mat, g, Vector3.zero);
            }
            Attach("head", g, Vector3.zero, Quaternion.identity);
            return c.y + hs.y;
        }

        /// The knight's cloak, as a real one is worn: clasped at the throat, it lies round the neck, covers the shoulders
        /// and the tops of the arms, and its edges go back behind the arms between the shoulder and the elbow; from there
        /// it hangs down the back, clear of the plates and the backside, to the middle of the calves, a little wider at
        /// the hem, with soft folds. Its sheet is laid out round the body's axis: across it (u) is the angle from the back
        /// (-Th..+Th, Th growing narrower from the neck to the elbows), down it (v) the height (rows closer together at the
        /// top, where it bends over the shoulders). The shaders find a vertex's place on the grid the same way (AHCloth).
        public Vector3[] capeRest, capeNrm; public Vector4 capeMap, capeMap2, capeMap3; public GameObject hoodDown;
        public const int CAPE_C = 12, CAPE_R = 12; public const float CAPE_EXP = 1.45f;
        public float capeTop, capeBot, capeCz; float capeVs, capeVe; public float[] capeRx, capeRz;
        public static float CapeTheta(Vector4 m2, Vector4 m3, float v) { return v < m2.w ? Mathf.Lerp(m2.x, m2.y, v / m2.w) : Mathf.Lerp(m2.y, m2.z, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(m2.w, m3.x, v))); }
        Vector3 CapePt(float u, float v)
        {
            float y = capeTop - (capeTop - capeBot) * Mathf.Pow(v, CAPE_EXP);
            float th = (u - 0.5f) * 2 * CapeTheta(capeMap2, capeMap3, v);
            int k = Mathf.Clamp(Mathf.RoundToInt(v * (capeRx.Length - 1)), 0, capeRx.Length - 1);
            float rx = capeRx[k], rz = capeRz[k];
            float sn = Mathf.Sin(th), cs = Mathf.Cos(th);
            float r = 1f / Mathf.Sqrt((sn / rx) * (sn / rx) + (cs / rz) * (cs / rz));
            r *= 1 + (0.004f + 0.035f * v) * Mathf.Cos(th * 9 + v * 3);                  // folds
            return new Vector3(sn * r, y, capeCz - cs * r);
        }
        void Cape()
        {
            var h = human; var P = h.pos; float neckY = h.Head("neck01").y;
            capeTop = neckY + 0.025f; capeBot = (h.Head("lowerleg01.L").y + h.Head("foot.L").y) / 2;
            float shY = h.Head("upperarm01.L").y, elY = h.Head("lowerarm01.L").y;
            // the body's extents at a height: half width (with the arms down to the elbow), front and back
            System.Func<float, bool, Vector3> ext = (y, arms) =>
            {
                float xm = 0, z0 = 1, z1 = -1;
                for (int i = 0; i < h.n; i++)
                {
                    if (HumanDB.vpart[i] != 0 || Mathf.Abs(P[i].y - y) > 0.018f) continue;
                    var rg = h.region[i];
                    bool body = rg == Region.Torso || rg == Region.Pelvis || rg == Region.Neck || rg == Region.Thigh || rg == Region.Shin || rg == Region.Head;
                    if (!(body || (arms && rg == Region.UpperArm))) continue;
                    xm = Mathf.Max(xm, Mathf.Abs(P[i].x)); z0 = Mathf.Min(z0, P[i].z); z1 = Mathf.Max(z1, P[i].z);
                }
                return new Vector3(xm, z0, z1);
            };
            var chest = ext(shY - 0.08f, false); if (chest.x <= 0) return;
            capeCz = (chest.y + chest.z) / 2;
            float neckR = Mathf.Max(0.06f, ext(neckY, false).x);
            const int NB = 48; capeRx = new float[NB + 1]; capeRz = new float[NB + 1];
            float prevBack = 0;
            for (int kk = 0; kk <= NB; kk++)
            {
                float v = kk / (float)NB, y = capeTop - (capeTop - capeBot) * Mathf.Pow(v, CAPE_EXP);
                var e = ext(y, y > elY - 0.02f);
                if (e.x <= 0) e = new Vector3(capeRx[Mathf.Max(0, kk - 1)], capeCz - prevBack, capeCz + 0.1f);
                float rx, rz;
                if (y > neckY - 0.01f) { rx = rz = neckR + 0.055f; }                                     // round the neck
                else
                {
                    rx = e.x + (y > elY - 0.02f ? 0.105f : 0.085f) + 0.12f * Mathf.Max(0, v - 0.35f);   // over the plates and the arms; wider at the hem
                    rz = Mathf.Max(capeCz - e.y + 0.105f, 0.12f);
                    if (y > shY) { float t = Mathf.InverseLerp(neckY, shY, y); rx = Mathf.Lerp(neckR + 0.06f, rx, 1 - t * t); }     // the slope of the shoulders
                }
                // it drapes: below the shoulder blades the back of it never comes back in toward the body
                if (y < shY - 0.1f) rz = Mathf.Max(rz, prevBack - 0.004f);
                prevBack = rz; capeRx[kk] = rx; capeRz[kk] = rz;
            }
            float vOf(float y) => Mathf.Pow(Mathf.Clamp01((capeTop - y) / (capeTop - capeBot)), 1f / CAPE_EXP);
            capeVs = vOf(shY - 0.04f); capeVe = vOf(elY);
            capeMap = new Vector4(capeCz, capeTop, capeBot, 1);
            capeMap2 = new Vector4(Mathf.PI * 0.86f, Mathf.PI * 0.66f, Mathf.PI * 0.56f, capeVs);       // round the neck (a gap at the clasp), over the shoulders, behind the arms
            capeMap3 = new Vector4(capeVe, 1f / CAPE_EXP, CAPE_C, CAPE_R);
            capeRest = new Vector3[CAPE_C * CAPE_R]; capeNrm = new Vector3[CAPE_C * CAPE_R];
            for (int r = 0; r < CAPE_R; r++) for (int c = 0; c < CAPE_C; c++)
                {
                    float u = c / (CAPE_C - 1f), v = r / (CAPE_R - 1f), e = 0.01f;
                    var p0 = CapePt(u, v); capeRest[r * CAPE_C + c] = p0;
                    var n = Vector3.Cross(CapePt(Mathf.Min(1, u + e), v) - CapePt(Mathf.Max(0, u - e), v), CapePt(u, Mathf.Min(1, v + e)) - CapePt(u, Mathf.Max(0, v - e))).normalized;
                    if (Vector3.Dot(n, new Vector3(p0.x, 0, p0.z - capeCz)) < 0) n = -n;          // outward, away from the body
                    capeNrm[r * CAPE_C + c] = n;
                }
            float width = Mathf.Max(chest.x * 2.65f, Mathf.Abs(h.Head("upperarm01.R").x) * 2.45f);
            float depth = Mathf.Max(0.20f, chest.z - chest.y + 0.11f);
            var size = new Vector3(width, capeTop - capeBot, depth);
            var center = new Vector3(0, (capeTop + capeBot) * 0.5f, capeCz - 0.025f);
            skirt = MountKnight("Cape", "spine01", center, Quaternion.identity, size);
        }
        /// the hood thrown back: a soft fold of cloth lying on the cloak's back below the neck (shown when the head is gone)
        void HoodDown()
        {
            if (capeRx == null) return;
            var vs = new List<Vector3>(); var idx = new List<int>(); int MC = 14, MR = 10;
            float top = capeTop - 0.03f, w0 = capeRx[0] * 2.2f, zb = capeCz - capeRz[2] - 0.01f;
            for (int r = 0; r <= MR; r++)
                for (int c = 0; c <= MC; c++)
                {
                    float u = c / (float)MC, v = r / (float)MR, w = w0 * (1 - 0.45f * v * v);
                    float x = (u - 0.5f) * w, bulge = 0.075f * Mathf.Sin(Mathf.PI * v) * (1 - Mathf.Pow(2 * u - 1, 2)) + 0.02f;
                    vs.Add(new Vector3(x, top - 0.27f * v, zb - bulge - 0.02f * Mathf.Sin(u * Mathf.PI * 5) * v));
                }
            for (int r = 0; r < MR; r++) for (int c = 0; c < MC; c++)
                { int a0 = r * (MC + 1) + c, b0 = a0 + MC + 1; idx.Add(a0); idx.Add(a0 + 1); idx.Add(b0); idx.Add(a0 + 1); idx.Add(b0 + 1); idx.Add(b0); }
            var m = new Mesh(); m.SetVertices(vs); m.SetTriangles(idx, 0); m.RecalculateNormals(); m.RecalculateBounds();
            MeshGen.AddGlyphUV(m, false);
            var g = Group("HoodDown"); Part(m, "CAPE", g, Vector3.zero);
            Attach("spine01", g, Vector3.zero, Quaternion.identity);
            hoodDown = g.gameObject; hoodDown.SetActive(false);
        }

        void RobeSkirt(float yTop)
        {
            var h = human; var P = h.pos; int rings = 22, segs = 48;
            Func<float, Vector3> ext = y =>
            {
                float x0 = 0, z0 = 0, z1 = 0;
                for (int i = 0; i < h.n; i++)
                {
                    var rg = h.region[i];
                    if (HumanDB.vpart[i] == 0 && Mathf.Abs(P[i].y - y) < 0.012f && (rg == Region.Torso || rg == Region.Pelvis || rg == Region.Thigh || rg == Region.Shin))
                    { x0 = Mathf.Max(x0, Mathf.Abs(P[i].x)); z0 = Mathf.Min(z0, P[i].z); z1 = Mathf.Max(z1, P[i].z); }
                }
                return new Vector3(x0, z0, z1);
            };
            var prev = ext(yTop); var v = new List<Vector3>(); var idx = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings, y = yTop - (yTop - 0.02f) * t;
                var e = ext(y);
                if (e.x < prev.x * 0.9f) e = prev;
                prev = new Vector3(Mathf.Max(e.x, prev.x), Mathf.Min(e.y, prev.y), Mathf.Max(e.z, prev.z));
                float flare = 0.025f + 0.11f * t * t;
                float rx = prev.x + flare, rz = (prev.z - prev.y) / 2 + flare * 0.9f, cz = (prev.z + prev.y) / 2;
                for (int s = 0; s <= segs; s++)
                {
                    float a = 2 * Mathf.PI * s / segs;
                    float fold = 1 + (0.012f + 0.05f * t) * Mathf.Sin(a * 9 + t * 2) / Mathf.Max(rx, 0.1f);
                    v.Add(new Vector3(Mathf.Cos(a) * rx * fold, y, cz + Mathf.Sin(a) * rz * fold));
                }
            }
            for (int r = 0; r < rings; r++) for (int s = 0; s < segs; s++)
                { int a = r * (segs + 1) + s, b = a + segs + 1; idx.Add(a); idx.Add(b); idx.Add(a + 1); idx.Add(a + 1); idx.Add(b); idx.Add(b + 1); }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(idx, 0); m.RecalculateNormals(); m.RecalculateBounds();
            MeshGen.AddGlyphUV(m, true);
            var g = Group("RobeSkirt"); skirt = Part(m, "ROBE", g, Vector3.zero).transform;
            skirtRest = v.ToArray(); skirtRings = rings; skirtSegs = segs;      // for the cloth simulation (AHCloth)
            Attach("spine05", g, Vector3.zero, Quaternion.identity);
        }

        // ---------- weapons ----------
        static List<Vector2> Bez(Vector2 a, Vector2 c, Vector2 b, int n)
        {
            var r = new List<Vector2>();
            for (int i = 1; i <= n; i++) { float t = i / (float)n, u = 1 - t; r.Add(u * u * a + 2 * u * t * c + t * t * b); }
            return r;
        }
        static Mesh Blade(float len, float w0, float w1, float thick, float tipLen)
        {
            var s = new List<Vector2> { new Vector2(-w0, 0), new Vector2(w0, 0), new Vector2(w1, len - tipLen), new Vector2(0, len), new Vector2(-w1, len - tipLen) };
            return MeshGen.Extrude(s, thick * 2.8f);
        }

        // a sabre: a curved single-edged blade, a knuckle guard (laid in the tomahawk's holder: its edge forward)
        void Sabre(Transform g)
        {
            Part(MeshGen.Cylinder(0.015f, 0.013f, 0.11f, 8), "LEATHER2", g, Vector3.zero);
            Part(MeshGen.Box(0.03f, 0.016f, 0.1f), "GOLD", g, new Vector3(0, 0.06f, 0.015f));
            // the blade: a curved strip, bending back along its length
            var s = new List<Vector2>(); int n = 8; float L = 0.72f;
            for (int i = 0; i <= n; i++) { float t = i / (float)n; s.Add(new Vector2(0.018f * (1 - t * 0.7f) - 0.09f * t * t, t * L)); }
            for (int i = n; i >= 0; i--) { float t = i / (float)n; s.Add(new Vector2(-0.016f * (1 - t) - 0.09f * t * t - (i == n ? 0 : 0.002f), t * L)); }
            var go = Part(MeshGen.Extrude(s, 0.008f), "STEEL", g, new Vector3(0, 0.068f, 0), Quaternion.Euler(0, 90, 0));
        }
        // a tomahawk: short haft, small bearded head with a spike behind
        void Tomahawk(Transform g)
        {
            Part(MeshGen.Cylinder(0.014f, 0.012f, 0.5f, 8), "WOOD", g, new Vector3(0, 0.17f, 0));
            var s = new List<Vector2> { new Vector2(0, -0.025f), new Vector2(0.035f, -0.022f) };
            s.AddRange(Bez(new Vector2(0.035f, -0.022f), new Vector2(0.08f, -0.06f), new Vector2(0.12f, -0.085f), 5));
            s.AddRange(Bez(new Vector2(0.12f, -0.085f), new Vector2(0.1f, 0f), new Vector2(0.12f, 0.07f), 6));
            s.AddRange(Bez(new Vector2(0.12f, 0.07f), new Vector2(0.08f, 0.05f), new Vector2(0.035f, 0.022f), 5));
            s.Add(new Vector2(0, 0.025f));
            Part(MeshGen.Extrude(s, 0.016f), "STEEL", g, new Vector3(0.01f, 0.38f, 0));
            Part(MeshGen.Cylinder(0.012f, 0f, 0.06f, 6), "STEEL", g, new Vector3(-0.04f, 0.38f, 0), Quaternion.Euler(0, 0, 90));
            Part(MeshGen.Cylinder(0.017f, 0.017f, 0.11f, 8), "LEATHER2", g, Vector3.zero);
        }
        // Grips (after fencing and woodcraft references): every weapon lies across the closed fist - the haft runs from the
        // heel of the hand out past the thumb and index finger, the fingers and thumb wrapped round it (hammer grip). Sword:
        // the edge in line with the knuckles, the pommel under the little finger. Tomahawk: held near the end of the
        // haft with a little of it showing below the fist, head up and edge forward. Spear: at its balance point, the middle.
        // Staff: upright in a fist held thumb-up, the forearm level. Where the weapon is carried (sword and tomahawk: point
        // ahead and low; spear: level, point ahead; staff: upright) the wrist turns at most 25 degrees towards it and the
        // rest is taken up by the grip lying diagonally across the palm, as a real hand does - never a bent-back wrist.
        static Vector3 CarryDir(string W)
        {
            return (W == "spear" ? new Vector3(0, 0.08f, 1f) : W == "staff" ? new Vector3(0, 1f, 0.05f) : new Vector3(0, -0.6f, 0.8f)).normalized;
        }
        Quaternion GripIn(string side, string W, out Vector3 grip)
        {
            Vector3 D = CarryDir(W);
            var wr = human.Bone("wrist." + side); var fr = Pose.Hand(human, side);
            Quaternion full = Quaternion.FromToRotation(fr.a, D); float ang = Quaternion.Angle(Quaternion.identity, full);
            if (ang > 0.1f && wr != null) wr.rotation = Quaternion.Slerp(Quaternion.identity, full, Mathf.Min(1f, 25f / ang)) * wr.rotation;
            fr = Pose.Hand(human, side);
            grip = fr.W + fr.f * 0.08f - fr.n * 0.03f;                     // the middle of the closed fist
            Vector3 fp = (fr.f - D * Vector3.Dot(fr.f, D)).normalized;       // the knuckles' direction, across the haft
            // sword: edge towards the knuckles (blade width on local x); tomahawk: head (holder +z) towards the knuckles
            Vector3 fwd = W == "sword" || W == "shield" ? Vector3.Cross(fp, D) : fp;
            return Quaternion.LookRotation(fwd, D);
        }
        void AddWeapon(string W)
        {
            Vector3 grip; var rot = GripIn("R", W, out grip);
            var g0 = Group("Weapon"); var g = g0;
            // axe: turned on its haft so the head points ahead (down the line of the cut), not sideways. Spear: gripped at the
            // middle. Staff: gripped a metre above its foot; the holder slides when the hand moves to the middle (running, fighting).
            if (W == "axe" || W == "spear" || W == "staff")
            {
                g = Group("Holder"); g.SetParent(g0, false);
                if (W == "axe") g.localRotation = Quaternion.Euler(0, -90, 0);
                if (W == "spear") g.localPosition = new Vector3(0, -0.55f, 0);
                if (W == "staff") { weaponSlideBase = 0.5f - grip.y; g.localPosition = new Vector3(0, weaponSlideBase, 0); weaponSlide = g; }     // its foot on the ground
            }
            if (W == "sword" && cfg.blade == "dagger")
            {   // a dagger: a narrow 30 cm blade, a small cross, a wrapped grip
                Part(MeshGen.Cylinder(0.013f, 0.012f, 0.09f, 8), "LEATHER2", g, Vector3.zero);
                Part(MeshGen.Box(0.09f, 0.014f, 0.022f), "STEEL", g, new Vector3(0, 0.052f, 0));
                Part(Blade(0.3f, 0.018f, 0.012f, 0.003f, 0.08f), "STEEL", g, new Vector3(0, 0.058f, 0));
            }
            else if (W == "sword" || W == "shield")
            {
                Part(MeshGen.Cylinder(0.015f, 0.014f, 0.11f, 10), "LEATHER2", g, Vector3.zero);
                Part(MeshGen.Sphere(0.024f, 10, 8), "GOLD", g, new Vector3(0, -0.07f, 0));
                Part(MeshGen.Box(0.2f, 0.018f, 0.03f), "GOLD", g, new Vector3(0, 0.065f, 0));
                foreach (float sx in new[] { -1f, 1f }) Part(MeshGen.Sphere(0.016f, 8, 6), "GOLD", g, new Vector3(sx * 0.1f, 0.065f, 0));
                Part(Blade(0.78f, 0.024f, 0.019f, 0.004f, 0.1f), "STEEL", g, new Vector3(0, 0.075f, 0));
                Part(MeshGen.Box(0.008f, 0.5f, 0.0125f), "DARK", g, new Vector3(0, 0.37f, 0));
            }
            else if (W == "axe" && cfg.blade == "sabre") Sabre(g);
            else if (W == "axe") Tomahawk(g);
            else if (W == "spear")
            {
                Part(MeshGen.Cylinder(0.015f, 0.013f, 2.1f, 10), "WOOD", g, new Vector3(0, 0.55f, 0));
                Part(Blade(0.24f, 0.03f, 0.032f, 0.006f, 0.14f), "STEEL", g, new Vector3(0, 1.63f, 0));
                Part(MeshGen.Cylinder(0.02f, 0.014f, 0.08f, 10), "STEEL", g, new Vector3(0, 1.6f, 0));
                Part(MeshGen.Cylinder(0.03f, 0.01f, 0.07f, 8), "RED", g, new Vector3(0, 1.53f, 0));
                Part(MeshGen.Cylinder(0f, 0.016f, 0.06f, 8), "STEEL", g, new Vector3(0, -0.52f, 0));
            }
            else if (W == "staff")
            {
                var pts = new List<Vector3>();
                for (int k = 0; k <= 24; k++) { float t = k / 24f; pts.Add(new Vector3(0.012f * Mathf.Sin(t * 9), -0.5f + t * 1.75f, 0.012f * Mathf.Cos(t * 7))); }
                Part(MeshGen.Tube(pts, 0.02f, 8), "WOOD", g, Vector3.zero);
                for (int k = 0; k < 4; k++)
                {
                    float a = k * Mathf.PI / 2;
                    var c = new List<Vector3> { new Vector3(0, 1.2f, 0), new Vector3(Mathf.Cos(a) * 0.05f, 1.3f, Mathf.Sin(a) * 0.05f), new Vector3(Mathf.Cos(a) * 0.035f, 1.42f, Mathf.Sin(a) * 0.035f) };
                    Part(MeshGen.Tube(c, 0.009f, 5), "WOOD", g, Vector3.zero);
                }
                var o = Part(MeshGen.Sphere(0.05f, 4, 2), "GLOW", g, new Vector3(0, 1.33f, 0));
                o.transform.localScale = new Vector3(0.8f, 1.2f, 0.8f); orb = o.transform;
                Part(MeshGen.Torus(0.025f, 0.006f, 16, 6), "GOLD", g, new Vector3(0, 1.2f, 0), Quaternion.Euler(90, 0, 0));
            }
            // convert world placement into model space (model scale is 1 while building)
            Attach("wrist.R", g0, model.InverseTransformPoint(grip), Quaternion.Inverse(model.rotation) * rot);
            weapon = g0;
            // where it sits in the other hand (the arm that held it can be torn off): the grip mirrored across the body
            {
                var wl = human.Bone("wrist.L"); var wr = human.Bone("wrist.R");
                if (wl != null && wr != null)
                {
                    weaponHome = g0.localPosition; weaponHomeRot = g0.localRotation;
                    Vector3 mp = model.InverseTransformPoint(g0.position); Quaternion mr = Quaternion.Inverse(model.rotation) * g0.rotation;
                    mp.x = -mp.x; mr = new Quaternion(mr.x, -mr.y, -mr.z, mr.w);
                    Vector3 wp = model.TransformPoint(mp); Quaternion wq = model.rotation * mr;
                    weaponAlt = wl.InverseTransformPoint(wp); weaponAltRot = Quaternion.Inverse(wl.rotation) * wq; hasAlt = true;
                }
            }
            if (W == "axe")
            {   // the second tomahawk, in the left hand
                Vector3 gripL; var rotL = GripIn("L", W, out gripL);
                var gL0 = Group("WeaponL"); var gL = Group("Holder"); gL.SetParent(gL0, false); gL.localRotation = Quaternion.Euler(0, -90, 0); if (cfg.blade == "sabre") Sabre(gL); else Tomahawk(gL);
                Attach("wrist.L", gL0, model.InverseTransformPoint(gripL), Quaternion.Inverse(model.rotation) * rotL);
                weaponL = gL0;
            }
        }

        void AddShield()
        {
            Vector3 e = human.Bone("lowerarm01.L").position, w = human.Bone("wrist.L").position;
            Vector3 d = (w - e).normalized;
            Vector3 outV = Vector3.forward; outV = (outV - d * Vector3.Dot(outV, d)).normalized;
            Vector3 up = Vector3.up; up = (up - outV * Vector3.Dot(up, outV)).normalized;
            Vector3 c = Vector3.Lerp(e, w, 0.5f) + outV * 0.07f - up * 0.03f;
            var g = Group("Shield");
            var s = new List<Vector2> { new Vector2(-0.24f, 0.26f), new Vector2(0.24f, 0.26f), new Vector2(0.24f, 0.02f) };
            s.AddRange(Bez(new Vector2(0.24f, 0.02f), new Vector2(0.22f, -0.26f), new Vector2(0, -0.42f), 10));
            s.AddRange(Bez(new Vector2(0, -0.42f), new Vector2(-0.22f, -0.26f), new Vector2(-0.24f, 0.02f), 10));
            s.RemoveAt(s.Count - 1); s.Add(new Vector2(-0.24f, 0.02f));
            Part(Bend(MeshGen.Extrude(s, 0.044f), 0.01f), "RED", g, Vector3.zero);
            var s2 = new List<Vector2>(); foreach (var p in s) s2.Add(p * 1.03f);
            Part(Bend(MeshGen.Extrude(s2, 0.008f), -0.002f), "STEEL", g, Vector3.zero);
            Part(MeshGen.Sphere(0.05f, 12, 8, Mathf.PI / 2), "STEEL", g, new Vector3(0, 0, 0.035f), Quaternion.Euler(90, 0, 0));
            Part(MeshGen.Box(0.05f, 0.6f, 0.01f), "GOLD", g, new Vector3(0, -0.06f, 0.036f));
            Part(MeshGen.Box(0.4f, 0.05f, 0.01f), "GOLD", g, new Vector3(0, 0.08f, 0.034f));
            Attach("lowerarm02.L", g, model.InverseTransformPoint(c), Quaternion.Inverse(model.rotation) * Quaternion.LookRotation(outV, up));
            shield = g;
        }
        static Mesh Bend(Mesh m, float dz)
        {
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i].z += dz - v[i].x * v[i].x * 0.6f;
            m.vertices = v; m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }

        public void Destroy() { if (go != null) UnityEngine.Object.Destroy(go); }
    }
}
