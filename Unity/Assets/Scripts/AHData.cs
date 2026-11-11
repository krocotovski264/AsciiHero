// Loads the baked data (Resources/human.bytes, Resources/mm.bytes).
// The data was produced in a right-handed system (three.js). Unity is left-handed, so on load
// every X coordinate is mirrored, triangle winding is flipped, quaternions become (x,-y,-z,w),
// yaw rates change sign and "left" velocities become "right" velocities.
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AsciiHero
{
    public class HumanVariant
    {
        public char sex; public int build; public float height;
        public Vector3[] pos; public Vector3[] heads; public Vector3[] tails;
    }

    public static class HumanDB
    {
        public static int nverts, ntris;
        public static string[] bones; public static int[] parents;
        public static int[] tris; public static byte[] vpart, skinIndex, skinWeight;
        public static List<HumanVariant> variants;
        public static Dictionary<string, int> BI;
        static bool loaded;

        static string ReadStr(BinaryReader br) { int n = br.ReadByte(); return Encoding.UTF8.GetString(br.ReadBytes(n)); }

        public static void Load()
        {
            if (loaded) return;
            var ta = Resources.Load<TextAsset>("human");
            var br = new BinaryReader(new MemoryStream(ta.bytes));
            br.ReadBytes(4);
            nverts = br.ReadInt32(); ntris = br.ReadInt32(); int nb = br.ReadInt32(); float q = br.ReadSingle();
            var lo = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            bones = new string[nb]; parents = new int[nb]; BI = new Dictionary<string, int>();
            for (int i = 0; i < nb; i++) { bones[i] = ReadStr(br); parents[i] = br.ReadInt32(); BI[bones[i]] = i; }
            tris = new int[ntris * 3];
            for (int k = 0; k < tris.Length; k++) tris[k] = br.ReadUInt16();
            for (int t = 0; t < tris.Length; t += 3) { int s = tris[t + 1]; tris[t + 1] = tris[t + 2]; tris[t + 2] = s; } // mirror -> flip winding
            vpart = br.ReadBytes(nverts); skinIndex = br.ReadBytes(nverts * 4); skinWeight = br.ReadBytes(nverts * 4);
            int nvar = br.ReadInt32(); variants = new List<HumanVariant>();
            for (int v = 0; v < nvar; v++)
            {
                var hv = new HumanVariant();
                hv.sex = (char)br.ReadByte(); hv.build = br.ReadInt32(); hv.height = br.ReadSingle();
                hv.pos = new Vector3[nverts];
                for (int i = 0; i < nverts; i++)
                {
                    float x = lo.x + br.ReadUInt16() * q, y = lo.y + br.ReadUInt16() * q, z = lo.z + br.ReadUInt16() * q;
                    hv.pos[i] = new Vector3(-x, y, z);
                }
                hv.heads = new Vector3[nb]; hv.tails = new Vector3[nb];
                for (int i = 0; i < nb; i++) { float x = br.ReadSingle(), y = br.ReadSingle(), z = br.ReadSingle(); hv.heads[i] = new Vector3(-x, y, z); }
                for (int i = 0; i < nb; i++) { float x = br.ReadSingle(), y = br.ReadSingle(), z = br.ReadSingle(); hv.tails[i] = new Vector3(-x, y, z); }
                variants.Add(hv);
            }
            loaded = true;
        }

        public static HumanVariant Variant(char sex, int build)
        {
            foreach (var v in variants) if (v.sex == sex && v.build == build) return v;
            return variants[0];
        }
    }

    /// Motion Matching database (CMU mocap retargeted to the MakeHuman rig) + mirrored copy built on load.
    public static class MotionDB
    {
        public static int n, N2, nb, nf, fps;
        public static string[] bones;
        public static float[] mean, scale;
        public static float[] Q, RO, RM, CON, F;     // RM = (forward speed, right speed, yaw rate) per frame
        public static byte[] VALID, IDLEB;
        public static int[] RANGE_END;
        static bool loaded;

        static string ReadStr(BinaryReader br) { int k = br.ReadByte(); return Encoding.UTF8.GetString(br.ReadBytes(k)); }
        // feature components that are X coordinates (mirror / handedness)
        static readonly int[] XIDX = { 0, 3, 6, 9, 12, 15, 17, 19, 21, 23, 25 };

        public static void Load()
        {
            if (loaded) return;
            var ta = Resources.Load<TextAsset>("mm");
            var br = new BinaryReader(new MemoryStream(ta.bytes));
            br.ReadBytes(4);
            n = br.ReadInt32(); nb = br.ReadInt32(); nf = br.ReadInt32(); fps = br.ReadInt32();
            bones = new string[nb]; for (int i = 0; i < nb; i++) bones[i] = ReadStr(br);
            mean = new float[nf]; scale = new float[nf];
            for (int i = 0; i < nf; i++) mean[i] = br.ReadSingle();
            for (int i = 0; i < nf; i++) scale[i] = br.ReadSingle();
            foreach (int j in XIDX) mean[j] = -mean[j];
            int nr = br.ReadInt32(); var ranges = new int[nr * 2];
            for (int i = 0; i < nr * 2; i++) ranges[i] = br.ReadInt32();
            N2 = n * 2;
            Q = new float[N2 * nb * 4]; RO = new float[N2 * 3]; RM = new float[N2 * 3]; CON = new float[N2 * 2]; F = new float[N2 * nf];
            VALID = new byte[N2]; IDLEB = new byte[N2]; RANGE_END = new int[N2];
            var q16 = new short[n * nb * 3]; for (int i = 0; i < q16.Length; i++) q16[i] = br.ReadInt16();
            var ro16 = new short[n * 3]; for (int i = 0; i < ro16.Length; i++) ro16[i] = br.ReadInt16();
            var rm16 = new short[n * 3]; for (int i = 0; i < rm16.Length; i++) rm16[i] = br.ReadInt16();
            var con8 = br.ReadBytes(n * 2);
            var f16 = new short[n * nf]; for (int i = 0; i < f16.Length; i++) f16[i] = br.ReadInt16();
            var val = br.ReadBytes(n); var ib = br.ReadBytes(n);

            // left/right bone swap table for the mirrored copy
            var sw = new int[nb];
            for (int k = 0; k < nb; k++)
            {
                string b = bones[k], o = b;
                if (b.EndsWith(".L")) o = b.Substring(0, b.Length - 2) + ".R";
                else if (b.EndsWith(".R")) o = b.Substring(0, b.Length - 2) + ".L";
                sw[k] = System.Array.IndexOf(bones, o);
            }
            var raw = new float[nf]; var mr = new float[nf];
            for (int f = 0; f < n; f++)
            {
                for (int k = 0; k < nb; k++)
                {
                    int o = (f * nb + k) * 3;
                    float x = q16[o] / 32767f, y = q16[o + 1] / 32767f, z = q16[o + 2] / 32767f;
                    float w = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x - y * y - z * z));
                    // right-handed -> Unity: (x, -y, -z, w)
                    float ux = x, uy = -y, uz = -z;
                    int d = (f * nb + k) * 4; Q[d] = ux; Q[d + 1] = uy; Q[d + 2] = uz; Q[d + 3] = w;
                    // mirrored copy: swap sides and reflect again
                    int dm = ((f + n) * nb + sw[k]) * 4; Q[dm] = ux; Q[dm + 1] = -uy; Q[dm + 2] = -uz; Q[dm + 3] = w;
                }
                float rox = -ro16[f * 3] / 4000f, roy = ro16[f * 3 + 1] / 4000f, roz = ro16[f * 3 + 2] / 4000f;
                RO[f * 3] = rox; RO[f * 3 + 1] = roy; RO[f * 3 + 2] = roz;
                RO[(f + n) * 3] = -rox; RO[(f + n) * 3 + 1] = roy; RO[(f + n) * 3 + 2] = roz;
                float fwd = rm16[f * 3] / 2000f, right = -rm16[f * 3 + 1] / 2000f, yr = -rm16[f * 3 + 2] / 2000f;
                RM[f * 3] = fwd; RM[f * 3 + 1] = right; RM[f * 3 + 2] = yr;
                RM[(f + n) * 3] = fwd; RM[(f + n) * 3 + 1] = -right; RM[(f + n) * 3 + 2] = -yr;
                CON[f * 2] = con8[f * 2] / 255f; CON[f * 2 + 1] = con8[f * 2 + 1] / 255f;
                CON[(f + n) * 2] = CON[f * 2 + 1]; CON[(f + n) * 2 + 1] = CON[f * 2];
                // features (normalised); Unity handedness: negate X components
                for (int j = 0; j < nf; j++) F[f * nf + j] = f16[f * nf + j] / 1000f;
                foreach (int j in XIDX) F[f * nf + j] = -F[f * nf + j];
                for (int j = 0; j < nf; j++) raw[j] = F[f * nf + j] / scale[j] + mean[j];
                for (int j = 0; j < 3; j++)
                {
                    float s = j == 0 ? -1f : 1f;
                    mr[j] = raw[3 + j] * s; mr[3 + j] = raw[j] * s; mr[6 + j] = raw[9 + j] * s; mr[9 + j] = raw[6 + j] * s;
                }
                mr[12] = -raw[12]; mr[13] = raw[13]; mr[14] = raw[14];
                for (int k = 0; k < 3; k++)
                {
                    mr[15 + 2 * k] = -raw[15 + 2 * k]; mr[16 + 2 * k] = raw[16 + 2 * k];
                    mr[21 + 2 * k] = -raw[21 + 2 * k]; mr[22 + 2 * k] = raw[22 + 2 * k];
                }
                for (int j = 0; j < nf; j++) F[(f + n) * nf + j] = (mr[j] - mean[j]) * scale[j];
                VALID[f] = VALID[f + n] = val[f]; IDLEB[f] = IDLEB[f + n] = ib[f];
            }
            for (int r = 0; r < nr; r++)
            {
                int a = ranges[r * 2], b = ranges[r * 2 + 1];
                for (int f = a; f < b; f++) { RANGE_END[f] = b - 1; RANGE_END[f + n] = b - 1 + n; }
            }
            loaded = true;
        }

        public static Quaternion Quat(int frame, int bone)
        {
            int d = (frame * nb + bone) * 4;
            return new Quaternion(Q[d], Q[d + 1], Q[d + 2], Q[d + 3]);
        }
    }
}
