// Motion Matching locomotion (after Clavet 2015 / Daniel Holden's open-source Motion-Matching, MIT),
// running on CMU Graphics Lab mocap retargeted to the MakeHuman rig.
//  * critically damped spring controller predicts the desired trajectory (0.33 / 0.66 / 1.0 s)
//  * every 0.1 s (or on sharp input changes) the database is searched for the frame whose feet,
//    hip velocity and future trajectory best continue the current motion
//  * transitions use inertialization (pose offsets decaying with a 0.1 s half-life)
//  * root motion from the animation, synchronised with the controller ("simulation bone")
//  * two-bone foot IK with contact locking, knee pole from the toe direction
// Unity conventions: heading h -> forward (sin h, cos h), right (cos h, -sin h); positive yaw = turning right.
using System;
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
        /// when set, the Animator Controller (Tools -> Patch Project Locomotion) plays the clips and this class
        /// only runs the controller, root motion sync, weapon masks and foot IK
        public AnimatorDriver driver;
        public float maxSpeed = 3.0f;
        struct Sim { public float x, z, vx, vz, ax, az, h, w; public bool init; }
        Sim S; bool hadDir; Vector2 lastDir;
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
                if (lockPos.HasValue) spd = Mathf.Min(spd, 1.7f);
            }
            float vgx = dir.HasValue ? dir.Value.x * spd : 0, vgz = dir.HasValue ? dir.Value.y * spd : 0;
            float hGoal = S.h;
            if (lockPos.HasValue) hGoal = Mathf.Atan2(lockPos.Value.x - S.x, lockPos.Value.y - S.z);
            else if (dir.HasValue) hGoal = Mathf.Atan2(dir.Value.x, dir.Value.y);
            const float HV = 0.13f, HR = 0.085f;   // snappy: velocity and facing reach the stick in ~0.25 s
            SpringChar(ref S.x, ref S.vx, ref S.ax, vgx, HV, dt);
            SpringChar(ref S.z, ref S.vz, ref S.az, vgz, HV, dt);
            SpringAng(ref S.h, ref S.w, hGoal, HR, dt);
            if (collide != null) { var p = collide(new Vector2(S.x, S.z)); S.x = p.x; S.z = p.y; }
            float dxs = S.x - myPos.x, dzs = S.z - myPos.y, dl = Mathf.Sqrt(dxs * dxs + dzs * dzs);
            if (dl > 0.25f) { S.x = myPos.x + dxs / dl * 0.25f; S.z = myPos.y + dzs / dl * 0.25f; }

            if (driver != null) return AnimatorLocomotion(dt, dir, mag, spd, vgx, vgz, hGoal, myPos);

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
                if (best >= 0 && (bc < cur - 0.05f || (force && atEnd))) Transition(best);
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
            label = LabelFor(fwd, lat, yr, dir.HasValue);
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
            label = driver.StateLabel();
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
                driver.ReadPose(out ro, con);
                for (int k = 0; k < nb; k++)
                    if (map[k] >= 0 && mask[k] > 0) B[map[k]].localRotation = Quaternion.Slerp(B[map[k]].localRotation, baseQ[map[k]], mask[k]);
                H.root.localPosition = rootPos + ro;
                RelaxArms(); FootIK(dt);
                Orb(t);
                return;
            }
            SamplePose(frame, outQ, ref ro, con);
            float decay = Mathf.Exp(-dt * LN2 / 0.1f);
            for (int k = 0; k < nb; k++) { off[k] = Quaternion.Slerp(off[k], Quaternion.identity, 1 - decay); outQ[k] = off[k] * outQ[k]; }
            offRO *= decay; ro += offRO;
            for (int k = 0; k < nb; k++)
            {
                if (map[k] < 0) continue;
                var q = outQ[k]; if (mask[k] > 0) q = Quaternion.Slerp(q, baseQ[map[k]], mask[k]);
                B[map[k]].localRotation = q;
            }
            H.root.localPosition = rootPos + ro;
            RelaxArms(); FootIK(dt);
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

        // ---------- arms ----------
        // The mocap actors hold their arms away from the body. Bring a hanging arm back to the torso: the sideways
        // angle of the upper arm is limited softly, the forward/back swing is untouched.
        const float ARM_MAX = 0.2f, ARM_SOFT = 0.2f;
        void RelaxArms()
        {
            Quaternion gq = ch.go.transform.rotation, gi = Quaternion.Inverse(gq);
            for (int si = 0; si < 2; si++)
            {
                string s = SIDES[si];
                if (s == "L" && ch.cfg.weapon == "shield") continue;
                Transform up = H.Bone("upperarm01." + s), el = H.Bone("lowerarm01." + s); if (up == null || el == null) continue;
                Vector3 dW = (el.position - up.position).normalized, d = gi * dW;
                if (d.y > -0.2f) continue;
                float sign = s == "L" ? -1f : 1f;       // Unity: the left side is -X
                float o = d.x * sign, r = Mathf.Sqrt(o * o + d.y * d.y), abd = Mathf.Atan2(o, -d.y);
                if (abd <= ARM_MAX) continue;
                float a2 = ARM_MAX + (abd - ARM_MAX) * ARM_SOFT;
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
            bool moving = !noLock && speed > 0.08f;
            float drop = 0;
            for (int si = 0; si < 2; si++)
            {
                string s = SIDES[si];
                Transform hip = H.Bone("upperleg01." + s), knee = H.Bone("lowerleg01." + s), foot = H.Bone("foot." + s), toe = H.Bone("toe3-1." + s);
                Vector3 hp = hip.position, kp = knee.position, ap = foot.position;
                var F = feet[si]; float cc = con[si];
                if (moving && cc > 0.6f) { if (!F.locked) { F.locked = true; F.pos = new Vector3(ap.x, 0, ap.z); } F.rel = 1; }
                else { F.locked = false; F.rel = Mathf.Max(0, F.rel - dt / 0.1f); }
                float dxl = F.pos.x - ap.x, dzl = F.pos.z - ap.z;
                if (F.rel > 0 && dxl * dxl + dzl * dzl > 0.16f * 0.16f) { F.locked = false; F.rel = 0; }
                Vector3 target = ap;
                if (F.rel > 0) { target.x += dxl * F.rel; target.z += dzl * F.rel; }
                target.y = Mathf.Max(target.y, groundAnkle);
                float l1 = Vector3.Distance(kp, hp), l2 = Vector3.Distance(ap, kp), over = Vector3.Distance(target, hp) - (l1 + l2) * 0.99f;
                if (F.rel > 0.5f && over > 0) drop = Mathf.Max(drop, over);
                legs[si] = new LegInfo { hip = hip, knee = knee, foot = foot, toe = toe, target = target, l1 = l1, l2 = l2 };
            }
            if (drop > 0) { var lp = H.root.localPosition; lp.y -= Mathf.Min(drop, 0.05f) / scaleY; H.root.localPosition = lp; }
            for (int si = 0; si < 2; si++)
            {
                var L = legs[si];
                Quaternion footQ = L.foot.rotation;
                Vector3 hp = L.hip.position, kp = L.knee.position, ap = L.foot.position;
                if ((ap - L.target).sqrMagnitude < 1e-7f) continue;
                Vector3 toT = L.target - hp; float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(L.l1 - L.l2) + 0.01f, (L.l1 + L.l2) * 0.995f); toT.Normalize();
                Vector3 kd = kp - hp;
                Vector3 perpK = kd - toT * Vector3.Dot(kd, toT);
                Vector3 tdir = L.toe.position - ap; Vector3 perpT = tdir - toT * Vector3.Dot(tdir, toT);
                if (perpT.sqrMagnitude > 1e-8f) perpT = perpT.normalized * (Mathf.Max(perpK.magnitude, 0.02f) * 0.6f);
                Vector3 perp = perpK + perpT; if (perp.sqrMagnitude < 1e-10f) continue; perp.Normalize();
                float a = Mathf.Acos(Mathf.Clamp((L.l1 * L.l1 + d * d - L.l2 * L.l2) / (2 * L.l1 * d), -1, 1));
                Vector3 newK = hp + toT * (Mathf.Cos(a) * L.l1) + perp * (Mathf.Sin(a) * L.l1);
                RotateWorld(L.hip, Quaternion.FromToRotation(kd.normalized, (newK - hp).normalized));
                Vector3 kp2 = L.knee.position, ap2 = L.foot.position;
                RotateWorld(L.knee, Quaternion.FromToRotation((ap2 - kp2).normalized, (L.target - kp2).normalized));
                L.foot.rotation = footQ;
            }
        }
    }
}
