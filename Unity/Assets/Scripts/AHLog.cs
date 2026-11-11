// On-device log: collects every Unity log message (any thread) so the game can show them on screen,
// and mirrors them to <persistentDataPath>/asciihero-log.txt.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AsciiHero
{
    public static class LogHub
    {
        public struct Line { public LogType type; public string text; public float time; }
        public static readonly List<Line> lines = new List<Line>();
        public static int errors, version;
        public static string lastError = "";
        static readonly Queue<Line> pending = new Queue<Line>();
        static readonly object gate = new object();
        static StreamWriter file;
        static bool installed;
        const int MAX = 400;

        public static string FilePath { get { return Path.Combine(Application.persistentDataPath, "asciihero-log.txt"); } }

        public static void Install()
        {
            if (installed) return; installed = true;
            try { file = new StreamWriter(FilePath, false) { AutoFlush = true }; } catch (Exception) { file = null; }
            Application.logMessageReceivedThreaded += OnLog;
            Debug.Log("ASCII Hero " + Application.version + " · Unity " + Application.unityVersion + " · " + SystemInfo.deviceModel);
            Debug.Log("GPU: " + SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsDeviceType + " · " + SystemInfo.graphicsDeviceVersion);
            Debug.Log("Screen " + Screen.width + "×" + Screen.height + " dpi " + Screen.dpi + " · RAM " + SystemInfo.systemMemorySize + " MB · ARGBHalf RT " +
                      SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) + " · colour space " + QualitySettings.activeColorSpace);
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            string text = msg;
            if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert) && !string.IsNullOrEmpty(stack))
                text += "\n" + Trim(stack, 6);
            var l = new Line { type = type, text = text };
            lock (gate) pending.Enqueue(l);
            try { if (file != null) lock (gate) file.WriteLine("[" + type + "] " + text); } catch (Exception) { }
        }
        static string Trim(string stack, int maxLines)
        {
            var parts = stack.Split('\n'); var n = Math.Min(maxLines, parts.Length);
            return "  " + string.Join("\n  ", parts, 0, n).TrimEnd();
        }

        /// move messages from other threads into the visible list (call once per frame on the main thread)
        public static void Pump()
        {
            lock (gate)
            {
                while (pending.Count > 0)
                {
                    var l = pending.Dequeue(); l.time = Time.realtimeSinceStartup;
                    lines.Add(l); version++;
                    if (l.type == LogType.Exception || l.type == LogType.Error || l.type == LogType.Assert) { errors++; lastError = l.text; }
                }
            }
            if (lines.Count > MAX) lines.RemoveRange(0, lines.Count - MAX);
        }

        public static string AllText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in lines) sb.Append('[').Append(l.type).Append("] ").Append(l.text).Append('\n');
            return sb.ToString();
        }
    }
}
