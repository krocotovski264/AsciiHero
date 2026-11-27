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
        public readonly BoneWeight[] mantlePins = new BoneWeight[2*CAPE_C];
        public float mantleEnvelopeTop, mantleEnvelopeBottom;
        public readonly float[] knightAnkleClearance = new float[2];

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
            MakeKnightBind(out var transforms, out var indices);
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
                    for (int side = 0; side < 2; side++)
                    {
                        string suffix = side == 0 ? ".L" : ".R";
                        float sole = float.PositiveInfinity;
                        for (int i = 0; i < v.Length; i++)
                            if (limbs[bw[i].boneIndex0] == side + 4) sole = Mathf.Min(sole, v[i].y);
                        if (!float.IsInfinity(sole))
                            knightAnkleClearance[side] = Mathf.Max(.015f, human.Head("foot" + suffix).y - sole);
                    }
                    cuirassSurface = torso.ToArray();
                    SamplePlateFront(v, bw, "spine01", mesh.bounds);
                }
                var r = AddSkinned(mesh, "PLATE", flight ? "FlightPart" + part.finger : "Knight");
                r.sharedMaterial = part.material;
                result.Add(r);
            }
            return result;
        }

        void MakeKnightBind(out Matrix4x4[] transforms, out int[] indices)
        {
            transforms = new Matrix4x4[flightRig.Length];
            indices = new int[flightRig.Length];
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
        }

        Transform MountKnightMantle()
        {
            var part = ReadKnight("WornMantle")[0];
            MakeKnightBind(out var bind, out var indices);
            if (mantleMaterial == null)
            {
                mantleMaterial = new Material(part.material) { name = "Mantle" };
                mantleMaterial.SetFloat("_Cloth", 1);
                mantleMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            }
            var mesh = FitPlate(part, Matrix4x4.identity);
            var v = mesh.vertices;
            float scale = human.height / 1.84f;
            float donorShoulder = flightRig[part.rigWeights[0].boneIndex0].start.y;
            for (int j = 0; j < flightRig.Length; j++)
                if (flightRig[j].bone == "upperarm01.L") { donorShoulder = flightRig[j].start.y; break; }
            var shoulder = human.Head("upperarm01.L");
            var rear = new float[capeRz.Length];
            for (int row = 0; row < rear.Length; row++) rear[row] = float.PositiveInfinity;
            foreach (var q in cuirassSurface)
            {
                if (Mathf.Abs(q.x) > Mathf.Abs(shoulder.x) * .8f) continue;
                int row = Mathf.RoundToInt(Mathf.InverseLerp(capeBot, capeTop, q.y) * (rear.Length-1));
                for (int k = Mathf.Max(0,row-1); k <= Mathf.Min(rear.Length-1,row+1); k++)
                    rear[k] = Mathf.Min(rear[k], q.z);
            }
            for (int i = 0; i < v.Length; i++)
            {
                var p = part.pos[i]; var w = part.rigWeights[i];
                var collar = bind[w.boneIndex0].MultiplyPoint3x4(p)*w.weight0
                           + bind[w.boneIndex1].MultiplyPoint3x4(p)*w.weight1
                           + bind[w.boneIndex2].MultiplyPoint3x4(p)*w.weight2
                           + bind[w.boneIndex3].MultiplyPoint3x4(p)*w.weight3;
                float free = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(donorShoulder+.015f, donorShoulder-.17f, p.y));
                float y = shoulder.y + (p.y-donorShoulder)*scale;
                int row = Mathf.RoundToInt(Mathf.InverseLerp(capeBot,capeTop,y)*(rear.Length-1));
                float z = capeCz + p.z*scale;
                if (!float.IsInfinity(rear[row])) z = Mathf.Min(z, rear[row]-.018f + Mathf.Min(0,p.z+.12f)*.20f);
                v[i] = Vector3.Lerp(collar, new Vector3(p.x*scale,y,z), free);
            }
            mesh.vertices = v; mesh.RecalculateBounds(); mesh.RecalculateNormals(); mesh.RecalculateTangents();
            FitMantleGrid(v, mesh.bounds, part.rigWeights, indices);
            MeshGen.AddGlyphUV(mesh, false);
            var expanded = mesh.bounds; expanded.Expand(2.5f); mesh.bounds = expanded;
            var group = Group("Mantle");
            var obj = MeshGen.Obj(mesh, mantleMaterial, group, Vector3.zero, Quaternion.identity);
            Attach("spine01", group, Vector3.zero, Quaternion.identity);
            return obj.transform;
        }

        // Fit the solver in the same angular/height coordinates sampled by both cloth shaders.
        void FitMantleGrid(Vector3[] vertices, Bounds bounds, BoneWeight[] weights, int[] indices)
        {
            mantleEnvelopeTop = capeTop; mantleEnvelopeBottom = capeBot;
            capeTop = bounds.max.y; capeBot = bounds.min.y;
            capeMap = new Vector4(capeCz, capeTop, capeBot, 1);
            float angle = 0;
            var polar = new Vector2[vertices.Length];
            for (int j = 0; j < vertices.Length; j++)
            {
                var p = vertices[j];
                polar[j] = new Vector2(Mathf.Atan2(p.x, capeCz-p.z), new Vector2(p.x,p.z-capeCz).magnitude);
                angle = Mathf.Max(angle, Mathf.Abs(polar[j].x));
            }
            capeMap2 = new Vector4(angle,angle,angle,.25f);
            for (int r = 0; r < CAPE_R; r++)
            {
                float v = r/(CAPE_R-1f);
                float y = Mathf.Lerp(capeTop,capeBot,Mathf.Pow(v,CAPE_EXP));
                for (int c = 0; c < CAPE_C; c++)
                {
                    float theta = angle*(2*c/(CAPE_C-1f)-1);
                    float sum = 0, radial = 0;
                    int nearest = 0; float closest = float.PositiveInfinity;
                    for (int j = 0; j < vertices.Length; j++)
                    {
                        float dy = (vertices[j].y-y)/.065f;
                        float da = (polar[j].x-theta)/.15f;
                        float distance = dy*dy + da*da;
                        if (distance < closest) { nearest = j; closest = distance; }
                        float k = 1/(1+distance); k *= k*k;
                        radial += k*polar[j].y; sum += k;
                    }
                    var outward = new Vector3(Mathf.Sin(theta),0,-Mathf.Cos(theta));
                    int i = r*CAPE_C+c;
                    capeRest[i] = new Vector3(0,y,capeCz) + outward*(radial/sum);
                    capeNrm[i] = outward;
                    if (r < 2)
                    {
                        var w = weights[nearest];
                        w.boneIndex0 = indices[w.boneIndex0]; w.boneIndex1 = indices[w.boneIndex1];
                        w.boneIndex2 = indices[w.boneIndex2]; w.boneIndex3 = indices[w.boneIndex3];
                        mantlePins[i] = w;
                    }
                }
            }
        }
    }
}
