// The story's places, built from a map drawn in the map editor (Resources/Maps/*.json, format "asciihero-map"):
// chunks of 20 x 20 m in 5 m tiles whose corners are cut with right isosceles triangles, round arenas, walls, exits
// and the things that stand in them. A map in metres: x to the right, y down; in the location x -> -X, y -> Z
// (the camera looks so that the screen's up is -Z and its right is -X, the way the map is drawn).
//   * Every connected run of tavern tiles is a place of its own (an inn, a den), entered through a hole in the
//     cave's rock or a door from the meadow; the rest of the map is one place (the dungeon and what lies round it).
//   * The edge of the walkable ground gets a wall in the style of the ground beside it: rough rock in the caves,
//     a hedge of trees and stones on the meadow, plank walls in the inns; the walls drawn on the map are added.
//   * Collisions, sight lines and paths use a grid of 25 cm cells over the place.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace AsciiHero
{
    /// a tiny JSON reader: objects are Dictionary<string, object>, arrays List<object>, numbers double
    public static class MiniJson
    {
        public static object Parse(string s) { int i = 0; return Val(s, ref i); }
        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        static object Val(string s, ref int i)
        {
            Ws(s, ref i); char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true) { Ws(s, ref i); string k = Str(s, ref i); Ws(s, ref i); i++; d[k] = Val(s, ref i); Ws(s, ref i); if (s[i] == ',') { i++; continue; } i++; return d; }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true) { l.Add(Val(s, ref i)); Ws(s, ref i); if (s[i] == ',') { i++; continue; } i++; return l; }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            int a = i; while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(a, i - a), CultureInfo.InvariantCulture);
        }
        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder(); i++;
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++; char e = s[i];
                    if (e == 'n') sb.Append('\n'); else if (e == 't') sb.Append('\t'); else if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; } else sb.Append(e);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++; return sb.ToString();
        }
        public static float F(object o, float def = 0) { return o is double d ? (float)d : def; }
        public static string S(Dictionary<string, object> d, string k, string def = "") { object o; return d.TryGetValue(k, out o) && o is string s ? s : def; }
        public static float N(Dictionary<string, object> d, string k, float def = 0) { object o; return d.TryGetValue(k, out o) ? F(o, def) : def; }
        public static List<object> L(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o is List<object> l ? l : new List<object>(); }
    }

    public class StoryMap
    {
        public struct Tile { public int i, j; public string b, s; }
        public struct Wall { public Vector2 a, b; }
        public struct Circle { public Vector2 c; public float r; public string b, name; }
        public class Ent { public string id, type, name, note; public Vector2 p; public bool stat; }
        public class Exit { public Rect r; public string to, name; }
        public float chunk = 20, tile = 5;
        public readonly List<Tile> tiles = new List<Tile>();
        public readonly List<Wall> walls = new List<Wall>();
        public readonly List<Circle> circles = new List<Circle>();
        public readonly List<Ent> ents = new List<Ent>();
        public readonly List<Exit> exits = new List<Exit>();
        public readonly List<KeyValuePair<Rect, string>> chunkNames = new List<KeyValuePair<Rect, string>>();
        public string name;

        public static StoryMap Load(string resource)
        {
            var ta = Resources.Load<TextAsset>(resource); if (ta == null) throw new Exception("нет карты " + resource);
            var d = MiniJson.Parse(ta.text) as Dictionary<string, object>;
            var m = new StoryMap { name = MiniJson.S(d, "name"), chunk = MiniJson.N(d, "chunk", 20), tile = MiniJson.N(d, "tile", 5) };
            foreach (List<object> t in MiniJson.L(d, "tiles")) m.tiles.Add(new Tile { i = (int)MiniJson.F(t[0]), j = (int)MiniJson.F(t[1]), b = (string)t[2], s = (string)t[3] });
            foreach (List<object> w in MiniJson.L(d, "walls")) m.walls.Add(new Wall { a = new Vector2(MiniJson.F(w[0]), MiniJson.F(w[1])), b = new Vector2(MiniJson.F(w[2]), MiniJson.F(w[3])) });
            foreach (Dictionary<string, object> c in MiniJson.L(d, "circles")) m.circles.Add(new Circle { c = new Vector2(MiniJson.N(c, "x"), MiniJson.N(c, "y")), r = MiniJson.N(c, "r", 10), b = MiniJson.S(c, "b", "cave"), name = MiniJson.S(c, "name") });
            foreach (Dictionary<string, object> e in MiniJson.L(d, "ents"))
            {
                object st; e.TryGetValue("static", out st);
                m.ents.Add(new Ent { id = MiniJson.S(e, "id"), type = MiniJson.S(e, "type"), name = MiniJson.S(e, "name"), note = MiniJson.S(e, "note"), p = new Vector2(MiniJson.N(e, "x"), MiniJson.N(e, "y")), stat = st is bool b && b });
            }
            foreach (Dictionary<string, object> x in MiniJson.L(d, "exits")) m.exits.Add(new Exit { r = new Rect(MiniJson.N(x, "x"), MiniJson.N(x, "y"), MiniJson.N(x, "w", 1), MiniJson.N(x, "h", 1)), to = MiniJson.S(x, "to", "world"), name = MiniJson.S(x, "name") });
            foreach (Dictionary<string, object> c in MiniJson.L(d, "chunks"))
            {
                string nm = MiniJson.S(c, "name"); if (nm.Length == 0) continue;
                m.chunkNames.Add(new KeyValuePair<Rect, string>(new Rect(MiniJson.N(c, "x") * m.chunk, MiniJson.N(c, "y") * m.chunk, m.chunk, m.chunk), nm));
            }
            return m;
        }
        /// the tile's polygon (map metres)
        public List<Vector2> Poly(Tile t)
        {
            float x = t.i * tile, y = t.j * tile, X = x + tile, Y = y + tile;
            var P = new Dictionary<string, Vector2> { { "NW", new Vector2(x, y) }, { "NE", new Vector2(X, y) }, { "SE", new Vector2(X, Y) }, { "SW", new Vector2(x, Y) } };
            var l = new List<Vector2>();
            if (t.s == "F") { l.Add(P["NW"]); l.Add(P["NE"]); l.Add(P["SE"]); l.Add(P["SW"]); return l; }
            string opp = t.s == "NW" ? "SE" : t.s == "NE" ? "SW" : t.s == "SE" ? "NW" : "NE";
            foreach (var k in new[] { "NW", "NE", "SE", "SW" }) if (k != opp) l.Add(P[k]);
            return l;
        }
        public string ChunkName(Vector2 p) { foreach (var kv in chunkNames) if (kv.Key.Contains(p)) return kv.Value; return null; }
    }

    /// a way between two places: a hole in the rock, a door
    public class StoryPassage { public Vector2 p, n; public string from, to, style; }

    public class StoryLocation : Location
    {
        public string region, title;
        public StoryMap map;
        // the grid (map metres)
        const float CELL = 0.25f;
        float gx0, gy0; int gw, gh; bool[] walk; string[] cellBiome;
        readonly List<StoryMap.Tile> myTiles = new List<StoryMap.Tile>();
        readonly List<StoryMap.Circle> myCircles = new List<StoryMap.Circle>();
        public readonly List<StoryPassage> passages = new List<StoryPassage>();
        System.Random rnd;
        float Rr(float a, float b) { return a + (b - a) * (float)rnd.NextDouble(); }

        // ---- map <-> location coordinates ----
        public static Vector3 L3(Vector2 m, float y = 0) { return new Vector3(-m.x, y, m.y); }
        public Vector2 ToMap(Vector3 world) { return new Vector2(-(world.x - origin.x), world.z - origin.z); }
        public Vector3 ToWorld(Vector2 m, float y = 0) { return L3(m, y) + origin; }

        public bool Walk(Vector2 m)
        {
            int i = Mathf.FloorToInt((m.x - gx0) / CELL), j = Mathf.FloorToInt((m.y - gy0) / CELL);
            return i >= 0 && j >= 0 && i < gw && j < gh && walk[j * gw + i];
        }
        public string BiomeAt(Vector2 m)
        {
            int i = Mathf.FloorToInt((m.x - gx0) / CELL), j = Mathf.FloorToInt((m.y - gy0) / CELL);
            if (i < 0 || j < 0 || i >= gw || j >= gh) return null;
            return cellBiome[j * gw + i];
        }
        public string BiomeAtW(Vector3 w) { return BiomeAt(ToMap(w)); }
        /// is the straight line between two points free of walls (map metres)?
        public bool Sight(Vector2 a, Vector2 b)
        {
            float len = (b - a).magnitude; int n = Mathf.CeilToInt(len / 0.2f);
            for (int k = 1; k < n; k++) if (!Walk(Vector2.Lerp(a, b, k / (float)n))) return false;
            return true;
        }
        public bool SightW(Vector3 a, Vector3 b) { return Sight(ToMap(a), ToMap(b)); }

        // ---- collisions: out of the cells that are not ground ----
        protected override Vector2 ExtraCollide(Vector2 p, float r)
        {
            // p is location-local (X, Z); the grid is in map metres: x = -X, y = Z
            for (int it = 0; it < 2; it++)
            {
                float mx = -p.x, my = p.y;
                int i0 = Mathf.FloorToInt((mx - r - gx0) / CELL), i1 = Mathf.FloorToInt((mx + r - gx0) / CELL);
                int j0 = Mathf.FloorToInt((my - r - gy0) / CELL), j1 = Mathf.FloorToInt((my + r - gy0) / CELL);
                bool moved = false;
                for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++)
                    {
                        bool w = i >= 0 && j >= 0 && i < gw && j < gh && walk[j * gw + i];
                        if (w) continue;
                        float cx0 = gx0 + i * CELL, cy0 = gy0 + j * CELL;
                        float qx = Mathf.Clamp(mx, cx0, cx0 + CELL), qy = Mathf.Clamp(my, cy0, cy0 + CELL);
                        float dx = mx - qx, dy = my - qy, d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d >= r) continue;
                        if (d > 1e-5f) { mx = qx + dx / d * r; my = qy + dy / d * r; }
                        else { float ccx = cx0 + CELL / 2, ccy = cy0 + CELL / 2; var v = new Vector2(mx - ccx, my - ccy); if (v.sqrMagnitude < 1e-8f) v = Vector2.up; v.Normalize(); mx = ccx + v.x * (CELL * 0.71f + r); my = ccy + v.y * (CELL * 0.71f + r); }
                        moved = true;
                    }
                p = new Vector2(-mx, my);
                if (!moved) break;
            }
            return p;
        }

        // ---- paths: breadth-first over 1 m cells (good enough for creatures that lost sight of the hero) ----
        int pw, ph; bool[] pwalk;
        void BuildPathGrid()
        {
            pw = Mathf.CeilToInt(gw * CELL); ph = Mathf.CeilToInt(gh * CELL); pwalk = new bool[pw * ph];
            int k = Mathf.RoundToInt(1f / CELL);
            for (int j = 0; j < ph; j++) for (int i = 0; i < pw; i++)
                {
                    int ok = 0, all = 0;
                    for (int b = 0; b < k; b++) for (int a = 0; a < k; a++) { int ci = i * k + a, cj = j * k + b; if (ci >= gw || cj >= gh) continue; all++; if (walk[cj * gw + ci]) ok++; }
                    pwalk[j * pw + i] = all > 0 && ok * 2 > all;
                }
        }
        /// the next point to walk to from a toward b (map metres), or b itself when it is in sight
        public Vector2 NextStep(Vector2 a, Vector2 b)
        {
            if (Sight(a, b)) return b;
            int ai = Mathf.FloorToInt(a.x - gx0), aj = Mathf.FloorToInt(a.y - gy0), bi = Mathf.FloorToInt(b.x - gx0), bj = Mathf.FloorToInt(b.y - gy0);
            if (ai < 0 || aj < 0 || ai >= pw || aj >= ph || bi < 0 || bj < 0 || bi >= pw || bj >= ph) return b;
            var prev = new int[pw * ph]; for (int q = 0; q < prev.Length; q++) prev[q] = -2;
            var Q = new Queue<int>(); int start = aj * pw + ai, goal = bj * pw + bi; prev[start] = -1; Q.Enqueue(start); int seen = 0;
            while (Q.Count > 0 && seen < 6000)
            {
                int c = Q.Dequeue(); seen++; if (c == goal) break;
                int ci = c % pw, cj = c / pw;
                for (int d = 0; d < 4; d++)
                {
                    int ni = ci + (d == 0 ? 1 : d == 1 ? -1 : 0), nj = cj + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (ni < 0 || nj < 0 || ni >= pw || nj >= ph) continue;
                    int n = nj * pw + ni; if (prev[n] != -2 || !pwalk[n]) continue;
                    prev[n] = c; Q.Enqueue(n);
                }
            }
            if (prev[goal] == -2) return b;
            // walk back to the first cell still in sight of a
            int cur = goal; var path = new List<int>();
            while (cur != -1 && cur != start) { path.Add(cur); cur = prev[cur]; }
            Vector2 best = b;
            for (int q = path.Count - 1; q >= 0; q--)
            {
                var pt = new Vector2(gx0 + path[q] % pw + 0.5f, gy0 + path[q] / pw + 0.5f);
                if (Sight(a, pt)) best = pt; else break;
            }
            return best;
        }

        // ---------------- building ----------------
        public static StoryLocation Build(Mats mats, StoryMap map, string region, HashSet<long> tileSet, bool withCircles, List<StoryPassage> allPassages, Vector3 at, int seed)
        {
            var L = new StoryLocation { map = map, region = region, rnd = new System.Random(seed), id = "story:" + region };
            L.root = new GameObject("Story_" + region);
            foreach (var t in map.tiles) if (tileSet.Contains(Key(t.i, t.j))) L.myTiles.Add(t);
            if (withCircles) L.myCircles.AddRange(map.circles);
            foreach (var p in allPassages) if (p.from == region || p.to == region) L.passages.Add(p);
            L.Grid();
            L.Geometry(mats);
            L.PlaceAt(at);
            return L;
        }
        public static long Key(int i, int j) { return ((long)i << 32) ^ (uint)j; }

        void Grid()
        {
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
            foreach (var t in myTiles) { x0 = Mathf.Min(x0, t.i * map.tile); y0 = Mathf.Min(y0, t.j * map.tile); x1 = Mathf.Max(x1, (t.i + 1) * map.tile); y1 = Mathf.Max(y1, (t.j + 1) * map.tile); }
            foreach (var c in myCircles) { x0 = Mathf.Min(x0, c.c.x - c.r); y0 = Mathf.Min(y0, c.c.y - c.r); x1 = Mathf.Max(x1, c.c.x + c.r); y1 = Mathf.Max(y1, c.c.y + c.r); }
            gx0 = Mathf.Floor(x0) - 3; gy0 = Mathf.Floor(y0) - 3;
            gw = Mathf.CeilToInt((x1 - gx0 + 3) / CELL); gh = Mathf.CeilToInt((y1 - gy0 + 3) / CELL);
            walk = new bool[gw * gh]; cellBiome = new string[gw * gh];
            for (int j = 0; j < gh; j++) for (int i = 0; i < gw; i++)
                {
                    var m = new Vector2(gx0 + (i + 0.5f) * CELL, gy0 + (j + 0.5f) * CELL);
                    string b = TileBiomeAt(m);
                    if (b == null) foreach (var c in myCircles) if ((m - c.c).sqrMagnitude < c.r * c.r) { b = c.b; break; }
                    if (b != null) { walk[j * gw + i] = true; cellBiome[j * gw + i] = b; }
                }
            // the walls drawn on the map block their cells (except where a passage goes through)
            foreach (var w in map.walls)
            {
                var mid = (w.a + w.b) / 2; var dir = (w.b - w.a); float len = dir.magnitude; if (len < 0.1f) continue; dir /= len;
                var nrm = new Vector2(-dir.y, dir.x);
                if (!Walk(mid + nrm * 0.6f) && !Walk(mid - nrm * 0.6f)) continue;
                int n = Mathf.CeilToInt(len / (CELL * 0.5f));
                for (int k = 0; k <= n; k++)
                {
                    var p = Vector2.Lerp(w.a, w.b, k / (float)n); if (InGap(p)) continue;
                    for (float o = -0.3f; o <= 0.3f; o += CELL * 0.5f) { var q = p + nrm * o; int i = Mathf.FloorToInt((q.x - gx0) / CELL), j = Mathf.FloorToInt((q.y - gy0) / CELL); if (i >= 0 && j >= 0 && i < gw && j < gh) walk[j * gw + i] = false; }
                }
            }
            // a bounds rectangle in location coordinates
            bounds = Rect.MinMaxRect(-(gx0 + gw * CELL), gy0, -gx0, gy0 + gh * CELL);
            BuildPathGrid();
        }
        string TileBiomeAt(Vector2 m)
        {
            int i = Mathf.FloorToInt(m.x / map.tile), j = Mathf.FloorToInt(m.y / map.tile);
            foreach (var t in myTiles)
            {
                if (t.i != i || t.j != j) continue;
                if (t.s == "F") return t.b;
                float u = m.x / map.tile - i, v = m.y / map.tile - j;
                bool inside = t.s == "SE" ? u + v >= 1 : t.s == "NW" ? u + v <= 1 : t.s == "NE" ? u >= v : v >= u;
                return inside ? t.b : null;
            }
            return null;
        }
        bool InGap(Vector2 p) { foreach (var g in passages) if ((p - g.p).sqrMagnitude < 0.95f * 0.95f) return true; return false; }

        // ---- the meshes ----
        class MB { public List<Vector3> v = new List<Vector3>(); public List<int> t = new List<int>(); }
        readonly Dictionary<string, MB> blocks = new Dictionary<string, MB>();
        MB Block(string mat, Vector2 m) { string k = mat + ":" + Mathf.FloorToInt(m.x / 10) + ":" + Mathf.FloorToInt(m.y / 10); MB b; if (!blocks.TryGetValue(k, out b)) { b = new MB(); blocks[k] = b; } return b; }
        static void Tri(MB b, Vector3 p0, Vector3 p1, Vector3 p2) { int o = b.v.Count; b.v.Add(p0); b.v.Add(p1); b.v.Add(p2); b.t.Add(o); b.t.Add(o + 1); b.t.Add(o + 2); }
        // a quad facing toward 'face' (the triangles are wound so their normal points that way)
        static void Quad(MB b, Vector3 a, Vector3 bb, Vector3 c, Vector3 d, Vector3 face)
        {
            var n = Vector3.Cross(bb - a, c - a);
            if (Vector3.Dot(n, face) < 0) { Tri(b, a, c, bb); Tri(b, a, d, c); } else { Tri(b, a, bb, c); Tri(b, a, c, d); }
        }
        static string FloorMat(string b) { return b == "cave" ? "CAVEFLOOR" : b == "tavern" ? "FLOOR" : b == "meadow" ? "GROUND" : b == "road" ? "ROAD" : b == "snow" ? "SAND" : b == "wastes" ? "SAND" : "GROUND"; }
        string WallStyle(string b) { return b == "cave" || b == "crystal" ? "rock" : b == "tavern" || b == "city" || b == "grim" ? "plank" : "hedge"; }

        void Geometry(Mats mats)
        {
            var T = root.transform;
            // floors: tiles as they are, a round arena as rings of small cells where no tile is
            foreach (var t in myTiles)
            {
                var poly = map.Poly(t); var mb = Block(FloorMat(t.b), poly[0]);
                for (int k = 1; k + 1 < poly.Count; k++) Tri(mb, L3(poly[0]), L3(poly[k + 1]), L3(poly[k]));
            }
            foreach (var c in myCircles)
            {
                int rings = Mathf.CeilToInt(c.r / 0.5f);
                for (int r = 0; r < rings; r++)
                {
                    float r0 = c.r * r / rings, r1 = c.r * (r + 1) / rings; int seg = Mathf.Max(8, Mathf.CeilToInt(2 * Mathf.PI * r1 / 0.5f));
                    for (int s = 0; s < seg; s++)
                    {
                        float a0 = s * Mathf.PI * 2 / seg, a1 = (s + 1) * Mathf.PI * 2 / seg, am = (a0 + a1) / 2, rm = (r0 + r1) / 2;
                        var mid = c.c + new Vector2(Mathf.Cos(am), Mathf.Sin(am)) * rm;
                        if (TileBiomeAt(mid) != null) continue;
                        Vector2 P(float a, float rr) { return c.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr; }
                        var mb = Block(FloorMat(c.b), mid);
                        Tri(mb, L3(P(a0, r0)), L3(P(a1, r1)), L3(P(a0, r1)));
                        if (r0 > 0) Tri(mb, L3(P(a0, r0)), L3(P(a1, r0)), L3(P(a1, r1)));
                    }
                }
            }
            // the edge of the ground: walls where one side is ground and the other is not
            var edges = new List<KeyValuePair<Vector2, Vector2>>();
            foreach (var t in myTiles) { var p = map.Poly(t); for (int k = 0; k < p.Count; k++) edges.Add(new KeyValuePair<Vector2, Vector2>(p[k], p[(k + 1) % p.Count])); }
            foreach (var c in myCircles) { int n = Mathf.CeilToInt(2 * Mathf.PI * c.r / 1.2f); for (int k = 0; k < n; k++) { float a0 = k * Mathf.PI * 2 / n, a1 = (k + 1) * Mathf.PI * 2 / n; edges.Add(new KeyValuePair<Vector2, Vector2>(c.c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * c.r, c.c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * c.r)); } }
            var done = new HashSet<string>();
            foreach (var e in edges)
            {
                var a = e.Key; var b = e.Value; var d = b - a; float len = d.magnitude; if (len < 0.05f) continue; d /= len;
                string key = Mathf.RoundToInt((a.x + b.x) * 10) + ":" + Mathf.RoundToInt((a.y + b.y) * 10);
                if (done.Contains(key)) continue;
                var nrm = new Vector2(-d.y, d.x); var mid = (a + b) / 2;
                bool s1 = Walk(mid + nrm * 0.35f), s2 = Walk(mid - nrm * 0.35f);
                if (s1 == s2) continue;
                done.Add(key);
                var outN = s1 ? -nrm : nrm; string bio = BiomeAt(mid - outN * 0.35f) ?? "cave";
                WallStrip(mats, a, b, outN, WallStyle(bio), false);
            }
            // the walls drawn on the map
            foreach (var w in map.walls)
            {
                var d = w.b - w.a; float len = d.magnitude; if (len < 0.1f) continue; d /= len;
                var nrm = new Vector2(-d.y, d.x); var mid = (w.a + w.b) / 2;
                string b1 = BiomeAt(mid + nrm * 0.6f), b2 = BiomeAt(mid - nrm * 0.6f);
                if (b1 == null && b2 == null) continue;
                WallStrip(mats, w.a, w.b, nrm, WallStyle(b1 ?? b2), true);
            }
            // the passages: a hole in the rock or a door
            foreach (var g in passages) PassageLook(mats, g);
            // meshes
            foreach (var kv in blocks)
            {
                string matName = kv.Key.Split(':')[0];
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; m.SetVertices(kv.Value.v); m.SetTriangles(kv.Value.t, 0);
                m.RecalculateNormals(); m.RecalculateBounds(); MeshGen.AddGlyphUV(m, false);
                Material mat = matName == "ROCKBIG" ? mats.Big(mats["ROCK"]) : matName == "PLANKW" ? mats["PLASTER"] : mats[matName];
                bool floor = matName == "CAVEFLOOR" || matName == "FLOOR" || matName == "GROUND" || matName == "ROAD" || matName == "SNOW" || matName == "SAND";
                MeshGen.Obj(m, mat, T, Vector3.zero, Quaternion.identity, !floor, floor ? "Ground" : "Wall");
            }
            Decor(mats);
        }
        /// a wall along a - b; outN points away from the ground it stands by (a wall drawn on the map: both sides)
        void WallStrip(Mats mats, Vector2 a, Vector2 b, Vector2 outN, string style, bool both)
        {
            float len = (b - a).magnitude; int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.6f));
            if (style == "rock")
            {
                float th = both ? 0.35f : 0.9f;
                for (int k = 0; k < n; k++)
                {
                    var p0 = Vector2.Lerp(a, b, k / (float)n); var p1 = Vector2.Lerp(a, b, (k + 1f) / n);
                    var pm = (p0 + p1) / 2; if (InGap(pm)) continue;
                    float j0 = Noise(p0), j1 = Noise(p1), h0 = 2.0f + 0.9f * Noise(p0 * 0.7f + Vector2.one * 9), h1 = 2.0f + 0.9f * Noise(p1 * 0.7f + Vector2.one * 9);
                    var in0 = p0 - outN * (both ? th : 0.02f + 0.18f * j0); var in1 = p1 - outN * (both ? th : 0.02f + 0.18f * j1);
                    var o0 = p0 + outN * (th + 0.25f * j0); var o1 = p1 + outN * (th + 0.25f * j1);
                    var mb = Block("ROCKBIG", pm); var face = L3(-outN) - L3(Vector2.zero);
                    // the face toward the ground leans back a little as it rises
                    var t0 = in0 + outN * 0.25f; var t1 = in1 + outN * 0.25f;
                    Quad(mb, L3(in0), L3(in1), L3(t1, h1), L3(t0, h0), face);
                    Quad(mb, L3(t0, h0), L3(t1, h1), L3(o1, h1 * 0.9f), L3(o0, h0 * 0.9f), Vector3.up);
                    if (both) Quad(mb, L3(o0), L3(o1), L3(o1, h1 * 0.9f), L3(o0, h0 * 0.9f), -face);
                }
            }
            else if (style == "plank")
            {
                for (int k = 0; k < n; k++)
                {
                    var p0 = Vector2.Lerp(a, b, k / (float)n); var p1 = Vector2.Lerp(a, b, (k + 1f) / n);
                    if (InGap((p0 + p1) / 2)) continue;
                    var mb = Block("PLANKW", (p0 + p1) / 2); const float H = 2.8f, TH = 0.12f;
                    var i0 = p0 - outN * (both ? TH : 0); var i1 = p1 - outN * (both ? TH : 0); var o0 = p0 + outN * TH; var o1 = p1 + outN * TH;
                    var face = L3(-outN) - L3(Vector2.zero);
                    Quad(mb, L3(i0), L3(i1), L3(i1, H), L3(i0, H), face);
                    Quad(mb, L3(i0, H), L3(i1, H), L3(o1, H), L3(o0, H), Vector3.up);
                    if (both) Quad(mb, L3(o0), L3(o1), L3(o1, H), L3(o0, H), -face);
                }
                // posts
                for (float s = 0; s <= len; s += 2.5f)
                {
                    var p = Vector2.Lerp(a, b, s / Mathf.Max(len, 0.01f)); if (InGap(p)) continue;
                    MeshGen.Obj(MeshGen.Box(0.22f, 2.9f, 0.22f), mats["BEAM"], root.transform, L3(p, 1.45f), Quaternion.identity, true, "Post");
                }
            }
            else
            {   // the meadow's edge: a bank of moss and stone with trees behind it
                for (int k = 0; k < n; k++)
                {
                    var p0 = Vector2.Lerp(a, b, k / (float)n); var p1 = Vector2.Lerp(a, b, (k + 1f) / n);
                    if (InGap((p0 + p1) / 2)) continue;
                    float h0 = 0.7f + 0.5f * Noise(p0 * 0.9f), h1 = 0.7f + 0.5f * Noise(p1 * 0.9f);
                    var mb = Block("MOSS", (p0 + p1) / 2); var face = L3(-outN) - L3(Vector2.zero);
                    var t0 = p0 + outN * 0.5f; var t1 = p1 + outN * 0.5f; var o0 = p0 + outN * 1.4f; var o1 = p1 + outN * 1.4f;
                    Quad(mb, L3(p0), L3(p1), L3(t1, h1), L3(t0, h0), face);
                    Quad(mb, L3(t0, h0), L3(t1, h1), L3(o1, h1 * 0.6f), L3(o0, h0 * 0.6f), Vector3.up);
                }
                if (!both)
                    for (float s = 1.2f; s < len; s += Rr(5.5f, 8f))
                    {
                        var p = Vector2.Lerp(a, b, s / len) + outN * Rr(1.6f, 2.8f); if (InGap(p)) continue;
                        try { NatureLib.Place(Rr(0, 1) < 0.65f ? "oak1" : "birch", mats, root.transform, L3(p), Rr(0, 360), Rr(0.8f, 1.15f)); } catch (Exception) { }
                    }
            }
        }
        float Noise(Vector2 p) { return Mathf.PerlinNoise(p.x * 0.9f + 31.7f, p.y * 0.9f + 11.3f); }

        void PassageLook(Mats mats, StoryPassage g)
        {
            bool here = g.from == region; var inward = here ? -g.n : g.n;     // into this place
            var T = root.transform;
            if (g.style == "hole" && here)
            {   // broken rock round a dark hole
                for (int k = 0; k < 7; k++)
                {
                    float a = k / 6f; var side = new Vector2(-g.n.y, g.n.x);
                    var p = g.p + side * Mathf.Lerp(-1.25f, 1.25f, a) - inward * 0.2f + inward * Rr(0, 0.5f);
                    float s = Rr(0.35f, 0.6f);
                    MeshGen.Obj(Cave.CaveMesh("stone", new Vector3(s, s * Rr(1.2f, 2.2f), s), rnd.NextDouble() < 0.5), mats["ROCK"], T, L3(p, -0.05f), Quaternion.Euler(Rr(-10, 10), Rr(0, 360), Rr(-10, 10)), true, "HoleRock");
                }
            }
            else
            {   // a door frame (or the planks broken round the hole, on the inn's side)
                var side = new Vector2(-g.n.y, g.n.x);
                foreach (float s in new[] { -0.95f, 0.95f })
                    MeshGen.Obj(MeshGen.Box(0.2f, 2.5f, 0.25f), mats["BEAM"], T, L3(g.p + side * s, 1.25f), Quaternion.LookRotation(L3(side) - L3(Vector2.zero)), true, "DoorPost");
                MeshGen.Obj(MeshGen.Box(2.1f, 0.2f, 0.25f), mats["BEAM"], T, L3(g.p, 2.5f), Quaternion.LookRotation(L3(g.n) - L3(Vector2.zero)), true, "Lintel");
            }
        }
        void Decor(Mats mats)
        {
            var T = root.transform;
            // in the caves: glowing mushrooms by the walls now and then (and their faint light), loose stones
            bool anyCave = false;
            foreach (var t in myTiles) if (t.b == "cave") { anyCave = true; break; }
            if (anyCave || myCircles.Count > 0)
            {
                int placed = 0;
                for (int k = 0; k < 4000 && placed < 60; k++)
                {
                    var p = new Vector2(Rr(gx0, gx0 + gw * CELL), Rr(gy0, gy0 + gh * CELL));
                    if (BiomeAt(p) != "cave" || !Walk(p)) continue;
                    bool nearWall = !Walk(p + Vector2.right * 0.9f) || !Walk(p - Vector2.right * 0.9f) || !Walk(p + Vector2.up * 0.9f) || !Walk(p - Vector2.up * 0.9f);
                    if (!nearWall) continue;
                    placed++;
                    if (placed % 3 == 0)
                    {
                        var g = new GameObject("Glow"); g.transform.SetParent(T, false); g.transform.localPosition = L3(p, 0.5f);
                        var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = Mats.Hex(0x5ff0d0); l.intensity = 0.9f; l.range = 3.2f;
                        for (int q = 0; q < 3; q++) { float s = Rr(0.06f, 0.12f); MeshGen.Obj(MeshGen.Sphere(s, 8, 5, Mathf.PI * 0.55f), mats["GLOWSHROOM"], T, L3(p + new Vector2(Rr(-0.3f, 0.3f), Rr(-0.3f, 0.3f)), Rr(0.08f, 0.2f)), Quaternion.identity, false, "Shroom"); }
                    }
                    else { float s = Rr(0.18f, 0.4f); MeshGen.Obj(Cave.CaveMesh("stone", Vector3.one * s, rnd.NextDouble() < 0.5), mats["ROCK"], T, L3(p, -0.03f), Quaternion.Euler(Rr(-12, 12), Rr(0, 360), Rr(-12, 12)), true, "Stone"); }
                }
            }
            // in an inn: tables, benches, barrels, a counter, a hearth and warm lights
            bool inn = false; foreach (var t in myTiles) if (t.b == "tavern") { inn = true; break; }
            if (inn)
            {
                Vector2 c = Vector2.zero; foreach (var t in myTiles) c += new Vector2((t.i + 0.5f) * map.tile, (t.j + 0.5f) * map.tile); c /= Mathf.Max(1, myTiles.Count);
                int tables = 0;
                for (int k = 0; k < 400 && tables < 5; k++)
                {
                    var p = c + new Vector2(Rr(-7, 7), Rr(-7, 7)); if (!Walk(p) || !Walk(p + Vector2.right * 1.6f) || !Walk(p - Vector2.right * 1.6f) || !Walk(p + Vector2.up * 1.6f) || !Walk(p - Vector2.up * 1.6f)) continue;
                    bool clash = false; foreach (var cc in colliders) if (cc.circle && (new Vector2(-p.x - cc.x, p.y - cc.z)).magnitude < 2.6f) clash = true; if (clash) continue;
                    tables++;
                    MeshGen.Obj(MeshGen.Box(1.6f, 0.08f, 0.9f), mats["WOOD"], T, L3(p, 0.75f), Quaternion.identity, true, "Table");
                    foreach (var o in new[] { new Vector2(-0.65f, -0.35f), new Vector2(0.65f, -0.35f), new Vector2(-0.65f, 0.35f), new Vector2(0.65f, 0.35f) }) MeshGen.Obj(MeshGen.Box(0.08f, 0.75f, 0.08f), mats["WOOD"], T, L3(p + o, 0.375f), Quaternion.identity, true, "Leg");
                    foreach (float s in new[] { -0.75f, 0.75f }) MeshGen.Obj(MeshGen.Box(1.5f, 0.06f, 0.3f), mats["BEAM"], T, L3(p + new Vector2(0, s), 0.45f), Quaternion.identity, true, "Bench");
                    colliders.Add(Collider2.Circle(-p.x, p.y, 0.8f));
                }
                for (int k = 0, b = 0; k < 300 && b < 4; k++)
                {
                    var p = c + new Vector2(Rr(-9, 9), Rr(-9, 9)); if (!Walk(p) || Walk(p + Vector2.right * 0.8f) && Walk(p - Vector2.right * 0.8f) && Walk(p + Vector2.up * 0.8f) && Walk(p - Vector2.up * 0.8f)) continue;
                    b++; MeshGen.Obj(MeshGen.Cylinder(0.32f, 0.28f, 0.9f, 12), mats["WOOD"], T, L3(p, 0.45f), Quaternion.identity, true, "Barrel"); colliders.Add(Collider2.Circle(-p.x, p.y, 0.4f));
                }
                for (int q = 0; q < 3; q++)
                {
                    var g = new GameObject("Lamp"); g.transform.SetParent(T, false); g.transform.localPosition = L3(c + new Vector2(Rr(-4, 4), Rr(-4, 4)), 2.3f);
                    var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = Mats.Hex(0xffb070); l.intensity = 1.4f; l.range = 7f;
                }
            }
        }
        public string TitleAt(Vector3 w) { return map.ChunkName(ToMap(w)) ?? title; }
    }
}
