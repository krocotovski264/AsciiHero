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
        /// the pools: the deep one a little lake (a shelf, then deeper than the waist in its middle), the others puddles; the
        /// stream a few centimetres; the mud a swamp's - shallow water over sticky ground
        public override float Depth(float x, float z)
        {
            float d = 0;
            foreach (var p in pools) { float k = PoolK(p, x, z); if (k < 0.97f) { float u = (0.97f - k) / 0.97f; d = Mathf.Max(d, p.depth > 0.1f ? WaterDepth.Lake(u, 0.3f, 1.2f) : WaterDepth.Puddle(u, p.depth)); } }
            if (d == 0 && WaterK(x, z) < 0.97f) d = WaterDepth.Stream(Mathf.Clamp01((0.97f - WaterK(x, z)) * 3f), 0.08f);
            return d;
        }
        public override bool HasWater => true;
        /// the mirror only over the deep pool (the puddles and the stream keep their own sheen)
        public override List<Bounds> MirrorWater()
        {
            var l = new List<Bounds>();
            foreach (var p in pools) if (p.depth > 0.1f) { float r = Mathf.Max(p.rx, p.rz) * 1.15f; l.Add(new Bounds(new Vector3(p.c.x + origin.x, WATER_Y, p.c.y + origin.z), new Vector3(2 * r, 0.2f, 2 * r))); }
            return l;
        }
        public override bool InWater(float x, float z) { return WaterK(x, z) < 0.97f; }
        public override float WaterSink(float x, float z)
        {
            float m = MudK(x, z);
            float s = Mathf.Min(Depth(x, z), WaterDepth.WAIST);
            if (m < 1) s = Mathf.Max(s, 0.08f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1f, 0.6f, m)));
            return s;
        }
        public override float SpeedK(float x, float z)
        {
            float k = 1;
            if (MudK(x, z) < 0.95f) k = 0.5f;                                   // sticky mud: a slow wade, no running
            // (in the water the depth decides: WaterDepth.WadeK)
            return k;
        }

        // ---- walls: continuous rock faces (a scanned cliff, straightened); a wall between the camera and the hero
        // does not sink: it starts to show through around him (dithered, AsciiHero/Lit + Glyph, _SeeThrough) ----
        struct WallSeg { public Vector2 a, b; public float h; }
        readonly List<WallSeg> wallSegs = new List<WallSeg>();
        float seeFade;
        readonly List<KeyValuePair<Mesh, Matrix4x4>> wallPieces = new List<KeyValuePair<Mesh, Matrix4x4>>();      // for placing things against the rock
        static Dictionary<string, KeyValuePair<Vector3[], int[]>> CM;      // scanned rocks and the pickaxe (Resources/cavemeshes)
        // ---- light that lives ----
        class Glow { public Light l; public float baseI, ph; public List<Renderer> caps = new List<Renderer>(); }
        readonly List<Glow> glows = new List<Glow>(); Light ember; MaterialPropertyBlock mpb; Color glowEm;
        // ---- drops falling into the pools ----
        public readonly List<Vector3> drips = new List<Vector3>();      // x, z, phase
        static float Frac(float x) { return x - Mathf.Floor(x); }
        // a drop every 3.5 s on average, each moved by up to +-0.75 s at random: one comes 2-5 s after the one before
        public const float DRIP_PERIOD = 3.5f, DRIP_FALL = 0.55f, DRIP_TOP = 2.9f;
        float lastT = -1;

        public static Cave Build(Mats mats) { var C = new Cave(); C.Construct(mats); return C; }

        /// the mouth in the near (south) wall: out into the forest
        public const float MOUTH_X = 5f;
        void Construct(Mats mats)
        {
            root = new GameObject("Cave"); var T = root.transform; R = new System.Random(20261115);
            id = "cave";
            ambSky = Mats.Hex(0x40506a) * 0.16f; ambEq = Mats.Hex(0x303840) * 0.14f; ambGround = Mats.Hex(0x181410) * 0.12f;    // dark, a little cold
            moonK = 0; heroLightK = 1.05f; exposure = 1.3f;
            spawn = new Vector3(MOUTH_X, 0, HZ - 2.0f);
            // (the south edge is open where the mouth leads out into the forest)
            bounds = Rect.MinMaxRect(-HX + 0.6f, -HZ + 0.6f, HX - 0.6f, HZ + 2.6f);
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

            // ---- walls: carved out of one volume of rock (see BuildRock) ----
            var rock = new Material(mats["WETROCK"]) { name = "CaveWall" }; rock.SetFloat("_SeeThrough", 1); rock.SetFloat("_Cull", 0); rock.SetVector("_GlyphCell", new Vector4(0.12f, 0.2f, 0, 0));      // (the rock is big: coarser characters, half as many)
            BuildRock(T, mats, rock);

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
                var p = Place(0.6f, 9.3f, -7.4f, 7.4f, 0.2f); float r = Rr(0.08f, 0.18f), sc = r * 2.6f;
                MeshGen.Obj(CaveMesh("stone", new Vector3(sc, sc * Rr(0.9f, 1.4f), sc), Rnd() < 0.5f), mats["ROCK"], T, new Vector3(p.x, -0.02f, p.y), Quaternion.Euler(Rr(-12, 12), Rr(0, 360), Rr(-12, 12)), true, "SharpRock");
            }       // (small: walked over, no collider)
            // ---- boulders, everywhere but the water ----
            for (int i = 0; i < 12; i++)
            {
                var p = Place(-9.2f, 9.2f, -7.3f, 7.3f, 0.6f); float s = Rr(0.3f, 0.7f);
                float d = s * 2.3f;
                MeshGen.Obj(CaveMesh(Rnd() < 0.5f ? "boulder_a" : "boulder_b", new Vector3(d * Rr(0.9f, 1.1f), d * Rr(0.85f, 1.15f), d * Rr(0.9f, 1.1f)), Rnd() < 0.5f), mats[Rnd() < 0.5f ? "ROCK" : "WETROCK"], T, new Vector3(p.x, -0.04f, p.y), Quaternion.Euler(0, Rr(0, 360), 0), true, "Boulder");
                if (s > 0.42f) colliders.Add(Collider2.Circle(p.x, p.y, s * 1.05f));      // (the small ones are stepped over)
                if (Rnd() < 0.6f) Moss(T, mats, p.x, p.y, s * 0.8f, s * 0.75f);          // moss on its top
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
            WallSkeleton(T, mats, "anat_sit", new Vector2(-HX - 0.15f, 2.4f), Vector2.right, 1.0f, 4);        // walled into the west wall
            WallSkeleton(T, mats, "anat_stand", new Vector2(-HX - 0.15f, 6.0f), Vector2.right, 1.05f, 6);
            WallSkeleton(T, mats, "anat_stand", new Vector2(-7.2f, 0.1f), Vector2.up, 1.0f, 8);              // in the middle wall, seen from the chamber
            WallSkeleton(T, mats, "anat_sit", new Vector2(-0.25f, 6.8f), Vector2.left, 0.95f, 2);           // in the dividing wall
            MeshGen.Obj(CaveMesh("anat_lie", Vector3.one), mats["BONE"], T, new Vector3(-6.2f, 0.0f, 6.4f), Quaternion.Euler(0, 35, 0), false, "Skeleton");       // lying on the floor
            MeshGen.Obj(CaveMesh("anat_curl", Vector3.one), mats["BONE"], T, new Vector3(-4.0f, 0.0f, 3.2f), Quaternion.Euler(0, 160, 0), false, "Skeleton");
            MeshGen.Obj(CaveMesh("anat_lie", Vector3.one * 0.97f), mats["BONE"], T, new Vector3(7.8f, 0.0f, 1.8f), Quaternion.Euler(0, -70, 0), false, "Skeleton");      // a miner in the entrance hall
            colliders.Add(Collider2.Circle(-6.2f, 6.4f, 0.45f)); colliders.Add(Collider2.Circle(7.8f, 1.8f, 0.45f));
            for (int i = 0; i < 26; i++)
            {
                var p = i < 18 ? Place(-9.2f, -0.8f, 0.8f, 7.4f, 0.05f) : Place(0.8f, 9.2f, -7.4f, 7.4f, 0.05f);
                Bone(T, mats, p.x, p.y, Rr(0.12f, 0.36f));
            }
            for (int i = 0; i < 4; i++) { var p = Place(-9f, -1f, 1f, 7.2f, 0.1f); MeshGen.Obj(CaveMesh("skull", Vector3.one * Rr(0.85f, 1f)), mats["BONE"], T, new Vector3(p.x, -0.015f, p.y), Quaternion.Euler(Rr(-15, 15), Rr(0, 360), Rr(-25, 25)), true, "Skull"); }

            // ---- the miners' leftovers in the entrance hall: torches, pickaxes, one torch still smouldering ----
            for (int i = 0; i < 5; i++) { var p = Place(1.0f, 9.0f, 0.8f, 7.2f, 0.15f); Torch(T, mats, p.x, p.y, Rnd() < 0.5f); }
            for (int i = 0; i < 3; i++) { var p = Place(1.0f, 9.0f, 0.8f, 7.2f, 0.2f); Pickaxe(T, mats, p.x, p.y); }
            {
                float wd = RayToWall(new Vector3(HX - 1.5f, 1.3f, 1.6f), Vector3.right, 3f); float wx = wd < 8 ? HX - 1.5f + wd : HX - 0.2f;
                var holder = new GameObject("WallTorch").transform; holder.SetParent(T, false); holder.localPosition = new Vector3(wx + 0.03f, 1.3f, 1.6f);
                holder.localRotation = Quaternion.Euler(0, -90, 0);              // its bracket to the rock, the basket into the hall
                var k = Vector3.one * 0.55f;
                MeshGen.Obj(CaveMesh("torch_wall_stick", k), mats["DARK"], holder, Vector3.zero, Quaternion.identity, true, "TorchBracket");
                MeshGen.Obj(CaveMesh("torch_wall_head", k), mats["DARK"], holder, Vector3.zero, Quaternion.identity, true, "TorchHead");
                GlyphLayer.MakeFire(holder, new Vector3(0, 0.39f, 0.2f), 0.04f, 0.04f, 0.15f, 0.05f, 16, 0.6f);
                var lg = new GameObject("Ember"); lg.transform.SetParent(holder, false); lg.transform.localPosition = new Vector3(0, 0.55f, 0.45f);
                ember = lg.AddComponent<Light>(); ember.type = LightType.Point; ember.color = new Color(1f, 0.55f, 0.25f); ember.intensity = 0.7f; ember.range = 2.8f;
            }

            // ---- light: a shaft through a break in the vault over the grotto, a beam through the crevice of the entrance ----
            // from above: through breaks in the vault (the grotto, the wet hall, the bone chamber)
            Beam(T, new Vector3(5.2f, 0, -4.0f), new Vector3(5.6f, 7.5f, -4.6f), 0.85f, 230, new Color(1f, 0.93f, 0.78f), 5.5f, 30f);
            Beam(T, new Vector3(-5.6f, 0, -4.4f), new Vector3(-5.1f, 7.5f, -5.2f), 0.6f, 170, new Color(0.9f, 0.95f, 1f), 4.0f, 24f);
            Beam(T, new Vector3(-3.4f, 0, 4.6f), new Vector3(-3.0f, 7.5f, 4.0f), 0.5f, 140, new Color(0.75f, 0.85f, 1f), 3.2f, 22f);
            // from the sides: the crevice of the entrance (east), a crack in the west wall, one in the far wall
            Beam(T, new Vector3(6.4f, 0, 4.1f), new Vector3(11.5f, 1.9f, 4.6f), 0.5f, 150, new Color(0.85f, 0.9f, 1f), 4.5f, 24f);
            Beam(T, new Vector3(-6.4f, 0, -2.6f), new Vector3(-11.6f, 2.1f, -2.9f), 0.42f, 130, new Color(1f, 0.9f, 0.75f), 3.8f, 22f);
            Beam(T, new Vector3(-5.4f, 0, -4.2f), new Vector3(-5.7f, 2.3f, -10.5f), 0.4f, 120, new Color(0.95f, 0.92f, 0.85f), 3.4f, 20f);

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
                if (Mathf.Abs(x - MOUTH_X) < 2.4f && z > HZ - 3.2f) continue;            // keep the way out free
                bool ok = true;
                foreach (var c in colliders) if (c.circle && (q - new Vector2(c.x, c.z)).magnitude < c.r + r + 0.15f) { ok = false; break; }
                if (ok) return q;
            }
            return new Vector2(Rr(x0, x1), Rr(z0, z1));
        }
        /// a scanned mesh, its size baked into the vertices (the characters are laid out on the mesh as it is, so a
        /// scaled transform would stretch them apart); flipX mirrors it
        public static Mesh CaveMesh(string name, Vector3 scale, bool flipX = false)
        {
            if (CM == null)
            {
                CM = new Dictionary<string, KeyValuePair<Vector3[], int[]>>();
                var b = Resources.Load<TextAsset>("cavemeshes").bytes; int o = 8, count = System.BitConverter.ToInt32(b, 4);
                for (int k = 0; k < count; k++)
                {
                    int ln = b[o]; string nm = System.Text.Encoding.ASCII.GetString(b, o + 1, ln); o += 1 + ln;
                    int nv = System.BitConverter.ToInt32(b, o), nt = System.BitConverter.ToInt32(b, o + 4); o += 8;
                    var vv = new Vector3[nv]; var tt = new int[nt * 3];
                    for (int i = 0; i < nv; i++, o += 12) vv[i] = new Vector3(System.BitConverter.ToSingle(b, o), System.BitConverter.ToSingle(b, o + 4), System.BitConverter.ToSingle(b, o + 8));
                    for (int i = 0; i < nt * 3; i++, o += 2) tt[i] = System.BitConverter.ToUInt16(b, o);
                    CM[nm] = new KeyValuePair<Vector3[], int[]>(vv, tt);
                }
            }
            var src = CM[name]; var v = new Vector3[src.Key.Length];
            for (int i = 0; i < v.Length; i++) { var p = src.Key[i]; v[i] = new Vector3((flipX ? -p.x : p.x) * scale.x, p.y * scale.y, p.z * scale.z); }
            var t = (int[])src.Value.Clone();
            if (flipX) for (int i = 0; i < t.Length; i += 3) { int x = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = x; }
            var m = new Mesh { name = name }; m.vertices = v; m.uv = new Vector2[v.Length]; m.triangles = t;
            m.RecalculateNormals(); m.RecalculateBounds(); MeshGen.AddGlyphUV(m, false);
            return m;
        }
        /// one piece of rock face (two scanned cliffs, straightened): len wide, h high, its relief d deep; local +z is the face
        /// a wall from a to b, its face turned to 'face': overlapping rock pieces (mirrored at random) + a row of colliders
        // ---- the rock: a volume, not pieces. As in voxel cave generators, the walls are the surface of a field over
        // the whole cave: f(p) > 0 in the air, < 0 in the rock. The field starts from the plan (the outline of the
        // halls and the two dividing walls with their passages; each wall rises to its own ragged crest and is wider
        // at its foot, where fallen rock gathers), then 3D noise reshapes it: domain-warped fBm for big bulges and
        // hollows (overhangs come by themselves), weighted ridged multifractal for sharp creases, wandering strata
        // that jut out as ledges, a fine grain. The crevices the side beams shine through are cut out of it. Its
        // surface is extracted with naive surface nets (one vertex per boundary cell, a quad per crossed edge), which
        // gives a smooth, closed, seamless rock - and new every time the cave is built. ----
        struct RockSeg { public Vector2 a, b; public float t, h; }
        struct Slot { public Vector3 a, b; public float r; }
        readonly List<RockSeg> rockSegs = new List<RockSeg>(); readonly List<Slot> slots = new List<Slot>();
        float nOx, nOy, nOz;
        static float H3(int x, int y, int z)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xffffff) / 16777215f;
        }
        static float N3(float x, float y, float z)          // smooth value noise 0..1
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y), iz = Mathf.FloorToInt(z);
            float fx = x - ix, fy = y - iy, fz = z - iz;
            fx = fx * fx * fx * (fx * (fx * 6 - 15) + 10); fy = fy * fy * fy * (fy * (fy * 6 - 15) + 10); fz = fz * fz * fz * (fz * (fz * 6 - 15) + 10);
            float a = Mathf.Lerp(H3(ix, iy, iz), H3(ix + 1, iy, iz), fx), b = Mathf.Lerp(H3(ix, iy + 1, iz), H3(ix + 1, iy + 1, iz), fx);
            float c = Mathf.Lerp(H3(ix, iy, iz + 1), H3(ix + 1, iy, iz + 1), fx), d = Mathf.Lerp(H3(ix, iy + 1, iz + 1), H3(ix + 1, iy + 1, iz + 1), fx);
            return Mathf.Lerp(Mathf.Lerp(a, b, fy), Mathf.Lerp(c, d, fy), fz);
        }
        static float Fbm3(float x, float y, float z, int oct)
        {
            float s = 0, a = 0.5f, n = 0;
            for (int o = 0; o < oct; o++) { s += a * N3(x, y, z); n += a; a *= 0.5f; x = x * 2.03f + 17.1f; y = y * 2.03f - 9.3f; z = z * 2.03f + 5.7f; }
            return s / n;
        }
        static float RidgedW(float x, float y, float z)       // weighted ridged multifractal (each octave weighted by the last)
        {
            float s = 0, a = 1, w = 1, n = 0;
            for (int o = 0; o < 3; o++)
            {
                float v = 1 - Mathf.Abs(2 * N3(x, y, z) - 1); v = v * v * w; w = Mathf.Clamp01(v * 3.2f);
                s += v * a; n += a; a *= 0.5f; x = x * 2.1f + 3.3f; y = y * 2.1f + 1.1f; z = z * 2.1f - 7.7f;
            }
            return s / n;
        }
        static float SegDist(float x, float z, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a, q = new Vector2(x, z) - a; float t = Mathf.Clamp01(Vector2.Dot(q, ab) / ab.sqrMagnitude); return (q - ab * t).magnitude;
        }
        /// the rock field: > 0 in the air, < 0 in the rock (about metres)
        float Air(Vector3 p)
        {
            float x = p.x, y = p.y, z = p.z;
            float foot = Mathf.Clamp01(1 - y / 1.1f); foot *= foot;
            float crestN = Fbm3(x * 0.23f + nOx, 0, z * 0.23f + nOz, 3) - 0.5f;
            // the outline: rock outside the halls, leaning back as it rises; the near side (towards the camera) lower
            float room = Mathf.Min(HX - Mathf.Abs(x), HZ - Mathf.Abs(z)) - 0.3f * foot + 0.1f * y;
            float hOut = Mathf.Lerp(3.0f, 1.8f, Mathf.SmoothStep(0, 1, (z - (HZ - 2.5f)) / 2.5f)) + 1.1f * crestN;
            float f = Mathf.Max(room, y - hOut);
            foreach (var sg in rockSegs)
            {
                float d = SegDist(x, z, sg.a, sg.b) - sg.t - 0.3f * foot;
                if (d > 1.6f) continue;
                f = Mathf.Min(f, Mathf.Max(d, y - (sg.h + 0.9f * crestN)));
            }
            foreach (var sl in slots)
            {
                Vector3 ab = sl.b - sl.a; float t = Mathf.Clamp01(Vector3.Dot(p - sl.a, ab) / ab.sqrMagnitude);
                f = Mathf.Max(f, sl.r - (p - (sl.a + ab * t)).magnitude);
            }
            if (Mathf.Abs(f) > 1.1f) return f;                   // far from any surface: no need for the detail
            // domain warp, then the big shapes, the creases, the strata, the grain
            float wx = x * 0.45f + nOx + 1.4f * Fbm3(x * 0.2f + nOx, y * 0.2f, z * 0.2f + nOz, 2);
            float wy = y * 0.45f + nOy + 1.4f * Fbm3(x * 0.2f + nOz, y * 0.2f + 4.1f, z * 0.2f + nOx, 2);
            float wz = z * 0.45f + nOz + 1.4f * Fbm3(x * 0.2f - 3.7f, y * 0.2f + nOy, z * 0.2f + 8.3f, 2);
            float big = (Fbm3(wx, wy, wz, 3) - 0.5f) * 1.25f;
            float crease = RidgedW(wx * 2.2f, wy * 2.2f, wz * 2.2f) * 0.26f;
            float sv = y * 2.2f + 1.2f * Fbm3(x * 0.18f + nOx, 0.5f, z * 0.18f + nOz, 2), ledge = sv - Mathf.Floor(sv);
            float strata = (1 - Mathf.SmoothStep(0, 1, ledge / 0.3f)) * 0.13f * Mathf.Clamp01(1.5f * Fbm3(x * 0.15f + 9, 0, z * 0.15f + 9, 2));
            float grain = (N3(x * 5.1f + nOx, y * 5.1f, z * 5.1f + nOz) - 0.5f) * 0.05f;
            return f + big - crease + 0.1f - strata + grain;
        }
        /// how far from 'o' along 'd' the rock is (9 = none within reach)
        float RayToWall(Vector3 o, Vector3 d, float maxD = 3f)
        {
            float t = 0;
            for (int i = 0; i < 300 && t < maxD; i++) { float f = Air(o + d * t); if (f < 0.004f) return t; t += Mathf.Max(0.008f, f * 0.5f); }
            return 9;
        }
        void BuildRock(Transform T, Mats mats, Material mat)
        {
            nOx = Rr(0, 300); nOy = Rr(0, 300); nOz = Rr(0, 300);
            // the plan: the two dividing walls with their passages
            foreach (var r in new[] { new Vector2(-HZ - 1, -5.6f), new Vector2(-2.4f, 2.4f), new Vector2(5.6f, HZ + 1) })
                rockSegs.Add(new RockSeg { a = new Vector2(0, r.x), b = new Vector2(0, r.y), t = 0.42f, h = Rr(2.1f, 2.6f) });
            foreach (var r in new[] { new Vector2(-HX - 1, -4.6f), new Vector2(-1.4f, 3.4f), new Vector2(6.6f, HX + 1) })
                rockSegs.Add(new RockSeg { a = new Vector2(r.x, 0.1f), b = new Vector2(r.y, 0.1f), t = 0.42f, h = Rr(2.1f, 2.6f) });
            // the crevices the side light comes through (Beam below starts in them)
            slots.Add(new Slot { a = new Vector3(12.5f, 2.1f, 4.65f), b = new Vector3(8.6f, 1.0f, 4.35f), r = 0.42f });
            slots.Add(new Slot { a = new Vector3(MOUTH_X, 1.15f, HZ - 1.4f), b = new Vector3(MOUTH_X, 1.15f, HZ + 2.8f), r = 1.75f });     // the mouth
            slots.Add(new Slot { a = new Vector3(-12.6f, 2.3f, -2.95f), b = new Vector3(-8.7f, 1.1f, -2.75f), r = 0.38f });
            slots.Add(new Slot { a = new Vector3(-5.75f, 2.5f, -11.5f), b = new Vector3(-5.55f, 1.2f, -7.3f), r = 0.38f });

            // the field on a grid
            const float S = 0.17f; Vector3 o = new Vector3(-HX - 1.2f, -0.25f, -HZ - 1.2f);
            int nx = Mathf.CeilToInt((2 * HX + 2.4f) / S) + 1, ny = Mathf.CeilToInt(4.1f / S) + 1, nz = Mathf.CeilToInt((2 * HZ + 2.4f) / S) + 1;
            var F = new float[nx * ny * nz];
            System.Func<int, int, int, int> I = (x, y, z) => (z * ny + y) * nx + x;
            for (int z = 0; z < nz; z++) for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
                    F[I(x, y, z)] = Air(o + new Vector3(x * S, y * S, z * S));
            // surface nets: a vertex in every cell the surface passes through, at the mean of its edge crossings
            var cellV = new Dictionary<int, int>(); var vs = new List<Vector3>(); var ns = new List<Vector3>();
            int[,] E = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            var cv = new float[8]; var cp = new Vector3[8];
            for (int z = 0; z < nz - 1; z++) for (int y = 0; y < ny - 1; y++) for (int x = 0; x < nx - 1; x++)
                    {
                        int neg = 0;
                        for (int k = 0; k < 8; k++)
                        {
                            int dx = k & 1, dy = (k >> 1) & 1, dz = (k >> 2) & 1;
                            cv[k] = F[I(x + dx, y + dy, z + dz)]; cp[k] = new Vector3(dx, dy, dz); if (cv[k] < 0) neg++;
                        }
                        if (neg == 0 || neg == 8) continue;
                        Vector3 sum = Vector3.zero; int cnt = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            float a = cv[E[e, 0]], b = cv[E[e, 1]]; if ((a < 0) == (b < 0)) continue;
                            sum += Vector3.Lerp(cp[E[e, 0]], cp[E[e, 1]], a / (a - b)); cnt++;
                        }
                        var pw = o + (new Vector3(x, y, z) + sum / cnt) * S;
                        cellV[I(x, y, z)] = vs.Count; vs.Add(pw);
                    }
            // the normals point into the air (the field's gradient)
            const float G = 0.06f;
            foreach (var pw in vs)
                ns.Add(new Vector3(Air(pw + Vector3.right * G) - Air(pw - Vector3.right * G), Air(pw + Vector3.up * G) - Air(pw - Vector3.up * G), Air(pw + Vector3.forward * G) - Air(pw - Vector3.forward * G)).normalized);
            // a quad for every grid edge the surface crosses, joining the four cells around it
            var ts = new List<int>();
            void Quad(int c0, int c1, int c2, int c3)
            {
                int a, b, c, d; if (!cellV.TryGetValue(c0, out a) || !cellV.TryGetValue(c1, out b) || !cellV.TryGetValue(c2, out c) || !cellV.TryGetValue(c3, out d)) return;
                foreach (var tri in new[] { new[] { a, b, c }, new[] { a, c, d } })
                {
                    var nrm = Vector3.Cross(vs[tri[1]] - vs[tri[0]], vs[tri[2]] - vs[tri[0]]);
                    if (Vector3.Dot(nrm, ns[tri[0]] + ns[tri[1]] + ns[tri[2]]) < 0) { ts.Add(tri[0]); ts.Add(tri[2]); ts.Add(tri[1]); }
                    else { ts.Add(tri[0]); ts.Add(tri[1]); ts.Add(tri[2]); }
                }
            }
            for (int z = 1; z < nz - 1; z++) for (int y = 1; y < ny - 1; y++) for (int x = 1; x < nx - 1; x++)
                    {
                        float f0 = F[I(x, y, z)];
                        if ((f0 < 0) != (F[I(x + 1, y, z)] < 0)) Quad(I(x, y - 1, z - 1), I(x, y, z - 1), I(x, y, z), I(x, y - 1, z));
                        if ((f0 < 0) != (F[I(x, y + 1, z)] < 0)) Quad(I(x - 1, y, z - 1), I(x, y, z - 1), I(x, y, z), I(x - 1, y, z));
                        if ((f0 < 0) != (F[I(x, y, z + 1)] < 0)) Quad(I(x - 1, y - 1, z), I(x, y - 1, z), I(x, y, z), I(x - 1, y, z));
                    }
            var m = new Mesh { name = "caverock", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(vs); m.SetNormals(ns); m.uv = new Vector2[vs.Count]; m.SetTriangles(ts, 0); m.RecalculateBounds();
            MeshGen.AddGlyphUV(m, false);
            // cut into 5 x 5 m pieces, so the parts of the rock off the screen are not drawn (nor their characters laid out)
            {
                var uv2 = m.uv2; var rchunks = new Dictionary<long, List<int>>();
                for (int q = 0; q < ts.Count; q += 3)
                {
                    var c = (vs[ts[q]] + vs[ts[q + 1]] + vs[ts[q + 2]]) / 3f;
                    long key = (long)Mathf.FloorToInt(c.x / 5f) * 1000L + Mathf.FloorToInt(c.z / 5f);
                    List<int> l; if (!rchunks.TryGetValue(key, out l)) { l = new List<int>(); rchunks[key] = l; }
                    l.Add(ts[q]); l.Add(ts[q + 1]); l.Add(ts[q + 2]);
                }
                foreach (var kv in rchunks)
                {
                    var rmap = new Dictionary<int, int>(); var rv = new List<Vector3>(); var rn = new List<Vector3>(); var ru = new List<Vector2>(); var rt = new List<int>();
                    foreach (int v in kv.Value)
                    {
                        int j; if (!rmap.TryGetValue(v, out j)) { j = rv.Count; rmap[v] = j; rv.Add(vs[v]); rn.Add(ns[v]); ru.Add(uv2[v]); }
                        rt.Add(j);
                    }
                    var rm2 = new Mesh { name = "caverock", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    rm2.SetVertices(rv); rm2.SetNormals(rn); rm2.uv = new Vector2[rv.Count]; rm2.SetUVs(1, ru); rm2.SetTriangles(rt, 0); rm2.RecalculateBounds();
                    MeshGen.Obj(rm2, mat, T, Vector3.zero, Quaternion.identity, true, "Rock");
                }
            }
            Debug.Log("Пещера: скала " + vs.Count + " вершин, " + ts.Count / 3 + " треугольников");

            // collisions: circles along the outline and the dividing walls, each as big as the rock reaches into the hall
            System.Func<Vector2, Vector2, float> reach = (q, inward) =>
            {
                for (float d = 0; d < 1.4f; d += 0.05f) if (Air(new Vector3(q.x + inward.x * d, 0.35f, q.y + inward.y * d)) > 0.02f) return d;
                return 1.4f;
            };
            void Edge(Vector2 a, Vector2 b, Vector2 inward, float h)
            {
                var ab = b - a; float L = ab.magnitude; var dir = ab / L;
                for (float t2 = 0; t2 <= L + 0.01f; t2 += 0.45f)
                {
                    var q = a + dir * t2; float r = reach(q, inward);
                    if (Air(new Vector3(q.x, 0.35f, q.y)) > 0.05f) continue;           // open here (the mouth)
                    colliders.Add(Collider2.Circle(q.x, q.y, Mathf.Max(0.35f, r + 0.12f)));
                    if (Rnd() < 0.18f) { var mp = q + inward * (r + 0.15f); if (WaterK(mp.x, mp.y) > 1.2f) Moss(T, mats, mp.x, mp.y, Rr(0.3f, 0.55f), 0.01f); }
                }
                wallSegs.Add(new WallSeg { a = a, b = b, h = h });
            }
            Edge(new Vector2(-HX, -HZ), new Vector2(HX, -HZ), Vector2.up, 2.8f);
            Edge(new Vector2(-HX, HZ), new Vector2(HX, HZ), Vector2.down, 1.9f);
            Edge(new Vector2(-HX, -HZ), new Vector2(-HX, HZ), Vector2.right, 2.8f);
            Edge(new Vector2(HX, -HZ), new Vector2(HX, HZ), Vector2.left, 2.8f);
            foreach (var sg in rockSegs)
            {
                var a = sg.a; var b = sg.b; a.x = Mathf.Clamp(a.x, -HX, HX); b.x = Mathf.Clamp(b.x, -HX, HX); a.y = Mathf.Clamp(a.y, -HZ, HZ); b.y = Mathf.Clamp(b.y, -HZ, HZ);
                var dir = (b - a).normalized; var side = new Vector2(-dir.y, dir.x);
                Edge(a, b, side, sg.h);
            }
        }
        /// a skeleton walled into the rock: its back sunk into the wall at (x, z) on the wall line; 'face' points into the room
        void WallSkeleton(Transform T, Mats mats, string pose, Vector2 at, Vector2 face, float s, float tilt)
        {
            var side = new Vector2(-face.y, face.x); var hits = new List<float>();
            foreach (float y in new[] { 0.35f, 0.7f, 1.0f })
                foreach (float l in new[] { -0.25f, 0f, 0.25f })
                {
                    var o2 = at + face * 1.6f + side * l; float d = RayToWall(new Vector3(o2.x, y, o2.y), new Vector3(-face.x, 0, -face.y), 3f);
                    if (d < 8) hits.Add(d);
                }
            float dd = 1.6f;
            if (hits.Count > 0) { hits.Sort(); dd = hits[hits.Count / 2]; }
            var p = at + face * (1.6f - dd + 0.07f);         // the back a few centimetres inside the rock
            float yaw = Mathf.Atan2(face.x, face.y) * Mathf.Rad2Deg;
            MeshGen.Obj(CaveMesh(pose, Vector3.one * s), mats["BONE"], T, new Vector3(p.x, -0.02f, p.y), Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(-tilt, 0, 0), false, "WallSkeleton");
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
        static readonly string[] BONES = { "b_femur", "b_humerus", "b_tibia", "b_rib", "b_rib", "b_pelvis", "b_jaw" };
        void Bone(Transform T, Mats mats, float x, float z, float len)
        {
            // a real bone (from the same anatomical skeleton), lying on its flattest side
            MeshGen.Obj(CaveMesh(BONES[R.Next(BONES.Length)], Vector3.one * Rr(0.9f, 1.05f)), mats["BONE"], T, new Vector3(x, -0.005f, z), Quaternion.Euler(0, Rr(0, 360), 0), false, "LooseBone");
        }
        void Torch(Transform T, Mats mats, float x, float z, bool leaning)
        {
            var t = new GameObject("OldTorch").transform; t.SetParent(T, false); t.localPosition = new Vector3(x, 0.05f, z);
            t.localRotation = Quaternion.Euler(0, Rr(0, 360), 0) * Quaternion.Euler(leaning ? Rr(55, 70) : 86, 0, 0);
            var k = Vector3.one * 0.55f;       // (an iron basket torch, 1 m: smaller here)
            MeshGen.Obj(CaveMesh("torch_stick", k), mats["WOOD"], t, new Vector3(0, 0.22f, 0), Quaternion.identity, true, "TorchStick");
            MeshGen.Obj(CaveMesh("torch_head", k), mats["DARK"], t, new Vector3(0, 0.22f, 0), Quaternion.identity, true, "TorchHead");      // burnt out
        }
        void Pickaxe(Transform T, Mats mats, float x, float z)
        {
            var t = new GameObject("Pickaxe").transform; t.SetParent(T, false); t.localPosition = new Vector3(x, 0.03f, z);
            t.localRotation = Quaternion.Euler(0, Rr(0, 360), 0) * Quaternion.Euler(88, 0, 0);
            var k = Vector3.one * 0.8f;        // (a scanned pickaxe, 1.1 m: a bit smaller here)
            MeshGen.Obj(CaveMesh("pick_handle", k), mats["WOOD"], t, Vector3.zero, Quaternion.identity, true, "Handle");
            MeshGen.Obj(CaveMesh("pick_head", k), mats["STEEL"], t, Vector3.zero, Quaternion.identity, true, "Pick");
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
            // the shaft itself: faint streaks along the beam (characters picked by its direction on the screen),
            // laid on a jittered grid across it so it reads as a column of light, brighter in the middle
            var sp = new List<Vector3>(); var ss = new List<Vector2>(); float len = d.magnitude;
            float step = Mathf.Clamp(radius * 0.32f, 0.13f, 0.22f);
            for (float y = step * 0.5f; y < len; y += step * 1.55f)
                for (float x = -radius; x <= radius; x += step)
                    for (float z = -radius; z <= radius; z += step)
                    {
                        float jx = x + Rr(-0.35f, 0.35f) * step, jz = z + Rr(-0.35f, 0.35f) * step, r = Mathf.Sqrt(jx * jx + jz * jz) / radius;
                        if (r > 1) continue;
                        sp.Add(new Vector3(jx / radius, (y + Rr(-0.4f, 0.4f) * step) / len, jz / radius)); ss.Add(new Vector2(r, Rnd()));
                    }
            GlyphLayer.MakeMotes(T, to, Quaternion.FromToRotation(Vector3.up, d.normalized), new Vector3(radius, len, radius), sp, ss, 3, c * 0.9f, new Vector4(0.075f, 0, 0, 0));
            // and the volume of the beam: an additive cone in the lit colour (AsciiHero/Beam)
            var bsh = Shader.Find("AsciiHero/Beam");
            if (bsh != null)
            {
                const int SEG = 28; float rTop = radius * 0.5f, rBot = radius * 1.08f;
                var cv = new List<Vector3>(); var cn = new List<Vector3>(); var cu = new List<Vector2>(); var ct = new List<int>();
                for (int k = 0; k <= SEG; k++)
                    for (int e = 0; e <= 6; e++)
                    {
                        float a = k * Mathf.PI * 2 / SEG, v = e / 6f, rr = Mathf.Lerp(rBot, rTop, v);
                        cv.Add(new Vector3(Mathf.Cos(a) * rr, v * len, Mathf.Sin(a) * rr)); cn.Add(new Vector3(Mathf.Cos(a), (rBot - rTop) / len, Mathf.Sin(a)).normalized); cu.Add(new Vector2(k / (float)SEG, v));
                    }
                for (int k = 0; k < SEG; k++)
                    for (int e = 0; e < 6; e++) { int o = k * 7 + e; ct.Add(o); ct.Add(o + 1); ct.Add(o + 7); ct.Add(o + 1); ct.Add(o + 8); ct.Add(o + 7); }
                var cm = new Mesh { name = "beam" }; cm.SetVertices(cv); cm.SetNormals(cn); cm.SetUVs(0, cu); cm.SetTriangles(ct, 0); cm.RecalculateBounds();
                var bm = new Material(bsh); bm.SetColor("_BeamColor", c * (0.05f * intensity));
                var cg = new GameObject("BeamCone"); cg.transform.SetParent(T, false); cg.transform.localPosition = to; cg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
                cg.AddComponent<MeshFilter>().sharedMesh = cm; var cr = cg.AddComponent<MeshRenderer>(); cr.sharedMaterial = bm;
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; cr.receiveShadows = false;
            }
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
                    float m0 = Mathf.Floor(t / DRIP_PERIOD + d.z);
                    for (int q = -1; q <= 1; q++)
                    {
                        float m = m0 + q, h = Frac(m * 0.1031f + d.z * 0.6180f); h *= h + 33.33f; h *= h + h; h = Frac(h);
                        float land = (m - d.z) * DRIP_PERIOD + (h - 0.5f) * 1.5f * DRIP_PERIOD / 3.5f + DRIP_FALL;
                        if (lastT < land && t >= land) addRipple(d.x, d.y, 0.85f);
                    }
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
            Shader.SetGlobalVector("_SeeHero", new Vector4(focus.x + origin.x, focus.y + 1.0f, focus.z + origin.z, 0.2f));
            Shader.SetGlobalFloat("_SeeFade", Mathf.SmoothStep(0f, 1f, seeFade));
        }
    }
}
