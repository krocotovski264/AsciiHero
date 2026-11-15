// Nature props and the armour piles, shared by the outdoor locations.
// The models are Quaternius' CC0 packs (Ultimate Nature, Medieval Village), converted offline into
// Resources/naturemeshes.bytes: per model one mesh per game material (BARK, LEAF, WHEAT, ...), in metres,
// standing on y = 0. Placing a model makes one child per material; the scale is baked into the vertices (the
// characters are laid out on the mesh as it is) and the meshes are cached, so the same tree placed many times shares
// its mesh and its character layout.
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public class ArmourPile
    {
        public Vector3 pos; public GameObject root;
        public readonly GameObject[] items = new GameObject[5];   // head, armL, armR, legL, legR (null = not in this pile)
        /// take the piece for limb l; true if the pile had one
        public bool Take(int l) { if (items[l] == null) return false; items[l].SetActive(false); items[l] = null; return true; }
        public int Count { get { int n = 0; foreach (var g in items) if (g != null) n++; return n; } }
    }

    public static class NatureLib
    {
        struct Part { public string mat; public Vector3[] v; public int[] t; }
        static Dictionary<string, List<Part>> M;
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();
        static void Load()
        {
            if (M != null) return;
            M = new Dictionary<string, List<Part>>();
            var ta = Resources.Load<TextAsset>("naturemeshes"); if (ta == null) { Debug.LogError("naturemeshes.bytes not found"); return; }
            var b = ta.bytes; int o = 8, n = System.BitConverter.ToInt32(b, 4);
            for (int k = 0; k < n; k++)
            {
                int ln = b[o]; string nm = System.Text.Encoding.ASCII.GetString(b, o + 1, ln); o += 1 + ln;
                int np = b[o]; o += 1; var parts = new List<Part>();
                for (int q = 0; q < np; q++)
                {
                    int lm = b[o]; string mat = System.Text.Encoding.ASCII.GetString(b, o + 1, lm); o += 1 + lm;
                    int nv = System.BitConverter.ToInt32(b, o), nt = System.BitConverter.ToInt32(b, o + 4); o += 8;
                    var v = new Vector3[nv]; var t = new int[nt * 3];
                    for (int i = 0; i < nv; i++, o += 12) v[i] = new Vector3(System.BitConverter.ToSingle(b, o), System.BitConverter.ToSingle(b, o + 4), System.BitConverter.ToSingle(b, o + 8));
                    for (int i = 0; i < nt * 3; i++, o += 2) t[i] = System.BitConverter.ToUInt16(b, o);
                    parts.Add(new Part { mat = mat, v = v, t = t });
                }
                M[nm] = parts;
            }
        }
        public static bool Has(string name) { Load(); return M.ContainsKey(name); }
        static Mesh MeshOf(string name, int part, float scale)
        {
            float sq = Mathf.Round(scale * 20f) / 20f;           // (scales are rounded to 5 %, so instances share meshes)
            string key = name + "/" + part + "/" + sq;
            Mesh m; if (cache.TryGetValue(key, out m) && m != null) return m;
            var p = M[name][part]; var v = new Vector3[p.v.Length];
            for (int i = 0; i < v.Length; i++) v[i] = p.v[i] * sq;
            m = new Mesh { name = name + "_" + p.mat }; m.vertices = v; m.uv = new Vector2[v.Length]; m.triangles = p.t;
            m.RecalculateNormals(); m.RecalculateBounds(); MeshGen.AddGlyphUV(m, false);
            cache[key] = m; return m;
        }
        /// put a model at pos (parent's space), turned by yaw; recolour: game material -> another (e.g. FLOWERB -> RED)
        public static GameObject Place(string name, Mats mats, Transform parent, Vector3 pos, float yaw, float scale,
            System.Func<string, Material> matFor = null, bool shadows = true, Dictionary<string, string> recolour = null)
        {
            Load();
            var h = new GameObject(name); h.transform.SetParent(parent, false); h.transform.localPosition = pos; h.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            List<Part> parts; if (!M.TryGetValue(name, out parts)) { Debug.LogWarning("no model " + name); return h; }
            for (int i = 0; i < parts.Count; i++)
            {
                string key = parts[i].mat; string rk; if (recolour != null && recolour.TryGetValue(key, out rk)) key = rk;
                var mat = matFor != null ? matFor(key) : null; if (mat == null) mat = mats[key];
                MeshGen.Obj(MeshOf(name, i, scale), mat, h.transform, Vector3.zero, Quaternion.identity, shadows, name + "_" + key);
            }
            return h;
        }
        /// the model's size (x, y, z) at scale 1
        public static Vector3 Size(string name)
        {
            Load(); List<Part> parts; if (!M.TryGetValue(name, out parts)) return Vector3.one;
            Vector3 lo = Vector3.one * 1e9f, hi = -lo;
            foreach (var p in parts) foreach (var v in p.v) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
            return hi - lo;
        }

        /// See-through copies of materials (tree crowns and trunks dissolve around the hero when they hide him)
        static readonly Dictionary<Material, Material> see = new Dictionary<Material, Material>();
        public static Material SeeThrough(Material m)
        {
            Material s; if (see.TryGetValue(m, out s)) return s;
            s = new Material(m) { name = m.name + "_see" }; s.SetFloat("_SeeThrough", 1); see[m] = s; return s;
        }

        // ---------------------------------------------------------------- armour piles
        /// one pile: a breastplate marks the spot, the pieces it holds (limbs l in 'have') lie around it
        public static ArmourPile Pile(Mats mats, Transform T, Vector3 pos, float yaw, IList<int> have)
        {
            var pile = new ArmourPile { pos = pos, root = new GameObject("ArmourPile") };
            var P = pile.root.transform; P.SetParent(T, false); P.localPosition = pos; P.localRotation = Quaternion.Euler(0, yaw, 0);
            var chest = MeshGen.Obj(MeshGen.Sphere(0.2f, 10, 6, Mathf.PI / 2), mats["PLATE"], P, new Vector3(0, 0.02f, 0), Quaternion.identity); chest.transform.localScale = new Vector3(1, 0.8f, 1.3f);
            MeshGen.Obj(MeshGen.Box(0.34f, 0.04f, 0.05f), mats["LEATHER2"], P, new Vector3(0, 0.12f, 0.2f), Quaternion.identity);
            foreach (int l in have)
            {
                GameObject g;
                switch (l)
                {
                    case 0:
                        g = MeshGen.Obj(MeshGen.Sphere(0.13f, 10, 8), mats["PLATE"], P, new Vector3(0.3f, 0.13f, 0.28f), Quaternion.identity);
                        MeshGen.Obj(MeshGen.Box(0.14f, 0.02f, 0.03f), mats["DARK"], g.transform, new Vector3(0, 0.02f, 0.12f), Quaternion.identity); break;
                    case 1: g = MeshGen.Obj(MeshGen.Cylinder(0.04f, 0.05f, 0.6f, 8), mats["PLATE"], P, new Vector3(-0.38f, 0.06f, 0.1f), Quaternion.Euler(90, 0, 30)); break;
                    case 2: g = MeshGen.Obj(MeshGen.Cylinder(0.04f, 0.05f, 0.6f, 8), mats["PLATE"], P, new Vector3(0.42f, 0.06f, -0.15f), Quaternion.Euler(90, 0, -40)); break;
                    case 3: g = MeshGen.Obj(MeshGen.Cylinder(0.055f, 0.075f, 0.85f, 8), mats["PLATE"], P, new Vector3(-0.25f, 0.08f, -0.42f), Quaternion.Euler(90, 0, 75)); break;
                    default: g = MeshGen.Obj(MeshGen.Cylinder(0.055f, 0.075f, 0.85f, 8), mats["PLATE"], P, new Vector3(0.15f, 0.08f, -0.55f), Quaternion.Euler(90, 0, 100)); break;
                }
                pile.items[l] = g;
            }
            return pile;
        }
        /// the rules: finds = floor(k / 3) (k = squares + triangles / 2 of 10 x 10 m), 1-5 different pieces each
        /// (30 / 30 / 20 / 12 / 8 %), at least 6 m apart, 4 m from the spawn; free(x, z, r) says if a spot is free
        public static void RespawnPiles(Location L, Mats mats, float k, System.Func<float, float, float, bool> free, System.Random R)
        {
            foreach (var p in L.piles) if (p.root != null) Object.Destroy(p.root);
            L.piles.Clear();
            int n = Mathf.FloorToInt(k / 3f + 1e-4f);
            for (int i = 0; i < n; i++)
            {
                double r = R.NextDouble(); int ne = r < 0.3 ? 1 : r < 0.6 ? 2 : r < 0.8 ? 3 : r < 0.92 ? 4 : 5;
                var all = new List<int> { 0, 1, 2, 3, 4 }; var have = new List<int>();
                for (int q = 0; q < ne; q++) { int j = R.Next(all.Count); have.Add(all[j]); all.RemoveAt(j); }
                for (int tries = 0; tries < 300; tries++)
                {
                    float x = Mathf.Lerp(L.bounds.xMin + 1.5f, L.bounds.xMax - 1.5f, (float)R.NextDouble()), z = Mathf.Lerp(L.bounds.yMin + 1.5f, L.bounds.yMax - 1.5f, (float)R.NextDouble());
                    if ((new Vector2(x, z) - new Vector2(L.spawn.x, L.spawn.z)).magnitude < 4f || !free(x, z, 0.6f)) continue;
                    bool far = true; foreach (var p in L.piles) if ((new Vector2(x - p.pos.x, z - p.pos.z)).magnitude < 6f) far = false;
                    if (!far) continue;
                    var pile = Pile(mats, L.root.transform, new Vector3(x, 0, z), (float)R.NextDouble() * 360, have);
                    GlyphLayer.Build(pile.root);
                    L.piles.Add(pile); break;
                }
            }
        }
    }
}
