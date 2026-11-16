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

    /// Where two biomes meet they mix over 3-7 m - the width changes along the seam, and inside it the neighbour's grass,
    /// plants and flowers come in patches (noise), not as a line. Both sides use the same world-space noise, so the two
    /// halves of the mix fit together at the seam (about half and half there).
    public static class BiomeBlend
    {
        public const float FIELD_FOREST_X = -10f;      // the seam between the meadow (east) and the forest (west), world x
        public static float Width(float along) { return 3f + 4f * Mathf.Clamp01(Mathf.PerlinNoise(along * 0.13f + 3.7f, 0.5f) * 1.25f - 0.12f); }
        /// how likely a thing d metres inside this biome (world wx, wz) takes the neighbour's look
        public static float Other(float d, float wx, float wz)
        {
            float w = Width(wz); if (d >= w || d < 0) return d < 0 ? 0.5f : 0;
            float t = 1 - d / w, n = Mathf.PerlinNoise(wx * 0.31f + 11.3f, wz * 0.31f + 5.1f);
            return Mathf.Clamp01(0.5f * t + (n - 0.5f) * 1.3f * t);
        }
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
            var sz0 = Size(name); bool big = Mathf.Max(sz0.x, Mathf.Max(sz0.y, sz0.z)) * scale > 3.2f && LeafKind(name) < 0;
            // trees and dead trees, logs, stumps: their wood had a quarter of a big biome's characters
            bool woody = LeafKind(name) >= 0 || name.Contains("dead") || name.Contains("log") || name.Contains("stump");     // (houses, towers, the mill: coarser characters; not the trees)
            for (int i = 0; i < parts.Count; i++)
            {
                string key = parts[i].mat; string rk; if (recolour != null && recolour.TryGetValue(key, out rk)) key = rk;
                int kind = LeafKind(name);
                if (kind >= 0 && (key == "AUTUMN" || key == "AUTUMN2")) kind = key == "AUTUMN" ? 5 : 6;      // (autumn crowns: rust and gold)
                if ((key == "LEAF" || key == "AUTUMN" || key == "AUTUMN2") && kind >= 0)
                {
                    // the leaves are characters, not a model: a cloud of them over the crown's surface; a tree's crown still
                    // throws its shadow (the model drawn only into the shadow map)
                    var crown = new GameObject("Crown"); crown.transform.SetParent(h.transform, false);
                    var lc = new GameObject("Leaves"); lc.layer = GlyphLayer.LAYER; lc.transform.SetParent(crown.transform, false);
                    lc.AddComponent<MeshFilter>().sharedMesh = LeafMesh(name, i, scale, kind);
                    var lr = lc.AddComponent<MeshRenderer>(); lr.sharedMaterial = LeafMat(kind, recolour);
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
                    if (shadows && (kind <= 2 || kind >= 5))
                    {
                        var sh = MeshGen.Obj(MeshOf(name, i, scale), mats[key], crown.transform, Vector3.zero, Quaternion.identity, true, "CrownShadow");
                        sh.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                    }
                    continue;
                }
                var mat = matFor != null ? matFor(key) : null; if (mat == null) mat = mats[key];
                if (big) mat = mats.Big(mat);
                else if (woody && (key == "BARK" || key == "BIRCH" || key == "DARK" || key == "SNOW")) mat = mats.Mid(mat);
                MeshGen.Obj(MeshOf(name, i, scale), mat, h.transform, Vector3.zero, Quaternion.identity, shadows, name + "_" + key);
            }
            return h;
        }
        // ---- leaves as characters ----
        /// 0 broadleaf tree (oak, willow), 1 birch, 2 pine, 3 bush, 4 fern; -1: the model's green stays a model (moss, lily)
        static int LeafKind(string n)
        {
            if (n.StartsWith("oak") || n == "willow" || n == "willow2" || n.StartsWith("soak") || n == "apple" || n.StartsWith("aoak") || n == "awillow") return 0;
            if (n.StartsWith("birch") || n == "abirch") return 1;
            if (n.StartsWith("pine") || n.StartsWith("spine") || n == "apine") return 2;
            if (n.StartsWith("bush") || n == "sbush") return 3;
            if (n.StartsWith("fern")) return 4;
            return -1;
        }
        static readonly int[] LEAF_CAP = { 620, 560, 640, 110, 34, 560, 420 };
        static readonly float[] LEAF_SIZE = { 1.05f, 0.95f, 1.0f, 0.9f, 0.75f, 1.05f, 1.0f };
        static readonly Color[,] LEAF_COL = {
            { new Color(0.09f, 0.27f, 0.11f), new Color(0.38f, 0.60f, 0.22f) },     // oak
            { new Color(0.16f, 0.32f, 0.10f), new Color(0.62f, 0.72f, 0.28f) },     // birch: lighter, yellower
            { new Color(0.05f, 0.20f, 0.13f), new Color(0.20f, 0.44f, 0.28f) },     // pine: dark, bluish
            { new Color(0.08f, 0.26f, 0.12f), new Color(0.30f, 0.56f, 0.24f) },     // bush
            { new Color(0.10f, 0.32f, 0.12f), new Color(0.40f, 0.68f, 0.28f) },     // fern
            { new Color(0.36f, 0.10f, 0.04f), new Color(0.86f, 0.40f, 0.12f) },     // autumn: rust to orange
            { new Color(0.42f, 0.26f, 0.04f), new Color(0.95f, 0.76f, 0.22f) } };   // autumn: gold
        static readonly Dictionary<string, Mesh> leafCache = new Dictionary<string, Mesh>();
        static readonly Dictionary<int, Material> leafMats = new Dictionary<int, Material>();
        static Material LeafMat(int kind, Dictionary<string, string> recolour)
        {
            Material m; if (leafMats.TryGetValue(kind, out m) && m != null) return m;
            var sh = Shader.Find("AsciiHero/Leaves"); if (sh == null) { Debug.LogError("AsciiHero/Leaves shader not found"); return null; }
            m = new Material(sh); m.SetColor("_GrassA", LEAF_COL[kind, 0]); m.SetColor("_GrassB", LEAF_COL[kind, 1]); m.SetFloat("_GrassH", LEAF_SIZE[kind]);
            m.SetFloat("_LeafSee", kind <= 2 || kind >= 5 ? 1 : 0);          // (tree crowns clear away under the trees; bushes and ferns stay)
            leafMats[kind] = m; return m;
        }
        /// points scattered over the crown's surface (by area, the faces turned down mostly left out: the camera looks from
        /// above), at most LEAF_CAP per model; one glyph quad each
        static Mesh LeafMesh(string name, int part, float scale, int kind)
        {
            float sq = Mathf.Round(scale * 20f) / 20f; string key = name + "/" + part + "/" + sq;
            Mesh m; if (leafCache.TryGetValue(key, out m) && m != null) return m;
            var p = M[name][part]; int nt = p.t.Length / 3;
            var area = new float[nt]; var nrm = new Vector3[nt]; float tot = 0, y0 = 1e9f, y1 = -1e9f;
            foreach (var v in p.v) { y0 = Mathf.Min(y0, v.y); y1 = Mathf.Max(y1, v.y); }
            for (int t = 0; t < nt; t++)
            {
                Vector3 A = p.v[p.t[t * 3]], B = p.v[p.t[t * 3 + 1]], C = p.v[p.t[t * 3 + 2]];
                var c = Vector3.Cross(B - A, C - A); float ar = c.magnitude * 0.5f; nrm[t] = ar > 1e-8f ? c / (2 * ar) : Vector3.up;
                if (nrm[t].y < -0.55f) ar *= 0.15f;
                area[t] = ar; tot += ar;
            }
            int n = Mathf.Clamp(Mathf.RoundToInt(tot * sq * sq * 14f), 8, LEAF_CAP[kind]);
            var R = new System.Random(name.GetHashCode() ^ part * 7919);
            var cum = new float[nt]; float acc = 0; for (int t = 0; t < nt; t++) { acc += area[t]; cum[t] = acc; }
            var verts = new List<Vector3>(n * 4); var corners = new List<Vector2>(n * 4); var seeds = new List<Vector2>(n * 4); var nr = new List<Vector2>(n * 4); var dirs = new List<Vector3>(n * 4); var tris = new List<int>(n * 6);
            var leafPts = new List<Vector3>();
            Vector2[] CR = { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
            for (int k = 0; k < n; k++)
            {
                float u = (float)R.NextDouble() * acc; int lo = 0, hi = nt - 1;
                while (lo < hi) { int mid = (lo + hi) / 2; if (cum[mid] < u) lo = mid + 1; else hi = mid; }
                float r1 = (float)R.NextDouble(), r2 = (float)R.NextDouble(); if (r1 + r2 > 1) { r1 = 1 - r1; r2 = 1 - r2; }
                Vector3 A = p.v[p.t[lo * 3]], B = p.v[p.t[lo * 3 + 1]], C = p.v[p.t[lo * 3 + 2]];
                var q = (A + (B - A) * r1 + (C - A) * r2 + nrm[lo] * 0.04f) * sq;
                float hk = (q.y / sq - y0) / Mathf.Max(0.05f, y1 - y0), sd = (float)R.NextDouble();
                int o = verts.Count;
                for (int c = 0; c < 4; c++) { verts.Add(q); corners.Add(CR[c]); seeds.Add(new Vector2(sd, hk)); nr.Add(new Vector2(nrm[lo].y, 0)); dirs.Add(Vector3.up); }
                tris.Add(o); tris.Add(o + 2); tris.Add(o + 1); tris.Add(o); tris.Add(o + 3); tris.Add(o + 2);
                leafPts.Add(q);
            }
            if (kind == 3 && leafPts.Count > 6)
            {
                // a bush: a short stem, and branches from it out into the leaves (characters as well, swaying with the leaves:
                // the sway grows with the height, so a branch bends from the ground up)
                float H = (y1 - y0) * sq; var top = new Vector3(0, H * 0.38f, 0);
                void Line(Vector3 a0, Vector3 b0)
                {
                    var d = b0 - a0; float len = d.magnitude; if (len < 0.05f) return; d /= len;
                    for (float u = 0; u <= len; u += 0.085f)
                    {
                        var q = a0 + d * u; float hk = (q.y - y0 * sq) / Mathf.Max(0.05f, H); int o = verts.Count;
                        for (int c = 0; c < 4; c++) { verts.Add(q); corners.Add(CR[c]); seeds.Add(new Vector2((float)R.NextDouble(), hk)); nr.Add(new Vector2(0.3f, 1)); dirs.Add(d); }
                        tris.Add(o); tris.Add(o + 2); tris.Add(o + 1); tris.Add(o); tris.Add(o + 3); tris.Add(o + 2);
                    }
                }
                Line(new Vector3(0, 0.02f, 0), top);
                int nb = 4 + R.Next(3);
                for (int k = 0; k < nb; k++)
                {
                    var to = leafPts[R.Next(leafPts.Count)]; var from = new Vector3(0, H * (0.1f + 0.28f * (float)R.NextDouble()), 0);
                    var mid = Vector3.Lerp(from, to, 0.5f) + new Vector3(0, 0.06f, 0);
                    Line(from, mid); Line(mid, Vector3.Lerp(from, to, 0.85f));
                }
            }
            m = new Mesh { name = "leaves_" + name };
            m.SetVertices(verts); m.SetUVs(0, corners); m.SetUVs(1, seeds); m.SetUVs(2, nr); m.SetUVs(3, dirs); m.SetTriangles(tris, 0);
            m.RecalculateBounds(); var b = m.bounds; b.Expand(0.8f); m.bounds = b;
            GlyphLayer.count += n;
            leafCache[key] = m; return m;
        }

        /// the model's size (x, y, z) at scale 1
        static readonly Dictionary<string, Vector3> sizes = new Dictionary<string, Vector3>();
        public static Vector3 Size(string name)
        {
            Vector3 sz; if (sizes.TryGetValue(name, out sz)) return sz;
            sz = SizeOf(name); sizes[name] = sz; return sz;
        }
        static Vector3 SizeOf(string name)
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
