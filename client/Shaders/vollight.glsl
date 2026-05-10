#version 460 core
layout(local_size_x = 8, local_size_y = 8) in;
layout(rgba32f, binding = 0) uniform image2D imgColor;
layout(rgba32f, binding = 1) uniform image2D imgPos;

uniform vec3  uCamPos, uCamFwd, uCamRgt, uCamUp;
uniform float uTime, uFov;
uniform vec3  uSunCol;

const vec3  SUN  = normalize(vec3(0.55, 1.0, 0.35));
const float CELL = 8.0;

// Minimal hash functions mirroring lod.glsl so shadow terrain matches.
uint _vuh(ivec3 p) {
    uvec3 v = uvec3(p) * uvec3(1664525u, 22695477u, 1013904223u);
    v.x ^= v.y; v.x *= 0x45d9f3bu;
    v.y ^= v.z; v.y *= 0x9e3779b9u;
    v.z ^= v.x; v.z *= 0x6c62272eu;
    return v.x ^ v.y ^ v.z;
}
float _vhf(uint h) {
    uint v = (h ^ 91u) * 2246822519u;
    v ^= v >> 13; v *= 0x45d9f3bu; v ^= v >> 16;
    return float(v >> 8) / 16777215.0;
}
float getH(vec2 p) {
    float h = 0.0, a = 0.6, freq = 0.007, maxA = 0.0;
    for (int i = 0; i < 6; i++) {
        vec2  sp = p * freq;
        ivec2 si = ivec2(floor(sp));
        vec2  sf = fract(sp);
        sf = sf * sf * (3.0 - 2.0 * sf);
        float n00 = _vhf(_vuh(ivec3(si.x,   si.y,   i)));
        float n10 = _vhf(_vuh(ivec3(si.x+1, si.y,   i)));
        float n01 = _vhf(_vuh(ivec3(si.x,   si.y+1, i)));
        float n11 = _vhf(_vuh(ivec3(si.x+1, si.y+1, i)));
        h += a * mix(mix(n00, n10, sf.x), mix(n01, n11, sf.x), sf.y);
        maxA += a; a *= 0.5; freq *= 2.1;
    }
    return 32.0 + (h / maxA) * 138.0;
}

float getTerrainWorldY(vec2 xz) {
    return floor(getH(floor(xz / CELL))) * CELL;
}

// Dual-lobe HG: strong forward peak (g=0.85) for sharp god rays +
// soft backward lobe (g=-0.1) for ambient fill.
float HG(float cosT) {
    const float g1 = 0.85, g1_2 = g1 * g1;
    const float g2 = -0.10, g2_2 = g2 * g2;
    float fwd = (1.0 - g1_2) / (4.0 * 3.14159265 * pow(max(1.0 + g1_2 - 2.0*g1*cosT, 0.001), 1.5));
    float bwd = (1.0 - g2_2) / (4.0 * 3.14159265 * pow(max(1.0 + g2_2 - 2.0*g2*cosT, 0.001), 1.5));
    return mix(bwd, fwd, 0.85);
}

float sunVisibility(vec3 worldPos) {
    if (worldPos.y < getTerrainWorldY(worldPos.xz)) return 0.0;
    const int S = 16; const float DIST = 1024.0;
    float sStep = DIST / float(S);
    for (int i = 1; i <= S; i++) {
        vec3 p = worldPos + SUN * (float(i) * sStep);
        if (p.y < getTerrainWorldY(p.xz)) return 0.0;
    }
    return 1.0;
}

void main() {
    ivec2 px = ivec2(gl_GlobalInvocationID.xy);
    ivec2 sz = imageSize(imgColor);
    if (px.x >= sz.x || px.y >= sz.y) return;

    vec2 ndc = ((vec2(px) + 0.5) / vec2(sz)) * 2.0 - 1.0;
    ndc.x *= float(sz.x) / float(sz.y);
    vec3 rd = normalize(uCamFwd + uCamRgt*ndc.x*uFov + uCamUp*ndc.y*uFov);

    vec4 posData  = imageLoad(imgPos, px);
    float hitDist = (posData.w > 0.5) ? length(posData.xyz - uCamPos) : 2048.0;
    float marchLen = min(hitDist, 2048.0);

    const int   STEPS   = 48;          // 2x steps for sharper ray definition
    const float DENSITY = 0.006;
    float stepSz = marchLen / float(STEPS);
    float cosT   = dot(rd, SUN);
    float phase  = HG(cosT);

    float scatter = 0.0, ambScatter = 0.0, transmit = 1.0;
    for (int i = 0; i < STEPS; i++) {
        vec3  sampleP = uCamPos + rd * (float(i) + 0.5) * stepSz;
        float vis     = sunVisibility(sampleP);
        float dens    = DENSITY * exp(-max(sampleP.y, 0.0) * 0.006);
        float sigmaS  = dens * stepSz;
        scatter    += transmit * sigmaS * phase * vis;
        ambScatter += transmit * sigmaS * 0.08;  // isotropic sky ambient
        transmit   *= exp(-dens * stepSz);
    }

    float sunElev = max(SUN.y, 0.0);
    vec3  volCol  = mix(vec3(1.0, 0.4, 0.05), uSunCol, sunElev) * scatter * 45.0
                 + vec3(0.08, 0.12, 0.20) * ambScatter * 12.0; // blue sky ambient haze

    vec4 col = imageLoad(imgColor, px);
    imageStore(imgColor, px, vec4(col.rgb * transmit + volCol, 1.0));
}
