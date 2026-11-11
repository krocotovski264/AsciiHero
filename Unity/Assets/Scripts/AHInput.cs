// Multi-touch / mouse / keyboard abstraction that works with both the legacy Input Manager and the new Input System.
// Pointer positions are in GUI coordinates (origin top-left, pixels).
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace AsciiHero
{
    public class Pointer
    {
        public int id; public Vector2 pos, start, prev; public bool began, ended; public float t0; public string owner;
        public Vector2 Delta { get { return pos - prev; } }
    }

    public static class InputHub
    {
        public static readonly Dictionary<int, Pointer> active = new Dictionary<int, Pointer>();
        public static readonly List<Pointer> ended = new List<Pointer>();
        public static float scroll;
        static int lastFrame = -1;
        static readonly Dictionary<int, Vector2> raw = new Dictionary<int, Vector2>();

        public static void Poll()
        {
            if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
            raw.Clear(); scroll = 0;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var ts = Touchscreen.current;
            if (ts != null)
                foreach (var t in ts.touches)
                {
                    var ph = t.phase.ReadValue();
                    if (ph == UnityEngine.InputSystem.TouchPhase.None || ph == UnityEngine.InputSystem.TouchPhase.Ended || ph == UnityEngine.InputSystem.TouchPhase.Canceled) continue;
                    var p = t.position.ReadValue(); raw[t.touchId.ReadValue()] = new Vector2(p.x, Screen.height - p.y);
                }
            var ms = Mouse.current;
            if (raw.Count == 0 && ms != null)
            {
                if (ms.leftButton.isPressed) { var p = ms.position.ReadValue(); raw[-1] = new Vector2(p.x, Screen.height - p.y); }
                scroll = ms.scroll.ReadValue().y / 120f;
            }
#else
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) continue;
                raw[t.fingerId] = new Vector2(t.position.x, Screen.height - t.position.y);
            }
            if (Input.touchCount == 0)
            {
                if (Input.GetMouseButton(0)) raw[-1] = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                scroll = Input.mouseScrollDelta.y;
            }
#endif
            ended.Clear();
            var gone = new List<int>();
            foreach (var kv in active) if (!raw.ContainsKey(kv.Key)) gone.Add(kv.Key);
            foreach (int id in gone) { var p = active[id]; p.ended = true; p.began = false; p.prev = p.pos; ended.Add(p); active.Remove(id); }
            foreach (var kv in raw)
            {
                Pointer p;
                if (active.TryGetValue(kv.Key, out p)) { p.began = false; p.prev = p.pos; p.pos = kv.Value; }
                else { p = new Pointer { id = kv.Key, pos = kv.Value, start = kv.Value, prev = kv.Value, began = true, t0 = Time.unscaledTime }; active[kv.Key] = p; }
            }
        }

        public static bool Key(string k)
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var kb = Keyboard.current; if (kb == null) return false;
            switch (k)
            {
                case "W": return kb.wKey.isPressed || kb.upArrowKey.isPressed;
                case "S": return kb.sKey.isPressed || kb.downArrowKey.isPressed;
                case "A": return kb.aKey.isPressed || kb.leftArrowKey.isPressed;
                case "D": return kb.dKey.isPressed || kb.rightArrowKey.isPressed;
                case "Back": return kb.escapeKey.wasPressedThisFrame;
            }
            return false;
#else
            switch (k)
            {
                case "W": return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
                case "S": return Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
                case "A": return Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
                case "D": return Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
                case "Back": return Input.GetKeyDown(KeyCode.Escape);
            }
            return false;
#endif
        }
    }
}
