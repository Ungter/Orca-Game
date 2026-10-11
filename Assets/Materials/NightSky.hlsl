#ifndef ORCA_NIGHT_SKY_INCLUDED
#define ORCA_NIGHT_SKY_INCLUDED

// Stars and aurora shared by the night skybox and the ocean's sky reflection.
// The including shader must declare the _Star* / _Twinkle* / _Aurora* properties.

#ifndef NIGHT_SKY_AURORA_STEPS
#define NIGHT_SKY_AURORA_STEPS 16
#endif

float ns_hash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float3 ns_hash33(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

float ns_hash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float ns_noise2(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = ns_hash12(i);
    float b = ns_hash12(i + float2(1, 0));
    float c = ns_hash12(i + float2(0, 1));
    float d = ns_hash12(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float ns_fbm2(float2 p)
{
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++)
    {
        v += a * ns_noise2(p);
        p = p * 2.03 + float2(17.1, 9.7);
        a *= 0.5;
    }
    return v;
}

float3 ns_RotateY(float3 d, float degrees)
{
    float r = radians(degrees);
    float s = sin(r), c = cos(r);
    return float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);
}

// One layer of stars on a 3D cell grid around the viewer. Star radius is kept at
// least about one pixel wide so stars don't shimmer as the camera turns.
float3 NightSkyStarLayer(float3 dir, float density, float amount, float size, float seed, float t)
{
    float3 p = dir * density;
    float pixel = length(fwidth(p));
    float3 cell = floor(p);
    float3 rnd = ns_hash33(cell + seed);
    float exists = step(ns_hash13(cell + seed * 1.7), amount);

    float3 starPos = cell + 0.25 + rnd * 0.5;
    float dist = length(p - starPos);
    float radius = max(size, pixel * 0.75);
    float energy = (size * size) / (radius * radius);

    float magnitude = pow(rnd.x, 6.0);
    float core = exp(-(dist * dist) / (radius * radius)) * energy;
    float halo = _StarGlow * size * 0.6 / (dist + size) * smoothstep(0.5, 0.0, dist) * magnitude;

    float phase = rnd.y * 6.2831;
    float speed = _TwinkleSpeed * (0.5 + rnd.z);
    float twinkle = 0.5 + 0.5 * sin(t * speed + phase) * sin(t * speed * 0.37 + phase * 2.3);
    twinkle = lerp(1.0, twinkle, _TwinkleAmount);

    float3 tint = lerp(float3(0.75, 0.85, 1.0), float3(1.0, 0.85, 0.65), rnd.z);
    tint = lerp(tint, float3(1, 1, 1), 0.5);
    return tint * (core * (0.15 + magnitude * 2.0) + halo) * twinkle * exists;
}

// Thin, wavy curtains: ridged noise on a stretched, domain-warped plane.
float NightSkyAuroraField(float2 p, float t)
{
    float2 warp = float2(ns_fbm2(p * 0.35 + t * 0.06), ns_fbm2(p * 0.35 - t * 0.05 + 7.3));
    p += (warp - 0.5) * 2.2;
    float v = ns_fbm2(float2(p.x * 0.18, p.y * 0.9 + t * 0.12));
    float ridge = 1.0 - abs(v * 2.0 - 1.0);
    return pow(saturate(ridge), 10.0);
}

float3 NightSkyAurora(float3 dir, float t)
{
    if (dir.y < 0.01) return 0;

    float3 acc = 0;
    float drift = t * _AuroraSpeed;
    for (int i = 0; i < NIGHT_SKY_AURORA_STEPS; i++)
    {
        float k = i / (float)(NIGHT_SKY_AURORA_STEPS - 1);
        float height = 1.0 + k * 0.9;
        float2 pos = dir.xz * (height / (dir.y + 0.08)) * _AuroraScale * 1.6;
        float field = NightSkyAuroraField(pos + float2(drift * 0.4, drift * 0.15), drift);
        float3 col = lerp(_AuroraColorA.rgb, _AuroraColorB.rgb, smoothstep(0.1, 0.95, k));
        acc += col * field * (1.0 - k) * (1.0 - k);
    }
    acc *= 3.0 / NIGHT_SKY_AURORA_STEPS;

    // Patches drift in and out across the sky, and the whole display breathes slowly.
    float2 patchPos = dir.xz / (dir.y + 0.15);
    float patches = smoothstep(0.35, 0.75, ns_fbm2(patchPos * 0.6 + t * 0.015));
    float w = 6.2831 / max(_AuroraFadePeriod, 0.01);
    float breathe = 0.5 + 0.5 * sin(t * w) * (0.7 + 0.3 * sin(t * w * 2.71 + 1.3));
    breathe = smoothstep(0.0, 1.0, breathe);
    float visibility = lerp(_AuroraMinVisibility, 1.0, breathe) * patches;

    float horizonFade = smoothstep(0.01, 0.18, dir.y) * (1.0 - smoothstep(0.75, 1.0, dir.y) * 0.6);
    return acc * visibility * horizonFade * _AuroraIntensity;
}

// Stars plus aurora along an already-rotated, normalized sky direction.
float3 NightSkyLights(float3 dir, float t)
{
    float starMask = smoothstep(-0.02, 0.12, dir.y);
    float3 stars = NightSkyStarLayer(dir, _StarDensity, _StarAmount, _StarSize, 0.0, t);
    stars += NightSkyStarLayer(dir, _StarDensity * 2.3, _StarAmount * 0.8, _StarSize * 0.8, 41.0, t) * 0.45;
    stars *= _StarBrightness * starMask;

    float3 aurora = NightSkyAurora(dir, t);
    // Bright aurora washes out the faintest stars a little, like real sky glow.
    stars *= 1.0 - saturate(dot(aurora, float3(0.3, 0.6, 0.1)) * 1.5);
    return stars + aurora;
}

#endif
