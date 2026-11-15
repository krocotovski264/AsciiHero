// Builds the character layer: points laid in rows on every model surface (uv2 = surface coordinates in metres),
// one glyph quad per point. Skinned meshes get a skinned glyph mesh with the same bones, so the characters
// follow the animation. Rendered by the glyph camera only (layer LAYER).
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public static class GlyphLayer
    {
        public const int LAYER = 31;
        public static int count;
        static readonly Dictionary<string, Material> MATS = new Dictionary<string, Material>();

        static Material GlyphMaterial(float row, Vector4 cell, float actor, float smoke, float cloth = 0, float see = 0)
        {
            string key = row + ":" + cell.x + ":" + actor + ":" + smoke + ":" + cloth + ":" + see;
            Material m;
            if (MATS.TryGetValue(key, out m)) return m;
            var sh = Shader.Find("AsciiHero/Glyph");
            if (sh == null) { Debug.LogError("AsciiHero/Glyph shader not found"); return null; }
            m = new Material(sh) { name = "Glyph " + key };
            m.SetFloat("_AsciiRow", row); m.SetFloat("_AsciiActor", actor); m.SetVector("_GlyphCell", cell); m.SetFloat("_GlyphSmoke", smoke); m.SetFloat("_GlyphCloth", cloth); m.SetFloat("_GlyphSee", see);
            MATS[key] = m; return m;
        }

        /// add the character layer to every renderer under root (once per renderer)
        /// limbOfBone (optional): bone index -> limb number 1..5 (0 = body); stored per glyph so torn-off limbs can be hidden.
        /// Returns the created skinned glyph renderers.
        /// source renderer -> its glyph renderer, for the last Build call
        static readonly int[] BAYER8 = {
            0, 32, 8, 40, 2, 34, 10, 42, 48, 16, 56, 24, 50, 18, 58, 26, 12, 44, 4, 36, 14, 46, 6, 38, 60, 28, 52, 20, 62, 30, 54, 22,
            3, 35, 11, 43, 1, 33, 9, 41, 51, 19, 59, 27, 49, 17, 57, 25, 15, 47, 7, 39, 13, 45, 5, 37, 63, 31, 55, 23, 61, 29, 53, 21 };
        static readonly Dictionary<string, Mesh> SAMPLED = new Dictionary<string, Mesh>();
        public static readonly Dictionary<Renderer, Renderer> pairs = new Dictionary<Renderer, Renderer>();
        public static List<Renderer> Build(GameObject root, int[] limbOfBone = null)
        {
            pairs.Clear();
            var made = new List<Renderer>();
            var list = new List<Renderer>(root.GetComponentsInChildren<Renderer>(true));
            foreach (var r in list)
            {
                if (r.gameObject.layer == LAYER || r.transform.Find("Glyphs") != null) continue;
                if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;      // (only throws a shadow)
                var mat = r.sharedMaterial; if (mat == null || !mat.HasProperty("_AsciiRow")) continue;
                var smr = r as SkinnedMeshRenderer;
                Mesh src = smr != null ? smr.sharedMesh : (r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null);
                if (src == null) continue;
                var gm = GlyphMaterial(mat.GetFloat("_AsciiRow"), mat.GetVector("_GlyphCell"), mat.GetFloat("_AsciiActor"), mat.HasProperty("_GlyphSmoke") ? mat.GetFloat("_GlyphSmoke") : 0, mat.HasProperty("_Cloth") ? mat.GetFloat("_Cloth") : 0, mat.HasProperty("_SeeThrough") ? mat.GetFloat("_SeeThrough") : 0);
                if (gm == null) return made;
                int[] cum = null; Mesh mesh;
                // the same static mesh with the same cell (trees, wheat, flowers placed many times): its characters are
                // laid out once and shared
                string ckey = smr == null && limbOfBone == null && mat.GetFloat("_AsciiActor") < 0.5f ? src.GetInstanceID() + ":" + mat.GetVector("_GlyphCell") : null;
                if (ckey != null && SAMPLED.TryGetValue(ckey, out mesh)) { }
                else { mesh = Sample(src, mat.GetVector("_GlyphCell"), smr != null, limbOfBone, out cum); if (ckey != null && mesh != null) SAMPLED[ckey] = mesh; }
                if (mesh == null) continue;
                bool actorMesh = mat.GetFloat("_AsciiActor") > 0.5f;
                var go = new GameObject("Glyphs"); go.layer = LAYER;
                if (smr != null)
                {
                    go.transform.SetParent(r.transform.parent, false);
                    go.transform.localPosition = r.transform.localPosition; go.transform.localRotation = r.transform.localRotation; go.transform.localScale = r.transform.localScale;
                    var g = go.AddComponent<SkinnedMeshRenderer>();
                    mesh.bindposes = src.bindposes;
                    g.sharedMesh = mesh; g.bones = smr.bones; g.rootBone = smr.rootBone; g.sharedMaterial = gm;
                    g.updateWhenOffscreen = true;
                    g.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; g.receiveShadows = false;
                    made.Add(g); pairs[r] = g; if (actorMesh) Register(g, mesh, cum);
                    // keep the pair together (visibility, destruction)
                    new GameObject("Glyphs").transform.SetParent(r.transform, false);
                }
                else
                {
                    go.transform.SetParent(r.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var g = go.AddComponent<MeshRenderer>(); g.sharedMaterial = gm; pairs[r] = g; if (actorMesh) Register(g, mesh, cum);
                    g.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; g.receiveShadows = false;
                }
            }
            return made;
        }

        /// grass blades: one glyph quad per point (positions in the parent's local space)
        /// the characters really in the scene now (the running 'count' also holds every rebuilt and destroyed mesh)
        public static int Recount()
        {
            int n = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (r.gameObject.layer != LAYER || !r.enabled) continue;
                var smr = r as SkinnedMeshRenderer; Mesh m = smr != null ? smr.sharedMesh : (r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null);
                if (m != null) n += m.vertexCount / 4;
            }
            count = n; return n;
        }

        // ---------- characters: even thinning by distance ----------
        class LodEntry { public Renderer r; public Mesh m; public int[] cum; public Bounds b; }
        static readonly List<LodEntry> LODS = new List<LodEntry>();
        /// 0 = every glyph, 1 = every second (checkerboard), 2 = every fourth
        public static int actorLod; public static int actorGlyphs, actorGlyphsDrawn;
        static void Register(Renderer r, Mesh m, int[] cum)
        {
            if (cum == null || cum[2] < 48) return;                 // small pieces keep every glyph
            var e = new LodEntry { r = r, m = m, cum = cum, b = m.bounds }; LODS.Add(e); Apply(e);
            if (LODS.Count > 64) LODS.RemoveAll(x => x.r == null);
        }
        static void Apply(LodEntry e)
        {
            int n = actorLod >= 2 ? e.cum[0] : actorLod == 1 ? e.cum[1] : e.cum[2];
            var d = new UnityEngine.Rendering.SubMeshDescriptor(0, n * 6) { bounds = e.b, firstVertex = 0, vertexCount = e.m.vertexCount };
            e.m.SetSubMesh(0, d, UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
        }
        /// the size the characters' glyphs are drawn at must follow: Shader global _AsciiActorLod = 1, 1.41, 2
        public static void SetActorLod(int level)
        {
            level = Mathf.Clamp(level, 0, 2);
            LODS.RemoveAll(x => x.r == null || x.m == null);
            if (level != actorLod) { actorLod = level; foreach (var e in LODS) Apply(e); }
            Shader.SetGlobalFloat("_AsciiActorLod", level == 2 ? 2f : level == 1 ? 1.4142f : 1f);
            actorGlyphs = 0; actorGlyphsDrawn = 0;
            foreach (var e in LODS) { actorGlyphs += e.cum[2]; actorGlyphsDrawn += level >= 2 ? e.cum[0] : level == 1 ? e.cum[1] : e.cum[2]; }
        }

        // ---------- fire ----------
        /// a fire of rising characters (AsciiHero/Fire): n particles born in an ellipse rx x rz at pos (parent space)
        public static GameObject MakeFire(Transform parent, Vector3 pos, float rx, float rz, float height, float glyph, int n, float speed = 1f)
        {
            var sh = Shader.Find("AsciiHero/Fire"); if (sh == null) { Debug.LogError("AsciiHero/Fire shader not found"); return null; }
            var verts = new List<Vector3>(n * 4); var corners = new List<Vector2>(n * 4); var seeds = new List<Vector2>(n * 4); var tris = new List<int>(n * 6);
            Vector2[] C = { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
            for (int i = 0; i < n; i++)
            {
                float a = Halton(i, 2) * Mathf.PI * 2, rr = Mathf.Pow(Halton(i, 3), 0.7f);
                var p = new Vector3(Mathf.Cos(a) * rr * rx, 0, Mathf.Sin(a) * rr * rz);
                // taller in the middle of the bed, lower at its edge
                var sd = new Vector2(Halton(i, 5), Mathf.Clamp01((1 - rr * 0.6f) * (0.55f + 0.45f * Halton(i, 7))));
                int o = verts.Count;
                for (int k = 0; k < 4; k++) { verts.Add(p); corners.Add(C[k]); seeds.Add(sd); }
                tris.Add(o); tris.Add(o + 2); tris.Add(o + 1); tris.Add(o); tris.Add(o + 3); tris.Add(o + 2);
            }
            var m = new Mesh { name = "fire" };
            m.SetVertices(verts); m.SetUVs(0, corners); m.SetUVs(1, seeds); m.SetTriangles(tris, 0);
            m.bounds = new Bounds(new Vector3(0, height, 0), new Vector3(rx * 2 + height * 2 + 1, height * 5 + 1, rz * 2 + height * 2 + 1));
            var go = new GameObject("Fire"); go.layer = LAYER; go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mat = new Material(sh); mat.SetVector("_FireSize", new Vector4(height, glyph, speed, 0));
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            // the mirror: the water surface itself, in the glyph layer, showing the reflected characters
            var pmf = parent.GetComponent<MeshFilter>(); var rsh = Shader.Find("AsciiHero/WaterRefl");
            if (pmf != null && rsh != null)
            {
                if (reflMat == null) reflMat = new Material(rsh);
                var mg = new GameObject("Mirror"); mg.layer = LAYER; mg.transform.SetParent(parent, false);
                mg.AddComponent<MeshFilter>().sharedMesh = pmf.sharedMesh;
                var mr = mg.AddComponent<MeshRenderer>(); mr.sharedMaterial = reflMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            }
            count += n;
            return go;
        }

        /// The lake's characters: one camera-facing quad per point (seed x: 0 in the middle .. 1 at the shore, y: random);
        /// what each one shows is worked out in the water shader
        static Material reflMat;
        public static GameObject MakeWater(Transform parent, List<Vector3> pts, List<Vector2> seedList) { return MakeWater(parent, pts, seedList, Vector4.zero); }
        public static GameObject MakeWater(Transform parent, List<Vector3> pts, List<Vector2> seedList, Vector4 mode)
        {
            var sh = Shader.Find("AsciiHero/Water"); if (sh == null) { Debug.LogError("AsciiHero/Water shader not found"); return null; }
            int n = pts.Count;
            var verts = new List<Vector3>(n * 4); var corners = new List<Vector2>(n * 4); var seeds = new List<Vector2>(n * 4); var tris = new List<int>(n * 6);
            Vector2[] C = { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
            for (int i = 0; i < n; i++)
            {
                int o = verts.Count;
                for (int k = 0; k < 4; k++) { verts.Add(pts[i]); corners.Add(C[k]); seeds.Add(seedList[i]); }
                tris.Add(o); tris.Add(o + 2); tris.Add(o + 1); tris.Add(o); tris.Add(o + 3); tris.Add(o + 2);
            }
            var m = new Mesh { name = "water", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetUVs(0, corners); m.SetUVs(1, seeds); m.SetTriangles(tris, 0);
            m.RecalculateBounds(); var b = m.bounds; b.Expand(0.6f); m.bounds = b;
            var go = new GameObject("Glyphs"); go.layer = LAYER; go.transform.SetParent(parent, false);      // ("Glyphs" under the water: GlyphLayer.Build leaves it alone)
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var wm = new Material(sh); wm.SetVector("_WaterMode", mode);
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = wm;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            count += n;
            return go;
        }

        /// Moving characters that are not surfaces (see AsciiMotes): mode 0 dust in a beam, 1 fireflies, 2 falling drops.
        /// pts are in the parent's local space; seed x: per-mode value (distance from the beam axis, drop phase), y: random
        public static GameObject MakeMotes(Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, List<Vector3> pts, List<Vector2> seedList, int mode, Color col, Vector4 prm)
        {
            var sh = Shader.Find("AsciiHero/Motes"); if (sh == null) { Debug.LogError("AsciiHero/Motes shader not found"); return null; }
            int n = pts.Count;
            var go = new GameObject("Motes"); go.layer = LAYER; go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = scale;
            var mat = new Material(sh); mat.SetFloat("_MoteMode", mode); mat.SetColor("_MoteColor", col); mat.SetVector("_MoteParams", prm);
            // many motes spread over a location (fireflies, leaves) are split into 5 x 5 m tiles: the tiles off the screen
            // are not drawn
            var tiles = new Dictionary<long, List<int>>(); float TL = n > 150 ? 5f : 1e6f;
            for (int i = 0; i < n; i++)
            {
                long key = (long)Mathf.FloorToInt(pts[i].x / TL) * 100000L + Mathf.FloorToInt(pts[i].z / TL);
                List<int> l; if (!tiles.TryGetValue(key, out l)) { l = new List<int>(); tiles[key] = l; }
                l.Add(i);
            }
            Vector2[] C = { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
            foreach (var kv in tiles)
            {
                int tn = kv.Value.Count;
                var verts = new List<Vector3>(tn * 4); var corners = new List<Vector2>(tn * 4); var seeds = new List<Vector2>(tn * 4); var tris = new List<int>(tn * 6);
                foreach (int i in kv.Value)
                {
                    int o = verts.Count;
                    for (int k = 0; k < 4; k++) { verts.Add(pts[i]); corners.Add(C[k]); seeds.Add(seedList[i]); }
                    tris.Add(o); tris.Add(o + 2); tris.Add(o + 1); tris.Add(o); tris.Add(o + 3); tris.Add(o + 2);
                }
                var m = new Mesh { name = "motes", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(verts); m.SetUVs(0, corners); m.SetUVs(1, seeds); m.SetTriangles(tris, 0);
                m.RecalculateBounds(); var b = m.bounds; b.Expand(new Vector3(2, prm.y + 1, 2)); m.bounds = b;
                var tg = tiles.Count == 1 ? go : new GameObject("MotesTile");
                if (tg != go) { tg.layer = LAYER; tg.transform.SetParent(go.transform, false); }
                tg.AddComponent<MeshFilter>().sharedMesh = m;
                var r = tg.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
            count += n;
            return go;
        }

        /// Grass. The blades are split into tiles of 4 x 4 m, each its own mesh, so the tiles outside the view are not
        /// sent to the GPU at all (as one mesh, every blade of the field ran the vertex shader every frame).
        public static GameObject MakeGrass(List<Vector3> pts, List<float> seedList, Transform parent) { return MakeGrass(pts, seedList, parent, new Color(0.16f, 0.50f, 0.24f), new Color(0.50f, 0.78f, 0.36f), 1f); }
        /// (the same for a field of wheat: other colours, taller blades)
        public static GameObject MakeGrass(List<Vector3> pts, List<float> seedList, Transform parent, Color ca, Color cb, float height)
        {
            var sh = Shader.Find("AsciiHero/Grass"); if (sh == null) { Debug.LogError("AsciiHero/Grass shader not found"); return null; }
            var root = new GameObject("Grass"); root.layer = LAYER; root.transform.SetParent(parent, false);
            var mat = new Material(sh); mat.SetColor("_GrassA", ca); mat.SetColor("_GrassB", cb); mat.SetFloat("_GrassH", height);
            const float TILE = 4f; var tiles = new Dictionary<long, List<int>>();
            for (int i = 0; i < pts.Count; i++)
            {
                long key = (long)Mathf.FloorToInt(pts[i].x / TILE) * 100000L + Mathf.FloorToInt(pts[i].z / TILE);
                List<int> l; if (!tiles.TryGetValue(key, out l)) { l = new List<int>(); tiles[key] = l; }
                l.Add(i);
            }
            Vector2[] C = { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
            foreach (var kv in tiles)
            {
                var ix = kv.Value; int n = ix.Count;
                var verts = new List<Vector3>(n * 4); var corners = new List<Vector2>(n * 4); var seeds = new List<Vector2>(n * 4); var tris = new List<int>(n * 6);
                foreach (int i in ix)
                {
                    int o = verts.Count;
                    for (int k = 0; k < 4; k++) { verts.Add(pts[i]); corners.Add(C[k]); seeds.Add(new Vector2(seedList[i], 0)); }
                    tris.Add(o); tris.Add(o + 2); tris.Add(o + 1); tris.Add(o); tris.Add(o + 3); tris.Add(o + 2);
                }
                var m = new Mesh { name = "grass", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(verts); m.SetUVs(0, corners); m.SetUVs(1, seeds); m.SetTriangles(tris, 0);
                m.RecalculateBounds(); var b = m.bounds; b.Expand(new Vector3(1.2f, 1.2f, 1.2f)); m.bounds = b;      // (the blades stand up and lean out of the tile)
                var go = new GameObject("GrassTile"); go.layer = LAYER; go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
            count += pts.Count;
            return root;
        }

        // rows of points: one per glyph cell centre of the surface coordinates
        static Mesh Sample(Mesh src, Vector4 cell, bool skinned, int[] limbOfBone, out int[] cum)
        {
            cum = null;
            var P = src.vertices; var N = src.normals; var U = new List<Vector2>(); src.GetUVs(1, U);
            if (U.Count != P.Length || N == null || N.Length != P.Length) return null;
            var BW = skinned ? src.boneWeights : null;
            var T = src.triangles;
            float cw = cell.x, ch = cell.y;
            var verts = new List<Vector3>(); var nors = new List<Vector3>(); var corners = new List<Vector2>(); var seeds = new List<Vector2>();
            var bws = new List<BoneWeight>(); var tris = new List<int>();
            var acc = new Dictionary<int, float>(); var ranks = new List<byte>();
            Vector2[] CORNER = { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
            for (int t = 0; t < T.Length; t += 3)
            {
                int ia = T[t], ib = T[t + 1], ic = T[t + 2];
                Vector2 a = U[ia], b = U[ib], c = U[ic];
                float area3 = Vector3.Cross(P[ib] - P[ia], P[ic] - P[ia]).magnitude / 2;
                float det = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y), areaUV = Mathf.Abs(det) / 2;
                if (!(area3 > 1e-10f)) continue;                               // degenerate triangle (was the source of NaN bounds)
                if (areaUV < area3 * 0.6f || areaUV > area3 * 4f)
                {
                    // seam, pole or squeezed mapping: scatter points by real area instead, so no bald patches appear
                    float want = area3 / (cw * ch); int kk = Mathf.FloorToInt(want); if (Random.value < want - kk) kk++;
                    for (int q = 0; q < kk; q++)
                    {
                        float r1 = Halton(t * 7 + q, 2), r2 = Halton(t * 7 + q, 3); if (r1 + r2 > 1) { r1 = 1 - r1; r2 = 1 - r2; }
                        int r4 = (t / 3 + q) & 3; Emit(1 - r1 - r2, r1, r2, t * 31 + q, q, r4 == 0 ? 0 : r4 == 2 ? 1 : 2);
                    }
                    continue;
                }
                int i0 = Mathf.CeilToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / cw - 0.5f), i1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / cw - 0.5f);
                int j0 = Mathf.CeilToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) / ch - 0.5f), j1 = Mathf.FloorToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) / ch - 0.5f);
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                    {
                        float u = (i + 0.5f) * cw, v = (j + 0.5f) * ch;
                        float wb = ((u - a.x) * (c.y - a.y) - (c.x - a.x) * (v - a.y)) / det;
                        float wc = ((b.x - a.x) * (v - a.y) - (u - a.x) * (b.y - a.y)) / det;
                        float wa = 1 - wb - wc;
                        if (wa < 0 || wb < 0 || wc < 0 || (wb == 0 && wc == 0)) continue;
                        Emit(wa, wb, wc, i, j, ((i & 1) == 0 && (j & 1) == 0) ? 0 : (((i + j) & 1) == 0 ? 1 : 2));
                    }

                void Emit(float wa, float wb, float wc, int i, int j, int rank)
                {
                        ranks.Add((byte)rank);
                        Vector3 p = P[ia] * wa + P[ib] * wb + P[ic] * wc;
                        Vector3 n = (N[ia] * wa + N[ib] * wb + N[ic] * wc).normalized;
                        float seed = Mathf.Abs(Mathf.Sin(i * 12.9898f + j * 78.233f) * 43758.5453f) % 1f;
                        BoneWeight bw = new BoneWeight();
                        if (skinned)
                        {
                            acc.Clear();
                            Add(acc, BW[ia], wa); Add(acc, BW[ib], wb); Add(acc, BW[ic], wc);
                            bw = Top4(acc);
                        }
                        // scenery: the ordered-dither rank of the point in its 8x8 block of the surface grid (y < 0), so
                        // a far camera keeps an even lattice of characters instead of random ones (no bald patches)
                        float dith = (BAYER8[(((j % 8) + 8) % 8) * 8 + ((i % 8) + 8) % 8] + 0.5f) / 64f;
                        float limb = skinned && limbOfBone != null && bw.boneIndex0 < limbOfBone.Length ? limbOfBone[bw.boneIndex0] : 0;
                        int o = verts.Count;
                        for (int k = 0; k < 4; k++)
                        {
                            verts.Add(p); nors.Add(n); corners.Add(CORNER[k]); seeds.Add(new Vector2(seed, skinned ? limb : -dith));
                            if (skinned) bws.Add(bw);
                        }
                        tris.Add(o); tris.Add(o + 2); tris.Add(o + 1); tris.Add(o); tris.Add(o + 3); tris.Add(o + 2);
                }
            }
            cum = null;
            if (verts.Count == 0) return null;
            // The glyphs are ordered by rank: first every fourth one of the surface grid (both cell indices even), then
            // the rest of the checkerboard, then the others. Drawing only the first part of the index buffer thins a
            // character evenly - no random holes - when it is small on the screen (SetActorLod).
            {
                int n = ranks.Count; var order = new int[n]; var c3 = new int[3];
                for (int g = 0; g < n; g++) c3[ranks[g]]++;
                int[] at = { 0, c3[0], c3[0] + c3[1] };
                for (int g = 0; g < n; g++) order[at[ranks[g]]++] = g;
                var v2 = new List<Vector3>(n * 4); var n2 = new List<Vector3>(n * 4); var cr2 = new List<Vector2>(n * 4); var sd2 = new List<Vector2>(n * 4);
                var bw2 = new List<BoneWeight>(skinned ? n * 4 : 0);
                for (int g = 0; g < n; g++)
                    for (int kq = 0; kq < 4; kq++)
                    {
                        int o = order[g] * 4 + kq;
                        v2.Add(verts[o]); n2.Add(nors[o]); cr2.Add(corners[o]); sd2.Add(seeds[o]); if (skinned) bw2.Add(bws[o]);
                    }
                verts = v2; nors = n2; corners = cr2; seeds = sd2; bws = bw2;
                cum = new[] { c3[0], c3[0] + c3[1], n };
            }
            var m = new Mesh { name = src.name + " glyphs" };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(verts); m.SetNormals(nors); m.SetUVs(0, corners); m.SetUVs(1, seeds); m.SetTriangles(tris, 0);
            if (skinned) m.boneWeights = bws.ToArray();
            m.RecalculateBounds();
            var bnd = m.bounds; bnd.Expand(1f); m.bounds = bnd;
            count += verts.Count / 4;
            return m;
        }
        static float Halton(int i, int b) { float f = 1, r = 0; i += 1; while (i > 0) { f /= b; r += f * (i % b); i /= b; } return r; }
        static void Add(Dictionary<int, float> acc, BoneWeight w, float k)
        {
            void A(int i, float x) { if (x <= 0) return; float c; acc.TryGetValue(i, out c); acc[i] = c + x * k; }
            A(w.boneIndex0, w.weight0); A(w.boneIndex1, w.weight1); A(w.boneIndex2, w.weight2); A(w.boneIndex3, w.weight3);
        }
        static BoneWeight Top4(Dictionary<int, float> acc)
        {
            var l = new List<KeyValuePair<int, float>>(acc); l.Sort((x, y) => y.Value.CompareTo(x.Value));
            float s = 0; for (int i = 0; i < Mathf.Min(4, l.Count); i++) s += l[i].Value; if (s <= 0) s = 1;
            var b = new BoneWeight();
            if (l.Count > 0) { b.boneIndex0 = l[0].Key; b.weight0 = l[0].Value / s; }
            if (l.Count > 1) { b.boneIndex1 = l[1].Key; b.weight1 = l[1].Value / s; }
            if (l.Count > 2) { b.boneIndex2 = l[2].Key; b.weight2 = l[2].Value / s; }
            if (l.Count > 3) { b.boneIndex3 = l[3].Key; b.weight3 = l[3].Value / s; }
            return b;
        }
    }
}
