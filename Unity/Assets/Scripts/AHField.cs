// The field (biome reference 2.6, meadows and farmland): tall grass waving in the wind, a fenced plot of golden
// wheat with a scarecrow, poppies, cornflowers and daisies, a lone spreading oak, haystacks, a dirt road with deep
// cart ruts from the inn to the forest, a windmill and a farmhouse on the horizon; at night wind waves over the grass,
// fireflies over the ears, warm light in the windows of the distant farm. The two training dummies stay in the middle.
// 2 x 2 sectors of 10 x 10 m, local = world coordinates (the field is at the origin), x, z in -10..10.
// East (+x, the left of the screen): the inn «Пьяный дракон» - its door leads into the tavern. West (-x): the forest edge.
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public class Field : Location
    {
        public const float HX = 10, HZ = 10;
        public readonly List<Vector4> trees = new List<Vector4>();
        public static readonly Vector3[] DUMMY_SPOTS = { new Vector3(1.75f, 0, -1.2f), new Vector3(-1.75f, 0, -1.2f) };   // [0] only takes hits, [1] hits back
        /// the road (local): from the inn door in the east to the forest in the west
        public static readonly Vector2[] ROAD = { new Vector2(10.8f, 0.0f), new Vector2(7.0f, 0.8f), new Vector2(3.5f, 1.9f), new Vector2(-1.0f, 2.3f), new Vector2(-5.5f, 1.6f), new Vector2(-10.8f, 2.0f) };
        /// the inn's door (local): walking into it leads into the tavern
        public static readonly Vector3 INN = new Vector3(13.6f, 0, 0); public const float DOOR_X = 10.9f;
        /// the wheat plot
        public static readonly Rect WHEAT = Rect.MinMaxRect(2.0f, -8.6f, 8.6f, -3.4f);
        System.Random R; float Rnd() { return (float)R.NextDouble(); } float Rr(float a, float b) { return a + (b - a) * Rnd(); }

        public static Field Build(Mats mats, int seed) { var F = new Field(); F.R = new System.Random(seed); F.Construct(mats); return F; }
        public static Field Build(Mats mats) { return Build(mats, 20261112); }

        public static float RoadD(float x, float z)
        {
            float best = 99; var q = new Vector2(x, z);
            for (int i = 0; i + 1 < ROAD.Length; i++)
            {
                Vector2 a = ROAD[i], ab = ROAD[i + 1] - a; float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, (q - (a + ab * t)).magnitude);
            }
            return best;
        }

        void Construct(Mats mats)
        {
            id = "field";
            root = new GameObject("Field"); var T = root.transform;
            ambSky = Mats.Hex(0x8fa4d8) * 0.55f; ambEq = Mats.Hex(0x4a5a58) * 0.55f; ambGround = Mats.Hex(0x1a2414) * 0.55f;
            moonK = 1f; heroLightK = 1.6f; exposure = 1.3f;
            // ---- ground: tiles from the forest edge (x = -10) east past the inn; the road with its two ruts on top ----
            {
                const float TS = 3.96f; const int SUB = 12; const float Q = TS / SUB;
                for (int ix = 0; ix < 7; ix++)
                    for (int iz = 0; iz < 7; iz++)
                    {
                        float x0 = -HX + ix * TS, z0 = -13.86f + iz * TS;
                        var vs = new List<Vector3>(); var ts = new List<int>();
                        for (int a = 0; a < SUB; a++)
                            for (int b = 0; b < SUB; b++)
                            {
                                float qx = x0 + a * Q, qz = z0 + b * Q;
                                if (qz >= HZ - 0.001f && qx < HX - 0.001f) continue;               // (south of z = 10 the bright city's paving)
                                int o = vs.Count;
                                vs.Add(new Vector3(qx, 0, qz)); vs.Add(new Vector3(qx, 0, qz + Q)); vs.Add(new Vector3(qx + Q, 0, qz + Q)); vs.Add(new Vector3(qx + Q, 0, qz));
                                ts.Add(o); ts.Add(o + 1); ts.Add(o + 2); ts.Add(o); ts.Add(o + 2); ts.Add(o + 3);
                            }
                        if (vs.Count == 0) continue;
                        var gm = new Mesh(); gm.SetVertices(vs); gm.uv = new Vector2[vs.Count]; gm.SetTriangles(ts, 0);
                        gm.RecalculateNormals(); gm.RecalculateBounds(); MeshGen.AddGlyphUV(gm, false);
                        MeshGen.Obj(gm, mats["GROUND"], T, Vector3.zero, Quaternion.identity, false, "Ground");
                    }
                Forest.PathStrip(T, mats["ROAD"], ROAD, 1.35f, 0.006f, "Road");
                // the ruts: two darker lines in the road
                foreach (float off in new[] { -0.55f, 0.55f })
                {
                    var line = new Vector2[ROAD.Length];
                    for (int i = 0; i < ROAD.Length; i++)
                    {
                        Vector2 d = (i + 1 < ROAD.Length ? ROAD[i + 1] - ROAD[i] : ROAD[i] - ROAD[i - 1]).normalized;
                        line[i] = ROAD[i] + new Vector2(-d.y, d.x) * off;
                    }
                    Forest.PathStrip(T, mats["MUD"], line, 0.13f, 0.012f, "Rut");
                }
            }
            spawn = new Vector3(0, 0, 4.0f);
            foreach (var dp in DUMMY_SPOTS) colliders.Add(Collider2.Circle(dp.x, dp.z, 0.34f));
            bool Free(float x, float z, float r)
            {
                if ((new Vector2(x, z) - new Vector2(spawn.x, spawn.z)).magnitude < 1.5f + r) return false;
                if (RoadD(x, z) < 1.6f + r) return false;
                if (WHEAT.Contains(new Vector2(x, z))) return false;
                foreach (var c in colliders) if (c.circle && (new Vector2(x - c.x, z - c.z)).magnitude < c.r + r + 0.5f) return false;
                return true;
            }
            Vector2 Place(float r, float x0, float x1, float z0, float z1)
            {
                for (int k = 0; k < 200; k++) { float x = Rr(x0, x1), z = Rr(z0, z1); if (Free(x, z, r)) return new Vector2(x, z); }
                return new Vector2(float.NaN, 0);
            }
            System.Func<string, Material> seeMat = k => (k == "LEAF" || k == "BARK" || k == "BIRCH" || k == "DARK") ? NatureLib.SeeThrough(mats[k]) : null;
            // ---- the inn (the tavern from outside) east of the road's end; fences along the edge on either side of its door ----
            NatureLib.Place("inn", mats, T, INN, -90, 1f);
            colliders.Add(Collider2.Box(INN.x - 3.0f, INN.x + 3.5f, -3.4f, -1.0f)); colliders.Add(Collider2.Box(INN.x - 3.0f, INN.x + 3.5f, 1.0f, 3.4f));
            {   // the light over the door
                var lg = new GameObject("InnLamp"); lg.transform.SetParent(T, false); lg.transform.localPosition = new Vector3(DOOR_X + 0.3f, 2.3f, 0.6f);
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.7f, 0.4f); l.intensity = 1.2f; l.range = 4.5f; l.shadows = LightShadows.None;
            }
            for (float z = -HZ + 1; z < HZ; z += 2.06f)
            {
                if (Mathf.Abs(z) < 3.6f) continue;
                NatureLib.Place("fence", mats, T, new Vector3(HX + 0.4f, 0, z + 1.03f), 90, 1f);
                colliders.Add(Collider2.Box(HX + 0.3f, HX + 0.5f, z, z + 2.06f));
            }
            // ---- the wheat plot: tall golden ears (blades of the grass shader), a fence round it, a scarecrow ----
            {
                var pts = new List<Vector3>(); var seeds = new List<float>();
                for (int i = 0; i < WHEAT.width * WHEAT.height * 150; i++) { pts.Add(new Vector3(Rr(WHEAT.xMin, WHEAT.xMax), 0, Rr(WHEAT.yMin, WHEAT.yMax))); seeds.Add(Rnd()); }
                GlyphLayer.MakeGrass(pts, seeds, T, new Color(0.62f, 0.48f, 0.16f), new Color(0.92f, 0.76f, 0.36f), 2.7f);
                for (int i = 0; i < 26; i++) NatureLib.Place(Rnd() < 0.7f ? "wheat" : "wheatb", mats, T, new Vector3(Rr(WHEAT.xMin + 0.3f, WHEAT.xMax - 0.3f), 0, Rr(WHEAT.yMin + 0.3f, WHEAT.yMax - 0.3f)), Rr(0, 360), Rr(0.9f, 1.15f), null, false);
                // fence: the north, east and west sides (the road side is open)
                for (float x = WHEAT.xMin; x < WHEAT.xMax - 0.5f; x += 2.06f) NatureLib.Place("fence", mats, T, new Vector3(x + 1.03f, 0, WHEAT.yMin - 0.2f), 0, 1f);
                for (float z = WHEAT.yMin; z < WHEAT.yMax - 0.5f; z += 2.06f) { NatureLib.Place("fence", mats, T, new Vector3(WHEAT.xMin - 0.2f, 0, z + 1.03f), 90, 1f); NatureLib.Place("fence", mats, T, new Vector3(WHEAT.xMax + 0.2f, 0, z + 1.03f), 90, 1f); }
                colliders.Add(Collider2.Box(WHEAT.xMin - 0.3f, WHEAT.xMax + 0.3f, WHEAT.yMin - 0.3f, WHEAT.yMin - 0.1f));
                colliders.Add(Collider2.Box(WHEAT.xMin - 0.3f, WHEAT.xMin - 0.1f, WHEAT.yMin - 0.3f, WHEAT.yMax));
                colliders.Add(Collider2.Box(WHEAT.xMax + 0.1f, WHEAT.xMax + 0.3f, WHEAT.yMin - 0.3f, WHEAT.yMax));
                Scarecrow(T, mats, new Vector3(5.6f, 0, -6.2f));
                colliders.Add(Collider2.Circle(5.6f, -6.2f, 0.25f));
            }
            // ---- a lone spreading oak (and a smaller one), haystacks, a cart ----
            NatureLib.Place("oak1", mats, T, new Vector3(-5.4f, -0.02f, -5.2f), 30, 1.35f, seeMat); colliders.Add(Collider2.Circle(-5.4f, -5.2f, 0.45f)); occluders.Add(new Vector4(-5.4f, -5.2f, 2.6f, 6.0f)); trees.Add(new Vector4(-5.4f, -5.2f, 3f, 2.6f));
            NatureLib.Place("oak5", mats, T, new Vector3(6.4f, -0.02f, 6.6f), 200, 1.0f, seeMat); colliders.Add(Collider2.Circle(6.4f, 6.6f, 0.35f)); occluders.Add(new Vector4(6.4f, 6.6f, 2.0f, 4.5f)); trees.Add(new Vector4(6.4f, 6.6f, 2.5f, 2f));
            foreach (var hp in new[] { new Vector2(-2.6f, -7.0f), new Vector2(-4.2f, -8.2f), new Vector2(8.4f, 4.8f) })
            { NatureLib.Place("hay", mats, T, new Vector3(hp.x, 0, hp.y), Rr(0, 360), Rr(0.9f, 1.15f)); colliders.Add(Collider2.Circle(hp.x, hp.y, 0.6f)); }
            NatureLib.Place("cart", mats, T, new Vector3(8.0f, 0, -1.6f), 75, 1f); colliders.Add(Collider2.Circle(8.0f, -1.6f, 0.75f));
            // ---- the forest edge (west): a few young birches and bushes thinning out into the meadow ----
            for (int i = 0; i < 5; i++)
            {
                var p = Place(0.6f, -HX + 0.4f, -HX + 3.4f, -HZ + 1, HZ - 1); if (float.IsNaN(p.x)) continue;
                if (i < 2) { NatureLib.Place("birch" + (i == 0 ? "3" : "5"), mats, T, new Vector3(p.x, -0.02f, p.y), Rr(0, 360), Rr(0.75f, 0.9f), seeMat); colliders.Add(Collider2.Circle(p.x, p.y, 0.25f)); occluders.Add(new Vector4(p.x, p.y, 1.4f, 5.5f)); }
                else { NatureLib.Place(i == 4 ? "bushberry" : "bush1", mats, T, new Vector3(p.x, 0, p.y), Rr(0, 360), Rr(0.8f, 1.1f)); }
            }
            // ---- flowers in clumps in the southern meadow: poppies, cornflowers, daisies ----
            var POPPY = new Dictionary<string, string> { { "FLOWERB", "RED" }, { "WHEAT", "RED" } };
            var DAISY = new Dictionary<string, string> { { "FLOWERB", "FLOWERW" } };
            for (int c = 0; c < 9; c++)
            {
                var cp = Place(0.4f, -HX + 1, HX - 1, 3.6f, HZ - 0.5f); if (float.IsNaN(cp.x)) continue;
                var kind = c % 3 == 0 ? POPPY : c % 3 == 1 ? DAISY : null;
                for (int i = 0; i < 6; i++)
                {
                    var p = cp + new Vector2(Rr(-1.1f, 1.1f), Rr(-0.8f, 0.8f));
                    if (RoadD(p.x, p.y) < 1.5f) continue;
                    NatureLib.Place(Rnd() < 0.6f ? "flowers" : "flower3", mats, T, new Vector3(p.x, 0, p.y), Rr(0, 360), Rr(0.8f, 1.2f), null, false, kind);
                }
            }
            // ---- on the horizon: the windmill and a farmhouse with warm windows (north, beyond the edge) ----
            NatureLib.Place("mill", mats, T, new Vector3(3.5f, 0, -19.5f), 160, 1f);
            NatureLib.Place("house", mats, T, new Vector3(-7.0f, 0, -17.0f), 20, 1f);
            {
                var lg = new GameObject("FarmWindow"); lg.transform.SetParent(T, false); lg.transform.localPosition = new Vector3(-6.4f, 1.6f, -15.2f);
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.68f, 0.35f); l.intensity = 0.9f; l.range = 4f; l.shadows = LightShadows.None;
            }
            // ---- tall grass everywhere else (the wind sends waves over it) ----
            {
                var pts = new List<Vector3>(); var seeds = new List<float>(); var fpts = new List<Vector3>(); var fseeds = new List<float>();
                for (int i = 0; i < 2 * HX * 2 * HZ * 120; i++)
                {
                    float x = Rr(-HX, HX + 3.5f), z = Rr(-HZ - 3f, HZ);
                    if (!Forest.RoadGrass(x, z, RoadD(x, z), 1.35f) || WHEAT.Contains(new Vector2(x, z)) || x > INN.x - 3.2f && Mathf.Abs(z) < 3.6f) continue;
                    bool blocked = false; foreach (var c in colliders) if (c.circle && (new Vector2(x - c.x, z - c.z)).magnitude < c.r * 0.8f) { blocked = true; break; }
                    if (blocked) continue;
                    // the forest's edge: patches of its shorter, darker, sparser grass come out into the meadow
                    if (Rnd() < BiomeBlend.Other(x - BiomeBlend.FIELD_FOREST_X, x, z)) { if (Rnd() < 0.32f) { fpts.Add(new Vector3(x, 0, z)); fseeds.Add(Rnd()); } continue; }
                    pts.Add(new Vector3(x, 0, z)); seeds.Add(Rnd());
                }
                GlyphLayer.MakeGrass(pts, seeds, T, new Color(0.16f, 0.50f, 0.24f), new Color(0.50f, 0.78f, 0.36f), 1.35f);
                if (fpts.Count > 0) GlyphLayer.MakeGrass(fpts, fseeds, T, new Color(0.12f, 0.40f, 0.20f), new Color(0.36f, 0.62f, 0.28f), 1.15f);
                // and its ferns and young bushes
                for (int i = 0; i < 90; i++)
                {
                    float x = Rr(-HX, -HX + 7f), z = Rr(-HZ + 0.5f, HZ - 0.5f);
                    if (RoadD(x, z) < 1.3f || Rnd() > BiomeBlend.Other(x - BiomeBlend.FIELD_FOREST_X, x, z) * 0.9f) continue;
                    NatureLib.Place(Rnd() < 0.8f ? (Rnd() < 0.5f ? "fern1" : "fern3") : "bush2", mats, T, new Vector3(x, 0, z), Rr(0, 360), Rr(0.7f, 1.1f), null, false);
                }
            }
            // ---- fireflies over the ears and the meadow ----
            {
                var pts = new List<Vector3>(); var sd = new List<Vector2>();
                for (int i = 0; i < 60; i++)
                {
                    bool wheat = i < 36;
                    pts.Add(wheat ? new Vector3(Rr(WHEAT.xMin, WHEAT.xMax), Rr(0.8f, 1.7f), Rr(WHEAT.yMin, WHEAT.yMax)) : new Vector3(Rr(-HX + 1, HX - 1), Rr(0.4f, 1.4f), Rr(-HZ + 1, HZ - 1)));
                    sd.Add(new Vector2(0, Rnd()));
                }
                GlyphLayer.MakeMotes(T, Vector3.zero, Quaternion.identity, Vector3.one, pts, sd, 1, new Color(0.85f, 1f, 0.45f) * 1.3f, new Vector4(0.045f, 0, 0, 0.55f));
            }
            // the walkable ground: the field, plus the strip shared with the forest (west) and the inn's porch (east)
            bounds = Rect.MinMaxRect(-HX - 3.0f, -HZ + 0.4f, DOOR_X + 0.15f, HZ + 2.6f);      // (open south: the bright city)
        }
        /// a scarecrow: a post and a cross-bar, a linen shirt, a sack head and a straw hat
        static void Scarecrow(Transform T, Mats mats, Vector3 p)
        {
            var s = new GameObject("Scarecrow").transform; s.SetParent(T, false); s.localPosition = p; s.localRotation = Quaternion.Euler(0, 160, 0);
            MeshGen.Obj(MeshGen.Cylinder(0.05f, 0.045f, 2.1f, 7), mats["WOOD"], s, new Vector3(0, 1.05f, 0), Quaternion.identity, true, "Post");
            MeshGen.Obj(MeshGen.Cylinder(0.035f, 0.035f, 1.5f, 6), mats["WOOD"], s, new Vector3(0, 1.55f, 0), Quaternion.Euler(0, 0, 90), true, "Bar");
            MeshGen.Obj(MeshGen.Box(0.5f, 0.6f, 0.2f), mats["LINEN"], s, new Vector3(0, 1.35f, 0), Quaternion.Euler(0, 0, 4), true, "Shirt");
            MeshGen.Obj(MeshGen.Box(1.2f, 0.16f, 0.16f), mats["LINEN"], s, new Vector3(0, 1.56f, 0), Quaternion.identity, true, "Sleeves");
            MeshGen.Obj(MeshGen.Sphere(0.16f, 9, 7), mats["HAY"], s, new Vector3(0, 1.85f, 0), Quaternion.identity, true, "Head");
            MeshGen.Obj(MeshGen.Cylinder(0.3f, 0.3f, 0.03f, 12), mats["HAY"], s, new Vector3(0, 1.98f, 0), Quaternion.Euler(6, 0, 0), true, "Brim");
            MeshGen.Obj(MeshGen.Cylinder(0.12f, 0.06f, 0.18f, 10), mats["HAY"], s, new Vector3(0, 2.07f, 0), Quaternion.Euler(6, 0, 0), true, "Crown");
        }
        public override void Cutaway(Vector3 camOff) { OccluderSeeThrough(camOff); }
    }
}
