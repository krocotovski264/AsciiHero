// The sky over the open world: the time of day, the season and the weather (for now switched by hand from the
// developer buttons). Each choice eases in over a couple of seconds; what it changes:
//   time of day - where the light comes from (a low warm sun in the morning and the evening, a high white one by day,
//                 the cold moon at night), how bright the ambient air is, the tint of the whole picture
//   season      - the foliage: fresh green with a few blossoms in spring, deep green in summer, orange and yellow in
//                 autumn, bare grey twigs in winter; in winter the ground lies white
//   weather     - precipitation on or off, light, medium or heavy; snow in winter, rain otherwise (grey air, wet dark
//                 ground that glints, slanting '/' streaks over the screen; heavy rain is a downpour
//                 (darker, denser and faster, flashes of lightning) or snow (flakes drifting down; the ground whitens
//                 the longer it falls)
// Night, summer and clear sky is the world exactly as it was before the sky existed.
using UnityEngine;

namespace AsciiHero
{
    public class Sky
    {
        public static readonly string[] TOD = { "Утро", "День", "Вечер", "Ночь" };
        public static readonly string[] SEASON = { "Весна", "Лето", "Осень", "Зима" };
        public static readonly string[] LEVEL = { "Лёгкие", "Средние", "Сильные" };
        // precipitation: on or off and how strong; what falls follows the season - snow in winter, rain the rest of the year
        public int tod = 3, season = 1, level = 1; public bool precip;
        static readonly float[] AMOUNT = { 0.35f, 0.65f, 1f };
        int weather => !precip ? 0 : season == 3 ? 3 : level == 2 ? 2 : 1;

        // eased state
        float[] todW = { 0, 0, 0, 1 }, seasW = { 0, 1, 0, 0 };
        float rain, snow, overcast, wet, snowCover, flash, flashT = 6;

        // what the game reads each frame
        public float ambK = 1, sunK = 0.3f, heroK = 1, expK = 1;
        public Color ambTint = Color.white, sunCol, grade = Color.white;
        public Vector3 sunDir;
        public float Rain => rain; public float Snow => snow; public float Flash => flash; public float SnowCover => snowCover;
        public float DayK => 1 - todW[3]; public float Winter => seasW[3];
        public bool IsDay => todW[1] + todW[0] * 0.6f + todW[2] * 0.6f > 0.5f;

        static readonly Color[] SUN_COL = { Mats.Hex(0xffc89a), Mats.Hex(0xfff4e2), Mats.Hex(0xff9a5a), Mats.Hex(0x8aa6ff) };
        static readonly float[] SUN_I = { 0.85f, 1.35f, 0.75f, 0.3f };
        static readonly float[] AMB_K = { 2.0f, 3.4f, 1.7f, 1.0f };
        static readonly float[] HERO_K = { 0.55f, 0.35f, 0.7f, 1.0f };
        static readonly float[] EXP_K = { 1.0f, 0.92f, 1.0f, 1.0f };
        static readonly Color[] GRADE = { new Color(1.05f, 0.97f, 0.92f), new Color(1, 1, 1), new Color(1.12f, 0.88f, 0.76f), new Color(1, 1, 1) };
        // where the light stands: east and low in the morning, high at noon, west and low in the evening, the moon
        static readonly Vector3[] SUN_DIR = { new Vector3(0.75f, 0.32f, -0.45f), new Vector3(0.15f, 0.92f, -0.35f), new Vector3(-0.78f, 0.28f, -0.4f), new Vector3(-0.12f, 0.788f, -0.604f) };

        public void Update(float dt)
        {
            float k = 1 - Mathf.Exp(-dt / 0.8f), kSlow = 1 - Mathf.Exp(-dt / 2.5f);
            for (int i = 0; i < 4; i++) { todW[i] += ((i == tod ? 1 : 0) - todW[i]) * k; seasW[i] += ((i == season ? 1 : 0) - seasW[i]) * k; }
            float amt = precip ? AMOUNT[Mathf.Clamp(level, 0, 2)] : 0;
            float rT = weather == 1 || weather == 2 ? amt : 0, sT = weather == 3 ? amt : 0;
            rain += (rT - rain) * k; snow += (sT - snow) * k;
            overcast += ((weather == 0 ? 0 : 0.45f + 0.55f * amt) - overcast) * k;
            wet += ((weather == 1 || weather == 2 ? 1 : 0) - wet) * (rT > wet ? kSlow : (1 - Mathf.Exp(-dt / 20f)));            // dries slowly
            float coverT = Mathf.Max(seasW[3] * 0.85f, weather == 3 ? 1 : 0);
            snowCover += (coverT - snowCover) * (coverT > snowCover ? (1 - Mathf.Exp(-dt / (weather == 3 ? 6f : 2f))) : (1 - Mathf.Exp(-dt / 8f)));
            // lightning in a downpour: a flash every 6-14 s, sometimes doubled
            flash = Mathf.Max(0, flash - dt * 4);
            if (rain > 0.8f && weather == 2) { flashT -= dt; if (flashT <= 0) { flash = 1; flashT = Random.value < 0.3f ? 0.18f : 6 + 8 * Random.value; } }

            ambK = 0; sunK = 0; heroK = 0; expK = 0; sunCol = Color.black; grade = Color.black; sunDir = Vector3.zero;
            for (int i = 0; i < 4; i++)
            {
                float w = todW[i];
                ambK += AMB_K[i] * w; sunK += SUN_I[i] * w; heroK += HERO_K[i] * w; expK += EXP_K[i] * w;
                sunCol += SUN_COL[i] * w; grade += GRADE[i] * w; sunDir += SUN_DIR[i].normalized * w;
            }
            sunDir = sunDir.sqrMagnitude > 1e-4f ? sunDir.normalized : SUN_DIR[3].normalized;
            // clouds: the sun is veiled, the air goes grey and a little dimmer (the moon was dim anyway)
            float day = 1 - todW[3];
            sunK *= Mathf.Lerp(1, 0.25f, overcast * Mathf.Lerp(0.6f, 1, day));
            ambK *= Mathf.Lerp(1, 0.75f, overcast * day);
            ambTint = Color.Lerp(Color.white, new Color(0.82f, 0.86f, 0.95f), overcast) * (1 + flash * 1.6f);
            grade = Color.Lerp(grade, new Color(0.88f, 0.92f, 1.0f), overcast * 0.6f);
            grade = Color.Lerp(grade, new Color(0.95f, 0.97f, 1.05f), snow * 0.4f);
            if (snowCover > 0.01f) ambK *= 1 + 0.25f * snowCover * day;         // snow throws the light back up

            Shader.SetGlobalVector("_AsciiGrade", new Vector4(grade.r, grade.g, grade.b, Mathf.Lerp(1, 0.7f, overcast * 0.7f)));
            Shader.SetGlobalVector("_AsciiPrecip", new Vector4(rain, snow, 0, flash));
            Shader.SetGlobalVector("_AsciiSeason", new Vector4(seasW[2], seasW[3], seasW[0], snowCover));
            Shader.SetGlobalFloat("_AsciiWet", wet);
            Shader.SetGlobalFloat("_AsciiDayK", day);
        }
        /// indoors and in the editor: no weather, no tint
        public static void Neutral()
        {
            Shader.SetGlobalVector("_AsciiGrade", new Vector4(1, 1, 1, 1)); Shader.SetGlobalVector("_AsciiPrecip", Vector4.zero);
            Shader.SetGlobalVector("_AsciiSeason", Vector4.zero); Shader.SetGlobalFloat("_AsciiWet", 0); Shader.SetGlobalFloat("_AsciiDayK", 0);
        }
        /// the precipitation leans with the wind (in screen cells per cell of fall)
        public void Wind(Vector2 screenWind) { Shader.SetGlobalVector("_AsciiPrecipWind", new Vector4(screenWind.x, screenWind.y, 0, 0)); }
    }
}
