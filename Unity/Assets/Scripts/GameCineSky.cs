// The cinematics' evening sky: characters standing in the world on a dome round the camera (not laid over the screen) -
// the light of the evening from the glow on the horizon up into the dark, and clouds drifting across, lit from below by
// the low sun. Behind everything (the scene's depth hides them behind the trees and the hero).
using UnityEngine;

namespace AsciiHero
{
    public partial class Game
    {
        CineParticles skyP; Vector3[] skyDir; float skyCell; Vector3 skyEye;
        const float SKY_R = 380f;

        /// the characters' places: a patch of the dome round the camera's direction (what it can see), one cell apart
        void CineSkyInit(Vector3 eye, Vector3 look, float fovDeg)
        {
            skyEye = eye;
            float az0 = Mathf.Atan2(look.x, look.z), half = (fovDeg * 0.5f * 9f / 16f + 6f) * Mathf.Deg2Rad;      // (the frame is upright: narrower across)
            float elTop = (fovDeg * 0.5f + 8f) * Mathf.Deg2Rad;
            skyCell = fovDeg * Mathf.Deg2Rad / 1920f * 11f;          // (a character about 11 px of the frame)
            var list = new System.Collections.Generic.List<Vector3>();
            for (float el = -0.03f; el < elTop; el += skyCell)
            {
                float stepA = skyCell / Mathf.Max(0.2f, Mathf.Cos(el));
                for (float a = az0 - half; a < az0 + half; a += stepA)
                {
                    float jx = (Hash(a * 91.7f + el * 13.1f) - 0.5f) * stepA * 0.5f, jy = (Hash(a * 37.3f - el * 71.9f) - 0.5f) * skyCell * 0.5f;
                    float A = a + jx, E = el + jy;
                    list.Add(new Vector3(Mathf.Sin(A) * Mathf.Cos(E), Mathf.Sin(E), Mathf.Cos(A) * Mathf.Cos(E)));
                }
            }
            skyDir = list.ToArray();
            skyP = new CineParticles(skyDir.Length);
            CineLog("cine: sky characters " + skyDir.Length);
        }
        static float Hash(float x) { x = Mathf.Sin(x) * 43758.5453f; return x - Mathf.Floor(x); }
        static float Fbm(float x, float y)
        {
            return Mathf.PerlinNoise(x, y) * 0.55f + Mathf.PerlinNoise(x * 2.1f + 5.2f, y * 2.1f + 1.3f) * 0.3f + Mathf.PerlinNoise(x * 4.3f + 9.7f, y * 4.3f + 3.1f) * 0.15f;
        }

        /// one frame of the sky: the gradient of the evening, the clouds drifting with the wind
        void CineSky(float t, Vector3 sunDir)
        {
            if (skyP == null) return;
            var horizon = new Color(1.0f, 0.42f, 0.16f); var low = new Color(0.78f, 0.24f, 0.22f); var mid = new Color(0.30f, 0.13f, 0.34f); var high = new Color(0.05f, 0.06f, 0.16f);
            float sunAz = Mathf.Atan2(sunDir.x, sunDir.z);
            skyP.Begin();
            float size = skyCell * SKY_R * 1.25f;
            for (int i = 0; i < skyDir.Length; i++)
            {
                var d = skyDir[i]; float el = Mathf.Asin(Mathf.Clamp(d.y, -1, 1)), az = Mathf.Atan2(d.x, d.z);
                // the gradient: hot at the horizon, through rose and violet into the night above; brighter towards the sun
                float u = Mathf.Clamp01(el / 0.42f);
                Color c = u < 0.12f ? Color.Lerp(horizon, low, u / 0.12f) : u < 0.45f ? Color.Lerp(low, mid, (u - 0.12f) / 0.33f) : Color.Lerp(mid, high, (u - 0.45f) / 0.55f);
                float toSun = Mathf.Max(0, Vector3.Dot(d, sunDir));
                c += new Color(1.0f, 0.6f, 0.3f) * (Mathf.Pow(toSun, 12f) * 0.8f + Mathf.Pow(toSun, 3f) * 0.15f);
                float b = 0.22f + 0.35f * (1 - u);
                // clouds: long bands, flattened towards the horizon (as a layer seen far off), drifting
                float cx = az * 3.2f + t * 0.012f, cy = (el + 0.02f) * 13f / Mathf.Max(0.25f, el * 4f + 0.25f);
                float n = Fbm(cx, cy * 0.55f + 3f);
                float cloud = Mathf.Clamp01((n - 0.47f) * 3.4f) * Mathf.Clamp01((el - 0.01f) / 0.04f) * Mathf.Clamp01(1.2f - u);
                int row = 11, g;
                if (cloud > 0.05f)
                {
                    // lit from below by the low sun: hot rims, the bodies rose and grey-violet
                    float under = Mathf.Clamp01(1 - (Fbm(cx, cy * 0.55f + 3.15f) - n) * 8f);
                    var cc = Color.Lerp(new Color(0.42f, 0.28f, 0.40f), new Color(1.0f, 0.55f, 0.32f), under * (0.5f + 0.5f * (1 - u)));
                    cc += new Color(1.0f, 0.55f, 0.25f) * Mathf.Pow(toSun, 6f) * 0.8f;
                    c = Color.Lerp(c, cc, cloud); b = Mathf.Lerp(b, 0.55f + 0.4f * under, cloud);
                    row = 2; g = Mathf.Clamp(Mathf.RoundToInt(3 + b * 11 + Hash(i * 1.7f) * 1.5f), 1, 15);
                }
                else g = Mathf.Clamp(Mathf.RoundToInt(1 + b * 6 + Hash(i * 3.1f) * 2f), 1, 8);
                skyP.Add(skyEye + d * SKY_R, c * (0.45f + 0.75f * b), size, g, row);
            }
            skyP.End();
        }
    }
}
