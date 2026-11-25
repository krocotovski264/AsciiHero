// The forest (biome reference 1.1): oaks, pines and birches, ferns, bushes, forest flowers, mushrooms at the roots and on
// stumps, a trodden path, mossy boulders, a quiet lake with lilies and reeds, fallen logs and stumps, falling leaves;
// at night fireflies over the water and the grass, moonlight through the leaves, a light mist over the lake.
// 2 x 2 sectors of 10 x 10 m, local coordinates x, z in -10..10. It stands west of the field (its east edge, the forest
// edge, meets the field's west edge: sparse trees and bushes there); the cave is north of it: a rocky slope with the
// cave mouth, overgrown with ivy and moss, where the path ends.
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public class Forest : Location
    {
        public const float HX = 10, HZ = 10, WATER_Y = 0.06f, ORIGIN_X = -20f;     // (ORIGIN_X: where the game puts it, west of the meadow)
        public static readonly Vector3 LAKE = new Vector3(-4.6f, 0, 3.6f); public const float LAKE_RX = 3.0f, LAKE_RZ = 2.3f;
        /// the path (local): from the field edge (east) to the cave mouth (north)
        public static readonly Vector2[] PATH = { new Vector2(10.6f, 2.0f), new Vector2(7.0f, 1.5f), new Vector2(3.6f, -0.8f), new Vector2(4.0f, -4.6f), new Vector2(5.0f, -7.8f), new Vector2(5.0f, -10.8f) };
        public const float MOUTH_X = 5f, MOUTH_HW = 1.7f;
        public System.Random R;
        float Rnd() { return (float)R.NextDouble(); }
        float Rr(float a, float b) { return a + (b - a) * Rnd(); }

        public override bool HasWater => true;
        public override List<Bounds> MirrorWater() { return new List<Bounds> { new Bounds(new Vector3(LAKE.x + origin.x, WATER_Y, LAKE.z + origin.z), new Vector3(LAKE_RX * 2.4f, 0.2f, LAKE_RZ * 2.4f)) }; }
        public override bool WaterIn(Rect w) { return w.Overlaps(new Rect(LAKE.x + origin.x - LAKE_RX * 1.2f, LAKE.z + origin.z - LAKE_RZ * 1.2f, LAKE_RX * 2.4f, LAKE_RZ * 2.4f)); }
        public override float WaterY => WATER_Y;
        public static float LakeK(float x, float z)
        {
            float a = Mathf.Atan2((z - LAKE.z) / LAKE_RZ, (x - LAKE.x) / LAKE_RX);
            float wob = 1 + 0.08f * Mathf.Sin(a * 3 + 1.3f) + 0.05f * Mathf.Sin(a * 5 + 0.4f);
            float dx = (x - LAKE.x) / LAKE_RX, dz = (z - LAKE.z) / LAKE_RZ;
            return Mathf.Sqrt(dx * dx + dz * dz) / wob;
        }
        public override bool InWater(float x, float z) { return LakeK(x, z) < 0.97f; }
        public override float ShoreK(float x, float z) { float k = LakeK(x, z); return k < 0.97f ? k : -1; }
        /// a forest lake: a shelf by the shore, the middle too deep to wade
        public override float Depth(float x, float z) { float k = LakeK(x, z); return k >= 0.97f ? 0 : WaterDepth.Lake(Mathf.Clamp01((0.97f - k) / 0.97f), 0.42f, 2.0f); }
        public static float PathD(float x, float z)
        {
            float best = 99; var q = new Vector2(x, z);
            for (int i = 0; i + 1 < PATH.Length; i++)
            {
                Vector2 a = PATH[i], ab = PATH[i + 1] - a; float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, (q - (a + ab * t)).magnitude);
            }
            return best;
        }

        public static Forest Build(Mats mats, int seed) { var F = new Forest(); F.R = new System.Random(seed); F.Construct(mats); return F; }

        Material see(Mats mats, string k) { return NatureLib.SeeThrough(mats[k]); }
        void Construct(Mats mats)
        {
            id = "forest";
            root = new GameObject("Forest"); var T = root.transform;
            // darker under the trees than in the open field; the moon comes through the leaves only in part
            ambSky = Mats.Hex(0x7088c0) * 0.36f; ambEq = Mats.Hex(0x3a4a44) * 0.36f; ambGround = Mats.Hex(0x141e10) * 0.4f;
            moonK = 0.75f; heroLightK = 1.45f; exposure = 1.35f;

            // ---- ground: tiles in local coordinates, the lake left out; a darker trodden path on top ----
            {
                const float TS = 3.96f; const int SUB = 12; const float Q = TS / SUB;
                for (int ix = 0; ix < 7; ix++)
                    for (int iz = 0; iz < 6; iz++)
                    {
                        float x0 = -HX - 3.86f + ix * TS, z0 = -HZ + iz * TS;            // (north of z = -10 the cave's floor and rock take over)
                        var vs = new List<Vector3>(); var ts = new List<int>();
                        for (int a = 0; a < SUB; a++)
                            for (int b = 0; b < SUB; b++)
                            {
                                float qx = x0 + a * Q, qz = z0 + b * Q;
                                if (qx >= HX - 0.001f || qz >= HZ - 0.001f) continue;                 // (the field's ground goes on from x = 10, the swamp's from z = 10)
                                float qx1 = Mathf.Min(qx + Q, HX);
                                if (Mathf.Max(Mathf.Max(LakeK(qx, qz), LakeK(qx1, qz)), Mathf.Max(LakeK(qx, qz + Q), LakeK(qx1, qz + Q))) < 0.98f) continue;
                                int o = vs.Count;
                                vs.Add(new Vector3(qx, 0, qz)); vs.Add(new Vector3(qx, 0, qz + Q)); vs.Add(new Vector3(qx1, 0, qz + Q)); vs.Add(new Vector3(qx1, 0, qz));
                                ts.Add(o); ts.Add(o + 1); ts.Add(o + 2); ts.Add(o); ts.Add(o + 2); ts.Add(o + 3);
                            }
                        if (vs.Count == 0) continue;
                        var gm = new Mesh(); gm.SetVertices(vs); gm.uv = new Vector2[vs.Count]; gm.SetTriangles(ts, 0);
                        gm.RecalculateNormals(); gm.RecalculateBounds(); MeshGen.AddGlyphUV(gm, false);
                        MeshGen.Obj(gm, mats["GROUND"], T, Vector3.zero, Quaternion.identity, false, "Ground");
                    }
                Strip(T, mats["ROAD"], PATH, 0.8f, 0.006f, "Path");
            }
            // ---- the lake: dark surface + its characters (and the mirror) ----
            {
                const int RINGS = 12, SEG = 56;
                var vs = new List<Vector3> { new Vector3(LAKE.x, WATER_Y, LAKE.z) }; var ts = new List<int>();
                for (int r = 1; r <= RINGS; r++)
                    for (int s = 0; s < SEG; s++)
                    {
                        float a = s * Mathf.PI * 2 / SEG, f = r / (float)RINGS * 1.01f, wob = 1 + 0.08f * Mathf.Sin(a * 3 + 1.3f) + 0.05f * Mathf.Sin(a * 5 + 0.4f);
                        vs.Add(new Vector3(LAKE.x + Mathf.Cos(a) * LAKE_RX * f * wob, WATER_Y, LAKE.z + Mathf.Sin(a) * LAKE_RZ * f * wob));
                    }
                for (int s = 0; s < SEG; s++) { ts.Add(0); ts.Add(1 + (s + 1) % SEG); ts.Add(1 + s); }
                for (int r = 1; r < RINGS; r++)
                    for (int s = 0; s < SEG; s++)
                    {
                        int a0 = 1 + (r - 1) * SEG + s, a1 = 1 + (r - 1) * SEG + (s + 1) % SEG, b0 = a0 + SEG, b1 = a1 + SEG;
                        ts.Add(a0); ts.Add(a1); ts.Add(b1); ts.Add(a0); ts.Add(b1); ts.Add(b0);
                    }
                var wm = new Mesh { name = "forestlake" }; wm.SetVertices(vs); wm.SetTriangles(ts, 0); wm.RecalculateNormals(); wm.RecalculateBounds();
                var lake = MeshGen.Obj(wm, mats["WATER"], T, Vector3.zero, Quaternion.identity, false, "Lake");
                var wp = new List<Vector3>(); var wsd = new List<Vector2>();
                for (float z = LAKE.z - LAKE_RZ * 1.2f; z <= LAKE.z + LAKE_RZ * 1.2f; z += 0.13f)
                    for (float x = LAKE.x - LAKE_RX * 1.2f; x <= LAKE.x + LAKE_RX * 1.2f; x += 0.1f)
                    {
                        float kk = LakeK(x, z); if (kk > 1.0f) continue;
                        wp.Add(new Vector3(x, WATER_Y, z)); wsd.Add(new Vector2(kk, Rnd()));
                    }
                GlyphLayer.MakeWater(lake.transform, wp, wsd);
                // lilies on the water, reeds on the bank
                for (int i = 0; i < 7; i++) { float a = Rr(0, 6.28f), f = Rr(0.3f, 0.8f); NatureLib.Place("lily", mats, T, new Vector3(LAKE.x + Mathf.Cos(a) * LAKE_RX * f, WATER_Y + 0.01f, LAKE.z + Mathf.Sin(a) * LAKE_RZ * f), Rr(0, 360), Rr(0.6f, 1f), null, false); }
                for (int i = 0; i < 14; i++) { float a = Rr(0, 6.28f), f = Rr(0.95f, 1.12f); NatureLib.Place("reed", mats, T, new Vector3(LAKE.x + Mathf.Cos(a) * LAKE_RX * f, 0, LAKE.z + Mathf.Sin(a) * LAKE_RZ * f), Rr(0, 360), Rr(0.8f, 1.2f), null, false); }
            }
            spawn = new Vector3(8.2f, 0, 2.0f);

            bool Free(float x, float z, float r)
            {
                if (LakeK(x, z) < 1.15f + r * 0.3f) return false;
                if (PathD(x, z) < 1.0f + r) return false;
                if (Mathf.Abs(x - MOUTH_X) < MOUTH_HW + 1.2f && z < -6.5f) return false;              // the cave mouth and its approach
                if ((new Vector2(x, z) - new Vector2(spawn.x, spawn.z)).magnitude < 1.5f + r) return false;
                foreach (var c in colliders) if (c.circle && (new Vector2(x - c.x, z - c.z)).magnitude < c.r + r + 0.2f) return false;
                return true;
            }
            Vector2 Place(float r, float x0, float x1, float z0, float z1)
            {
                for (int k = 0; k < 120; k++) { float x = Rr(x0, x1), z = Rr(z0, z1); if (Free(x, z, r)) return new Vector2(x, z); }
                return new Vector2(float.NaN, 0);
            }
            System.Func<string, Material> seeMat = k => (k == "LEAF" || k == "BARK" || k == "BIRCH" || k == "DARK") ? see(mats, k) : null;
            string[] OAK = { "oak1", "oak3", "oak5" }, PINE = { "pine1", "pine2", "pine3", "pine5" }, BIRCH = { "birch1", "birch3", "birch5" };
            var treeAt = new List<Vector2>();
            void Tree(Vector2 p, float s)
            {
                float t = Rnd(); string nm = t < 0.4f ? PINE[R.Next(PINE.Length)] : t < 0.75f ? OAK[R.Next(OAK.Length)] : BIRCH[R.Next(BIRCH.Length)];
                var tg = NatureLib.Place(nm, mats, T, new Vector3(p.x, -0.02f, p.y), Rr(0, 360), s, seeMat);
                var cr = tg.transform.Find("Crown"); if (cr != null) crowns.Add(cr.gameObject);
                colliders.Add(Collider2.Circle(p.x, p.y, 0.32f * s)); treeAt.Add(p);
                occluders.Add(new Vector4(p.x, p.y, 1.6f * s, 5.5f * s));
            }
            // ---- the edges: a thick wall of trees west and south (the near side: lower, smaller trees and bushes) ----
            for (float z = -HZ + 0.6f; z <= HZ; z += Rr(1.6f, 2.2f)) Tree(new Vector2(-HX + Rr(-0.4f, 0.5f), z), Rr(0.9f, 1.15f));
            for (float x = -HX + 1.5f; x <= HX - 2.5f; x += Rr(1.8f, 2.6f))
            {
                if (Mathf.Abs(x) < 3.2f) continue;          // (the way south, into the swamp)
                if (Rnd() < 0.55f) NatureLib.Place(Rnd() < 0.5f ? "bush1" : "bush2", mats, T, new Vector3(x, 0, HZ - Rr(0, 0.5f)), Rr(0, 360), Rr(0.9f, 1.3f));
                else { Tree(new Vector2(x, HZ + Rr(-0.3f, 0.3f)), Rr(0.7f, 0.9f)); colliders.Add(Collider2.Circle(x, HZ, 0.6f)); }
            }
            // ---- the rocky slope in the north with the cave mouth (the cave's own rock rises behind it) ----
            for (float x = -HX - 0.5f; x <= HX + 0.5f; x += 2.0f)
            {
                if (Mathf.Abs(x - MOUTH_X) < MOUTH_HW + 0.6f) continue;
                bool nearMouth = Mathf.Abs(x - MOUTH_X) < MOUTH_HW + 2.8f;
                float h = nearMouth ? Rr(2.8f, 3.4f) : Rr(2.2f, 3.2f), len = Rr(2.6f, 3.2f);
                MeshGen.Obj(Cave.CaveMesh(Rnd() < 0.5f ? "wall_a" : "wall_b", new Vector3(len, h, len * 0.8f), Rnd() < 0.5f), mats["WETROCK"], T, new Vector3(x, -0.05f, -HZ - 0.35f), Quaternion.Euler(0, Rr(-6, 6), 0), true, "Slope");
                for (float cx = x - 0.9f; cx <= x + 0.91f; cx += 0.6f) colliders.Add(Collider2.Circle(cx, -HZ + 0.1f, 0.55f));
                occluders.Add(new Vector4(x, -HZ, 1.5f, h));
            }
            // the mouth: two jambs of rock, ivy and moss hanging over it
            foreach (int sd in new[] { -1, 1 })
            {
                float jx = MOUTH_X + sd * (MOUTH_HW + 0.35f);
                MeshGen.Obj(Cave.CaveMesh("boulder_a", new Vector3(1.3f, 2.4f, 1.3f), sd > 0), mats["WETROCK"], T, new Vector3(jx, -0.05f, -HZ + 0.2f), Quaternion.Euler(0, Rr(0, 360), 0), true, "Jamb");
                colliders.Add(Collider2.Circle(jx, -HZ + 0.2f, 0.6f));
                for (int k = 0; k < 3; k++) NatureLib.Place(k == 0 ? "bush1" : "fern3", mats, T, new Vector3(jx + Rr(-0.6f, 0.6f), Rr(0.8f, 2.2f), -HZ + Rr(0.1f, 0.5f)), Rr(0, 360), Rr(0.6f, 0.9f), null, false);
            }
            for (int k = 0; k < 5; k++) NatureLib.Place("fern1", mats, T, new Vector3(MOUTH_X + Rr(-1.8f, 1.8f), Rr(2.6f, 3.0f), -HZ - Rr(0.0f, 0.4f)), Rr(0, 360), Rr(0.7f, 1.0f), null, false);    // ivy over the arch
            for (int k = 0; k < 8; k++) { var p = new Vector2(Rr(-HX + 1, HX - 1), -HZ + Rr(0.6f, 1.6f)); if (Mathf.Abs(p.x - MOUTH_X) > MOUTH_HW + 0.8f) { NatureLib.Place(Rnd() < 0.5f ? "mrock3" : "mrock6", mats, T, new Vector3(p.x, 0, p.y), Rr(0, 360), Rr(0.8f, 1.3f)); colliders.Add(Collider2.Circle(p.x, p.y, 0.5f)); } }

            // ---- inside: trees, thinning out towards the field (the forest edge: x > 5) ----
            for (int i = 0; i < 70; i++)
            {
                var p = Place(1.3f, -HX + 1.5f, HX - 0.5f, -HZ + 1.8f, HZ - 1.5f); if (float.IsNaN(p.x)) continue;
                if (p.x > 5f && Rnd() < (p.x - 5f) / 5.5f) continue;            // the edge: fewer trees
                bool close = false; foreach (var q in treeAt) if ((q - p).magnitude < 2.3f) { close = true; break; }
                if (close) continue;
                Tree(p, Rr(0.85f, 1.2f));
            }
            // a fallen tree, logs, stumps (with mushrooms), mossy boulders
            for (int i = 0; i < 4; i++)
            {
                var p = Place(1.0f, -HX + 2, HX - 2, -HZ + 2, HZ - 2); if (float.IsNaN(p.x)) continue; float yaw = Rr(0, 360);
                NatureLib.Place(Rnd() < 0.6f ? "logm" : "log", mats, T, new Vector3(p.x, 0, p.y), yaw, Rr(0.9f, 1.2f));
                var d = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                for (float s = -1f; s <= 1.01f; s += 0.66f) colliders.Add(Collider2.Circle(p.x + d.x * s, p.y + d.z * s, 0.35f));
            }
            var stumps = new List<Vector2>();
            for (int i = 0; i < 6; i++)
            {
                var p = Place(0.6f, -HX + 1.5f, HX - 1.5f, -HZ + 2, HZ - 1.5f); if (float.IsNaN(p.x)) continue;
                NatureLib.Place(Rnd() < 0.5f ? "stumpm" : "stump", mats, T, new Vector3(p.x, 0, p.y), Rr(0, 360), Rr(0.8f, 1.1f));
                colliders.Add(Collider2.Circle(p.x, p.y, 0.45f)); stumps.Add(p);
            }
            for (int i = 0; i < 8; i++)
            {
                var p = Place(0.6f, -HX + 1.5f, HX - 1.5f, -HZ + 2, HZ - 1.5f); if (float.IsNaN(p.x)) continue;
                float rs = Rr(0.8f, 1.3f);
                NatureLib.Place(i % 3 == 0 ? "mrock1" : i % 3 == 1 ? "mrock3" : "mrock6", mats, T, new Vector3(p.x, -0.03f, p.y), Rr(0, 360), rs);
                if (i % 3 != 0 && rs > 1.05f) colliders.Add(Collider2.Circle(p.x, p.y, 0.5f));      // (the small stones are walked over)
            }
            // ---- undergrowth: ferns, bushes, berries, flowers; mushrooms at the roots and on the stumps ----
            for (int i = 0; i < 46; i++) { var p = Place(0.4f, -HX + 0.8f, HX - 0.5f, -HZ + 1.5f, HZ - 0.8f); if (!float.IsNaN(p.x)) NatureLib.Place(Rnd() < 0.5f ? "fern1" : "fern3", mats, T, new Vector3(p.x, 0, p.y), Rr(0, 360), Rr(0.8f, 1.3f), null, false); }
            for (int i = 0; i < 16; i++) { var p = Place(0.6f, -HX + 0.8f, HX - 0.5f, -HZ + 1.5f, HZ - 0.8f); if (float.IsNaN(p.x)) continue; NatureLib.Place(i < 4 ? "bushberry" : Rnd() < 0.5f ? "bush1" : "bush2", mats, T, new Vector3(p.x, 0, p.y), Rr(0, 360), Rr(0.8f, 1.2f)); }      // (bushes: walked through, they part)
            for (int i = 0; i < 18; i++) { var p = Place(0.2f, -HX + 0.8f, HX - 0.5f, -HZ + 1.5f, HZ - 0.8f); if (!float.IsNaN(p.x)) NatureLib.Place("flower3", mats, T, new Vector3(p.x, 0, p.y), Rr(0, 360), Rr(0.8f, 1.2f), null, false, Rnd() < 0.5f ? null : new Dictionary<string, string> { { "FLOWERB", "FLOWERW" } }); }
            for (int i = 0; i < 26; i++)
            {
                Vector2 b = i < 16 && treeAt.Count > 0 ? treeAt[R.Next(treeAt.Count)] : stumps.Count > 0 ? stumps[R.Next(stumps.Count)] : Vector2.zero;
                float a = Rr(0, 6.28f), d = Rr(0.35f, 0.7f); var p = b + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                if (LakeK(p.x, p.y) < 1.1f || PathD(p.x, p.y) < 0.7f) continue;
                NatureLib.Place(Rnd() < 0.6f ? "mush1" : "mush3", mats, T, new Vector3(p.x, i >= 16 ? 0.55f : 0, p.y), Rr(0, 360), Rr(0.8f, 1.3f), null, false);
            }
            // ---- grass: thinner than in the field, none on the path, in the water or under the trunks ----
            {
                var pts = new List<Vector3>(); var seeds = new List<float>(); var mpts = new List<Vector3>(); var mseeds = new List<float>();
                for (int i = 0; i < 2 * HX * 2 * HZ * 38; i++)
                {
                    float x = Rr(-HX - 1.5f, HX), z = Rr(-HZ, HZ);
                    if (LakeK(x, z) < 1.06f || !RoadGrass(x, z, PathD(x, z), 0.8f)) continue;
                    bool blocked = false; foreach (var c in colliders) if (c.circle && (new Vector2(x - c.x, z - c.z)).magnitude < c.r * 0.8f) { blocked = true; break; }
                    if (blocked) continue;
                    // the meadow's edge (east): patches of its tall light grass reach in among the trees, three times as dense
                    float wx = x + ORIGIN_X;
                    if (Rnd() < BiomeBlend.Other(BiomeBlend.FIELD_FOREST_X - wx, wx, z))
                    { for (int k = 0; k < 3; k++) { mpts.Add(new Vector3(x + Rr(-0.08f, 0.08f), 0, z + Rr(-0.08f, 0.08f))); mseeds.Add(Rnd()); } continue; }
                    pts.Add(new Vector3(x, 0, z)); seeds.Add(Rnd());
                }
                GlyphLayer.MakeGrass(pts, seeds, T, new Color(0.12f, 0.40f, 0.20f), new Color(0.36f, 0.62f, 0.28f), 1.15f);
                if (mpts.Count > 0) GlyphLayer.MakeGrass(mpts, mseeds, T, new Color(0.16f, 0.50f, 0.24f), new Color(0.50f, 0.78f, 0.36f), 1.35f);
                // and its flowers
                var POP = new Dictionary<string, string> { { "FLOWERB", "RED" }, { "WHEAT", "RED" } }; var DAI = new Dictionary<string, string> { { "FLOWERB", "FLOWERW" } };
                for (int i = 0; i < 70; i++)
                {
                    float x = Rr(HX - 7f, HX + 1.5f), z = Rr(-HZ + 1f, HZ - 0.5f), wx = x + ORIGIN_X;
                    if (PathD(x, z) < 1f || Rnd() > BiomeBlend.Other(BiomeBlend.FIELD_FOREST_X - wx, wx, z) * 0.8f) continue;
                    float c = Rnd(); NatureLib.Place(c < 0.5f ? "flowers" : "flower3", mats, T, new Vector3(x, 0, z), Rr(0, 360), Rr(0.8f, 1.2f), null, false, c < 0.33f ? POP : c < 0.66f ? DAI : null);
                }
            }
            // ---- night: fireflies at the water and over the grass, falling leaves, mist over the lake, moonlight ----
            {
                var pts = new List<Vector3>(); var sd = new List<Vector2>();
                for (int i = 0; i < 70; i++)
                {
                    bool water = i < 40; float a = Rr(0, 6.28f), f = Rr(0.6f, 1.4f);
                    pts.Add(water ? new Vector3(LAKE.x + Mathf.Cos(a) * LAKE_RX * f, Rr(0.2f, 1.4f), LAKE.z + Mathf.Sin(a) * LAKE_RZ * f) : new Vector3(Rr(-HX + 1, HX - 1), Rr(0.3f, 1.6f), Rr(-HZ + 2, HZ - 1)));
                    sd.Add(new Vector2(0, Rnd()));
                }
                GlyphLayer.MakeMotes(T, Vector3.zero, Quaternion.identity, Vector3.one, pts, sd, 1, new Color(0.8f, 1f, 0.45f) * 1.25f, new Vector4(0.045f, 0, 0, 0.5f));
                var lp = new List<Vector3>(); var ls = new List<Vector2>();
                for (int i = 0; i < 90; i++) { lp.Add(new Vector3(Rr(-HX, HX), Rr(0, 1), Rr(-HZ, HZ))); ls.Add(new Vector2(Rnd(), Rnd())); }
                GlyphLayer.MakeMotes(T, new Vector3(0, 0, 0), Quaternion.identity, new Vector3(1, 6.5f, 1), lp, ls, 4, new Color(0.55f, 0.62f, 0.32f), new Vector4(0.05f, 0, 0, 0));
                Mist(T, new Vector3(LAKE.x, WATER_Y + 0.05f, LAKE.z), LAKE_RX * 1.15f, LAKE_RZ * 1.15f, new Color(0.5f, 0.58f, 0.7f) * 0.07f);
                Mist(T, new Vector3(LAKE.x + 0.4f, WATER_Y + 0.35f, LAKE.z - 0.2f), LAKE_RX * 0.9f, LAKE_RZ * 0.85f, new Color(0.5f, 0.58f, 0.7f) * 0.05f);
                // two shafts of moonlight through gaps in the crowns
                MoonShaft(T, new Vector3(-1.5f, 0, -3.5f), 0.9f); MoonShaft(T, new Vector3(1.8f, 0, 5.2f), 0.75f);
            }
            bounds = Rect.MinMaxRect(-HX + 0.4f, -HZ - 2.6f, HX + 3.0f, HZ + 2.6f);         // (open south: the swamp)
        }
        /// a flat band on the ground along a polyline (the path)
        public static bool RoadGrass(float x, float z, float distance, float halfWidth)
        {
            float edge = halfWidth * (0.78f + 0.44f * Mathf.PerlinNoise(x * 2.3f, z * 2.3f));
            float chance = Mathf.InverseLerp(edge - 0.28f, edge + 0.40f, distance);
            return Mathf.PerlinNoise(x * 37.1f + 17f, z * 37.1f + 29f) < chance;
        }
        static void Strip(Transform T, Material m, Vector2[] line, float hw, float y, string name)
        {
            var vs = new List<Vector3>(); var ts = new List<int>();
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector2 a = line[i], b = line[i + 1], d = (b - a).normalized, n = new Vector2(-d.y, d.x);
                int steps = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / 0.12f));
                for (int s = 0; s <= steps; s++)
                {
                    var p = Vector2.Lerp(a, b, s / (float)steps); float w = hw * (0.78f + 0.44f * Mathf.PerlinNoise(p.x * 2.3f, p.y * 2.3f));
                    vs.Add(new Vector3(p.x + n.x * w, y, p.y + n.y * w)); vs.Add(new Vector3(p.x - n.x * w, y, p.y - n.y * w));
                    if (s > 0) { int o = vs.Count - 4; ts.Add(o); ts.Add(o + 2); ts.Add(o + 1); ts.Add(o + 1); ts.Add(o + 2); ts.Add(o + 3); }
                }
            }
            var mesh = new Mesh { name = name }; mesh.SetVertices(vs); mesh.uv = new Vector2[vs.Count]; mesh.SetTriangles(ts, 0);
            mesh.RecalculateNormals();
            var nn = mesh.normals; for (int i = 0; i < nn.Length; i++) if (nn[i].y < 0) { var tt = mesh.triangles; for (int k = 0; k < tt.Length; k += 3) { int x = tt[k + 1]; tt[k + 1] = tt[k + 2]; tt[k + 2] = x; } mesh.triangles = tt; mesh.RecalculateNormals(); break; }
            mesh.RecalculateBounds(); MeshGen.AddGlyphUV(mesh, false);
            MeshGen.Obj(mesh, m, T, Vector3.zero, Quaternion.identity, false, name);
        }
        public static void PathStrip(Transform T, Material m, Vector2[] line, float hw, float y, string name) { Strip(T, m, line, hw, y, name); }
        /// a low layer of mist: a flat additive disc in the lit colour (AsciiHero/Beam, seen from above)
        public static void Mist(Transform T, Vector3 c, float rx, float rz, Color col)
        {
            var sh = Shader.Find("AsciiHero/Beam"); if (sh == null) return;
            const int SEG = 32; var vs = new List<Vector3> { Vector3.zero }; var ns = new List<Vector3> { Vector3.up }; var uv = new List<Vector2> { new Vector2(0, 0.5f) }; var ts = new List<int>();
            for (int s = 0; s < SEG; s++) { float a = s * Mathf.PI * 2 / SEG; vs.Add(new Vector3(Mathf.Cos(a) * rx, 0, Mathf.Sin(a) * rz)); ns.Add(Vector3.up); uv.Add(new Vector2(s / (float)SEG, 0.15f)); }
            for (int s = 0; s < SEG; s++) { ts.Add(0); ts.Add(1 + (s + 1) % SEG); ts.Add(1 + s); }
            var m = new Mesh { name = "mist" }; m.SetVertices(vs); m.SetNormals(ns); m.SetUVs(0, uv); m.SetTriangles(ts, 0); m.RecalculateBounds();
            var mat = new Material(sh); mat.SetColor("_BeamColor", col);
            var g = new GameObject("Mist"); g.transform.SetParent(T, false); g.transform.localPosition = c;
            g.AddComponent<MeshFilter>().sharedMesh = m; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }
        /// moonlight through a gap in the leaves: a faint cone and the dust in it
        void MoonShaft(Transform T, Vector3 to, float radius)
        {
            var from = to + new Vector3(0.8f, 7.5f, -1.2f); var d = from - to; float len = d.magnitude;
            var sh = Shader.Find("AsciiHero/Beam");
            if (sh != null)
            {
                const int SEG = 24; float rTop = radius * 0.5f, rBot = radius * 1.1f;
                var cv = new List<Vector3>(); var cn = new List<Vector3>(); var cu = new List<Vector2>(); var ct = new List<int>();
                for (int k = 0; k <= SEG; k++)
                    for (int e = 0; e <= 4; e++)
                    {
                        float a = k * Mathf.PI * 2 / SEG, v = e / 4f, rr = Mathf.Lerp(rBot, rTop, v);
                        cv.Add(new Vector3(Mathf.Cos(a) * rr, v * len, Mathf.Sin(a) * rr)); cn.Add(new Vector3(Mathf.Cos(a), 0.05f, Mathf.Sin(a)).normalized); cu.Add(new Vector2(k / (float)SEG, v));
                    }
                for (int k = 0; k < SEG; k++) for (int e = 0; e < 4; e++) { int o = k * 5 + e; ct.Add(o); ct.Add(o + 1); ct.Add(o + 5); ct.Add(o + 1); ct.Add(o + 6); ct.Add(o + 5); }
                var cm = new Mesh { name = "moonshaft" }; cm.SetVertices(cv); cm.SetNormals(cn); cm.SetUVs(0, cu); cm.SetTriangles(ct, 0); cm.RecalculateBounds();
                var bm = new Material(sh); bm.SetColor("_BeamColor", new Color(0.55f, 0.65f, 0.95f) * 0.12f);
                var cg = new GameObject("MoonShaft"); cg.transform.SetParent(T, false); cg.transform.localPosition = to; cg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
                cg.AddComponent<MeshFilter>().sharedMesh = cm; var cr = cg.AddComponent<MeshRenderer>(); cr.sharedMaterial = bm;
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; cr.receiveShadows = false;
            }
            var pts = new List<Vector3>(); var sd = new List<Vector2>();
            for (int i = 0; i < 80; i++) { float a = Rr(0, 6.28f), r = Mathf.Sqrt(Rnd()); pts.Add(new Vector3(Mathf.Cos(a) * r, Rnd(), Mathf.Sin(a) * r)); sd.Add(new Vector2(r, Rnd())); }
            GlyphLayer.MakeMotes(T, to, Quaternion.FromToRotation(Vector3.up, d.normalized), new Vector3(radius, len, radius), pts, sd, 0, new Color(0.6f, 0.7f, 1f) * 0.7f, new Vector4(0.04f, 0, 0, 0));
        }
        public override void Cutaway(Vector3 camOff) { OccluderSeeThrough(camOff); }
    }
}
