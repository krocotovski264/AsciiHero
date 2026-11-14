// Motion Matching locomotion (after Clavet 2015 / Daniel Holden's open-source Motion-Matching, MIT),
// running on motion capture retargeted to the MakeHuman rig.
//  * critically damped spring controller predicts the desired trajectory (0.33 / 0.66 / 1.0 s)
//  * every 0.1 s (or on sharp input changes) the database is searched for the frame whose feet,
//    hip velocity and future trajectory best continue the current motion
//  * transitions use inertialization (pose offsets decaying with a 0.1 s half-life)
//  * root motion from the animation, synchronised with the controller ("simulation bone")
//  * two-bone foot IK with contact locking, knee pole from the toe direction
// Unity conventions: heading h -> forward (sin h, cos h), right (cos h, -sin h); positive yaw = turning right.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AsciiHero
{
    public class MotionActor
    {
        const float LN2 = 0.69314718f;
        static float Damp(float h) { return 4f * LN2 / (h + 1e-5f); }
        static float Wrap(float a) { return Mathf.Atan2(Mathf.Sin(a), Mathf.Cos(a)); }

        // Holden "Spring-It-On": critically damped velocity spring with integrated position
        static void SpringChar(ref float x, ref float v, ref float a, float vGoal, float h, float dt)
        {
            float y = Damp(h) / 2, j0 = v - vGoal, j1 = a + j0 * y, e = Mathf.Exp(-y * dt);
            float nx = e * ((-j1) / (y * y) + (-j0 - j1 * dt) / y) + j1 / (y * y) + j0 / y + vGoal * dt + x;
            float nv = e * (j0 + j1 * dt) + vGoal;
            float na = e * (a - j1 * y * dt);
            x = nx; v = nv; a = na;
        }
        static void SpringAng(ref float x, ref float v, float goal, float h, float dt)
        {
            float y = Damp(h) / 2, j0 = Wrap(x - goal), j1 = v + j0 * y, e = Mathf.Exp(-y * dt);
            x = e * (j0 + j1 * dt) + goal; v = e * (v - j1 * y * dt);
        }

        // ---- state ----
        public Character ch; Human H;
        Quaternion[] baseQ; Vector3 rootPos; int[] map; float[] mask;
        public float heading, speed, frame, cost; float searchT, idleT, idleBreakAt;
        public string label = "idle";
        public bool noLock;
        float narrow = 1;
        /// when set, the Animator Controller (Tools -> Patch Project Locomotion) plays the clips and this class
        /// only runs the controller, root motion sync, weapon masks and foot IK
        public AnimatorDriver driver;
        /// false (default): phase-matched idle/walk/run loops; true: motion matching over the whole database
        public bool useMM;
        bool loopMode;
        public float maxSpeed = 3.0f;
        struct Sim { public float x, z, vx, vz, ax, az, h, w; public bool init; }
        Sim S; public Vector3 Velocity => new Vector3(S.vx, 0, S.vz); bool hadDir; Vector2 lastDir;
        Quaternion[] off, outQ, tmpQ; Vector3 offRO, ro; float[] con = { 1, 1 };
        class Foot { public bool locked; public Vector3 pos; public float rel; }
        readonly Foot[] feet = { new Foot(), new Foot() };
        readonly float[] query;
        float restAnkle;
        static readonly System.Random RNG = new System.Random();

        public MotionActor(Character c)
        {
            MotionDB.Load();
            ch = c; H = c.human;
            int nbAll = H.bones.Length; baseQ = new Quaternion[nbAll];
            for (int i = 0; i < nbAll; i++) baseQ[i] = H.bones[i].localRotation;
            rootPos = H.root.localPosition;
            int nb = MotionDB.nb; map = new int[nb]; mask = new float[nb];
            off = new Quaternion[nb]; outQ = new Quaternion[nb]; tmpQ = new Quaternion[nb];
            string W = c.cfg.weapon;
            for (int k = 0; k < nb; k++)
            {
                int bi; map[k] = HumanDB.BI.TryGetValue(MotionDB.bones[k], out bi) ? bi : -1;
                off[k] = Quaternion.identity; outQ[k] = Quaternion.identity;
                string n = MotionDB.bones[k]; float m = 0;
                bool armR = n == "upperarm01.R" || n == "lowerarm01.R" || n == "wrist.R";
                bool armL = n == "upperarm01.L" || n == "lowerarm01.L" || n == "wrist.L";
                if (W != "none" && armR) m = (W == "spear" || W == "staff") ? 0.7f : 0.5f;
                if (W == "shield" && armL) m = 0.85f;
                if (n.StartsWith("wrist") && m == 0) m = 0.3f;
                mask[k] = m;
            }
            query = new float[MotionDB.nf];
            restAnkle = Mathf.Min(LocalY("foot.L"), LocalY("foot.R"));
            {   // upright trunk and pelvis angles of the rest pose
                Transform rt = H.Bone("root"), sp = H.Bone("spine03"), nk = H.Bone("neck01");
                if (rt != null && sp != null && nk != null)
                {
                    Vector3 u = Quaternion.Inverse(c.go.transform.rotation) * (nk.position - rt.position), w = Quaternion.Inverse(c.go.transform.rotation) * (sp.position - rt.position);
                    restTrunk = Mathf.Atan2(u.z, u.y); restLower = Mathf.Atan2(w.z, w.y);
                    var hb0 = H.Bone("head"); if (hb0 != null) { headRest = Quaternion.Inverse(sp.rotation) * hb0.rotation; hasHeadRest = true; }
                }
            }
            // weapon / shield arms: remember the built pose of the whole arm chain relative to the body
            Quaternion gi0 = Quaternion.Inverse(c.go.transform.rotation);
            for (int si = 0; si < 2; si++)
                for (int j = 0; j < ARM_CHAIN.Length; j++)
                {
                    armBone[si, j] = H.Bone(ARM_CHAIN[j] + "." + SIDES[si]);
                    if (armBone[si, j] != null) armRel[si, j] = gi0 * armBone[si, j].rotation;
                }
            idleBreakAt = 9 + (float)RNG.NextDouble() * 5;
            frame = StartFrame();
        }
        /// forget the controller state (teleport / mode change)
        public void ResetSim() { S.init = false; S.vx = S.vz = S.ax = S.az = S.w = 0; hadDir = false; }
        /// keep the actor standing on the spot at the origin (character editor)
        public void HoldInPlace(float dt)
        {
            noLock = true;
            Locomotion(dt, null, 0, null, Vector2.zero, null);
            heading = 0; S.x = S.z = 0; S.h = 0;
        }
        float LocalY(string b) { return ch.model.InverseTransformPoint(H.Bone(b).position).y; }
        static int StartFrame()
        {
            for (int f = 0; f < MotionDB.N2; f++) if (MotionDB.IDLEB[f] != 0 && MotionDB.VALID[f] != 0) return Mathf.Min(f + 60, MotionDB.RANGE_END[f]);
            return 0;
        }

        // pose of a fractional database frame
        void SamplePose(float fr, Quaternion[] oq, ref Vector3 oro, float[] ocon)
        {
            int f0 = Mathf.FloorToInt(fr), end = MotionDB.RANGE_END[f0], f1 = Math.Min(end, f0 + 1); float u = fr - f0;
            int nb = MotionDB.nb;
            for (int k = 0; k < nb; k++) oq[k] = Quaternion.Slerp(MotionDB.Quat(f0, k), MotionDB.Quat(f1, k), u);
            var R = MotionDB.RO;
            oro = new Vector3(R[f0 * 3] * (1 - u) + R[f1 * 3] * u, R[f0 * 3 + 1] * (1 - u) + R[f1 * 3 + 1] * u, R[f0 * 3 + 2] * (1 - u) + R[f1 * 3 + 2] * u);
            if (ocon != null) for (int j = 0; j < 2; j++) ocon[j] = MotionDB.CON[f0 * 2 + j] * (1 - u) + MotionDB.CON[f1 * 2 + j] * u;
        }

        // brute-force nearest neighbour over all valid frames
        int Search(float[] q, int ex0, int ex1, out float bestCost)
        {
            var F = MotionDB.F; int NF = MotionDB.nf; var V = MotionDB.VALID; int best = -1; float bc = float.PositiveInfinity;
            for (int f = 0; f < MotionDB.N2; f++)
            {
                if (V[f] == 0 || (f >= ex0 && f <= ex1)) continue;
                float c = 0; int o = f * NF;
                for (int j = 0; j < NF; j++) { float d = q[j] - F[o + j]; c += d * d; if (c >= bc) break; }
                if (c < bc) { bc = c; best = f; }
            }
            bestCost = bc; return best;
        }
        float CostAt(float[] q, int f) { float c = 0; int o = f * MotionDB.nf; for (int j = 0; j < MotionDB.nf; j++) { float d = q[j] - MotionDB.F[o + j]; c += d * d; } return c; }

        void Transition(int f)
        {
            // inertialization: remember the offset between what is shown now and the new pose
            Vector3 nro = Vector3.zero;
            SamplePose(f, tmpQ, ref nro, null);
            for (int k = 0; k < tmpQ.Length; k++)
            {
                var o = outQ[k] * Quaternion.Inverse(tmpQ[k]);
                if (o.w < 0) o = new Quaternion(-o.x, -o.y, -o.z, -o.w);
                off[k] = o;
            }
            offRO = ro - nro;
            frame = f;
        }

        /// dir: desired world direction (x,z) or null; mag 0..1; lockPos: target to face (strafing) or null.
        /// collide: clamps a proposed position to the walkable area. Returns the displacement (x,z) for this frame.
        public Vector2 Locomotion(float dt, Vector2? dir, float mag, Vector2? lockPos, Vector2 myPos, Func<Vector2, Vector2> collide)
        {
            if (!S.init) { S.x = myPos.x; S.z = myPos.y; S.h = heading; S.init = true; }
            float spd = 0;
            if (dir.HasValue)
            {
                // mag 0..1: stick inside the ring (turn on the spot -> stroll -> brisk walk); 1..2: finger pulled past the ring (run)
                float walk = mag < 0.22f ? 0 : 0.75f + 0.65f * Mathf.Clamp01((mag - 0.22f) / 0.78f);
                spd = mag <= 1 ? walk : 1.4f + (maxSpeed - 1.4f) * Mathf.Clamp01(mag - 1);
                // sprint: holding the run for a moment builds into an all-out sprint (and it drops at once when the run is released)
                float vNow = Mathf.Sqrt(S.vx * S.vx + S.vz * S.vz);
                sprintT = mag > 1.6f && vNow > 2.3f && !lockPos.HasValue ? Mathf.Min(3f, sprintT + dt) : Mathf.Max(0f, sprintT - dt * 4);
                sprint = Smooth01(0.7f, 1.7f, sprintT);
                if (mag > 1.6f) spd += SPRINT_EXTRA * sprint;
                if (lockPos.HasValue) spd = Mathf.Min(spd, 1.7f);
                if (guardHeld) spd = Mathf.Min(spd, 1.1f);          // behind the shield you only walk
                int legsC = Legs, armsC = (limbs[1] ? 1 : 0) + (limbs[2] ? 1 : 0);
                if (legsC == 1) spd = Mathf.Min(spd, 1.9f);                                        // hopping on one leg
                if (legsC == 0) spd = Mathf.Min(spd, armsC == 2 ? 0.5f : armsC == 1 ? 0.35f : 0.18f);   // crawling
            }
            if (!dir.HasValue) { sprintT = Mathf.Max(0f, sprintT - dt * 4); sprint = Smooth01(0.7f, 1.7f, sprintT); }
            float vgx = dir.HasValue ? dir.Value.x * spd : 0, vgz = dir.HasValue ? dir.Value.y * spd : 0;
            float hGoal = S.h;
            if (lockPos.HasValue) hGoal = Mathf.Atan2(lockPos.Value.x - S.x, lockPos.Value.y - S.z);
            else if (dir.HasValue) hGoal = Mathf.Atan2(dir.Value.x, dir.Value.y);
            const float HV = 0.15f, HR = 0.1f;   // snappy: velocity and facing reach the stick in ~0.25 s
            SpringChar(ref S.x, ref S.vx, ref S.ax, vgx, HV, dt);
            SpringChar(ref S.z, ref S.vz, ref S.az, vgz, HV, dt);
            SpringAng(ref S.h, ref S.w, hGoal, HR, dt);
            if (collide != null) { var p = collide(new Vector2(S.x, S.z)); S.x = p.x; S.z = p.y; }
            float dxs = S.x - myPos.x, dzs = S.z - myPos.y, dl = Mathf.Sqrt(dxs * dxs + dzs * dzs);
            if (dl > 0.25f) { S.x = myPos.x + dxs / dl * 0.25f; S.z = myPos.y + dzs / dl * 0.25f; }

            if (driver != null) return AnimatorLocomotion(dt, dir, mag, spd, vgx, vgz, hGoal, myPos);
            loopMode = !useMM && !lockPos.HasValue;
            if (loopMode) return LoopLocomotion(dt, myPos);

            // ---- query: current pose features + predicted trajectory relative to the character ----
            int NF = MotionDB.nf; var mean = MotionDB.mean; var scale = MotionDB.scale;
            int f0 = Mathf.FloorToInt(frame);
            for (int j = 0; j < 15; j++) query[j] = MotionDB.F[f0 * NF + j];
            float c = Mathf.Cos(heading), s = Mathf.Sin(heading);
            for (int k = 0; k < 3; k++)
            {
                float t = (k + 1) / 3f;
                float px = S.x, pvx = S.vx, pax = S.ax, pz = S.z, pvz = S.vz, paz = S.az, ph = S.h, pw = S.w;
                SpringChar(ref px, ref pvx, ref pax, vgx, HV, t); SpringChar(ref pz, ref pvz, ref paz, vgz, HV, t);
                SpringAng(ref ph, ref pw, hGoal, HR, t);
                float rx = px - myPos.x, rz = pz - myPos.y;
                float lx = c * rx - s * rz, lz = s * rx + c * rz;      // +x = character right, +z = forward
                float dh = Wrap(ph - heading);
                query[15 + 2 * k] = (lx - mean[15 + 2 * k]) * scale[15 + 2 * k];
                query[16 + 2 * k] = (lz - mean[16 + 2 * k]) * scale[16 + 2 * k];
                query[21 + 2 * k] = (Mathf.Sin(dh) - mean[21 + 2 * k]) * scale[21 + 2 * k];
                query[22 + 2 * k] = (Mathf.Cos(dh) - mean[22 + 2 * k]) * scale[22 + 2 * k];
            }
            // ---- search on a timer, at clip ends, or when the input changes sharply ----
            searchT -= dt;
            bool atEnd = frame + 1 >= MotionDB.RANGE_END[f0];
            bool force = atEnd;
            if (dir.HasValue && hadDir && Vector2.Dot(dir.Value, lastDir) < 0.7f) force = true;
            if (dir.HasValue != hadDir) force = true;
            hadDir = dir.HasValue; if (dir.HasValue) lastDir = dir.Value;
            if (searchT <= 0 || force)
            {
                searchT = 0.07f;
                float cur = CostAt(query, f0), bc;
                int best = Search(query, f0 - 3, f0 + 12, out bc);
                // a jump must beat the current clip clearly, otherwise the pose flickers between clips
                if (best >= 0 && (bc < cur * 0.7f - 0.05f || (force && atEnd))) Transition(best);
                cost = Mathf.Min(cur, bc);
            }
            // ---- idle breaker ----
            bool idle = !dir.HasValue && Mathf.Sqrt(S.vx * S.vx + S.vz * S.vz) < 0.05f;
            idleT = idle ? idleT + dt : 0;
            if (idleT > idleBreakAt)
            {
                idleT = 0; idleBreakAt = 9 + (float)RNG.NextDouble() * 6;
                var cands = new System.Collections.Generic.List<int>();
                for (int f = 0; f < MotionDB.N2; f += 7) if (MotionDB.IDLEB[f] != 0 && MotionDB.VALID[f] != 0) cands.Add(f);
                if (cands.Count > 0) Transition(cands[RNG.Next(cands.Count)]);
            }
            // ---- advance the animation and apply its root motion ----
            frame = Mathf.Min(MotionDB.RANGE_END[Mathf.FloorToInt(frame)], frame + dt * MotionDB.fps);
            int fi = Mathf.FloorToInt(frame);
            float fwd = MotionDB.RM[fi * 3], lat = MotionDB.RM[fi * 3 + 1], yr = MotionDB.RM[fi * 3 + 2];
            heading += yr * dt;
            float fx = Mathf.Sin(heading), fz = Mathf.Cos(heading), rxv = Mathf.Cos(heading), rzv = -Mathf.Sin(heading);
            float dx = (fx * fwd + rxv * lat) * dt, dz = (fz * fwd + rzv * lat) * dt;
            float kk = 1 - Mathf.Exp(-dt * LN2 / 0.05f);
            dx += (S.x - (myPos.x + dx)) * kk; dz += (S.z - (myPos.y + dz)) * kk;
            heading += Wrap(S.h - heading) * (1 - Mathf.Exp(-dt * LN2 / 0.05f));
            speed = Mathf.Sqrt(fwd * fwd + lat * lat);
            narrow = 1 - Mathf.Clamp01(Mathf.Abs(lat) / 0.5f);
            label = LabelFor(fwd, lat, yr, dir.HasValue) + InjuryLabel();
            vel = Mathf.Sqrt(dx * dx + dz * dz) / Mathf.Max(dt, 1e-4f);
            return new Vector2(dx, dz);
        }

        Vector2 AnimatorLocomotion(float dt, Vector2? dir, float mag, float spd, float vgx, float vgz, float hGoal, Vector2 myPos)
        {
            float turnDeg = Wrap(hGoal - heading) * Mathf.Rad2Deg;
            bool moving = dir.HasValue && spd > 0;
            driver.Drive(new Vector2(S.vx, S.vz), new Vector2(vgx, vgz), S.w, dir.HasValue ? turnDeg : 0, dir.HasValue ? mag : 0, moving, dt);
            float fwd, lat, yr; driver.ReadRootMotion(out fwd, out lat, out yr);
            heading += yr * dt;
            float fx = Mathf.Sin(heading), fz = Mathf.Cos(heading), rxv = Mathf.Cos(heading), rzv = -Mathf.Sin(heading);
            float dx = (fx * fwd + rxv * lat) * dt, dz = (fz * fwd + rzv * lat) * dt;
            float kk = 1 - Mathf.Exp(-dt * LN2 / 0.05f);
            dx += (S.x - (myPos.x + dx)) * kk; dz += (S.z - (myPos.y + dz)) * kk;
            heading += Wrap(S.h - heading) * (1 - Mathf.Exp(-dt * LN2 / 0.05f));
            speed = Mathf.Sqrt(fwd * fwd + lat * lat);
            narrow = 1 - Mathf.Clamp01(Mathf.Abs(lat) / 0.5f);
            label = driver.StateLabel() + InjuryLabel();
            vel = Mathf.Sqrt(dx * dx + dz * dz) / Mathf.Max(dt, 1e-4f);
            return new Vector2(dx, dz);
        }

        static string LabelFor(float fwd, float lat, float yr, bool dir)
        {
            float s = Mathf.Sqrt(fwd * fwd + lat * lat);
            if (s < 0.15f) return Mathf.Abs(yr) > 0.6f ? "turn in place" : (dir ? "idle → start" : "idle");
            string kind = s < 1.5f ? "walk" : s < 2.4f ? "jog" : "sprint";
            if (fwd < -0.3f && Mathf.Abs(lat) < Mathf.Abs(fwd)) return kind + " backward";
            if (Mathf.Abs(lat) > Mathf.Abs(fwd) * 0.8f) return "strafe " + (lat > 0 ? "R" : "L");
            if (Mathf.Abs(yr) > 1.2f) return kind + " pivot/turn " + (yr > 0 ? "R" : "L");
            if (Mathf.Abs(yr) > 0.35f) return kind + " arc " + (yr > 0 ? "R" : "L");
            return kind + " " + s.ToString("0.0") + " m/s";
        }

        // ---------- pose ----------
        public void Animate(float t, float dt)
        {
            var B = H.bones; int nb = map.Length;
            if (driver != null)
            {
                // the Animator has already written the clip pose this frame
                driver.ReadPose(out ro, con); SteadyPelvis(dt);
                for (int k = 0; k < nb; k++)
                    if (map[k] >= 0 && mask[k] > 0) B[map[k]].localRotation = Quaternion.Slerp(B[map[k]].localRotation, baseQ[map[k]], mask[k]);
                H.root.localPosition = rootPos + ro;
                Injuries(dt);
                Orb(t);
                return;
            }
            if (loopMode) LoopPose();
            else
            {
                SamplePose(frame, outQ, ref ro, con);
                float decay = Mathf.Exp(-dt * LN2 / 0.1f);
                for (int k = 0; k < nb; k++) { off[k] = Quaternion.Slerp(off[k], Quaternion.identity, 1 - decay); outQ[k] = off[k] * outQ[k]; }
                offRO *= decay; ro += offRO;
                SteadyPelvis(dt);
            }
            AttackLayer(dt);
            bool shieldArm = ch.cfg.weapon == "shield";
            for (int k = 0; k < nb; k++)
            {
                if (map[k] < 0) continue;
                // a cut takes the arms over completely (the shield arm keeps its pose)
                float mk = mask[k]; if (!(shieldArm && MotionDB.bones[k].EndsWith(".L"))) mk *= 1 - ovW;
                var q = outQ[k]; if (mk > 0) q = Quaternion.Slerp(q, baseQ[map[k]], mk);
                B[map[k]].localRotation = q;
            }
            H.root.localPosition = rootPos + ro;
            Injuries(dt);
            Orb(t);
        }
        void Orb(float t)
        {
            if (ch.orb != null)
            {
                ch.orb.localRotation = Quaternion.Euler(0, t * 1.4f * Mathf.Rad2Deg, 0);
                float sc = 1 + 0.08f * Mathf.Sin(t * 3); ch.orb.localScale = new Vector3(0.8f * sc, 1.2f * sc, 0.8f * sc);
            }
        }

        /// static editor preview (no locomotion): plays the idle frames in place
        public void PreviewIdle(float dt)
        {
            frame = Mathf.Min(MotionDB.RANGE_END[Mathf.FloorToInt(frame)], frame + dt * MotionDB.fps);
            if (frame + 1 >= MotionDB.RANGE_END[Mathf.FloorToInt(frame)]) Transition(StartFrame());
        }

        // ---------- phase-matched loops (default locomotion) ----------
        // The standard way games get smooth movement from a small set of clips: three clean cycles cut from the mocap
        // (idle, walk, run) are aligned by foot contact and blended by speed with a shared phase. Game logic moves the
        // body, the playback rate follows the real speed so the feet do not slide, and nothing jumps between clips.
        class Loop { public int a, b, len, off; public float T, v, dx, dz, mx, mz; }
        static Loop LIdle, LWalk, LRun;
        static Loop Prep(int a, int b)
        {
            var L = new Loop { a = a, b = b, len = b - a }; L.T = L.len / (float)MotionDB.fps;
            var RM = MotionDB.RM; var RO = MotionDB.RO; var CON = MotionDB.CON;
            for (int f = a; f < b; f++) L.v += Mathf.Sqrt(RM[f * 3] * RM[f * 3] + RM[f * 3 + 1] * RM[f * 3 + 1]); L.v /= L.len;
            // the pelvis offset drifts along a clip (up to half a metre in the run); keep only its cyclic part
            L.dx = RO[b * 3] - RO[a * 3]; L.dz = RO[b * 3 + 2] - RO[a * 3 + 2];
            for (int i = 0; i < L.len; i++) { L.mx += RO[(a + i) * 3] - L.dx * i / L.len; L.mz += RO[(a + i) * 3 + 2] - L.dz * i / L.len; }
            L.mx /= L.len; L.mz /= L.len;
            for (int i = 0; i < L.len; i++) { int f = a + i, p = a + (i + L.len - 1) % L.len; if (CON[p * 2] < 0.5f && CON[f * 2] >= 0.5f) { L.off = i; break; } }   // phase 0 = left foot lands
            return L;
        }
        static void PrepLoops() { if (LIdle != null) return; LIdle = Prep(9019, 9064); LWalk = Prep(648, 685); LRun = Prep(7678, 7700); }
        Quaternion[] lq, q2, q3; float phase, idlePh, wWalk, wRun;
        readonly float[] c2 = new float[2], c3 = new float[2], cTmp = new float[2];
        void SampleLoop(Loop L, float ph, Quaternion[] oq, ref Vector3 oro, float[] ocon)
        {
            int nb = MotionDB.nb; float pos = (Mathf.Repeat(ph, 1f) * L.len + L.off) % L.len, W = Mathf.Min(5f, L.len / 3f);
            SamplePose(L.a + pos, oq, ref oro, ocon);
            oro.x -= L.dx * pos / L.len + L.mx; oro.z -= L.dz * pos / L.len + L.mz;
            if (pos > L.len - W)
            {
                // cross-fade the end of the cycle into its start: a seamless loop
                float t = (pos - (L.len - W)) / W, u = t * t * (3 - 2 * t);
                if (lq == null) lq = new Quaternion[nb];
                Vector3 ro2 = Vector3.zero;
                SamplePose(L.a + pos - L.len, lq, ref ro2, cTmp);
                ro2.x -= L.dx * (pos - L.len) / L.len + L.mx; ro2.z -= L.dz * (pos - L.len) / L.len + L.mz;
                for (int k = 0; k < nb; k++) oq[k] = Quaternion.Slerp(oq[k], lq[k], u);
                oro = Vector3.Lerp(oro, ro2, u);
                for (int j = 0; j < 2; j++) ocon[j] += (cTmp[j] - ocon[j]) * u;
            }
        }
        static float Smooth01(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        Vector2 LoopLocomotion(float dt, Vector2 myPos)
        {
            PrepLoops();
            float v = Mathf.Sqrt(S.vx * S.vx + S.vz * S.vz);
            wRun = Smooth01(1.6f, 2.5f, v); wWalk = Smooth01(0.04f, 0.5f, v);
            float T = Mathf.Lerp(LWalk.T, LRun.T, wRun), nat = Mathf.Lerp(LWalk.v, LRun.v, wRun);
            phase = (phase + dt / T * Mathf.Clamp(v / nat, 0.55f, 1.5f)) % 1f;
            idlePh = (idlePh + dt / LIdle.T) % 1f;
            // the body is moved by the controller; facing follows it smoothly
            float dx = S.x - myPos.x, dz = S.z - myPos.y;
            heading += Wrap(S.h - heading) * (1 - Mathf.Exp(-dt * LN2 / 0.045f));
            speed = v; narrow = 1; vel = Mathf.Sqrt(dx * dx + dz * dz) / Mathf.Max(dt, 1e-4f);
            label = (v < 0.08f ? "idle" : v < 1.6f ? "walk " + v.ToString("0.0") + " m/s" : (sprint > 0.5f ? "sprint " : "run ") + v.ToString("0.0") + " m/s") + InjuryLabel();
            return new Vector2(dx, dz);
        }
        void LoopPose()
        {
            PrepLoops();
            int nb = MotionDB.nb;
            if (q2 == null) { q2 = new Quaternion[nb]; q3 = new Quaternion[nb]; }
            SampleLoop(LIdle, idlePh, outQ, ref ro, con);
            if (wWalk > 0.001f)
            {
                Vector3 ro2 = Vector3.zero, ro3 = Vector3.zero;
                SampleLoop(LWalk, phase, q2, ref ro2, c2);
                if (wRun > 0.001f)
                {
                    SampleLoop(LRun, phase, q3, ref ro3, c3);
                    for (int k = 0; k < nb; k++) q2[k] = Quaternion.Slerp(q2[k], q3[k], wRun);
                    ro2 = Vector3.Lerp(ro2, ro3, wRun); for (int j = 0; j < 2; j++) c2[j] += (c3[j] - c2[j]) * wRun;
                }
                for (int k = 0; k < nb; k++) outQ[k] = Quaternion.Slerp(outQ[k], q2[k], wWalk);
                ro = Vector3.Lerp(ro, ro2, wWalk); for (int j = 0; j < 2; j++) con[j] += (c2[j] - con[j]) * wWalk;
            }
        }

        // ---------- missing limbs ----------
        // The hero is a shadow in armour; limbs can be torn off. Bones of a missing limb are scaled to nothing and the
        // rest of the body compensates on top of the matched mocap pose:
        //   one arm - the torso tilts towards the remaining arm
        //   one leg - hopping: the body rises while the remaining foot is in the air, arms go out for balance
        //   no legs - crawling: prone, pulled along by whichever arms are left
        public readonly bool[] limbs = { true, true, true, true, true };   // head, armL, armR, legL, legR
        float hopPh, hopH, crawlPh, vel;
        int Legs { get { return (limbs[3] ? 1 : 0) + (limbs[4] ? 1 : 0); } }
        readonly float[] gone = new float[6];
        MaterialPropertyBlock mpb;

        public void SetLimbs(bool[] present)
        {
            for (int l = 0; l < 5; l++)
            {
                limbs[l] = present[l]; gone[l + 1] = present[l] ? 0 : 1;
                var b = H.Bone(Character.LIMB_BONE[l]); if (b == null) continue;
                b.localScale = present[l] ? Vector3.one : Vector3.one * 1e-4f;
                foreach (var r in b.GetComponentsInChildren<Renderer>(true)) r.enabled = present[l];   // rigid gear on that limb
            }
            if (mpb == null) mpb = new MaterialPropertyBlock();
            mpb.SetFloatArray("_LimbGone", gone);
            foreach (var r in ch.glyphs) if (r != null) r.SetPropertyBlock(mpb);
            feet[0].locked = feet[1].locked = false; feet[0].rel = feet[1].rel = 0;
        }
        string InjuryLabel()
        {
            bool any = false; for (int l = 0; l < 5; l++) if (!limbs[l]) any = true;
            if (!any) return "";
            if (Legs == 0) return " · ползком";
            if (Legs == 1) return " · на одной ноге";
            string m = ""; string[] nm = { "головы", "л.руки", "п.руки" };
            for (int l = 0; l < 3; l++) if (!limbs[l]) m += (m.Length > 0 ? ", " : "") + nm[l];
            return " · без " + m;
        }
        void Injuries(float dt)
        {
            if (Legs == 0) { Crawl(dt); return; }
            if (Legs == 1) HopBody(dt);
            PinArms();
            if (loopMode) BodyLife(dt);
            ArmLean();
            RelaxArms();
            Combat(dt);
            JointLimits();
            FootIK(dt);
        }
        void Aim(string a, string b, Vector3 dirW)
        {
            Transform ba = H.Bone(a), bb = H.Bone(b); if (ba == null || bb == null) return;
            ba.rotation = Quaternion.FromToRotation((bb.position - ba.position).normalized, dirW.normalized) * ba.rotation;
        }
        void ArmLean()
        {
            if (limbs[1] == limbs[2]) return;
            var sp = H.Bone("spine03"); if (sp == null) return;
            Vector3 fwd = ch.go.transform.rotation * Vector3.forward;
            sp.rotation = Quaternion.AngleAxis((limbs[1] ? 0.08f : -0.08f) * Mathf.Rad2Deg, fwd) * sp.rotation;
        }
        // A real hop cycle: on the ground the leg sinks and loads (the foot travels from front to back under the body), it
        // drives off with the body thrown forward, in the air the leg is pulled up and swung far forward and the arms
        // lift, then it reaches out, lands ahead of the body and gives again.
        float hopStride = 0.13f;
        void HopBody(float dt)
        {
            bool moving = vel > 0.15f; float side = limbs[3] ? -1f : 1f;      // Unity: left is -X
            hopPh = moving ? (hopPh + dt * Mathf.PI * 2 * (1.5f + 0.5f * vel)) % (Mathf.PI * 2) : 0;      // 0..pi in the air, pi..2pi on the ground
            float sn = Mathf.Sin(hopPh), cs = Mathf.Cos(hopPh), kk = moving ? Mathf.Clamp(vel / 1.2f, 0.4f, 1f) : 0;
            hopH = moving && sn > 0 ? 0.19f * kk * sn : 0;
            float dipH = moving && sn < 0 ? 0.075f * kk * -sn : 0;
            hopStride = 0.2f + 0.11f * vel;
            var p = H.root.localPosition; p.y += hopH - dipH; p.x += side * 0.05f; H.root.localPosition = p;   // weight over the remaining foot
            if (!moving) return;
            Vector3 X = ch.go.transform.rotation * Vector3.right;
            float lean = (0.07f + 0.12f * cs) * kk;              // thrown forward at take-off, upright (a touch back) on landing
            Rot("spine05", X, lean * 0.5f); Rot("spine03", X, lean * 0.5f);
            float lift = Mathf.Max(0, sn) * kk;                  // the arms swing up with the jump and drop to load the next one
            for (int si = 0; si < 2; si++) if (limbs[1 + si]) { Rot("upperarm01." + SIDES[si], X, -0.55f * lift + 0.2f * Mathf.Max(0, -sn) * kk); Rot("lowerarm01." + SIDES[si], X, -0.4f * lift); }
        }
        void Crawl(float dt)
        {
            Quaternion gq = ch.go.transform.rotation;
            crawlPh = (crawlPh + dt * vel * Mathf.PI * 2 / 0.5f) % (Mathf.PI * 2);
            float ph = crawlPh;
            H.root.localPosition = new Vector3(rootPos.x, 0.23f + 0.015f * Mathf.Sin(ph * 2), rootPos.z);
            H.root.localRotation = Quaternion.AngleAxis(1.2f * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(-0.13f * Mathf.Sin(ph) * Mathf.Rad2Deg, Vector3.up) * H.root.localRotation;
            Vector3 right = gq * Vector3.right;
            foreach (var n in new[] { "neck01", "head" }) { var b = H.Bone(n); if (b != null) b.rotation = Quaternion.AngleAxis(-0.48f * Mathf.Rad2Deg, right) * b.rotation; }   // look ahead
            for (int si = 0; si < 2; si++)
            {
                if (!limbs[1 + si]) continue;
                string s = SIDES[si]; float sg = si == 0 ? -1f : 1f;
                float r = (Mathf.Sin(ph + (si == 0 ? 0 : Mathf.PI)) + 1) / 2;       // 1 = reaching forward, 0 = pulled back under the chest
                Aim("upperarm01." + s, "lowerarm01." + s, gq * new Vector3(sg * (0.45f - 0.15f * r), -0.55f + 0.3f * r, 0.2f + 0.8f * r));
                Aim("lowerarm01." + s, "wrist." + s, gq * new Vector3(-sg * 0.1f, -0.9f + 0.4f * r, -0.1f + 0.95f * r));
            }
        }

        // The pelvis offset stored with the clips drifts (up to half a metre along a run clip) and differs from clip to
        // clip, so every clip change or loop wrap made the body jump. Keep only its fast part (the sway of the gait).
        Vector3 roAvg; bool roInit;
        void SteadyPelvis(float dt)
        {
            if (!roInit) { roAvg = ro; roInit = true; }
            float k = 1 - Mathf.Exp(-dt * LN2 / 0.25f);
            // a jump of the stored offset is absorbed at once instead of being shown
            if (Mathf.Abs(ro.x - roAvg.x) > 0.12f) roAvg.x = ro.x; if (Mathf.Abs(ro.z - roAvg.z) > 0.12f) roAvg.z = ro.z;
            roAvg.x += (ro.x - roAvg.x) * k; roAvg.z += (ro.z - roAvg.z) * k;
            ro.x -= roAvg.x; ro.z -= roAvg.z;
        }

        // ---------- body life (procedural layer over the loops) ----------
        // What makes the figure read as a person rather than a cut-out:
        //   * the torso leans with acceleration: forward on a start, back on a stop or a sharp reversal (with a dip of
        //     the pelvis as the legs take the load), into the curve on a turn; it also leans a little with speed
        //   * shoulders counter-rotate against the hips with every step; the head stays level and looks into the turn
        //   * arms swing with the gait even when they carry a shield or a weapon, elbows bend more in a run
        //   * standing still is not frozen: breathing, a slow shift of weight, the head drifting
        float lp, lr, dip, look, restTrunk, restLower;
        const float SPRINT_EXTRA = 1.1f; float sprintT; public float sprint;
        void Rot(string name, Vector3 axis, float ang)
        {
            var b = H.Bone(name); if (b == null || Mathf.Abs(ang) < 1e-5f) return;
            b.rotation = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, axis) * b.rotation;
        }
        void BodyLife(float dt)
        {
            float t = Time.time, wW = wWalk, wR = wRun;
            Quaternion gq = ch.go.transform.rotation;
            float c = Mathf.Cos(heading), sn = Mathf.Sin(heading);
            // acceleration in the body frame (Unity: +x = right, +z = forward)
            float aF = S.ax * sn + S.az * c, aS = S.ax * c - S.az * sn, v = Mathf.Sqrt(S.vx * S.vx + S.vz * S.vz);
            float k = 1 - Mathf.Exp(-dt * LN2 / 0.11f);
            float pitchT = Mathf.Clamp(aF * 0.024f, -0.2f, 0.17f), rollT = Mathf.Clamp(aS * 0.02f, -0.11f, 0.11f);
            lp += (pitchT - lp) * k; lr += (rollT - lr) * k;
            float dipT = Mathf.Clamp((Mathf.Sqrt(S.ax * S.ax + S.az * S.az) - 3) * 0.008f, 0, 0.07f); dip += (dipT - dip) * k;
            float lookT = Mathf.Clamp(S.w * 0.1f + Wrap(S.h - heading) * 0.6f, -0.45f, 0.45f); look += (lookT - look) * (1 - Mathf.Exp(-dt * LN2 / 0.15f));
            Vector3 X = gq * Vector3.right, Y = Vector3.up, Z = gq * Vector3.forward;
            float ph = phase * Mathf.PI * 2, cs = Mathf.Cos(ph), breath = Mathf.Sin(t * 1.7f), idle = 1 - wW;
            // posture: the mocap runner bends 17 degrees forward and throws the head back; bring the trunk to a normal
            // carriage for the gait (upright at rest, about 3 degrees forward walking, 8 running) before adding the leans
            float sp = sprint;
            float kw = 1 - ovW;          // during a cut or the guard the captured posture is left alone
            float want = 0.01f + 0.07f * wW + 0.19f * wR + 0.1f * sp;      // 4 degrees forward walking, 12 running, 17 in the sprint: one line from hips to head
            Transform bRoot = H.Bone("root"), bSp = H.Bone("spine03"), bNk = H.Bone("neck01");
            if (bRoot != null && bSp != null && bNk != null)
            {
                // the pelvis first (a pelvis tipped forward under an upright chest is what reads as an arched back); the legs keep their pose
                Vector3 w3 = bSp.position - bRoot.position; float d0 = (want * 0.3f - (Mathf.Atan2(Vector3.Dot(w3, Z), w3.y) - restLower)) * 0.8f * kw;      // the pelvis stays nearly upright: the lean is made by the spine, not by sticking the seat out
                Rot("root", X, d0); Rot("upperleg01.L", X, -d0); Rot("upperleg01.R", X, -d0);
                Vector3 u3 = bNk.position - bRoot.position; float d1 = (want - (Mathf.Atan2(Vector3.Dot(u3, Z), u3.y) - restTrunk)) * kw;
                Rot("spine05", X, d1 * 0.5f); Rot("spine03", X, d1 * 0.5f);
            }
            // running, the back rounds forward over the stride (the head stays up, see below)
            // pelvis: dip under load, slow weight shift when standing
            var rp = H.root.localPosition; rp.y -= dip; rp.x += idle * 0.012f * Mathf.Sin(t * 0.45f); H.root.localPosition = rp;
            // torso: lean (root, lower and upper spine share it), counter-rotation, breathing
            float twist = -(0.07f * wW + 0.07f * wR + 0.08f * sp) * cs;
            string[] chain = { "root", "spine05", "spine03" }; float[] share = { 0.3f, 0.3f, 0.4f };
            for (int i = 0; i < 3; i++) { Rot(chain[i], X, lp * share[i]); Rot(chain[i], Z, -lr * share[i]); }
            Rot("spine05", Y, twist * 0.4f + look * 0.15f); Rot("spine03", Y, twist * 0.6f + look * 0.2f);
            Rot("spine03", X, -0.012f * breath - idle * 0.01f * Mathf.Sin(t * 0.6f));
            // head: stays level, looks where the body is turning, drifts a little at rest
            Rot("neck01", Y, -twist * 0.5f + look * 0.3f + idle * 0.06f * Mathf.Sin(t * 0.37f)); Rot("head", Y, -twist * 0.35f + look * 0.35f + idle * 0.05f * Mathf.Sin(t * 0.23f + 1));
            // head: eyes on the path ahead, a touch below level, whatever the trunk does
            var hb = H.Bone("head");
            if (hb != null)
            {
                Vector3 f = hb.rotation * Vector3.forward;
                float d = (Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) - (-0.07f + idle * 0.02f * Mathf.Sin(t * 0.8f))) * 0.9f * kw;
                Rot("neck01", X, d * 0.4f); Rot("head", X, d * 0.6f);
            }
            // arms: swing opposite to the legs (phase 0 = left foot lands, so the right arm is forward)
            string W = ch.cfg.weapon;
            for (int si = 0; si < 2; si++)
            {
                if (!limbs[1 + si]) continue;
                string s = SIDES[si]; float sg = si == 0 ? -1f : 1f;      // Unity: left is -X
                bool shield = si == 0 && W == "shield", carried = si == 0 ? shield : W != "none";
                // free arms already swing in the mocap; carried arms get the swing here (the shield moves less than a weapon hand)
                float amp = carried ? (shield ? 0.07f * wW + 0.1f * wR : 0.2f * wW + 0.3f * wR) : 0.05f * wW + 0.12f * wR;
                float sw = sg * cs;                                       // +1 = this arm is forward
                Rot("upperarm01." + s, X, -amp * sw + idle * 0.02f * breath);
                Rot("lowerarm01." + s, X, -(carried && !shield ? 0.1f * wW + 0.5f * wR : 0.25f * wR) * Mathf.Max(0, sw) - (shield ? 0 : 0.35f * wR));
                Rot("upperarm01." + s, Z, sg * (0.03f * breath * idle + 0.04f * wR * Mathf.Abs(cs)));
                // run: compact arm action. The elbow stays sharply bent; in front the fist comes up to the chest (towards
                // the breastbone, not reaching forward), behind the elbow goes only a little past the back, fist at the hip
                float aw = Mathf.Min(1f, 0.8f * wR + 0.2f * sp) * (shield ? 0.45f : 1f) * kw;
                if (aw > 0.01f)
                {
                    float u = 0.5f + 0.5f * sw * (carried && !shield ? 0.5f : 1f);     // 0 = arm back, 1 = arm in front; the arm that carries the weapon swings half as far
                    // behind: the arm is driven well back and opens almost straight (only a slight bend in the elbow), away from the ribs
                    float a = -0.95f + 1.05f * u, b2 = -0.7f + 3.1f * u;     // upper arm and forearm angles from straight down, forwards
                    SprintPart("upperarm01." + s, "lowerarm01." + s, gq * new Vector3(sg * (0.3f - 0.07f * u), -Mathf.Cos(a), Mathf.Sin(a)), aw);   // elbows lifted 11-15 degrees off the trunk, travelling on a diagonal
                    SprintPart("lowerarm01." + s, "wrist." + s, gq * new Vector3(sg * (0.12f - 0.62f * u), -Mathf.Cos(b2), Mathf.Sin(b2)), aw);
                }
            }
        }

        // ---------- combat: two sword cuts and the guard ----------
        // The cuts are motion capture retargeted to this skeleton and stored at the end of the motion database. 1: from overhead down to the lower
        // left. 2: wound up low behind the left side and drawn across to the upper right.
        // knots: (game time, clip frame) - the wind-up and the cut are played faster than they were performed.
        // blade: the direction of the blade per clip frame in the body frame (x = right, y = up, z = forward).
        // Clips 1 and 2 are motion capture retargeted to this skeleton: 1 from overhead down to the lower left, 2 wound up low
        // behind the left side and drawn across to the upper right; 3 and 4 are the same cuts mirrored (the database keeps
        // a mirrored copy of every frame). knots (kt, kf): game time -> clip frame - the wind-up and the cut are played faster
        // than they were performed. blade: the blade direction per clip frame in the body frame (x = right, y = up, z = forward).
        // 5 and 6 are posed (spear and staff): 5 a thrust - the shaft drawn back level along the right side with the hips
        // turned away, then driven straight out by the legs and the turn of the hips, the lunge carrying the body behind the
        // point; 6 a sweep with a full turn of the whole body.
        class ProcKey { public float t, yaw, pitch, fwd, drop; public Vector3 ua, fa, la, lf, sh;
            public ProcKey(float t, Vector3 ua, Vector3 fa, Vector3 la, Vector3 lf, Vector3 sh, float yaw, float pitch, float fwd, float drop)
            { this.t = t; this.ua = ua; this.fa = fa; this.la = la; this.lf = lf; this.sh = sh; this.yaw = yaw; this.pitch = pitch; this.fwd = fwd; this.drop = drop; } }
        class AtkClip { public int a; public float dur, cut0, cut1, spin0 = -1, spin1; public float[] kt, kf, blade; public bool mirror, proc, around; public ProcKey[] k;
            public AtkClip Mirrored() { var m = (AtkClip)MemberwiseClone(); m.mirror = true; return m; } }
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        static readonly AtkClip[] ATTACKS = MakeAttacks();
        static AtkClip[] MakeAttacks()
        {
            var A = new AtkClip[7];
            A[1] = new AtkClip { a = 9945, dur = 0.74f, cut0 = 0.4f, cut1 = 0.55f, kt = new[] { 0f, 0.3f, 0.57f, 0.74f }, kf = new[] { 2f, 32f, 52f, 62f },
                blade = new[] { -0.003f, 0.417f, 0.909f, -0.003f, 0.425f, 0.905f, -0.003f, 0.438f, 0.899f, -0.001f, 0.458f, 0.889f, 0.002f, 0.486f, 0.874f, 0.006f, 0.524f, 0.851f, 0.01f, 0.572f, 0.82f, 0.013f, 0.628f, 0.778f, 0.015f, 0.689f, 0.724f, 0.016f, 0.754f, 0.657f, 0.017f, 0.818f, 0.575f, 0.017f, 0.878f, 0.479f, 0.016f, 0.928f, 0.371f, 0.013f, 0.967f, 0.254f, 0.009f, 0.991f, 0.131f, 0.001f, 1f, 0.006f, -0.01f, 0.993f, -0.118f, -0.022f, 0.971f, -0.237f, -0.034f, 0.936f, -0.35f, -0.044f, 0.891f, -0.452f, -0.051f, 0.838f, -0.543f, -0.056f, 0.782f, -0.621f, -0.059f, 0.727f, -0.684f, -0.06f, 0.677f, -0.734f, -0.061f, 0.632f, -0.772f, -0.06f, 0.595f, -0.802f, -0.059f, 0.564f, -0.823f, -0.058f, 0.541f, -0.839f, -0.058f, 0.523f, -0.851f, -0.059f, 0.508f, -0.859f, -0.06f, 0.496f, -0.866f, -0.061f, 0.485f, -0.872f, -0.06f, 0.475f, -0.878f, -0.057f, 0.468f, -0.882f, -0.048f, 0.464f, -0.885f, -0.03f, 0.466f, -0.884f, 0.005f, 0.48f, -0.877f, 0.062f, 0.516f, -0.854f, 0.145f, 0.583f, -0.799f, 0.249f, 0.686f, -0.684f, 0.354f, 0.808f, -0.471f, 0.419f, 0.898f, -0.137f, 0.392f, 0.874f, 0.289f, 0.25f, 0.678f, 0.691f, 0.044f, 0.359f, 0.932f, -0.141f, 0.041f, 0.989f, -0.259f, -0.203f, 0.944f, -0.316f, -0.371f, 0.873f, -0.336f, -0.482f, 0.809f, -0.341f, -0.554f, 0.759f, -0.343f, -0.597f, 0.725f, -0.342f, -0.619f, 0.707f, -0.343f, -0.624f, 0.702f, -0.347f, -0.616f, 0.708f, -0.355f, -0.598f, 0.719f, -0.365f, -0.575f, 0.732f, -0.377f, -0.549f, 0.746f, -0.389f, -0.523f, 0.758f, -0.401f, -0.497f, 0.769f, -0.411f, -0.474f, 0.779f, -0.418f, -0.453f, 0.787f, -0.425f, -0.437f, 0.793f, -0.431f, -0.423f, 0.797f, -0.436f, -0.413f, 0.8f, -0.439f, -0.406f, 0.801f, -0.442f, -0.402f, 0.802f } };
            A[2] = new AtkClip { a = 10011, dur = 0.76f, cut0 = 0.41f, cut1 = 0.56f, kt = new[] { 0f, 0.32f, 0.59f, 0.76f }, kf = new[] { 2f, 35f, 53f, 64f },
                blade = new[] { 0.003f, 0.412f, 0.911f, 0.003f, 0.417f, 0.909f, 0.005f, 0.425f, 0.905f, 0.009f, 0.438f, 0.899f, 0.015f, 0.456f, 0.89f, 0.022f, 0.481f, 0.877f, 0.031f, 0.515f, 0.857f, 0.04f, 0.557f, 0.83f, 0.048f, 0.607f, 0.793f, 0.056f, 0.664f, 0.745f, 0.063f, 0.728f, 0.683f, 0.069f, 0.792f, 0.606f, 0.073f, 0.855f, 0.514f, 0.076f, 0.91f, 0.407f, 0.079f, 0.955f, 0.285f, 0.082f, 0.985f, 0.152f, 0.089f, 0.996f, 0.011f, 0.1f, 0.986f, -0.132f, 0.117f, 0.955f, -0.271f, 0.137f, 0.906f, -0.401f, 0.158f, 0.843f, -0.515f, 0.176f, 0.771f, -0.612f, 0.191f, 0.696f, -0.692f, 0.202f, 0.62f, -0.758f, 0.21f, 0.544f, -0.813f, 0.215f, 0.471f, -0.856f, 0.214f, 0.403f, -0.89f, 0.206f, 0.34f, -0.918f, 0.189f, 0.281f, -0.941f, 0.162f, 0.225f, -0.961f, 0.126f, 0.172f, -0.977f, 0.082f, 0.122f, -0.989f, 0.031f, 0.072f, -0.997f, -0.024f, 0.018f, -1f, -0.089f, -0.046f, -0.995f, -0.17f, -0.12f, -0.978f, -0.275f, -0.203f, -0.94f, -0.407f, -0.286f, -0.868f, -0.562f, -0.358f, -0.745f, -0.727f, -0.408f, -0.551f, -0.867f, -0.419f, -0.269f, -0.925f, -0.368f, 0.089f, -0.859f, -0.236f, 0.455f, -0.684f, -0.026f, 0.729f, -0.472f, 0.234f, 0.85f, -0.3f, 0.495f, 0.816f, -0.204f, 0.706f, 0.678f, -0.173f, 0.842f, 0.51f, -0.178f, 0.913f, 0.368f, -0.194f, 0.942f, 0.274f, -0.203f, 0.953f, 0.226f, -0.194f, 0.958f, 0.211f, -0.17f, 0.961f, 0.217f, -0.141f, 0.962f, 0.234f, -0.115f, 0.96f, 0.255f, -0.096f, 0.957f, 0.275f, -0.082f, 0.953f, 0.292f, -0.073f, 0.949f, 0.307f, -0.069f, 0.946f, 0.316f, -0.066f, 0.945f, 0.322f, -0.065f, 0.943f, 0.325f, -0.063f, 0.943f, 0.328f, -0.061f, 0.942f, 0.331f, -0.059f, 0.941f, 0.333f, -0.057f, 0.941f, 0.334f, -0.057f, 0.942f, 0.332f, -0.059f, 0.943f, 0.328f, -0.06f, 0.944f, 0.326f } };
            A[3] = A[1].Mirrored(); A[4] = A[2].Mirrored();
            A[5] = new AtkClip { proc = true, dur = 0.66f, cut0 = 0.26f, cut1 = 0.4f, k = new[] {
                new ProcKey(0f, V(0.25f, -0.8f, 0.3f), V(0.1f, -0.2f, 0.97f), V(-0.3f, -0.6f, 0.7f), V(0.25f, 0.05f, 0.96f), V(0, 0.03f, 1), 0, 0, 0, 0),
                new ProcKey(0.2f, V(0.3f, -0.55f, -0.75f), V(0.05f, 0.05f, 1), V(-0.1f, -0.5f, 0.85f), V(0.45f, 0.05f, 0.9f), V(0, 0.02f, 1), 0.6f, -0.05f, -0.07f, 0.04f),
                new ProcKey(0.32f, V(0.1f, -0.15f, 0.98f), V(0, -0.02f, 1), V(-0.25f, -0.35f, 0.9f), V(0.3f, -0.1f, 0.95f), V(0, -0.04f, 1), -0.5f, 0.3f, 0.2f, 0.1f),
                new ProcKey(0.42f, V(0.1f, -0.15f, 0.98f), V(0, -0.02f, 1), V(-0.25f, -0.35f, 0.9f), V(0.3f, -0.1f, 0.95f), V(0, -0.04f, 1), -0.45f, 0.26f, 0.17f, 0.09f),
                new ProcKey(0.66f, V(0.25f, -0.8f, 0.3f), V(0.1f, -0.2f, 0.97f), V(-0.3f, -0.6f, 0.7f), V(0.25f, 0.05f, 0.96f), V(0, 0.03f, 1), 0, 0, 0, 0) } };
            A[6] = new AtkClip { proc = true, around = true, dur = 0.86f, cut0 = 0.2f, cut1 = 0.7f, spin0 = 0.12f, spin1 = 0.74f, k = new[] {
                new ProcKey(0f, V(0.3f, -0.7f, 0.4f), V(0.3f, -0.1f, 0.9f), V(-0.3f, -0.6f, 0.7f), V(0.2f, 0, 0.98f), V(0.2f, 0.05f, 0.98f), 0, 0, 0, 0),
                new ProcKey(0.14f, V(0.75f, -0.3f, -0.4f), V(0.95f, 0.05f, 0.1f), V(-0.05f, -0.45f, 0.88f), V(0.7f, 0.05f, 0.7f), V(0.95f, 0.08f, -0.3f), 0.55f, 0.05f, 0, 0.05f),
                new ProcKey(0.4f, V(0.85f, -0.2f, 0.3f), V(0.9f, 0, 0.42f), V(-0.1f, -0.4f, 0.9f), V(0.6f, 0, 0.8f), V(0.92f, 0.02f, 0.38f), 0.1f, 0.12f, 0, 0.09f),
                new ProcKey(0.72f, V(0.7f, -0.3f, 0.6f), V(0.6f, -0.05f, 0.8f), V(-0.2f, -0.45f, 0.87f), V(0.4f, 0, 0.9f), V(0.75f, -0.05f, 0.66f), -0.35f, 0.1f, 0, 0.06f),
                new ProcKey(0.86f, V(0.3f, -0.7f, 0.4f), V(0.3f, -0.1f, 0.9f), V(-0.3f, -0.6f, 0.7f), V(0.2f, 0, 0.98f), V(0.2f, 0.05f, 0.98f), 0, 0, 0, 0) } };
            return A;
        }
        // which attacks a weapon has
        static int[] AttackList(string w)
        {
            return w == "shield" ? new[] { 1, 2 } : w == "spear" ? new[] { 5, 1, 6, 5, 3 } : w == "staff" ? new[] { 1, 6, 2, 3, 4 } : new[] { 1, 2, 3, 4 };
        }
        AtkClip Clip => ATTACKS[atkType];
        // guard with the weapon alone (motion capture) - both hands come up in front of the face, the knees give
        const int GUARD_A = 10079, GUARD_N = 22; const float GUARD_Y0 = 0.09f;
        const float ATK_IN = 0.09f, ATK_OUT = 0.2f, CHAIN_T0 = 0.07f, CHAIN_BLEND = 0.2f; const int STANCE_FRAME = 2;
        float atkW, grdW, stW, ovW, atkFr, atkMix = 1; Quaternion[] atkQ, atkQ2;
        public float spinYaw; public bool atkHeld; bool atkQueued;
        int fromBase = -1; float fromFr; AtkClip fromC;
        bool TwoHanded => ch.cfg.weapon != "shield" && ch.cfg.weapon != "none";
        static float Sm(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }
        static float CR(float a0, float a1, float a2, float a3, float u) { return 0.5f * (2 * a1 + (a2 - a0) * u + (2 * a0 - 5 * a1 + 4 * a2 - a3) * u * u + (3 * a1 - a0 - 3 * a2 + a3) * u * u * u); }
        static Vector3 CR(Vector3 a0, Vector3 a1, Vector3 a2, Vector3 a3, float u) { return new Vector3(CR(a0.x, a1.x, a2.x, a3.x, u), CR(a0.y, a1.y, a2.y, a3.y, u), CR(a0.z, a1.z, a2.z, a3.z, u)); }
        // lays a captured pose over the pose from locomotion: the whole body when standing, from the waist up when moving
        void Lay(Quaternion[] Q, Vector3 aro, float w, bool yAbs)
        {
            float wl = w * (1 - wWalk); bool shield = ch.cfg.weapon == "shield";
            for (int k = 0; k < MotionDB.nb; k++)
            {
                string n = MotionDB.bones[k]; float wk = w;
                if (n == "root" || n.Contains("leg") || n.Contains("foot")) wk = wl;
                else if (shield && n.EndsWith(".L")) wk = 0;               // the shield arm keeps the shield up
                outQ[k] = Quaternion.Slerp(outQ[k], Q[k], wk);
            }
            ro.x += (aro.x - ro.x) * wl; ro.z += (aro.z - ro.z) * wl; ro.y += yAbs ? (aro.y - ro.y) * wl : aro.y * wl;
        }
        // the layers over locomotion: the two-handed fighting stance, the guard, the cut
        void AttackLayer(float dt)
        {
            atkW = 0; grdW = 0; stW = 0; ovW = 0; spinYaw = 0;
            int nb = MotionDB.nb; if (atkQ == null) { atkQ = new Quaternion[nb]; atkQ2 = new Quaternion[nb]; }
            // stance: with a weapon in both hands the body takes the guard the cuts start from (hilt low in front, point forward)
            if (stance > 0.01f && TwoHanded && limbs[2]) { Vector3 s0 = Vector3.zero; SamplePose(ATTACKS[1].a + STANCE_FRAME, atkQ, ref s0, null); Lay(atkQ, s0, stance, true); stW = stance; }
            if (guard > 0.01f && ch.cfg.weapon != "shield" && limbs[2])
            {
                Vector3 gro = Vector3.zero; SamplePose(GUARD_A + guard * (GUARD_N - 1), atkQ, ref gro, null);
                Lay(atkQ, new Vector3(gro.x, gro.y - GUARD_Y0, gro.z), guard, false); grdW = guard;
            }
            ovW = Mathf.Max(stW, grdW);
            if (!Attacking || !limbs[2]) { atkQueued = false; return; }
            // the next cut follows straight on if the button is held or was pressed again: no return to the stance in between
            if (atkT >= atkDur - ATK_OUT && (atkQueued || atkHeld))
            {
                var fc = Clip; float ffr = atkFr; atkQueued = false; atkType = 0;
                StartAttack();
                if (atkType != 0 && !fc.proc && !Clip.proc) { fromC = fc; fromBase = fc.a + (fc.mirror ? MotionDB.n : 0); fromFr = ffr; atkT = CHAIN_T0; }
                if (atkType == 0) return;
            }
            atkT += dt; var C = Clip; float t = Mathf.Min(atkT, C.dur);
            float w = Mathf.Min(fromBase >= 0 ? 1 : Sm(t / ATK_IN), Sm((C.dur - t) / ATK_OUT));
            atkW = w; ovW = Mathf.Max(ovW, w);
            if (C.spin0 >= 0) { float u = Mathf.Clamp01((t - C.spin0) / (C.spin1 - C.spin0)); spinYaw = u >= 1 ? 0 : Mathf.PI * 2 * u * u * u * (u * (u * 6 - 15) + 10); }
            if (C.proc) return;                              // spear and staff moves are posed on the bones in Combat()
            int i = 0; while (i < C.kt.Length - 2 && t > C.kt[i + 1]) i++;
            atkFr = Mathf.Lerp(C.kf[i], C.kf[i + 1], Mathf.Clamp01((t - C.kt[i]) / (C.kt[i + 1] - C.kt[i])));
            Vector3 aro = Vector3.zero; SamplePose(C.a + (C.mirror ? MotionDB.n : 0) + atkFr, atkQ, ref aro, null);
            atkMix = 1;
            if (fromBase >= 0)
            {   // out of the end of the previous cut into this one
                float m = Sm((t - CHAIN_T0) / CHAIN_BLEND); atkMix = m;
                if (m < 1) { Vector3 r2 = Vector3.zero; SamplePose(fromBase + fromFr, atkQ2, ref r2, null); for (int k = 0; k < nb; k++) atkQ[k] = Quaternion.Slerp(atkQ2[k], atkQ[k], m); aro = Vector3.Lerp(r2, aro, m); }
            }
            Lay(atkQ, aro, w, true);
        }
        public const float COMBAT_HOLD = 3f;      // seconds the fighting stance is kept after the last cut, block or hit
        public float combatT; float stance, stanceFeet;
        /// dummies (by index) the current cut has already hit
        public readonly HashSet<int> atkHits = new HashSet<int>();
        /// the edge is travelling: this is when the cut can land
        public bool Cutting => Attacking && atkT > Clip.cut0 && atkT < Clip.cut1;
        public float Reach => Attacking && Clip.proc ? 2.3f : 1.75f;
        public bool Around => Attacking && Clip.around;
        public float Guard => guard;
        /// points the blade passed through this frame (tip and mid-blade): the game turns them into a streak in the air
        public readonly List<Vector3> trail = new List<Vector3>();
        int atkType, lastAtk; float atkT = 9, atkDur; float guard; public bool guardHeld;
        public bool Attacking => atkType != 0 && atkT < atkDur;
        /// starts an attack if none is running (the cooldown is the animation itself); pressed during one, the next is chained on.
        /// Standing: any attack of the weapon but the last one. Running with a blade: sword arm behind -> an overhead cut, in front -> a drawn one.
        public bool StartAttack()
        {
            if (Attacking) { atkQueued = true; return false; }
            if (!limbs[2]) return false;
            string Wn = ch.cfg.weapon; var list = new List<int>(AttackList(Wn)); int type;
            if (Velocity.magnitude > 0.5f && Wn != "spear" && Wn != "staff")
            { bool over = Mathf.Cos(phase * Mathf.PI * 2) <= 0; list.RemoveAll(x => (x % 2 == 1) != over); }
            else if (list.Count > 1) list.RemoveAll(x => x == lastAtk);
            type = list[UnityEngine.Random.Range(0, list.Count)];
            atkHits.Clear(); combatT = COMBAT_HOLD; fromBase = -1;
            lastAtk = type; atkType = type; atkT = 0; atkDur = Clip.dur;
            return true;
        }
        void AimW(string an, string bn, Vector3 dirW, float w)
        {
            if (w <= 0) return; Transform ba = H.Bone(an), bb = H.Bone(bn); if (ba == null || bb == null) return;
            Vector3 cur = bb.position - ba.position; if (cur.sqrMagnitude < 1e-8f) return;
            ba.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur.normalized, dirW.normalized), w) * ba.rotation;
        }
        void BladeW(Vector3 dirW, float w)
        {
            var wb = H.Bone("wrist.R"); if (w <= 0 || wb == null || ch.weapon == null) return;
            wb.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(ch.weapon.up, dirW.normalized), w) * wb.rotation;
        }
        // the whole trunk winds up and unwinds; the head keeps looking ahead
        void TwistRight(float a) { Rot("spine05", Vector3.up, a * 0.4f); Rot("spine03", Vector3.up, a * 0.6f); Rot("neck01", Vector3.up, -a * 0.45f); }
        // The back is rounded forward and the pelvis tucked under (no hollow lower back, no seat pushed out): a little at
        // rest, clearly in the run and in the fighting stance. The head is brought back up so the eyes stay on the path.
        void SpineFix()
        {
            Vector3 X = ch.go.transform.rotation * Vector3.right;
            float f = (0.07f + 0.2f * wRun + 0.06f * sprint + 0.12f * stance) * (1 - 0.6f * atkW);
            Rot("root", X, -0.35f * f); Rot("upperleg01.L", X, 0.35f * f); Rot("upperleg01.R", X, 0.35f * f);
            Rot("spine05", X, 0.6f * f); Rot("spine03", X, 0.75f * f); Rot("neck01", X, -0.5f * f); Rot("head", X, -0.3f * f);
        }
        void Combat(float dt)
        {
            Quaternion gq = ch.go.transform.rotation;
            // fighting stance: kept for a few seconds after the last cut, block or hit. Feet wider than the shoulders
            // (see FootIK), knees bent, the back rounded forward, the arms held a little away from the body
            if (guardHeld) combatT = COMBAT_HOLD;
            combatT = Mathf.Max(0, combatT - dt);
            stance += ((combatT > 0 ? 1f : 0f) - stance) * (1 - Mathf.Exp(-dt * LN2 / 0.14f));
            SpineFix();
            string Wc = ch.cfg.weapon; bool two = TwoHanded;
            // (with a weapon in both hands the stance is the captured guard, see AttackLayer; the posed one is for shield and bare hands)
            float st = two ? 0 : stance * (1 - ovW), still = 1 - wWalk; stanceFeet = st * still;
            if (st > 0.01f)
            {
                Vector3 SX = gq * Vector3.right, SZ = gq * Vector3.forward;
                var sp0 = H.root.localPosition; sp0.y -= (0.1f * still + 0.02f) * st; H.root.localPosition = sp0;
                float hunch = st * (0.6f + 0.4f * still);
                Rot("spine05", SX, 0.1f * hunch); Rot("spine03", SX, 0.12f * hunch); Rot("neck01", SX, -0.15f * hunch);
                for (int si = 0; si < 2; si++)
                {
                    if (!limbs[1 + si]) continue; float sg = si == 0 ? -1f : 1f;
                    Rot("upperarm01." + SIDES[si], SZ, sg * 0.24f * st); Rot("lowerarm01." + SIDES[si], SX, -0.3f * st * still);
                }
            }
            float free = (1 - atkW) * (1 - grdW);
            // two-handed stance: the point is carried forward, 12 degrees above level
            if (two && stW > 0.01f) BladeW(gq * V(0, 0.21f, 0.98f), stW * free);
            float calm = free * (1 - stW);
            if (Wc == "spear" && calm > 0.01f) BladeW(gq * V(0, 0.08f, 1), calm);            // carried level at its middle, point ahead
            if (Wc == "staff")
            {
                // walking staff: upright, its foot on the ground, set down again with every stride; when running (or fighting)
                // the hand slides to the middle and it is carried level like a spear
                float th = 0.3f * Mathf.Sin(phase * Mathf.PI * 2) * wWalk * (1 - wRun);
                Vector3 dir = V(0, Mathf.Cos(th) * (1 - wRun) + 0.1f * wRun, Mathf.Sin(th) * (1 - wRun) + wRun);
                if (calm > 0.01f)
                {
                    BladeW(gq * dir, calm); float up = calm * (1 - wRun);
                    AimW("upperarm01.R", "lowerarm01.R", gq * V(0.2f, -0.82f, 0.45f), up); AimW("lowerarm01.R", "wrist.R", gq * V(0.05f, 0.3f, 0.95f), up); BladeW(gq * dir, calm);
                }
                if (ch.weaponSlide != null) ch.weaponSlide.localPosition = new Vector3(0, -0.5f + 0.5f * Mathf.Max(wRun, 1 - calm), 0);
            }
            // in the stance the shield is carried out in front of the body
            if (st > 0.01f && Wc == "shield" && limbs[1])
            { AimW("upperarm01.L", "lowerarm01.L", gq * V(-0.28f, -0.72f, 0.63f), 0.85f * st); AimW("lowerarm01.L", "wrist.L", gq * V(0.3f, 0.2f, 0.93f), 0.85f * st); }
            // guard
            guard += ((guardHeld ? 1f : 0f) - guard) * (1 - Mathf.Exp(-dt * LN2 / 0.06f));
            float gw = guard; bool shield = Wc == "shield";
            if (gw > 0.01f)
            {
                if (shield) { var rp = H.root.localPosition; rp.y -= 0.05f * gw; H.root.localPosition = rp; }
                Vector3 X = gq * Vector3.right; Rot("spine05", X, 0.06f * gw); Rot("spine03", X, 0.06f * gw);
                if (shield && limbs[1])
                {
                    // the forearm lies across the chest and the shield is turned to stand upright, squarely facing forward
                    AimW("upperarm01.L", "lowerarm01.L", gq * V(-0.3f, -0.6f, 0.74f), gw); AimW("lowerarm01.L", "wrist.L", gq * V(0.8f, 0.25f, 0.45f), gw);
                    Transform sh = ch.shield, fb = H.Bone("lowerarm02.L");
                    if (sh != null && fb != null)
                    {
                        fb.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(sh.forward, (gq * V(0f, 0.08f, 1f)).normalized), gw) * fb.rotation;
                        Vector3 f = sh.forward, up = sh.up, want = Vector3.up - f * Vector3.Dot(Vector3.up, f);
                        if (want.sqrMagnitude > 1e-6f) fb.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(up, want.normalized, f) * gw, f) * fb.rotation;
                    }
                }
                else if (limbs[2]) BladeW(gq * V(-0.8f, 0.55f, 0.2f), gw);        // the body comes from the captured guard; the blade is laid across in front of the face
            }
            // attack: a cut is driven by the captured motion (AttackLayer) and only the blade is set here; a spear or staff
            // move is posed from its keys. Either way the edge leaves a streak in the air.
            plantW = 0; if (!Attacking || atkW <= 0) return;
            var C = Clip; float w = atkW;
            if (C.proc)
            {
                var K = C.k; float t = Mathf.Min(atkT, C.dur); int i = 0; while (i < K.Length - 2 && t > K[i + 1].t) i++;
                float u = Mathf.Clamp01((t - K[i].t) / (K[i + 1].t - K[i].t));
                ProcKey p0 = K[Mathf.Max(i - 1, 0)], p1 = K[i], p2 = K[i + 1], p3 = K[Mathf.Min(i + 2, K.Length - 1)];
                float stl = 1 - wWalk * 0.6f; Vector3 X = gq * Vector3.right;
                // the legs and hips carry it: the pelvis goes forward and down into the lunge, the trunk turns and leans behind the shaft
                var rp = H.root.localPosition; rp.z += CR(p0.fwd, p1.fwd, p2.fwd, p3.fwd, u) * w * stl; rp.y -= CR(p0.drop, p1.drop, p2.drop, p3.drop, u) * w; H.root.localPosition = rp;
                TwistRight(CR(p0.yaw, p1.yaw, p2.yaw, p3.yaw, u) * w);
                float pitch = CR(p0.pitch, p1.pitch, p2.pitch, p3.pitch, u) * w * 0.5f; Rot("spine05", X, pitch); Rot("spine03", X, pitch);
                AimW("upperarm01.R", "lowerarm01.R", gq * CR(p0.ua, p1.ua, p2.ua, p3.ua, u), w); AimW("lowerarm01.R", "wrist.R", gq * CR(p0.fa, p1.fa, p2.fa, p3.fa, u), w);
                if (limbs[1]) { AimW("upperarm01.L", "lowerarm01.L", gq * CR(p0.la, p1.la, p2.la, p3.la, u), w); AimW("lowerarm01.L", "wrist.L", gq * CR(p0.lf, p1.lf, p2.lf, p3.lf, u), w); }
                BladeW(gq * CR(p0.sh, p1.sh, p2.sh, p3.sh, u), w);
            }
            else
            {
                Vector3 bd = BladeOf(C, atkFr);
                if (fromBase >= 0 && atkMix < 1) bd = Vector3.Lerp(BladeOf(fromC, fromFr), bd, atkMix);
                BladeW(gq * bd, w);
            }
            float tipY = Wc == "spear" ? 1.05f : Wc == "staff" ? 0.9f : 0.8f;
            if (ch.weapon != null && atkT > C.cut0 - 0.05f && atkT < C.cut1 + 0.03f) { trail.Add(ch.weapon.TransformPoint(0, tipY, 0)); trail.Add(ch.weapon.TransformPoint(0, tipY * 0.55f, 0)); }
        }
        static Vector3 BladeOf(AtkClip C, float fr)
        {
            int nfb = C.blade.Length / 3, f0 = Mathf.Min(nfb - 1, Mathf.FloorToInt(fr)), f1 = Mathf.Min(nfb - 1, f0 + 1); float u = fr - f0;
            Vector3 bd = Vector3.Lerp(new Vector3(C.blade[f0 * 3], C.blade[f0 * 3 + 1], C.blade[f0 * 3 + 2]), new Vector3(C.blade[f1 * 3], C.blade[f1 * 3 + 1], C.blade[f1 * 3 + 2]), u);
            if (C.mirror) bd.x = -bd.x;
            return bd;
        }
        readonly Vector3[] plant = new Vector3[2]; float plantW;
        /// moves the simulated position (dash) without touching the velocity
        public void Teleport(float dx, float dz, float newHeading) { S.x += dx; S.z += dz; S.h = newHeading; heading = newHeading; }

        // ---------- anatomical joint limits ----------
        // Knees and elbows are hinges: they never bend the wrong way and never fold further than a real joint can.
        // The head cannot turn or nod further than a neck allows.
        Quaternion headRest; bool hasHeadRest;
        void Hinge(string an, string bn, string cn, bool fwdBlocked, float maxFlex, Vector3 Z)
        {
            Transform ba = H.Bone(an), bb = H.Bone(bn), bc = H.Bone(cn); if (ba == null || bb == null || bc == null) return;
            Vector3 t = bb.position - ba.position, sv = bc.position - bb.position;
            if (t.sqrMagnitude < 1e-8f || sv.sqrMagnitude < 1e-8f) return;
            t.Normalize(); sv.Normalize(); Vector3 s2 = sv;
            if (fwdBlocked)
            {   // knee: the shin may fold back, never forwards past the line of the thigh
                Vector3 f = Z - t * Vector3.Dot(Z, t);
                if (f.sqrMagnitude > 1e-4f) { f.Normalize(); float c = Vector3.Dot(s2, f); if (c > 0) s2 = (s2 - f * c).normalized; }
            }
            float flex = Mathf.Acos(Mathf.Clamp(Vector3.Dot(s2, t), -1f, 1f));
            if (flex > maxFlex) { Vector3 ax = Vector3.Cross(t, s2); if (ax.sqrMagnitude > 1e-8f) s2 = Quaternion.AngleAxis(maxFlex * Mathf.Rad2Deg, ax.normalized) * t; }
            if ((s2 - sv).sqrMagnitude > 1e-8f) bb.rotation = Quaternion.FromToRotation(sv, s2) * bb.rotation;
        }
        void JointLimits()
        {
            Vector3 Z = ch.go.transform.rotation * Vector3.forward;
            for (int si = 0; si < 2; si++)
            {
                string s = SIDES[si];
                Hinge("upperleg01." + s, "lowerleg01." + s, "foot." + s, true, 2.45f, Z);      // knee: 0..140 degrees
                Hinge("upperarm01." + s, "lowerarm01." + s, "wrist." + s, false, 2.53f, Z);    // elbow: up to 145 degrees
            }
            // neck: the head stays within about 57 degrees of its rest attitude on the chest
            Transform hb = H.Bone("head"), cb = H.Bone("spine03");
            if (hb != null && cb != null && hasHeadRest)
            {
                Quaternion rel = Quaternion.Inverse(cb.rotation) * hb.rotation, d = Quaternion.Inverse(headRest) * rel;
                float ang = Quaternion.Angle(Quaternion.identity, d) * Mathf.Deg2Rad;
                if (ang > 1.0f) hb.rotation = cb.rotation * headRest * Quaternion.Slerp(Quaternion.identity, d, 1.0f / ang);
            }
        }
        void SprintPart(string an, string bn, Vector3 dirW, float w)
        {
            Transform ba = H.Bone(an), bb = H.Bone(bn); if (ba == null || bb == null) return;
            Vector3 cur = bb.position - ba.position; if (cur.sqrMagnitude < 1e-8f) return;
            ba.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur.normalized, dirW.normalized), w) * ba.rotation;
        }

        // ---------- weapon arms ----------
        // An arm that carries something keeps the pose it was built with, relative to the body: fully for the shield
        // (it stays in front of the body instead of following the mocap arm swing), mostly for a weapon.
        // The whole chain is set in world space, so the mocap collar bone cannot swing the arm out sideways.
        static readonly string[] ARM_CHAIN = { "upperarm01", "upperarm02", "lowerarm01", "lowerarm02", "wrist" };
        readonly Transform[,] armBone = new Transform[2, 5]; readonly Quaternion[,] armRel = new Quaternion[2, 5];
        void PinArms()
        {
            string W = ch.cfg.weapon; if (W == "none") return;
            Quaternion gq = ch.go.transform.rotation;
            for (int si = 0; si < 2; si++)
            {
                float w = si == 0 ? (W == "shield" ? 1f : 0f) : (W == "spear" || W == "staff" ? 0.8f : 0.55f);
                if (si == 1) w *= 1 - ovW;                 // the cut drives the sword arm itself
                if (w <= 0.001f || !limbs[1 + si]) continue;
                for (int j = 0; j < ARM_CHAIN.Length; j++)
                {
                    var b = armBone[si, j]; if (b == null) continue;
                    b.rotation = Quaternion.Slerp(b.rotation, gq * armRel[si, j], w);
                }
            }
        }

        // ---------- arms ----------
        // The mocap actors hold their arms away from the body. Bring a hanging arm back to the torso: the sideways
        // angle of the upper arm is limited softly, the forward/back swing is untouched.
        const float ARM_MAX = 0.13f, ARM_SOFT = 0.12f;
        void RelaxArms()
        {
            Quaternion gq = ch.go.transform.rotation, gi = Quaternion.Inverse(gq);
            for (int si = 0; si < 2; si++)
            {
                string s = SIDES[si];
                if (s == "L" && ch.cfg.weapon == "shield") continue;
                if (s == "R" && ch.cfg.weapon != "none") continue;
                if (!limbs[si == 0 ? 1 : 2]) continue;
                Transform up = H.Bone("upperarm01." + s), el = H.Bone("lowerarm01." + s); if (up == null || el == null) continue;
                Vector3 dW = (el.position - up.position).normalized, d = gi * dW;
                float sign = s == "L" ? -1f : 1f;       // Unity: the left side is -X
                float o = d.x * sign, r = Mathf.Sqrt(o * o + d.y * d.y), abd = Mathf.Atan2(o, -d.y);
                bool oneLeg = limbs[3] != limbs[4];
                if (ovW > 0.3f) continue;            // a cut or the guard raises the arms on purpose
                if (!oneLeg && abd <= ARM_MAX) continue;
                float a2 = oneLeg ? Mathf.Max(abd, 0.55f + 0.12f * Mathf.Sin(hopPh)) : (ARM_MAX + 0.13f * wRun) + Mathf.Max(0, abd - ARM_MAX - 0.13f * wRun) * ARM_SOFT;   // one leg: arms out for balance
                Vector3 d2 = gq * new Vector3(Mathf.Sin(a2) * r * sign, -Mathf.Cos(a2) * r, d.z).normalized;
                up.rotation = Quaternion.FromToRotation(dW, d2) * up.rotation;
            }
        }

        // ---------- foot IK ----------
        static void RotateWorld(Transform b, Quaternion delta) { b.rotation = delta * b.rotation; }
        struct LegInfo { public Transform hip, knee, foot, toe; public Vector3 target; public float l1, l2; }
        readonly LegInfo[] legs = new LegInfo[2];
        static readonly string[] SIDES = { "L", "R" };
        void FootIK(float dt)
        {
            var model = ch.model;
            float scaleY = model.lossyScale.y, groundAnkle = model.position.y + restAnkle * scaleY;
            // Foot locking is off: the body follows the controller, and a foot pinned to the ground while the body moved on
            // was what made the knees buckle. Feet are only kept from sinking below the ground.
            bool moving = false;
            float drop = 0;
            bool hopping = limbs[3] != limbs[4];
            for (int si = 0; si < 2; si++)
            {
                string s = SIDES[si];
                legs[si] = new LegInfo();
                if (!limbs[3 + si]) continue;
                Transform hip = H.Bone("upperleg01." + s), knee = H.Bone("lowerleg01." + s), foot = H.Bone("foot." + s), toe = H.Bone("toe3-1." + s);
                Vector3 hp = hip.position, kp = knee.position, ap = foot.position;
                var F = feet[si]; float cc = con[si];
                if (moving && cc > 0.6f) { if (!F.locked) { F.locked = true; F.pos = new Vector3(ap.x, 0, ap.z); } F.rel = 1; }
                else { F.locked = false; F.rel = Mathf.Max(0, F.rel - dt / 0.1f); }
                float dxl = F.pos.x - ap.x, dzl = F.pos.z - ap.z;
                if (F.rel > 0 && dxl * dxl + dzl * dzl > 0.16f * 0.16f) { F.locked = false; F.rel = 0; }
                Vector3 target = ap;
                if (plantW > 0.01f) { target.x += (plant[si].x - target.x) * plantW; target.z += (plant[si].z - target.z) * plantW; }
                if (F.rel > 0) { target.x += dxl * F.rel; target.z += dzl * F.rel; }
                else if (narrow > 0 && ovW <= 0.3f)
                {
                    // feet under the hips: the mocap stance is wider than a relaxed one (not while side-stepping)
                    Transform g = ch.go.transform; Quaternion gi = Quaternion.Inverse(g.rotation);
                    Vector3 al = gi * (ap - g.position); float lim = Mathf.Abs((gi * (hp - g.position)).x), ax = Mathf.Abs(al.x);
                    bool movedT = false;
                    if (ax > lim) { float nx = Mathf.Sign(al.x) * (lim + (ax - lim) * 0.1f); al.x += (nx - al.x) * narrow; movedT = true; }
                    // fighting stance: feet a little wider than the shoulders, the left one half a step ahead
                    if (stanceFeet > 0.01f) { float sgn = si == 0 ? -1f : 1f; al.x += (sgn * (lim + 0.21f) - al.x) * stanceFeet; al.z += -sgn * 0.1f * stanceFeet; movedT = true; }
                    if (movedT) { Vector3 w = g.rotation * al + g.position; target.x = w.x; target.z = w.z; }
                }
                if (hopping)
                {
                    // hopping: the foot stays under the body, swings forward in the air and back on the ground
                    Transform g = ch.go.transform; float sg = si == 0 ? -1f : 1f, mv = vel > 0.15f ? 1f : 0f;
                    Vector3 w = g.rotation * new Vector3(sg * 0.03f, 0, -hopStride * Mathf.Cos(hopPh) * mv) + g.position;
                    target = new Vector3(w.x, groundAnkle + (0.5f * hopH + 0.14f * Mathf.Max(0, Mathf.Sin(hopPh)) * mv) * scaleY, w.z);   // in the air the foot is pulled up under the body F.locked = false; F.rel = 0;
                }
                target.y = Mathf.Max(target.y, groundAnkle);
                float l1 = Vector3.Distance(kp, hp), l2 = Vector3.Distance(ap, kp), over = Vector3.Distance(target, hp) - (l1 + l2) * 0.99f;
                if (F.rel > 0.5f && over > 0) drop = Mathf.Max(drop, over);
                legs[si] = new LegInfo { hip = hip, knee = knee, foot = foot, toe = toe, target = target, l1 = l1, l2 = l2 };
            }
            if (drop > 0) { var lp = H.root.localPosition; lp.y -= Mathf.Min(drop, 0.05f) / scaleY; H.root.localPosition = lp; }
            for (int si = 0; si < 2; si++)
            {
                var L = legs[si]; if (L.hip == null) continue;
                Quaternion footQ = L.foot.rotation;
                Vector3 hp = L.hip.position, kp = L.knee.position, ap = L.foot.position;
                if ((ap - L.target).sqrMagnitude < 1e-7f) continue;
                Vector3 toT = L.target - hp; float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(L.l1 - L.l2) + 0.01f, (L.l1 + L.l2) * 0.995f); toT.Normalize();
                Vector3 kd = kp - hp;
                Vector3 perpK = kd - toT * Vector3.Dot(kd, toT);
                Vector3 tdir = L.toe.position - ap; Vector3 perpT = tdir - toT * Vector3.Dot(tdir, toT);
                if (perpT.sqrMagnitude > 1e-8f) perpT = perpT.normalized * (Mathf.Max(perpK.magnitude, 0.02f) * 0.6f);
                Vector3 perp = perpK + perpT;
                // fighting stance: the knees open outwards over the feet (the thighs turn out), not only the shins
                float sgK = si == 0 ? -1f : 1f;
                if (stanceFeet > 0.01f)
                {
                    Vector3 o = ch.go.transform.rotation * new Vector3(sgK, 0, 0.25f); o -= toT * Vector3.Dot(o, toT);
                    if (o.sqrMagnitude > 1e-8f) perp += o.normalized * (Mathf.Max(perp.magnitude, 0.03f) * 1.6f * stanceFeet);
                }
                if (perp.sqrMagnitude < 1e-10f) continue; perp.Normalize();
                float a = Mathf.Acos(Mathf.Clamp((L.l1 * L.l1 + d * d - L.l2 * L.l2) / (2 * L.l1 * d), -1, 1));
                Vector3 newK = hp + toT * (Mathf.Cos(a) * L.l1) + perp * (Mathf.Sin(a) * L.l1);
                RotateWorld(L.hip, Quaternion.FromToRotation(kd.normalized, (newK - hp).normalized));
                Vector3 kp2 = L.knee.position, ap2 = L.foot.position;
                RotateWorld(L.knee, Quaternion.FromToRotation((ap2 - kp2).normalized, (L.target - kp2).normalized));
                L.foot.rotation = stanceFeet > 0.01f ? Quaternion.AngleAxis(sgK * 0.3f * stanceFeet * Mathf.Rad2Deg, Vector3.up) * footQ : footQ;     // toes turned out
            }
        }
    }
}
