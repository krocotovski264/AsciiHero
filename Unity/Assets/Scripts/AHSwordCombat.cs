using UnityEngine;

namespace AsciiHero
{
    public partial class MotionActor
    {
        public enum SwordSide { Left = -1, High = 0, Right = 1 }
        enum SwordResult { Miss, Hit, Blocked }

        struct SwordPose
        {
            public Vector3 hand, blade;
            public float twist, drop;
            public SwordPose(Vector3 hand, Vector3 blade, float twist, float drop)
            {
                this.hand = hand;
                this.blade = blade.normalized;
                this.twist = twist;
                this.drop = drop;
            }

            public static SwordPose Blend(SwordPose a, SwordPose b, float t) => new SwordPose(
                Vector3.Lerp(a.hand, b.hand, t), Vector3.Slerp(a.blade, b.blade, t),
                Mathf.Lerp(a.twist, b.twist, t), Mathf.Lerp(a.drop, b.drop, t));
        }

        struct SwordStrike
        {
            public SwordSide exit;
            public bool heavy;
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
                return new SwordPose(V(.2f,.27f,.31f), V(-.86f,.29f,.4f), -.08f, .045f);
            float sign = (int)side;
            return new SwordPose(V(sign*.24f,-.17f,.39f), V(sign*.34f,.74f,.57f), sign*.13f, .035f);
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
                    swordPose = SwordPose.Blend(strike.start, strike.wind, SmoothSword(swordTime/strike.windup));
                else if (swordTime < strike.contactEnd)
                {
                    float swing = (swordTime-strike.windup)/(strike.contactEnd-strike.windup);
                    swordPose = swing < .48f ? SwordPose.Blend(strike.wind, strike.contact, SmoothSword(swing/.48f))
                        : SwordPose.Blend(strike.contact, strike.finish, SmoothSword((swing-.48f)/.52f));
                }
                else
                {
                    SwordPose recoveryStart = swordResult == SwordResult.Blocked ? deflectedPose : strike.finish;
                    float progress = Mathf.Clamp01((swordTime-strike.contactEnd)/SwordRecovery);
                    swordPose = SwordPose.Blend(recoveryStart, SwordGuard(strike.exit), SmoothSword(progress));
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
                if (swordQueued && blockTime <= .11f) BeginSwordStrike(queuedHeavy, swordPose);
            }
            else if (guardHeld || combatT > 0)
                swordPose = SwordPose.Blend(swordPoseWeight < .01f ? SwordGuard(swordSide) : swordPose,
                    SwordGuard(swordSide), 1-Mathf.Exp(-dt*16));

            bool ready = cut == null && limbs[2] && leapT < 0 && !IdleBusy;
            float weight = ready && (swordActive || blockTime > 0 || guardHeld || combatT > 0) ? 1 : 0;
            swordPoseWeight = Mathf.MoveTowards(swordPoseWeight, weight, dt*9);
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
            H.root.localPosition = root;
            TwistRight(swordPose.twist*weight);
            var shoulder = H.Bone("upperarm01.R").position;
            ArmIK("R", shoulder + facing*(swordPose.hand*ch.model.lossyScale.y), weight);
            BladeW(facing*swordPose.blade, weight);
            if (swordContact)
            {
                trail.Add(ch.weapon.TransformPoint(0,.83f,0));
                trail.Add(ch.weapon.TransformPoint(0,.46f,0));
            }
        }
    }
}
