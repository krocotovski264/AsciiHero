// Small procedural mesh library (replaces three.js geometries).
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public static class MeshGen
    {
        static Mesh Finish(List<Vector3> v, List<int> t, List<Vector2> uv = null)
        {
            var m = new Mesh();
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); if (uv != null && uv.Count == v.Count) m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            AddGlyphUV(m, cylNext); cylNext = false;
            return m;
        }
        static bool cylNext;

        /// Surface-glyph coordinates (uv2, metres, object space): planar by the dominant normal axis,
        /// or cylindrical around local Y for turned shapes. u runs left -> right seen from outside, v runs up.
        public static void AddGlyphUV(Mesh m, bool cylindrical)
        {
            var p = m.vertices; var n = m.normals; if (n == null || n.Length != p.Length) { m.RecalculateNormals(); n = m.normals; }
            var uv = new Vector2[p.Length];
            float rm = 0;
            if (cylindrical) { foreach (var q in p) rm += Mathf.Sqrt(q.x * q.x + q.z * q.z); rm = Mathf.Max(0.01f, rm / Mathf.Max(1, p.Length)); }
            for (int i = 0; i < p.Length; i++)
            {
                Vector3 q = p[i], nn = n[i]; float ax = Mathf.Abs(nn.x), ay = Mathf.Abs(nn.y), az = Mathf.Abs(nn.z);
                if (cylindrical && ay < 0.7f) uv[i] = new Vector2(Mathf.Atan2(-q.x, q.z) * rm, q.y);
                else if (ay >= ax && ay >= az) uv[i] = new Vector2(q.x, nn.y > 0 ? q.z : -q.z);
                else if (ax >= az) uv[i] = new Vector2(nn.x > 0 ? q.z : -q.z, q.y);
                else uv[i] = new Vector2(nn.z > 0 ? -q.x : q.x, q.y);
            }
            m.uv2 = uv;
        }
        // quad helper (Unity winding: clockwise seen from the front)
        static void Quad(List<int> t, int a, int b, int c, int d) { t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d); }

        public static Mesh Box(float w, float h, float d)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            Vector3 e = new Vector3(w, h, d) * 0.5f;
            Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var nn in n)
            {
                Vector3 u = Mathf.Abs(nn.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 s = Vector3.Cross(nn, u); u = Vector3.Cross(s, nn);
                int o = v.Count;
                Vector3 c = Vector3.Scale(nn, e);
                Vector3 su = Vector3.Scale(u, e), ss = Vector3.Scale(s, e);
                v.Add(c - ss - su); v.Add(c - ss + su); v.Add(c + ss + su); v.Add(c + ss - su);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
                Quad(t, o, o + 1, o + 2, o + 3);
            }
            return FixOutward(v, t, uv);
        }
        // makes sure every triangle faces away from the mesh centre (robust for convex shapes)
        static Mesh FixOutward(List<Vector3> v, List<int> t, List<Vector2> uv = null)
        {
            Vector3 c = Vector3.zero; foreach (var p in v) c += p; c /= Mathf.Max(1, v.Count);
            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], cc = v[t[i + 2]];
                Vector3 nrm = Vector3.Cross(b - a, cc - a);
                if (Vector3.Dot(nrm, (a + b + cc) / 3f - c) < 0) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; }
            }
            return Finish(v, t, uv);
        }

        /// tapered cylinder along Y, centred; r0 = bottom radius, r1 = top radius
        public static Mesh Cylinder(float r0, float r1, float h, int seg = 12)
        {
            var prof = new List<Vector2> { new Vector2(0, -h / 2), new Vector2(r0, -h / 2), new Vector2(r1, h / 2), new Vector2(0, h / 2) };
            return Lathe(prof, seg);
        }
        /// surface of revolution around Y from a profile of (radius, y)
        public static Mesh Lathe(List<Vector2> prof, int seg = 18)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            int rows = prof.Count;
            for (int r = 0; r < rows; r++)
                for (int s = 0; s <= seg; s++)
                {
                    float a = s * Mathf.PI * 2 / seg;
                    v.Add(new Vector3(Mathf.Cos(a) * prof[r].x, prof[r].y, Mathf.Sin(a) * prof[r].x));
                    uv.Add(new Vector2(s / (float)seg, r / (float)Mathf.Max(1, rows - 1)));
                }
            for (int r = 0; r < rows - 1; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    Quad(t, a, b, b + 1, a + 1);
                }
            cylNext = true;
            return FixRadial(v, t, uv);
        }
        // orient faces away from the Y axis (lathe / tubes)
        static Mesh FixRadial(List<Vector3> v, List<int> t, List<Vector2> uv)
        {
            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 nrm = Vector3.Cross(b - a, c - a); Vector3 m = (a + b + c) / 3f;
                Vector3 radial = new Vector3(m.x, 0, m.z);
                float dot = radial.sqrMagnitude > 1e-8f ? Vector3.Dot(nrm, radial) : nrm.y * m.y;
                if (dot < 0) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; }
            }
            return Finish(v, t, uv);
        }
        public static Mesh Sphere(float r, int seg = 12, int rings = 8, float thetaMax = Mathf.PI)
        {
            var prof = new List<Vector2>();
            for (int i = 0; i <= rings; i++) { float th = thetaMax * i / rings; prof.Add(new Vector2(Mathf.Sin(th) * r, Mathf.Cos(th) * r)); }
            return Lathe(prof, seg);
        }
        /// torus in the XY plane (axis = Z), arc in radians
        public static Mesh Torus(float R, float r, int seg = 16, int tube = 6, float arc = Mathf.PI * 2)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float u = arc * i / seg; var c = new Vector3(Mathf.Cos(u) * R, Mathf.Sin(u) * R, 0);
                for (int j = 0; j <= tube; j++)
                {
                    float w = Mathf.PI * 2 * j / tube;
                    var dir = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0) * Mathf.Cos(w) + Vector3.forward * Mathf.Sin(w);
                    v.Add(c + dir * r);
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < tube; j++)
                {
                    int a = i * (tube + 1) + j, b = a + tube + 1;
                    Quad(t, a, b, b + 1, a + 1);
                }
            // orient away from the tube centre line
            for (int k = 0; k < t.Count; k += 3)
            {
                Vector3 a = v[t[k]], b = v[t[k + 1]], c = v[t[k + 2]]; Vector3 m = (a + b + c) / 3f;
                float ang = Mathf.Atan2(m.y, m.x); var cl = new Vector3(Mathf.Cos(ang) * R, Mathf.Sin(ang) * R, 0);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), m - cl) < 0) { int s = t[k + 1]; t[k + 1] = t[k + 2]; t[k + 2] = s; }
            }
            return Finish(v, t);
        }
        /// extrude a 2D outline (XY) along Z, centred on z = 0
        public static Mesh Extrude(List<Vector2> outline, float depth)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            int n = outline.Count; float hz = depth / 2;
            var tri = Triangulate(outline);
            // front (z = +hz) and back (z = -hz) caps
            for (int i = 0; i < n; i++) v.Add(new Vector3(outline[i].x, outline[i].y, hz));
            for (int i = 0; i < n; i++) v.Add(new Vector3(outline[i].x, outline[i].y, -hz));
            for (int i = 0; i < tri.Count; i += 3)
            {
                t.Add(tri[i]); t.Add(tri[i + 2]); t.Add(tri[i + 1]);           // front faces +z
                t.Add(n + tri[i]); t.Add(n + tri[i + 1]); t.Add(n + tri[i + 2]); // back faces -z
            }
            // fix cap orientation explicitly
            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                float want = a.z > 0 ? 1 : -1;
                if (Vector3.Cross(b - a, c - a).z * want < 0) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; }
            }
            // side walls (separate vertices for sharp edges)
            float area = SignedArea(outline);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n; int o = v.Count;
                v.Add(new Vector3(outline[i].x, outline[i].y, hz)); v.Add(new Vector3(outline[j].x, outline[j].y, hz));
                v.Add(new Vector3(outline[j].x, outline[j].y, -hz)); v.Add(new Vector3(outline[i].x, outline[i].y, -hz));
                Vector2 e = outline[j] - outline[i]; Vector3 outN = area > 0 ? new Vector3(e.y, -e.x, 0) : new Vector3(-e.y, e.x, 0);
                Vector3 nrm = Vector3.Cross(v[o + 1] - v[o], v[o + 2] - v[o]);
                if (Vector3.Dot(nrm, outN) >= 0) { t.Add(o); t.Add(o + 1); t.Add(o + 2); t.Add(o); t.Add(o + 2); t.Add(o + 3); }
                else { t.Add(o); t.Add(o + 2); t.Add(o + 1); t.Add(o); t.Add(o + 3); t.Add(o + 2); }
            }
            return Finish(v, t);
        }
        static float SignedArea(List<Vector2> p) { float a = 0; for (int i = 0; i < p.Count; i++) { var q = p[(i + 1) % p.Count]; a += p[i].x * q.y - q.x * p[i].y; } return a * 0.5f; }
        // ear clipping
        static List<int> Triangulate(List<Vector2> pts)
        {
            var res = new List<int>(); var idx = new List<int>();
            for (int i = 0; i < pts.Count; i++) idx.Add(i);
            bool ccw = SignedArea(pts) > 0; int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                    Vector2 A = pts[a], B = pts[b], C = pts[c];
                    float cr = (B.x - A.x) * (C.y - A.y) - (B.y - A.y) * (C.x - A.x);
                    if (ccw ? cr <= 0 : cr >= 0) continue;
                    bool inside = false;
                    foreach (int k in idx) { if (k == a || k == b || k == c) continue; if (InTri(pts[k], A, B, C)) { inside = true; break; } }
                    if (inside) continue;
                    res.Add(a); res.Add(b); res.Add(c); idx.RemoveAt(i); clipped = true; break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) { res.Add(idx[0]); res.Add(idx[1]); res.Add(idx[2]); }
            return res;
        }
        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
        /// tube along a polyline
        public static Mesh Tube(List<Vector3> path, float radius, int seg = 8)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 d = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 a = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized; Vector3 b = Vector3.Cross(d, a);
                for (int s = 0; s <= seg; s++) { float ang = Mathf.PI * 2 * s / seg; v.Add(path[i] + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * radius); }
            }
            for (int i = 0; i < path.Count - 1; i++)
                for (int s = 0; s < seg; s++) { int a = i * (seg + 1) + s, b = a + seg + 1; Quad(t, a, b, b + 1, a + 1); }
            for (int k = 0; k < t.Count; k += 3)
            {
                int ring = (t[k] / (seg + 1)); Vector3 c = path[Mathf.Clamp(ring, 0, path.Count - 1)];
                Vector3 A = v[t[k]], B = v[t[k + 1]], C = v[t[k + 2]];
                if (Vector3.Dot(Vector3.Cross(B - A, C - A), (A + B + C) / 3f - c) < 0) { int s = t[k + 1]; t[k + 1] = t[k + 2]; t[k + 2] = s; }
            }
            return Finish(v, t);
        }

        public static GameObject Obj(Mesh m, Material mat, Transform parent, Vector3 pos, Quaternion rot, bool shadows = true, string name = "part")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go;
        }
    }
}
