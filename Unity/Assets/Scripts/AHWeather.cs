// The weather of the story's open world (after the design note "dynamic precipitation, accumulation and the world"):
//   - spells of rain or snow come and go on their own (clear 4-12 min, falling 2-8 min; light, medium or heavy; a
//     heavy one is sometimes a storm that drives it sideways); what falls follows the season: snow in winter, rain in
//     summer, rain in spring and autumn (now and then wet snow at night); nothing falls in the caves and the inns
//   - what lies on the ground grows with the time it falls (the share of the open level ground it covers):
//                 0-1 min      1-3 min    3-5 min    > 5 min (to 10)
//       light     0 (damp)     0 -> 5 %   5 -> 10 %  10 -> 20 %
//       medium    0 (damp)     0 -> 10 %  10 -> 20 % 20 -> 30 %
//       heavy     0 -> 5 %     5 -> 20 %  20 -> 30 % 30 -> 40 %
//     puddles from rain, drifts from snow (up to 0.85 m high at 40 %), laid out by a world-space noise field (the
//     drifts on the same field mirrored and turned) so the same spots fill first and nothing breaks at a tile's edge
//   - after it stops: puddles dry 1 % in 5 s by day, in 10 s in the evening, not at night; then the damp goes in 30 s.
//     Snow melts above freezing (not in winter) 1 % in 8 s by day, in 20 s in the evening, not at night, feeding the
//     damp and up to 5 % of puddles; rain on snow melts it three times as fast into slush and puddles
//   - in winter there are no puddles: those lying at the turn of the year freeze into ice (slippery, unless snow has
//     covered more than 15 % of the ground); in spring the ice thaws into puddles again
//   - the creatures out in it: soaked by rain (darker, glossier), snow settling on the shoulders and helmets
// The ground shader and the game read the same noise (WxNoise here and in AsciiWeather.hlsl), so a foot knows whether
// it steps in a puddle, on ice or into a drift.
using UnityEngine;

namespace AsciiHero
{
    public class Weather
    {
        public bool auto = true;
        public int level = -1;                  // -1 nothing falls, 0 light, 1 medium, 2 heavy
        public bool snowFalls, stormy;
        public float puddle, snow, ice, wet, white, actorWet, actorSnow, storm;
        float rainT, snowT, wait = 300, left, meltPud; bool wetSnowNight; int lastSeason = -1;
        readonly System.Random R = new System.Random(7731);
        public bool Falling => level >= 0;
        public bool Raining => level >= 0 && !snowFalls;
        public bool Snowing => level >= 0 && snowFalls;

        static readonly float[] TT = { 0, 60, 180, 300, 600 };
        static readonly float[][] CC = { new[] { 0, 0, 0.05f, 0.10f, 0.20f }, new[] { 0, 0, 0.10f, 0.20f, 0.30f }, new[] { 0, 0.05f, 0.20f, 0.30f, 0.40f } };
        static float Table(int lv, float t)
        {
            var c = CC[lv]; if (t >= TT[4]) return c[4];
            int k = 0; while (k < 3 && t > TT[k + 1]) k++;
            return Mathf.Lerp(c[k], c[k + 1], Mathf.InverseLerp(TT[k], TT[k + 1], t));
        }
        /// how long it must have fallen to lie as much as it does (a new spell goes on from what lies already)
        static float Since(int lv, float cov)
        {
            var c = CC[lv]; if (cov >= c[4]) return TT[4];
            for (int k = 0; k < 4; k++) if (cov <= c[k + 1] && c[k + 1] > c[k]) return Mathf.Lerp(TT[k], TT[k + 1], Mathf.InverseLerp(c[k], c[k + 1], cov));
            return 0;
        }
        public void Start(int lv, float dur)
        {
            level = Mathf.Clamp(lv, 0, 2); left = dur;
            rainT = Since(level, puddle); snowT = Since(level, snow);
            wetSnowNight = R.NextDouble() < 0.35; stormy = level == 2 && R.NextDouble() < 0.5;
        }
        public void Stop() { level = -1; wait = 240 + 480 * (float)R.NextDouble(); stormy = false; }

        /// tod: 0 morning, 1 day, 2 evening, 3 night; outside: the hero is under the open sky
        public void Update(float dt, int season, int tod, bool outside)
        {
            bool winter = season == 3;
            // the turn of the year: puddles freeze when winter comes
            if (lastSeason != season) { lastSeason = season; }
            if (auto)
            {
                if (level < 0) { wait -= dt; if (wait <= 0) { double r = R.NextDouble(); Start(r < 0.45 ? 0 : r < 0.8 ? 1 : 2, 120 + 360 * (float)R.NextDouble()); } }
                else { left -= dt; if (left <= 0) Stop(); }
            }
            snowFalls = level >= 0 && (winter || (season == 0 || season == 2) && tod == 3 && wetSnowNight);
            bool day = tod <= 1, eve = tod == 2;
            float dry = day ? 1 / 500f : eve ? 1 / 1000f : 0;               // 1 % in 5 s, in 10 s, none at night
            float melt = winter ? 0 : day ? 1 / 800f : eve ? 1 / 2000f : 0;    // 1 % in 8 s, in 20 s, none at night
            if (Raining)
            {
                rainT += dt;
                if (!winter) puddle = Mathf.Max(puddle, Table(level, rainT));
                wet = Mathf.MoveTowards(wet, 1, dt / (level == 2 ? 4f : 60f));
                if (snow > 0 && !winter)
                {   // rain on snow: three times the melt (by day's rate even at night), into slush and puddles
                    float m = Mathf.Min(snow, 3 * Mathf.Max(melt, 1 / 2000f) * dt); snow -= m; puddle = Mathf.Min(0.4f, puddle + m);
                }
            }
            else
            {
                puddle = Mathf.Max(0, puddle - dry * dt);
                if (puddle <= 0 && !Snowing && dry > 0 && snow <= 0) wet = Mathf.MoveTowards(wet, 0, dt / 30f);
            }
            if (Snowing)
            {
                snowT += dt;
                snow = Mathf.Max(snow, Table(level, snowT));
                white = Mathf.MoveTowards(white, 1, dt / (level == 2 ? 6f : 60f));
            }
            else if (snow > 0 && melt > 0)
            {   // thaw: the snow goes into the damp and a few puddles
                float m = Mathf.Min(snow, melt * dt); snow -= m; wet = 1;
                float p = Mathf.Min(m * 0.125f, 0.05f - meltPud); if (p > 0) { puddle += p; meltPud += p; }
            }
            if (snow <= 0.001f && !Snowing) { snow = 0; meltPud = 0; if (melt > 0 || !winter) white = Mathf.MoveTowards(white, 0, dt / 30f); }
            // winter: nothing stays liquid; spring and summer: the ice thaws by day
            if (winter) { if (puddle > 0) { ice = Mathf.Max(ice, puddle); puddle = 0; } }
            else if (ice > 0 && melt > 0) { float m = Mathf.Min(ice, 2 * melt * dt); ice -= m; puddle += m; wet = Mathf.Max(wet, 0.6f); }
            // the creatures out in it
            if (outside && Raining) actorWet = Mathf.MoveTowards(actorWet, 1, dt / (12f - 4 * level));
            else actorWet = Mathf.MoveTowards(actorWet, 0, dt / (day ? 60f : 150f));
            if (outside && Snowing) actorSnow = Mathf.MoveTowards(actorSnow, 1, dt / (40f - 12 * level));
            else actorSnow = Mathf.MoveTowards(actorSnow, 0, dt / (winter ? 120f : 25f));
            storm = Mathf.MoveTowards(storm, stormy ? 1 : 0, dt / 8f);
        }

        // ---- the ground's noise (the same as AsciiWeather.hlsl) ----
        static float Hash(int x, int y)
        {
            unchecked { uint h = ((uint)x * 73856093u) ^ ((uint)y * 19349663u); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xffffffu) / 16777216f; }
        }
        static float VN(float px, float py)
        {
            float fx0 = Mathf.Floor(px), fy0 = Mathf.Floor(py), fx = px - fx0, fy = py - fy0; int ix = (int)fx0, iy = (int)fy0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }
        public static float Noise(float x, float z) { return 0.65f * VN(x * 0.45f, z * 0.45f) + 0.35f * VN(x * 1.3f + 17.3f, z * 1.3f + 17.3f); }
        public static float SnowNoise(float x, float z) { return Noise(z * 0.8f + 41.1f, -x * 0.8f + 41.1f); }

        /// the noise level under which a share of the ground lies (the field's quantiles, sampled once)
        static float[] Q;
        public static float Threshold(float cover)
        {
            if (cover <= 0.0005f) return -1;
            if (Q == null)
            {
                var r = new System.Random(3); Q = new float[4096];
                for (int i = 0; i < Q.Length; i++) Q[i] = Noise((float)r.NextDouble() * 500 - 250, (float)r.NextDouble() * 500 - 250);
                System.Array.Sort(Q);
            }
            return Q[Mathf.Clamp((int)(cover * (Q.Length - 1)), 0, Q.Length - 1)];
        }
        public float DriftH => 0.85f * Mathf.Clamp01(snow / 0.4f);

        /// what lies under a foot here: 0 bare, 1 a puddle, 2 ice, 3 snow (with its depth in m)
        public int SurfaceAt(Vector3 p, float trail, out float depth)
        {
            depth = 0;
            float n = Noise(p.x, p.z);
            float dr = Mathf.Clamp01((Threshold(snow) - SnowNoise(p.x, p.z)) / 0.06f);
            depth = dr * DriftH * (1 - 0.85f * trail);
            if (dr > 0.3f || white > 0.5f && snow > 0.005f) return 3;
            if (n < Threshold(ice)) return 2;
            if (n < Threshold(puddle)) return 1;
            return 0;
        }
    }
}
