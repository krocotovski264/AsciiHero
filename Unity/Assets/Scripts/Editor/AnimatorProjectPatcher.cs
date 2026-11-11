// Tools -> Patch Project Locomotion
//  1. bakes the project's mocap database (Resources/mm.bytes + Editor/LocomotionClips.json) into .anim clips
//  2. scans every AnimationClip in the project and maps clips to locomotion roles by name
//  3. builds / patches the Animator Controller (2D freeform blend tree, turns in place, starts & stops, run pivot)
//  4. writes the clip speeds used by the runtime driver (Resources/LocomotionSpeeds.txt)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AsciiHero;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AsciiHero.Editor
{
    [InitializeOnLoad]
    public static class AnimatorProjectPatcher
    {
        public const string CONTROLLER_PATH = "Assets/Resources/Locomotion.controller";
        public const string BAKE_FOLDER = "Assets/Art/Animations/Mocap";
        public const string SPEEDS_PATH = "Assets/Resources/LocomotionSpeeds.txt";
        const string INDEX_NAME = "LocomotionClips";

        // ---------- roles ----------
        public static readonly string[] ROLES = {
            "Idle", "IdleBreaker", "Walk", "Jog", "Sprint",
            "WalkLeanL", "WalkLeanR", "JogLeanL", "JogLeanR", "SprintLeanL", "SprintLeanR",
            "Turn90L", "Turn90R", "Turn180L", "Turn180R",
            "WalkStart", "RunStart", "WalkStop", "JogStop", "SprintStop",
            "RunPivot180L", "RunPivot180R" };

        // when a role has no clip, the nearest available clip fills the slot (and is reported)
        static readonly Dictionary<string, string[]> FALLBACK = new Dictionary<string, string[]> {
            { "IdleBreaker", new[] { "Idle" } },
            { "Jog", new[] { "Walk", "Sprint" } }, { "Sprint", new[] { "Jog", "Walk" } }, { "Walk", new[] { "Jog" } },
            { "WalkLeanL", new[] { "Walk" } }, { "WalkLeanR", new[] { "Walk" } },
            { "JogLeanL", new[] { "WalkLeanL", "Jog" } }, { "JogLeanR", new[] { "WalkLeanR", "Jog" } },
            { "SprintLeanL", new[] { "JogLeanL", "Sprint" } }, { "SprintLeanR", new[] { "JogLeanR", "Sprint" } },
            { "Turn90L", new[] { "Idle" } }, { "Turn90R", new[] { "Idle" } },
            { "Turn180L", new[] { "Turn90L", "Idle" } }, { "Turn180R", new[] { "Turn90R", "Idle" } },
            { "WalkStart", new[] { "Walk" } }, { "RunStart", new[] { "WalkStart", "Jog" } },
            { "WalkStop", new[] { "Idle" } }, { "JogStop", new[] { "WalkStop", "Idle" } }, { "SprintStop", new[] { "JogStop", "WalkStop", "Idle" } },
            { "RunPivot180L", new[] { "RunPivot180R", "Jog" } }, { "RunPivot180R", new[] { "RunPivot180L", "Jog" } },
        };

        static AnimatorProjectPatcher()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH) == null && FindIndex() != null)
                    Patch(false);
            };
        }

        [MenuItem("Tools/Patch Project Locomotion")]
        public static void PatchMenu() { Patch(true); }

        [MenuItem("Tools/Patch Project Locomotion (only remap clips, no bake)")]
        public static void PatchNoBake() { Patch(true, false); }

        public static void Patch(bool interactive, bool bake = true)
        {
            var report = new StringBuilder();
            try
            {
                if (bake) BakeMocap(report);
                var map = MapClips(report);
                var ctrl = TargetController(report);
                BuildController(ctrl, map, report);
                WriteSpeeds(map, report);
                EditorUtility.SetDirty(ctrl);
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
                Debug.Log("[Locomotion] patch complete\n" + report);
                if (interactive) EditorUtility.DisplayDialog("Patch Project Locomotion", Trim(report.ToString(), 1800), "OK");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (interactive) EditorUtility.DisplayDialog("Patch Project Locomotion", "Ошибка: " + e.Message, "OK");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }
        static string Trim(string s, int n) { return s.Length <= n ? s : s.Substring(0, n) + "\n… (полный отчёт в Console)"; }

        // ---------- 1. bake the mocap database ----------
        [Serializable] class ClipDef { public string name; public int start, end; public bool loop, mirror; public float speed, yaw; }
        [Serializable] class ClipIndex { public int fps, frames; public ClipDef[] clips; }

        static TextAsset FindIndex()
        {
            foreach (var g in AssetDatabase.FindAssets(INDEX_NAME + " t:TextAsset"))
            {
                var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(g));
                if (ta != null && ta.name == INDEX_NAME) return ta;
            }
            return null;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/'), leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static string BonePath(int i)
        {
            var parts = new List<string>();
            for (int b = i; b >= 0; b = HumanDB.parents[b]) parts.Add(HumanDB.bones[b]);
            parts.Reverse();
            return "Model/" + string.Join("/", parts.ToArray());
        }

        static AnimationCurve Linear(List<float> t, List<float> v)
        {
            var keys = new Keyframe[t.Count];
            for (int i = 0; i < t.Count; i++)
            {
                float sl = 0;
                if (t.Count > 1)
                {
                    int a = Mathf.Max(0, i - 1), b = Mathf.Min(t.Count - 1, i + 1);
                    sl = (v[b] - v[a]) / Mathf.Max(1e-5f, t[b] - t[a]);
                }
                keys[i] = new Keyframe(t[i], v[i], sl, sl);
            }
            return new AnimationCurve(keys);
        }

        static void BakeMocap(StringBuilder report)
        {
            var ta = FindIndex();
            if (ta == null) { report.AppendLine("• База mocap не найдена (" + INDEX_NAME + ".json) — запекание пропущено."); return; }
            var idx = JsonUtility.FromJson<ClipIndex>(ta.text);
            HumanDB.Load(); MotionDB.Load();
            if (idx.frames != MotionDB.n) throw new Exception("LocomotionClips.json не соответствует mm.bytes (" + idx.frames + " ≠ " + MotionDB.n + ")");
            EnsureFolder(BAKE_FOLDER);
            int nb = MotionDB.nb; var paths = new string[nb];
            for (int k = 0; k < nb; k++) { int bi; paths[k] = HumanDB.BI.TryGetValue(MotionDB.bones[k], out bi) ? BonePath(bi) : null; }
            float fps = idx.fps > 0 ? idx.fps : MotionDB.fps;
            for (int c = 0; c < idx.clips.Length; c++)
            {
                var d = idx.clips[c];
                EditorUtility.DisplayProgressBar("Patch Project Locomotion", "Запекание " + d.name, c / (float)idx.clips.Length);
                int off = d.mirror ? MotionDB.n : 0;
                var clip = new AnimationClip { name = d.name, frameRate = fps, legacy = false };
                var times = new List<float>();
                for (int f = d.start; f <= d.end; f++) times.Add((f - d.start) / fps);
                for (int k = 0; k < nb; k++)
                {
                    if (paths[k] == null) continue;
                    var cx = new List<float>(); var cy = new List<float>(); var cz = new List<float>(); var cw = new List<float>();
                    Quaternion prev = Quaternion.identity; bool first = true;
                    for (int f = d.start; f <= d.end; f++)
                    {
                        var q = MotionDB.Quat(f + off, k);
                        if (!first && Quaternion.Dot(prev, q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        prev = q; first = false;
                        cx.Add(q.x); cy.Add(q.y); cz.Add(q.z); cw.Add(q.w);
                    }
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(paths[k], typeof(Transform), "m_LocalRotation.x"), Linear(times, cx));
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(paths[k], typeof(Transform), "m_LocalRotation.y"), Linear(times, cy));
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(paths[k], typeof(Transform), "m_LocalRotation.z"), Linear(times, cz));
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(paths[k], typeof(Transform), "m_LocalRotation.w"), Linear(times, cw));
                }
                // animator-parameter curves: pelvis offset, foot contacts, root motion (Unity convention)
                var ch = new Dictionary<string, List<float>>();
                foreach (var nm in AnimatorDriver.CURVE_PARAMS) ch[nm] = new List<float>();
                for (int f = d.start; f <= d.end; f++)
                {
                    int g = f + off;
                    ch["RootX"].Add(MotionDB.RO[g * 3]); ch["RootY"].Add(MotionDB.RO[g * 3 + 1]); ch["RootZ"].Add(MotionDB.RO[g * 3 + 2]);
                    ch["ContactL"].Add(MotionDB.CON[g * 2]); ch["ContactR"].Add(MotionDB.CON[g * 2 + 1]);
                    ch["RootFwd"].Add(MotionDB.RM[g * 3]); ch["RootRight"].Add(MotionDB.RM[g * 3 + 1]); ch["RootYawRate"].Add(MotionDB.RM[g * 3 + 2]);
                }
                foreach (var kv in ch) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key), Linear(times, kv.Value));
                clip.EnsureQuaternionContinuity();
                var st = AnimationUtility.GetAnimationClipSettings(clip);
                st.loopTime = d.loop; st.loopBlend = false; st.keepOriginalOrientation = true; st.keepOriginalPositionY = true; st.keepOriginalPositionXZ = true;
                AnimationUtility.SetAnimationClipSettings(clip, st);
                string path = BAKE_FOLDER + "/" + d.name + ".anim";
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (existing != null) { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); UnityEngine.Object.DestroyImmediate(clip); }
                else AssetDatabase.CreateAsset(clip, path);
            }
            AssetDatabase.SaveAssets();
            report.AppendLine("• Запечено клипов из mocap (CMU): " + idx.clips.Length + " → " + BAKE_FOLDER);
        }

        // ---------- 2. map project clips to roles ----------
        public class Pick { public AnimationClip clip; public string source; public int score; public bool fallback; public float speed; }

        static string Compact(string s) { return Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", ""); }

        static int Score(string role, string rawName)
        {
            string n = Compact(rawName), r = Compact(role);
            if (n == r) return 100;
            bool left = Regex.IsMatch(n, "left") || Regex.IsMatch(n, "(lean|arc|turn|pivot|90|180|strafe)l($|[^a-z])") || Regex.IsMatch(n, "l(90|180)");
            bool right = Regex.IsMatch(n, "right") || Regex.IsMatch(n, "(lean|arc|turn|pivot|90|180|strafe)r($|[^a-z])") || Regex.IsMatch(n, "r(90|180)");
            bool lean = n.Contains("lean") || n.Contains("arc") || n.Contains("curve");
            bool start = n.Contains("start") || n.Contains("begin"), stop = n.Contains("stop") || n.Contains("halt");
            bool turn = n.Contains("turn"), pivot = n.Contains("pivot"), strafe = n.Contains("strafe") || n.Contains("back");
            bool walk = n.Contains("walk"), sprint = n.Contains("sprint") || n.Contains("fastrun") || n.Contains("runfast");
            bool jog = n.Contains("jog") || (n.Contains("run") && !sprint);
            bool idle = n.Contains("idle");
            bool breaker = n.Contains("break") || n.Contains("fidget") || n.Contains("bored") || n.Contains("variation") || n.Contains("lookaround");
            int pen = Mathf.Min(20, n.Length / 4);
            switch (role)
            {
                case "Idle": return idle && !breaker && !turn && !walk && !jog && !sprint && !start && !stop ? 60 - pen : 0;
                case "IdleBreaker": return (idle && breaker) || n.Contains("fidget") ? 60 - pen : 0;
                case "Walk": return walk && !lean && !turn && !start && !stop && !strafe && !pivot && !left && !right ? 60 - pen : 0;
                case "Jog": return jog && !walk && !lean && !turn && !start && !stop && !strafe && !pivot && !left && !right ? 60 - pen : 0;
                case "Sprint": return sprint && !lean && !turn && !start && !stop && !pivot && !left && !right ? 60 - pen : 0;
            }
            if (role.EndsWith("L") || role.EndsWith("R"))
            {
                bool wantL = role.EndsWith("L");
                if (wantL ? !left || right : !right || left) return 0;
                string b = role.Substring(0, role.Length - 1);
                switch (b)
                {
                    case "WalkLean": return walk && lean ? 55 - pen : 0;
                    case "JogLean": return jog && lean ? 55 - pen : 0;
                    case "SprintLean": return sprint && lean ? 55 - pen : 0;
                    case "Turn90": return turn && n.Contains("90") && !walk && !jog ? 55 - pen : 0;
                    case "Turn180": return turn && n.Contains("180") && !pivot && !walk && !jog ? 55 - pen : 0;
                    case "RunPivot180": return pivot && (jog || sprint || n.Contains("180")) ? 55 - pen : 0;
                }
                return 0;
            }
            switch (role)
            {
                case "WalkStart": return walk && start ? 55 - pen : 0;
                case "RunStart": return (jog || sprint) && start ? 55 - pen : 0;
                case "WalkStop": return walk && stop ? 55 - pen : 0;
                case "JogStop": return jog && stop ? 55 - pen : 0;
                case "SprintStop": return sprint && stop ? 55 - pen : 0;
            }
            return 0;
        }

        static float ClipSpeed(AnimationClip c)
        {
            var b = EditorCurveBinding.FloatCurve("", typeof(Animator), "RootFwd");
            var cv = AnimationUtility.GetEditorCurve(c, b);
            if (cv != null && cv.length > 0) { float s = 0; foreach (var k in cv.keys) s += k.value; return s / cv.length; }
            if (c.hasGenericRootTransform || c.isHumanMotion) return c.averageSpeed.magnitude;
            return 0;
        }

        public static Dictionary<string, Pick> MapClips(StringBuilder report)
        {
            var all = new List<AnimationClip>();
            foreach (var g in AssetDatabase.FindAssets("t:AnimationClip"))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
                {
                    var c = o as AnimationClip;
                    if (c == null || c.name.StartsWith("__preview__") || all.Contains(c)) continue;
                    all.Add(c);
                }
            }
            var map = new Dictionary<string, Pick>();
            foreach (var role in ROLES)
            {
                Pick best = null;
                foreach (var c in all)
                {
                    int s = Score(role, c.name); if (s <= 0) continue;
                    string path = AssetDatabase.GetAssetPath(c);
                    if (path.StartsWith(BAKE_FOLDER)) s += 1;     // project mocap wins ties (it matches this rig)
                    if (best == null || s > best.score) best = new Pick { clip = c, source = path, score = s };
                }
                if (best != null) { best.speed = ClipSpeed(best.clip); map[role] = best; }
            }
            // fill empty slots from the fallback chain
            foreach (var role in ROLES)
            {
                if (map.ContainsKey(role)) continue;
                string[] fb; if (!FALLBACK.TryGetValue(role, out fb)) continue;
                foreach (var f in fb)
                {
                    Pick p; if (map.TryGetValue(f, out p) && !p.fallback) { map[role] = new Pick { clip = p.clip, source = p.source, score = 0, fallback = true, speed = p.speed }; break; }
                }
            }
            report.AppendLine("• Найдено клипов в проекте: " + all.Count);
            foreach (var role in ROLES)
            {
                Pick p;
                if (!map.TryGetValue(role, out p)) report.AppendLine("   " + role + ": — (слот пустой)");
                else report.AppendLine("   " + role + ": " + p.clip.name + (p.fallback ? "  [замена]" : "") + (p.speed > 0 ? "  " + p.speed.ToString("0.00") + " м/с" : ""));
            }
            return map;
        }

        // ---------- 3. controller ----------
        static AnimatorController TargetController(StringBuilder report)
        {
            var sel = Selection.activeObject as AnimatorController;
            if (sel != null) { report.AppendLine("• Патчу выбранный контроллер: " + AssetDatabase.GetAssetPath(sel)); return sel; }
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH);
            if (c != null) { report.AppendLine("• Патчу контроллер: " + CONTROLLER_PATH); return c; }
            EnsureFolder(Path.GetDirectoryName(CONTROLLER_PATH).Replace('\\', '/'));
            report.AppendLine("• Создан контроллер: " + CONTROLLER_PATH);
            return AnimatorController.CreateAnimatorControllerAtPath(CONTROLLER_PATH);
        }

        static void Param(AnimatorController c, string name, AnimatorControllerParameterType t, float def = 0, bool defBool = false)
        {
            var ps = c.parameters;
            for (int i = ps.Length - 1; i >= 0; i--) if (ps[i].name == name) c.RemoveParameter(i);
            var p = new AnimatorControllerParameter { name = name, type = t, defaultFloat = def, defaultBool = defBool };
            c.AddParameter(p);
        }

        static void ClearMachine(AnimatorController c, AnimatorStateMachine sm)
        {
            foreach (var cs in sm.states)
            {
                var bt = cs.state.motion as BlendTree;
                if (bt != null && AssetDatabase.GetAssetPath(bt) == AssetDatabase.GetAssetPath(c)) RemoveTree(bt);
                sm.RemoveState(cs.state);
            }
            foreach (var csm in sm.stateMachines) { ClearMachine(c, csm.stateMachine); sm.RemoveStateMachine(csm.stateMachine); }
            foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
            foreach (var t in sm.entryTransitions) sm.RemoveEntryTransition(t);
        }
        static void RemoveTree(BlendTree bt)
        {
            foreach (var ch in bt.children) { var sub = ch.motion as BlendTree; if (sub != null) RemoveTree(sub); }
            AssetDatabase.RemoveObjectFromAsset(bt); UnityEngine.Object.DestroyImmediate(bt, true);
        }

        static Motion M(Dictionary<string, Pick> map, string role) { Pick p; return map.TryGetValue(role, out p) ? p.clip : null; }

        static AnimatorStateTransition T(AnimatorState from, AnimatorState to, float duration, bool exitTime, float exitAt = 0.85f,
            TransitionInterruptionSource src = TransitionInterruptionSource.Source)
        {
            var t = from.AddTransition(to);
            t.hasFixedDuration = true; t.duration = duration; t.offset = 0;
            t.hasExitTime = exitTime; t.exitTime = exitAt;
            t.interruptionSource = src; t.orderedInterruption = true; t.canTransitionToSelf = false;
            return t;
        }

        static AnimatorState State(AnimatorStateMachine sm, string name, Motion m, Vector3 pos, string tag = "")
        {
            var s = sm.AddState(name, pos); s.motion = m; s.tag = tag; s.writeDefaultValues = true; s.iKOnFeet = true; return s;
        }

        static void BuildController(AnimatorController c, Dictionary<string, Pick> map, StringBuilder report)
        {
            // parameters
            Param(c, "Speed", AnimatorControllerParameterType.Float);
            Param(c, "LeanAngle", AnimatorControllerParameterType.Float);
            Param(c, "TurnAngle", AnimatorControllerParameterType.Float);
            Param(c, "InputMagnitude", AnimatorControllerParameterType.Float);
            Param(c, "IsMoving", AnimatorControllerParameterType.Bool);
            Param(c, "IsStopping", AnimatorControllerParameterType.Bool);
            Param(c, "IsPivoting", AnimatorControllerParameterType.Bool);
            Param(c, "IsGrounded", AnimatorControllerParameterType.Bool, 0, true);
            Param(c, "TriggerPivot", AnimatorControllerParameterType.Trigger);
            Param(c, "TriggerStop", AnimatorControllerParameterType.Trigger);
            Param(c, "TriggerIdleBreak", AnimatorControllerParameterType.Trigger);
            foreach (var nm in AnimatorDriver.CURVE_PARAMS) Param(c, nm, AnimatorControllerParameterType.Float, nm == "ContactL" || nm == "ContactR" ? 1 : 0);

            // base layer with IK pass
            if (c.layers.Length == 0) c.AddLayer("Base Layer");
            var layers = c.layers; layers[0].iKPass = true; layers[0].defaultWeight = 1; c.layers = layers;
            var sm = c.layers[0].stateMachine;
            ClearMachine(c, sm);
            sm.entryPosition = new Vector3(0, 0, 0); sm.anyStatePosition = new Vector3(0, -80, 0); sm.exitPosition = new Vector3(700, 0, 0);

            // ---- Locomotion: 2D freeform cartesian (X = LeanAngle, Y = Speed) ----
            var tree = new BlendTree
            {
                name = "Locomotion", blendType = BlendTreeType.FreeformCartesian2D,
                blendParameter = "LeanAngle", blendParameterY = "Speed", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(tree, c);
            tree.AddChild(M(map, "Idle"), new Vector2(0, 0));
            tree.AddChild(M(map, "Walk"), new Vector2(0, 1.5f));
            tree.AddChild(M(map, "WalkLeanL"), new Vector2(-45, 1.5f));
            tree.AddChild(M(map, "WalkLeanR"), new Vector2(45, 1.5f));
            tree.AddChild(M(map, "Jog"), new Vector2(0, 3.5f));
            tree.AddChild(M(map, "JogLeanL"), new Vector2(-45, 3.5f));
            tree.AddChild(M(map, "JogLeanR"), new Vector2(45, 3.5f));
            tree.AddChild(M(map, "Sprint"), new Vector2(0, 6f));
            tree.AddChild(M(map, "SprintLeanL"), new Vector2(-45, 6f));
            tree.AddChild(M(map, "SprintLeanR"), new Vector2(45, 6f));
            var loco = sm.AddState("Locomotion", new Vector3(260, 0, 0)); loco.motion = tree; loco.writeDefaultValues = true; loco.iKOnFeet = true; loco.tag = "Locomotion";
            sm.defaultState = loco;

            var idleBreak = State(sm, "Idle Breaker", M(map, "IdleBreaker"), new Vector3(260, -140, 0), "Idle");

            // ---- Turns In Place ----
            var turns = sm.AddStateMachine("Turns In Place", new Vector3(560, -160, 0));
            var t90L = State(turns, "Turn 90 L", M(map, "Turn90L"), new Vector3(250, 0, 0), "Turn");
            var t90R = State(turns, "Turn 90 R", M(map, "Turn90R"), new Vector3(250, 70, 0), "Turn");
            var t180L = State(turns, "Turn 180 L", M(map, "Turn180L"), new Vector3(250, 140, 0), "Turn");
            var t180R = State(turns, "Turn 180 R", M(map, "Turn180R"), new Vector3(250, 210, 0), "Turn");

            // ---- Starts & Stops ----
            var ss = sm.AddStateMachine("Starts & Stops", new Vector3(560, 120, 0));
            var walkStart = State(ss, "Walk Start", M(map, "WalkStart"), new Vector3(250, 0, 0), "Start");
            var runStart = State(ss, "Run Start", M(map, "RunStart"), new Vector3(250, 70, 0), "Start");
            var walkStop = State(ss, "Walk Stop", M(map, "WalkStop"), new Vector3(250, 140, 0), "Stop");
            var jogStop = State(ss, "Jog Stop", M(map, "JogStop"), new Vector3(250, 210, 0), "Stop");
            var sprintStop = State(ss, "Sprint Stop", M(map, "SprintStop"), new Vector3(250, 280, 0), "Stop");

            // ---- Run Pivot 180 (1D on TurnAngle: left / right) ----
            var pivTree = new BlendTree { name = "Run Pivot 180", blendType = BlendTreeType.Simple1D, blendParameter = "TurnAngle", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(pivTree, c);
            pivTree.AddChild(M(map, "RunPivot180L"), -180f);
            pivTree.AddChild(M(map, "RunPivot180R"), 180f);
            var pivot = sm.AddState("Run Pivot 180", new Vector3(260, 160, 0)); pivot.motion = pivTree; pivot.tag = "Pivot"; pivot.writeDefaultValues = true; pivot.iKOnFeet = true;

            // ---- transitions ----
            // pivot first (highest priority), 0.15 s
            var tp = T(loco, pivot, 0.15f, false); tp.AddCondition(AnimatorConditionMode.If, 0, "TriggerPivot"); tp.AddCondition(AnimatorConditionMode.Greater, 3.0f, "Speed");
            var tpb = T(pivot, loco, 0.15f, true, 0.8f);
            var tpStop = T(pivot, loco, 0.15f, false); tpStop.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving"); tpStop.AddCondition(AnimatorConditionMode.Less, 0.5f, "InputMagnitude");

            // turns in place: from idle (Speed < 0.1) when the stick asks for a big direction change
            void TurnIn(AnimatorState dst, float lo, float hi)
            {
                var t = T(loco, dst, 0.2f, false);
                t.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
                t.AddCondition(AnimatorConditionMode.Greater, 0.1f, "InputMagnitude");
                t.AddCondition(AnimatorConditionMode.Greater, lo, "TurnAngle");
                t.AddCondition(AnimatorConditionMode.Less, hi, "TurnAngle");
                var back = T(dst, loco, 0.25f, true, 0.85f);
                var go = T(dst, loco, 0.2f, false); go.AddCondition(AnimatorConditionMode.Greater, 1.0f, "Speed"); go.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
            }
            TurnIn(t180R, 135f, 181f); TurnIn(t90R, 45f, 135f); TurnIn(t90L, -135f, -45f); TurnIn(t180L, -181f, -135f);

            // stops: IsMoving == false with a speed threshold; the new stick input interrupts at once
            void StopIn(AnimatorState dst, float lo, float hi)
            {
                var t = T(loco, dst, 0.15f, false, 0.85f, TransitionInterruptionSource.Destination);
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");
                t.AddCondition(AnimatorConditionMode.Greater, lo, "Speed");
                if (hi < 99) t.AddCondition(AnimatorConditionMode.Less, hi, "Speed");
                var back = T(dst, loco, 0.25f, true, 0.85f, TransitionInterruptionSource.Destination);
                var go = T(dst, loco, 0.2f, false, 0.85f, TransitionInterruptionSource.Destination); go.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
            }
            StopIn(sprintStop, 4.75f, 999f); StopIn(jogStop, 2.5f, 4.75f); StopIn(walkStop, 0.6f, 2.5f);

            // starts: from standing, stick pushed roughly forward (big angles are turns)
            void StartIn(AnimatorState dst, float magLo, float magHi)
            {
                var t = T(loco, dst, 0.2f, false);
                t.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
                t.AddCondition(AnimatorConditionMode.Less, 0.3f, "Speed");
                t.AddCondition(AnimatorConditionMode.Greater, magLo, "InputMagnitude");
                if (magHi < 1.5f) t.AddCondition(AnimatorConditionMode.Less, magHi, "InputMagnitude");
                t.AddCondition(AnimatorConditionMode.Greater, -45f, "TurnAngle");
                t.AddCondition(AnimatorConditionMode.Less, 45f, "TurnAngle");
                var back = T(dst, loco, 0.2f, true, 0.85f);
                var cancel = T(dst, loco, 0.25f, false); cancel.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");
            }
            StartIn(runStart, 0.75f, 2f); StartIn(walkStart, 0.3f, 0.75f);

            // idle breaker: the runtime driver fires TriggerIdleBreak after a long idle
            var ib = T(loco, idleBreak, 0.25f, false); ib.AddCondition(AnimatorConditionMode.Less, 0.05f, "Speed"); ib.AddCondition(AnimatorConditionMode.If, 0, "TriggerIdleBreak");
            ib.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");
            var ibBack = T(idleBreak, loco, 0.25f, true, 0.9f);
            var ibGo = T(idleBreak, loco, 0.2f, false); ibGo.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");

            int empty = 0; foreach (var r in ROLES) if (!map.ContainsKey(r)) empty++;
            report.AppendLine("• Контроллер собран: Locomotion (2D Freeform Cartesian), Turns In Place, Starts & Stops, Run Pivot 180, Idle Breaker; IK Pass включён." + (empty > 0 ? " Пустых слотов: " + empty : ""));
        }

        // ---------- 4. speeds for the runtime driver ----------
        static void WriteSpeeds(Dictionary<string, Pick> map, StringBuilder report)
        {
            float S(string r, float d) { Pick p; return map.TryGetValue(r, out p) && p.speed > 0.05f ? p.speed : d; }
            float walk = S("Walk", 1.4f), jog = Mathf.Max(walk + 0.3f, S("Jog", 2.8f)), sprint = Mathf.Max(jog + 0.3f, S("Sprint", 4.0f));
            var txt = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.000} {1:0.000} {2:0.000}", walk, jog, sprint);
            EnsureFolder(Path.GetDirectoryName(SPEEDS_PATH).Replace('\\', '/'));
            File.WriteAllText(SPEEDS_PATH, txt);
            AssetDatabase.ImportAsset(SPEEDS_PATH);
            report.AppendLine("• Скорости клипов (walk/jog/sprint, м/с): " + txt);
        }
    }
}
