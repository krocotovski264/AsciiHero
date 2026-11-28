using UnityEngine;

namespace AsciiHero
{
    public partial class MotionActor
    {
        public enum SwordSide { Left = -1, High = 0, Right = 1 }
        enum SwordResult { Miss, Hit, Blocked }

        struct SwordPose
        {
            public Vector3 hand, blade, leftOffset, elbowOffset;
            public float twist, drop, weightShift;
            public SwordPose(Vector3 hand, Vector3 blade, float twist, float drop)
            {
                this.hand = hand;
                this.blade = blade.normalized;
                this.twist = twist;
                this.drop = drop;
                leftOffset = elbowOffset = Vector3.zero;
                weightShift = 0;
            }

            public static SwordPose Blend(SwordPose a, SwordPose b, float t) => new SwordPose(
                Vector3.Lerp(a.hand, b.hand, t), Vector3.Slerp(a.blade, b.blade, t),
                Mathf.Lerp(a.twist, b.twist, t), Mathf.Lerp(a.drop, b.drop, t))
                {
                    leftOffset = Vector3.Lerp(a.leftOffset, b.leftOffset, t),
                    elbowOffset = Vector3.Lerp(a.elbowOffset, b.elbowOffset, t),
                    weightShift = Mathf.Lerp(a.weightShift, b.weightShift, t)
                };
        }

        struct SwordStrike
        {
            public SwordSide exit;
            public bool heavy;
            public int motion;
            public float windup, contactEnd;
            public SwordPose start, wind, contact, finish;
        }

        public bool useDirectedSword;
        public bool DirectedSword => useDirectedSword && ch.cfg.weapon == "sword" && ch.cfg.blade != "dagger" && ch.weapon != null;
        public float SwordDamageScale => !DirectedSword ? 1f : swordStrike.heavy ? 1.45f : swordRiposte ? 1.2f : 1f;
        // Named by the defender's view. An attacker-right cut arrives on the defender's left.
        public SwordSide IncomingSwordSide => atkType == 2 || atkType == 11 ? SwordSide.Right
            : atkType == 4 || atkType == 10 ? SwordSide.Left : SwordSide.High;

        SwordSide swordSide = SwordSide.Right, blockSide;
        SwordSide? nextSide;
        SwordStrike swordStrike;
        SwordPose swordPose, blockStart, deflectedPose;
        SwordResult swordResult;
        bool swordActive, swordContact, swordQueued, queuedHeavy, swordRiposte;
        float swordTime, swordPoseWeight, blockTime, riposteTime;

        public void SelectSwordSide(SwordSide side)
        {
            if (swordActive) nextSide = side;
            else swordSide = side;
        }

        public bool RequestSwordAttack(bool heavy)
        {
            if (IdleBusy) { PokeIdle(); return false; }
            if (!limbs[2] || InCut || leapT >= 0) return false;
            if (swordActive || blockTime > .11f)
            {
                swordQueued = true;
                queuedHeavy = heavy;
                return false;
            }
            BeginSwordStrike(heavy, swordPoseWeight > .05f ? swordPose : SwordGuard(swordSide));
            return true;
        }

        public bool TrySwordBlock(SwordSide side, bool directional = true)
        {
            if (!DirectedSword) return true;
            if (swordActive || InCut || leapT >= 0 || !limbs[2] || guard < .4f) return false;
            if (directional && side != swordSide) return false;
            blockStart = swordPose;
            blockSide = swordSide;
            blockTime = .26f;
            riposteTime = .55f;
            return true;
        }

        public void SwordContact(bool blocked)
        {
            if (!DirectedSword || !swordActive) return;
            if (blocked)
            {
                swordResult = SwordResult.Blocked;
                swordContact = false;
                deflectedPose = swordPose;
                deflectedPose.hand += V(.04f,.045f,-.08f);
                swordTime = swordStrike.contactEnd;
            }
            else swordResult = SwordResult.Hit;
        }

        void CancelSwordSequence()
        {
            swordActive = swordQueued = swordContact = false;
            blockTime = riposteTime = 0;
            nextSide = null;
        }

        static SwordPose SwordGuard(SwordSide side)
        {
            if (side == SwordSide.High)
                return new SwordPose(V(.21f,.24f,.28f), V(-.91f,.15f,.38f), -.06f, .035f);
            float sign = (int)side;
            return new SwordPose(V(sign*.22f,-.19f,.40f), V(sign*.34f,.82f,.46f), sign*.11f, .03f);
        }

        void BeginSwordStrike(bool heavy, SwordPose from)
        {
            SwordSide side = swordSide;
            swordRiposte = riposteTime > 0;
            riposteTime = blockTime = 0;
            if (swordRiposte) heavy = false;
            float sign = (int)side;
            bool high = side == SwordSide.High;
            var exit = side == SwordSide.Left ? SwordSide.Right : SwordSide.Left;
            swordStrike = new SwordStrike
            {
                exit = exit,
                heavy = heavy,
                motion = (1+(int)side) + (heavy ? 3 : 0),
                start = from,
                windup = swordRiposte ? .12f : heavy ? .35f : .20f,
                wind = high
                    ? new SwordPose(V(.22f,.29f,.18f), V(.15f,.86f,-.48f), -.18f, .04f)
                    : new SwordPose(V(sign*.30f,heavy ? .11f : -.04f,.25f),
                        V(sign*.80f,.43f,heavy ? -.52f : -.17f), sign*(heavy ? .44f : .26f), .045f),
                contact = high
                    ? new SwordPose(V(.13f,-.04f,.52f), V(-.10f,-.42f,.90f), .12f, .09f)
                    : new SwordPose(V(.03f,-.15f,.54f), V(-sign*.15f,heavy ? -.33f : .04f,.94f), -sign*.20f, heavy ? .09f : .06f),
                finish = high
                    ? new SwordPose(V(-.18f,-.42f,.37f), V(-.40f,-.78f,.48f), .21f, .09f)
                    : new SwordPose(V(-sign*.24f,-.24f,.37f), V(-sign*.86f,heavy ? -.43f : -.14f,.37f), -sign*.36f, .06f)
            };
            swordStrike.contactEnd = swordStrike.windup + (heavy ? .20f : .15f);
            swordQueued = false;
            swordContact = false;
            swordActive = true;
            swordTime = 0;
            swordResult = SwordResult.Miss;
            atkHits.Clear();
            combatT = COMBAT_HOLD;
        }

        float SwordRecovery => swordResult == SwordResult.Blocked ? .42f
            : swordResult == SwordResult.Hit ? (swordStrike.heavy ? .32f : .22f)
            : swordStrike.heavy ? .55f : .39f;

        void AdvanceSword(float dt)
        {
            if (cut != null || !limbs[2]) CancelSwordSequence();
            riposteTime = Mathf.Max(0, riposteTime-dt);
            blockTime = Mathf.Max(0, blockTime-dt);
            swordContact = false;
            if (swordActive)
            {
                var strike = swordStrike;
                float previous = swordTime;
                swordTime += dt;
                if (swordResult != SwordResult.Blocked)
                    swordContact = swordTime >= Mathf.Lerp(strike.windup, strike.contactEnd, .27f)
                        && previous < Mathf.Lerp(strike.windup, strike.contactEnd, .85f);
                if (swordTime < strike.windup)
                {
                    float progress = swordTime/strike.windup;
                    swordPose = SwordPose.Blend(strike.start, strike.wind, SmoothSword(progress));
                    AddSwordMotion(strike.motion, 0, progress, .85f);
                }
                else if (swordTime < strike.contactEnd)
                {
                    float swing = (swordTime-strike.windup)/(strike.contactEnd-strike.windup);
                    swordPose = swing < .48f ? SwordPose.Blend(strike.wind, strike.contact, SmoothSword(swing/.48f))
                        : SwordPose.Blend(strike.contact, strike.finish, SmoothSword((swing-.48f)/.52f));
                    AddSwordMotion(strike.motion, 1, swing, 1);

                }
                else
                {
                    SwordPose recoveryStart = swordResult == SwordResult.Blocked ? deflectedPose : strike.finish;
                    float progress = Mathf.Clamp01((swordTime-strike.contactEnd)/SwordRecovery);
                    swordPose = SwordPose.Blend(recoveryStart, SwordGuard(strike.exit), SmoothSword(progress));
                    if (swordResult != SwordResult.Blocked) AddSwordMotion(strike.motion, 2, progress, .65f);
                    float link = swordResult == SwordResult.Hit && !strike.heavy ? .52f : 1f;
                    if (!swordContact && progress >= link && (swordQueued || atkHeld))
                    {
                        swordSide = nextSide ?? strike.exit;
                        nextSide = null;
                        BeginSwordStrike(swordQueued && queuedHeavy, swordPose);
                    }
                    else if (!swordContact && progress >= 1)
                    {
                        swordActive = false;
                        swordSide = nextSide ?? strike.exit;
                        nextSide = null;
                    }
                }
            }
            else if (blockTime > 0)
            {
                var guardPose = SwordGuard(blockSide);
                float impact = Mathf.Sin(Mathf.PI * (1-blockTime/.26f));
                guardPose.hand += V((int)blockSide*.09f,blockSide == SwordSide.High ? -.12f : -.035f,-.12f)*impact;
                guardPose.drop += .055f*impact;
                if (blockSide == SwordSide.High) guardPose.blade = Vector3.Slerp(guardPose.blade, V(-.93f,-.15f,.25f), impact);
                swordPose = SwordPose.Blend(blockStart, guardPose, SmoothSword((.26f-blockTime)/.065f));
                float phase = Mathf.Clamp01(1-blockTime/.26f)*3;
                int stage = Mathf.Min(2, Mathf.FloorToInt(phase));
                AddSwordMotion(7+(int)blockSide, stage, phase-stage, .75f);
                if (swordQueued && blockTime <= .11f) BeginSwordStrike(queuedHeavy, swordPose);
            }
            else if (guardHeld || combatT > 0)
                swordPose = SwordPose.Blend(swordPoseWeight < .01f ? SwordGuard(swordSide) : swordPose,
                    SwordGuard(swordSide), 1-Mathf.Exp(-dt*16));

            bool ready = cut == null && limbs[2] && leapT < 0 && !IdleBusy;
            float weight = ready && (swordActive || blockTime > 0 || guardHeld || combatT > 0) ? 1 : 0;
            swordPoseWeight = Mathf.MoveTowards(swordPoseWeight, weight, dt*9);
        }

        static float[] swordMotion;
        void AddSwordMotion(int track, int phase, float progress, float weight)
        {
            const int samples = 13, stride = 12;
            if (swordMotion == null)
            {
                var asset = Resources.Load<TextAsset>("Knight/SwordMotion");
                swordMotion = new float[9*3*samples*stride];
                if (asset != null && asset.bytes.Length == swordMotion.Length*sizeof(float))
                    System.Buffer.BlockCopy(asset.bytes, 0, swordMotion, 0, asset.bytes.Length);
            }
            float frame = Mathf.Clamp01(progress)*(samples-1);
            int frame0 = Mathf.Min(samples-2, Mathf.FloorToInt(frame));
            int offset = ((track*3+phase)*samples + frame0)*stride;
            float Read(int channel) => Mathf.Lerp(swordMotion[offset+channel],
                swordMotion[offset+stride+channel], frame-frame0)*weight;
            swordPose.hand += V(Read(0),Read(1),Read(2));
            swordPose.leftOffset = V(Read(3),Read(4),Read(5));
            swordPose.elbowOffset = V(Read(6),Read(7),Read(8));
            swordPose.twist += Read(9);
            swordPose.drop = Mathf.Max(0, swordPose.drop-Read(10));
            swordPose.weightShift = Read(11);
        }

        static float SmoothSword(float t) { t = Mathf.Clamp01(t); return t*t*(3-2*t); }

        bool swordFeetPlanted;
        void PlantSwordFeet(float dt)
        {
            bool plantBoth = Legs == 2 && vel < .12f && wWalk < .15f
                && swordPoseWeight > .5f && leapT < 0 && cut == null;
            if (plantBoth && !swordFeetPlanted)
                for (int side = 0; side < SIDES.Length; side++)
                    plant[side] = H.Bone("foot." + SIDES[side]).position;
            if (plantBoth)
                for (int side = 0; side < SIDES.Length; side++)
                    if ((H.Bone("foot." + SIDES[side]).position - plant[side]).sqrMagnitude > .12f)
                        plantBoth = false;
            swordFeetPlanted = plantBoth;
            plantW = Mathf.MoveTowards(plantW, plantBoth ? 1 : 0, dt*10);
        }

        void ApplySwordPose(Quaternion facing)
        {
            if (swordPoseWeight < .001f || cut != null || leapT >= 0 || !limbs[2]) return;
            float weight = swordPoseWeight;
            var root = H.root.localPosition;
            root.y -= swordPose.drop * weight * (1-wWalk*.75f);
            root.x += swordPose.weightShift * weight * (1-wWalk);
            H.root.localPosition = root;
            TwistRight(swordPose.twist*weight);
            var shoulder = H.Bone("upperarm01.R").position;
            float scale = ch.model.lossyScale.y;
            Vector3 elbow = facing * (V(.24f,-.5f,.05f) + 2*swordPose.elbowOffset);
            ArmIK("R", shoulder + facing*(swordPose.hand*scale), weight, elbow);
            if (limbs[1])
            {
                var leftShoulder = H.Bone("upperarm01.L").position;
                var leftHand = V(-.16f,-.34f,.19f) + swordPose.leftOffset;
                ArmIK("L", leftShoulder + facing*(leftHand*scale), .8f*weight, facing*V(-.25f,-.65f,.01f));
            }
            BladeW(facing*swordPose.blade, weight);
            if (swordContact)
            {
                trail.Add(ch.weapon.TransformPoint(0,.83f,0));
                trail.Add(ch.weapon.TransformPoint(0,.46f,0));
            }
        }
    }
}
