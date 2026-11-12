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

        static Material GlyphMaterial(float row, Vector4 cell, float actor, float smoke)
        {
            string key = row + ":" + cell.x + ":" + actor + ":" + smoke;
            Material m;
            if (MATS.TryGetValue(key, out m)) return m;
            var sh = Shader.Find("AsciiHero/Glyph");
            if (sh == null) { Debug.LogError("AsciiHero/Glyph shader not found"); return null; }
            m = new Material(sh) { name = "Glyph " + key };
            m.SetFloat("_AsciiRow", row); m.SetFloat("_AsciiActor", actor); m.SetVector("_GlyphCell", cell); m.SetFloat("_GlyphSmoke", smoke);
            MATS[key] = m; return m;
        }

        /// add the character layer to every renderer under root (once per renderer)
        /// limbOfBone (optional): bone index -> limb number 1..5 (0 = body); stored per glyph so torn-off limbs can be hidden.
        /// Returns the created skinned glyph renderers.
        public static List<Renderer> Build(GameObject root, int[] limbOfBone = null)
        {
            var made = new List<Renderer>();
            var list = new List<Renderer>(root.GetComponentsInChildren<Renderer>(true));
            foreach (var r in list)
            {
                if (r.gameObject.layer == LAYER || r.transform.Find("Glyphs") != null) continue;
                var mat = r.sharedMaterial; if (mat == null || !mat.HasProperty("_AsciiRow")) continue;
                var smr = r as SkinnedMeshRenderer;
                Mesh src = smr != null ? smr.sharedMesh : (r.GetComponent<MeshFilter>() != null ? r.GetComponent<MeshFilter>().sharedMesh : null);
                if (src == null) continue;
                var gm = GlyphMaterial(mat.GetFloat("_AsciiRow"), mat.GetVector("_GlyphCell"), mat.GetFloat("_AsciiActor"), mat.HasProperty("_GlyphSmoke") ? mat.GetFloat("_GlyphSmoke") : 0);
                if (gm == null) return made;
                var mesh = Sample(src, mat.GetVector("_GlyphCell"), smr != null, limbOfBone);
                if (mesh == null) continue;
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
                    made.Add(g);
                    // keep the pair together (visibility, destruction)
                    new GameObject("Glyphs").transform.SetParent(r.transform, false);
                }
                else
                {
                    go.transform.SetParent(r.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var g = go.AddComponent<MeshRenderer>(); g.sharedMaterial = gm;
                    g.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; g.receiveShadows = false;
                }
            }
            return made;
        }

        /// grass blades: one glyph quad per point (positions in the parent's local space)
        public static GameObject MakeGrass(List<Vector3> pts, List<float> seedList, Transform parent)
        {
            var sh = Shader.Find("AsciiHero/Grass"); if (sh == null) { Debug.LogError("AsciiHero/Grass shader not found"); return null; }
            var verts = new List<Vector3>(pts.Count * 4); var corners = new List<Vector2>(pts.Count * 4); var seeds = new List<Vector2>(pts.Count * 4); var tris = new List<int>(pts.Count * 6);
            Vector2[] C = { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
            for (int i = 0; i < pts.Count; i++)
            {
                int o = verts.Count;
                for (int k = 0; k < 4; k++) { verts.Add(pts[i]); corners.Add(C[k]); seeds.Add(new Vector2(seedList[i], 0)); }
                tris.Add(o); tris.Add(o + 2); tris.Add(o + 1); tris.Add(o); tris.Add(o + 3); tris.Add(o + 2);
            }
            var m = new Mesh { name = "grass", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetUVs(0, corners); m.SetUVs(1, seeds); m.SetTriangles(tris, 0);
            m.RecalculateBounds(); var b = m.bounds; b.Expand(2f); m.bounds = b;
            var go = new GameObject("Grass"); go.layer = LAYER; go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = new Material(sh);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            count += pts.Count;
            return go;
        }

        // rows of points: one per glyph cell centre of the surface coordinates
        static Mesh Sample(Mesh src, Vector4 cell, bool skinned, int[] limbOfBone)
        {
            var P = src.vertices; var N = src.normals; var U = new List<Vector2>(); src.GetUVs(1, U);
            if (U.Count != P.Length || N == null || N.Length != P.Length) return null;
            var BW = skinned ? src.boneWeights : null;
            var T = src.triangles;
            float cw = cell.x, ch = cell.y;
            var verts = new List<Vector3>(); var nors = new List<Vector3>(); var corners = new List<Vector2>(); var seeds = new List<Vector2>();
            var bws = new List<BoneWeight>(); var tris = new List<int>();
            var acc = new Dictionary<int, float>();
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
                        Emit(1 - r1 - r2, r1, r2, t * 31 + q, q);
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
                        Emit(wa, wb, wc, i, j);
                    }

                void Emit(float wa, float wb, float wc, int i, int j)
                {
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
                        float limb = skinned && limbOfBone != null && bw.boneIndex0 < limbOfBone.Length ? limbOfBone[bw.boneIndex0] : 0;
                        int o = verts.Count;
                        for (int k = 0; k < 4; k++)
                        {
                            verts.Add(p); nors.Add(n); corners.Add(CORNER[k]); seeds.Add(new Vector2(seed, limb));
                            if (skinned) bws.Add(bw);
                        }
                        tris.Add(o); tris.Add(o + 2); tris.Add(o + 1); tris.Add(o); tris.Add(o + 3); tris.Add(o + 2);
                }
            }
            if (verts.Count == 0) return null;
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
