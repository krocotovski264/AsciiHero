// The remaining biomes of the environment reference, as playable mock-ups (20 x 20 m each), laid out south of the
// meadow and the forest:
//
//            x = -40          -20            0            20
//   z =   0               [forest]      [meadow]
//   z = +20  [toxic swamp]  [swamp]    [bright city]
//   z = +40  [snow heights] [autumn]   [grim city]   [wastes]
//   z = +60                                         [crystal mines]
//
// Each is built from the same kit (ground, water of its kind, paths between its open sides, grass of its colour, props
// from the CC0 packs, its night: lights, motes, mist); the sides that meet a neighbour are open (with 3-7 m of mixing of
// their grass), the others closed by the biome's own wall (dead trees, cliffs, houses, rock).
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public class Mockup : Location
    {
        public const float HX = 10, HZ = 10, WY = 0.05f;
        public string kind, title;
        System.Random R; float Rnd() { return (float)R.NextDouble(); } float Rr(float a, float b) { return a + (b - a) * Rnd(); }
        /// open sides: N (-z, up on the screen), S (+z), W (-x, right on the screen), E (+x)
        public bool openN, openS, openW, openE;
        /// what lies beyond each open side (for the grass mixing): N, S, W, E
        public string[] nb = new string[4];

        // ---- water: pools of a kind (0 lake, 1 swamp, 2 puddle, 3 acid, 4 mirror lake) ----
        class Pool { public Vector2 c; public float rx, rz, ang; public int kind; }
        readonly List<Pool> pools = new List<Pool>();
        float PoolK(Pool p, float x, float z)
        {
            float dx = x - p.c.x, dz = z - p.c.y, ca = Mathf.Cos(p.ang), sa = Mathf.Sin(p.ang);
            float u = (dx * ca + dz * sa) / p.rx, v = (-dx * sa + dz * ca) / p.rz;
            float a = Mathf.Atan2(v, u), wob = 1 + 0.09f * Mathf.Sin(a * 3 + p.c.x) + 0.05f * Mathf.Sin(a * 5 + p.c.y);
            return Mathf.Sqrt(u * u + v * v) / wob;
        }
        float WaterK(float x, float z) { float k = 9; foreach (var p in pools) k = Mathf.Min(k, PoolK(p, x, z)); return k; }
        public override bool HasWater => pools.Count > 0;
        public bool Underground => kind == "crystal";
        /// (acid does not freeze, nor the water under the ground)
        public override float ShoreK(float x, float z)
        {
            if (Underground || walks.Count > 0 && OnWalk(x, z)) return -1;
            float k = 9; foreach (var p in pools) if (p.kind != 3) k = Mathf.Min(k, PoolK(p, x, z));
            return k < 0.97f ? k : -1;
        }
        public override List<Bounds> MirrorWater()
        {
            var l = new List<Bounds>();
            foreach (var p in pools) if (p.kind == 0 || p.kind == 4) { float r = Mathf.Max(p.rx, p.rz) * 1.15f; l.Add(new Bounds(new Vector3(p.c.x + origin.x, WY, p.c.y + origin.z), new Vector3(2 * r, 0.2f, 2 * r))); }
            return l;
        }
        public override bool WaterIn(Rect w)
        {
            foreach (var p in pools) { float r = Mathf.Max(p.rx, p.rz) * 1.15f; if (w.Overlaps(new Rect(p.c.x + origin.x - r, p.c.y + origin.z - r, 2 * r, 2 * r))) return true; }
            return false;
        }
        public override float WaterY => WY;
        readonly List<Vector2[]> walks = new List<Vector2[]>();      // boardwalks: dry over the water
        bool OnWalk(float x, float z)
        {
            var q = new Vector2(x, z);
            foreach (var w in walks) { Vector2 a = w[0], ab = w[1] - a; float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude); if ((q - (a + ab * t)).magnitude < 0.55f) return true; }
            return false;
        }
        public override bool InWater(float x, float z) { return WaterK(x, z) < 0.97f && !OnWalk(x, z); }
        public override float Depth(float x, float z)
        {
            float d = 0; if (walks.Count > 0 && OnWalk(x, z)) return 0;
            foreach (var p in pools)
            {
                float k = PoolK(p, x, z); if (k >= 0.97f) continue; float u = (0.97f - k) / 0.97f;
                float dd = p.kind == 0 ? WaterDepth.Lake(u, 0.4f, 2.2f) : p.kind == 1 ? WaterDepth.Swamp(u, u > 0.75f && p.rx > 1.6f ? 0.8f : 0) : p.kind == 2 ? WaterDepth.Puddle(u, 0.07f)
                         : p.kind == 3 ? WaterDepth.Swamp(u) * 0.8f : WaterDepth.Lake(u, 0.3f, 1.6f);
                d = Mathf.Max(d, dd);
            }
            return d;
        }
        // (swamp water is thick: a third slower on top of the depth)
        public override float SpeedK(float x, float z) { foreach (var p in pools) if ((p.kind == 1 || p.kind == 3) && PoolK(p, x, z) < 0.9f) return 0.7f; return 1; }

        // ---- paths between the open sides ----
        readonly List<Vector2[]> paths = new List<Vector2[]>();
        float PathD(float x, float z)
        {
            float best = 99; var q = new Vector2(x, z);
            foreach (var line in paths)
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    Vector2 a = line[i], ab = line[i + 1] - a; float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                    best = Mathf.Min(best, (q - (a + ab * t)).magnitude);
                }
            return best;
        }
        bool Free(float x, float z, float r, float pathGap = 1.1f)
        {
            if (Mathf.Abs(x) > HX - 0.3f || Mathf.Abs(z) > HZ - 0.3f) return false;
            if (pools.Count > 0 && WaterK(x, z) < 1.15f + r * 0.3f) return false;
            if (PathD(x, z) < pathGap + r) return false;
            if ((new Vector2(x - spawn.x, z - spawn.z)).magnitude < 1.4f + r) return false;
            foreach (var c in colliders) if (c.circle && (new Vector2(x - c.x, z - c.z)).magnitude < c.r + r + 0.15f) return false;
            foreach (var c in colliders) if (!c.circle && x > c.minx - r && x < c.maxx + r && z > c.minz - r && z < c.maxz + r) return false;
            return true;
        }
        Vector2 Spot(float r, float pathGap = 1.1f, float x0 = -HX + 0.8f, float x1 = HX - 0.8f, float z0 = -HZ + 0.8f, float z1 = HZ - 0.8f)
        {
            for (int k = 0; k < 150; k++) { float x = Rr(x0, x1), z = Rr(z0, z1); if (Free(x, z, r, pathGap)) return new Vector2(x, z); }
            return new Vector2(float.NaN, 0);
        }

        Mats M; Transform T;
        GameObject Put(string name, Vector2 p, float s, float yaw = -1, float y = 0, bool shadows = true, Dictionary<string, string> recolour = null, System.Func<string, Material> matFor = null)
        { return NatureLib.Place(name, M, T, new Vector3(p.x, y, p.y), yaw < 0 ? Rr(0, 360) : yaw, s, matFor, shadows, recolour); }
        void Tree(string name, Vector2 p, float s, float colR = 0.3f)
        {
            var g = Put(name, p, s, -1, -0.02f, true, null, k => k == "BARK" || k == "BIRCH" || k == "DARK" ? NatureLib.SeeThrough(M[k]) : null);
            var cr = g.transform.Find("Crown"); if (cr != null) crowns.Add(cr.gameObject);
            colliders.Add(Collider2.Circle(p.x, p.y, colR * s)); occluders.Add(new Vector4(p.x, p.y, 1.5f * s, 5f * s));
        }
        GameObject Obj(Mesh m, Material mat, Vector3 pos, Quaternion q, bool shadows = true, string name = "part")
        {
            // big pieces (cliffs, dunes, the fountain, walls of rock) get coarser characters
            var b = m.bounds.size; if (Mathf.Max(b.x, Mathf.Max(b.y, b.z)) > 1.9f && name != "Ground" && name != "Pool") mat = M.Big(mat);
            return MeshGen.Obj(m, mat, T, pos, q, shadows, name);
        }
        Light Lamp(Vector3 p, Color c, float intensity, float range)
        {
            var g = new GameObject("Lamp"); g.transform.SetParent(T, false); g.transform.localPosition = p;
            var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            return l;
        }
        class Flick { public Light l; public float baseI, ph, kind; }
        readonly List<Flick> flicks = new List<Flick>();
        public override void Update(float t)
        {
            foreach (var f in flicks)
            {
                if (f.l == null || !f.l.enabled) continue;
                float k = f.kind == 0 ? 0.85f + 0.15f * Mathf.PerlinNoise(t * 3f, f.ph)                                                    // a flame
                        : f.kind == 1 ? (Mathf.PerlinNoise(t * 2.5f, f.ph) > 0.28f ? 0.9f + 0.1f * Mathf.Sin(t * 40) : 0.08f)            // a failing street lamp
                        : 0.55f + 0.45f * Mathf.Sin(t * (1.1f + f.ph) + f.ph * 7);                                                         // a crystal, pulsing
                f.l.intensity = f.baseI * k;
            }
        }
        public override void Cutaway(Vector3 camOff) { OccluderSeeThrough(camOff); }

        public static Mockup Build(Mats mats, string kind, int seed, Vector3 at, bool n, bool s, bool w, bool e, string[] neighbours)
        {
            var L = new Mockup { kind = kind, R = new System.Random(seed), openN = n, openS = s, openW = w, openE = e };
            L.origin = at;          // (known while building: the mixing at the seams uses world coordinates)
            if (neighbours != null) L.nb = neighbours;
            L.M = mats; L.Construct(); return L;
        }

        // ---- the kit ----
        void Ground(Material m)
        {
            const int SUB = 12; float TS = 20f / 5, Q = TS / SUB;
            for (int ix = 0; ix < 5; ix++)
                for (int iz = 0; iz < 5; iz++)
                {
                    float x0 = -HX + ix * TS, z0 = -HZ + iz * TS;
                    var vs = new List<Vector3>(); var ts = new List<int>();
                    for (int a = 0; a < SUB; a++)
                        for (int b = 0; b < SUB; b++)
                        {
                            float qx = x0 + a * Q, qz = z0 + b * Q;
                            if (pools.Count > 0 && Mathf.Max(Mathf.Max(WaterK(qx, qz), WaterK(qx + Q, qz)), Mathf.Max(WaterK(qx, qz + Q), WaterK(qx + Q, qz + Q))) < 0.98f) continue;
                            int o = vs.Count;
                            vs.Add(new Vector3(qx, 0, qz)); vs.Add(new Vector3(qx, 0, qz + Q)); vs.Add(new Vector3(qx + Q, 0, qz + Q)); vs.Add(new Vector3(qx + Q, 0, qz));
                            ts.Add(o); ts.Add(o + 1); ts.Add(o + 2); ts.Add(o); ts.Add(o + 2); ts.Add(o + 3);
                        }
                    if (vs.Count == 0) continue;
                    var gm = new Mesh(); gm.SetVertices(vs); gm.uv = new Vector2[vs.Count]; gm.SetTriangles(ts, 0);
                    gm.RecalculateNormals(); gm.RecalculateBounds(); MeshGen.AddGlyphUV(gm, false);
                    Obj(gm, m, Vector3.zero, Quaternion.identity, false, "Ground");
                }
        }
        void AddPool(Vector2 c, float rx, float rz, int k) { pools.Add(new Pool { c = c, rx = rx, rz = rz, ang = Rr(0, 3.14f), kind = k }); }
        /// the water of every pool: an opaque surface for the light and depth, and the water's characters on it
        void Water(Color deep, Color crest, Material surface, bool underground = false)
        {
            foreach (var p in pools)
            {
                const int RINGS = 8, SEG = 40; var vs = new List<Vector3> { new Vector3(p.c.x, WY, p.c.y) }; var ts = new List<int>();
                float ca = Mathf.Cos(p.ang), sa = Mathf.Sin(p.ang);
                for (int r = 1; r <= RINGS; r++)
                    for (int s = 0; s < SEG; s++)
                    {
                        float a = s * Mathf.PI * 2 / SEG, f = r / (float)RINGS * 1.01f, wob = 1 + 0.09f * Mathf.Sin(a * 3 + p.c.x) + 0.05f * Mathf.Sin(a * 5 + p.c.y);
                        float u = Mathf.Cos(a) * p.rx * f * wob, v = Mathf.Sin(a) * p.rz * f * wob;
                        vs.Add(new Vector3(p.c.x + u * ca - v * sa, WY, p.c.y + u * sa + v * ca));
                    }
                for (int s = 0; s < SEG; s++) { ts.Add(0); ts.Add(1 + (s + 1) % SEG); ts.Add(1 + s); }
                for (int r = 1; r < RINGS; r++)
                    for (int s = 0; s < SEG; s++)
                    {
                        int a0 = 1 + (r - 1) * SEG + s, a1 = 1 + (r - 1) * SEG + (s + 1) % SEG, b0 = a0 + SEG, b1 = a1 + SEG;
                        ts.Add(a0); ts.Add(a1); ts.Add(b1); ts.Add(a0); ts.Add(b1); ts.Add(b0);
                    }
                var wm = new Mesh { name = "pool" }; wm.SetVertices(vs); wm.SetTriangles(ts, 0); wm.RecalculateNormals(); wm.RecalculateBounds();
                var go = Obj(wm, surface, Vector3.zero, Quaternion.identity, false, "Pool");
                var wp = new List<Vector3>(); var wsd = new List<Vector2>(); float ext = Mathf.Max(p.rx, p.rz) * 1.2f;
                for (float z = p.c.y - ext; z <= p.c.y + ext; z += 0.13f)
                    for (float x = p.c.x - ext; x <= p.c.x + ext; x += 0.1f)
                    { float kk = PoolK(p, x, z); if (kk > 1.0f) continue; wp.Add(new Vector3(x, WY, z)); wsd.Add(new Vector2(kk, Rnd())); }
                GlyphLayer.MakeWater(go.transform, wp, wsd, new Vector4(underground ? 1 : 0, 0, 0, 0), deep, crest);
            }
        }
        /// a path from the middle to the middle of each open side, with a bend
        void Paths(Material m, float hw, Material ground)
        {
            var mid = new Vector2(Rr(-1.5f, 1.5f), Rr(-1.5f, 1.5f));
            void To(Vector2 end) { var bend = Vector2.Lerp(mid, end, 0.5f) + new Vector2(Rr(-1.6f, 1.6f), Rr(-1.6f, 1.6f)); var line = new[] { mid, bend, end }; paths.Add(line); Forest.PathStrip(T, m, line, hw, 0.006f, "Path", ground); }
            if (openN) To(new Vector2(Rr(-2, 2), -HZ - 0.5f));
            if (openS) To(new Vector2(Rr(-2, 2), HZ + 0.5f));
            if (openW) To(new Vector2(-HX - 0.5f, Rr(-2, 2)));
            if (openE) To(new Vector2(HX + 0.5f, Rr(-2, 2)));
            spawn = new Vector3(mid.x, 0, mid.y);
        }
        public struct GrassSpec { public Color a, b; public float h, den; }
        public static GrassSpec Spec(string k)
        {
            switch (k)
            {
                case "field": return new GrassSpec { a = new Color(0.16f, 0.50f, 0.24f), b = new Color(0.50f, 0.78f, 0.36f), h = 1.35f, den = 90 };
                case "forest": return new GrassSpec { a = new Color(0.12f, 0.40f, 0.20f), b = new Color(0.36f, 0.62f, 0.28f), h = 1.15f, den = 38 };
                case "swamp": return new GrassSpec { a = new Color(0.18f, 0.24f, 0.09f), b = new Color(0.40f, 0.46f, 0.18f), h = 1.6f, den = 45 };
                case "toxic": return new GrassSpec { a = new Color(0.24f, 0.22f, 0.08f), b = new Color(0.44f, 0.46f, 0.12f), h = 0.9f, den = 12 };
                case "grim": return new GrassSpec { a = new Color(0.22f, 0.22f, 0.12f), b = new Color(0.38f, 0.36f, 0.2f), h = 0.7f, den = 5 };
                case "wastes": return new GrassSpec { a = new Color(0.40f, 0.32f, 0.15f), b = new Color(0.62f, 0.52f, 0.28f), h = 0.7f, den = 5 };
                case "autumn": return new GrassSpec { a = new Color(0.42f, 0.14f, 0.05f), b = new Color(0.86f, 0.50f, 0.14f), h = 0.55f, den = 55 };
                case "snow": return new GrassSpec { a = new Color(0.55f, 0.55f, 0.5f), b = new Color(0.75f, 0.74f, 0.68f), h = 0.6f, den = 3 };
                default: return new GrassSpec { den = 0 };
            }
        }
        /// grass of this biome, and near each open side, in patches over 3-7 m, the neighbour's
        void Grass(float pathGap = 0.8f)
        {
            var own = Spec(kind); var groups = new Dictionary<string, KeyValuePair<List<Vector3>, List<float>>>();
            void Add(string k, Vector3 p) { KeyValuePair<List<Vector3>, List<float>> g; if (!groups.TryGetValue(k, out g)) { g = new KeyValuePair<List<Vector3>, List<float>>(new List<Vector3>(), new List<float>()); groups[k] = g; } g.Key.Add(p); g.Value.Add(Rnd()); }
            bool Ok(float x, float z)
            {
                if (pools.Count > 0 && WaterK(x, z) < 1.05f) return false;
                if (!Forest.RoadGrass(x, z, PathD(x, z), pathGap)) return false;
                foreach (var c in colliders) { if (c.circle ? (new Vector2(x - c.x, z - c.z)).magnitude < c.r * 0.8f : x > c.minx && x < c.maxx && z > c.minz && z < c.maxz) return false; }
                return true;
            }
            // the neighbour of the side this point is nearest to (if open), and how far in it is
            string Near(float x, float z, out float d)
            {
                d = 99; string k = null;
                if (openN && nb[0] != null && z + HZ < d) { d = z + HZ; k = nb[0]; }
                if (openS && nb[1] != null && HZ - z < d) { d = HZ - z; k = nb[1]; }
                if (openW && nb[2] != null && x + HX < d) { d = x + HX; k = nb[2]; }
                if (openE && nb[3] != null && HX - x < d) { d = HX - x; k = nb[3]; }
                return k;
            }
            float maxDen = own.den;
            foreach (var k in nb) if (k != null) maxDen = Mathf.Max(maxDen, Spec(k).den);
            int n = Mathf.RoundToInt(4 * HX * HZ * maxDen);
            for (int i = 0; i < n; i++)
            {
                float x = Rr(-HX, HX), z = Rr(-HZ, HZ); float d; string nk = Near(x, z, out d);
                string k = kind;
                if (nk != null && Rnd() < BiomeBlend.Other(d, x + origin.x, z + origin.z)) k = nk;
                var sp = Spec(k); if (sp.den <= 0 || Rnd() > sp.den / maxDen || !Ok(x, z)) continue;
                Add(k, new Vector3(x, 0, z));
            }
            foreach (var kv in groups) { var sp = Spec(kv.Key); GlyphLayer.MakeGrass(kv.Value.Key, kv.Value.Value, T, sp.a, sp.b, sp.h); }
        }
        void Motes(int mode, int n, Color col, float y0, float y1, Vector4 prm, Vector3? scale = null)
        {
            var pts = new List<Vector3>(); var sd = new List<Vector2>();
            for (int i = 0; i < n; i++) { pts.Add(new Vector3(Rr(-HX, HX), Rr(y0, y1), Rr(-HZ, HZ))); sd.Add(new Vector2(mode == 4 ? Rnd() : 0, Rnd())); }
            GlyphLayer.MakeMotes(T, Vector3.zero, Quaternion.identity, scale ?? Vector3.one, pts, sd, mode, col, prm);
        }
        /// along each closed side: the biome's own wall (its things put at steps, with colliders)
        void Edges(System.Action<Vector2, int> put, float step)
        {
            for (int side = 0; side < 4; side++)
            {
                bool open = side == 0 ? openN : side == 1 ? openS : side == 2 ? openW : openE; if (open) continue;
                for (float u = -HX + 0.3f; u <= HX - 0.3f; u += step * Rr(0.8f, 1.2f))
                {
                    float o = Rr(-0.35f, 0.35f);
                    var p = side == 0 ? new Vector2(u, -HZ + 0.4f + o) : side == 1 ? new Vector2(u, HZ - 0.4f + o) : side == 2 ? new Vector2(-HX + 0.4f + o, u) : new Vector2(HX - 0.4f + o, u);
                    put(p, side);
                }
            }
        }
        Mesh Prism(float r, float h, int seg = 6) { return MeshGen.Cylinder(r, r * 0.35f, h, seg); }

        void Construct()
        {
            id = kind;
            root = new GameObject("Mockup_" + kind); T = root.transform;
            switch (kind)
            {
                case "swamp": Swamp(); break;
                case "toxic": Toxic(); break;
                case "bright": BrightCity(); break;
                case "grim": GrimCity(); break;
                case "wastes": Wastes(); break;
                case "crystal": Crystal(); break;
                case "autumn": Autumn(); break;
                case "snow": Snow(); break;
            }
            bounds = Rect.MinMaxRect(openW ? -HX - 2.6f : -HX + 0.5f, openN ? -HZ - 2.6f : -HZ + 0.5f, openE ? HX + 2.6f : HX - 0.5f, openS ? HZ + 2.6f : HZ - 0.5f);
        }

        // ================= 2.1 the swamp =================
        void Swamp()
        {
            title = "Болото";
            ambSky = Mats.Hex(0x6a7a70) * 0.32f; ambEq = Mats.Hex(0x34402c) * 0.32f; ambGround = Mats.Hex(0x10140a) * 0.35f; moonK = 0.7f; heroLightK = 1.4f; exposure = 1.35f;
            Paths(M.Tint("MUD", 0x2a2416), 0.7f, M.Tint("GROUND", 0x1c2212));
            for (int i = 0; i < 6; i++) { var p = Spot(1.6f, 1.6f); if (!float.IsNaN(p.x)) AddPool(p, Rr(1.2f, 2.6f), Rr(0.9f, 1.8f), 1); }
            Ground(M.Tint("GROUND", 0x1c2212));
            Water(new Color(0.05f, 0.07f, 0.035f), new Color(0.24f, 0.28f, 0.13f), M.Tint("WATER", 0x161a0c, 0x040602));
            // duckweed and lilies on the water, sedge hummocks and reeds at the edges
            foreach (var p in pools)
            {
                for (int i = 0; i < 5; i++) { float a = Rr(0, 6.28f), f = Rr(0.2f, 0.75f); Put("lily", p.c + new Vector2(Mathf.Cos(a) * p.rx * f, Mathf.Sin(a) * p.rz * f), Rr(0.5f, 0.9f), -1, WY + 0.01f, false); }
                for (int i = 0; i < 9; i++) { float a = Rr(0, 6.28f), f = Rr(0.9f, 1.15f); Put("reed", p.c + new Vector2(Mathf.Cos(a) * p.rx * f, Mathf.Sin(a) * p.rz * f), Rr(0.9f, 1.4f), -1, 0, false); }
                // a snag sticking out of the water
                if (Rnd() < 0.6f) { var q = p.c + new Vector2(Rr(-0.4f, 0.4f) * p.rx, Rr(-0.4f, 0.4f) * p.rz); Obj(MeshGen.Cylinder(0.08f, 0.03f, 1.1f, 6), M["BARK"], new Vector3(q.x, 0.45f, q.y), Quaternion.Euler(Rr(-35, 35), Rr(0, 360), Rr(-35, 35)), true, "Snag"); }
            }
            // weeping willows, dead willows, rotten logs, stumps, sedge
            for (int i = 0; i < 9; i++) { var p = Spot(1.0f); if (!float.IsNaN(p.x)) Tree(i % 3 == 0 ? "willowdead" : "willow2", p, Rr(0.85f, 1.15f), 0.35f); }
            for (int i = 0; i < 6; i++) { var p = Spot(0.8f); if (!float.IsNaN(p.x)) { Put("logm", p, Rr(0.9f, 1.2f), -1, 0); } }
            for (int i = 0; i < 6; i++) { var p = Spot(0.5f); if (!float.IsNaN(p.x)) Put("stumpm", p, Rr(0.8f, 1.1f)); }
            for (int i = 0; i < 30; i++) { var p = Spot(0.3f, 0.7f); if (!float.IsNaN(p.x)) Put(Rnd() < 0.5f ? "grass2" : "reed", p, Rr(1.0f, 1.5f), -1, 0, false); }
            // a rotten boardwalk across the biggest pool, a crooked signpost, an old boat
            if (pools.Count > 0)
            {
                var p = pools[0]; float a = p.ang; var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                for (float s = -p.rx * 1.15f; s <= p.rx * 1.15f; s += 0.42f)
                {
                    var q = p.c + dir * s; if (Rnd() < 0.12f) continue;      // (a missing board)
                    Obj(MeshGen.Box(1.1f, 0.06f, 0.36f), M["WOOD"], new Vector3(q.x, WY + 0.12f + Rr(-0.03f, 0.03f), q.y), Quaternion.Euler(Rr(-4, 4), -a * Mathf.Rad2Deg + 90, Rr(-6, 6)), true, "Board");
                }
                paths.Add(new[] { p.c - dir * p.rx * 1.2f, p.c + dir * p.rx * 1.2f }); walks.Add(paths[paths.Count - 1]);
                var hull = p.c + new Vector2(-dir.y, dir.x) * p.rz * 0.9f;
                Obj(MeshGen.Box(0.9f, 0.3f, 2.2f), M["WOOD"], new Vector3(hull.x, 0.08f, hull.y), Quaternion.Euler(8, Rr(0, 360), 14), true, "OldBoat");
            }
            {
                var p = Spot(0.3f, 0.9f);
                if (!float.IsNaN(p.x)) { Obj(MeshGen.Box(0.1f, 1.6f, 0.1f), M["WOOD"], new Vector3(p.x, 0.8f, p.y), Quaternion.Euler(0, 0, 9), true, "Signpost"); Obj(MeshGen.Box(0.8f, 0.22f, 0.05f), M["WOOD"], new Vector3(p.x + 0.25f, 1.35f, p.y), Quaternion.Euler(0, 20, 14), true, "Sign"); }
            }
            Edges((p, s) => { if (Rnd() < 0.5f) Tree(Rnd() < 0.5f ? "willow2" : "willowdead", p, Rr(0.9f, 1.2f), 0.4f); else { Put("reed", p, 1.4f, -1, 0, false); colliders.Add(Collider2.Circle(p.x, p.y, 0.5f)); } }, 1.5f);
            Grass(0.6f);
            // night: will-o'-wisps over the bog, glowing spores, low mist
            Motes(1, 26, new Color(0.55f, 0.95f, 1f) * 1.4f, 0.4f, 1.3f, new Vector4(0.06f, 0, 0, 0.8f));
            Motes(1, 60, new Color(0.6f, 0.8f, 0.4f) * 0.6f, 0.3f, 2.2f, new Vector4(0.03f, 0, 0, 0.4f));
            foreach (var p in pools) Forest.Mist(T, new Vector3(p.c.x, WY + 0.15f, p.c.y), p.rx * 1.6f, p.rz * 1.6f, new Color(0.5f, 0.6f, 0.55f) * 0.08f);
            Forest.Mist(T, new Vector3(0, 0.6f, 0), HX * 1.1f, HZ * 1.1f, new Color(0.45f, 0.52f, 0.5f) * 0.05f);
        }

        // ================= 2.2 the poisoned swamp =================
        void Toxic()
        {
            title = "Ядовитое болото";
            ambSky = Mats.Hex(0x5a6a50) * 0.28f; ambEq = Mats.Hex(0x30382a) * 0.3f; ambGround = Mats.Hex(0x0c0e08) * 0.3f; moonK = 0.6f; heroLightK = 1.35f; exposure = 1.4f;
            Paths(M.Tint("MUD", 0x1a1612), 0.6f, M.Tint("GROUND", 0x141210));
            for (int i = 0; i < 5; i++) { var p = Spot(1.4f, 1.5f); if (!float.IsNaN(p.x)) AddPool(p, Rr(1.0f, 2.2f), Rr(0.8f, 1.6f), 3); }
            Ground(M.Tint("GROUND", 0x141210));
            // cracks in the black earth: thin dark strips
            for (int i = 0; i < 14; i++)
            {
                var a = new Vector2(Rr(-HX, HX), Rr(-HZ, HZ)); var line = new Vector2[4]; line[0] = a;
                for (int k = 1; k < 4; k++) line[k] = line[k - 1] + new Vector2(Rr(-1.2f, 1.2f), Rr(-1.2f, 1.2f));
                Forest.PathStrip(T, M.Tint("MUD", 0x060504), line, 0.05f, 0.008f, "Crack");
            }
            // the ooze: acid green, one pool violet; their glow lights the banks; salt crusts round them
            var acid = M.Tint("WATER", 0x2a7a14, 0x1a6a0a);
            Water(new Color(0.05f, 0.24f, 0.03f), new Color(0.5f, 1.0f, 0.22f), acid);
            for (int i = 0; i < pools.Count; i++)
            {
                var p = pools[i]; bool vio = i == pools.Count - 1;
                var l = Lamp(new Vector3(p.c.x, 0.6f, p.c.y), vio ? new Color(0.7f, 0.3f, 1f) : new Color(0.45f, 1f, 0.3f), 1.3f, Mathf.Max(p.rx, p.rz) * 2.2f);
                flicks.Add(new Flick { l = l, baseI = 1.3f, ph = Rnd() * 5, kind = 2 });
                for (int k = 0; k < 10; k++) { float a = Rr(0, 6.28f), f = Rr(1.0f, 1.2f); Obj(MeshGen.Box(Rr(0.2f, 0.5f), 0.03f, Rr(0.15f, 0.35f)), M.Tint("MARBLE", 0xc8c8a8), new Vector3(p.c.x + Mathf.Cos(a) * p.rx * f, 0.015f, p.c.y + Mathf.Sin(a) * p.rz * f), Quaternion.Euler(0, Rr(0, 360), 0), false, "Crust"); }
            }
            // bare twisted trees, thorny bushes, acid mushrooms, bones, skulls on poles, a ruined suit of armour
            for (int i = 0; i < 12; i++) { var p = Spot(0.8f); if (!float.IsNaN(p.x)) Tree(i % 4 == 0 ? "willowdead" : i % 4 == 1 ? "birchdead" : i % 2 == 0 ? "dead1" : "dead4", p, Rr(0.85f, 1.2f), 0.3f); }
            for (int i = 0; i < 8; i++) { var p = Spot(0.5f); if (!float.IsNaN(p.x)) { Put("bush2", p, Rr(0.7f, 1.0f), -1, 0, true, null, k => k == "LEAF" ? M.Tint("LEAF", 0x2a2a14) : null); } }
            for (int i = 0; i < 18; i++) { var p = Spot(0.25f, 0.8f); if (!float.IsNaN(p.x)) Put(Rnd() < 0.5f ? "mush1" : "mush3", p, Rr(0.8f, 1.4f), -1, 0, false, null, k => k == "MUSH" ? M["TOXIC"] : null); }
            for (int i = 0; i < 6; i++) { var p = Spot(0.3f, 0.8f); if (!float.IsNaN(p.x)) Put("skull", p, Rr(0.5f, 0.8f), -1, 0); }
            for (int i = 0; i < 4; i++)
            {
                var p = Spot(0.3f, 1.0f); if (float.IsNaN(p.x)) continue;
                Obj(MeshGen.Cylinder(0.04f, 0.03f, 1.9f, 6), M["BARK"], new Vector3(p.x, 0.95f, p.y), Quaternion.Euler(Rr(-6, 6), 0, Rr(-6, 6)), true, "Pole");
                Put("skull", p, 0.45f, Rr(0, 360), 1.85f);
                colliders.Add(Collider2.Circle(p.x, p.y, 0.15f));
            }
            Edges((p, s) => { Tree(Rnd() < 0.5f ? "dead1" : "dead4", p, Rr(1.0f, 1.3f), 0.45f); }, 1.4f);
            Grass(0.6f);
            // night: poison-green fireflies, glowing bubbles over the ooze, a green and violet haze on the ground
            Motes(1, 40, new Color(0.5f, 1f, 0.3f) * 1.3f, 0.3f, 1.6f, new Vector4(0.045f, 0, 0, 0.5f));
            {
                var pts = new List<Vector3>(); var sd = new List<Vector2>();
                foreach (var p in pools) for (int i = 0; i < 12; i++) { float a = Rr(0, 6.28f), f = Rr(0, 0.8f); pts.Add(new Vector3(p.c.x + Mathf.Cos(a) * p.rx * f, WY, p.c.y + Mathf.Sin(a) * p.rz * f)); sd.Add(new Vector2(Rnd(), Rnd())); }
                for (int i = 0; i < pts.Count; i++) pts[i] += new Vector3(0, Rr(0.05f, 0.35f), 0);
                if (pts.Count > 0) GlyphLayer.MakeMotes(T, Vector3.zero, Quaternion.identity, Vector3.one, pts, sd, 1, new Color(0.6f, 1f, 0.4f) * 1.2f, new Vector4(0.04f, 0, 0, 0.15f));     // (glowing gas bubbles hanging low over the ooze)
            }
            Forest.Mist(T, new Vector3(0, 0.2f, 0), HX * 1.1f, HZ * 1.1f, new Color(0.35f, 0.6f, 0.2f) * 0.07f);
            Forest.Mist(T, new Vector3(-3, 0.45f, 3), 6, 5, new Color(0.5f, 0.25f, 0.7f) * 0.05f);
        }

        // ================= 2.4 the bright city =================
        void BrightCity()
        {
            title = "Светлый город";
            ambSky = Mats.Hex(0x9aaad8) * 0.55f; ambEq = Mats.Hex(0x5a5a58) * 0.5f; ambGround = Mats.Hex(0x2a2620) * 0.5f; moonK = 1f; heroLightK = 1.4f; exposure = 1.3f;
            Paths(M.Tint("COBBLE", 0xb0a898), 1.5f, M.Tint("COBBLE", 0x8c867a));
            AddPool(new Vector2(0, 0), 1.25f, 1.25f, 2);          // the fountain's basin
            spawn = new Vector3(0, 0, 2.6f);
            Ground(M.Tint("COBBLE", 0x8c867a));
            Water(new Color(0.07f, 0.14f, 0.26f), new Color(0.4f, 0.6f, 0.9f), M["WATER"]);
            // the fountain: a marble rim, a column with a bowl, the water
            Obj(MeshGen.Cylinder(1.45f, 1.45f, 0.35f, 28), M["MARBLE"], new Vector3(0, 0.175f, 0), Quaternion.identity, true, "FountainRim");
            Obj(MeshGen.Cylinder(1.28f, 1.28f, 0.36f, 28), M.Tint("WATER", 0x16304a, 0x061220), new Vector3(0, 0.2f, 0), Quaternion.identity, false, "FountainWater");
            Obj(MeshGen.Cylinder(0.16f, 0.12f, 1.4f, 10), M["MARBLE"], new Vector3(0, 0.9f, 0), Quaternion.identity, true, "FountainColumn");
            Obj(MeshGen.Cylinder(0.25f, 0.6f, 0.2f, 16), M["MARBLE"], new Vector3(0, 1.62f, 0), Quaternion.identity, true, "FountainBowl");
            colliders.Add(Collider2.Circle(0, 0, 1.5f));
            // white houses round the square, a bell tower, a smithy; their walls and roofs in the bright colours
            var white = new Dictionary<string, string> { { "ROCK", "MARBLE" }, { "PLASTER", "MARBLE" } };
            System.Func<string, Material> roofs = k => k == "ROOF" ? M.Tint("ROOF", 0xb0462a) : null;
            void House(string nm, Vector2 p, float yaw, float r) { Put(nm, p, 1f, yaw, 0, true, white, roofs); colliders.Add(Collider2.Circle(p.x, p.y, r)); occluders.Add(new Vector4(p.x, p.y, r + 0.6f, 6)); }
            Edges((p, s) =>
            {
                float yaw = s == 0 ? 180 : s == 1 ? 0 : s == 2 ? 90 : -90;
                if (Rnd() < 0.75f) House(Rnd() < 0.4f ? "house2" : Rnd() < 0.5f ? "house3" : "smithy", p + (s == 2 ? new Vector2(-1.2f, 0) : s == 3 ? new Vector2(1.2f, 0) : Vector2.zero), yaw, 1.9f);
                else { Put("wall", p, 1f, yaw, 0, true, white); colliders.Add(Collider2.Circle(p.x, p.y, 1.0f)); }
            }, 4.2f);
            { var p = Spot(2.2f, 2.0f); if (!float.IsNaN(p.x)) House("belltower", p, Rr(0, 360), 2.1f); }
            // the statue on its pedestal, flower beds, trees in tubs, market stalls under coloured awnings, benches, flags
            { var p = Spot(1.2f, 1.6f); if (!float.IsNaN(p.x)) { Put("pedestal", p, 0.9f, 0); Put("statue", p, 0.45f, Rr(0, 360), 1.3f); colliders.Add(Collider2.Circle(p.x, p.y, 0.9f)); } }
            var POP = new Dictionary<string, string> { { "FLOWERB", "RED" } };
            for (int i = 0; i < 6; i++)
            {
                var p = Spot(0.8f, 1.6f); if (float.IsNaN(p.x)) continue;
                Obj(MeshGen.Box(1.6f, 0.3f, 0.8f), M["MARBLE"], new Vector3(p.x, 0.15f, p.y), Quaternion.Euler(0, 0, 0), true, "Bed");
                Obj(MeshGen.Box(1.45f, 0.05f, 0.65f), M.Tint("GROUND", 0x2a2010), new Vector3(p.x, 0.31f, p.y), Quaternion.identity, false, "Soil");
                for (int k = 0; k < 4; k++) Put(k % 2 == 0 ? "flowers" : "flower3", p + new Vector2(Rr(-0.6f, 0.6f), Rr(-0.2f, 0.2f)), Rr(0.8f, 1.1f), -1, 0.32f, false, k == 0 ? POP : null);
                colliders.Add(Collider2.Box(p.x - 0.85f, p.x + 0.85f, p.y - 0.45f, p.y + 0.45f));
            }
            for (int i = 0; i < 5; i++)
            {
                var p = Spot(0.6f, 1.6f); if (float.IsNaN(p.x)) continue;
                Obj(MeshGen.Cylinder(0.32f, 0.38f, 0.5f, 12), M["WOOD"], new Vector3(p.x, 0.25f, p.y), Quaternion.identity, true, "Tub");
                Tree("oak5", p, 0.5f, 0.6f);
            }
            System.Func<string, Material> awning = k => k == "ROOF" ? M.Tint("RED", Rnd() < 0.5f ? 0xc83a2au : 0x2a6ac8u) : null;
            for (int i = 0; i < 4; i++) { var p = Spot(0.8f, 1.6f); if (float.IsNaN(p.x)) continue; Put(i % 2 == 0 ? "market1" : "market2", p, 1f, Rr(0, 360), 0, true, null, awning); colliders.Add(Collider2.Circle(p.x, p.y, 0.9f)); }
            for (int i = 0; i < 4; i++) { var p = Spot(0.5f, 1.5f); if (!float.IsNaN(p.x)) Put("bench", p, 1f); }
            for (int i = 0; i < 3; i++) { var p = Spot(0.3f, 1.4f); if (float.IsNaN(p.x)) continue; Obj(MeshGen.Cylinder(0.04f, 0.04f, 3f, 6), M["DARK"], new Vector3(p.x, 1.5f, p.y), Quaternion.identity, true, "Flagpole"); Put("banner", p, 1.2f, Rr(0, 360), 2.2f); colliders.Add(Collider2.Circle(p.x, p.y, 0.12f)); }
            // the street clock on a post
            { var p = Spot(0.3f, 1.4f); if (!float.IsNaN(p.x)) { Obj(MeshGen.Cylinder(0.06f, 0.06f, 2.4f, 8), M["DARK"], new Vector3(p.x, 1.2f, p.y), Quaternion.identity, true, "ClockPost"); Obj(MeshGen.Cylinder(0.32f, 0.32f, 0.1f, 18), M["MARBLE"], new Vector3(p.x, 2.55f, p.y), Quaternion.Euler(90, 0, 0), true, "Clock"); colliders.Add(Collider2.Circle(p.x, p.y, 0.12f)); } }
            // night: warm street lamps, garlands over the square, lit windows
            for (int i = 0; i < 7; i++)
            {
                var p = Spot(0.3f, 1.6f); if (float.IsNaN(p.x)) continue;
                Obj(MeshGen.Cylinder(0.05f, 0.05f, 2.6f, 6), M["DARK"], new Vector3(p.x, 1.3f, p.y), Quaternion.identity, true, "LampPost");
                Obj(MeshGen.Box(0.22f, 0.3f, 0.22f), M["WINDOW"], new Vector3(p.x, 2.7f, p.y), Quaternion.identity, false, "Lantern");
                var l = Lamp(new Vector3(p.x, 2.7f, p.y), new Color(1f, 0.75f, 0.42f), 1.4f, 5.5f); flicks.Add(new Flick { l = l, baseI = 1.4f, ph = Rnd() * 5, kind = 0 });
                colliders.Add(Collider2.Circle(p.x, p.y, 0.12f));
            }
            {
                var pts = new List<Vector3>(); var sd = new List<Vector2>();
                for (int g = 0; g < 3; g++)
                {
                    float a0 = Rr(0, 6.28f); var c = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * Rr(3, 6);
                    var a = c + new Vector2(-2.5f, Rr(-1, 1)); var b = c + new Vector2(2.5f, Rr(-1, 1));
                    for (int k = 0; k <= 24; k++) { float t = k / 24f; var q = Vector2.Lerp(a, b, t); pts.Add(new Vector3(q.x, 3.1f - 0.6f * Mathf.Sin(t * Mathf.PI), q.y)); sd.Add(new Vector2(0, Rnd())); }
                }
                GlyphLayer.MakeMotes(T, Vector3.zero, Quaternion.identity, Vector3.one, pts, sd, 1, new Color(1f, 0.82f, 0.4f) * 1.4f, new Vector4(0.04f, 0, 0, 0.02f));     // (fireflies that hardly wander: lights on a string)
            }
        }

        // ================= 2.3 the grim city: slums and ruins =================
        void GrimCity()
        {
            title = "Трущобы";
            ambSky = Mats.Hex(0x5a5e6a) * 0.3f; ambEq = Mats.Hex(0x2e2c2a) * 0.3f; ambGround = Mats.Hex(0x0e0c0a) * 0.3f; moonK = 0.65f; heroLightK = 1.45f; exposure = 1.4f;
            Paths(M.Tint("COBBLE", 0x2c2a28), 1.3f, M.Tint("COBBLE", 0x3c3834));
            for (int i = 0; i < 6; i++) { var p = Spot(0.8f, 0.0f); if (!float.IsNaN(p.x)) AddPool(p, Rr(0.6f, 1.2f), Rr(0.4f, 0.8f), 2); }
            Ground(M.Tint("COBBLE", 0x3c3834));
            Water(new Color(0.03f, 0.03f, 0.04f), new Color(0.3f, 0.24f, 0.36f), M.Tint("WATER", 0x0a0a0c, 0x020204));      // (oily: a dull violet sheen)
            // crooked dark houses (boarded windows, peeling plaster), a sewer ditch
            var grim = new Dictionary<string, string> { { "PLASTER", "MUD" }, { "WINDOW", "DARK" } };
            System.Func<string, Material> dark = k => k == "ROOF" ? M.Tint("ROOF", 0x2a2426) : k == "ROCK" ? M.Tint("ROCK", 0x4a4644) : null;
            void House(string nm, Vector2 p, float yaw, bool lit)
            {
                var g = Put(nm, p, Rr(0.95f, 1.05f), yaw, 0, true, lit ? null : grim, dark);
                g.transform.localRotation = Quaternion.Euler(Rr(-3, 3), yaw, Rr(-4, 4));      // (crooked)
                colliders.Add(Collider2.Circle(p.x, p.y, 1.8f)); occluders.Add(new Vector4(p.x, p.y, 2.4f, 5));
                if (lit) { var l = Lamp(new Vector3(p.x, 1.6f, p.y), new Color(1f, 0.6f, 0.3f), 0.5f, 3f); flicks.Add(new Flick { l = l, baseI = 0.5f, ph = Rnd(), kind = 0 }); }
            }
            Edges((p, s) =>
            {
                float yaw = s == 0 ? 180 : s == 1 ? 0 : s == 2 ? 90 : -90;
                var q = p + (s == 2 ? new Vector2(-1.1f, 0) : s == 3 ? new Vector2(1.1f, 0) : Vector2.zero);
                if (Rnd() < 0.8f) House(Rnd() < 0.5f ? "house3" : Rnd() < 0.5f ? "house4" : "house2", q, yaw + Rr(-8, 8), Rnd() < 0.25f);
                else { Put("wallb", p, 1f, yaw, 0, true, null, dark); colliders.Add(Collider2.Circle(p.x, p.y, 1.0f)); }
            }, 4f);
            for (int i = 0; i < 2; i++) { var p = Spot(2.0f, 1.8f); if (!float.IsNaN(p.x)) House(i == 0 ? "house4" : "house3", p, Rr(0, 360), false); }
            {   // an open sewer ditch across the street
                var a = new Vector2(Rr(-HX, -4), Rr(-6, 6)); var line = new[] { a, a + new Vector2(5, Rr(-2, 2)), a + new Vector2(10, Rr(-3, 3)) };
                Forest.PathStrip(T, M.Tint("MUD", 0x141008), line, 0.35f, 0.01f, "Ditch");
            }
            // heaps of rubbish, crates, barrels, sacks; rusty grilles; a washing line with rags
            for (int i = 0; i < 10; i++)
            {
                var p = Spot(0.4f, 1.2f); if (float.IsNaN(p.x)) continue; float r = Rnd();
                Put(r < 0.35f ? "crate" : r < 0.65f ? "barrel" : "bags", p, Rr(0.85f, 1.15f), -1, 0, true, null, k => k == "WOOD" ? M.Tint("WOOD", 0x3a2a1a) : null);
                if (r < 0.65f) colliders.Add(Collider2.Circle(p.x, p.y, 0.4f));
            }
            for (int i = 0; i < 5; i++)
            {
                var p = Spot(0.5f, 1.0f); if (float.IsNaN(p.x)) continue;      // a rubbish heap: lumps of dark stuff
                for (int k = 0; k < 6; k++) Obj(MeshGen.Sphere(Rr(0.12f, 0.3f), 7, 5), M.Tint("MUD", 0x2a241c), new Vector3(p.x + Rr(-0.4f, 0.4f), 0.05f, p.y + Rr(-0.4f, 0.4f)), Quaternion.Euler(0, Rr(0, 360), 0), true, "Rubbish");
            }
            {
                var p = Spot(1.5f, 1.0f);
                if (!float.IsNaN(p.x))
                {
                    var a = p + new Vector2(-1.5f, 0); var b = p + new Vector2(1.5f, 0);
                    foreach (var q in new[] { a, b }) { Obj(MeshGen.Cylinder(0.04f, 0.04f, 2.2f, 6), M["WOOD"], new Vector3(q.x, 1.1f, q.y), Quaternion.Euler(Rr(-5, 5), 0, Rr(-5, 5)), true, "LinePost"); colliders.Add(Collider2.Circle(q.x, q.y, 0.1f)); }
                    Obj(MeshGen.Box(3f, 0.015f, 0.015f), M["LINEN"], new Vector3(p.x, 2.05f, p.y), Quaternion.identity, false, "Line");
                    for (int k = 0; k < 4; k++) Obj(MeshGen.Box(Rr(0.3f, 0.5f), Rr(0.4f, 0.7f), 0.02f), M.Tint("LINEN", Rnd() < 0.5f ? 0x6a5a48u : 0x4a4a52u), new Vector3(p.x - 1.1f + k * 0.7f, 1.75f, p.y), Quaternion.Euler(0, 0, Rr(-6, 6)), true, "Rag");
                }
            }
            // night: failing street lamps (flickering), a dim window here and there, smog
            for (int i = 0; i < 4; i++)
            {
                var p = Spot(0.3f, 1.4f); if (float.IsNaN(p.x)) continue;
                Obj(MeshGen.Cylinder(0.05f, 0.05f, 2.5f, 6), M.Tint("DARK", 0x2a1c14), new Vector3(p.x, 1.25f, p.y), Quaternion.Euler(Rr(-7, 7), 0, Rr(-7, 7)), true, "LampPost");
                Obj(MeshGen.Box(0.2f, 0.26f, 0.2f), M.Tint("WINDOW", 0x806040, 0x604020), new Vector3(p.x, 2.6f, p.y), Quaternion.identity, false, "Lantern");
                var l = Lamp(new Vector3(p.x, 2.6f, p.y), new Color(1f, 0.7f, 0.4f), 1.1f, 4.5f); flicks.Add(new Flick { l = l, baseI = 1.1f, ph = Rnd() * 9, kind = 1 });
                colliders.Add(Collider2.Circle(p.x, p.y, 0.12f));
            }
            Grass(0.5f);
            Forest.Mist(T, new Vector3(0, 0.9f, 0), HX * 1.2f, HZ * 1.2f, new Color(0.42f, 0.4f, 0.38f) * 0.06f);
        }

        // ================= 2.5 the wastes =================
        void Wastes()
        {
            title = "Пустоши";
            ambSky = Mats.Hex(0x7a8ac8) * 0.5f; ambEq = Mats.Hex(0x5a4e40) * 0.45f; ambGround = Mats.Hex(0x2a2014) * 0.45f; moonK = 1.3f; heroLightK = 1.35f; exposure = 1.25f;
            Paths(M.Tint("SAND", 0x7a6444), 0.9f, M["SAND"]);
            Ground(M["SAND"]);
            // cracks of the dry earth
            for (int i = 0; i < 26; i++)
            {
                var a = new Vector2(Rr(-HX, HX), Rr(-HZ, HZ)); var line = new Vector2[3]; line[0] = a;
                for (int k = 1; k < 3; k++) line[k] = line[k - 1] + new Vector2(Rr(-1.4f, 1.4f), Rr(-1.4f, 1.4f));
                Forest.PathStrip(T, M.Tint("SAND", 0x4a3a26), line, 0.04f, 0.008f, "Crack");
            }
            // dunes: low long drifts of sand
            var sand = M.Tint("SAND", 0xa88a5e);
            for (int i = 0; i < 5; i++) { var p = Spot(1.6f, 1.6f); if (float.IsNaN(p.x)) continue; var g = Obj(MeshGen.Sphere(1f, 16, 8, Mathf.PI * 0.5f), sand, new Vector3(p.x, -0.02f, p.y), Quaternion.Euler(0, Rr(0, 180), 0), true, "Dune"); g.transform.localScale = new Vector3(Rr(2.2f, 3.4f), Rr(0.35f, 0.6f), Rr(1.2f, 1.8f)); }
            // rock outcrops and a canyon wall along the closed sides, a cave mouth in the south (the crystal mines)
            var rockSand = M.Tint("ROCK", 0x7a6650);
            System.Func<string, Material> sr = k => k == "ROCK" ? rockSand : null;
            Edges((p, s) => { Put(Rnd() < 0.5f ? "rock7" : "rock5", p, Rr(1.6f, 2.4f), -1, -0.1f, true, null, sr); colliders.Add(Collider2.Circle(p.x, p.y, 1.0f)); }, 1.8f);
            if (openS)
            {   // the mouth: a rock face along the south edge with a dark way down, half buried in sand
                for (float x = -HX; x <= HX; x += 1.8f)
                {
                    if (Mathf.Abs(x - 0f) < 1.8f) continue;
                    var g = Obj(Cave.CaveMesh(Rnd() < 0.5f ? "wall_a" : "wall_b", new Vector3(2.1f, Rr(2.4f, 3.2f), 1.6f), Rnd() < 0.5f), rockSand, new Vector3(x, -0.05f, HZ + 0.35f), Quaternion.Euler(0, 180 + Rr(-6, 6), 0), true, "Cliff");
                    colliders.Add(Collider2.Circle(x, HZ - 0.1f, 0.9f));
                }
                for (int k = 0; k < 6; k++) { var g = Obj(MeshGen.Sphere(Rr(0.3f, 0.6f), 9, 5, Mathf.PI * 0.5f), sand, new Vector3(Rr(-2.4f, 2.4f), -0.02f, HZ - Rr(0.2f, 1.2f)), Quaternion.identity, true, "Drift"); g.transform.localScale = new Vector3(1.6f, 0.5f, 1.1f); }
                paths.Add(new[] { new Vector2(0, HZ - 3), new Vector2(0, HZ + 1) });
            }
            for (int i = 0; i < 9; i++) { var p = Spot(0.8f, 1.2f); if (!float.IsNaN(p.x)) { Put(i % 3 == 0 ? "rock7" : i % 3 == 1 ? "rock3" : "rock1", p, Rr(1.0f, 1.8f), -1, -0.05f, true, null, sr); colliders.Add(Collider2.Circle(p.x, p.y, 0.7f)); } }
            // tumbleweeds, dry thorn bushes, cacti, dead stumps
            for (int i = 0; i < 10; i++) { var p = Spot(0.4f, 1.0f); if (!float.IsNaN(p.x)) Put(Rnd() < 0.5f ? "bush2" : "bush1", p, Rr(0.4f, 0.75f), -1, 0, true, null, k => k == "LEAF" ? M.Tint("HAY", 0x6a5430) : null); }
            for (int i = 0; i < 8; i++) { var p = Spot(0.4f, 1.0f); if (!float.IsNaN(p.x)) { Put(i % 3 == 0 ? "cactusf" : i % 3 == 1 ? "cactus3" : "cactus1", p, Rr(0.8f, 1.2f)); colliders.Add(Collider2.Circle(p.x, p.y, 0.3f)); } }
            for (int i = 0; i < 5; i++) { var p = Spot(0.5f, 1.0f); if (!float.IsNaN(p.x)) Put("stump", p, Rr(0.8f, 1.1f), -1, 0, true, null, k => k == "BARK" ? M.Tint("BARK", 0x8a7a64) : null); }
            // bleached bones of a great beast: a skull and its ribs; a broken cart half in the sand
            {
                var p = Spot(2f, 1.4f);
                if (!float.IsNaN(p.x))
                {
                    Put("skull", p, 2.2f, Rr(0, 360));
                    for (int k = 0; k < 7; k++) Obj(MeshGen.Cylinder(0.06f, 0.03f, 1.6f, 6), M["BONE"], new Vector3(p.x + 1.1f + k * 0.38f, 0.45f, p.y + 0.4f), Quaternion.Euler(0, 0, 58 - k * 3) * Quaternion.Euler(0, 90, 0), true, "Rib");
                    colliders.Add(Collider2.Circle(p.x, p.y, 0.9f));
                }
            }
            { var p = Spot(1.0f, 1.4f); if (!float.IsNaN(p.x)) { var g = Put("cart", p, 1f, Rr(0, 360), -0.15f); g.transform.localRotation *= Quaternion.Euler(0, 0, 14); colliders.Add(Collider2.Circle(p.x, p.y, 0.8f)); } }
            // ruins half buried in sand
            for (int i = 0; i < 3; i++) { var p = Spot(1.0f, 1.4f); if (!float.IsNaN(p.x)) { Put("wall", p, 1f, Rr(0, 360), -Rr(0.3f, 0.8f), true, null, sr); colliders.Add(Collider2.Circle(p.x, p.y, 0.9f)); } }
            Grass(0.5f);
            // night: the wind moves the dust; eyes of beasts glowing in the dark at the edges
            Motes(1, 70, new Color(0.7f, 0.62f, 0.48f) * 0.5f, 0.1f, 1.2f, new Vector4(0.035f, 0, 0, 1.2f));
            {
                var pts = new List<Vector3>(); var sd = new List<Vector2>();
                for (int i = 0; i < 4; i++) { var c = new Vector3(Rr(-HX + 1, HX - 1), 0.5f, Rnd() < 0.5f ? -HZ + 0.8f : HZ - 0.8f); pts.Add(c); pts.Add(c + new Vector3(0.12f, 0, 0)); sd.Add(new Vector2(0, Rnd())); sd.Add(new Vector2(0, Rnd())); }
                GlyphLayer.MakeMotes(T, Vector3.zero, Quaternion.identity, Vector3.one, pts, sd, 1, new Color(1f, 0.85f, 0.2f) * 1.6f, new Vector4(0.035f, 0, 0, 0.0f));
            }
        }

        // ================= 3.1 the crystal mines =================
        void Crystal()
        {
            title = "Кристальные копи";
            ambSky = Mats.Hex(0x302848) * 0.25f; ambEq = Mats.Hex(0x201a30) * 0.25f; ambGround = Mats.Hex(0x0a0810) * 0.3f; moonK = 0f; heroLightK = 1.1f; exposure = 1.45f;
            Paths(M.Tint("CAVEFLOOR", 0x2c2a34), 0.9f, M.Tint("CAVEFLOOR", 0x24222a));
            { var p = Spot(2.4f, 2.0f); if (!float.IsNaN(p.x)) AddPool(p, 2.6f, 1.8f, 4); }      // the mirror lake
            Ground(M.Tint("CAVEFLOOR", 0x24222a));
            Water(new Color(0.015f, 0.012f, 0.04f), new Color(0.3f, 0.22f, 0.5f), M.Tint("WATER", 0x08060e, 0x02010a), true);
            // the rock walls all round but at the entrance (north)
            var wall = M.Tint("WETROCK", 0x3a3644);
            Edges((p, s) =>
            {
                float yaw = s == 0 ? 0 : s == 1 ? 180 : s == 2 ? 90 : -90;
                Obj(Cave.CaveMesh(Rnd() < 0.5f ? "wall_a" : "wall_b", new Vector3(2.2f, Rr(2.6f, 3.4f), 1.7f), Rnd() < 0.5f), wall, new Vector3(p.x, -0.05f, p.y), Quaternion.Euler(0, yaw + Rr(-8, 8), 0), true, "Wall");
                colliders.Add(Collider2.Circle(p.x, p.y, 1.0f));
            }, 1.9f);
            // crystal druses: clusters of six-sided prisms, violet and blue, in the floor and the walls; they light the rock
            var cv = M["CRYSTAL"]; var cb = M["CRYSTALB"];
            void Druse(Vector2 p, float s, bool blue, float y = 0, float tilt = 0)
            {
                int n = 4 + R.Next(5);
                for (int k = 0; k < n; k++)
                {
                    float h = Rr(0.4f, 1.3f) * s, r = Rr(0.07f, 0.16f) * s;
                    var q = Quaternion.Euler(Rr(-35, 35) + tilt, Rr(0, 360), Rr(-35, 35));
                    Obj(Prism(r, h), blue ? cb : cv, new Vector3(p.x + Rr(-0.25f, 0.25f) * s, y + h * 0.4f, p.y + Rr(-0.25f, 0.25f) * s), q, true, "Crystal");
                }
            }
            for (int i = 0; i < 9; i++)
            {
                var p = Spot(0.6f, 1.2f); if (float.IsNaN(p.x)) continue; bool blue = Rnd() < 0.4f;
                Druse(p, Rr(0.8f, 1.5f), blue); colliders.Add(Collider2.Circle(p.x, p.y, 0.45f));
                var l = Lamp(new Vector3(p.x, 0.9f, p.y), blue ? new Color(0.4f, 0.75f, 1f) : new Color(0.75f, 0.45f, 1f), 1.2f, 4f);
                flicks.Add(new Flick { l = l, baseI = 1.2f, ph = Rnd() * 3, kind = 2 });
            }
            Edges((p, s) => { if (Rnd() < 0.35f) Druse(p + (s == 0 ? new Vector2(0, 0.6f) : s == 1 ? new Vector2(0, -0.6f) : s == 2 ? new Vector2(0.6f, 0) : new Vector2(-0.6f, 0)), Rr(0.9f, 1.4f), Rnd() < 0.5f, Rr(0.3f, 1.2f), s == 1 ? -40 : 40); }, 2.2f);
            // the rune circle on the floor: a ring of glowing marks
            {
                var p = Spot(1.6f, 1.5f);
                if (!float.IsNaN(p.x))
                {
                    var glow = M.Tint("CRYSTAL", 0x6040a0, 0x5a30c8);
                    for (int k = 0; k < 24; k++) { float a = k * Mathf.PI * 2 / 24; Obj(MeshGen.Box(0.28f, 0.02f, 0.06f), glow, new Vector3(p.x + Mathf.Cos(a) * 1.3f, 0.012f, p.y + Mathf.Sin(a) * 1.3f), Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90, 0), false, "Rune"); }
                    for (int k = 0; k < 6; k++) { float a = k * Mathf.PI * 2 / 6; Obj(MeshGen.Box(0.12f, 0.02f, 0.5f), glow, new Vector3(p.x + Mathf.Cos(a) * 0.8f, 0.012f, p.y + Mathf.Sin(a) * 0.8f), Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), false, "Rune"); }
                    var l = Lamp(new Vector3(p.x, 0.4f, p.y), new Color(0.6f, 0.4f, 1f), 0.9f, 3f); flicks.Add(new Flick { l = l, baseI = 0.9f, ph = 1.7f, kind = 2 });
                }
            }
            // floating shards and sparks of mana
            {
                var pts = new List<Vector3>(); var sd = new List<Vector2>();
                for (int i = 0; i < 14; i++) { var p = Spot(0.2f, 0.5f); if (float.IsNaN(p.x)) continue; var g = Obj(Prism(Rr(0.03f, 0.06f), Rr(0.12f, 0.25f), 5), Rnd() < 0.5f ? cv : cb, new Vector3(p.x, Rr(1.2f, 2.2f), p.y), Quaternion.Euler(Rr(0, 360), Rr(0, 360), Rr(0, 360)), false, "Shard"); }
                for (int i = 0; i < 60; i++) { pts.Add(new Vector3(Rr(-HX, HX), Rr(0.2f, 2.4f), Rr(-HZ, HZ))); sd.Add(new Vector2(0, Rnd())); }
                GlyphLayer.MakeMotes(T, Vector3.zero, Quaternion.identity, Vector3.one, pts, sd, 1, new Color(0.75f, 0.55f, 1f) * 1.3f, new Vector4(0.035f, 0, 0, 0.7f));
            }
        }

        // ================= 3.2 the autumn forest =================
        void Autumn()
        {
            title = "Осенний лес";
            ambSky = Mats.Hex(0x8a7a90) * 0.4f; ambEq = Mats.Hex(0x4a3628) * 0.4f; ambGround = Mats.Hex(0x1e1208) * 0.4f; moonK = 0.85f; heroLightK = 1.45f; exposure = 1.3f;
            Paths(M.Tint("ROAD", 0x3a2a1a), 0.75f, M.Tint("GROUND", 0x3a2814));
            Ground(M.Tint("GROUND", 0x3a2814));
            string[] TR = { "aoak1", "aoak3", "abirch", "apine", "awillow" };
            Edges((p, s) => Tree(TR[R.Next(TR.Length)], p, Rr(0.9f, 1.2f), 0.4f), 1.7f);
            for (int i = 0; i < 16; i++) { var p = Spot(1.1f, 1.3f); if (!float.IsNaN(p.x)) Tree(TR[R.Next(TR.Length)], p, Rr(0.85f, 1.15f)); }
            // wild pumpkins, fallen logs, mossy stones, ferns gone brown
            for (int i = 0; i < 10; i++) { var p = Spot(0.4f, 1.0f); if (!float.IsNaN(p.x)) Put(Rnd() < 0.6f ? "pumpkin" : "pumpkin3", p, Rr(0.8f, 1.3f)); }
            for (int i = 0; i < 4; i++) { var p = Spot(0.9f, 1.2f); if (!float.IsNaN(p.x)) { Put("logm", p, Rr(0.9f, 1.2f)); colliders.Add(Collider2.Circle(p.x, p.y, 0.4f)); } }
            for (int i = 0; i < 12; i++) { var p = Spot(0.3f, 0.8f); if (!float.IsNaN(p.x)) Put("fern3", p, Rr(0.8f, 1.1f), -1, 0, false, null, k => k == "LEAF" ? M["AUTUMN"] : null); }
            // an old altar of mossy stones
            {
                var p = Spot(1.4f, 1.6f);
                if (!float.IsNaN(p.x))
                {
                    Obj(MeshGen.Box(1.6f, 0.8f, 0.9f), M.Tint("ROCK", 0x5a5850), new Vector3(p.x, 0.4f, p.y), Quaternion.Euler(0, Rr(0, 360), 0), true, "Altar");
                    Obj(MeshGen.Box(1.4f, 0.06f, 0.75f), M["MOSS"], new Vector3(p.x, 0.83f, p.y), Quaternion.identity, false, "AltarMoss");
                    for (int k = 0; k < 4; k++) { float a = k * 1.571f + 0.6f; Put("mrock3", p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1.5f, 1.1f); }
                    colliders.Add(Collider2.Circle(p.x, p.y, 0.9f));
                }
            }
            // night: a few warm wayside lanterns, ground mist, the slow fall of leaves, eddies of leaves in the wind
            for (int i = 0; i < 4; i++)
            {
                var p = Spot(0.3f, 0.9f); if (float.IsNaN(p.x)) continue;
                Obj(MeshGen.Cylinder(0.04f, 0.04f, 1.4f, 6), M["WOOD"], new Vector3(p.x, 0.7f, p.y), Quaternion.identity, true, "LanternPost");
                Obj(MeshGen.Box(0.16f, 0.22f, 0.16f), M["WINDOW"], new Vector3(p.x, 1.5f, p.y), Quaternion.identity, false, "Lantern");
                var l = Lamp(new Vector3(p.x, 1.5f, p.y), new Color(1f, 0.66f, 0.32f), 1.0f, 4f); flicks.Add(new Flick { l = l, baseI = 1.0f, ph = Rnd() * 5, kind = 0 });
                colliders.Add(Collider2.Circle(p.x, p.y, 0.1f));
            }
            Grass(0.5f);
            Motes(4, 160, new Color(0.85f, 0.42f, 0.12f), 0f, 1f, new Vector4(0.06f, 0, 0, 0), new Vector3(1, 6.5f, 1));
            Forest.Mist(T, new Vector3(0, 0.15f, 0), HX * 1.15f, HZ * 1.15f, new Color(0.6f, 0.5f, 0.45f) * 0.06f);
        }

        // ================= 3.3 the snowy heights =================
        void Snow()
        {
            title = "Заснеженное высокогорье";
            ambSky = Mats.Hex(0x9ab0e0) * 0.5f; ambEq = Mats.Hex(0x6a7488) * 0.45f; ambGround = Mats.Hex(0x3a4250) * 0.4f; moonK = 1.2f; heroLightK = 1.3f; exposure = 1.15f;
            Paths(M.Tint("SNOW", 0xa8b2bc), 0.7f, M["SNOW"]);
            Ground(M["SNOW"]);
            // icy cliffs on the closed sides, with icicles
            var ice = M["ICE"]; var cliff = M.Tint("WETROCK", 0x5a6270);
            Edges((p, s) =>
            {
                float yaw = s == 0 ? 0 : s == 1 ? 180 : s == 2 ? 90 : -90;
                Obj(Cave.CaveMesh(Rnd() < 0.5f ? "wall_a" : "wall_b", new Vector3(2.2f, Rr(3.0f, 4.2f), 1.7f), Rnd() < 0.5f), cliff, new Vector3(p.x, -0.05f, p.y), Quaternion.Euler(0, yaw + Rr(-8, 8), 0), true, "Cliff");
                Obj(MeshGen.Box(2.0f, 0.18f, 1.2f), M["SNOW"], new Vector3(p.x, Rr(2.6f, 3.4f), p.y), Quaternion.Euler(Rr(-8, 8), yaw, Rr(-6, 6)), true, "SnowCap");
                for (int k = 0; k < 3; k++) { float h = Rr(0.3f, 0.9f); Obj(MeshGen.Cylinder(0.01f, 0.05f, h, 6), ice, new Vector3(p.x + Rr(-0.8f, 0.8f), Rr(1.6f, 2.4f) - h / 2, p.y + (s == 0 ? 0.7f : s == 1 ? -0.7f : 0) + (s == 2 ? 0.7f : s == 3 ? -0.7f : 0)), Quaternion.identity, false, "Icicle"); }
                colliders.Add(Collider2.Circle(p.x, p.y, 1.0f));
            }, 1.9f);
            // a frozen lake (walkable ice) and a frozen waterfall falling into it off the cliff
            {
                var p = Spot(2.4f, 1.8f);
                if (!float.IsNaN(p.x))
                {
                    var g = Obj(MeshGen.Cylinder(1f, 1f, 0.04f, 32), ice, new Vector3(p.x, 0.01f, p.y), Quaternion.Euler(0, Rr(0, 180), 0), false, "FrozenLake"); g.transform.localScale = new Vector3(2.6f, 1, 1.7f);
                    for (int k = 0; k < 8; k++) Obj(MeshGen.Box(Rr(0.4f, 1.2f), 0.01f, 0.03f), M.Tint("ICE", 0xdfefff), new Vector3(p.x + Rr(-1.6f, 1.6f), 0.035f, p.y + Rr(-1f, 1f)), Quaternion.Euler(0, Rr(0, 180), 0), false, "IceCrack");
                }
            }
            {   // the frozen waterfall: columns of ice down a cliff face
                var p = new Vector2(Rr(-4, 4), openN ? HZ - 0.9f : -HZ + 0.9f);
                for (int k = 0; k < 7; k++) { float h = Rr(1.8f, 3.2f); Obj(MeshGen.Cylinder(Rr(0.08f, 0.18f), 0.04f, h, 7), ice, new Vector3(p.x - 0.9f + k * 0.3f, h / 2, p.y), Quaternion.Euler(0, 0, 180), true, "IceFall"); }
            }
            // snowy firs, bare birches, snow-covered rocks and stumps and logs, bushes under snow; cairns marking the path
            string[] TR = { "spine1", "spine3", "spine5", "soak3", "sbirchdead" };
            for (int i = 0; i < 14; i++) { var p = Spot(1.0f, 1.3f); if (!float.IsNaN(p.x)) Tree(TR[R.Next(TR.Length)], p, Rr(0.85f, 1.2f)); }
            for (int i = 0; i < 9; i++) { var p = Spot(0.7f, 1.1f); if (!float.IsNaN(p.x)) { Put(i % 3 == 0 ? "srock6" : i % 3 == 1 ? "srock3" : "srock1", p, Rr(1.0f, 1.6f)); if (i % 3 == 0) colliders.Add(Collider2.Circle(p.x, p.y, 0.6f)); } }
            for (int i = 0; i < 4; i++) { var p = Spot(0.6f, 1.1f); if (!float.IsNaN(p.x)) Put(i % 2 == 0 ? "sstump" : "slog", p, Rr(0.9f, 1.1f)); }
            for (int i = 0; i < 7; i++) { var p = Spot(0.5f, 1.0f); if (!float.IsNaN(p.x)) Put("sbush", p, Rr(0.8f, 1.1f)); }
            foreach (var line in paths)
                for (int k = 1; k < line.Length; k++)
                {
                    var q = Vector2.Lerp(line[k - 1], line[k], 0.5f) + new Vector2(1.2f, 0.4f); if (Mathf.Abs(q.x) > HX - 1 || Mathf.Abs(q.y) > HZ - 1) continue;
                    float y = 0; for (int s = 0; s < 4; s++) { float r = 0.22f - s * 0.04f; Obj(MeshGen.Sphere(r, 8, 6), M.Tint("ROCK", 0x6a6e76), new Vector3(q.x + Rr(-0.03f, 0.03f), y + r * 0.8f, q.y), Quaternion.Euler(0, Rr(0, 360), 0), true, "Cairn"); y += r * 1.5f; }
                    colliders.Add(Collider2.Circle(q.x, q.y, 0.22f));
                }
            Grass(0.5f);
            // a snowfall that comes and goes; the moon bright on the ice
            Motes(4, 260, new Color(0.85f, 0.9f, 1f), 0f, 1f, new Vector4(0.05f, 0, 0, 0), new Vector3(1, 7f, 1));
        }
    }
}
