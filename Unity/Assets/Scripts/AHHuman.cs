// MakeHuman body: variant positions, skeleton (identity rest rotations), regions, body-fitted garment shells.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AsciiHero
{
    public enum Region { Torso, Pelvis, Neck, Head, UpperArm, ForeArm, Hand, Thigh, Shin, Foot }

    public class Human
    {
        public HumanVariant v; public char sex; public int build;
        public int n; public Vector3[] pos, nor; public int[][] adj;
        public Region[] region; public float[] limbT;
        public List<int>[] partTris = new List<int>[4];
        public Transform[] bones; public Transform root; public Transform meshRoot;
        public Matrix4x4[] bindposes;

        class Cache { public Vector3[] nor; public int[][] adj; public Region[] region; public float[] limbT; }
        static readonly Dictionary<string, Cache> CACHE = new Dictionary<string, Cache>();

        public Vector3 Head(string b) { return v.heads[HumanDB.BI[b]]; }
        public Vector3 Tail(string b) { return v.tails[HumanDB.BI[b]]; }
        public Transform Bone(string b) { int i; return HumanDB.BI.TryGetValue(b, out i) ? bones[i] : null; }

        public Human(char sex, int build)
        {
            HumanDB.Load();
            this.sex = sex; this.build = build; v = HumanDB.Variant(sex, build);
            n = HumanDB.nverts; pos = (Vector3[])v.pos.Clone();
            for (int p = 0; p < 4; p++) partTris[p] = new List<int>();
            var T = HumanDB.tris;
            for (int t = 0; t < T.Length; t += 3)
            {
                int part = HumanDB.vpart[T[t]];
                partTris[part].Add(T[t]); partTris[part].Add(T[t + 1]); partTris[part].Add(T[t + 2]);
            }
            string key = sex.ToString() + build;
            Cache c;
            if (CACHE.TryGetValue(key, out c)) { nor = c.nor; adj = c.adj; region = c.region; limbT = c.limbT; }
            else
            {
                nor = VertexNormals(pos, partTris[0], n);
                adj = Adjacency(partTris[0], n);
                ComputeRegions();
                CACHE[key] = new Cache { nor = nor, adj = adj, region = region, limbT = limbT };
            }
        }

        static Vector3[] VertexNormals(Vector3[] p, List<int> idx, int count)
        {
            var nr = new Vector3[count];
            for (int t = 0; t < idx.Count; t += 3)
            {
                int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                var fn = Vector3.Cross(p[b] - p[a], p[c] - p[a]);
                nr[a] += fn; nr[b] += fn; nr[c] += fn;
            }
            for (int i = 0; i < count; i++) nr[i] = nr[i].sqrMagnitude > 0 ? nr[i].normalized : Vector3.up;
            return nr;
        }
        static int[][] Adjacency(List<int> idx, int count)
        {
            var sets = new HashSet<int>[count];
            for (int i = 0; i < count; i++) sets[i] = new HashSet<int>();
            for (int t = 0; t < idx.Count; t += 3)
            {
                int a = idx[t], b = idx[t + 1], c = idx[t + 2];
                sets[a].Add(b); sets[a].Add(c); sets[b].Add(a); sets[b].Add(c); sets[c].Add(a); sets[c].Add(b);
            }
            var r = new int[count][];
            for (int i = 0; i < count; i++) { r[i] = new int[sets[i].Count]; sets[i].CopyTo(r[i]); }
            return r;
        }

        static Region GroupOf(string name)
        {
            if (name.StartsWith("finger") || name.StartsWith("metacarpal") || name.StartsWith("wrist")) return Region.Hand;
            if (name.StartsWith("lowerarm")) return Region.ForeArm;
            if (name.StartsWith("upperarm") || name.StartsWith("shoulder01")) return Region.UpperArm;
            if (name.StartsWith("toe") || name.StartsWith("foot")) return Region.Foot;
            if (name.StartsWith("lowerleg")) return Region.Shin;
            if (name.StartsWith("upperleg")) return Region.Thigh;
            if (name.StartsWith("neck")) return Region.Neck;
            string[] head = { "head", "jaw", "eye", "oculi", "orbicularis", "oris", "risorius", "levator", "temporalis", "special03", "special04", "special05", "special06", "tongue" };
            foreach (var h in head) if (name.StartsWith(h)) return Region.Head;
            if (name.StartsWith("pelvis")) return Region.Pelvis;
            return Region.Torso;
        }

        void ComputeRegions()
        {
            var groups = new Region[HumanDB.bones.Length];
            for (int i = 0; i < groups.Length; i++) groups[i] = GroupOf(HumanDB.bones[i]);
            region = new Region[n]; limbT = new float[n];
            Vector3 aL0 = Head("upperarm01.L"), aL1 = Head("wrist.L"), aR0 = Head("upperarm01.R"), aR1 = Head("wrist.R");
            Vector3 lL0 = Head("upperleg01.L"), lL1 = Head("foot.L"), lR0 = Head("upperleg01.R"), lR1 = Head("foot.R");
            var acc = new float[10];
            for (int i = 0; i < n; i++)
            {
                Array.Clear(acc, 0, acc.Length);
                for (int k = 0; k < 4; k++) acc[(int)groups[HumanDB.skinIndex[i * 4 + k]]] += HumanDB.skinWeight[i * 4 + k];
                int best = 0; for (int g = 1; g < 10; g++) if (acc[g] > acc[best]) best = g;
                var r = (Region)best; region[i] = r;
                var p = pos[i];
                bool left = Vector3.Distance(p, aL0) < Vector3.Distance(p, aR0);
                if (r == Region.UpperArm || r == Region.ForeArm || r == Region.Hand) limbT[i] = left ? Proj(p, aL0, aL1) : Proj(p, aR0, aR1);
                else if (r == Region.Thigh || r == Region.Shin || r == Region.Foot)
                { bool l = Vector3.Distance(p, lL0) < Vector3.Distance(p, lR0); limbT[i] = l ? Proj(p, lL0, lL1) : Proj(p, lR0, lR1); }
            }
        }
        static float Proj(Vector3 p, Vector3 a, Vector3 b) { var d = b - a; return Vector3.Dot(p - a, d) / d.sqrMagnitude; }

        // ---------- skeleton ----------
        public void MakeSkeleton(Transform parent)
        {
            meshRoot = parent;
            int nb = HumanDB.bones.Length; bones = new Transform[nb];
            for (int i = 0; i < nb; i++)
            {
                var go = new GameObject(HumanDB.bones[i]); bones[i] = go.transform;
                int p = HumanDB.parents[i];
                if (p >= 0) { go.transform.SetParent(bones[p], false); go.transform.localPosition = v.heads[i] - v.heads[p]; }
                else { go.transform.SetParent(parent, false); go.transform.localPosition = v.heads[i]; root = go.transform; }
                go.transform.localRotation = Quaternion.identity;
            }
            bindposes = new Matrix4x4[nb];
            for (int i = 0; i < nb; i++) bindposes[i] = bones[i].worldToLocalMatrix * parent.localToWorldMatrix;
        }

        // ---------- meshes ----------
        public Mesh MakeMesh(Vector3[] posAll, List<int> tris)
        {
            var map = new Dictionary<int, int>(); var outIdx = new List<int>(tris.Count);
            foreach (int vi in tris) { int j; if (!map.TryGetValue(vi, out j)) { j = map.Count; map[vi] = j; } outIdx.Add(j); }
            int m = map.Count;
            var p = new Vector3[m]; var bw = new BoneWeight[m];
            foreach (var kv in map)
            {
                int vi = kv.Key, j = kv.Value; p[j] = posAll[vi];
                var w = new BoneWeight();
                w.boneIndex0 = HumanDB.skinIndex[vi * 4]; w.weight0 = HumanDB.skinWeight[vi * 4] / 255f;
                w.boneIndex1 = HumanDB.skinIndex[vi * 4 + 1]; w.weight1 = HumanDB.skinWeight[vi * 4 + 1] / 255f;
                w.boneIndex2 = HumanDB.skinIndex[vi * 4 + 2]; w.weight2 = HumanDB.skinWeight[vi * 4 + 2] / 255f;
                w.boneIndex3 = HumanDB.skinIndex[vi * 4 + 3]; w.weight3 = HumanDB.skinWeight[vi * 4 + 3] / 255f;
                bw[j] = w;
            }
            var mesh = new Mesh();
            if (m > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = p; mesh.boneWeights = bw; mesh.SetTriangles(outIdx, 0);
            mesh.bindposes = bindposes;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        public Mesh PartMesh(int part, float offset = 0f)
        {
            var T = partTris[part]; var p = pos;
            if (offset != 0f)
            {
                var nr = VertexNormals(pos, T, n); p = (Vector3[])pos.Clone();
                foreach (int i in new HashSet<int>(T)) p[i] += nr[i] * offset;
            }
            return MakeMesh(p, T);
        }

        public class ShellOpts { public int smooth; public float reinflate; public bool any; }

        // garment shell: body faces selected by predicate, offset along the normal
        public Mesh Shell(Func<int, bool> pred, Func<int, float> thick, ShellOpts o = null)
        {
            o = o ?? new ShellOpts();
            var sel = new bool[n];
            for (int i = 0; i < n; i++) sel[i] = HumanDB.vpart[i] == 0 && pred(i);
            var T = partTris[0]; var tris = new List<int>();
            for (int t = 0; t < T.Count; t += 3)
            {
                int a = T[t], b = T[t + 1], c = T[t + 2];
                bool ok = o.any ? (sel[a] || sel[b] || sel[c]) : (sel[a] && sel[b] && sel[c]);
                if (ok) { tris.Add(a); tris.Add(b); tris.Add(c); }
            }
            var used = new HashSet<int>(tris);
            var p = (Vector3[])pos.Clone();
            foreach (int i in used) p[i] += nor[i] * thick(i);
            if (o.smooth > 0)
            {
                var inner = new List<int>(used); var tmp = new Vector3[p.Length];
                for (int it = 0; it < o.smooth; it++)
                {
                    Array.Copy(p, tmp, p.Length);
                    foreach (int i in inner)
                    {
                        var a = adj[i]; if (a.Length == 0) continue;
                        Vector3 s = Vector3.zero; foreach (int j in a) s += tmp[j];
                        s /= a.Length; p[i] += (s - p[i]) * 0.5f;
                    }
                }
                if (o.reinflate != 0) foreach (int i in inner) p[i] += nor[i] * o.reinflate;
            }
            return MakeMesh(p, tris);
        }
        public Mesh Shell(Func<int, bool> pred, float thick, ShellOpts o = null) { return Shell(pred, i => thick, o); }
    }
}
