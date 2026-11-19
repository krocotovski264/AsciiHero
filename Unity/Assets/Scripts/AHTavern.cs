// First location: the tavern «Пьяный дракон». Procedural geometry, textures, lights and colliders.
// Layout coordinates are written exactly as in the web build (right-handed) and mirrored on X when placed.
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public struct Collider2
    {
        public bool circle; public float x, z, r, minx, maxx, minz, maxz;
        public static Collider2 Circle(float x, float z, float r) { return new Collider2 { circle = true, x = x, z = z, r = r }; }
        public static Collider2 Box(float minx, float maxx, float minz, float maxz) { return new Collider2 { minx = minx, maxx = maxx, minz = minz, maxz = maxz }; }
    }

    /// tiny CPU "canvas" for procedural textures
    public class Canvas2D
    {
        public readonly int W, H; readonly Color[] px;
        public Canvas2D(int w, int h, Color bg) { W = w; H = h; px = new Color[w * h]; for (int i = 0; i < px.Length; i++) px[i] = bg; }
        public void Fill(float x, float y, float w, float h, Color c, float a = 1f)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(x)), x1 = Mathf.Min(W, Mathf.CeilToInt(x + w));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(y)), y1 = Mathf.Min(H, Mathf.CeilToInt(y + h));
            for (int yy = y0; yy < y1; yy++) for (int xx = x0; xx < x1; xx++) { int i = (H - 1 - yy) * W + xx; px[i] = Color.Lerp(px[i], c, a); }
        }
        public void Disc(float cx, float cy, float r, System.Func<float, Color> col)
        {
            for (int yy = Mathf.Max(0, (int)(cy - r)); yy < Mathf.Min(H, (int)(cy + r) + 1); yy++)
                for (int xx = Mathf.Max(0, (int)(cx - r)); xx < Mathf.Min(W, (int)(cx + r) + 1); xx++)
                { float d = Mathf.Sqrt((xx - cx) * (xx - cx) + (yy - cy) * (yy - cy)); if (d <= r) px[(H - 1 - yy) * W + xx] = col(d / r); }
        }
        public void Ring(float cx, float cy, float r, float lw, Color c)
        {
            for (int yy = 0; yy < H; yy++) for (int xx = 0; xx < W; xx++)
                { float d = Mathf.Sqrt((xx - cx) * (xx - cx) + (yy - cy) * (yy - cy)); if (Mathf.Abs(d - r) <= lw / 2) px[(H - 1 - yy) * W + xx] = c; }
        }
        public void Line(float x0, float y0, float x1, float y1, float lw, Color c)
        {
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)));
            for (int s = 0; s <= steps; s++) { float t = s / (float)Mathf.Max(1, steps); Fill(Mathf.Lerp(x0, x1, t) - lw / 2, Mathf.Lerp(y0, y1, t) - lw / 2, lw, lw, c); }
        }
        public Texture2D Texture(bool repeat)
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, true, false);
            t.SetPixels(px); t.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; t.filterMode = FilterMode.Trilinear; t.Apply(true, true);
            return t;
        }
        public static Color RGB(float r, float g, float b) { return new Color(r / 255f, g / 255f, b / 255f, 1); }
    }

    /// a place the hero can walk in: geometry, circle/box colliders and bounds
    public class Location
    {
        public GameObject root;
        public List<Collider2> colliders = new List<Collider2>();
        public Rect bounds;  // xMin..xMax = x range, yMin..yMax = z range (Unity coordinates)
        public Vector3 spawn;
        public virtual void Update(float t) { }
        public virtual void Cutaway(Vector3 camOff) { }
        // water, mud and the like (the lake of the field, the pools and the stream of the cave)
        public Vector3 focus;                                        // where the hero is (for cutaways)
        public System.Action<float, float, float> addRipple;         // x, z, strength: a ring on the water
        public virtual bool HasWater => false;
        /// is any of its water within this piece of the world's ground (x, z)? (the mirror is drawn only then: it renders
        /// the scene a second time)
        public virtual bool WaterIn(Rect world) { return HasWater; }
        /// the boxes (world) of the water worth a mirror (lakes; not puddles or swamp murk); null = all of it, anywhere
        public virtual List<Bounds> MirrorWater() { return null; }
        public virtual bool InWater(float x, float z) { return false; }
        /// how deep the hero sinks here (m)
        public virtual float WaterSink(float x, float z) { return Mathf.Min(Depth(x, z), WaterDepth.WAIST); }
        /// how deep the water is here (m; 0 = dry) - see WaterDepth for how each kind of water is shaped
        public virtual float Depth(float x, float z) { return 0; }
        public float DepthW(Vector3 w) { return FrozenW(w) ? 0 : Depth(w.x - origin.x, w.z - origin.z); }
        // ---- winter: the open water freezes from the shore in (iceK 0 .. 1 = frozen over); on the ice it is dry ground ----
        public float iceK;
        /// where the water is between its middle (0) and its shore (1), for water that can freeze; -1 = none here
        public virtual float ShoreK(float x, float z) { return -1; }
        public bool FrozenW(Vector3 w) { if (iceK <= 0) return false; float k = ShoreK(w.x - origin.x, w.z - origin.z); return k >= 0 && k > 1 - iceK - 1e-3f; }
        /// the most of his normal pace he can go here (1 = no limit)
        public virtual float SpeedK(float x, float z) { return 1; } public virtual float WaterY => 0.04f;            // the height of the water surface (the mirror's plane)
        public void SetActive(bool on) { root.SetActive(on); }
        public bool Active => root != null && root.activeSelf;

        // ---- the open world: the outdoor locations stand side by side in one world (the field at the origin, the forest
        // west of it, the cave north of the forest); each keeps its own coordinates and is moved to its 'origin' ----
        public Vector3 origin;
        public string id = "";
        /// the location's ground in world coordinates (x range, z range), with the strips it shares with its neighbours
        public Rect WorldRect => new Rect(bounds.xMin + origin.x, bounds.yMin + origin.z, bounds.width, bounds.height);
        public void PlaceAt(Vector3 o) { origin = o; root.transform.position = o; }
        public Vector2 L(Vector2 w) { return new Vector2(w.x - origin.x, w.y - origin.z); }
        public Vector2 CollideW(Vector2 w, float r) { var q = Collide(L(w), r); return new Vector2(q.x + origin.x, q.y + origin.z); }
        /// only the colliders (no clamping to the bounds): a neighbour's trees and rocks near the seam
        public Vector2 CollideNoClampW(Vector2 w, float r) { var q = CollideRaw(L(w), r); return new Vector2(q.x + origin.x, q.y + origin.z); }
        public bool InWaterW(Vector3 w) { return !FrozenW(w) && InWater(w.x - origin.x, w.z - origin.z); }
        public float WaterSinkW(Vector3 w) { return FrozenW(w) ? 0 : WaterSink(w.x - origin.x, w.z - origin.z); }
        public float SpeedKW(Vector3 w) { return FrozenW(w) ? 1 : SpeedK(w.x - origin.x, w.z - origin.z); }
        public Vector3 SpawnW => spawn + origin;
        /// the light of the place: ambient (sky, horizon, ground), the moon's share, the hero's lantern, exposure
        public Color ambSky = new Color(0.3f, 0.35f, 0.45f), ambEq = new Color(0.2f, 0.22f, 0.22f), ambGround = new Color(0.08f, 0.1f, 0.06f);
        public float moonK = 1, heroLightK = 1.6f, exposure = 1.3f;
        /// the armour lying about (piles); regenerated in new places every time the hero comes to the world
        public readonly List<ArmourPile> piles = new List<ArmourPile>();
        /// the trees' crowns (leaf clouds and their shadows): the forest hides them while the hero is inside it
        public readonly List<GameObject> crowns = new List<GameObject>(); bool crownsOn = true;
        public void ShowCrowns(bool on) { if (on == crownsOn) return; crownsOn = on; foreach (var c in crowns) if (c != null) c.SetActive(on); }
        /// tall things the hero can be hidden behind (x, z, radius, height): they show through around him (see-through)
        public readonly List<Vector4> occluders = new List<Vector4>();
        protected float seeFadeL;
        /// default see-through: is a tree crown (an occluder) between the camera and the hero?
        protected void OccluderSeeThrough(Vector3 camOff)
        {
            bool hidden = false; var c2 = new Vector2(camOff.x, camOff.z); float hz = c2.magnitude;
            if (hz > 1e-3f)
            {
                c2 /= hz; float tanP = camOff.y / hz;
                foreach (var o in occluders)
                {
                    var d = new Vector2(o.x - focus.x, o.y - focus.z); float along = Vector2.Dot(d, c2), across = Mathf.Abs(d.x * c2.y - d.y * c2.x);
                    if (along > -0.3f && along < 5f && across < o.z + 0.6f && o.w > 0.6f + along * tanP) { hidden = true; break; }
                }
            }
            seeFadeL = Mathf.MoveTowards(seeFadeL, hidden ? 1f : 0f, Time.deltaTime * (hidden ? 1.6f : 2.2f));
            var w = focus + origin;
            Shader.SetGlobalVector("_SeeHero", new Vector4(w.x, w.y + 1.0f, w.z, 0.2f));
            Shader.SetGlobalFloat("_SeeFade", Mathf.SmoothStep(0f, 1f, seeFadeL));
        }
        public Vector2 Collide(Vector2 p, float r)
        {
            p = CollideRaw(p, r);
            p.x = Mathf.Clamp(p.x, bounds.xMin, bounds.xMax); p.y = Mathf.Clamp(p.y, bounds.yMin, bounds.yMax);
            return p;
        }
        /// a place's own collisions on top of its colliders (the story's places: the cells of their grid that are not ground)
        protected virtual Vector2 ExtraCollide(Vector2 p, float r) { return p; }
        public Vector2 CollideRaw(Vector2 p, float r)
        {
            p = ExtraCollide(p, r);
            for (int it = 0; it < 2; it++)
                foreach (var c in colliders)
                {
                    if (c.circle)
                    {
                        float dx = p.x - c.x, dz = p.y - c.z, d = Mathf.Sqrt(dx * dx + dz * dz), mm = c.r + r;
                        if (d < mm && d > 1e-6f) { p.x = c.x + dx / d * mm; p.y = c.z + dz / d * mm; }
                    }
                    else
                    {
                        float cx = Mathf.Clamp(p.x, c.minx, c.maxx), cz = Mathf.Clamp(p.y, c.minz, c.maxz);
                        float dx = p.x - cx, dz = p.y - cz, d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (d < r)
                        {
                            if (d > 1e-6f) { p.x = cx + dx / d * r; p.y = cz + dz / d * r; }
                            else
                            {
                                float[] o = { p.x - c.minx, c.maxx - p.x, p.y - c.minz, c.maxz - p.y };
                                int k = 0; for (int i = 1; i < 4; i++) if (o[i] < o[k]) k = i;
                                if (k == 0) p.x = c.minx - r; else if (k == 1) p.x = c.maxx + r; else if (k == 2) p.y = c.minz - r; else p.y = c.maxz + r;
                            }
                        }
                    }
                }
            return p;
        }
    }

    /// The depth of the water, by kind of water. u: how far in from the shore, 0 at the waterline .. 1 at the deepest
    /// point (the centre of a lake or a pool, the middle of a river's channel). The hero wades to the waist (WAIST) and no
    /// deeper; the speed falls with the depth (WadeK).
    ///   puddle  3-8 cm, a dish: no slowing at all
    ///   stream  10-30 cm, a flat bed with a soft edge: a slower run at most
    ///   river   a shallow bank 20-40 cm, then the channel falls to 1-2 m (across only at a ford, 30-50 cm)
    ///   lake    a shelf (to 40 cm over the first third), then a drop-off to 1.5-4 m in the middle
    ///   sea     a beach rising 2 cm per metre for the first 10 m, then deep
    ///   swamp   20-60 cm of water over mud, with bog holes over a metre deep
    public static class WaterDepth
    {
        public const float WAIST = 0.95f, MID_THIGH = 0.5f;
        static float S(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        public static float Puddle(float u, float max = 0.06f) { return max * S(0, 0.6f, u); }
        public static float Stream(float u, float max = 0.22f) { return max * S(0, 0.35f, u); }
        public static float River(float u, float bank = 0.3f, float channel = 1.6f) { return bank * S(0, 0.25f, u) + (channel - bank) * S(0.35f, 0.75f, u); }
        public static float Lake(float u, float shelf = 0.4f, float deep = 2.2f) { return shelf * S(0, 0.35f, u) + (deep - shelf) * S(0.4f, 0.85f, u); }
        public static float Sea(float metresFromShore) { return metresFromShore < 10 ? 0.02f * metresFromShore : 0.2f + (metresFromShore - 10) * 0.15f; }
        public static float Swamp(float u, float hole = 0) { return 0.2f + 0.3f * S(0, 0.5f, u) + hole; }
        /// the share of the full run speed the water allows: to 5 cm all of it, 5-15 cm down to 90 %, then 10 % less for
        /// every 5 cm (it never goes below the walk: the game keeps the walk speed as the floor)
        public static float WadeK(float d)
        {
            if (d <= 0.05f) return 1f;
            if (d <= 0.15f) return 1f - 0.1f * (d - 0.05f) / 0.1f;
            return Mathf.Max(0f, 0.9f - 0.1f * (d - 0.15f) / 0.05f);
        }
    }

    public class Tavern : Location
    {
        public Vector3 bartender;
        class Wall { public GameObject tall, low; public Vector3 n; }
        readonly Dictionary<string, Wall> walls = new Dictionary<string, Wall>();
        class Flick { public Light l; public float baseI; public int kind; }
        readonly List<Flick> lights = new List<Flick>();
        class Flame { public Transform t; public Vector3 basePos; public int k; }
        readonly List<Flame> flames = new List<Flame>();
        Transform cur; Mats mats;

        static Vector3 MX(float x, float y, float z) { return new Vector3(-x, y, z); }
        // three.js Euler XYZ, mirrored on X: rotations about Y and Z change sign
        static Quaternion Rot(float rx, float ry, float rz)
        {
            return Quaternion.AngleAxis(rx * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(-ry * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(-rz * Mathf.Rad2Deg, Vector3.forward);
        }
        GameObject M(Mesh m, Material mat, float x, float y, float z, float rx = 0, float ry = 0, float rz = 0, bool cast = true)
        {
            return MeshGen.Obj(m, mat, cur, MX(x, y, z), Rot(rx, ry, rz), cast);
        }
        GameObject B(float w, float h, float d, Material mat, float x, float y, float z, float rx = 0, float ry = 0, float rz = 0, bool cast = true)
        { return M(MeshGen.Box(w, h, d), mat, x, y + h / 2, z, rx, ry, rz, cast); }
        GameObject C(float r0, float r1, float h, Material mat, float x, float y, float z, int seg = 14, bool cast = true)
        { return M(MeshGen.Cylinder(r0, r1, h, seg), mat, x, y + h / 2, z, 0, 0, 0, cast); }
        void BoxCol(float x, float z, float w, float d) { colliders.Add(Collider2.Box(-(x + w / 2), -(x - w / 2), z - d / 2, z + d / 2)); }
        void CircCol(float x, float z, float r) { colliders.Add(Collider2.Circle(-x, z, r)); }
        Light PointLight(Color c, float intensity, float range, float x, float y, float z, int kind)
        {
            var go = new GameObject("Light"); go.transform.SetParent(root.transform, false); go.transform.localPosition = MX(x, y, z);
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            lights.Add(new Flick { l = l, baseI = intensity, kind = kind });
            return l;
        }

        static Texture2D PlanksTexture()
        {
            int W = 512, H = 512, rows = 8; var c = new Canvas2D(W, H, Color.black);
            for (int r = 0; r < rows; r++)
            {
                float y = r * H / (float)rows; int xx = -((r * 137) % 200);
                while (xx < W)
                {
                    int len = 180 + (((r * 31 + xx) % 140) + 140) % 140;
                    int v = 150 + (((r * 53 + xx * 7) % 60) + 60) % 60;
                    c.Fill(xx, y, len, H / (float)rows, Canvas2D.RGB(v, (int)(v * 0.78f), (int)(v * 0.55f)));
                    c.Fill(xx, y, 3, H / (float)rows, Color.black, 0.75f);
                    for (int k = 0; k < 6; k++) c.Fill(xx + 8, y + 6 + k * 9 + (k * 3) % 5, len - 16, 1, Canvas2D.RGB(60, 30, 10), 0.25f);
                    xx += len;
                }
                c.Fill(0, y, W, 4, Color.black, 0.85f);
            }
            return c.Texture(true);
        }
        static Texture2D StoneTexture()
        {
            int S = 256, rows = 6; var c = new Canvas2D(S, S, Canvas2D.RGB(34, 34, 34));
            for (int r = 0; r < rows; r++)
            {
                int xx = (r % 2) * -30;
                while (xx < S)
                {
                    int w = 50 + (((r * 17 + xx) % 30) + 30) % 30, v = 120 + (((r * 41 + xx * 3) % 70) + 70) % 70;
                    c.Fill(xx + 3, r * S / (float)rows + 3, w - 6, S / (float)rows - 6, Canvas2D.RGB(v, (int)(v * 0.95f), (int)(v * 0.88f)));
                    xx += w;
                }
            }
            return c.Texture(true);
        }
        static Mesh FlatQuad(float w, float d)
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-w / 2, 0, -d / 2), new Vector3(-w / 2, 0, d / 2), new Vector3(w / 2, 0, d / 2), new Vector3(w / 2, 0, -d / 2) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 }; m.RecalculateNormals(); m.RecalculateBounds(); MeshGen.AddGlyphUV(m, false); return m;
        }
        static Mesh Barrel(float r, float h)
        {
            var pts = new List<Vector2> { new Vector2(0, -h / 2) };
            for (int k = 0; k <= 10; k++) { float t = k / 10f; pts.Add(new Vector2(r * (0.86f + 0.14f * Mathf.Sin(t * Mathf.PI)), (t - 0.5f) * h)); }
            pts.Add(new Vector2(0, h / 2));
            return MeshGen.Lathe(pts, 18);
        }
        void Mug(float x, float y, float z)
        {
            M(MeshGen.Cylinder(0.04f, 0.045f, 0.11f, 10), mats["WOOD"], x, y + 0.055f, z);
            M(MeshGen.Torus(0.03f, 0.008f, 10, 5, Mathf.PI), mats["WOOD"], x + 0.045f, y + 0.06f, z, 0, 0, -Mathf.PI / 2);
            M(MeshGen.Cylinder(0.042f, 0.042f, 0.015f, 10), mats["LINEN"], x, y + 0.11f, z);
        }
        // box UVs are 0..1 per face; tile planks a little
        static Mesh BoxUV(float w, float h, float d) { return MeshGen.Box(w, h, d); }

        public static Tavern Build(Mats mats)
        {
            var T = new Tavern { mats = mats };
            T.Construct();
            return T;
        }

        void Construct()
        {
            root = new GameObject("Tavern"); cur = root.transform;
            var planksTex = PlanksTexture(); var stoneTex = StoneTexture();
            var plank = mats.WithTexture("PLANK", planksTex, new Vector2(3, 3)); plank.SetColor("_Color", Mats.Hex(0x8a7058));
            var stoneM = mats.WithTexture("HEARTH", stoneTex, new Vector2(2, 2));
            var floorM = mats.WithTexture("FLOOR", planksTex, new Vector2(3, 3)); floorM.SetColor("_Color", Mats.Hex(0x8a7864));
            Material m(string k) { return mats[k]; }
            floorM.SetFloat("_AsciiFlat", 1);
            // the walls' own plaster and beams (flagged: walls and floors get no outline)
            var wallP = new Material(m("PLASTER")); wallP.SetFloat("_AsciiFlat", 1); var wallB = new Material(m("BEAM")); wallB.SetFloat("_AsciiFlat", 1);

            const float RX = 5, RZ = 4, RH = 3.2f;
            M(FlatQuad(RX * 2, RZ * 2), floorM, 0, 0, 0, 0, 0, 0, false);

            Wall MkWall(string key, Vector3 outwardWeb)
            {
                var w = new Wall { tall = new GameObject("Wall" + key), low = new GameObject("WallLow" + key), n = new Vector3(-outwardWeb.x, 0, outwardWeb.z) };
                w.tall.transform.SetParent(root.transform, false); w.low.transform.SetParent(root.transform, false); w.low.SetActive(false);
                walls[key] = w; return w;
            }
            var WN = MkWall("N", new Vector3(0, 0, -1)); var WS = MkWall("S", new Vector3(0, 0, 1));
            var WW = MkWall("W", new Vector3(-1, 0, 0)); var WE = MkWall("E", new Vector3(1, 0, 0));
            void WallSet(Wall W, bool horiz, float c)
            {
                float sg = Mathf.Sign(c);
                cur = W.tall.transform;
                if (horiz)
                {
                    B(RX * 2 + 0.5f, RH, 0.25f, wallP, 0, 0, c);
                    foreach (float x in new[] { -5f, -2.5f, 0f, 2.5f, 5f }) B(0.22f, RH, 0.12f, wallB, x, 0, c - sg * 0.15f);
                    B(RX * 2, 0.2f, 0.14f, wallB, 0, 2.3f, c - sg * 0.15f); B(RX * 2, 0.18f, 0.14f, wallB, 0, 0, c - sg * 0.15f);
                }
                else
                {
                    B(0.25f, RH, RZ * 2 + 0.5f, wallP, c, 0, 0);
                    foreach (float z in new[] { -4f, -1.5f, 1.5f, 4f }) B(0.12f, RH, 0.22f, wallB, c - sg * 0.15f, 0, z);
                    B(0.14f, 0.2f, RZ * 2, wallB, c - sg * 0.15f, 2.3f, 0);
                }
                cur = W.low.transform;
                if (horiz) { B(RX * 2 + 0.5f, 0.42f, 0.25f, wallP, 0, 0, c); B(RX * 2 + 0.5f, 0.08f, 0.32f, wallB, 0, 0.42f, c); }
                else { B(0.25f, 0.42f, RZ * 2 + 0.5f, wallP, c, 0, 0); B(0.32f, 0.08f, RZ * 2 + 0.5f, wallB, c, 0.42f, 0); }
                cur = root.transform;
            }
            WallSet(WN, true, -RZ - 0.125f); WallSet(WS, true, RZ + 0.125f);
            WallSet(WW, false, -RX - 0.125f); WallSet(WE, false, RX + 0.125f);
            cur = WN.tall.transform;
            foreach (float x in new[] { -2.5f, 0f, 2.5f }) M(MeshGen.Box(0.1f, 0.1f, 1.8f), wallB, x - 0.6f, 1.3f, -RZ + 0.05f, 0, 0, 0.7f);

            // windows (moonlight)
            foreach (float x in new[] { -1.25f, 3.75f })
            {
                B(1.0f, 1.1f, 0.05f, m("MOON"), x, 1.15f, -RZ + 0.01f, 0, 0, 0, false);
                B(1.12f, 0.08f, 0.1f, m("BEAM"), x, 1.1f, -RZ + 0.05f); B(1.12f, 0.08f, 0.1f, m("BEAM"), x, 2.22f, -RZ + 0.05f);
                B(0.08f, 1.12f, 0.1f, m("BEAM"), x - 0.53f, 1.12f, -RZ + 0.05f); B(0.08f, 1.12f, 0.1f, m("BEAM"), x + 0.53f, 1.12f, -RZ + 0.05f);
                B(0.05f, 1.1f, 0.08f, m("BEAM"), x, 1.12f, -RZ + 0.06f); B(1.0f, 0.05f, 0.08f, m("BEAM"), x, 1.65f, -RZ + 0.06f);
            }
            // door
            B(1.0f, 2.05f, 0.08f, m("WOOD"), -3.7f, 0, -RZ + 0.06f);
            for (int k = 0; k < 3; k++) B(1.0f, 0.07f, 0.1f, m("STEEL"), -3.7f, 0.35f + k * 0.65f, -RZ + 0.09f);
            M(MeshGen.Torus(0.06f, 0.012f, 12, 6), m("GOLD"), -3.35f, 1.0f, -RZ + 0.13f);
            B(1.2f, 0.14f, 0.14f, m("BEAM"), -3.7f, 2.05f, -RZ + 0.07f);

            cur = root.transform;
            // ---- bar counter ----
            float bx = 1.3f, bz = -2.35f, bw = 4.4f;
            B(bw, 1.0f, 0.6f, plank, bx, 0, bz);
            B(bw + 0.12f, 0.07f, 0.78f, m("BEAM"), bx, 1.0f, bz);
            for (int k = 0; k <= 6; k++) B(0.08f, 1.0f, 0.05f, m("BEAM"), bx - bw / 2 + k * bw / 6, 0, bz + 0.31f);
            M(MeshGen.Cylinder(0.025f, 0.025f, bw, 8), m("GOLD"), bx, 0.18f, bz + 0.45f, 0, 0, Mathf.PI / 2);
            BoxCol(bx, bz, bw + 0.15f, 0.85f);
            for (int k = 0; k < 4; k++)
            {
                float sx = bx - 1.5f + k * 1.0f, sz = bz + 0.85f;
                C(0.19f, 0.19f, 0.06f, m("WOOD"), sx, 0.66f, sz);
                for (int l = 0; l < 3; l++) { float a = l * 2.094f + k; M(MeshGen.Cylinder(0.03f, 0.022f, 0.68f, 6), m("WOOD"), sx + Mathf.Cos(a) * 0.12f, 0.33f, sz + Mathf.Sin(a) * 0.12f, -Mathf.Sin(a) * 0.12f, 0, Mathf.Cos(a) * 0.12f); }
                CircCol(sx, sz, 0.22f);
            }
            foreach (float kx in new[] { bx - 1.6f, bx + 1.7f })
            {
                M(Barrel(0.22f, 0.5f), m("WOOD"), kx, 1.31f, bz - 0.05f, 0, 0, Mathf.PI / 2);
                foreach (float o in new[] { -0.17f, 0.17f }) M(MeshGen.Torus(0.205f, 0.012f, 18, 6), m("STEEL"), kx + o, 1.31f, bz - 0.05f, 0, Mathf.PI / 2, 0);
                C(0.02f, 0.02f, 0.1f, m("GOLD"), kx + 0.27f, 1.22f, bz - 0.05f);
                B(0.3f, 0.12f, 0.16f, m("BEAM"), kx, 1.07f, bz - 0.05f);
            }
            for (int k = 0; k < 5; k++) Mug(bx - 1.0f + k * 0.45f + (k % 2) * 0.1f, 1.07f, bz + 0.1f - (k % 2) * 0.15f);

            // back shelves with bottles
            cur = WN.tall.transform;
            foreach (float sy in new[] { 1.35f, 1.85f })
            {
                B(4.2f, 0.05f, 0.32f, m("BEAM"), bx, sy, -RZ + 0.2f);
                string[] glass = { "GLASS", "GLASS2", "GLASS", "GLASS3" };
                for (int k = 0; k < 13; k++)
                {
                    float x = bx - 2.0f + k * 0.32f + ((k * 7) % 3) * 0.03f, h = 0.22f + ((k * 13) % 4) * 0.03f;
                    var gm = m(glass[(k + (sy > 1.5f ? 1 : 0)) % 4]);
                    C(0.045f, 0.045f, h, gm, x, sy + 0.05f, -RZ + 0.2f, 8, false);
                    C(0.045f, 0.014f, 0.08f, gm, x, sy + 0.05f + h, -RZ + 0.2f, 8, false);
                }
            }
            foreach (float sx in new[] { bx - 2.1f, bx + 2.1f }) B(0.07f, 1.3f, 0.3f, m("BEAM"), sx, 1.0f, -RZ + 0.2f);

            cur = root.transform;
            float[][] barrels = { new[] { 4.35f, -3.45f, 0f }, new[] { 3.65f, -3.45f, 0f }, new[] { 4.35f, -2.75f, 0f }, new[] { 4.0f, -3.45f, 1f } };
            foreach (var br in barrels)
            {
                float x = br[0], z = br[1]; bool upB = br[2] > 0; float y = upB ? 0.95f : 0;
                M(Barrel(0.32f, 0.9f), m("WOOD"), x, y + 0.45f, z);
                foreach (float o in new[] { -0.3f, 0f, 0.3f }) M(MeshGen.Torus(o == 0 ? 0.335f : 0.305f, 0.014f, 20, 6), m("STEEL"), x, y + 0.45f + o, z, Mathf.PI / 2);
                if (!upB) CircCol(x, z, 0.36f);
            }

            // ---- fireplace (west wall) ----
            float fx = -RX + 0.45f, fz = 0.6f;
            B(0.7f, 1.4f, 0.5f, stoneM, fx, 0, fz - 0.85f); B(0.7f, 1.4f, 0.5f, stoneM, fx, 0, fz + 0.85f);
            cur = WW.tall.transform;
            B(0.8f, 0.55f, 2.3f, stoneM, fx, 1.4f, fz);
            B(0.95f, 0.12f, 2.5f, m("BEAM"), fx + 0.05f, 1.95f, fz);
            B(0.55f, 1.15f, 1.3f, stoneM, fx - 0.05f, 2.05f, fz);
            cur = root.transform;
            B(0.5f, 1.4f, 1.2f, m("DARK"), fx - 0.22f, 0, fz, 0, 0, 0, false);
            B(0.9f, 0.08f, 2.0f, stoneM, fx + 0.1f, 0, fz);
            BoxCol(fx, fz, 0.95f, 2.4f);
            for (int k = 0; k < 3; k++) M(MeshGen.Cylinder(0.07f, 0.07f, 0.75f, 8), m("WOOD"), fx - 0.05f, 0.16f + (k == 2 ? 0.11f : 0), fz + (k - 1) * 0.12f, Mathf.PI / 2, (k - 1) * 0.4f, 0);
            // the fire: characters rising from the logs and fading out (AsciiHero/Fire)
            GlyphLayer.MakeFire(cur, MX(fx - 0.04f, 0.2f, fz), 0.11f, 0.36f, 0.95f, 0.085f, 300);
            PointLight(Mats.Hex(0xff8a30), 3.2f, 8, fx + 0.6f, 0.8f, fz, 0);
            cur = WW.tall.transform;
            foreach (float s in new[] { -1f, 1f })
            {
                var sw = new GameObject("Sword").transform; sw.SetParent(cur, false);
                MeshGen.Obj(MeshGen.Box(0.02f, 0.9f, 0.06f), m("STEEL"), sw, Vector3.zero, Quaternion.identity);
                MeshGen.Obj(MeshGen.Box(0.04f, 0.04f, 0.22f), m("GOLD"), sw, new Vector3(0, -0.33f, 0), Quaternion.identity);
                sw.localPosition = MX(fx + 0.3f, 2.55f, fz); sw.localRotation = Rot(s * 0.7f, 0, 0);
            }
            M(MeshGen.Cylinder(0.28f, 0.28f, 0.04f, 20), m("RED"), fx + 0.33f, 2.55f, fz, 0, 0, Mathf.PI / 2);
            M(MeshGen.Sphere(0.06f, 10, 6), m("STEEL"), fx + 0.36f, 2.55f, fz);

            cur = root.transform;
            // ---- tables ----
            float[][] tables = { new[] { -1.6f, 1.4f }, new[] { 1.6f, 1.2f }, new[] { -2.0f, -1.3f }, new[] { 3.6f, 2.0f } };
            for (int ti = 0; ti < tables.Length; ti++)
            {
                float x = tables[ti][0], z = tables[ti][1];
                C(0.62f, 0.62f, 0.07f, plank, x, 0.72f, z, 20);
                C(0.07f, 0.08f, 0.72f, m("BEAM"), x, 0, z, 8);
                C(0.32f, 0.32f, 0.05f, m("BEAM"), x, 0, z, 12);
                CircCol(x, z, 0.68f);
                for (int k = 0; k < 3; k++)
                {
                    float a = k * 2.094f + ti * 0.7f, sx = x + Mathf.Cos(a) * 0.95f, sz = z + Mathf.Sin(a) * 0.95f;
                    C(0.2f, 0.2f, 0.06f, m("WOOD"), sx, 0.44f, sz);
                    for (int l = 0; l < 3; l++) { float b = l * 2.094f + a; M(MeshGen.Cylinder(0.028f, 0.022f, 0.45f, 6), m("WOOD"), sx + Mathf.Cos(b) * 0.12f, 0.22f, sz + Mathf.Sin(b) * 0.12f); }
                    CircCol(sx, sz, 0.22f);
                }
                Mug(x + 0.2f, 0.79f, z - 0.1f); Mug(x - 0.25f, 0.79f, z + 0.15f);
                C(0.13f, 0.11f, 0.015f, m("PLASTER"), x + 0.05f, 0.79f, z + 0.25f, 16);
                C(0.03f, 0.03f, 0.14f, m("LINEN"), x - 0.05f, 0.79f, z - 0.25f, 8);
                GlyphLayer.MakeFire(cur, MX(x - 0.05f, 0.935f, z - 0.25f), 0.006f, 0.006f, 0.11f, 0.034f, 12, 0.8f);      // the candle's flame
                PointLight(Mats.Hex(0xffb060), 0.9f, 2.2f, x - 0.05f, 1.05f, z - 0.25f, 1);
            }

            // rug
            M(FlatQuad(3.2f, 2.0f), m("GOLD"), 0, 0.004f, 0.25f, 0, 0, 0, false);
            M(FlatQuad(3.0f, 1.8f), m("RED"), 0, 0.007f, 0.25f, 0, 0, 0, false);

            // stairs along the east wall
            for (int k = 0; k < 9; k++) B(0.95f, 0.24f * (k + 1), 0.32f, plank, RX - 0.5f, 0, 3.7f - k * 0.32f);
            B(0.06f, 1.0f, 3.0f, m("BEAM"), RX - 1.0f, 1.1f, 2.4f, 0.62f);
            BoxCol(RX - 0.5f, 2.4f, 1.05f, 2.95f);

            // hanging lanterns
            foreach (var p in new[] { new Vector2(-0.8f, 0.0f), new Vector2(2.2f, -0.4f), new Vector2(-2.6f, 2.6f) })
            {
                C(0.008f, 0.008f, 0.8f, m("STEEL"), p.x, 2.4f, p.y, 4, false);
                GlyphLayer.MakeFire(cur, MX(p.x, 2.19f, p.y), 0.04f, 0.04f, 0.2f, 0.05f, 28, 0.8f);                       // the lantern's flame
                B(0.2f, 0.03f, 0.2f, m("STEEL"), p.x, 2.4f, p.y, 0, 0, 0, false); B(0.2f, 0.03f, 0.2f, m("STEEL"), p.x, 2.15f, p.y, 0, 0, 0, false);
                PointLight(Mats.Hex(0xffa850), 1.6f, 4.5f, p.x, 2.1f, p.y, 2);
            }

            bounds = Rect.MinMaxRect(-(RX - 0.32f), -RZ + 0.35f, RX - 0.32f, RZ - 0.3f);
            spawn = MX(0.6f, 0, 2.8f);
            bartender = MX(bx + 0.2f, 0, -3.25f);
        }

        public override void Update(float t)
        {
            foreach (var L in lights)
            {
                float f = L.kind == 0 ? 0.82f + 0.12f * Mathf.Sin(t * 13.1f) + 0.08f * Mathf.Sin(t * 7.3f + 1) + 0.06f * Mathf.Sin(t * 23.7f)
                    : L.kind == 1 ? 0.9f + 0.1f * Mathf.Sin(t * 17 + L.baseI * 3) : 0.95f + 0.05f * Mathf.Sin(t * 3.1f);
                L.l.intensity = L.baseI * f;
            }
            foreach (var f in flames)
            {
                float s = 0.85f + 0.25f * Mathf.Sin(t * (9 + f.k) + f.k * 1.7f);
                f.t.localScale = new Vector3(1, s, 1);
                f.t.localPosition = f.basePos + new Vector3(-0.012f * Mathf.Sin(t * 11 + f.k), 0, 0);
            }
        }

        /// hide the walls between the camera and the hero (camOff = camera offset from the target, Unity coords)
        public override void Cutaway(Vector3 camOff)
        {
            var d = new Vector3(camOff.x, 0, camOff.z).normalized;
            foreach (var w in walls.Values) { bool near = Vector3.Dot(w.n, d) > 0.25f; w.tall.SetActive(!near); w.low.SetActive(near); }
        }


    }
}
