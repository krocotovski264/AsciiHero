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
        public Transform mantleFrame;
        public Transform[] mantleNodes;
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

        // The helmet, gorget and breastplate form one silhouette in the donor rest pose.
        // Give them one similarity transform. Their weights still animate on the game skeleton.
        Matrix4x4 KnightTorsoFit()
        {
            var left = System.Array.Find(flightRig, b => b.bone == "upperarm01.L").start;
            var right = System.Array.Find(flightRig, b => b.bone == "upperarm01.R").start;
            var donor = (left + right) * .5f;
            var shoulder = (human.Head("upperarm01.L") + human.Head("upperarm01.R")) * .5f;
            float scale = human.height / 1.84f;
            return Matrix4x4.TRS(shoulder, Quaternion.identity, Vector3.one*scale)
                 * Matrix4x4.Translate(-donor);
        }

        void MakeKnightBind(out Matrix4x4[] transforms, out int[] indices)
        {
            var torsoFit = KnightTorsoFit();
            transforms = new Matrix4x4[flightRig.Length];
            indices = new int[flightRig.Length];
            for (int i = 0; i < flightRig.Length; i++)
            {
                var b = flightRig[i];
                indices[i] = HumanDB.BI[b.bone];
                if (b.bone == "root" || b.bone.StartsWith("spine") || b.bone == "neck01" ||
                    b.bone == "head" || b.bone.StartsWith("clavicle"))
                {
                    transforms[i] = torsoFit;
                    continue;
                }
                var start = human.Head(b.bone);
                var end = b.tip.Length > 0 ? human.Head(b.tip) : human.Tail(b.bone);
                var from = b.end - b.start;
                var to = end - start;
                var rot = Quaternion.FromToRotation(from, to);
                float scale = Mathf.Clamp(to.magnitude / Mathf.Max(.001f, from.magnitude), .4f, 1.8f);
                transforms[i] = Matrix4x4.TRS(start, rot, Vector3.one * scale) * Matrix4x4.Translate(-b.start);
            }
        }

        Transform MountKnightMantle()
        {
            var part = ReadKnight("WornMantle")[0];
            MakeKnightBind(out var bind, out var indices);
            if (mantleMaterial == null)
            {
                mantleMaterial = new Material(part.material) { name = "Mantle" };
                mantleMaterial.SetFloat("_Cloth", -1); // two-sided fabric deformed by skinning
                mantleMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            }
            var mesh = FitPlate(part, Matrix4x4.identity);
            var v = mesh.vertices;
            var release = new float[v.Length];
            var weights = new BoneWeight[v.Length];
            var boundNormals = new Vector3[v.Length];
            var torsoFit = KnightTorsoFit();
            float donorShoulder = System.Array.Find(flightRig, b => b.bone == "upperarm01.L").start.y;
            for (int i = 0; i < v.Length; i++)
            {
                var p = part.pos[i]; var w = part.rigWeights[i];
                var collar = bind[w.boneIndex0].MultiplyPoint3x4(p)*w.weight0
                           + bind[w.boneIndex1].MultiplyPoint3x4(p)*w.weight1
                           + bind[w.boneIndex2].MultiplyPoint3x4(p)*w.weight2
                           + bind[w.boneIndex3].MultiplyPoint3x4(p)*w.weight3;
                float free = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(donorShoulder+.015f, donorShoulder-.17f, p.y));
                free *= part.mantleRelease[i] / 255f;
                release[i] = free;
                var n = part.normal[i];
                boundNormals[i] = (bind[w.boneIndex0].MultiplyVector(n).normalized*w.weight0
                                 + bind[w.boneIndex1].MultiplyVector(n).normalized*w.weight1
                                 + bind[w.boneIndex2].MultiplyVector(n).normalized*w.weight2
                                 + bind[w.boneIndex3].MultiplyVector(n).normalized*w.weight3).normalized;
                w.boneIndex0 = indices[w.boneIndex0]; w.boneIndex1 = indices[w.boneIndex1];
                w.boneIndex2 = indices[w.boneIndex2]; w.boneIndex3 = indices[w.boneIndex3];
                weights[i] = w;
                v[i] = Vector3.Lerp(collar, torsoFit.MultiplyPoint3x4(p), free);
            }
            mesh.vertices = v; mesh.RecalculateBounds(); mesh.RecalculateNormals();
            var normals = mesh.normals;
            for (int i = 0; i < normals.Length; i++)
                normals[i] = Vector3.Lerp(boundNormals[i], normals[i], release[i]).normalized;
            mesh.normals = normals;
            mesh.RecalculateTangents();
            FitMantleGrid(v, mesh.bounds, part.rigWeights, indices);
            MeshGen.AddGlyphUV(mesh, false);
            var expanded = mesh.bounds; expanded.Expand(2.5f); mesh.bounds = expanded;
            mantleFrame = Group("MantleFrame");
            Attach("spine01", mantleFrame, Vector3.zero, Quaternion.identity);
            mantleNodes = new Transform[CAPE_C * CAPE_R];
            int first = human.bones.Length;
            var bones = new List<Transform>(human.bones);
            var poses = new List<Matrix4x4>(human.bindposes);
            var frameBind = mantleFrame.worldToLocalMatrix * model.localToWorldMatrix;
            // Sample alternate solver nodes, including both hems, to keep the mobile bone palette compact.
            int columns = CAPE_C/2+1, rows = CAPE_R/2+1;
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < columns; col++)
                {
                    int j = Mathf.Min(2*row,CAPE_R-1)*CAPE_C + Mathf.Min(2*col,CAPE_C-1);
                    mantleNodes[j] = Group("Cloth" + j);
                    mantleNodes[j].SetParent(mantleFrame, false);
                    bones.Add(mantleNodes[j]);
                    poses.Add(frameBind);
                }
            var influences = new Dictionary<int,float>(8);
            for (int i = 0; i < v.Length; i++)
            {
                if (release[i] == 0) continue; // identical bind and weights on either side of the seam
                float theta = Mathf.Atan2(v[i].x, capeCz-v[i].z);
                float col = Mathf.InverseLerp(-capeMap2.x, capeMap2.x, theta)*(CAPE_C-1);
                float row = Mathf.Pow(Mathf.InverseLerp(capeTop, capeBot, v[i].y), 1f/CAPE_EXP)*(CAPE_R-1);
                int c = Mathf.Min(Mathf.FloorToInt(col/2), columns-2);
                int r = Mathf.Min(Mathf.FloorToInt(row/2), rows-2);
                float u = Mathf.InverseLerp(2*c, Mathf.Min(2*c+2,CAPE_C-1), col);
                float t = Mathf.InverseLerp(2*r, Mathf.Min(2*r+2,CAPE_R-1), row);
                int j = first + r*columns+c;
                var grid = new BoneWeight { boneIndex0=j, boneIndex1=j+1, boneIndex2=j+columns, boneIndex3=j+columns+1,
                    weight0=(1-u)*(1-t), weight1=u*(1-t), weight2=(1-u)*t, weight3=u*t };
                influences.Clear();
                if (release[i] < 1) GlyphLayer.Add(influences, weights[i], 1-release[i]);
                GlyphLayer.Add(influences, grid, release[i]);
                weights[i] = GlyphLayer.Top4(influences);
            }
            mesh.bindposes = poses.ToArray();
            mesh.boneWeights = weights;
            var renderer = AddSkinned(mesh, "PLATE", "Mantle");
            renderer.bones = bones.ToArray();
            renderer.sharedMaterial = mantleMaterial;
            return renderer.transform;
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
