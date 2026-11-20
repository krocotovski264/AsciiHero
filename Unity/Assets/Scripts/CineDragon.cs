// The cinematics' dragon (the "Demon Dragon" of Sketchfab by endlessvoidmc, CC BY): read from demon.bin (made by the
// docs' dexp.py: its mesh, bones and animations baked at 30 frames a second, in Unity's axes), one skinned mesh whose
// bones are laid flat under the dragon's root and set each frame from the baked matrices (a crossfade between two
// animations is a blend of those matrices), with turns laid on top for the jaw, the neck and the head.
// The surface is covered with the game's characters like any actor (GlyphLayer).
// Also: CineParticles, characters thrown by a script (AsciiHero/Breath): the fire, the smoke, the dust.
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AsciiHero
{
    public class CineDragon
    {
        public GameObject root; public Transform[] bones; public string[] names; int[] parent; List<int>[] kids;
        public Dictionary<string, Vector3[]> animP = new Dictionary<string, Vector3[]>();
        public Dictionary<string, Quaternion[]> animR = new Dictionary<string, Quaternion[]>();
        public Dictionary<string, float[]> animS = new Dictionary<string, float[]>();
        public Dictionary<string, int> animN = new Dictionary<string, int>();
        public int nb; public SkinnedMeshRenderer smr;
        Vector3[] P; Quaternion[] R; float[] S;          // the pose being built this frame (model space)

        public static CineDragon Load(byte[] b, Texture2D tex, Material baseMat, float k)
        {
            var d = new CineDragon(); var br = new BinaryReader(new MemoryStream(b));
            if (new string(br.ReadChars(4)) != "DRG1") throw new System.Exception("demon.bin: not a dragon");
            int nb = br.ReadInt32(), nv = br.ReadInt32(), nt = br.ReadInt32(), na = br.ReadInt32(); br.ReadInt32();
            d.nb = nb; d.names = new string[nb]; d.parent = new int[nb]; var bind = new Matrix4x4[nb];
            for (int i = 0; i < nb; i++)
            {
                int l = br.ReadByte(); d.names[i] = new string(br.ReadChars(l)); d.parent[i] = br.ReadInt32();
                var m = new Matrix4x4(); for (int q = 0; q < 16; q++) m[q] = br.ReadSingle();
                m.m03 *= k; m.m13 *= k; m.m23 *= k; bind[i] = m;          // (the size baked in: the characters' cell is in metres)
            }
            var pos = new Vector3[nv]; var nrm = new Vector3[nv]; var uv = new Vector2[nv]; var bw = new BoneWeight[nv];
            for (int i = 0; i < nv; i++) pos[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()) * k;
            for (int i = 0; i < nv; i++) nrm[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            for (int i = 0; i < nv; i++) uv[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
            var bix = new int[nv * 4]; for (int i = 0; i < nv * 4; i++) bix[i] = br.ReadInt16();
            var bwt = new float[nv * 4]; for (int i = 0; i < nv * 4; i++) bwt[i] = br.ReadSingle();
            for (int i = 0; i < nv; i++)
                bw[i] = new BoneWeight { boneIndex0 = bix[i * 4], boneIndex1 = bix[i * 4 + 1], boneIndex2 = bix[i * 4 + 2], boneIndex3 = bix[i * 4 + 3],
                                         weight0 = bwt[i * 4], weight1 = bwt[i * 4 + 1], weight2 = bwt[i * 4 + 2], weight3 = bwt[i * 4 + 3] };
            var tri = new int[nt * 3]; for (int i = 0; i < nt * 3; i++) tri[i] = br.ReadInt32();
            for (int a = 0; a < na; a++)
            {
                int l = br.ReadByte(); string nm = new string(br.ReadChars(l)); int nf = br.ReadInt32();
                var ap = new Vector3[nf * nb]; var ar = new Quaternion[nf * nb]; var asc = new float[nf * nb];
                for (int q = 0; q < nf * nb; q++)
                {
                    ap[q] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()) * k;
                    ar[q] = new Quaternion(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()); asc[q] = br.ReadSingle();
                }
                d.animP[nm] = ap; d.animR[nm] = ar; d.animS[nm] = asc; d.animN[nm] = nf;
            }
            d.kids = new List<int>[nb]; for (int i = 0; i < nb; i++) d.kids[i] = new List<int>();
            for (int i = 0; i < nb; i++) if (d.parent[i] >= 0) d.kids[d.parent[i]].Add(i);
            // the object
            d.root = new GameObject("Dragon");
            d.bones = new Transform[nb];
            for (int i = 0; i < nb; i++) { var t = new GameObject(d.names[i]).transform; t.SetParent(d.root.transform, false); d.bones[i] = t; }
            var mesh = new Mesh { name = "dragon", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = pos; mesh.normals = nrm; mesh.uv = uv; mesh.boneWeights = bw; mesh.triangles = tri;
            var bp = new Matrix4x4[nb]; for (int i = 0; i < nb; i++) bp[i] = bind[i].inverse; mesh.bindposes = bp;
            mesh.RecalculateBounds(); MeshGen.AddGlyphUV(mesh, false);
            var body = new GameObject("Body"); body.transform.SetParent(d.root.transform, false);
            d.smr = body.AddComponent<SkinnedMeshRenderer>(); d.smr.sharedMesh = mesh; d.smr.bones = d.bones; d.smr.rootBone = d.bones[0];
            d.smr.updateWhenOffscreen = true;
            var mat = new Material(baseMat) { name = "Dragon" };
            mat.SetTexture("_MainTex", tex); mat.SetColor("_Color", new Color(1.15f, 1.05f, 1.0f));
            mat.SetFloat("_AsciiActor", 1); mat.SetFloat("_Cull", 0); mat.SetFloat("_Glossiness", 0.38f); mat.SetFloat("_Metallic", 0.05f);
            mat.SetVector("_GlyphCell", new Vector4(0.085f, 0.14f, 0, 0));          // (a 20 m beast: its characters as large on it as the hero's on him at ~5x the distance)
            d.smr.sharedMaterial = mat;
            d.P = new Vector3[nb]; d.R = new Quaternion[nb]; d.S = new float[nb];
            d.Pose("idle", 0, null, 0, 0); d.Apply();
            return d;
        }

        public int Bone(string prefix) { for (int i = 0; i < nb; i++) if (names[i].StartsWith(prefix)) return i; return -1; }
        public float Duration(string a) { return (animN[a] - 1) / 30f; }

        void Sample(string a, float t, bool loop, Vector3[] p, Quaternion[] r, float[] s)
        {
            int n = animN[a]; float f = t * 30f;
            if (loop) { f %= (n - 1); if (f < 0) f += n - 1; } else f = Mathf.Clamp(f, 0, n - 1.001f);
            int f0 = Mathf.FloorToInt(f), f1 = Mathf.Min(n - 1, f0 + 1); float u = f - f0;
            var AP = animP[a]; var AR = animR[a]; var AS = animS[a];
            for (int i = 0; i < nb; i++)
            {
                p[i] = Vector3.Lerp(AP[f0 * nb + i], AP[f1 * nb + i], u);
                r[i] = Quaternion.Slerp(AR[f0 * nb + i], AR[f1 * nb + i], u); s[i] = Mathf.Lerp(AS[f0 * nb + i], AS[f1 * nb + i], u);
            }
        }
        Vector3[] P2; Quaternion[] R2; float[] S2;
        /// the pose: animation a at time t (looped), crossfaded (w) into animation b at time tb
        public void Pose(string a, float t, string b, float tb, float w, bool loopA = true, bool loopB = true)
        {
            Sample(a, t, loopA, P, R, S);
            if (b != null && w > 0.001f)
            {
                if (P2 == null) { P2 = new Vector3[nb]; R2 = new Quaternion[nb]; S2 = new float[nb]; }
                Sample(b, tb, loopB, P2, R2, S2);
                for (int i = 0; i < nb; i++) { P[i] = Vector3.Lerp(P[i], P2[i], w); R[i] = Quaternion.Slerp(R[i], R2[i], w); S[i] = Mathf.Lerp(S[i], S2[i], w); }
            }
        }
        /// a turn of a bone and everything hanging from it about the bone's own place (model space)
        public void Turn(int bone, Quaternion q)
        {
            if (bone < 0) return; Vector3 c = P[bone];
            var st = new Stack<int>(); st.Push(bone);
            while (st.Count > 0)
            {
                int i = st.Pop(); P[i] = c + q * (P[i] - c); R[i] = q * R[i];
                foreach (int k in kids[i]) st.Push(k);
            }
        }
        public Vector3 BoneP(int i) { return P[i]; }
        public Quaternion BoneR(int i) { return R[i]; }
        public void Apply()
        {
            for (int i = 0; i < nb; i++) { bones[i].localPosition = P[i]; bones[i].localRotation = R[i]; bones[i].localScale = Vector3.one * S[i]; }
        }
    }

    /// characters thrown by a script: up to n quads, written each frame
    public class CineParticles
    {
        public GameObject go; Mesh mesh; int n;
        Vector3[] v; Color[] c; Vector2[] g, x; int used;
        public CineParticles(int max, bool smoke = false)
        {
            n = max; v = new Vector3[n * 4]; c = new Color[n * 4]; g = new Vector2[n * 4]; x = new Vector2[n * 4];
            var corner = new Vector2[n * 4]; var tri = new int[n * 6];
            Vector2[] C = { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < 4; k++) corner[i * 4 + k] = C[k];
                int o = i * 4; tri[i * 6] = o; tri[i * 6 + 1] = o + 2; tri[i * 6 + 2] = o + 1; tri[i * 6 + 3] = o; tri[i * 6 + 4] = o + 3; tri[i * 6 + 5] = o + 2;
            }
            mesh = new Mesh { name = "cineparts", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = v; mesh.uv = corner; mesh.uv2 = g; mesh.uv3 = x; mesh.colors = c; mesh.triangles = tri;
            mesh.MarkDynamic();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 5000);
            go = new GameObject("CineParticles"); go.layer = GlyphLayer.LAYER;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); var m = new Material(Shader.Find("AsciiHero/Breath"));
            if (smoke) { m.SetFloat("_Op", 0); m.SetFloat("_Src", 5); m.SetFloat("_Dst", 10); m.SetFloat("_Smoke", 1); m.renderQueue = 3010; }
            else m.renderQueue = 3005;
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }
        public void Begin() { used = 0; }
        /// one character: place, colour, size (m), character index and glyph row
        public void Add(Vector3 p, Color col, float size, int glyph, int row, float opacity = 1)
        {
            if (used >= n) return;
            int o = used * 4; col.a = size;
            for (int k = 0; k < 4; k++) { v[o + k] = p; c[o + k] = col; g[o + k] = new Vector2(glyph, row); x[o + k] = new Vector2(opacity, 0); }
            used++;
        }
        public void End()
        {
            Vector3 mn = Vector3.one * 1e9f, mx = -mn;
            for (int i = 0; i < used; i++) { mn = Vector3.Min(mn, v[i * 4]); mx = Vector3.Max(mx, v[i * 4]); }
            if (used == 0) { mn = mx = Vector3.zero; }
            for (int i = used; i < n; i++) { int o = i * 4; for (int k = 0; k < 4; k++) { c[o + k] = Color.clear; v[o + k] = mn; } }
            mesh.vertices = v; mesh.colors = c; mesh.uv2 = g; mesh.uv3 = x;
            var b = new Bounds((mn + mx) * 0.5f, mx - mn + Vector3.one * 4f); mesh.bounds = b;       // (where the characters are: else the mesh is culled far from the origin)
        }
    }
}
