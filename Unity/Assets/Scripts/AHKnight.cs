using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public partial class Character
    {
        // Packed game meshes use normalized plate bounds, glTF texture coordinates converted
        // to Unity, and the source PBR maps. The hand retains its articulated design coordinates.
        class Plate
        {
            public Material material;
            public Vector3[] pos, normal;
            public Vector2[] uv;
            public int[] triangles;
            public int finger;
            public BoneWeight[] rigWeights;
        }
        static readonly Dictionary<string, Plate[]> knightParts = new Dictionary<string, Plate[]>();

        static Vector3 ReadPoint(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        static Texture2D ReadGearMap(BinaryReader r, bool linear)
        {
            int size = r.ReadInt32();
            if (size == 0) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, true, linear);
            if (!texture.LoadImage(r.ReadBytes(size), true)) throw new InvalidDataException("Unreadable knight texture");
            return texture;
        }
        Plate[] ReadKnight(string name)
        {
            if (knightParts.TryGetValue(name, out var saved)) return saved;
            var file = Resources.Load<TextAsset>("Knight/" + name);
            using (var r = new BinaryReader(new MemoryStream(file.bytes)))
            {
                var materials = new Material[r.ReadInt32()];
                for (int i = 0; i < materials.Length; i++)
                {
                    var m = new Material(mats[name == "Cape" ? "CAPE" : "PLATE"]) { name = name };
                    m.color = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    m.SetFloat("_Metallic", r.ReadSingle()); m.SetFloat("_Glossiness", 1 - r.ReadSingle());
                    var color = ReadGearMap(r, false); var mr = ReadGearMap(r, true); var normal = ReadGearMap(r, true);
                    if (color != null) m.mainTexture = color;
                    if (mr != null) m.SetTexture("_GearMR", mr);
                    if (normal != null) m.SetTexture("_GearNormal", normal);
                    m.SetFloat("_GearMaps", normal != null ? 2 : mr != null ? 1 : 0);
                    materials[i] = m;
                }
                if (name == "Flight") ReadFlightRig(r);
                var parts = new Plate[r.ReadInt32()];
                for (int i = 0; i < parts.Length; i++)
                {
                    var p = new Plate { material = materials[r.ReadInt32()], finger = r.ReadInt32() };
                    int count = r.ReadInt32(), indices = r.ReadInt32();
                    p.pos = new Vector3[count]; p.normal = new Vector3[count]; p.uv = new Vector2[count];
                    if (name == "Flight") p.rigWeights = new BoneWeight[count];
                    for (int k = 0; k < count; k++)
                    {
                        p.pos[k] = ReadPoint(r); p.normal[k] = ReadPoint(r);
                        p.uv[k] = new Vector2(r.ReadSingle(), r.ReadSingle());
                        if (p.rigWeights != null)
                            p.rigWeights[k] = new BoneWeight {
                                boneIndex0 = r.ReadByte(), boneIndex1 = r.ReadByte(), boneIndex2 = r.ReadByte(), boneIndex3 = r.ReadByte(),
                                weight0 = r.ReadByte()/255f, weight1 = r.ReadByte()/255f, weight2 = r.ReadByte()/255f, weight3 = r.ReadByte()/255f };

                    }
                    p.triangles = new int[indices];
                    for (int k = 0; k < indices; k++) p.triangles[k] = r.ReadUInt16();
                    parts[i] = p;
                }
                return knightParts[name] = parts;
            }
        }

        static Mesh FitPlate(Plate p, Matrix4x4 fit)
        {
            var v = new Vector3[p.pos.Length]; var n = new Vector3[v.Length];
            var normalMatrix = fit.inverse.transpose;
            for (int i = 0; i < v.Length; i++) { v[i] = fit.MultiplyPoint3x4(p.pos[i]); n[i] = normalMatrix.MultiplyVector(p.normal[i]).normalized; }
            var tri = p.triangles;
            if (fit.determinant < 0)
            {
                tri = (int[])tri.Clone();
                for (int i = 0; i < tri.Length; i += 3) { int t = tri[i]; tri[i] = tri[i + 1]; tri[i + 1] = t; }
            }
            var mesh = new Mesh { name = p.material.name, vertices = v, normals = n, uv = p.uv, triangles = tri };
            mesh.RecalculateTangents(); MeshGen.AddGlyphUV(mesh, false);
            return mesh;
        }

        // Intersect the morphed body with parallel planes in the bone's frame. Each
        // convex section is stored as support distances, including the underlayer.
        class ArmourSections
        {
            const int Rows = 25, Sides = 24;
            readonly Vector2[] axes = new Vector2[Sides];
            readonly float[,] limits = new float[Rows, Sides];
            readonly Bounds[] bounds = new Bounds[Rows];
            readonly bool[] filled = new bool[Rows];
            readonly float bottom, step;

            public ArmourSections(Human h, Quaternion inv, Region region, float side, float bottom, float height)
            {
                this.bottom = bottom; step = height / (Rows - 1);
                for (int j = 0; j < Sides; j++)
                {
                    float a = 2 * Mathf.PI * j / Sides;
                    axes[j] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    for (int i = 0; i < Rows; i++) limits[i,j] = float.NegativeInfinity;
                }
                var points = new Vector3[h.n]; var mask = new bool[h.n];
                for (int i = 0; i < h.n; i++)
                {
                    var r = h.region[i]; var p = h.pos[i]; points[i] = inv * p;
                    mask[i] = (side == 0 || side*p.x > 0) && (r == region ||
                        (region == Region.Torso && (r == Region.Pelvis || r == Region.Neck)));
                }
                var tri = h.partTris[0];
                for (int t = 0; t < tri.Count; t += 3)
                {
                    int a = tri[t], b = tri[t+1], c = tri[t+2];
                    if (!(mask[a] || mask[b] || mask[c])) continue;
                    Edge(points[a],points[b]); Edge(points[b],points[c]); Edge(points[c],points[a]);
                }
            }
            void Edge(Vector3 a, Vector3 b)
            {
                if (a.y > b.y) { var tmp = a; a = b; b = tmp; }
                if (b.y - a.y < 1e-6f) return;
                int first = Mathf.Max(0, Mathf.CeilToInt((a.y-bottom)/step));
                int last = Mathf.Min(Rows-1, Mathf.FloorToInt((b.y-bottom)/step));
                for (int i = first; i <= last; i++)
                {
                    var p = Vector3.Lerp(a,b,(bottom+i*step-a.y)/(b.y-a.y));
                    if (filled[i]) bounds[i].Encapsulate(p);
                    else { bounds[i] = new Bounds(p,Vector3.zero); filled[i] = true; }
                    var q = new Vector2(p.x,p.z);
                    for (int j=0; j<Sides; j++) limits[i,j] = Mathf.Max(limits[i,j],Vector2.Dot(axes[j],q));
                }
            }
            public Vector3 Fit(Vector3 p, float gap, float radialScale)
            {
                float row = Mathf.Clamp((p.y-bottom)/step,0,Rows-1);
                int a = Mathf.FloorToInt(row), b = Mathf.Min(Rows-1,a+1);
                if (!filled[a] && !filled[b]) return p;
                if (!filled[a]) a=b; if (!filled[b]) b=a;
                float t=row-Mathf.Floor(row);
                var c=Vector3.Lerp(bounds[a].center,bounds[b].center,t);
                var center=new Vector2(c.x,c.z); var delta=new Vector2(p.x-c.x,p.z-c.z);
                float r=delta.magnitude; if (r<.002f) return p;
                var dir=delta/r; float required=float.PositiveInfinity;
                for (int j=0;j<Sides;j++)
                {
                    float cosine=Vector2.Dot(dir,axes[j]); if (cosine<=.01f) continue;
                    float support=Mathf.Lerp(limits[a,j],limits[b,j],t)-Vector2.Dot(center,axes[j]);
                    required=Mathf.Min(required,(support+gap)/cosine);
                }
                float fitted = Mathf.Max(r * radialScale, required);
                return new Vector3(c.x + dir.x * fitted, p.y, c.z + dir.y * fitted);
            }
        }

        // Long flat facets need intermediate samples to follow the belly and calves.
        static void RefinePlate(Mesh mesh)
        {
            var v=mesh.vertices.ToList(); var n=mesh.normals.ToList(); var uv=mesh.uv.ToList();
            var tris=mesh.triangles.ToList();
            for (int pass=0;pass<2;pass++)
            {
                var next=new List<int>(); var edges=new Dictionary<long,int>();
                int Mid(int a,int b)
                {
                    long key=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
                    if(edges.TryGetValue(key,out int index)) return index;
                    index=v.Count; edges.Add(key,index); v.Add((v[a]+v[b])*.5f);
                    n.Add((n[a]+n[b]).normalized); uv.Add((uv[a]+uv[b])*.5f); return index;
                }
                void Face(int a,int b,int c) { next.Add(a); next.Add(b); next.Add(c); }
                for(int i=0;i<tris.Count;i+=3)
                {
                    int a=tris[i],b=tris[i+1],c=tris[i+2];
                    const float maxEdge=.055f*.055f;
                    int ab=(v[a]-v[b]).sqrMagnitude>maxEdge?Mid(a,b):-1;
                    int bc=(v[b]-v[c]).sqrMagnitude>maxEdge?Mid(b,c):-1;
                    int ca=(v[c]-v[a]).sqrMagnitude>maxEdge?Mid(c,a):-1;
                    if(ab<0 && bc<0 && ca<0) { Face(a,b,c); continue; }
                    if(ab>=0 && bc>=0 && ca>=0) { Face(a,ab,ca); Face(ab,b,bc); Face(ca,bc,c); Face(ab,bc,ca); continue; }
                    if(ab<0) { int x=a; a=b; b=c; c=x; x=ab; ab=bc; bc=ca; ca=x; }
                    if(ab<0) { int x=a; a=b; b=c; c=x; x=ab; ab=bc; bc=ca; ca=x; }
                    if(bc>=0) { Face(a,ab,c); Face(ab,bc,c); Face(ab,b,bc); }
                    else if(ca>=0) { Face(ab,b,c); Face(ab,c,ca); Face(a,ab,ca); }
                    else { Face(a,ab,c); Face(ab,b,c); }
                }
                tris=next;
            }
            mesh.Clear(); if(v.Count>65535) mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0,uv); mesh.SetTriangles(tris,0);
        }

        public struct CapeObstacle
        {
            public Vector3 point;
            public BoneWeight weight;
        }
        public readonly List<CapeObstacle> capeObstacles = new List<CapeObstacle>();

        void SamplePlateFront(Vector3[] vertices, BoneWeight[] weights, string bone, Bounds bounds)
        {
            const int bands = 16;
            var front = Enumerable.Repeat(-1, bands).ToArray();
            for (int i = 0; i < vertices.Length; i++)
            {
                int band = Mathf.RoundToInt(Mathf.InverseLerp(bounds.min.y, bounds.max.y, vertices[i].y) * (bands - 1));
                if (front[band] < 0 || vertices[i].z > vertices[front[band]].z) front[band] = i;
            }
            var rigid = new BoneWeight { boneIndex0 = HumanDB.BI[bone], weight0 = 1 };
            foreach (int i in front)
                if (i >= 0) capeObstacles.Add(new CapeObstacle { point = vertices[i], weight = weights == null ? rigid : weights[i] });
        }

        Transform MountKnight(string name, string bone, Vector3 center, Quaternion rotation, Vector3 size, Region? fitRegion = null, float side = 0)
        {
            var plate = ReadKnight(name)[0];
            var mesh = FitPlate(plate, Matrix4x4.TRS(center, rotation, size));
            if (fitRegion.HasValue)
            {
                RefinePlate(mesh);
                var inv = Quaternion.Inverse(rotation);
                var sections = new ArmourSections(human, inv, fitRegion.Value, side, (inv*center).y-size.y/2, size.y);
                var v = mesh.vertices;
                var top = (inv * center).y + size.y * .5f;
                for (int i = 0; i < v.Length; i++)
                {
                    var local = inv * v[i];
                    float towardKnee = name == "Cuisse" ? Mathf.SmoothStep(0, 1, (top - local.y) / size.y) : 0;
                    float gap = name == "Cuirass" ? .024f : Mathf.Lerp(.016f, .009f, towardKnee);
                    v[i] = rotation * sections.Fit(local, gap, 1 - .28f * towardKnee);
                }
                mesh.vertices=v; mesh.RecalculateBounds(); mesh.RecalculateNormals(); mesh.RecalculateTangents();
                MeshGen.AddGlyphUV(mesh,false);
                if (name == "Cuisse") SamplePlateFront(v, null, bone, mesh.bounds);
                if (name=="Cuirass")
                {
                    // Transfer the body's weights so the abdominal lames follow the hips and the chest follows the spine.
                    var bodyIndices = Enumerable.Range(0,human.n).Where(k=>HumanDB.vpart[k]==0 &&
                        (human.region[k]==Region.Torso || human.region[k]==Region.Pelvis || human.region[k]==Region.Neck)).ToArray();
                    var weights=new BoneWeight[v.Length];
                    for(int k=0;k<v.Length;k++)
                    {
                        int near=bodyIndices[0]; float best=float.PositiveInfinity;
                        foreach(int j in bodyIndices)
                        {
                            float d=(v[k]-human.pos[j]).sqrMagnitude;
                            if(d<best) { best=d; near=j; }
                        }
                        int o=4*near; var indices=HumanDB.skinIndex; var w=HumanDB.skinWeight;
                        weights[k]=new BoneWeight { boneIndex0=indices[o],boneIndex1=indices[o+1],boneIndex2=indices[o+2],boneIndex3=indices[o+3],
                            weight0=w[o]/255f,weight1=w[o+1]/255f,weight2=w[o+2]/255f,weight3=w[o+3]/255f };
                    }
                    SamplePlateFront(v, weights, bone, mesh.bounds);
                    cuirassSurface = v;
                    mesh.boneWeights=weights; mesh.bindposes=human.bindposes;
                    var renderer=AddSkinned(mesh,"PLATE",name); renderer.sharedMaterial=plate.material;
                    return renderer.transform;
                }
            }
            if (name == "Cape")
            {
                RefinePlate(mesh);
                var inverseFit = Matrix4x4.TRS(center, rotation, size).inverse;
                var shoulder = human.Head("upperarm01.L");
                float upperLip = Mathf.Min(capeTop - .01f, shoulder.y + .045f);
                float underarm = shoulder.y - .48f * Vector3.Distance(shoulder, human.Head("lowerarm01.L"));
                var v = mesh.vertices;
                for (int i = 0; i < v.Length; i++)
                {
                    var source = inverseFit.MultiplyPoint3x4(v[i]);
                    float f = Mathf.Clamp01(.5f - source.y);
                    // The original mesh's side opening lies between 4% and 14% of its length.
                    float y = f < .04f ? Mathf.Lerp(capeTop, upperLip, f / .04f) :
                        f < .14f ? Mathf.Lerp(upperLip, underarm, (f - .04f) / .10f) :
                        Mathf.Lerp(underarm, capeBot, (f - .14f) / .86f);
                    float t = Mathf.Pow(Mathf.InverseLerp(capeTop, capeBot, y), 1f / CAPE_EXP);
                    float angle = Mathf.Atan2(source.x, -source.z);
                    float u = 0.5f + angle / (2 * Mathf.PI);
                    var p = CapePt(u, t);
                    // Keep the source folds around the fitted envelope, including the two front hems.
                    var outward = new Vector3(p.x, 0, p.z - capeCz).normalized;
                    float relief = new Vector2(source.x, source.z).magnitude;
                    p += outward * Mathf.Clamp((relief - .4f) * size.z * .15f, -.008f, .008f);
                    v[i] = p;
                }
                mesh.vertices = v; mesh.RecalculateNormals(); mesh.RecalculateTangents(); MeshGen.AddGlyphUV(mesh, false);
            }
            if (name == "Cape") { var bounds = mesh.bounds; bounds.Expand(2.5f); mesh.bounds = bounds; }
            var group = Group(name);
            var go = MeshGen.Obj(mesh, plate.material, group, Vector3.zero, Quaternion.identity);
            Attach(bone, group, Vector3.zero, Quaternion.identity);
            return go.transform;
        }

        Bounds KnightBounds(Region region, Quaternion rotation, float side = 0)
        {
            var bounds = new Bounds(); bool first = true; var inverse = Quaternion.Inverse(rotation);
            for (int i = 0; i < human.n; i++)
            {
                var p = human.pos[i];
                if (HumanDB.vpart[i] != 0 || human.region[i] != region || (side != 0 && p.x * side <= 0)) continue;
                var point = inverse * p;
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
            return bounds;
        }
        void DressKnight(float hip, float neck)
        {
            var h = human;
            var head = KnightBounds(Region.Head, Quaternion.identity);
            MountKnight("Helmet", "head", head.center, Quaternion.identity, Vector3.Scale(head.size, new Vector3(1.18f, 1.12f, 1.18f)));
            var chest = KnightBounds(Region.Torso, Quaternion.identity);
            float low = hip - 0.055f, high = neck + 0.035f;
            MountKnight("Cuirass", "spine01", new Vector3(chest.center.x, (low + high) / 2, chest.center.z), Quaternion.identity,
                new Vector3(chest.size.x + 0.035f, high - low, chest.size.z + 0.045f), Region.Torso);
            foreach (string side in new[] { "R", "L" })
            {
                string suffix = "." + side; float sign = Mathf.Sign(h.Head("upperarm01" + suffix).x);
                void Sleeve(string name, string top, string bottom, Region region, float from, float to, float padding)
                {
                    var a = h.Head(top + suffix); var b = h.Head(bottom + suffix);
                    var up = (a - b).normalized;
                    var forward = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
                    if (name=="Shoulder") forward=Vector3.ProjectOnPlane(sign*Vector3.right+Vector3.forward*.18f,up).normalized;
                    var rot = Quaternion.LookRotation(forward, up);
                    var bounds = KnightBounds(region, rot, sign);
                    var axisCenter = Quaternion.Inverse(rot) * Vector3.LerpUnclamped(a, b, (from + to) * .5f);
                    var center = rot * new Vector3(bounds.center.x, axisCenter.y, bounds.center.z);
                    var fit = new Vector3(sign * bounds.size.x * padding, Vector3.Distance(a,b) * (to-from), bounds.size.z * padding);
                    MountKnight(name, top + suffix, center, rot, fit, region, sign);
                }
                Sleeve("Shoulder", "upperarm01", "lowerarm01", Region.UpperArm, -.19f, .71f, 1.16f);
                Sleeve("Bracer", "lowerarm01", "wrist", Region.ForeArm, .04f, .94f, 1.15f);
                Sleeve("Cuisse", "upperleg01", "lowerleg01", Region.Thigh, .10f, .88f, 1.12f);
                Sleeve("Greave", "lowerleg01", "foot", Region.Shin, .13f, .94f, 1.12f);
                var knee = h.Head("lowerleg01" + suffix);
                var calf = KnightBounds(Region.Shin, Quaternion.identity, sign);
                MountKnight("Poleyn", "lowerleg01" + suffix, knee + Vector3.forward * .024f, Quaternion.identity,
                    new Vector3(sign * calf.size.x * 1.12f, (hip - knee.y) * .34f, calf.size.z * .89f));
                var foot = KnightBounds(Region.Foot, Quaternion.identity, sign);
                var bootSize = Vector3.Scale(foot.size, new Vector3(sign * 1.16f, 1.30f, 1.13f));
                MountKnight("Sabaton", "foot" + suffix, foot.center + Vector3.up * (bootSize.y-foot.size.y)*.5f, Quaternion.identity, bootSize);
                MountGauntlet(side);
            }
        }

        // Orient the thumb toward the index finger on each hand. Each finger follows its
        // own three phalanges, so closing the sword hand also closes the metal glove.
        void MountGauntlet(string side)
        {
            var h = human; string suffix = "." + side;
            var wrist = h.Head("wrist" + suffix);
            var middle = h.Head("finger3-1" + suffix); var along = (middle - wrist).normalized;
            var across = Vector3.ProjectOnPlane(h.Head("finger2-1" + suffix) - h.Head("finger5-1" + suffix), along).normalized;
            var dorsal = Vector3.Cross(across, along).normalized;
            if (dorsal.x * wrist.x < 0) dorsal = -dorsal;
            float width = Vector3.Distance(h.Head("finger2-1" + suffix), h.Head("finger5-1" + suffix)) / .706f;
            float length = Vector3.Distance(wrist, middle);
            var hand = Matrix4x4.identity;
            hand.SetColumn(0, across * width); hand.SetColumn(1, along * length / .95f); hand.SetColumn(2, dorsal * width);
            hand.SetColumn(3, new Vector4(wrist.x, wrist.y, wrist.z, 1) + (Vector4)(along * length * .56f));
            float[] starts = {0,-.055f,.42f,.42f,.42f,.36f}, lengths = {0,.55f,.89f,.99f,.90f,.70f};
            float[] fingerX = {0,.366f,.346f,.105f,-.14f,-.36f}, driftX = {0,.52f,.20f,.054f,-.10f,-.205f};
            foreach (var group in ReadKnight("Gauntlet").GroupBy(p => p.material))
            {
                var verts = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>();
                var weights = new List<BoneWeight>(); var triangles = new List<int>();
                foreach (var plate in group)
                {
                    var mesh = FitPlate(plate, hand);
                    var v = mesh.vertices; int offset = verts.Count;
                    for (int i = 0; i < v.Length; i++)
                    {
                        int f = plate.finger, bone = HumanDB.BI["wrist" + suffix];
                        if (f != 0)
                        {
                            float t = Mathf.Clamp01((plate.pos[i].y - starts[f]) / lengths[f]);
                            float step = t < .43f ? t / .43f : t < .75f ? 1 + (t-.43f)/.32f : 2 + (t-.75f)/.25f;
                            int j = Mathf.Min(2, Mathf.FloorToInt(step));
                            string joint = "finger" + f + "-" + (j+1) + suffix; bone = HumanDB.BI[joint];
                            var axis = Vector3.Lerp(h.Head(joint), h.Tail(joint), step-j);
                            v[i] = axis + across * ((plate.pos[i].x-fingerX[f]-driftX[f]*t)*width)
                                        + dorsal * ((plate.pos[i].z + .13f*t*t)*width);
                        }
                        weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1 });
                    }
                    verts.AddRange(v); normals.AddRange(mesh.normals); uvs.AddRange(mesh.uv);
                    foreach (int ix in mesh.triangles) triangles.Add(offset + ix);
                    if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
                    else UnityEngine.Object.DestroyImmediate(mesh);
                }
                var combined = new Mesh { name = "Gauntlet " + side };
                combined.SetVertices(verts); combined.SetNormals(normals); combined.SetUVs(0, uvs);
                combined.SetTriangles(triangles, 0); combined.boneWeights = weights.ToArray(); combined.bindposes = h.bindposes;
                combined.RecalculateTangents(); MeshGen.AddGlyphUV(combined, false);
                var r = AddSkinned(combined, "PLATE", combined.name); r.sharedMaterial = group.Key;
            }
        }
    }
}
