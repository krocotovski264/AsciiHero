// Test field: open ground the size of four taverns (20 x 16 m), a few trees and rocks, grass that reacts to the
// hero, and ten piles of armour with random contents.
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
    }

    public class Field : Location
    {
        public readonly List<ArmourPile> piles = new List<ArmourPile>();
        public readonly List<Vector4> trees = new List<Vector4>();     // x, z, trunk height, crown radius (the lake mirrors them)
        const float HX = 10, HZ = 8;
        // the lake in the bottom right corner of the view (the camera looks along -z, so screen right is -x, down is +z)
        public static readonly Vector3 LAKE = new Vector3(-6.2f, 0, 4.4f); public const float LAKE_RX = 3.1f, LAKE_RZ = 2.4f, WATER_Y = 0.06f;
        /// 0 in the middle of the lake, 1 on its shore, more outside
        public static float LakeK(float x, float z)
        {
            float a = Mathf.Atan2((z - LAKE.z) / LAKE_RZ, (x - LAKE.x) / LAKE_RX);
            float wob = 1 + 0.07f * Mathf.Sin(a * 3 + 0.7f) + 0.05f * Mathf.Sin(a * 5 + 2.1f);       // not a perfect ellipse
            float dx = (x - LAKE.x) / LAKE_RX, dz = (z - LAKE.z) / LAKE_RZ;
            return Mathf.Sqrt(dx * dx + dz * dz) / wob;
        }
        public static readonly Vector3[] DUMMY_SPOTS = { new Vector3(1.75f, 0, -1.2f), new Vector3(-1.75f, 0, -1.2f) };   // [0] only takes hits, [1] hits back

        public static Field Build(Mats mats)
        {
            var F = new Field(); F.Construct(mats); return F;
        }

        static Mesh Ground(float w, float d)
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-w / 2, 0, -d / 2), new Vector3(-w / 2, 0, d / 2), new Vector3(w / 2, 0, d / 2), new Vector3(w / 2, 0, -d / 2) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 }; m.RecalculateNormals(); m.RecalculateBounds(); MeshGen.AddGlyphUV(m, false); return m;
        }

        void Construct(Mats mats)
        {
            root = new GameObject("Field"); var T = root.transform; var R = new System.Random(20261112);
            float Rnd() { return (float)R.NextDouble(); }
            // the ground in tiles (their size is a whole number of glyph cells and the vertices are in field coordinates,
            // so the rows of characters run on across the joins): tiles outside the view are culled with their characters.
            // Each tile is a grid of small squares, so the squares under the lake can be left out.
            {
                const float TS = 3.96f; const int SUB = 12; const float Q = TS / SUB;
                for (int ix = 0; ix < 7; ix++)
                    for (int iz = 0; iz < 6; iz++)
                    {
                        float x0 = (ix - 3.5f) * TS, z0 = (iz - 3f) * TS;
                        var vs = new List<Vector3>(); var uvs = new List<Vector2>(); var ts = new List<int>();
                        for (int a = 0; a < SUB; a++)
                            for (int b = 0; b < SUB; b++)
                            {
                                float qx = x0 + a * Q, qz = z0 + b * Q;
                                if (Mathf.Max(Mathf.Max(LakeK(qx, qz), LakeK(qx + Q, qz)), Mathf.Max(LakeK(qx, qz + Q), LakeK(qx + Q, qz + Q))) < 0.98f) continue;     // under water
                                int o = vs.Count;
                                vs.Add(new Vector3(qx, 0, qz)); vs.Add(new Vector3(qx, 0, qz + Q)); vs.Add(new Vector3(qx + Q, 0, qz + Q)); vs.Add(new Vector3(qx + Q, 0, qz));
                                uvs.Add(new Vector2(a / (float)SUB, b / (float)SUB)); uvs.Add(new Vector2(a / (float)SUB, (b + 1) / (float)SUB));
                                uvs.Add(new Vector2((a + 1) / (float)SUB, (b + 1) / (float)SUB)); uvs.Add(new Vector2((a + 1) / (float)SUB, b / (float)SUB));
                                ts.Add(o); ts.Add(o + 1); ts.Add(o + 2); ts.Add(o); ts.Add(o + 2); ts.Add(o + 3);
                            }
                        if (vs.Count == 0) continue;
                        var gm = new Mesh(); gm.SetVertices(vs); gm.SetUVs(0, uvs); gm.SetTriangles(ts, 0);
                        gm.RecalculateNormals(); gm.RecalculateBounds(); MeshGen.AddGlyphUV(gm, false);
                        MeshGen.Obj(gm, mats["GROUND"], T, Vector3.zero, Quaternion.identity, false, "Ground");
                    }
            }
            // the lake: a dark surface a few centimetres above the ground (it hides what is under it from the ground's
            // characters) and its own characters, which the water shader animates
            {
                const int RINGS = 14, SEG = 64;
                var vs = new List<Vector3> { new Vector3(LAKE.x, WATER_Y, LAKE.z) }; var ts = new List<int>();
                for (int r = 1; r <= RINGS; r++)
                    for (int sgi = 0; sgi < SEG; sgi++)
                    {
                        float a = sgi * Mathf.PI * 2 / SEG, f = r / (float)RINGS * 1.01f;
                        float wob = 1 + 0.07f * Mathf.Sin(a * 3 + 0.7f) + 0.05f * Mathf.Sin(a * 5 + 2.1f);
                        vs.Add(new Vector3(LAKE.x + Mathf.Cos(a) * LAKE_RX * f * wob, WATER_Y, LAKE.z + Mathf.Sin(a) * LAKE_RZ * f * wob));
                    }
                for (int sgi = 0; sgi < SEG; sgi++) { ts.Add(0); ts.Add(1 + (sgi + 1) % SEG); ts.Add(1 + sgi); }
                for (int r = 1; r < RINGS; r++)
                    for (int sgi = 0; sgi < SEG; sgi++)
                    {
                        int a0 = 1 + (r - 1) * SEG + sgi, a1 = 1 + (r - 1) * SEG + (sgi + 1) % SEG, b0 = a0 + SEG, b1 = a1 + SEG;
                        ts.Add(a0); ts.Add(a1); ts.Add(b1); ts.Add(a0); ts.Add(b1); ts.Add(b0);
                    }
                var wm = new Mesh { name = "lake" }; wm.SetVertices(vs); wm.SetTriangles(ts, 0); wm.RecalculateNormals(); wm.RecalculateBounds();
                var lake = MeshGen.Obj(wm, mats["WATER"], T, Vector3.zero, Quaternion.identity, false, "Lake");
                var wp = new List<Vector3>(); var wsd = new List<Vector2>();
                const float SX = 0.1f, SZ = 0.13f;
                for (float z = LAKE.z - LAKE_RZ * 1.15f; z <= LAKE.z + LAKE_RZ * 1.15f; z += SZ)
                    for (float x = LAKE.x - LAKE_RX * 1.15f; x <= LAKE.x + LAKE_RX * 1.15f; x += SX)
                    {
                        float kk = LakeK(x, z); if (kk > 1.0f) continue;
                        wp.Add(new Vector3(x, WATER_Y, z)); wsd.Add(new Vector2(kk, Rnd()));
                    }
                GlyphLayer.MakeWater(lake.transform, wp, wsd);
            }
            spawn = new Vector3(0, 0, 1.5f);
            // two training dummies in the middle, 3.5 m apart (the game builds them): keep the spots clear
            foreach (var dp in DUMMY_SPOTS) colliders.Add(Collider2.Circle(dp.x, dp.z, 0.34f));
            bool Free(float x, float z, float r)
            {
                if (Mathf.Sqrt(x * x + z * z) < 1.5f + r) return false;
                if (LakeK(x, z) < 1.25f + r * 0.4f) return false;          // nothing in the water or right on its shore
                foreach (var c in colliders) if (Mathf.Sqrt((x - c.x) * (x - c.x) + (z - c.z) * (z - c.z)) < c.r + r + 0.5f) return false;
                foreach (var p in piles) if (Mathf.Sqrt((x - p.pos.x) * (x - p.pos.x) + (z - p.pos.z) * (z - p.pos.z)) < 1.6f + r) return false;
                return true;
            }
            Vector2 Place(float r, float margin)
            {
                for (int k = 0; k < 200; k++) { float x = (Rnd() * 2 - 1) * (HX - margin), z = (Rnd() * 2 - 1) * (HZ - margin); if (Free(x, z, r)) return new Vector2(x, z); }
                return Vector2.zero;
            }
            // trees
            for (int i = 0; i < 7; i++)
            {
                var p = Place(0.4f, 1.5f); float h = 1.5f + Rnd() * 0.8f, r = 0.11f + Rnd() * 0.06f;
                MeshGen.Obj(MeshGen.Cylinder(r, r * 0.7f, h, 8), mats["WOOD"], T, new Vector3(p.x, h / 2, p.y), Quaternion.identity, true, "Trunk");
                for (int k = 0; k < 3; k++) MeshGen.Obj(MeshGen.Cylinder(0.62f - k * 0.15f, 0, 0.9f, 9), mats["LEAF"], T, new Vector3(p.x, h + 0.25f + k * 0.55f, p.y), Quaternion.identity, true, "Crown");
                colliders.Add(Collider2.Circle(p.x, p.y, r + 0.2f));
                trees.Add(new Vector4(p.x, p.y, h, 0.62f));
            }
            // rocks
            for (int i = 0; i < 12; i++)
            {
                float s = 0.25f + Rnd() * 0.5f; var p = Place(s, 0.8f);
                var g = MeshGen.Obj(MeshGen.Sphere(s, 9, 6), mats["ROCK"], T, new Vector3(p.x, s * 0.35f, p.y), Quaternion.Euler(0, Rnd() * 360, 0), true, "Rock");
                g.transform.localScale = new Vector3(1 + Rnd() * 0.5f, 0.55f + Rnd() * 0.3f, 1 + Rnd() * 0.4f);
                colliders.Add(Collider2.Circle(p.x, p.y, s * 1.05f));
            }
            // armour piles: a breastplate marks the spot, the limbs that the pile contains lie around it
            int[][] missing = { new int[0], new int[0], new[] { 1 }, new[] { 2 }, new[] { 3 }, new[] { 4 }, new[] { 0 }, new[] { 1, 4 }, new[] { 0, 2 }, new[] { 3, 4, 1 } };
            for (int i = 0; i < 10; i++)
            {
                var p = Place(0.6f, 1.2f);
                var pile = new ArmourPile { pos = new Vector3(p.x, 0, p.y), root = new GameObject("ArmourPile") };
                var P = pile.root.transform; P.SetParent(T, false); P.localPosition = pile.pos; P.localRotation = Quaternion.Euler(0, Rnd() * 360, 0);
                var chest = MeshGen.Obj(MeshGen.Sphere(0.2f, 10, 6, Mathf.PI / 2), mats["PLATE"], P, new Vector3(0, 0.02f, 0), Quaternion.identity); chest.transform.localScale = new Vector3(1, 0.8f, 1.3f);
                MeshGen.Obj(MeshGen.Box(0.34f, 0.04f, 0.05f), mats["LEATHER2"], P, new Vector3(0, 0.12f, 0.2f), Quaternion.identity);
                for (int l = 0; l < 5; l++)
                {
                    if (System.Array.IndexOf(missing[i], l) >= 0) continue;
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
                piles.Add(pile);
            }
            // grass
            var pts = new List<Vector3>(); var seeds = new List<float>(); int N = Mathf.RoundToInt(HX * 2 * HZ * 2 * 120);      // dense grass (the settings can thin it out)
            for (int i = 0; i < N; i++)
            {
                float x = (Rnd() * 2 - 1) * (HX + 2), z = (Rnd() * 2 - 1) * (HZ + 2); bool blocked = LakeK(x, z) < 1.06f;
                foreach (var c in colliders) if (Mathf.Sqrt((x - c.x) * (x - c.x) + (z - c.z) * (z - c.z)) < c.r * 0.8f) { blocked = true; break; }
                if (blocked) continue;
                if (LakeK(x, z) < 1.25f && Rnd() < 0.5f) continue;      // the bank: sparser grass
                pts.Add(new Vector3(x, 0, z)); seeds.Add(Rnd());
            }
            GlyphLayer.MakeGrass(pts, seeds, T);
            bounds = Rect.MinMaxRect(-HX + 0.3f, -HZ + 0.3f, HX - 0.3f, HZ - 0.3f);
        }
    }
}
