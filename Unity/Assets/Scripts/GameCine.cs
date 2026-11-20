// Cinematics on the game's own engine: the WebGL build opened with ?cine=<name> plays a scripted scene instead of the
// game. The interface is hidden, a free camera follows the script, time runs exactly 1/30 s per rendered frame
// (Time.captureFramerate), and every frame of the ASCII picture is handed to the page (Cine.jslib), which sends it on
// to whoever records it.
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AsciiHero
{
    public partial class Game
    {
        string cineName;                 // the scene asked for (null: the game as usual)
        bool Cine => cineName != null;
        bool cineRec, cineCamOn; int cineFrame; float cineT;
        Vector3 cineEye, cineAt; float cineFov = 40, cineRoll;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CineFrame(byte[] data, int len, int idx);
        [DllImport("__Internal")] static extern void CineLog(string s);
        [DllImport("__Internal")] static extern void CineDone(int n);
#else
        static void CineFrame(byte[] data, int len, int idx) { }
        static void CineLog(string s) { Debug.Log(s); }
        static void CineDone(int n) { }
#endif

        static string CineArg()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string u = Application.absoluteURL; int i = u.IndexOf("cine=");
            if (i < 0) return null;
            string s = u.Substring(i + 5); int a = s.IndexOfAny(new[] { '&', '#' }); if (a >= 0) s = s.Substring(0, a);
            return s.Length > 0 ? s : null;
#else
            return null;
#endif
        }

        /// the scene's camera, laid over whatever the game's own camera did this frame (called at the end of LateUpdate)
        void CineCamera()
        {
            if (!Cine || !cineCamOn || sceneCam == null) return;
            sceneCam.transform.position = cineEye;
            var f = cineAt - cineEye; if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            sceneCam.transform.rotation = Quaternion.LookRotation(f) * Quaternion.Euler(0, 0, cineRoll);
            sceneCam.fieldOfView = cineFov; sceneCam.farClipPlane = 600; sceneCam.nearClipPlane = 0.05f;
        }

        /// each frame's finished picture, to the page
        IEnumerator CineCapture()
        {
            var wait = new WaitForEndOfFrame(); Texture2D tex = null;
            while (true)
            {
                yield return wait;
                if (!cineRec || ascii == null || ascii.final == null) continue;
                var rt = ascii.final;
                if (tex == null || tex.width != rt.width || tex.height != rt.height) tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); tex.Apply(false);
                RenderTexture.active = prev;
                var jpg = tex.EncodeToJPG(93);
                CineFrame(jpg, jpg.Length, cineFrame++);
            }
        }

        IEnumerator CineRun()
        {
            CineLog("cine: " + cineName + " " + Screen.width + "x" + Screen.height);
            Application.runInBackground = true;
            // the story's open land, the hero standing in it
            worldBuilt = true;
            if (tavern == null) SetupWorld();
            if (smap == null) BuildStory();
            inMenu = false; menuPage = 0;
            var L = sMain;
            StoryGo(L, L.SpawnW, 0);
            yield return null; yield return null;
            Time.captureFramerate = 30;
            StartCoroutine(CineCapture());
            switch (cineName)
            {
                default: yield return CineTest(); break;
            }
            Time.captureFramerate = 0;
            CineDone(cineFrame);
            CineLog("cine: done, " + cineFrame + " frames");
        }

        /// a first test: the camera goes round the hero, low, then rises (2 s)
        IEnumerator CineTest()
        {
            cineCamOn = true; cineRec = true;
            for (int i = 0; i < 60; i++)
            {
                float t = i / 30f, a = t * 1.2f;
                var c = pos + Vector3.up * 1.0f;
                cineEye = c + new Vector3(Mathf.Sin(a) * 5f, 0.6f + t * 1.5f, Mathf.Cos(a) * 5f); cineAt = c; cineFov = 45;
                yield return null;
            }
            cineRec = false;
        }
    }
}
