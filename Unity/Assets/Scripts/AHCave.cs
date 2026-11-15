// Cave: the third biome mock-up, 2 x 2 sectors of 10 x 8 m (20 x 16 m in all), divided by rock walls with passages.
// Seen from the camera (it looks along -z: screen right is -x, screen down is +z):
//   bottom left  (x > 0, z > 0)  the entrance hall: light from a crevice in the side wall, old torches, pickaxes,
//                                boulders, a smouldering torch; the hero starts here
//   top left     (x > 0, z < 0)  the grotto: stalagmites from the floor, stalactites from the vault, sharp rocks, moss,
//                                a shaft of light through a break in the vault, a dripping puddle
//   top right    (x < 0, z < 0)  the wet hall: a stream, deep and shallow pools, sticky mud, drops falling into the
//                                pools, fireflies, moss, ordinary and glowing mushrooms
//   bottom right (x < 0, z > 0)  the bone chamber: skeletons walled into the rock and lying on the floor, scattered
//                                bones, clusters of glowing mushrooms (a dim, slow pulse), fireflies; the stream leaves here
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public class Cave : Location
    {
        const float HX = 10, HZ = 8, WATER_Y = 0.04f, MUD_Y = 0.015f;
        System.Random R; float Rnd() { return (float)R.NextDouble(); }
        float Rr(float a, float b) { return a + (b - a) * Rnd(); }

        // ---- water and mud ----
        struct Pool { public Vector2 c; public float rx, rz, ang, depth; }
        readonly List<Pool> pools = new List<Pool>(), muds = new List<Pool>();
        static readonly Vector2[] STREAM = { new Vector2(-6.6f, -8.6f), new Vector2(-5.6f, -5.4f), new Vector2(-3.6f, -2.6f), new Vector2(-3f, 0f),
                                             new Vector2(-3.5f, 2.6f), new Vector2(-6f, 5.2f), new Vector2(-8.6f, 8.6f) };
        const float STREAM_HW = 0.32f;
        static float PoolK(Pool p, float x, float z)
        {
            float dx = x - p.c.x, dz = z - p.c.y, cs = Mathf.Cos(p.ang), sn = Mathf.Sin(p.ang);
            float u = (dx * cs + dz * sn) / p.rx, v = (-dx * sn + dz * cs) / p.rz;
            float a = Mathf.Atan2(v, u), wob = 1 + 0.08f * Mathf.Sin(a * 3 + p.c.x) + 0.05f * Mathf.Sin(a * 5 + p.c.y);
            return Mathf.Sqrt(u * u + v * v) / wob;
        }
        static float StreamK(float x, float z)
        {
            float best = 1e9f; var q = new Vector2(x, z);
            for (int i = 0; i + 1 < STREAM.Length; i++)
            {
                Vector2 a = STREAM[i], b = STREAM[i + 1], ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                float w = STREAM_HW * (1 + 0.25f * Mathf.Sin((a.x + t * ab.x) * 2.3f + (a.y + t * ab.y) * 1.7f));
                best = Mathf.Min(best, (q - (a + ab * t)).magnitude / w);
            }
            return best;
        }
        /// 0 in the middle of some water .. 1 at its edge, more outside
        public float WaterK(float x, float z)
        {
            float k = StreamK(x, z);
            foreach (var p in pools) k = Mathf.Min(k, PoolK(p, x, z));
            return k;
        }
        float MudK(float x, float z) { float k = 9; foreach (var p in muds) k = Mathf.Min(k, PoolK(p, x, z)); return k; }
        float DeepAt(float x, float z) { float d = 0.06f; foreach (var p in pools) if (PoolK(p, x, z) < 1) d = Mathf.Max(d, p.depth); return d; }
        public override bool HasWater => true;
        public override bool InWater(float x, float z) { return WaterK(x, z) < 0.97f; }
        public override float WaterSink(float x, float z)
        {
            float w = WaterK(x, z), m = MudK(x, z);
            float s = w < 1 ? DeepAt(x, z) * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1f, 0.5f, w)) : 0;
            if (m < 1) s = Mathf.Max(s, 0.08f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1f, 0.6f, m)));
            return s;
        }
        public override float SpeedK(float x, float z)
        {
            float k = 1;
            if (MudK(x, z) < 0.95f) k = 0.5f;                                   // sticky mud: a slow wade, no running
            else if (WaterK(x, z) < 0.9f) k = DeepAt(x, z) > 0.12f ? 0.7f : 0.9f;
            return k;
        }

        // ---- walls: continuous rock faces (a scanned cliff, straightened); a wall between the camera and the hero
        // does not sink: it starts to show through around him (dithered, AsciiHero/Lit + Glyph, _SeeThrough) ----
        struct WallSeg { public Vector2 a, b; public float h; }
        readonly List<WallSeg> wallSegs = new List<WallSeg>();
        float seeFade;
        static Vector3[] RF_V; static int[] RF_T;
        // ---- light that lives ----
        class Glow { public Light l; public float baseI, ph; public List<Renderer> caps = new List<Renderer>(); }
        readonly List<Glow> glows = new List<Glow>(); Light ember; MaterialPropertyBlock mpb; Color glowEm;
        // ---- drops falling into the pools ----
        public readonly List<Vector3> drips = new List<Vector3>();      // x, z, phase
        public const float DRIP_PERIOD = 2.3f, DRIP_FALL = 0.55f, DRIP_TOP = 2.9f;
        float lastT = -1;

        public static Cave Build(Mats mats) { var C = new Cave(); C.Construct(mats); return C; }

        void Construct(Mats mats)
        {
            root = new GameObject("Cave"); var T = root.transform; R = new System.Random(20261115);
            spawn = new Vector3(6, 0, 5.4f);
            bounds = Rect.MinMaxRect(-HX + 0.6f, -HZ + 0.6f, HX - 0.6f, HZ - 0.6f);
            glowEm = Mats.Hex(0x3fd8b8);

            // water: a deep pool and shallow ones in the wet hall, a puddle under the stalactites, one in the bone chamber
            pools.Add(new Pool { c = new Vector2(-6.4f, -3.0f), rx = 1.7f, rz = 1.15f, ang = 0.3f, depth = 0.22f });
            pools.Add(new Pool { c = new Vector2(-1.9f, -5.7f), rx = 0.85f, rz = 0.6f, ang = -0.4f, depth = 0.06f });
            pools.Add(new Pool { c = new Vector2(-8.3f, -6.2f), rx = 0.7f, rz = 0.55f, ang = 0.8f, depth = 0.06f });
            pools.Add(new Pool { c = new Vector2(5.6f, -6.3f), rx = 0.75f, rz = 0.5f, ang = 0.1f, depth = 0.06f });
            pools.Add(new Pool { c = new Vector2(-1.9f, 6.3f), rx = 0.8f, rz = 0.55f, ang = 0.6f, depth = 0.06f });
            muds.Add(new Pool { c = new Vector2(-8.2f, -1.6f), rx = 1.5f, rz = 0.95f, ang = 0.2f });
            muds.Add(new Pool { c = new Vector2(-1.8f, 3.4f), rx = 1.2f, rz = 0.75f, ang = -0.3f });

            // ---- floor: tiles of small squares, none under water or mud ----
            {
                const float TS = 3.96f; const int SUB = 12; const float Q = TS / SUB;
                for (int ix = 0; ix < 6; ix++)
                    for (int iz = 0; iz < 5; iz++)
                    {
                        float x0 = -HX - 1.88f + ix * TS, z0 = -HZ - 1.9f + iz * TS;
                        var vs = new List<Vector3>(); var uvs = new List<Vector2>(); var ts = new List<int>();
                        for (int a = 0; a < SUB; a++)
                            for (int b = 0; b < SUB; b++)
                            {
                                float qx = x0 + a * Q, qz = z0 + b * Q, cx = qx + Q / 2, cz = qz + Q / 2;
                                if (Mathf.Max(Mathf.Max(WaterK(qx, qz), WaterK(qx + Q, qz)), Mathf.Max(WaterK(qx, qz + Q), WaterK(qx + Q, qz + Q))) < 0.98f) continue;
                                if (Mathf.Max(Mathf.Max(MudK(qx, qz), MudK(qx + Q, qz)), Mathf.Max(MudK(qx, qz + Q), MudK(qx + Q, qz + Q))) < 0.98f) continue;
                                int o = vs.Count;
                                vs.Add(new Vector3(qx, 0, qz)); vs.Add(new Vector3(qx, 0, qz + Q)); vs.Add(new Vector3(qx + Q, 0, qz + Q)); vs.Add(new Vector3(qx + Q, 0, qz));
                                uvs.Add(new Vector2(a / (float)SUB, b / (float)SUB)); uvs.Add(new Vector2(a / (float)SUB, (b + 1) / (float)SUB));
                                uvs.Add(new Vector2((a + 1) / (float)SUB, (b + 1) / (float)SUB)); uvs.Add(new Vector2((a + 1) / (float)SUB, b / (float)SUB));
                                ts.Add(o); ts.Add(o + 1); ts.Add(o + 2); ts.Add(o); ts.Add(o + 2); ts.Add(o + 3);
                            }
                        if (vs.Count == 0) continue;
                        var gm = new Mesh(); gm.SetVertices(vs); gm.SetUVs(0, uvs); gm.SetTriangles(ts, 0);
                        gm.RecalculateNormals(); gm.RecalculateBounds(); MeshGen.AddGlyphUV(gm, false);
                        MeshGen.Obj(gm, mats["CAVEFLOOR"], T, Vector3.zero, Quaternion.identity, false, "Ground");
                    }
            }
            // ---- mud: a dark sticky surface just above the floor ----
            foreach (var m in muds) Patch(T, mats["MUD"], (x, z) => PoolK(m, x, z), m.c, Mathf.Max(m.rx, m.rz) + 0.4f, MUD_Y, "Mud");

            // ---- water: the stream (each stretch flows its own way), the pools ----
            for (int i = 0; i + 1 < STREAM.Length; i++)
            {
                Vector2 a = STREAM[i], b = STREAM[i + 1]; var flow = (b - a).normalized * 0.55f; int ii = i;
                System.Func<float, float, float> k = (x, z) =>
                {   // this stretch only (the joins overlap a little)
                    var q = new Vector2(x, z); Vector2 ab = b - a; float t = Vector2.Dot(q - a, ab) / ab.sqrMagnitude;
                    if (t < -0.08f || t > 1.08f) return 9;
                    return StreamK(x, z);
                };
                Water(T, mats, k, (a + b) / 2, (b - a).magnitude / 2 + 0.6f, new Vector4(1, flow.x, flow.y, 0.2f));
            }
            foreach (var p in pools) { var pp = p; Water(T, mats, (x, z) => PoolK(pp, x, z), p.c, Mathf.Max(p.rx, p.rz) + 0.3f, new Vector4(1, 0, 0, p.depth > 0.1f ? 1 : 0)); }

            // ---- walls: the outline, and the two dividing walls with passages ----
            var rock = new Material(mats["WETROCK"]) { name = "CaveWall" }; rock.SetFloat("_SeeThrough", 1); rock.SetFloat("_Cull", 0);
            RockWall(T, mats, rock, new Vector2(-HX - 0.6f, -HZ - 0.15f), new Vector2(HX + 0.6f, -HZ - 0.15f), Vector2.up, 2.6f, 3.3f);       // far wall
            RockWall(T, mats, rock, new Vector2(-HX - 0.6f, HZ + 0.35f), new Vector2(HX + 0.6f, HZ + 0.35f), Vector2.up, 1.7f, 2.3f);          // near wall (faces the camera)
            RockWall(T, mats, rock, new Vector2(-HX - 0.15f, -HZ - 0.3f), new Vector2(-HX - 0.15f, HZ + 0.3f), Vector2.right, 2.4f, 3.1f);
            RockWall(T, mats, rock, new Vector2(HX + 0.15f, -HZ - 0.3f), new Vector2(HX + 0.15f, 3.9f), Vector2.left, 2.4f, 3.1f);
            RockWall(T, mats, rock, new Vector2(HX + 0.15f, 5.2f), new Vector2(HX + 0.15f, HZ + 0.3f), Vector2.left, 2.4f, 3.1f);         // (the crevice at z 4.5: the entrance light)
            foreach (var r in new[] { new Vector2(-HZ, -5.6f), new Vector2(-2.4f, 2.4f), new Vector2(5.6f, HZ) })
            {   // the wall across the middle (x 0), seen from both sides
                RockWall(T, mats, rock, new Vector2(0.25f, r.x), new Vector2(0.25f, r.y), Vector2.right, 2.0f, 2.6f);
                RockWall(T, mats, rock, new Vector2(-0.25f, r.x), new Vector2(-0.25f, r.y), Vector2.left, 2.0f, 2.6f, false);
            }
            foreach (var r in new[] { new Vector2(-HX, -4.6f), new Vector2(-1.4f, 3.4f), new Vector2(6.6f, HX) })
                RockWall(T, mats, rock, new Vector2(r.x, 0.1f), new Vector2(r.y, 0.1f), Vector2.up, 2.0f, 2.6f);

            // ---- the grotto: stalagmites, stalactites, sharp rocks, moss ----
            for (int i = 0; i < 16; i++)
            {
                var p = Place(0.6f, 9.3f, -7.4f, -0.8f, 0.35f); float h = Rr(0.5f, 1.6f), r = Rr(0.09f, 0.2f);
                MeshGen.Obj(MeshGen.Cylinder(r, 0.015f, h, 8), mats["WETROCK"], T, new Vector3(p.x, h / 2, p.y), Quaternion.Euler(Rr(-5, 5), Rr(0, 360), Rr(-5, 5)), true, "Stalagmite");
                colliders.Add(Collider2.Circle(p.x, p.y, r + 0.08f));
                if (Rnd() < 0.5f) Moss(T, mats, p.x + Rr(-0.2f, 0.2f), p.y + Rr(-0.2f, 0.2f), r * 2.2f, 0.01f);
            }
            for (int i = 0; i < 22; i++)
            {   // hanging from the vault, 3 m up: mostly over the grotto, a few along the walls elsewhere
                bool grotto = i < 14; float x = grotto ? Rr(0.8f, 9.3f) : Rr(-9.3f, 9.3f), z = grotto ? Rr(-7.5f, -0.8f) : (Rnd() < 0.5f ? Rr(-7.6f, -6.6f) : Rr(6.6f, 7.4f));
                float h = Rr(0.35f, 1.2f), r = Rr(0.06f, 0.15f);
                MeshGen.Obj(MeshGen.Cylinder(0.012f, r, h, 7), mats["WETROCK"], T, new Vector3(x, 3.0f - h / 2, z), Quaternion.Euler(0, Rr(0, 360), 0), false, "Stalactite");
            }
            for (int i = 0; i < 14; i++)
            {
                var p = Place(0.6f, 9.3f, -7.4f, 7.4f, 0.2f); float h = Rr(0.15f, 0.45f), r = Rr(0.08f, 0.18f);
                MeshGen.Obj(MeshGen.Cylinder(r, 0.01f, h, 4), mats["ROCK"], T, new Vector3(p.x, h * 0.4f, p.y), Quaternion.Euler(Rr(-25, 25), Rr(0, 360), Rr(-25, 25)), true, "SharpRock");
                colliders.Add(Collider2.Circle(p.x, p.y, r * 0.8f));
            }
            // ---- boulders, everywhere but the water ----
            for (int i = 0; i < 12; i++)
            {
                var p = Place(-9.2f, 9.2f, -7.3f, 7.3f, 0.6f); float s = Rr(0.3f, 0.7f);
                var g = MeshGen.Obj(MeshGen.Sphere(s, 9, 6), mats[Rnd() < 0.5f ? "ROCK" : "WETROCK"], T, new Vector3(p.x, s * 0.35f, p.y), Quaternion.Euler(0, Rr(0, 360), 0), true, "Boulder");
                g.transform.localScale = new Vector3(Rr(1, 1.4f), Rr(0.55f, 0.85f), Rr(1, 1.3f));
                colliders.Add(Collider2.Circle(p.x, p.y, s * 1.05f));
                if (Rnd() < 0.6f) Moss(T, mats, p.x, p.y, s * 0.9f, s * 0.6f);          // moss on its top
            }
            // moss along the foot of the walls in the wet hall and the grotto
            for (int i = 0; i < 26; i++)
            {
                float x = Rnd() < 0.5f ? Rr(-9.4f, -0.6f) : Rr(0.6f, 9.4f), z = Rr(-7.6f, -0.5f);
                if (Mathf.Abs(Mathf.Abs(x) - 9.4f) > 1.0f && Mathf.Abs(z + 7.6f) > 1.0f && Mathf.Abs(z + 0.5f) > 0.8f && Mathf.Abs(x) > 1.2f) continue;      // only near a wall
                if (WaterK(x, z) < 1.2f) continue;
                Moss(T, mats, x, z, Rr(0.25f, 0.6f), 0.01f);
            }

            // ---- mushrooms: ordinary ones in the wet hall and the entrance, glowing clusters in the bone chamber and beyond ----
            for (int c = 0; c < 9; c++)
            {
                var p = c < 5 ? Place(-9.2f, -1.0f, -7.3f, -0.7f, 0.3f) : Place(1.0f, 9.2f, 0.8f, 7.2f, 0.3f);
                int n = 3 + R.Next(5);
                for (int i = 0; i < n; i++) Mushroom(T, mats, p.x + Rr(-0.25f, 0.25f), p.y + Rr(-0.25f, 0.25f), Rr(0.7f, 1.3f), false, null);
            }
            Vector2[] glowAt = { new Vector2(-8.6f, 1.5f), new Vector2(-1.4f, 7.1f), new Vector2(-8.8f, -5.0f), new Vector2(8.6f, -1.6f) };
            foreach (var ga in glowAt)
            {
                var gl = new Glow { ph = Rr(0, 6.28f), baseI = 0.55f };
                int n = 5 + R.Next(4);
                for (int i = 0; i < n; i++) Mushroom(T, mats, ga.x + Rr(-0.35f, 0.35f), ga.y + Rr(-0.3f, 0.3f), Rr(0.6f, 1.4f), true, gl.caps);
                var lg = new GameObject("GlowLight"); lg.transform.SetParent(T, false); lg.transform.localPosition = new Vector3(ga.x, 0.45f, ga.y);
                gl.l = lg.AddComponent<Light>(); gl.l.type = LightType.Point; gl.l.color = new Color(0.35f, 1f, 0.85f); gl.l.range = 2.6f; gl.l.intensity = gl.baseI;
                glows.Add(gl);
            }

            // ---- bones: skeletons in the walls of the bone chamber, on its floor, bones everywhere there ----
            Skeleton(T, mats, new Vector3(-9.35f, 1.1f, 2.4f), Quaternion.Euler(-90, -90, 0) * Quaternion.Euler(0, 0, 8), 1.0f);      // walled into the east wall, upright, half sunk
            Skeleton(T, mats, new Vector3(-9.4f, 0.9f, 6.0f), Quaternion.Euler(-80, -90, 0) * Quaternion.Euler(0, 0, -12), 0.95f);
            Skeleton(T, mats, new Vector3(-0.55f, 1.0f, 6.6f), Quaternion.Euler(-90, 90, 0) * Quaternion.Euler(0, 0, 5), 1.0f);       // in the dividing wall
            Skeleton(T, mats, new Vector3(-6.2f, 0.05f, 6.6f), Quaternion.Euler(0, 35, 0), 1.0f);                                       // lying on the floor
            Skeleton(T, mats, new Vector3(-4.0f, 0.05f, 1.4f), Quaternion.Euler(0, 160, 0), 0.95f);
            Skeleton(T, mats, new Vector3(7.8f, 0.05f, 1.8f), Quaternion.Euler(0, -70, 0), 1.0f);                                       // a miner in the entrance hall
            for (int i = 0; i < 26; i++)
            {
                var p = i < 18 ? Place(-9.2f, -0.8f, 0.8f, 7.4f, 0.05f) : Place(0.8f, 9.2f, -7.4f, 7.4f, 0.05f);
                Bone(T, mats, p.x, p.y, Rr(0.12f, 0.36f));
            }
            for (int i = 0; i < 3; i++) { var p = Place(-9f, -1f, 1f, 7.2f, 0.1f); MeshGen.Obj(MeshGen.Sphere(0.1f, 9, 7), mats["BONE"], T, new Vector3(p.x, 0.08f, p.y), Quaternion.Euler(Rr(-30, 30), Rr(0, 360), Rr(-60, 60)), true, "Skull"); }

            // ---- the miners' leftovers in the entrance hall: torches, pickaxes, one torch still smouldering ----
            for (int i = 0; i < 5; i++) { var p = Place(1.0f, 9.0f, 0.8f, 7.2f, 0.15f); Torch(T, mats, p.x, p.y, Rnd() < 0.5f); }
            for (int i = 0; i < 3; i++) { var p = Place(1.0f, 9.0f, 0.8f, 7.2f, 0.2f); Pickaxe(T, mats, p.x, p.y); }
            {
                var holder = new GameObject("WallTorch").transform; holder.SetParent(T, false); holder.localPosition = new Vector3(9.25f, 1.25f, 1.6f);
                MeshGen.Obj(MeshGen.Cylinder(0.025f, 0.02f, 0.5f, 7), mats["WOOD"], holder, Vector3.zero, Quaternion.Euler(0, 0, 25), true, "TorchStick");
                MeshGen.Obj(MeshGen.Cylinder(0.04f, 0.035f, 0.1f, 7), mats["DARK"], holder, new Vector3(-0.1f, 0.24f, 0), Quaternion.Euler(0, 0, 25), true, "TorchHead");
                GlyphLayer.MakeFire(holder, new Vector3(-0.12f, 0.29f, 0), 0.03f, 0.03f, 0.13f, 0.04f, 14, 0.6f);
                var lg = new GameObject("Ember"); lg.transform.SetParent(holder, false); lg.transform.localPosition = new Vector3(-0.3f, 0.4f, 0);
                ember = lg.AddComponent<Light>(); ember.type = LightType.Point; ember.color = new Color(1f, 0.55f, 0.25f); ember.intensity = 0.7f; ember.range = 2.8f;
            }

            // ---- light: a shaft through a break in the vault over the grotto, a beam through the crevice of the entrance ----
            Beam(T, new Vector3(5.2f, 0, -4.0f), new Vector3(5.6f, 7.5f, -4.6f), 0.85f, 230, new Color(1f, 0.93f, 0.78f), 5.5f, 30f);
            Beam(T, new Vector3(6.4f, 0, 4.1f), new Vector3(11.5f, 1.9f, 4.6f), 0.5f, 150, new Color(0.85f, 0.9f, 1f), 4.5f, 24f);

            // ---- fireflies over the wet hall and the bone chamber ----
            {
                var pts = new List<Vector3>(); var sd = new List<Vector2>();
                for (int i = 0; i < 60; i++)
                {
                    bool wet = i < 38; pts.Add(new Vector3(wet ? Rr(-9f, -1f) : Rr(-9f, -1f), Rr(0.3f, 1.7f), wet ? Rr(-7.2f, -0.8f) : Rr(0.8f, 7.2f)));
                    sd.Add(new Vector2(0, Rnd()));
                }
                GlyphLayer.MakeMotes(T, Vector3.zero, Quaternion.identity, Vector3.one, pts, sd, 1, new Color(0.8f, 1f, 0.4f) * 1.3f, new Vector4(0.045f, 0, 0, 0.45f));
            }
            // ---- drops from the vault: into the pools and the stream, now and then ----
            {
                var pts = new List<Vector3>(); var sd = new List<Vector2>();
                foreach (var p in pools)
                    for (int i = 0; i < (p.depth > 0.1f ? 4 : 2); i++)
                    {
                        float a = Rr(0, 6.28f), rr = Rr(0, 0.7f); var q = p.c + new Vector2(Mathf.Cos(a) * p.rx * rr, Mathf.Sin(a) * p.rz * rr); float ph = Rnd();
                        pts.Add(new Vector3(q.x, WATER_Y, q.y)); sd.Add(new Vector2(ph, Rnd())); drips.Add(new Vector3(q.x, q.y, ph));
                    }
                for (int i = 0; i < 3; i++) { var a = STREAM[1 + i * 2]; float ph = Rnd(); pts.Add(new Vector3(a.x, WATER_Y, a.y)); sd.Add(new Vector2(ph, Rnd())); drips.Add(new Vector3(a.x, a.y, ph)); }
                GlyphLayer.MakeMotes(T, Vector3.zero, Quaternion.identity, Vector3.one, pts, sd, 2, new Color(0.65f, 0.8f, 0.9f), new Vector4(0.05f, DRIP_TOP, DRIP_PERIOD, 0));
            }
        }

        // a free spot for something of radius r inside the box, away from water, walls and what is already there
        Vector2 Place(float x0, float x1, float z0, float z1, float r)
        {
            for (int k = 0; k < 120; k++)
            {
                float x = Rr(x0, x1), z = Rr(z0, z1); var q = new Vector2(x, z);
                if (WaterK(x, z) < 1.25f || MudK(x, z) < 1.1f) continue;
                if ((q - new Vector2(spawn.x, spawn.z)).magnitude < 1.2f) continue;
                bool ok = true;
                foreach (var c in colliders) if (c.circle && (q - new Vector2(c.x, c.z)).magnitude < c.r + r + 0.15f) { ok = false; break; }
                if (ok) return q;
            }
            return new Vector2(Rr(x0, x1), Rr(z0, z1));
        }
        /// one piece of the rock face, the size baked into the vertices (the characters are laid out on the mesh as it
        /// is): len wide, h high, its relief d deep; local +z is the face
        static Mesh RockPiece(float len, float h, float d, bool flip)
        {
            if (RF_V == null)
            {
                var b = Resources.Load<TextAsset>("rockface").bytes;
                int nv = System.BitConverter.ToInt32(b, 4), nt = System.BitConverter.ToInt32(b, 8), o = 12;
                RF_V = new Vector3[nv]; RF_T = new int[nt * 3];
                for (int i = 0; i < nv; i++, o += 12) RF_V[i] = new Vector3(System.BitConverter.ToSingle(b, o), System.BitConverter.ToSingle(b, o + 4), System.BitConverter.ToSingle(b, o + 8));
                for (int i = 0; i < nt * 3; i++, o += 2) RF_T[i] = System.BitConverter.ToUInt16(b, o);
            }
            var v = new Vector3[RF_V.Length];
            for (int i = 0; i < v.Length; i++) { var p = RF_V[i]; v[i] = new Vector3((flip ? -p.x : p.x) * len, p.y * h, p.z * d); }
            var t = (int[])RF_T.Clone();
            if (flip) for (int i = 0; i < t.Length; i += 3) { int x = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = x; }
            var m = new Mesh { name = "rockface" }; m.vertices = v; m.uv = new Vector2[v.Length]; m.triangles = t;
            m.RecalculateNormals(); m.RecalculateBounds(); MeshGen.AddGlyphUV(m, false);
            return m;
        }
        /// a wall from a to b, its face turned to 'face': overlapping rock pieces (mirrored at random) + a row of colliders
        void RockWall(Transform T, Mats mats, Material mat, Vector2 a, Vector2 b, Vector2 face, float h0, float h1, bool collide = true)
        {
            var dir = b - a; float L = dir.magnitude; dir /= L;
            float yaw = Mathf.Atan2(face.x, face.y) * Mathf.Rad2Deg;
            int n = Mathf.Max(1, Mathf.CeilToInt(L / 2.4f)); float seg = L / n, hMax = 0;
            for (int k = 0; k < n; k++)
            {
                float len = seg * Rr(1.3f, 1.55f), h = Rr(h0, h1); hMax = Mathf.Max(hMax, h);
                var c = a + dir * (seg * (k + 0.5f)) + face * Rr(-0.12f, 0.08f);
                MeshGen.Obj(RockPiece(len, h, Mathf.Min(len, 3f) * Rr(0.8f, 1.1f), Rnd() < 0.5f), mat, T, new Vector3(c.x, -0.06f, c.y), Quaternion.Euler(0, yaw + Rr(-5, 5), 0), true, "Wall");
                if (Rnd() < 0.35f) Moss(T, mats, c.x + face.x * 0.25f + dir.x * Rr(-0.8f, 0.8f), c.y + face.y * 0.25f + dir.y * Rr(-0.8f, 0.8f), Rr(0.3f, 0.5f), 0.01f);
            }
            if (!collide) return;
            for (float s = 0; s <= L + 0.01f; s += 0.5f) { var p = a + dir * s - face * 0.2f; colliders.Add(Collider2.Circle(p.x, p.y, 0.5f)); }
            wallSegs.Add(new WallSeg { a = a, b = b, h = hMax });
        }
        void Moss(Transform T, Mats mats, float x, float z, float r, float y)
        {
            var g = MeshGen.Obj(MeshGen.Sphere(1, 8, 3, Mathf.PI / 2), mats["MOSS"], T, new Vector3(x, y, z), Quaternion.Euler(0, Rr(0, 360), 0), false, "Moss");
            g.transform.localScale = new Vector3(r * Rr(0.8f, 1.2f), Rr(0.04f, 0.08f), r * Rr(0.8f, 1.2f));
        }
        void Mushroom(Transform T, Mats mats, float x, float z, float s, bool glow, List<Renderer> caps)
        {
            if (WaterK(x, z) < 1.05f) return;
            var holder = new GameObject(glow ? "GlowMushroom" : "Mushroom").transform; holder.SetParent(T, false);
            holder.localPosition = new Vector3(x, 0, z); holder.localRotation = Quaternion.Euler(Rr(-12, 12), Rr(0, 360), Rr(-12, 12)); holder.localScale = Vector3.one * s;
            float h = Rr(0.07f, 0.16f);
            MeshGen.Obj(MeshGen.Cylinder(0.022f, 0.016f, h, 6), mats["LINEN"], holder, new Vector3(0, h / 2, 0), Quaternion.identity, false, "Stem");
            var cap = MeshGen.Obj(MeshGen.Sphere(0.06f, 8, 4, Mathf.PI / 2), mats[glow ? "GLOWSHROOM" : "MUSH"], holder, new Vector3(0, h - 0.005f, 0), Quaternion.identity, false, "Cap");
            cap.transform.localScale = new Vector3(1, glow ? 0.75f : 0.55f, 1);
            if (caps != null) caps.Add(cap.GetComponent<Renderer>());
        }
        // a skeleton laid along its local +z, face up (rot sets it upright when it is walled in)
        void Skeleton(Transform T, Mats mats, Vector3 pos, Quaternion rot, float s)
        {
            var sk = new GameObject("Skeleton").transform; sk.SetParent(T, false); sk.localPosition = pos; sk.localRotation = rot; sk.localScale = Vector3.one * s;
            Material B = mats["BONE"];
            void Rod(Vector3 a, Vector3 b, float r) { var d = b - a; MeshGen.Obj(MeshGen.Cylinder(r, r * 0.85f, d.magnitude, 6), B, sk, (a + b) / 2, Quaternion.FromToRotation(Vector3.up, d), false, "Bone"); }
            MeshGen.Obj(MeshGen.Sphere(0.1f, 9, 7), B, sk, new Vector3(0, 0.06f, 0.82f), Quaternion.Euler(Rr(-20, 20), Rr(-30, 30), 0), false, "Skull");
            MeshGen.Obj(MeshGen.Box(0.08f, 0.03f, 0.06f), B, sk, new Vector3(0, 0.0f, 0.9f), Quaternion.identity, false, "Jaw");
            Rod(new Vector3(0, 0.03f, 0.7f), new Vector3(0, 0.03f, 0.05f), 0.018f);                                      // spine
            for (int i = 0; i < 5; i++)
                MeshGen.Obj(MeshGen.Torus(0.12f - i * 0.008f, 0.011f, 10, 4, Mathf.PI), B, sk, new Vector3(0, 0.05f, 0.62f - i * 0.065f), Quaternion.Euler(0, 0, 0), false, "Rib");
            var pel = MeshGen.Obj(MeshGen.Sphere(0.11f, 8, 5), B, sk, new Vector3(0, 0.03f, 0.02f), Quaternion.identity, false, "Pelvis"); pel.transform.localScale = new Vector3(1.3f, 0.45f, 0.7f);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                float ax = Rr(0.05f, 0.25f);
                Rod(new Vector3(sd * 0.17f, 0.02f, 0.64f), new Vector3(sd * (0.22f + ax), 0.02f, 0.36f), 0.017f);
                Rod(new Vector3(sd * (0.22f + ax), 0.02f, 0.36f), new Vector3(sd * (0.26f + ax * 1.6f), 0.02f, 0.12f), 0.014f);
                float lx = Rr(0.0f, 0.12f);
                Rod(new Vector3(sd * 0.09f, 0.02f, -0.02f), new Vector3(sd * (0.12f + lx), 0.02f, -0.44f), 0.022f);
                Rod(new Vector3(sd * (0.12f + lx), 0.02f, -0.44f), new Vector3(sd * (0.13f + lx * 1.5f), 0.02f, -0.86f), 0.018f);
            }
        }
        void Bone(Transform T, Mats mats, float x, float z, float len)
        {
            var b = new GameObject("LooseBone").transform; b.SetParent(T, false); b.localPosition = new Vector3(x, 0.025f, z); b.localRotation = Quaternion.Euler(0, Rr(0, 360), 0);
            MeshGen.Obj(MeshGen.Cylinder(0.016f, 0.014f, len, 6), mats["BONE"], b, Vector3.zero, Quaternion.Euler(90, 0, 0), false, "Bone");
            MeshGen.Obj(MeshGen.Sphere(0.026f, 6, 4), mats["BONE"], b, new Vector3(0, 0, len / 2), Quaternion.identity, false, "BoneEnd");
            MeshGen.Obj(MeshGen.Sphere(0.026f, 6, 4), mats["BONE"], b, new Vector3(0, 0, -len / 2), Quaternion.identity, false, "BoneEnd");
        }
        void Torch(Transform T, Mats mats, float x, float z, bool leaning)
        {
            var t = new GameObject("OldTorch").transform; t.SetParent(T, false); t.localPosition = new Vector3(x, 0.02f, z);
            t.localRotation = Quaternion.Euler(0, Rr(0, 360), 0) * Quaternion.Euler(leaning ? Rr(25, 50) : 88, 0, 0);
            MeshGen.Obj(MeshGen.Cylinder(0.022f, 0.018f, 0.5f, 6), mats["WOOD"], t, new Vector3(0, 0.25f, 0), Quaternion.identity, true, "TorchStick");
            MeshGen.Obj(MeshGen.Cylinder(0.035f, 0.03f, 0.1f, 6), mats["DARK"], t, new Vector3(0, 0.53f, 0), Quaternion.identity, true, "TorchHead");      // burnt out
        }
        void Pickaxe(Transform T, Mats mats, float x, float z)
        {
            var t = new GameObject("Pickaxe").transform; t.SetParent(T, false); t.localPosition = new Vector3(x, 0.03f, z);
            t.localRotation = Quaternion.Euler(0, Rr(0, 360), 0) * Quaternion.Euler(88, 0, 0);
            MeshGen.Obj(MeshGen.Cylinder(0.02f, 0.018f, 0.85f, 6), mats["WOOD"], t, new Vector3(0, 0.42f, 0), Quaternion.identity, true, "Handle");
            MeshGen.Obj(MeshGen.Box(0.2f, 0.035f, 0.035f), mats["STEEL"], t, new Vector3(0.09f, 0.84f, 0), Quaternion.Euler(0, 0, -14), true, "Pick");
            MeshGen.Obj(MeshGen.Box(0.2f, 0.035f, 0.035f), mats["STEEL"], t, new Vector3(-0.09f, 0.84f, 0), Quaternion.Euler(0, 0, 14), true, "Pick");
        }
        // a beam of light from 'from' down to the floor at 'to': a spot light and the dust glinting in it
        void Beam(Transform T, Vector3 to, Vector3 from, float radius, int n, Color c, float intensity, float angle)
        {
            var lg = new GameObject("BeamLight"); lg.transform.SetParent(T, false); lg.transform.localPosition = from;
            lg.transform.localRotation = Quaternion.LookRotation(to - from);
            var l = lg.AddComponent<Light>(); l.type = LightType.Spot; l.color = c; l.intensity = intensity; l.range = (to - from).magnitude + 3; l.spotAngle = angle; l.innerSpotAngle = angle * 0.6f;
            l.shadows = LightShadows.None;
            var pts = new List<Vector3>(); var sd = new List<Vector2>();
            for (int i = 0; i < n; i++) { float a = Rr(0, 6.28f), r = Mathf.Sqrt(Rnd()); pts.Add(new Vector3(Mathf.Cos(a) * r, Rnd(), Mathf.Sin(a) * r)); sd.Add(new Vector2(r, Rnd())); }
            var d = from - to;
            GlyphLayer.MakeMotes(T, to, Quaternion.FromToRotation(Vector3.up, d.normalized), new Vector3(radius, d.magnitude, radius), pts, sd, 0, c * 0.85f, new Vector4(0.04f, 0, 0, 0));
        }
        // a flat surface (mud) covering where k < 1
        void Patch(Transform T, Material m, System.Func<float, float, float> k, Vector2 c, float r, float y, string name)
        {
            const float Q = 0.12f; var vs = new List<Vector3>(); var ts = new List<int>(); var uvs = new List<Vector2>();
            for (float x = c.x - r; x < c.x + r; x += Q)
                for (float z = c.y - r; z < c.y + r; z += Q)
                {
                    if (Mathf.Min(Mathf.Min(k(x, z), k(x + Q, z)), Mathf.Min(k(x, z + Q), k(x + Q, z + Q))) > 1.01f) continue;
                    int o = vs.Count;
                    vs.Add(new Vector3(x, y, z)); vs.Add(new Vector3(x, y, z + Q)); vs.Add(new Vector3(x + Q, y, z + Q)); vs.Add(new Vector3(x + Q, y, z));
                    for (int i = 0; i < 4; i++) uvs.Add(new Vector2(vs[o + i].x * 0.25f, vs[o + i].z * 0.25f));
                    ts.Add(o); ts.Add(o + 1); ts.Add(o + 2); ts.Add(o); ts.Add(o + 2); ts.Add(o + 3);
                }
            if (vs.Count == 0) return;
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; mesh.SetVertices(vs); mesh.SetUVs(0, uvs); mesh.SetTriangles(ts, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); MeshGen.AddGlyphUV(mesh, false);
            MeshGen.Obj(mesh, m, T, Vector3.zero, Quaternion.identity, false, name);
        }
        // water where k < 1: the dark surface (for the lit colour) and its own characters
        void Water(Transform T, Mats mats, System.Func<float, float, float> k, Vector2 c, float r, Vector4 mode)
        {
            const float Q = 0.12f; var vs = new List<Vector3>(); var ts = new List<int>();
            for (float x = c.x - r; x < c.x + r; x += Q)
                for (float z = c.y - r; z < c.y + r; z += Q)
                {
                    if (Mathf.Min(Mathf.Min(k(x, z), k(x + Q, z)), Mathf.Min(k(x, z + Q), k(x + Q, z + Q))) > 1.02f) continue;
                    int o = vs.Count;
                    vs.Add(new Vector3(x, WATER_Y, z)); vs.Add(new Vector3(x, WATER_Y, z + Q)); vs.Add(new Vector3(x + Q, WATER_Y, z + Q)); vs.Add(new Vector3(x + Q, WATER_Y, z));
                    ts.Add(o); ts.Add(o + 1); ts.Add(o + 2); ts.Add(o); ts.Add(o + 2); ts.Add(o + 3);
                }
            if (vs.Count == 0) return;
            var wm = new Mesh { name = "cavewater", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; wm.SetVertices(vs); wm.SetTriangles(ts, 0); wm.RecalculateNormals(); wm.RecalculateBounds();
            var go = MeshGen.Obj(wm, mats["WATER"], T, Vector3.zero, Quaternion.identity, false, "Water");
            var wp = new List<Vector3>(); var wsd = new List<Vector2>();
            for (float z = c.y - r; z <= c.y + r; z += 0.13f)
                for (float x = c.x - r; x <= c.x + r; x += 0.1f)
                {
                    float kk = k(x, z); if (kk > 1.0f) continue;
                    wp.Add(new Vector3(x, WATER_Y, z)); wsd.Add(new Vector2(kk, Rnd()));
                }
            if (wp.Count > 0) GlyphLayer.MakeWater(go.transform, wp, wsd, mode);
        }

        public override void Update(float t)
        {
            // the glowing mushrooms breathe: a dim, slow pulse of their light and their caps
            if (mpb == null) mpb = new MaterialPropertyBlock();
            foreach (var g in glows)
            {
                float p = 0.72f + 0.28f * Mathf.Sin(t * 0.7f + g.ph) * (0.8f + 0.2f * Mathf.Sin(t * 1.9f + g.ph * 2));
                g.l.intensity = g.baseI * p;
                foreach (var r in g.caps) { if (r == null) continue; r.GetPropertyBlock(mpb); mpb.SetColor("_EmissionColor", glowEm * (0.45f + 0.55f * p)); r.SetPropertyBlock(mpb); }
            }
            if (ember != null) ember.intensity = 0.55f + 0.2f * Mathf.PerlinNoise(t * 3f, 0.3f);
            // a drop reaches the water: a ring where it falls
            if (lastT >= 0 && addRipple != null)
                foreach (var d in drips)
                {
                    float a = (lastT / DRIP_PERIOD + d.z) % 1f, b = (t / DRIP_PERIOD + d.z) % 1f, hit = DRIP_FALL / DRIP_PERIOD;
                    if ((a < hit && b >= hit) || (b < a && b >= hit)) addRipple(d.x, d.y, 0.85f);
                }
            lastT = t;
        }
        public override void Cutaway(Vector3 camOff)
        {
            // is a wall between the camera and the hero? walk from him toward the camera (on the ground) and compare the
            // heights of the walls crossed with the height of the line of sight there
            var h2 = new Vector2(focus.x, focus.z); var c2 = new Vector2(camOff.x, camOff.z); float horiz = c2.magnitude;
            bool hidden = false;
            if (horiz > 1e-3f)
            {
                c2 /= horiz; float tanP = camOff.y / horiz;
                for (float t = 0.15f; t < 4.5f && !hidden; t += 0.15f)
                {
                    var q = h2 + c2 * t; float sight = 0.4f + t * tanP;
                    foreach (var w in wallSegs)
                    {
                        if (w.h < sight) continue;
                        var ab = w.b - w.a; float u = Mathf.Clamp01(Vector2.Dot(q - w.a, ab) / ab.sqrMagnitude);
                        if ((w.a + ab * u - q).sqrMagnitude < 0.45f * 0.45f) { hidden = true; break; }
                    }
                }
            }
            // the walls start to show through, then thin out (a second or so), and fill in again when he leaves
            seeFade = Mathf.MoveTowards(seeFade, hidden ? 1f : 0f, Time.deltaTime * (hidden ? 1.6f : 2.2f));
            Shader.SetGlobalVector("_SeeHero", new Vector4(focus.x, focus.y + 1.0f, focus.z, 0.2f));
            Shader.SetGlobalFloat("_SeeFade", Mathf.SmoothStep(0f, 1f, seeFade));
        }
    }
}
