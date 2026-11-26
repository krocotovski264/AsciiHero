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
                var parts = new Plate[r.ReadInt32()];
                for (int i = 0; i < parts.Length; i++)
                {
                    var p = new Plate { material = materials[r.ReadInt32()], finger = r.ReadInt32() };
                    int count = r.ReadInt32(), indices = r.ReadInt32();
                    p.pos = new Vector3[count]; p.normal = new Vector3[count]; p.uv = new Vector2[count];
                    for (int k = 0; k < count; k++)
                    {
                        p.pos[k] = ReadPoint(r); p.normal[k] = ReadPoint(r);
                        p.uv[k] = new Vector2(r.ReadSingle(), r.ReadSingle());
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

        Transform MountKnight(string name, string bone, Vector3 center, Quaternion rotation, Vector3 size)
        {
            var plate = ReadKnight(name)[0];
            var mesh = FitPlate(plate, Matrix4x4.TRS(center, rotation, size));
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
            MountKnight("Helmet", "head", head.center, Quaternion.identity, Vector3.Scale(head.size, new Vector3(1.29f, 1.17f, 1.24f)));
            var chest = KnightBounds(Region.Torso, Quaternion.identity);
            float low = hip - 0.065f, high = neck + 0.023f;
            MountKnight("Cuirass", "spine01", new Vector3(0, (low + high) / 2, chest.center.z), Quaternion.identity,
                new Vector3(chest.size.x * 1.13f, high - low, chest.size.z * 1.18f));
            foreach (string side in new[] { "R", "L" })
            {
                string suffix = "." + side; float sign = Mathf.Sign(h.Head("upperarm01" + suffix).x);
                void Sleeve(string name, string top, string bottom, Region region, float from, float to, float padding)
                {
                    var a = h.Head(top + suffix); var b = h.Head(bottom + suffix);
                    var up = (a - b).normalized;
                    var forward = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
                    var rot = Quaternion.LookRotation(forward, up);
                    var bounds = KnightBounds(region, rot, sign);
                    var center = Vector3.LerpUnclamped(a, b, (from + to) * .5f);
                    var fit = new Vector3(sign * bounds.size.x * padding, Vector3.Distance(a,b) * (to-from), bounds.size.z * padding);
                    MountKnight(name, top + suffix, center, rot, fit);
                }
                Sleeve("Shoulder", "upperarm01", "lowerarm01", Region.UpperArm, -.15f, .83f, 1.24f);
                Sleeve("Bracer", "lowerarm01", "wrist", Region.ForeArm, -.06f, .91f, 1.22f);
                Sleeve("Cuisse", "upperleg01", "lowerleg01", Region.Thigh, .13f, .90f, 1.14f);
                Sleeve("Greave", "lowerleg01", "foot", Region.Shin, .11f, .96f, 1.15f);
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
                    UnityEngine.Object.Destroy(mesh);
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
