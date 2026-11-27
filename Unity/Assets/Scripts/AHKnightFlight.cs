using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AsciiHero
{
    public partial class Character
    {
        struct FlightBone
        {
            public string bone, tip;
            public Vector3 start, end;
        }
        static FlightBone[] flightRig;
        static Material mantleMaterial;

        static void ReadFlightRig(BinaryReader reader)
        {
            flightRig = new FlightBone[reader.ReadInt32()];
            for (int i = 0; i < flightRig.Length; i++)
                flightRig[i] = new FlightBone { bone = reader.ReadString(), tip = reader.ReadString(),
                    start = ReadPoint(reader), end = ReadPoint(reader) };
        }

        // Bind the donor knight's T pose to the current character's rest pose before applying the game's animation.
        public List<Renderer> BuildFlightArmour()
        {
            var parts = ReadKnight("Flight");
            return BindKnightParts(parts, true);
        }

        void DressWornKnight()
        {
            BindKnightParts(ReadKnight("Worn"), false);
        }

        List<Renderer> BindKnightParts(Plate[] parts, bool flight)
        {
            var transforms = new Matrix4x4[flightRig.Length];
            var indices = new int[flightRig.Length];
            for (int i = 0; i < flightRig.Length; i++)
            {
                var b = flightRig[i];
                var start = human.Head(b.bone);
                var end = b.tip.Length > 0 ? human.Head(b.tip) : human.Tail(b.bone);
                var from = b.end - b.start;
                var to = end - start;
                var rot = Quaternion.FromToRotation(from, to);
                float scale = Mathf.Clamp(to.magnitude / Mathf.Max(.001f, from.magnitude), .4f, 1.8f);
                transforms[i] = Matrix4x4.TRS(start, rot, Vector3.one * scale) * Matrix4x4.Translate(-b.start);
                indices[i] = HumanDB.BI[b.bone];
            }
            var result = new List<Renderer>(parts.Length - 1);
            foreach (var part in parts)
            {
                if (flight && part.finger == 2) continue; // the source mantle stays attached throughout the dash
                var v = new Vector3[part.pos.Length];
                var n = new Vector3[v.Length];
                var bw = new BoneWeight[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    var w = part.rigWeights[i];
                    void Influence(int joint, float weight)
                    {
                        if (weight <= 0) return;
                        v[i] += transforms[joint].MultiplyPoint3x4(part.pos[i]) * weight;
                        n[i] += transforms[joint].MultiplyVector(part.normal[i]).normalized * weight;
                    }
                    Influence(w.boneIndex0, w.weight0); Influence(w.boneIndex1, w.weight1);
                    Influence(w.boneIndex2, w.weight2); Influence(w.boneIndex3, w.weight3);
                    n[i].Normalize();
                    w.boneIndex0 = indices[w.boneIndex0]; w.boneIndex1 = indices[w.boneIndex1];
                    w.boneIndex2 = indices[w.boneIndex2]; w.boneIndex3 = indices[w.boneIndex3];
                    bw[i] = w;
                }
                var mesh = new Mesh { vertices = v, normals = n, uv = part.uv, triangles = part.triangles,
                    bindposes = human.bindposes, boneWeights = bw };
                mesh.RecalculateTangents(); MeshGen.AddGlyphUV(mesh, false);
                if (!flight)
                {
                    // Fit the mantle against the new torso without counting outstretched arms.
                    var limbs = LimbOfBone();
                    var torso = new List<Vector3>();
                    for (int i = 0; i < v.Length; i++)
                        if (limbs[bw[i].boneIndex0] == 0) torso.Add(v[i]);
                    cuirassSurface = torso.ToArray();
                    SamplePlateFront(v, bw, "spine01", mesh.bounds);
                }
                var r = AddSkinned(mesh, "PLATE", flight ? "FlightPart" + part.finger : "Knight");
                r.sharedMaterial = part.material;
                result.Add(r);
            }
            return result;
        }

        Transform MountKnightMantle()
        {
            var part = ReadKnight("Flight")[2];
            if (mantleMaterial == null)
            {
                mantleMaterial = new Material(part.material) { name = "Mantle" };
                mantleMaterial.SetFloat("_Cloth", 1);
                mantleMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            }
            var mesh = FitPlate(part, Matrix4x4.identity);
            var v = mesh.vertices;
            var bounds = mesh.bounds;
            var shoulder = human.Head("upperarm01.L");
            float scale = Mathf.Clamp(Mathf.Abs(shoulder.x) / .23f, .65f, 1.65f);
            // Retain the source folds and the thickness of the two cloth faces. Move a whole
            // cross-section back together to clear the fitted backplate at that height.
            var offset = new float[capeRz.Length];
            float Row(float y) => Mathf.Pow(Mathf.InverseLerp(bounds.max.y, bounds.min.y, y),
                1f / CAPE_EXP) * (offset.Length - 1);
            foreach (var p in v)
            {
                float row = Row(p.y), x = p.x * scale;
                int lo = Mathf.FloorToInt(row), hi = Mathf.Min(lo + 1, offset.Length - 1);
                float rx = Mathf.Lerp(capeRx[lo], capeRx[hi], row - lo);
                float rz = Mathf.Lerp(capeRz[lo], capeRz[hi], row - lo);
                float radial = Mathf.Sqrt(Mathf.Max(0, 1 - x*x / (rx*rx)));
                float limit = -radial*rz - .012f;
                float shift = (p.z - .035f)*scale - limit;
                offset[lo] = Mathf.Max(offset[lo], shift);
                offset[hi] = Mathf.Max(offset[hi], shift);
            }
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i];
                float row = Row(p.y), f = Mathf.InverseLerp(bounds.max.y, bounds.min.y, p.y);
                int lo = Mathf.FloorToInt(row), hi = Mathf.Min(lo + 1, offset.Length - 1);
                float z = capeCz + (p.z - .035f)*scale - Mathf.Lerp(offset[lo], offset[hi], row - lo);
                v[i] = new Vector3(p.x * scale, Mathf.Lerp(capeTop, capeBot, f), z);
            }
            mesh.vertices = v; mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            MeshGen.AddGlyphUV(mesh, false);
            var expanded = mesh.bounds; expanded.Expand(2.5f); mesh.bounds = expanded;
            var group = Group("Mantle");
            var obj = MeshGen.Obj(mesh, mantleMaterial, group, Vector3.zero, Quaternion.identity);
            Attach("spine01", group, Vector3.zero, Quaternion.identity);
            return obj.transform;
        }
    }
}
