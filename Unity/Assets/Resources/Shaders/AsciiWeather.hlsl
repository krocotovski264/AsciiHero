// What lies on the ground (Weather.cs): puddles, ice and snow drifts laid out by one world-space noise field (the
// same numbers as Weather.Noise on the CPU, so a foot knows what it steps in), and the trail of trodden snow.
#ifndef ASCII_WEATHER_INCLUDED
#define ASCII_WEATHER_INCLUDED
float4 _WxGround;   // x puddle threshold, y ice threshold, z snow threshold, w drift height (m)
float4 _WxState;    // x damp, y rain (rings on the puddles), z frost (the thin white before the drifts)
float4 _WxActor;    // x soaked, y snow on the shoulders
float4 _WxTrailRect;  // x0, z0, 1 / size, on
TEXTURE2D(_WxTrail); SAMPLER(sampler_WxTrail);
float WxHash(int2 c)
{
    uint h = ((uint)c.x * 73856093u) ^ ((uint)c.y * 19349663u);
    h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
    return (float)(h & 0xffffffu) / 16777216.0;
}
float WxVN(float2 p)
{
    float2 i = floor(p), f = p - i; f = f * f * (3.0 - 2.0 * f); int2 c = (int2)i;
    float a = WxHash(c), b = WxHash(c + int2(1, 0)), cc = WxHash(c + int2(0, 1)), d = WxHash(c + int2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(cc, d, f.x), f.y);
}
float WxNoise(float2 p) { return 0.65 * WxVN(p * 0.45) + 0.35 * WxVN(p * 1.3 + 17.3); }
float WxSnowNoise(float2 p) { return WxNoise(float2(p.y * 0.8 + 41.1, -p.x * 0.8 + 41.1)); }      // the same field, mirrored and turned
float WxDrift(float2 p) { return _WxGround.z > 0.0 ? saturate((_WxGround.z - WxSnowNoise(p)) / 0.06) : 0.0; }
float WxTrail(float2 p)
{
    if (_WxTrailRect.w < 0.5) return 0.0;
    float2 uv = (p - _WxTrailRect.xy) * _WxTrailRect.z;
    if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) return 0.0;
    return SAMPLE_TEXTURE2D_LOD(_WxTrail, sampler_WxTrail, uv, 0).r;
}
#endif
