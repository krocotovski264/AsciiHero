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
    public class Cloak
    {
        public const int COLS = 12, ROWS = 8;
        readonly Transform skirt, model; readonly Human H;
        readonly Vector3[] restL = new Vector3[COLS * ROWS];          // rest shape, skirt-local
        readonly Vector3[] x = new Vector3[COLS * ROWS], ox = new Vector3[COLS * ROWS], rest = new Vector3[COLS * ROWS];
        readonly Vector4[] disp = new Vector4[COLS * ROWS];
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
            Shader.SetGlobalVector("_ClothMap", map);
            foreach (var r in rs)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb); mpb.SetFloat("_ClothSim", 1); r.SetPropertyBlock(mpb);
            }
        }
    }
}
