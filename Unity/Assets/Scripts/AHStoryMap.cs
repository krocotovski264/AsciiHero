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
    /// long builds cut into frames: the work yields a frame when this frame's slice of time is spent (so the loading's
    /// smoke goes on moving). Run drains the same work at once, with no frames between.
    public static class Slicer
    {
        public static float budget = 0.008f; static float t0;
        public static bool Due { get { return Time.realtimeSinceStartup - t0 > budget; } }
        public static void Mark() { t0 = Time.realtimeSinceStartup; }
        /// work with nested work (an IEnumerator yielded runs inside): null from it = a frame may pass here
        public static System.Collections.IEnumerator Frames(System.Collections.IEnumerator work)
        {
            var st = new Stack<System.Collections.IEnumerator>(); st.Push(work); Mark();
            while (st.Count > 0)
            {
                var top = st.Peek();
                if (!top.MoveNext()) { st.Pop(); continue; }
                var sub = top.Current as System.Collections.IEnumerator;
                if (sub != null) { st.Push(sub); continue; }
                if (Due) { yield return null; Mark(); }
            }
        }
        public static void Run(System.Collections.IEnumerator work)
        {
            var st = new Stack<System.Collections.IEnumerator>(); st.Push(work);
            while (st.Count > 0) { var top = st.Peek(); if (!top.MoveNext()) { st.Pop(); continue; } var sub = top.Current as System.Collections.IEnumerator; if (sub != null) st.Push(sub); }
        }
    }

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

    /// a way between two places. Each side has its own point (in that place's map coordinates), the way out of it
    /// through the passage, and its look: hole (broken rock), mouth (a cave's mouth in a mountain), door (a frame),
    /// house (the inn standing outside), inn (the inn's own door, inside)
    public class StoryPassage
    {
        public string from, to, styleA, styleB; public Vector2 pA, nA, pB, nB;
        public bool Has(string r) { return from == r || to == r; }
        public Vector2 P(string r) { return r == from ? pA : pB; }
        public Vector2 N(string r) { return r == from ? nA : nB; }
        public string Style(string r) { return r == from ? styleA : styleB; }
        public string Other(string r) { return r == from ? to : from; }
    }

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
            if (!gridCollide || walk == null) return p;
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
        public bool outdoor, useVis = true, gridCollide = true; public Tavern inn; HashSet<long> mountain; float margin = 3;
        public static StoryLocation Build(Mats mats, StoryMap map, string region, HashSet<long> tileSet, bool withCircles, List<StoryPassage> allPassages, Vector3 at, int seed, bool outdoor = false, HashSet<long> mountainTiles = null)
        { var o = new StoryLocation[1]; Slicer.Run(BuildCo(o, mats, map, region, tileSet, withCircles, allPassages, at, seed, outdoor, mountainTiles)); return o[0]; }
        public static System.Collections.IEnumerator BuildCo(StoryLocation[] o, Mats mats, StoryMap map, string region, HashSet<long> tileSet, bool withCircles, List<StoryPassage> allPassages, Vector3 at, int seed, bool outdoor = false, HashSet<long> mountainTiles = null)
        {
            float tb = Time.realtimeSinceStartup;
            var L = new StoryLocation { map = map, region = region, rnd = new System.Random(seed), id = "story:" + region, outdoor = outdoor, useVis = !outdoor, mountain = mountainTiles, margin = outdoor ? 11 : 3 };
            L.root = new GameObject("Story_" + region);
            foreach (var t in map.tiles) if (tileSet.Contains(Key(t.i, t.j))) L.myTiles.Add(t);
            if (withCircles) L.myCircles.AddRange(map.circles);
            foreach (var p in allPassages) if (p.Has(region)) L.passages.Add(p);
            o[0] = L;
            L.Grid(); yield return null;
            yield return L.GeometryCo(mats);
            L.PlaceAt(at); yield return null;
            L.CollectCull();
            Debug.Log(region + ": построено за " + ((Time.realtimeSinceStartup - tb) * 1000).ToString("0") + " мс (" + L.tLog + ")");
        }
        string tLog = "";
        /// an inn: the test world's tavern, its walk grid from its own walls and furniture (map x = -local x)
        public static StoryLocation BuildInn(Mats mats, StoryMap map, string region, List<StoryPassage> allPassages, Vector3 at, int seed)
        {
            var L = new StoryLocation { map = map, region = region, rnd = new System.Random(seed), id = "story:" + region, useVis = false, gridCollide = false };
            L.root = new GameObject("Story_" + region);
            L.inn = Tavern.Build(mats); L.inn.root.transform.SetParent(L.root.transform, false);
            L.colliders.AddRange(L.inn.colliders); L.bounds = L.inn.bounds; L.spawn = L.inn.spawn;
            foreach (var p in allPassages) if (p.Has(region)) L.passages.Add(p);
            var b = L.bounds; L.gx0 = -b.xMax - 1; L.gy0 = b.yMin - 1; L.gw = Mathf.CeilToInt((b.width + 2) / CELL); L.gh = Mathf.CeilToInt((b.height + 2) / CELL);
            L.walk = new bool[L.gw * L.gh]; L.cellBiome = new string[L.gw * L.gh];
            for (int j = 0; j < L.gh; j++) for (int i = 0; i < L.gw; i++)
                {
                    float mx = L.gx0 + (i + 0.5f) * CELL, my = L.gy0 + (j + 0.5f) * CELL; var lp = new Vector2(-mx, my);
                    bool ok = b.Contains(lp) && (L.CollideRaw(lp, 0.05f) - lp).sqrMagnitude < 1e-6f;
                    L.walk[j * L.gw + i] = ok; L.cellBiome[j * L.gw + i] = ok ? "tavern" : null;
                }
            L.BuildPathGrid();
            L.PlaceAt(at); L.inn.origin = at;
            return L;
        }
        public static long Key(int i, int j) { return ((long)i << 32) ^ (uint)j; }

        void Grid()
        {
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
            foreach (var t in myTiles) { x0 = Mathf.Min(x0, t.i * map.tile); y0 = Mathf.Min(y0, t.j * map.tile); x1 = Mathf.Max(x1, (t.i + 1) * map.tile); y1 = Mathf.Max(y1, (t.j + 1) * map.tile); }
            foreach (var c in myCircles) { x0 = Mathf.Min(x0, c.c.x - c.r); y0 = Mathf.Min(y0, c.c.y - c.r); x1 = Mathf.Max(x1, c.c.x + c.r); y1 = Mathf.Max(y1, c.c.y + c.r); }
            gx0 = Mathf.Floor(x0) - margin; gy0 = Mathf.Floor(y0) - margin;
            gw = Mathf.CeilToInt((x1 - gx0 + margin) / CELL); gh = Mathf.CeilToInt((y1 - gy0 + margin) / CELL);
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
                    for (float o = -0.45f; o <= 0.45f; o += CELL * 0.5f) { var q = p + nrm * o; int i = Mathf.FloorToInt((q.x - gx0) / CELL), j = Mathf.FloorToInt((q.y - gy0) / CELL); if (i >= 0 && j >= 0 && i < gw && j < gh) walk[j * gw + i] = false; }
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
        bool InGap(Vector2 p) { foreach (var g in passages) if ((p - g.P(region)).sqrMagnitude < 0.95f * 0.95f) return true; return false; }

        // ---- the meshes ----
        class MB { public List<Vector3> v = new List<Vector3>(); public List<int> t = new List<int>(); }
        readonly Dictionary<string, MB> blocks = new Dictionary<string, MB>();
        MB Block(string mat, Vector2 m) { string k = mat + ":" + Mathf.FloorToInt(m.x / 10) + ":" + Mathf.FloorToInt(m.y / 10); MB b; if (!blocks.TryGetValue(k, out b)) { b = new MB(); blocks[k] = b; } return b; }
        static void TriUp(MB b, Vector3 p0, Vector3 p1, Vector3 p2) { if (Vector3.Cross(p1 - p0, p2 - p0).y < 0) Tri(b, p0, p2, p1); else Tri(b, p0, p1, p2); }
        static void Tri(MB b, Vector3 p0, Vector3 p1, Vector3 p2) { int o = b.v.Count; b.v.Add(p0); b.v.Add(p1); b.v.Add(p2); b.t.Add(o); b.t.Add(o + 1); b.t.Add(o + 2); }
        // a quad facing toward 'face' (the triangles are wound so their normal points that way)
        static void Quad(MB b, Vector3 a, Vector3 bb, Vector3 c, Vector3 d, Vector3 face)
        {
            var n = Vector3.Cross(bb - a, c - a);
            if (Vector3.Dot(n, face) < 0) { Tri(b, a, c, bb); Tri(b, a, d, c); } else { Tri(b, a, bb, c); Tri(b, a, c, d); }
        }
        static string FloorMat(string b) { return b == "cave" ? "CAVEFLOOR" : b == "tavern" ? "FLOOR" : b == "meadow" ? "GROUND" : b == "road" ? "ROAD" : b == "snow" ? "SAND" : b == "wastes" ? "SAND" : "GROUND"; }
        string WallStyle(string b) { return b == "cave" || b == "crystal" ? "rock" : b == "tavern" || b == "city" || b == "grim" ? "plank" : "hedge"; }

        System.Collections.IEnumerator GeometryCo(Mats mats)
        {
            var T = root.transform; float tp = Time.realtimeSinceStartup;
            System.Func<string, string> Lap = nm => { float n = Time.realtimeSinceStartup; string r = nm + " " + ((n - tp) * 1000).ToString("0") + "; "; tp = n; return r; };
            // floors: tiles as they are, a round arena as rings of small cells where no tile is
            foreach (var t in myTiles)
            {
                var poly = map.Poly(t); var mb = Block(FloorMat(t.b), poly[0]);
                for (int k = 1; k + 1 < poly.Count; k++) TriUp(mb, L3(poly[0]), L3(poly[k + 1]), L3(poly[k]));
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
                        TriUp(mb, L3(P(a0, r0)), L3(P(a1, r1)), L3(P(a0, r1)));
                        if (r0 > 0) TriUp(mb, L3(P(a0, r0)), L3(P(a1, r0)), L3(P(a1, r1)));
                    }
                }
            }
            foreach (var kv in blocks)
            {
                string matName = kv.Key.Split(':')[0];
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; m.SetVertices(kv.Value.v); m.SetTriangles(kv.Value.t, 0);
                m.RecalculateNormals(); m.RecalculateBounds(); MeshGen.AddGlyphUV(m, false);
                MeshGen.Obj(m, mats[matName], T, Vector3.zero, Quaternion.identity, false, "Ground");
            }
            tLog += Lap("пол"); yield return null;
            Fields(); tLog += Lap("поля"); yield return null;
            yield return Rock(mats); tLog += Lap("скала");
            foreach (var g in passages) { PassageLook(mats, g); yield return null; }
            tLog += Lap("проходы");
            Decor(mats); tLog += Lap("убранство");
        }

        // ================= the walls: the surface of one field over the whole place =================
        // As the test cave (Cave.BuildRock), but the plan comes from the map: the signed distance to the edge of the
        // ground (> 0 over the ground, < 0 in the rock), worked out on the walk grid. Every wall gets the style of the
        // ground nearest to it - rough rock leaning back in the caves, a low grassy bank on the meadow, straight plank
        // walls in the inns - and the rock is a band 1.6 m wide (beyond it, nothing: the sight mask hides it anyway).
        // Its surface is taken with surface nets in 8 m chunks: closed, without gaps or seams, a mesh per chunk.
        float[] sdf; byte[] lab; float[] dInM;             // per cell: signed distance (m), nearest ground's style, distance to the rock (m)
        const float BAND = 1.6f;
        // styles: 0 cave rock, 1 meadow (no rock: the forest stands there), 2 inn planks, 3 a mountain over a cave seen from outside
        static readonly float[] H_TOP = { 2.9f, 0f, 2.9f, 7f }, CREST = { 0.9f, 0f, 0f, 2.2f }, LEAN = { 0.1f, 0f, 0f, 0.5f }, ROUGH = { 1f, 0f, 0f, 1f }, BANDS = { 1.6f, 0f, 1.6f, 9f };
        static byte Code(string b) { return (byte)(b == "meadow" ? 1 : b == "tavern" || b == "city" || b == "grim" ? 2 : 0); }
        float nOx, nOz;
        void Fields()
        {
            int n = gw * gh; const float INF = 1e9f;
            var dO = new float[n]; var dI = new float[n]; lab = new byte[n];
            for (int q = 0; q < n; q++) { dO[q] = walk[q] ? 0 : INF; dI[q] = walk[q] ? INF : 0; lab[q] = walk[q] ? Code(cellBiome[q]) : (byte)0; }
            const float D = 1.41421f;
            // two chamfer passes each way; the style of the nearest ground travels with the distance
            for (int pass = 0; pass < 2; pass++)
            {
                for (int j = 0; j < gh; j++) for (int i = 0; i < gw; i++)
                    {
                        int q = j * gw + i;
                        void Try(int ii, int jj, float c) { if (ii < 0 || jj < 0 || ii >= gw || jj >= gh) return; int r = jj * gw + ii; if (dO[r] + c < dO[q]) { dO[q] = dO[r] + c; lab[q] = lab[r]; } if (dI[r] + c < dI[q]) dI[q] = dI[r] + c; }
                        Try(i - 1, j, 1); Try(i, j - 1, 1); Try(i - 1, j - 1, D); Try(i + 1, j - 1, D);
                    }
                for (int j = gh - 1; j >= 0; j--) for (int i = gw - 1; i >= 0; i--)
                    {
                        int q = j * gw + i;
                        void Try(int ii, int jj, float c) { if (ii < 0 || jj < 0 || ii >= gw || jj >= gh) return; int r = jj * gw + ii; if (dO[r] + c < dO[q]) { dO[q] = dO[r] + c; lab[q] = lab[r]; } if (dI[r] + c < dI[q]) dI[q] = dI[r] + c; }
                        Try(i + 1, j, 1); Try(i, j + 1, 1); Try(i + 1, j + 1, D); Try(i - 1, j + 1, D);
                    }
            }
            if (mountain != null)
                for (int j = 0; j < gh; j++) for (int i = 0; i < gw; i++)
                    {
                        int q = j * gw + i; if (walk[q]) continue;
                        float mx = gx0 + (i + 0.5f) * CELL, my = gy0 + (j + 0.5f) * CELL;
                        if (mountain.Contains(Key(Mathf.FloorToInt(mx / map.tile), Mathf.FloorToInt(my / map.tile)))) lab[q] = 3;
                    }
            sdf = new float[n]; dInM = new float[n];
            for (int q = 0; q < n; q++) { sdf[q] = walk[q] ? (dI[q] - 0.5f) * CELL : -(dO[q] - 0.5f) * CELL; dInM[q] = walk[q] ? dI[q] * CELL : 0; }
            nOx = Rr(0, 300); nOz = Rr(0, 300);
        }
        /// the signed distance at a point of the map (bilinear over the cell centres)
        public float SdfAt(float x, float y)
        {
            float fx = (x - gx0) / CELL - 0.5f, fy = (y - gy0) / CELL - 0.5f;
            int i = Mathf.FloorToInt(fx), j = Mathf.FloorToInt(fy); float u = fx - i, v = fy - j;
            float S(int a, int b) { a = Mathf.Clamp(a, 0, gw - 1); b = Mathf.Clamp(b, 0, gh - 1); return sdf[b * gw + a]; }
            return Mathf.Lerp(Mathf.Lerp(S(i, j), S(i + 1, j), u), Mathf.Lerp(S(i, j + 1), S(i + 1, j + 1), u), v);
        }
        int LabAt(float x, float y) { int i = Mathf.Clamp(Mathf.FloorToInt((x - gx0) / CELL), 0, gw - 1), j = Mathf.Clamp(Mathf.FloorToInt((y - gy0) / CELL), 0, gh - 1); return lab[j * gw + i]; }
        /// how far a point of the ground is from the rock (m; 0 off the ground)
        public float WallDist(Vector2 m) { int i = Mathf.FloorToInt((m.x - gx0) / CELL), j = Mathf.FloorToInt((m.y - gy0) / CELL); return i < 0 || j < 0 || i >= gw || j >= gh ? 0 : dInM[j * gw + i]; }
        /// the rock field at a point (map x, height y, map y as z): > 0 in the air
        float Air(float x, float y, float z)
        {
            float sd = SdfAt(x, z);
            if (sd > 1.4f) return sd;
            int c = LabAt(x, z);
            if (c == 1) return Mathf.Max(sd, 1f);                // the meadow's edge: no rock
            float foot = Mathf.Clamp01(1 - y / 1.1f); foot *= foot;
            float room = sd + LEAN[c] * y - 0.15f * ROUGH[c] * foot;
            float hOut = H_TOP[c] + CREST[c] * (Mathf.PerlinNoise(x * 0.23f + nOx, z * 0.23f + nOz) - 0.5f) * 2f;
            if (c == 3) hOut *= Mathf.SmoothStep(0, 1, -sd / 2.5f + 0.25f) * (1 - Mathf.SmoothStep(0, 1, (-sd - 6f) / 3f));       // a hill: up from the meadow, down again behind
            float f = Mathf.Max(room, y - hOut);
            f = Mathf.Max(f, -(sd + BANDS[c]));
            // the ways through: a hole carved in the rock (a doorway in the inn's planks)
            foreach (var g in passages)
            {
                var into = g.N(region); var gp = g.P(region); string st = g.Style(region);
                float len = st == "mouth" ? 9.6f : BAND + 0.6f, rad = st == "mouth" ? 1.5f : 1.0f;
                var a = gp - into * 0.3f; var b = gp + into * len; var ab = b - a;
                var q = new Vector2(x, z); float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude); var cl = a + ab * t;
                float side = Mathf.Abs(Vector2.Dot(q - cl, new Vector2(-into.y, into.x)));
                float hole = st == "door" || st == "house" ? Mathf.Min(0.95f - side, 2.3f - y) : rad - new Vector2(side, Mathf.Max(0, y - rad)).magnitude;
                if ((q - cl).magnitude < rad + 0.4f) f = Mathf.Max(f, hole);
            }
            float rough = ROUGH[c];
            if (rough <= 0 || Mathf.Abs(f) > 0.8f) return f;
            float big = (Cave.Fbm3(x * 0.45f + nOx, y * 0.45f, z * 0.45f + nOz, 2) - 0.5f) * 0.8f;
            float crease = Cave.RidgedW(x * 0.9f + nOz, y * 0.9f, z * 0.9f + nOx) * 0.2f;
            float grain = (Cave.N3(x * 5.1f + nOx, y * 5.1f, z * 5.1f + nOz) - 0.5f) * 0.05f;
            return f + (big - crease + 0.08f + grain) * rough;
        }
        System.Collections.IEnumerator Rock(Mats mats)
        {
            var T = root.transform;
            var mRock = new Material(mats["WETROCK"]) { name = "StoryRock" }; mRock.SetFloat("_SeeThrough", 1); mRock.SetFloat("_Cull", 0); mRock.SetVector("_GlyphCell", new Vector4(0.12f, 0.2f, 0, 0));
            var mBank = new Material(mats["MOSS"]) { name = "StoryBank" }; mBank.SetFloat("_SeeThrough", 1); mBank.SetFloat("_Cull", 0); mBank.SetVector("_GlyphCell", new Vector4(0.1f, 0.16f, 0, 0));
            var mPlank = new Material(mats["BEAM"]) { name = "StoryPlanks" }; mPlank.SetColor("_Color", Mats.Hex(0x6a4626)); mPlank.SetFloat("_SeeThrough", 1); mPlank.SetFloat("_Cull", 0); mPlank.SetVector("_GlyphCell", new Vector4(0.06f, 0.1f, 0, 0));
            var MAT = new[] { mRock, mBank, mPlank, mRock };
            bool tall = mountain != null && mountain.Count > 0;
            float S = tall ? 0.35f : 0.25f; const float CH = 8f, Y0 = -0.25f; int NC = Mathf.RoundToInt(CH / S), NY = Mathf.CeilToInt(((tall ? 9.8f : 3.9f) - Y0) / S) + 1;
            float X0 = gx0, Z0 = gy0, X1 = gx0 + gw * CELL, Z1 = gy0 + gh * CELL;
            int NP = NC + 3;      // points -1 .. NC+1
            var F = new float[NP * NY * NP];
            int I(int i, int y, int j) { return ((j + 1) * NY + y) * NP + (i + 1); }
            int verts = 0, tris = 0;
            for (float cz = Z0; cz < Z1; cz += CH)
                for (float cx = X0; cx < X1; cx += CH)
                {
                    yield return null;            // (a frame may pass between the chunks)
                    // anything of the rock here? (between the edge of the ground and the end of the band)
                    bool any = false;
                    float maxBand = mountain != null && mountain.Count > 0 ? 9.6f : 2.2f;
                    for (float sz = cz - S; sz <= cz + CH + S && !any; sz += 0.5f) for (float sx = cx - S; sx <= cx + CH + S; sx += 0.5f) { float d = SdfAt(sx, sz); if (d < 0.6f && d > -maxBand) { any = true; break; } }
                    if (!any) continue;
                    for (int j = -1; j <= NC + 1; j++) for (int i = -1; i <= NC + 1; i++) for (int y = 0; y < NY; y++)
                                F[I(i, y, j)] = Air(cx + i * S, Y0 + y * S, cz + j * S);
                    var cellV = new Dictionary<int, int>(); var vs = new List<Vector3>(); var ns = new List<Vector3>(); var vc = new List<int>();
                    int[,] E = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
                    var cv = new float[8]; var cp = new Vector3[8];
                    for (int j = -1; j <= NC; j++) for (int i = -1; i <= NC; i++) for (int y = 0; y < NY - 1; y++)
                            {
                                int neg = 0;
                                for (int k = 0; k < 8; k++) { int dx = k & 1, dy = (k >> 1) & 1, dz = (k >> 2) & 1; cv[k] = F[I(i + dx, y + dy, j + dz)]; cp[k] = new Vector3(dx, dy, dz); if (cv[k] < 0) neg++; }
                                if (neg == 0 || neg == 8) continue;
                                Vector3 sum = Vector3.zero; int cnt = 0;
                                for (int e = 0; e < 12; e++) { float a = cv[E[e, 0]], b = cv[E[e, 1]]; if ((a < 0) == (b < 0)) continue; sum += Vector3.Lerp(cp[E[e, 0]], cp[E[e, 1]], a / (a - b)); cnt++; }
                                var pc = sum / cnt; float mx = cx + (i + pc.x) * S, my = Y0 + (y + pc.y) * S, mz = cz + (j + pc.z) * S;
                                // the normal from the field's slope in this cell (into the air), turned into the place's axes
                                float gxv = (cv[1] - cv[0] + cv[3] - cv[2] + cv[5] - cv[4] + cv[7] - cv[6]), gyv = (cv[2] - cv[0] + cv[3] - cv[1] + cv[6] - cv[4] + cv[7] - cv[5]), gzv = (cv[4] - cv[0] + cv[5] - cv[1] + cv[6] - cv[2] + cv[7] - cv[3]);
                                cellV[I(i, y, j)] = vs.Count; vs.Add(new Vector3(-mx, my, mz)); ns.Add(new Vector3(-gxv, gyv, gzv).normalized); vc.Add(LabAt(mx, mz));
                            }
                    if (vs.Count == 0) continue;
                    var ts = new List<int>[4]; for (int q = 0; q < 4; q++) ts[q] = new List<int>();
                    void Quad(int c0, int c1, int c2, int c3)
                    {
                        int a, b, c, d; if (!cellV.TryGetValue(c0, out a) || !cellV.TryGetValue(c1, out b) || !cellV.TryGetValue(c2, out c) || !cellV.TryGetValue(c3, out d)) return;
                        var L = ts[vc[a]];
                        foreach (var tri in new[] { new[] { a, b, c }, new[] { a, c, d } })
                        {
                            var nrm = Vector3.Cross(vs[tri[1]] - vs[tri[0]], vs[tri[2]] - vs[tri[0]]);
                            if (Vector3.Dot(nrm, ns[tri[0]] + ns[tri[1]] + ns[tri[2]]) < 0) { L.Add(tri[0]); L.Add(tri[2]); L.Add(tri[1]); }
                            else { L.Add(tri[0]); L.Add(tri[1]); L.Add(tri[2]); }
                        }
                    }
                    // a quad for every crossed grid edge whose point belongs to this chunk
                    for (int j = 0; j < NC; j++) for (int i = 0; i < NC; i++) for (int y = 1; y < NY - 1; y++)
                            {
                                float f0 = F[I(i, y, j)];
                                if ((f0 < 0) != (F[I(i + 1, y, j)] < 0)) Quad(I(i, y - 1, j - 1), I(i, y, j - 1), I(i, y, j), I(i, y - 1, j));
                                if ((f0 < 0) != (F[I(i, y + 1, j)] < 0)) Quad(I(i - 1, y, j - 1), I(i, y, j - 1), I(i, y, j), I(i - 1, y, j));
                                if ((f0 < 0) != (F[I(i, y, j + 1)] < 0)) Quad(I(i - 1, y - 1, j), I(i, y - 1, j), I(i, y, j), I(i - 1, y, j));
                            }
                    for (int q = 0; q < 4; q++)
                    {
                        if (ts[q].Count == 0) continue;
                        // only the vertices this material uses
                        var map2 = new Dictionary<int, int>(); var rv = new List<Vector3>(); var rn = new List<Vector3>(); var rt = new List<int>();
                        foreach (int v in ts[q]) { int k2; if (!map2.TryGetValue(v, out k2)) { k2 = rv.Count; map2[v] = k2; rv.Add(vs[v]); rn.Add(ns[v]); } rt.Add(k2); }
                        var m = new Mesh { name = "storyrock", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                        m.SetVertices(rv); m.SetNormals(rn); m.uv = new Vector2[rv.Count]; m.SetTriangles(rt, 0); m.RecalculateBounds(); MeshGen.AddGlyphUV(m, false);
                        MeshGen.Obj(m, MAT[q], T, Vector3.zero, Quaternion.identity, true, "Rock");
                        verts += rv.Count; tris += rt.Count / 3;
                    }
                }
            Debug.Log(region + ": скала " + verts + " вершин, " + tris + " треугольников");
        }

        void PassageLook(Mats mats, StoryPassage g)
        {
            var T = root.transform; var gp = g.P(region); var nOut = g.N(region); string st = g.Style(region);
            var side = new Vector2(-nOut.y, nOut.x);
            if (st == "hole" || st == "mouth")
            {   // broken rock round a dark hole
                float w = st == "mouth" ? 1.8f : 1.25f;
                for (int k = 0; k < (st == "mouth" ? 11 : 7); k++)
                {
                    var p = gp + side * Mathf.Lerp(-w, w, Rr(0, 1)) - nOut * Rr(0.1f, 0.7f);
                    if (Mathf.Abs(Vector2.Dot(p - gp, side)) < 0.7f) continue;          // (the way itself stays clear)
                    float s = Rr(0.35f, st == "mouth" ? 0.9f : 0.6f);
                    MeshGen.Obj(Cave.CaveMesh("stone", new Vector3(s, s * Rr(1.2f, 2.2f), s), rnd.NextDouble() < 0.5), mats["ROCK"], T, L3(p, -0.05f), Quaternion.Euler(Rr(-10, 10), Rr(0, 360), Rr(-10, 10)), true, "HoleRock");
                }
            }
            else if (st == "house")
            {   // the inn from outside (the test world's), its door at the way in
                var D = L3(-nOut) - L3(Vector2.zero);           // from the inn out into the meadow
                var c = L3(gp) - D * 2.7f; float yaw = Mathf.Atan2(D.x, D.z) * Mathf.Rad2Deg;
                NatureLib.Place("inn", mats, T, c, yaw, 1f);
                var lg = new GameObject("InnLamp"); lg.transform.SetParent(T, false); lg.transform.localPosition = L3(gp, 2.3f) + D * 0.3f + Vector3.Cross(Vector3.up, D) * 0.6f;
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.7f, 0.4f); l.intensity = 1.2f; l.range = 4.5f; l.shadows = LightShadows.None;
            }
            else if (st == "door")
            {
                foreach (float s in new[] { -0.95f, 0.95f })
                    MeshGen.Obj(MeshGen.Box(0.2f, 2.5f, 0.25f), mats["BEAM"], T, L3(gp + side * s, 1.25f), Quaternion.LookRotation(L3(side) - L3(Vector2.zero)), true, "DoorPost");
                MeshGen.Obj(MeshGen.Box(2.1f, 0.2f, 0.25f), mats["BEAM"], T, L3(gp, 2.5f), Quaternion.LookRotation(L3(nOut) - L3(Vector2.zero)), true, "Lintel");
            }
        }
        // ================= what lies about =================
        // Placed by rules from the ground itself, so a new map gets it right away: by the walls (stalagmites, boulders,
        // moss, mushrooms - some glowing), in the open (puddles with drops falling into them, skeletons, bones, sharp
        // stones), hanging from the dark (stalactites); never on a thing of the map (a creature, a chest, a way out).
        struct Pool { public Vector2 c; public float rx, ry, ang, depth; }
        readonly List<Pool> pools = new List<Pool>();
        class GlowC { public Light l; public float baseI, ph; public List<Renderer> caps = new List<Renderer>(); }
        readonly List<GlowC> glowsC = new List<GlowC>(); MaterialPropertyBlock mpbC;
        readonly List<Vector3> drips = new List<Vector3>(); float lastT = -1;
        public System.Action<float, float> onDrip;
        readonly List<Vector2> keepFree = new List<Vector2>();
        static float Frac(float x) { return x - Mathf.Floor(x); }
        float PoolK(Pool p, float x, float y)
        {
            float dx = x - p.c.x, dy = y - p.c.y, cs = Mathf.Cos(p.ang), sn = Mathf.Sin(p.ang);
            float u = (dx * cs + dy * sn) / p.rx, v = (-dx * sn + dy * cs) / p.ry;
            float a = Mathf.Atan2(v, u), wob = 1 + 0.08f * Mathf.Sin(a * 3 + p.c.x) + 0.05f * Mathf.Sin(a * 5 + p.c.y);
            return Mathf.Sqrt(u * u + v * v) / wob;
        }
        float WaterK(Vector2 m) { float k = 9; foreach (var p in pools) k = Mathf.Min(k, PoolK(p, m.x, m.y)); return k; }
        // location-local x, z -> map: x = -X, y = Z
        public override bool HasWater => pools.Count > 0;
        public override bool InWater(float x, float z) { return WaterK(new Vector2(-x, z)) < 0.97f; }
        public override float Depth(float x, float z)
        {
            float d = 0; foreach (var p in pools) { float k = PoolK(p, -x, z); if (k < 0.97f) d = Mathf.Max(d, WaterDepth.Puddle((0.97f - k) / 0.97f, p.depth)); }
            return d;
        }
        public override List<Bounds> MirrorWater() { return new List<Bounds>(); }
        bool Free(Vector2 p, float r)
        {
            if (!Walk(p)) return false;
            foreach (var q in keepFree) if ((p - q).sqrMagnitude < 2.2f * 2.2f) return false;
            foreach (var c in colliders) if (c.circle && (new Vector2(-p.x - c.x, p.y - c.z)).magnitude < c.r + r + 0.15f) return false;
            return WaterK(p) > 1.25f;
        }
        /// a random free spot on this ground whose distance from the rock is within [d0, d1]
        bool Spot(string biome, float d0, float d1, float r, out Vector2 p)
        {
            for (int k = 0; k < 60; k++)
            {
                p = new Vector2(Rr(gx0, gx0 + gw * CELL), Rr(gy0, gy0 + gh * CELL));
                if (BiomeAt(p) != biome) continue;
                float wd = WallDist(p); if (wd < d0 || wd > d1) continue;
                if (Free(p, r)) return true;
            }
            p = Vector2.zero; return false;
        }
        float AreaOf(string biome) { int n = 0; for (int q = 0; q < walk.Length; q++) if (walk[q] && cellBiome[q] == biome) n++; return n * CELL * CELL; }
        void Moss(Transform T, Mats mats, Vector2 p, float r, float y)
        {
            var g = MeshGen.Obj(MeshGen.Sphere(1, 8, 3, Mathf.PI / 2), mats["MOSS"], T, L3(p, y), Quaternion.Euler(0, Rr(0, 360), 0), false, "Moss");
            g.transform.localScale = new Vector3(r * Rr(0.8f, 1.2f), Rr(0.04f, 0.08f), r * Rr(0.8f, 1.2f));
        }
        void Mushroom(Transform T, Mats mats, Vector2 p, float s, bool glow, List<Renderer> caps)
        {
            var holder = new GameObject(glow ? "GlowMushroom" : "Mushroom").transform; holder.SetParent(T, false);
            holder.localPosition = L3(p); holder.localRotation = Quaternion.Euler(Rr(-12, 12), Rr(0, 360), Rr(-12, 12)); holder.localScale = Vector3.one * s;
            float h = Rr(0.07f, 0.16f);
            MeshGen.Obj(MeshGen.Cylinder(0.022f, 0.016f, h, 6), mats["LINEN"], holder, new Vector3(0, h / 2, 0), Quaternion.identity, false, "Stem");
            var cap = MeshGen.Obj(MeshGen.Sphere(0.06f, 8, 4, Mathf.PI / 2), mats[glow ? "GLOWSHROOM" : "MUSH"], holder, new Vector3(0, h - 0.005f, 0), Quaternion.identity, false, "Cap");
            cap.transform.localScale = new Vector3(1, glow ? 0.75f : 0.55f, 1);
            if (caps != null) caps.Add(cap.GetComponent<Renderer>());
        }
        static readonly string[] BONES = { "b_femur", "b_humerus", "b_tibia", "b_rib", "b_rib", "b_pelvis", "b_jaw" };
        void Decor(Mats mats)
        {
            var T = root.transform;
            foreach (var e in map.ents) keepFree.Add(e.p);
            foreach (var g in passages) { var gp = g.P(region); var gn = g.N(region); keepFree.Add(gp); keepFree.Add(gp + gn * 1.5f); keepFree.Add(gp - gn * 1.5f); }
            float cave = AreaOf("cave"), meadow = AreaOf("meadow");
            Vector2 p;
            if (cave > 1)
            {
                // puddles first (the rest keeps off them): in the open, a drop falling into each now and then
                var dpts = new List<Vector3>(); var dsd = new List<Vector2>();
                for (int k = 0, n = Mathf.RoundToInt(cave / 150); k < n; k++)
                {
                    if (!Spot("cave", 1.4f, 99, 1.0f, out p)) continue;
                    var pl = new Pool { c = p, rx = Rr(0.5f, 1.1f), ry = Rr(0.35f, 0.8f), ang = Rr(0, 3.14f), depth = 0.06f };
                    pools.Add(pl);
                    WaterMesh(T, mats, pl);
                    for (int q = 0; q < 2; q++)
                    {
                        float a = Rr(0, 6.28f), rr = Rr(0, 0.6f); var dp = pl.c + new Vector2(Mathf.Cos(a) * pl.rx * rr, Mathf.Sin(a) * pl.ry * rr); float ph = Rr(0, 1);
                        dpts.Add(L3(dp, WATER_Y)); dsd.Add(new Vector2(ph, Rr(0, 1))); drips.Add(new Vector3(dp.x, dp.y, ph));
                    }
                }
                if (dpts.Count > 0) GlyphLayer.MakeMotes(T, Vector3.zero, Quaternion.identity, Vector3.one, dpts, dsd, 2, new Color(0.65f, 0.8f, 0.9f), new Vector4(0.05f, Cave.DRIP_TOP, Cave.DRIP_PERIOD, 0));
                // stalagmites and boulders by the walls (they block the way: colliders)
                for (int k = 0, n = Mathf.RoundToInt(cave / 45); k < n; k++)
                {
                    float r = Rr(0.09f, 0.2f); if (!Spot("cave", 0.35f, 1.3f, r, out p)) continue;
                    float h = Rr(0.5f, 1.7f);
                    MeshGen.Obj(MeshGen.Cylinder(r, 0.015f, h, 8), mats["WETROCK"], T, L3(p, h / 2), Quaternion.Euler(Rr(-5, 5), Rr(0, 360), Rr(-5, 5)), true, "Stalagmite");
                    colliders.Add(Collider2.Circle(-p.x, p.y, r + 0.08f));
                    if (Rr(0, 1) < 0.5f) Moss(T, mats, p + new Vector2(Rr(-0.2f, 0.2f), Rr(-0.2f, 0.2f)), r * 2.2f, 0.01f);
                }
                for (int k = 0, n = Mathf.RoundToInt(cave / 70); k < n; k++)
                {
                    float s = Rr(0.3f, 0.7f); if (!Spot("cave", 0.2f, 1.6f, s, out p)) continue;
                    float d = s * 2.3f;
                    MeshGen.Obj(Cave.CaveMesh(Rr(0, 1) < 0.5f ? "boulder_a" : "boulder_b", new Vector3(d * Rr(0.9f, 1.1f), d * Rr(0.85f, 1.15f), d * Rr(0.9f, 1.1f)), Rr(0, 1) < 0.5f), mats[Rr(0, 1) < 0.5f ? "ROCK" : "WETROCK"], T, L3(p, -0.04f), Quaternion.Euler(0, Rr(0, 360), 0), true, "Boulder");
                    if (s > 0.42f) colliders.Add(Collider2.Circle(-p.x, p.y, s * 1.05f));
                    if (Rr(0, 1) < 0.6f) Moss(T, mats, p, s * 0.8f, s * 0.75f);
                }
                // stalactites over the ground, 3 m up
                for (int k = 0, n = Mathf.RoundToInt(cave / 35); k < n; k++)
                {
                    if (!Spot("cave", 0.2f, 99, 0, out p)) continue;
                    float h = Rr(0.35f, 1.2f), r = Rr(0.06f, 0.15f);
                    MeshGen.Obj(MeshGen.Cylinder(0.012f, r, h, 7), mats["WETROCK"], T, L3(p, 3.0f - h / 2), Quaternion.Euler(0, Rr(0, 360), 0), false, "Stalactite");
                }
                // small sharp stones, walked over
                for (int k = 0, n = Mathf.RoundToInt(cave / 50); k < n; k++)
                {
                    float r = Rr(0.08f, 0.18f), sc = r * 2.6f; if (!Spot("cave", 0.2f, 99, r, out p)) continue;
                    MeshGen.Obj(Cave.CaveMesh("stone", new Vector3(sc, sc * Rr(0.9f, 1.4f), sc), Rr(0, 1) < 0.5f), mats["ROCK"], T, L3(p, -0.02f), Quaternion.Euler(Rr(-12, 12), Rr(0, 360), Rr(-12, 12)), true, "SharpRock");
                }
                // moss at the foot of the walls
                for (int k = 0, n = Mathf.RoundToInt(cave / 25); k < n; k++) { if (!Spot("cave", 0.1f, 0.6f, 0.2f, out p)) continue; Moss(T, mats, p, Rr(0.25f, 0.6f), 0.01f); }
                // mushrooms: clusters by the walls, a third of them glowing (a slow pulse of their light)
                int lights = 0;
                for (int k = 0, n = Mathf.RoundToInt(cave / 90); k < n; k++)
                {
                    if (!Spot("cave", 0.2f, 1.0f, 0.3f, out p)) continue;
                    bool glow = k % 3 == 0;
                    var gl = glow ? new GlowC { ph = Rr(0, 6.28f), baseI = 0.55f } : null;
                    int c = 3 + rnd.Next(5);
                    for (int i = 0; i < c; i++) Mushroom(T, mats, p + new Vector2(Rr(-0.3f, 0.3f), Rr(-0.3f, 0.3f)), Rr(0.7f, 1.4f), glow, gl != null ? gl.caps : null);
                    if (glow && lights < 16)
                    {
                        lights++;
                        var lg = new GameObject("GlowLight"); lg.transform.SetParent(T, false); lg.transform.localPosition = L3(p, 0.45f);
                        gl.l = lg.AddComponent<Light>(); gl.l.type = LightType.Point; gl.l.color = new Color(0.35f, 1f, 0.85f); gl.l.range = 2.6f; gl.l.intensity = gl.baseI;
                    }
                    if (gl != null) glowsC.Add(gl);
                }
                // the dead of earlier days: skeletons lying, skulls, loose bones
                for (int k = 0, n = Mathf.Max(2, Mathf.RoundToInt(cave / 350)); k < n; k++)
                {
                    if (!Spot("cave", 0.6f, 99, 0.6f, out p)) continue;
                    MeshGen.Obj(Cave.CaveMesh(Rr(0, 1) < 0.6f ? "anat_lie" : "anat_curl", Vector3.one * Rr(0.95f, 1.03f)), mats["BONE"], T, L3(p), Quaternion.Euler(0, Rr(0, 360), 0), false, "Skeleton");
                    colliders.Add(Collider2.Circle(-p.x, p.y, 0.4f));
                }
                for (int k = 0, n = Mathf.RoundToInt(cave / 250); k < n; k++) { if (!Spot("cave", 0.2f, 99, 0.15f, out p)) continue; MeshGen.Obj(Cave.CaveMesh("skull", Vector3.one * Rr(0.85f, 1f)), mats["BONE"], T, L3(p, -0.015f), Quaternion.Euler(Rr(-15, 15), Rr(0, 360), Rr(-25, 25)), true, "Skull"); }
                for (int k = 0, n = Mathf.RoundToInt(cave / 40); k < n; k++) { if (!Spot("cave", 0.1f, 99, 0.1f, out p)) continue; MeshGen.Obj(Cave.CaveMesh(BONES[rnd.Next(BONES.Length)], Vector3.one * Rr(0.9f, 1.05f)), mats["BONE"], T, L3(p, -0.005f), Quaternion.Euler(0, Rr(0, 360), 0), false, "LooseBone"); }
                // what the miners left: burnt-out torches, pickaxes
                for (int k = 0, n = Mathf.RoundToInt(cave / 500); k < n; k++)
                {
                    if (!Spot("cave", 0.3f, 99, 0.2f, out p)) continue;
                    var t = new GameObject("OldTorch").transform; t.SetParent(T, false); t.localPosition = L3(p, 0.05f);
                    t.localRotation = Quaternion.Euler(0, Rr(0, 360), 0) * Quaternion.Euler(Rr(0, 1) < 0.5f ? Rr(55, 70) : 86, 0, 0);
                    MeshGen.Obj(Cave.CaveMesh("torch_stick", Vector3.one * 0.55f), mats["WOOD"], t, new Vector3(0, 0.22f, 0), Quaternion.identity, true, "TorchStick");
                    MeshGen.Obj(Cave.CaveMesh("torch_head", Vector3.one * 0.55f), mats["DARK"], t, new Vector3(0, 0.22f, 0), Quaternion.identity, true, "TorchHead");
                }
                for (int k = 0, n = Mathf.RoundToInt(cave / 600); k < n; k++)
                {
                    if (!Spot("cave", 0.3f, 99, 0.2f, out p)) continue;
                    var t = new GameObject("Pickaxe").transform; t.SetParent(T, false); t.localPosition = L3(p, 0.03f);
                    t.localRotation = Quaternion.Euler(0, Rr(0, 360), 0) * Quaternion.Euler(88, 0, 0);
                    MeshGen.Obj(Cave.CaveMesh("pick_handle", Vector3.one * 0.8f), mats["WOOD"], t, Vector3.zero, Quaternion.identity, true, "Handle");
                    MeshGen.Obj(Cave.CaveMesh("pick_head", Vector3.one * 0.8f), mats["STEEL"], t, Vector3.zero, Quaternion.identity, true, "Pick");
                }
                // light through the rock: shafts from breaks in the vault, beams from cracks in the walls
                for (int k = 0, n = Mathf.Max(1, Mathf.RoundToInt(cave / 420)); k < n; k++)
                {
                    if (!Spot("cave", 1.2f, 99, 0.3f, out p)) continue;
                    var from = L3(p + new Vector2(Rr(-0.7f, 0.7f), Rr(-0.7f, 0.7f)), 7.5f); var to = L3(p);
                    Beam(T, to, from, Rr(0.45f, 0.8f), 160, Color.Lerp(new Color(1f, 0.93f, 0.78f), new Color(0.8f, 0.9f, 1f), Rr(0, 1)), Rr(3.2f, 5.5f), Rr(20, 30));
                }
                for (int k = 0, n = Mathf.Max(1, Mathf.RoundToInt(cave / 500)); k < n; k++)
                {
                    if (!Spot("cave", 0.6f, 1.6f, 0.3f, out p)) continue;
                    var tw = WallDir(p); if (tw == Vector2.zero) continue;
                    var to = L3(p - tw * 0.6f); var from = L3(p + tw * (WallDist(p) + 3.5f), Rr(1.9f, 2.4f));
                    Beam(T, to, from, Rr(0.38f, 0.5f), 130, Color.Lerp(new Color(1f, 0.9f, 0.75f), new Color(0.85f, 0.9f, 1f), Rr(0, 1)), Rr(3.4f, 4.5f), Rr(20, 24));
                }
                // the dead walled into the rock (their backs sunk into it), a torch still burning on a wall
                for (int k = 0, n = Mathf.Max(1, Mathf.RoundToInt(cave / 600)); k < n; k++)
                {
                    if (!Spot("cave", 0.25f, 0.6f, 0.4f, out p)) continue;
                    var tw = WallDir(p); if (tw == Vector2.zero) continue;
                    var at = p + tw * (WallDist(p) + 0.07f); var face = -tw; var fw = L3(face) - L3(Vector2.zero);
                    MeshGen.Obj(Cave.CaveMesh(Rr(0, 1) < 0.5f ? "anat_sit" : "anat_stand", Vector3.one * Rr(0.95f, 1.05f)), mats["BONE"], T, L3(at, -0.02f), Quaternion.LookRotation(fw) * Quaternion.Euler(-Rr(2, 8), 0, 0), false, "WallSkeleton");
                }
                for (int k = 0, n = Mathf.Max(1, Mathf.RoundToInt(cave / 800)); k < n; k++)
                {
                    if (!Spot("cave", 0.3f, 0.7f, 0.3f, out p)) continue;
                    var tw = WallDir(p); if (tw == Vector2.zero) continue;
                    var at = p + tw * (WallDist(p) - 0.03f); var fw = L3(-tw) - L3(Vector2.zero);
                    var holder = new GameObject("WallTorch").transform; holder.SetParent(T, false); holder.localPosition = L3(at, 1.3f); holder.localRotation = Quaternion.LookRotation(fw);
                    var kk = Vector3.one * 0.55f;
                    MeshGen.Obj(Cave.CaveMesh("torch_wall_stick", kk), mats["DARK"], holder, Vector3.zero, Quaternion.identity, true, "TorchBracket");
                    MeshGen.Obj(Cave.CaveMesh("torch_wall_head", kk), mats["DARK"], holder, Vector3.zero, Quaternion.identity, true, "TorchHead");
                    GlyphLayer.MakeFire(holder, new Vector3(0, 0.39f, 0.2f), 0.04f, 0.04f, 0.15f, 0.05f, 16, 0.6f);
                    var lg = new GameObject("Ember"); lg.transform.SetParent(holder, false); lg.transform.localPosition = new Vector3(0, 0.55f, 0.45f);
                    var em = lg.AddComponent<Light>(); em.type = LightType.Point; em.color = new Color(1f, 0.55f, 0.25f); em.intensity = 0.7f; em.range = 2.8f; embers.Add(em);
                }
                // sticky mud in the open (it slows the step)
                for (int k = 0, n = Mathf.RoundToInt(cave / 420); k < n; k++)
                {
                    if (!Spot("cave", 1.3f, 99, 0.9f, out p)) continue;
                    var md = new Pool { c = p, rx = Rr(0.8f, 1.5f), ry = Rr(0.6f, 1.0f), ang = Rr(0, 3.14f) }; muds.Add(md);
                    Patch(T, mats["MUD"], (x, z) => PoolK(md, -x, z), L3(md.c) is Vector3 lc ? new Vector2(lc.x, lc.z) : Vector2.zero, Mathf.Max(md.rx, md.ry) + 0.4f, 0.015f, "Mud");
                }
                // fireflies, here and there
                for (int k = 0, n = Mathf.Max(1, Mathf.RoundToInt(cave / 900)); k < n; k++)
                {
                    if (!Spot("cave", 1.0f, 99, 0, out p)) continue;
                    var pts = new List<Vector3>(); var sd = new List<Vector2>();
                    for (int i = 0; i < 18; i++) { var q = p + new Vector2(Rr(-3, 3), Rr(-3, 3)); if (!Walk(q)) continue; pts.Add(L3(q, Rr(0.3f, 1.7f))); sd.Add(new Vector2(0, Rr(0, 1))); }
                    if (pts.Count > 0) GlyphLayer.MakeMotes(T, Vector3.zero, Quaternion.identity, Vector3.one, pts, sd, 1, new Color(0.8f, 1f, 0.4f) * 1.3f, new Vector4(0.045f, 0, 0, 0.45f));
                }
            }
            if (outdoor && meadow > 1) Outdoor(mats);
            // in an inn: tables, benches, barrels, warm lights
            bool inn = false; foreach (var t in myTiles) if (t.b == "tavern") { inn = true; break; }
            if (inn) Inn(mats);
        }
        // ================= the open land: as the test world's field and forest =================
        // a road between the ways out of it (the inn's door, the cave's mouth), paths off it to the edges of the map,
        // tall grass waving in the wind, a forest round the meadow (oaks, birches, pines, bushes), lone trees, flowers,
        // haystacks, a cart, fireflies
        readonly List<Vector2[]> roads = new List<Vector2[]>();
        float RoadD(Vector2 m)
        {
            float best = 99;
            foreach (var r in roads) for (int i = 0; i + 1 < r.Length; i++) { var ab = r[i + 1] - r[i]; float t = Mathf.Clamp01(Vector2.Dot(m - r[i], ab) / Mathf.Max(1e-5f, ab.sqrMagnitude)); best = Mathf.Min(best, (m - (r[i] + ab * t)).magnitude); }
            return best;
        }
        /// the whole way from a to b over the 1 m grid (map metres), smoothed
        public List<Vector2> PathPoints(Vector2 a, Vector2 b)
        {
            var res = new List<Vector2>();
            int ai = Mathf.FloorToInt(a.x - gx0), aj = Mathf.FloorToInt(a.y - gy0), bi = Mathf.FloorToInt(b.x - gx0), bj = Mathf.FloorToInt(b.y - gy0);
            if (ai < 0 || aj < 0 || ai >= pw || aj >= ph || bi < 0 || bj < 0 || bi >= pw || bj >= ph) return res;
            var prev = new int[pw * ph]; for (int q = 0; q < prev.Length; q++) prev[q] = -2;
            var Q = new Queue<int>(); int start = aj * pw + ai, goal = bj * pw + bi; prev[start] = -1; Q.Enqueue(start);
            while (Q.Count > 0)
            {
                int c = Q.Dequeue(); if (c == goal) break; int ci = c % pw, cj = c / pw;
                for (int d = 0; d < 8; d++)
                {
                    int ni = ci + (d == 0 || d == 4 || d == 7 ? 1 : d == 1 || d == 5 || d == 6 ? -1 : 0), nj = cj + (d == 2 || d == 4 || d == 5 ? 1 : d == 3 || d == 6 || d == 7 ? -1 : 0);
                    if (ni < 0 || nj < 0 || ni >= pw || nj >= ph) continue;
                    int n = nj * pw + ni; if (prev[n] != -2 || !pwalk[n]) continue;
                    prev[n] = c; Q.Enqueue(n);
                }
            }
            if (prev[goal] == -2) return res;
            for (int cur = goal; cur != -1; cur = prev[cur]) res.Add(new Vector2(gx0 + cur % pw + 0.5f, gy0 + cur / pw + 0.5f));
            res.Reverse(); res[0] = a; res[res.Count - 1] = b;
            // fewer points, then rounded corners (Chaikin), then a little wander
            var thin = new List<Vector2>(); for (int i = 0; i < res.Count; i += 3) thin.Add(res[i]); if (thin[thin.Count - 1] != b) thin.Add(b);
            for (int it = 0; it < 2; it++)
            {
                var sm = new List<Vector2> { thin[0] };
                for (int i = 0; i + 1 < thin.Count; i++) { sm.Add(Vector2.Lerp(thin[i], thin[i + 1], 0.25f)); sm.Add(Vector2.Lerp(thin[i], thin[i + 1], 0.75f)); }
                sm.Add(thin[thin.Count - 1]); thin = sm;
            }
            return thin;
        }
        static Vector2[] Local(List<Vector2> m) { var r = new Vector2[m.Count]; for (int i = 0; i < m.Count; i++) r[i] = new Vector2(-m[i].x, m[i].y); return r; }
        bool OtherPlace(Vector2 m)
        {   // the footprint of another place of the map (the inn's house, the mountain over a cave)
            long k = Key(Mathf.FloorToInt(m.x / map.tile), Mathf.FloorToInt(m.y / map.tile));
            foreach (var t in map.tiles) if (Key(t.i, t.j) == k) { foreach (var my in myTiles) if (my.i == t.i && my.j == t.j) return false; return true; }
            return false;
        }
        void Outdoor(Mats mats)
        {
            var T = root.transform;
            System.Func<string, Material> seeMat = k => (k == "LEAF" || k == "BARK" || k == "BIRCH" || k == "DARK") ? NatureLib.SeeThrough(mats[k]) : null;
            // ---- the road from the inn's door to every other way out; paths to the map's exits ----
            var ways = new List<Vector2>(); Vector2 hub = Vector2.zero; bool hasHub = false;
            foreach (var g in passages) { var p0 = g.P(region) - g.N(region) * 1.2f; if (g.Style(region) == "house" && !hasHub) { hub = p0; hasHub = true; } else ways.Add(p0); }
            if (!hasHub && ways.Count > 0) { hub = ways[0]; ways.RemoveAt(0); hasHub = true; }
            if (hasHub)
            {
                foreach (var w in ways)
                {
                    var line = PathPoints(hub, w); if (line.Count < 2) continue; roads.Add(line.ToArray());
                    Forest.PathStrip(T, mats["ROAD"], Local(line), 1.35f, 0.006f, "Road");
                    foreach (float off in new[] { -0.55f, 0.55f })
                    {
                        var rl = new List<Vector2>();
                        for (int i = 0; i < line.Count; i++) { var d = (i + 1 < line.Count ? line[i + 1] - line[i] : line[i] - line[i - 1]).normalized; rl.Add(line[i] + new Vector2(-d.y, d.x) * off); }
                        Forest.PathStrip(T, mats["MUD"], Local(rl), 0.13f, 0.012f, "Rut");
                    }
                }
                foreach (var ex in map.exits)
                {
                    var c = ex.r.center; Vector2 best = c; float bd = 1e9f;
                    for (int k = 0; k < 200; k++) { var q = c + new Vector2(Rr(-8, 8), Rr(-8, 8)); if (!Walk(q) || BiomeAt(q) != "meadow") continue; float d = (q - c).magnitude; if (d < bd) { bd = d; best = q; } }
                    if (bd > 9) continue;
                    var line = PathPoints(hub, best); if (line.Count < 2) continue; roads.Add(line.ToArray());
                    Forest.PathStrip(T, mats["ROAD"], Local(line), 0.55f, 0.007f, "Path");
                }
            }
            bool Open(Vector2 m, float r) { return Walk(m) && BiomeAt(m) == "meadow" && RoadD(m) > 1.6f + r && Free(m, r); }
            Vector2 p;
            // ---- the forest round the meadow (outside its edge, not over another place) ----
            var forestAt = new List<Vector2>();
            int nf = Mathf.RoundToInt(gw * gh * CELL * CELL / 5f);
            for (int k = 0; k < nf * 4 && forestAt.Count < 520; k++)
            {
                var m = new Vector2(Rr(gx0, gx0 + gw * CELL), Rr(gy0, gy0 + gh * CELL));
                float sd = SdfAt(m.x, m.y); if (sd > -0.9f || sd < -9f || LabAt(m.x, m.y) != 1 || OtherPlace(m)) continue;
                bool near = false; foreach (var q in forestAt) if ((q - m).sqrMagnitude < 2.4f * 2.4f) { near = true; break; } if (near) continue;
                foreach (var g in passages) if ((g.P(region) - m).sqrMagnitude < 8f * 8f) near = true; if (near) continue;
                forestAt.Add(m);
                float r = Rr(0, 1);
                if (r < 0.62f) { string nm = r < 0.2f ? (Rr(0, 1) < 0.5f ? "oak1" : "oak3") : r < 0.4f ? "birch" + (Rr(0, 1) < 0.5f ? "3" : "5") : "pine" + (new[] { "1", "2", "3", "5" })[rnd.Next(4)]; try { NatureLib.Place(nm, mats, T, L3(m, -0.02f), Rr(0, 360), Rr(0.8f, 1.2f), seeMat); occluders.Add(new Vector4(-m.x, m.y, 1.8f, 5.5f)); } catch (Exception) { } }
                else try { NatureLib.Place(Rr(0, 1) < 0.5f ? "bush1" : Rr(0, 1) < 0.5f ? "bush2" : "bushberry", mats, T, L3(m), Rr(0, 360), Rr(0.8f, 1.3f)); } catch (Exception) { }
            }
            float area = AreaOf("meadow");
            // ---- lone trees on the meadow, haystacks, a cart ----
            for (int k = 0, n = Mathf.Max(2, Mathf.RoundToInt(area / 260)); k < n; k++)
            {
                if (!SpotWhere(m => Open(m, 2f) && WallDist(m) > 3f, out p)) continue;
                try { NatureLib.Place(Rr(0, 1) < 0.6f ? "oak1" : "oak5", mats, T, L3(p, -0.02f), Rr(0, 360), Rr(0.9f, 1.3f), seeMat); colliders.Add(Collider2.Circle(-p.x, p.y, 0.45f)); occluders.Add(new Vector4(-p.x, p.y, 2.6f, 6f)); } catch (Exception) { }
            }
            for (int k = 0, n = Mathf.RoundToInt(area / 450); k < n; k++)
            {
                if (!SpotWhere(m => Open(m, 0.8f), out p)) continue;
                try { NatureLib.Place("hay", mats, T, L3(p), Rr(0, 360), Rr(0.9f, 1.15f)); colliders.Add(Collider2.Circle(-p.x, p.y, 0.6f)); } catch (Exception) { }
            }
            if (roads.Count > 0 && SpotWhere(m => Walk(m) && RoadD(m) > 1.5f && RoadD(m) < 2.6f && Free(m, 0.8f), out p))
                try { NatureLib.Place("cart", mats, T, L3(p), Rr(0, 360), 1f); colliders.Add(Collider2.Circle(-p.x, p.y, 0.75f)); } catch (Exception) { }
            // ---- flowers in clumps: poppies, cornflowers, daisies; ferns by the forest ----
            var POPPY = new Dictionary<string, string> { { "FLOWERB", "RED" }, { "WHEAT", "RED" } };
            var DAISY = new Dictionary<string, string> { { "FLOWERB", "FLOWERW" } };
            for (int c = 0, n = Mathf.RoundToInt(area / 110); c < n; c++)
            {
                if (!SpotWhere(m => Open(m, 0.4f), out p)) continue;
                var kind = c % 3 == 0 ? POPPY : c % 3 == 1 ? DAISY : null;
                for (int i = 0; i < 6; i++) { var q = p + new Vector2(Rr(-1.1f, 1.1f), Rr(-0.8f, 0.8f)); if (!Open(q, 0)) continue; try { NatureLib.Place(Rr(0, 1) < 0.6f ? "flowers" : "flower3", mats, T, L3(q), Rr(0, 360), Rr(0.8f, 1.2f), null, false, kind); } catch (Exception) { } }
            }
            for (int k = 0, n = Mathf.RoundToInt(area / 40); k < n; k++)
            {
                if (!SpotWhere(m => Open(m, 0.2f) && WallDist(m) < 2.5f, out p)) continue;
                try { NatureLib.Place(Rr(0, 1) < 0.8f ? (Rr(0, 1) < 0.5f ? "fern1" : "fern3") : "bush2", mats, T, L3(p), Rr(0, 360), Rr(0.7f, 1.1f), null, false); } catch (Exception) { }
            }
            // ---- tall grass everywhere else, in 10 m patches (each switched off with its piece of the meadow) ----
            var blocksG = new Dictionary<long, KeyValuePair<List<Vector3>, List<float>>>();
            int ng = Mathf.RoundToInt(area * 70);
            for (int k = 0; k < ng; k++)
            {
                var m = new Vector2(Rr(gx0, gx0 + gw * CELL), Rr(gy0, gy0 + gh * CELL));
                if (!Walk(m) || BiomeAt(m) != "meadow" || RoadD(m) < 1.1f) continue;
                bool blocked = false; foreach (var c in colliders) if (c.circle && (new Vector2(-m.x - c.x, m.y - c.z)).magnitude < c.r * 0.8f) { blocked = true; break; } if (blocked) continue;
                long key = Key(Mathf.FloorToInt(m.x / 10), Mathf.FloorToInt(m.y / 10));
                KeyValuePair<List<Vector3>, List<float>> l; if (!blocksG.TryGetValue(key, out l)) { l = new KeyValuePair<List<Vector3>, List<float>>(new List<Vector3>(), new List<float>()); blocksG[key] = l; }
                l.Key.Add(L3(m)); l.Value.Add(Rr(0, 1));
            }
            foreach (var kv in blocksG)
            {
                var holder = new GameObject("Grass"); holder.transform.SetParent(T, false);
                GlyphLayer.MakeGrass(kv.Value.Key, kv.Value.Value, holder.transform, new Color(0.16f, 0.50f, 0.24f), new Color(0.50f, 0.78f, 0.36f), 1.35f);
            }
            // ---- fireflies over the meadow ----
            {
                var pts = new List<Vector3>(); var sd = new List<Vector2>();
                for (int i = 0; i < 200 && pts.Count < 80; i++) { var m = new Vector2(Rr(gx0, gx0 + gw * CELL), Rr(gy0, gy0 + gh * CELL)); if (!Walk(m)) continue; pts.Add(L3(m, Rr(0.4f, 1.6f))); sd.Add(new Vector2(0, Rr(0, 1))); }
                if (pts.Count > 0) GlyphLayer.MakeMotes(T, Vector3.zero, Quaternion.identity, Vector3.one, pts, sd, 1, new Color(0.85f, 1f, 0.45f) * 1.3f, new Vector4(0.045f, 0, 0, 0.55f));
            }
        }
        bool SpotWhere(System.Func<Vector2, bool> ok, out Vector2 p)
        {
            for (int k = 0; k < 120; k++) { p = new Vector2(Rr(gx0, gx0 + gw * CELL), Rr(gy0, gy0 + gh * CELL)); if (ok(p)) return true; }
            p = Vector2.zero; return false;
        }
        readonly List<Pool> muds = new List<Pool>(); readonly List<Light> embers = new List<Light>();
        public override float SpeedK(float x, float z) { foreach (var m in muds) if (PoolK(m, -x, z) < 0.95f) return 0.5f; return 1; }
        /// toward the nearest rock from a point of the ground (the slope of the distance field), zero if unclear
        Vector2 WallDir(Vector2 m)
        {
            const float E = 0.3f; var g = new Vector2(SdfAt(m.x + E, m.y) - SdfAt(m.x - E, m.y), SdfAt(m.x, m.y + E) - SdfAt(m.x, m.y - E));
            return g.sqrMagnitude < 1e-6f ? Vector2.zero : -g.normalized;
        }
        void Beam(Transform T, Vector3 to, Vector3 from, float radius, int n, Color c, float intensity, float angle)
        {
            var lg = new GameObject("BeamLight"); lg.transform.SetParent(T, false); lg.transform.localPosition = from;
            lg.transform.localRotation = Quaternion.LookRotation(to - from);
            var l = lg.AddComponent<Light>(); l.type = LightType.Spot; l.color = c; l.intensity = intensity; l.range = (to - from).magnitude + 3; l.spotAngle = angle; l.innerSpotAngle = angle * 0.6f;
            l.shadows = LightShadows.None;
            var pts = new List<Vector3>(); var sd = new List<Vector2>();
            for (int i = 0; i < n; i++) { float a = Rr(0, 6.28f), r = Mathf.Sqrt(Rr(0, 1)); pts.Add(new Vector3(Mathf.Cos(a) * r, Rr(0, 1), Mathf.Sin(a) * r)); sd.Add(new Vector2(r, Rr(0, 1))); }
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
                        sp.Add(new Vector3(jx / radius, (y + Rr(-0.4f, 0.4f) * step) / len, jz / radius)); ss.Add(new Vector2(r, Rr(0, 1)));
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
        const float WATER_Y = 0.04f;
        void WaterMesh(Transform T, Mats mats, Pool pl)
        {
            const float Q = 0.12f; var vs = new List<Vector3>(); var ts = new List<int>(); float r = Mathf.Max(pl.rx, pl.ry) + 0.3f;
            var c = pl.c;
            for (float x = c.x - r; x < c.x + r; x += Q)
                for (float y = c.y - r; y < c.y + r; y += Q)
                {
                    if (Mathf.Min(Mathf.Min(PoolK(pl, x, y), PoolK(pl, x + Q, y)), Mathf.Min(PoolK(pl, x, y + Q), PoolK(pl, x + Q, y + Q))) > 1.02f) continue;
                    int o = vs.Count;
                    vs.Add(L3(new Vector2(x, y), WATER_Y)); vs.Add(L3(new Vector2(x, y + Q), WATER_Y)); vs.Add(L3(new Vector2(x + Q, y + Q), WATER_Y)); vs.Add(L3(new Vector2(x + Q, y), WATER_Y));
                    ts.Add(o); ts.Add(o + 2); ts.Add(o + 1); ts.Add(o); ts.Add(o + 3); ts.Add(o + 2);
                }
            if (vs.Count == 0) return;
            var wm = new Mesh { name = "puddle" }; wm.SetVertices(vs); wm.SetTriangles(ts, 0); wm.RecalculateNormals(); wm.RecalculateBounds();
            if (wm.normals[0].y < 0) { for (int i = 0; i < ts.Count; i += 3) { int s = ts[i + 1]; ts[i + 1] = ts[i + 2]; ts[i + 2] = s; } wm.SetTriangles(ts, 0); wm.RecalculateNormals(); }
            var go = MeshGen.Obj(wm, mats["WATER"], T, Vector3.zero, Quaternion.identity, false, "Water");
            var wp = new List<Vector3>(); var wsd = new List<Vector2>();
            for (float y = c.y - r; y <= c.y + r; y += 0.13f)
                for (float x = c.x - r; x <= c.x + r; x += 0.1f) { float kk = PoolK(pl, x, y); if (kk > 1.0f) continue; wp.Add(L3(new Vector2(x, y), WATER_Y)); wsd.Add(new Vector2(kk, Rr(0, 1))); }
            if (wp.Count > 0) GlyphLayer.MakeWater(go.transform, wp, wsd, new Vector4(1, 0, 0, 0));
        }

        void Inn(Mats mats)
        {
            var T = root.transform;

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

        // ---- only what is near the hero is on (the place is far bigger than the screen) ----
        struct Cull { public GameObject go; public Vector3 c; public float r; }
        readonly List<Cull> cull = new List<Cull>(); float cullT;
        void CollectCull()
        {
            foreach (Transform ch in root.transform)
            {
                Bounds b = new Bounds(ch.position, Vector3.one * 2); bool any = false;
                foreach (var r in ch.GetComponentsInChildren<Renderer>(true)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                cull.Add(new Cull { go = ch.gameObject, c = b.center - origin, r = new Vector2(b.extents.x, b.extents.z).magnitude });
            }
        }
        public override void Update(float t)
        {
            if (inn != null) { inn.focus = focus; inn.Update(t); return; }
            // the glowing mushrooms breathe; a drop reaches a puddle: a ring where it falls
            if (mpbC == null) mpbC = new MaterialPropertyBlock();
            foreach (var g in glowsC)
            {
                float k = 0.72f + 0.28f * Mathf.Sin(t * 0.7f + g.ph) * (0.8f + 0.2f * Mathf.Sin(t * 1.9f + g.ph * 2));
                if (g.l != null) g.l.intensity = g.baseI * k;
                foreach (var r in g.caps) { if (r == null || !r.isVisible) continue; r.GetPropertyBlock(mpbC); mpbC.SetColor("_EmissionColor", Mats.Hex(0x3fd8b8) * (0.45f + 0.55f * k)); r.SetPropertyBlock(mpbC); }
            }
            if (lastT >= 0 && addRipple != null)
                foreach (var d in drips)
                {
                    float m0 = Mathf.Floor(t / Cave.DRIP_PERIOD + d.z);
                    for (int q = -1; q <= 1; q++)
                    {
                        float m = m0 + q, h = Frac(m * 0.1031f + d.z * 0.6180f); h *= h + 33.33f; h *= h + h; h = Frac(h);
                        float land = (m - d.z) * Cave.DRIP_PERIOD + (h - 0.5f) * 1.5f * Cave.DRIP_PERIOD / 3.5f + Cave.DRIP_FALL;
                        if (lastT < land && t >= land && (new Vector2(-d.x - focus.x, d.y - focus.z)).sqrMagnitude < 400) { addRipple(-d.x, d.y, 0.85f); if (onDrip != null) onDrip(-d.x, d.y); }
                    }
                }
            lastT = t;
            foreach (var e in embers) if (e != null) e.intensity = 0.55f + 0.2f * Mathf.PerlinNoise(t * 3f + e.GetInstanceID() * 0.1f, 0.3f);
            cullT -= Time.deltaTime; if (cullT > 0) return; cullT = 0.2f;
            foreach (var c in cull)
            {
                bool on = (new Vector2(c.c.x - focus.x, c.c.z - focus.z)).magnitude - c.r < 24f;
                if (c.go.activeSelf != on) c.go.SetActive(on);
            }
        }
        public void CullNow() { cullT = 0; Update(0); }
        // ---- sight: rays from the hero over the ground, every way; what lies past the rock they hit is not drawn
        // (the shaders read this mask: _VisTex over _VisRect). A ray goes on 0.9 m into the rock, so the wall's face
        // shows and its top beyond does not. ----
        const int VN = 256; const float VC = 0.25f; const int RAYS = 720; const float VR = 30f;
        Texture2D visTex; byte[] visPx;
        public Texture2D VisUpdate(Vector3 heroW, out Vector4 rect)
        {
            if (visTex == null) { visTex = new Texture2D(VN, VN, TextureFormat.R8, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "StoryVis" }; visPx = new byte[VN * VN]; }
            float x0 = Mathf.Floor(heroW.x / VC) * VC - VN * VC / 2, z0 = Mathf.Floor(heroW.z / VC) * VC - VN * VC / 2;
            System.Array.Clear(visPx, 0, visPx.Length);
            var h = ToMap(heroW);
            for (int r = 0; r < RAYS; r++)
            {
                float a = r * Mathf.PI * 2 / RAYS; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float hit = -1;
                for (float t = 0; t < VR; t += 0.2f)
                {
                    var m = h + d * t;
                    if (hit < 0 && !Walk(m)) hit = t;
                    if (hit >= 0 && t - hit > 0.9f) break;
                    // map -> world: x = origin.x - m.x, z = origin.z + m.y
                    int i = (int)((origin.x - m.x - x0) / VC), j = (int)((origin.z + m.y - z0) / VC);
                    if (i < 0 || j < 0 || i >= VN || j >= VN) break;
                    visPx[j * VN + i] = 255;
                }
            }
            visTex.LoadRawTextureData(visPx); visTex.Apply(false);
            rect = new Vector4(x0, z0, 1f / (VN * VC), 1f / (VN * VC));
            return visTex;
        }
        /// a wall between the camera and the hero shows through round him (as in the test cave)
        float seeFade;
        public override void Cutaway(Vector3 camOff)
        {
            if (inn != null) { inn.focus = focus; inn.Cutaway(camOff); return; }
            if (outdoor) { OccluderSeeThrough(camOff); return; }
            var h = ToMap(focus + origin); var c2 = new Vector2(-camOff.x, camOff.z); float horiz = c2.magnitude; bool hidden = false;
            if (horiz > 1e-3f)
            {
                c2 /= horiz; float tanP = camOff.y / horiz;
                for (float t = 0.3f; t < 4.5f && !hidden; t += 0.2f)
                {
                    var q = h + c2 * t; if (Walk(q)) continue;
                    int code = LabAt(q.x, q.y); if (H_TOP[code] > 0.4f + t * tanP) hidden = true;
                }
            }
            seeFade = Mathf.MoveTowards(seeFade, hidden ? 1f : 0f, Time.deltaTime * (hidden ? 1.6f : 2.2f));
            Shader.SetGlobalVector("_SeeHero", new Vector4(focus.x + origin.x, focus.y + 1.0f, focus.z + origin.z, 0.2f));
            Shader.SetGlobalFloat("_SeeFade", Mathf.SmoothStep(0f, 1f, seeFade));
        }
        public string TitleAt(Vector3 w) { return map.ChunkName(ToMap(w)) ?? title; }
    }
}
