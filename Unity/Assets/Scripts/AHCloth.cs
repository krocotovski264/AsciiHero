// The robe's skirt as cloth. A coarse grid of particles (12 round the body x 8 from the waist to the hem) laid on the
// skirt's rest shape, simulated the way game cloth usually is (T. Jakobsen, "Advanced Character Physics"; position
// based dynamics, M. Mueller et al.):
//   * Verlet integration: the velocity is implicit in the last two positions, gravity, air drag and wind are added;
//   * distance constraints relaxed a few times per step: stretch (round the rings and down the folds, stiff), shear
//     (diagonals) and bend (two particles apart, soft) so the cloth keeps its length but folds freely;
//   * the top ring is not simulated: it is pinned to the waist and moves with the body;
//   * collisions are resolved by projection: particles are pushed out of capsules round the thighs, shins and hips,
//     and kept above the ground;
//   * a weak pull towards the rest shape keeps the skirt hanging round the body instead of collapsing onto the legs.
// The grid's displacement from the rest shape goes to the shaders (_ClothGrid), which move every vertex of the skirt.
using UnityEngine;

namespace AsciiHero
{
    public interface ICloth { void Update(float dt, Vector3 windW); void Apply(MaterialPropertyBlock mpb, System.Collections.Generic.IEnumerable<Renderer> rs); }

    public class Cloak : ICloth
    {
        public const int COLS = 12, ROWS = 8;
        readonly Transform skirt, model; readonly Human H;
        readonly Vector3[] restL = new Vector3[COLS * ROWS];          // rest shape, skirt-local
        readonly Vector3[] x = new Vector3[COLS * ROWS], ox = new Vector3[COLS * ROWS], rest = new Vector3[COLS * ROWS];
        readonly Vector4[] disp = new Vector4[CapeSim.GRID];          // (sized for the cloak's grid: the shaders' array is one size)
        struct Link { public int a, b; public float len, k; }
        readonly System.Collections.Generic.List<Link> links = new System.Collections.Generic.List<Link>();
        public readonly Vector4 map;
        float acc; bool ready; Vector3 lastPos;
        const float STEP = 1f / 90f;

        public Cloak(Character ch)
        {
            skirt = ch.skirt; model = ch.model; H = ch.human;
            var v = ch.skirtRest; int segs = ch.skirtSegs, rings = ch.skirtRings;
            float cz = 0; for (int i = 0; i < v.Length; i++) cz += v[i].z; cz /= v.Length;
            map = new Vector4(cz, v[0].y, v[v.Length - 1].y, 0);
            for (int r = 0; r < ROWS; r++)
            {
                float fr = r * rings / (float)(ROWS - 1); int r0 = Mathf.Min((int)fr, rings - 1); float f = fr - r0;
                for (int c = 0; c < COLS; c++)
                {
                    int s = c * segs / COLS;
                    restL[r * COLS + c] = Vector3.Lerp(v[r0 * (segs + 1) + s], v[(r0 + 1) * (segs + 1) + s], f);
                }
            }
            float sc = skirt.lossyScale.y;
            System.Action<int, int, float> add = (a, b, k) => links.Add(new Link { a = a, b = b, len = (restL[a] - restL[b]).magnitude * sc, k = k });
            for (int r = 0; r < ROWS; r++)
                for (int c = 0; c < COLS; c++)
                {
                    int i = r * COLS + c, cr = r * COLS + (c + 1) % COLS;
                    add(i, cr, 1f);                                                    // round the ring
                    if (r + 1 < ROWS) { add(i, i + COLS, 1f); add(i, (r + 1) * COLS + (c + 1) % COLS, 0.5f); add(cr, i + COLS, 0.5f); }   // down, shear
                    if (r + 2 < ROWS) add(i, i + 2 * COLS, 0.25f);                     // bend
                    add(i, r * COLS + (c + 2) % COLS, 0.25f);
                }
        }

        void Rest()
        {
            for (int i = 0; i < rest.Length; i++) rest[i] = skirt.TransformPoint(restL[i]);
        }

        // body capsules the cloth cannot pass through
        readonly Vector3[] capA = new Vector3[6], capB = new Vector3[6]; readonly float[] capR = new float[6]; int nCap;
        void Capsules()
        {
            nCap = 0;
            System.Action<string, string, float> cap = (a, b, r) =>
            {
                var ta = H.Bone(a); var tb = H.Bone(b); if (ta == null || tb == null || ta.lossyScale.x < 0.01f) return;
                capA[nCap] = ta.position; capB[nCap] = tb.position; capR[nCap] = r; nCap++;
            };
            cap("upperleg01.L", "lowerleg01.L", 0.095f); cap("upperleg01.R", "lowerleg01.R", 0.095f);
            cap("lowerleg01.L", "foot.L", 0.07f); cap("lowerleg01.R", "foot.R", 0.07f);
            cap("root", "spine03", 0.16f);
        }
        void Collide(int i)
        {
            for (int k = 0; k < nCap; k++)
            {
                Vector3 ab = capB[k] - capA[k]; float t = Mathf.Clamp01(Vector3.Dot(x[i] - capA[k], ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                Vector3 p = capA[k] + ab * t, d = x[i] - p; float l = d.magnitude, r = capR[k] + 0.02f;
                if (l < r) x[i] = p + (l > 1e-5f ? d / l : model.forward) * r;
            }
            float g = model.position.y + 0.015f; if (x[i].y < g) x[i].y = g;
        }

        /// windW: world wind velocity (m/s)
        public void Update(float dt, Vector3 windW)
        {
            if (skirt == null) return;
            Rest();
            if (!ready || (model.position - lastPos).sqrMagnitude > 9f)
            {   // start (or a respawn, a new place): the cloth at rest
                for (int i = 0; i < x.Length; i++) { x[i] = ox[i] = rest[i]; }
                ready = true;
            }
            lastPos = model.position;
            Capsules();
            acc = Mathf.Min(acc + dt, STEP * 4);
            float t = Time.time;
            while (acc >= STEP)
            {
                acc -= STEP;
                for (int i = 0; i < x.Length; i++)
                {
                    if (i < COLS) { ox[i] = x[i]; x[i] = rest[i]; continue; }            // the waist is pinned to the body
                    int r = i / COLS, c = i % COLS;
                    // wind: stronger lower down, pressing the side that faces it, fluttering
                    Vector3 radial = (rest[i] - skirt.position); radial.y = 0; radial = radial.sqrMagnitude > 1e-6f ? radial.normalized : Vector3.zero;
                    float face = 0.6f + 0.4f * Mathf.Clamp(-Vector3.Dot(radial, windW.normalized), -1, 1);
                    float fl = 0.75f + 0.5f * Mathf.Sin(t * 5.1f + c * 1.3f + r * 0.9f);
                    Vector3 a = Physics.gravity + windW * (1.6f * face * fl * (0.4f + 0.6f * r / (ROWS - 1f)));
                    Vector3 vel = (x[i] - ox[i]) * 0.985f;                                 // air drag
                    ox[i] = x[i]; x[i] += vel + a * (STEP * STEP);
                    // keeps the skirt's shape round the body (weak, more at the top)
                    x[i] += (rest[i] - x[i]) * (0.012f + 0.03f * (1 - r / (ROWS - 1f)));
                }
                for (int it = 0; it < 4; it++)
                {
                    foreach (var L in links)
                    {
                        Vector3 d = x[L.b] - x[L.a]; float l = d.magnitude; if (l < 1e-6f) continue;
                        float wa = L.a < COLS ? 0 : 1, wb = L.b < COLS ? 0 : 1; if (wa + wb == 0) continue;
                        Vector3 corr = d * ((l - L.len) / l * L.k / (wa + wb));
                        x[L.a] += corr * wa; x[L.b] -= corr * wb;
                    }
                    for (int i = COLS; i < x.Length; i++) Collide(i);
                }
            }
            for (int i = 0; i < x.Length; i++)
            {
                Vector3 d = x[i] - rest[i];
                if (d.sqrMagnitude > 2.25f) { d = d.normalized * 1.5f; x[i] = rest[i] + d; ox[i] = x[i]; }      // never flies off
                disp[i] = d;
            }
        }
        public void Apply(MaterialPropertyBlock mpb, System.Collections.Generic.IEnumerable<Renderer> rs)
        {
            Shader.SetGlobalVectorArray("_ClothGrid", disp);
            Shader.SetGlobalVector("_ClothMap", map); Shader.SetGlobalVectorArray("_ClothNrm", disp);
            foreach (var r in rs)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb); mpb.SetFloat("_ClothSim", 1); r.SetPropertyBlock(mpb);
            }
        }
    }

    // ---- the knight's cloak ----
    // Built the way game cloth for capes is usually set up (Unreal's Chaos Cloth: kinematic top, long-range attachments,
    // max distance, backstop, anim drive; T.-Y. Kim, N. Chentanez, M. Mueller, "Long Range Attachments - A Method to
    // Simulate Inextensible Clothing in Computer Games", SCA 2012):
    //   * a 10 x 14 grid of particles on the cloak's rest shape; the top row is fixed to the shoulders;
    //   * Verlet integration at 90 Hz: gravity, wind pressing on the cloth along its normal (so it bellies and flaps),
    //     air drag; part of the body's own movement is carried straight into the cloth (inertia scale), so a sprint or a
    //     dash does not leave it behind;
    //   * distance constraints: stiff against stretching, soft against squashing (the cloth folds instead of bulging),
    //     shear and bending links;
    //   * long-range attachments: no particle may be further from its column's top than the cloth between them is long -
    //     the cloth can never stretch, however fast the body goes;
    //   * collisions with capsules round the chest, the hips, the upper arms and the legs, and a backstop: each particle
    //     keeps out of a sphere lying on the body side of its rest place, so the cloak can swing out and round but never
    //     into the armour (which is what happened after a dash);
    //   * a max distance from the rest place growing down the cloak, and a weak pull back to it (anim drive).
    // The grid's displacement and its normals go to the shaders, which move and light every vertex of the cloak.
    public class CapeSim : ICloth
    {
        public const int C = Character.CAPE_C, R = Character.CAPE_R, GRID = 144;
        readonly Transform cape, model; readonly Human H;
        readonly Vector3[] restL, nrmL, rest = new Vector3[C * R], rn = new Vector3[C * R], x = new Vector3[C * R], ox = new Vector3[C * R];
        readonly float[] tether = new float[C * R];
        struct Link { public int a, b; public float len, ks, kc; }
        readonly System.Collections.Generic.List<Link> links = new System.Collections.Generic.List<Link>();
        readonly Character chr;
        float acc; bool ready; Vector3 lastAnchor;
        const float STEP = 1f / 90f;

        const int DepthBands = 32;
        readonly float[] plateFront = new float[DepthBands];
        readonly Matrix4x4[] toCape;
        readonly int[] obstacleBones;

        readonly Vector3[] pins = new Vector3[2*C];

        void UpdatePlateFront()
        {
            var inverse = cape.worldToLocalMatrix;
            foreach (int bone in obstacleBones)
                toCape[bone] = inverse * H.bones[bone].localToWorldMatrix * H.bindposes[bone];
            for (int i = 0; i < pins.Length; i++)
            {
                var w = chr.mantlePins[i]; var p = restL[i];
                var q = toCape[w.boneIndex0].MultiplyPoint3x4(p)*w.weight0;
                if (w.weight1 > 0) q += toCape[w.boneIndex1].MultiplyPoint3x4(p)*w.weight1;
                if (w.weight2 > 0) q += toCape[w.boneIndex2].MultiplyPoint3x4(p)*w.weight2;
                if (w.weight3 > 0) q += toCape[w.boneIndex3].MultiplyPoint3x4(p)*w.weight3;
                // Arm motion is already applied to the sewn edge by skinning. Give
                // the solver a stable shoulder frame so it cannot lever the cape out.
                pins[i] = cape.TransformPoint(Vector3.Lerp(p, q, .25f));
            }
            for (int i = 0; i < DepthBands; i++) plateFront[i] = chr.capeCz;
            foreach (var sample in chr.capeObstacles)
            {
                var w = sample.weight;
                var q = toCape[w.boneIndex0].MultiplyPoint3x4(sample.point) * w.weight0;
                if (w.weight1 > 0) q += toCape[w.boneIndex1].MultiplyPoint3x4(sample.point) * w.weight1;
                if (w.weight2 > 0) q += toCape[w.boneIndex2].MultiplyPoint3x4(sample.point) * w.weight2;
                if (w.weight3 > 0) q += toCape[w.boneIndex3].MultiplyPoint3x4(sample.point) * w.weight3;
                int row = Mathf.RoundToInt(Mathf.InverseLerp(chr.capeBot, chr.capeTop, q.y) * (DepthBands - 1));
                for (int k = Mathf.Max(0, row - 1); k <= Mathf.Min(DepthBands - 1, row + 1); k++)
                    plateFront[k] = Mathf.Max(plateFront[k], q.z + .035f);
            }
        }

        public CapeSim(Character ch)
        {
            cape = ch.mantleFrame; model = ch.model; H = ch.human; restL = ch.capeRest; nrmL = ch.capeNrm;
            chr = ch;
            toCape = new Matrix4x4[H.bones.Length];
            var used = new System.Collections.Generic.HashSet<int>();
            foreach (var sample in ch.capeObstacles)
            {
                var w = sample.weight;
                used.Add(w.boneIndex0);
                if (w.weight1 > 0) used.Add(w.boneIndex1);
                if (w.weight2 > 0) used.Add(w.boneIndex2);
                if (w.weight3 > 0) used.Add(w.boneIndex3);
            }
            foreach (var w in ch.mantlePins)
            {
                used.Add(w.boneIndex0);
                if (w.weight1 > 0) used.Add(w.boneIndex1);
                if (w.weight2 > 0) used.Add(w.boneIndex2);
                if (w.weight3 > 0) used.Add(w.boneIndex3);
            }
            obstacleBones = new int[used.Count];
            used.CopyTo(obstacleBones);
            float sc = cape.lossyScale.y;
            System.Action<int, int, float, float> add = (a, b, ks, kc) => links.Add(new Link { a = a, b = b, len = (restL[a] - restL[b]).magnitude * sc, ks = ks, kc = kc });
            for (int r = 0; r < R; r++)
                for (int c = 0; c < C; c++)
                {
                    int i = r * C + c;
                    // cloth: hard to stretch, easy to squash (it folds) and to bend (it does not keep its shape)
                    if (c + 1 < C) add(i, i + 1, 1f, 0.12f);                                   // across
                    if (r + 1 < R) add(i, i + C, 1f, 0.25f);                                   // down
                    if (r + 1 < R && c + 1 < C) { add(i, i + C + 1, 0.35f, 0.03f); add(i + 1, i + C, 0.35f, 0.03f); }   // shear
                    if (c + 2 < C) add(i, i + 2, 0.06f, 0.01f);                                // bend
                    if (r + 2 < R) add(i, i + 2 * C, 0.12f, 0.02f);
                }
            // the length of cloth from each particle up its column to the shoulders (the tether's limit)
            for (int c = 0; c < C; c++) { float L = 0; for (int r = 1; r < R; r++) { L += (restL[r * C + c] - restL[(r - 1) * C + c]).magnitude * sc; tether[r * C + c] = L * 1.02f; } }
        }

        // body capsules
        readonly Vector3[] capA = new Vector3[12], capB = new Vector3[12]; readonly float[] capR = new float[12]; int nCap;
        void Capsules()
        {
            nCap = 0;
            System.Action<string, string, float> cap = (a, b, r) =>
            {
                var ta = H.Bone(a); var tb = H.Bone(b); if (ta == null || tb == null || ta.lossyScale.x < 0.01f) return;
                capA[nCap] = ta.position; capB[nCap] = tb.position; capR[nCap] = r; nCap++;
            };
            cap("spine01", "spine04", 0.2f); cap("spine05", "root", 0.18f);
            cap("upperarm01.L", "upperarm01.R", 0.12f);                                // across the shoulders (the pauldrons)
            cap("neck01", "head", 0.085f);
            cap("upperarm01.L", "lowerarm01.L", 0.095f); cap("upperarm01.R", "lowerarm01.R", 0.095f);
            cap("upperleg01.L", "lowerleg01.L", 0.11f); cap("upperleg01.R", "lowerleg01.R", 0.11f);
            cap("lowerleg01.L", "foot.L", 0.08f); cap("lowerleg01.R", "foot.R", 0.08f);
        }
        void Collide(int i, int r)
        {
            for (int k = 0; k < nCap; k++)
            {
                Vector3 ab = capB[k] - capA[k]; float t = Mathf.Clamp01(Vector3.Dot(x[i] - capA[k], ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                Vector3 p = capA[k] + ab * t, d = x[i] - p; float l = d.magnitude, rr = capR[k] + 0.025f;
                if (l < rr) x[i] = p + (l > 1e-5f ? d / l : -model.forward) * rr;
            }
            // (no backstop: the body's capsules alone keep it out, so wind from behind lays it on the back and it flows
            // round the body)  max distance from the rest place:
            float v = r / (R - 1f), md = .015f + .48f * v*v;
            Vector3 dr = x[i] - rest[i]; if (dr.sqrMagnitude > md * md) x[i] = rest[i] + dr.normalized * md;
            float g = model.position.y + 0.02f; if (x[i].y < g) x[i].y = g;
        }
        /// the check that keeps the cloak out of the armour: round the body's axis, at the particle's height, the armour's
        /// outline (the cloak's own rest outline, a little smaller) - a particle inside it is put back on it, along its
        /// angle from the axis; one that has got deep inside (a dash, a fall) goes back where it should hang
        bool Envelope(int i)
        {
            Vector3 l = cape.InverseTransformPoint(x[i]);
            float H = chr.mantleEnvelopeTop - chr.mantleEnvelopeBottom; if (H <= 0) return false;
            float f = (chr.mantleEnvelopeTop - l.y) / H; if (f < 0.02f || f > 1.05f) return false;
            float v = Mathf.Pow(Mathf.Clamp01(f), 1f / Character.CAPE_EXP);
            int k = Mathf.Clamp(Mathf.RoundToInt(v * (chr.capeRx.Length - 1)), 0, chr.capeRx.Length - 1);
            float sh = Mathf.Lerp(.98f, .90f, v);
            float rx = chr.capeRx[k] * sh, rz = chr.capeRz[k] * sh;
            // Cap the collision envelope at the actual fitted garment. A broad underlying
            // body must not inflate the back of a knight whose body mesh is hidden.
            Vector3 bind = restL[i];
            float bx = bind.x / rx, bz = (bind.z - chr.capeCz) / rz;
            float fit = Mathf.Clamp(Mathf.Sqrt(bx*bx + bz*bz), .01f, 1f);
            rx *= fit; rz *= fit;
            float dx = l.x, dz = l.z - chr.capeCz, e = (dx / rx) * (dx / rx) + (dz / rz) * (dz / rz);
            if (e >= 1) return false;
            if (e < 0.25f) { x[i] = rest[i]; ox[i] = rest[i]; return true; }
            float s = 1f / Mathf.Sqrt(e);
            l.x = dx * s; l.z = chr.capeCz + dz * s;
            var w = cape.TransformPoint(l); ox[i] += w - x[i]; x[i] = w;      // (no speed gained from the push)
            return true;
        }

        // A front hem can swing outward but must not slip through the torso to the back.
        // Use the same constraint in reverse for the back panel. Side folds remain free.
        void KeepPanelSide(int i)
        {
            float depth = restL[i].z - chr.capeCz;
            if (Mathf.Abs(depth) < .04f) return;
            var local = cape.InverseTransformPoint(x[i]);
            float sign = Mathf.Sign(depth);
            float correction = Mathf.Abs(depth) * .65f - sign * (local.z - chr.capeCz);
            int column = i % C;
            if (depth > 0 && (column <= 1 || column >= C - 2))
            {
                float row = Mathf.InverseLerp(chr.capeBot, chr.capeTop, local.y) * (DepthBands - 1);
                int lower = Mathf.FloorToInt(row);
                float front = Mathf.Lerp(plateFront[lower], plateFront[Mathf.Min(lower + 1, DepthBands - 1)], row - lower);
                correction = Mathf.Max(correction, front - local.z);
            }
            if (correction <= 0) return;
            var push = cape.TransformVector(Vector3.forward * (sign * correction));
            x[i] += push;
            ox[i] += push;
        }
        public void Update(float dt, Vector3 windW)
        {
            if (cape == null || restL == null) return;
            for (int i = 0; i < rest.Length; i++) { rest[i] = cape.TransformPoint(restL[i]); rn[i] = cape.TransformDirection(nrmL[i]).normalized; }
            Vector3 anchor = cape.position;
            if (!ready || (anchor - lastAnchor).sqrMagnitude > 4f)
            {   // start, a respawn, a new place: the cloth at rest
                for (int i = 0; i < x.Length; i++) x[i] = ox[i] = rest[i];
                ready = true; lastAnchor = anchor;
            }
            // inertia scale: 60 % of the body's movement this frame goes straight into the cloth (positions and their
            // history alike, so it brings no speed of its own) - the rest it has to catch up with
            // (a dash, a leap: the body jumps faster than any wind; the cloak goes with it whole, or it would be left
            // hanging in front of him when he dashes backwards or aside)
            float jump = dt > 1e-5f ? (anchor - lastAnchor).magnitude / dt : 0;
            Vector3 mv = (anchor - lastAnchor) * Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(4f, 9f, jump)); lastAnchor = anchor;
            for (int i = 2*C; i < x.Length; i++) { x[i] += mv; ox[i] += mv; }
            Capsules();
            UpdatePlateFront();

            acc = Mathf.Min(acc + dt, STEP * 4);
            float t = Time.time;
            while (acc >= STEP)
            {
                acc -= STEP;
                for (int i = 0; i < x.Length; i++)
                {
                    int r = i / C, c = i % C; float v = r / (R - 1f);
                    if (r < 2) { ox[i] = x[i] = pins[i]; continue; }                // pinned to the shoulders
                    Vector3 vel = (x[i] - ox[i]) / STEP;
                    // wind: the air pushes on the cloth along its normal, by how fast it meets it; it gusts and flutters
                    Vector3 n = NormalAt(r, c);
                    float fl = 0.7f + 0.6f * Mathf.PerlinNoise(t * 1.7f + c * 0.31f, r * 0.27f + t * 0.9f);
                    Vector3 rel = windW * fl - vel;
                    float dn = Vector3.Dot(rel, n);
                    Vector3 a = Physics.gravity + n * (dn * Mathf.Abs(dn) * 0.9f + dn * 1.4f) + rel * 0.25f;     // pressure on the sheet + skin drag
                    Vector3 step = (x[i] - ox[i]) * 0.99f;                                  // air drag
                    ox[i] = x[i]; x[i] += step + a * (STEP * STEP);
                    x[i] += (rest[i] - x[i]) * (0.0015f + 0.05f * Mathf.Pow(1 - v, 4));    // anim drive: holds it on the shoulders, not below
                }
                for (int it = 0; it < 6; it++)
                {
                    foreach (var L in links)
                    {
                        Vector3 d = x[L.b] - x[L.a]; float l = d.magnitude; if (l < 1e-6f) continue;
                        float wa = L.a < 2*C ? 0 : 1, wb = L.b < 2*C ? 0 : 1; if (wa + wb == 0) continue;
                        float k = l > L.len ? L.ks : L.kc;
                        Vector3 corr = d * ((l - L.len) / l * k / (wa + wb));
                        x[L.a] += corr * wa; x[L.b] -= corr * wb;
                    }
                    for (int i = 2*C; i < x.Length; i++)
                    {
                        int r = i / C, c = i % C;
                        // long-range attachment to its column's top
                        Vector3 top = x[c], d = x[i] - top; float l = d.magnitude;
                        if (l > tether[i]) x[i] = top + d * (tether[i] / l);

                        Collide(i, r);
                        Envelope(i);
                        KeepPanelSide(i);
                    }
                }
            }
            for (int i = 0; i < 2*C; i++) x[i] = ox[i] = pins[i];
            for (int i = 2*C; i < x.Length; i++)
            {
                Envelope(i); KeepPanelSide(i);
                // Leave room for a trailing hem, but prevent the lower sheet from folding up onto the collar.
                float v = (i / C) / (R - 1f);
                float excess = x[i].y-rest[i].y-(.02f+.22f*v*v)*model.lossyScale.y;
                if (excess > 0) { x[i].y -= excess; ox[i].y -= excess; }
            }
            for (int i = 0; i < x.Length; i++)
            {
                Vector3 d = x[i] - rest[i];
                if (!(d.sqrMagnitude < 4f)) { d = Vector3.zero; x[i] = ox[i] = rest[i]; }      // (never flies off, never NaN)
                if (chr.mantleNodes[i] != null)
                {
                    float depth = chr.capeTop - restL[i].y;
                    float freedom = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.07f, .36f, depth));
                    chr.mantleNodes[i].position = cape.position + d * freedom;
                }
            }
        }
        Vector3 NormalAt(int r, int c)
        {
            int i = r * C + c;
            Vector3 du = x[r * C + Mathf.Min(c + 1, C - 1)] - x[r * C + Mathf.Max(c - 1, 0)];
            Vector3 dv = x[Mathf.Min(r + 1, R - 1) * C + c] - x[Mathf.Max(r - 1, 0) * C + c];
            Vector3 n = Vector3.Cross(du, dv); float l = n.magnitude; if (l < 1e-7f) return rn[i];
            n /= l; return Vector3.Dot(n, rn[i]) < 0 ? -n : n;
        }
        public void Apply(MaterialPropertyBlock mpb, System.Collections.Generic.IEnumerable<Renderer> rs)
        {
            // Surface and glyph renderers share the simulated bones.
        }
    }
}
