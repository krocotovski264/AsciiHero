// Feeds the locomotion Animator Controller (built by Tools -> Patch Project Locomotion) from the character controller.
// Parameters are damped (SetFloat with dampTime) and addressed by cached hashes.
using UnityEngine;

namespace AsciiHero
{
    [RequireComponent(typeof(Animator))]
    public class AnimatorDriver : MonoBehaviour
    {
        // curves baked into the clips that drive animator parameters (Unity convention)
        public static readonly string[] CURVE_PARAMS = { "RootX", "RootY", "RootZ", "ContactL", "ContactR", "RootFwd", "RootRight", "RootYawRate" };

        static readonly int H_SPEED = Animator.StringToHash("Speed"), H_LEAN = Animator.StringToHash("LeanAngle"), H_TURN = Animator.StringToHash("TurnAngle"),
            H_INPUT = Animator.StringToHash("InputMagnitude"), H_MOVING = Animator.StringToHash("IsMoving"), H_STOPPING = Animator.StringToHash("IsStopping"),
            H_PIVOTING = Animator.StringToHash("IsPivoting"), H_GROUNDED = Animator.StringToHash("IsGrounded"),
            H_TPIVOT = Animator.StringToHash("TriggerPivot"), H_TSTOP = Animator.StringToHash("TriggerStop"), H_TIDLE = Animator.StringToHash("TriggerIdleBreak"),
            H_RX = Animator.StringToHash("RootX"), H_RY = Animator.StringToHash("RootY"), H_RZ = Animator.StringToHash("RootZ"),
            H_CL = Animator.StringToHash("ContactL"), H_CR = Animator.StringToHash("ContactR"),
            H_RF = Animator.StringToHash("RootFwd"), H_RR = Animator.StringToHash("RootRight"), H_RYAW = Animator.StringToHash("RootYawRate"),
            TAG_PIVOT = Animator.StringToHash("Pivot"), TAG_TURN = Animator.StringToHash("Turn"), TAG_STOP = Animator.StringToHash("Stop"),
            TAG_START = Animator.StringToHash("Start"), TAG_IDLE = Animator.StringToHash("Idle");

        [Header("Damping (s)")] public float speedDamp = 0.12f, leanDamp = 0.18f, turnDamp = 0.05f, inputDamp = 0.08f;
        [Header("Clip speeds (m/s), read from Resources/LocomotionSpeeds.txt")] public float walkSpeed = 1.03f, jogSpeed = 2.8f, sprintSpeed = 3.37f;
        [Header("Lean: degrees of LeanAngle per degree/s of turning")] public float leanPerYawRate = 0.6f;
        public float idleBreakAfter = 10f;

        public Animator anim { get; private set; }
        float pivotCooldown, idleT, stopTriggerT; bool wasMoving;

        public static AnimatorDriver Attach(GameObject go, RuntimeAnimatorController ctrl)
        {
            var a = go.GetComponent<Animator>(); if (a == null) a = go.AddComponent<Animator>();
            a.runtimeAnimatorController = ctrl; a.applyRootMotion = false; a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            a.updateMode = AnimatorUpdateMode.Normal;
            var d = go.GetComponent<AnimatorDriver>(); if (d == null) d = go.AddComponent<AnimatorDriver>();
            d.anim = a; d.LoadSpeeds(); a.Rebind();
            return d;
        }
        void Awake() { anim = GetComponent<Animator>(); LoadSpeeds(); }

        void LoadSpeeds()
        {
            var ta = Resources.Load<TextAsset>("LocomotionSpeeds");
            if (ta == null) return;
            var p = ta.text.Trim().Split(' ');
            float w, j, s; var ci = System.Globalization.CultureInfo.InvariantCulture; var st = System.Globalization.NumberStyles.Float;
            if (p.Length >= 3 && float.TryParse(p[0], st, ci, out w) && float.TryParse(p[1], st, ci, out j) && float.TryParse(p[2], st, ci, out s))
            { walkSpeed = w; jogSpeed = j; sprintSpeed = s; }
        }

        /// real ground speed (m/s) -> blend-space Speed (0 idle, 1.5 walk, 3.5 jog, 6 sprint)
        public float SpeedParam(float v)
        {
            if (v <= walkSpeed) return 1.5f * v / Mathf.Max(0.01f, walkSpeed);
            if (v <= jogSpeed) return 1.5f + 2.0f * (v - walkSpeed) / Mathf.Max(0.01f, jogSpeed - walkSpeed);
            return Mathf.Min(6.5f, 3.5f + 2.5f * (v - jogSpeed) / Mathf.Max(0.01f, sprintSpeed - jogSpeed));
        }

        /// velocity / desired velocity are world XZ; yawRate in rad/s (positive = turning right); turnAngle in degrees (positive = right)
        public void Drive(Vector2 velocity, Vector2 desired, float yawRate, float turnAngle, float inputMag, bool moving, float dt)
        {
            if (anim == null || anim.runtimeAnimatorController == null) return;
            float v = velocity.magnitude, sp = SpeedParam(v);
            anim.SetFloat(H_SPEED, sp, speedDamp, dt);
            anim.SetFloat(H_LEAN, Mathf.Clamp(yawRate * Mathf.Rad2Deg * leanPerYawRate, -45f, 45f), leanDamp, dt);
            anim.SetFloat(H_TURN, Mathf.Clamp(turnAngle, -180f, 180f), turnDamp, dt);
            anim.SetFloat(H_INPUT, Mathf.Clamp01(inputMag), inputDamp, dt);
            anim.SetBool(H_MOVING, moving);
            anim.SetBool(H_GROUNDED, true);
            var info = anim.GetCurrentAnimatorStateInfo(0);
            bool pivoting = info.tagHash == TAG_PIVOT;
            anim.SetBool(H_PIVOTING, pivoting);
            anim.SetBool(H_STOPPING, info.tagHash == TAG_STOP || (!moving && anim.GetFloat(H_SPEED) > 0.6f));
            // sharp reversal of the stick at speed -> pivot
            pivotCooldown -= dt;
            if (moving && v > 0.1f && desired.sqrMagnitude > 0.01f && pivotCooldown <= 0 && !pivoting &&
                Vector2.Dot(velocity / v, desired.normalized) < -0.5f && anim.GetFloat(H_SPEED) > 3.0f)
            { anim.SetTrigger(H_TPIVOT); pivotCooldown = 0.8f; }
            // stick released while moving -> stop
            if (wasMoving && !moving && sp > 0.6f) { anim.SetTrigger(H_TSTOP); stopTriggerT = 0.3f; }
            if (stopTriggerT > 0) { stopTriggerT -= dt; if (stopTriggerT <= 0) anim.ResetTrigger(H_TSTOP); }
            wasMoving = moving;
            // idle breaker
            idleT = (!moving && sp < 0.05f && info.tagHash == Animator.StringToHash("Locomotion")) ? idleT + dt : 0;
            if (idleT > idleBreakAfter) { idleT = 0; idleBreakAfter = 9 + Random.value * 6; anim.SetTrigger(H_TIDLE); }
        }

        /// root motion carried by the clips (fwd, right m/s and yaw rate rad/s, Unity convention)
        public void ReadRootMotion(out float fwd, out float right, out float yawRate)
        {
            fwd = anim.GetFloat(H_RF); right = anim.GetFloat(H_RR); yawRate = anim.GetFloat(H_RYAW);
        }
        /// pelvis offset and foot contacts carried by the clips
        public void ReadPose(out Vector3 rootOffset, float[] contacts)
        {
            rootOffset = new Vector3(anim.GetFloat(H_RX), anim.GetFloat(H_RY), anim.GetFloat(H_RZ));
            contacts[0] = anim.GetFloat(H_CL); contacts[1] = anim.GetFloat(H_CR);
        }

        public string StateLabel()
        {
            if (anim == null) return "";
            var i = anim.GetCurrentAnimatorStateInfo(0); string tag;
            if (i.tagHash == TAG_PIVOT) tag = "run pivot 180";
            else if (i.tagHash == TAG_TURN) tag = "turn in place";
            else if (i.tagHash == TAG_STOP) tag = "stop";
            else if (i.tagHash == TAG_START) tag = "start";
            else if (i.tagHash == TAG_IDLE) tag = "idle breaker";
            else tag = "blend tree";
            return "Animator: " + tag + " · Speed " + anim.GetFloat(H_SPEED).ToString("0.0") + " · Lean " + anim.GetFloat(H_LEAN).ToString("0");
        }
    }
}
