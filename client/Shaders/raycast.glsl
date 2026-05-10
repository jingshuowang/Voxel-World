#version 460 core
layout(local_size_x = 8, local_size_y = 8) in;
layout(rgba32f, binding = 0) uniform image2D imgColor;
layout(rgba32f, binding = 1) uniform image2D imgPos;
layout(rgba32f, binding = 2) uniform image2D imgEmissive;

uniform vec3  uCamPos, uCamFwd, uCamRgt, uCamUp;
uniform float uTime, uFov, uFrame;
uniform vec2  uJitter;

#include "lod.glsl"
#include "fog.glsl"

uniform float uRenderDistance;
uniform float uLodThreshold;
uniform float uLodScale;
uniform float uFogStart;
uniform float uFogEnd;

uniform vec3  uSunCol;
uniform float uSunBrightness;
uniform float uSunBloom;
uniform float uAmbientBase;
uniform float uAmbientSlope;
uniform float uCloudAlt;
uniform float uCloudCover;
uniform float uCloudScale;
uniform float uShowGrid;
uniform float uEnableFog;

const float MAX_D = 262144.0;
const vec3  SUN   = normalize(vec3(0.55, 1.0, 0.35));

// Maximum world-height rise per world-horizontal from getH's analytic derivative:
// 0.008*96 + 0.04*24 + 0.12*6 + 0.25*1.5 = 2.823. Use 3.0 for safety margin.
const float TERRAIN_MAX_SLOPE = 3.0;

// -- RNG --

uint rngState;
void initRNG(ivec2 px, uint frame) {
    rngState = uh(ivec3(px, int(frame)));
}
float nextRand() {
    rngState = rngState * 1664525u + 1013904223u;
    return float(rngState & 0x00FFFFFFu) / 16777216.0;
}
vec3 randomHemisphere(vec3 norm) {
    float u1  = nextRand();
    float u2  = nextRand();
    float r   = sqrt(max(0.0, 1.0 - u1 * u1));
    float phi = 6.2831853 * u2;
    vec3  dir = vec3(cos(phi) * r, sin(phi) * r, u1);
    vec3  tan = normalize(abs(norm.x) > 0.1 ? vec3(0, 1, 0) : vec3(1, 0, 0));
    tan = normalize(tan - norm * dot(tan, norm));
    vec3 bit = cross(norm, tan);
    return normalize(tan * dir.x + bit * dir.y + norm * dir.z);
}

// -- Ray-AABB intersection --

float hitCube(vec3 ro, vec3 rd, vec3 cen, float hs, out vec3 norm) {
    vec3  m     = 1.0 / (rd + sign(rd) * 1e-7);
    vec3  t0    = (cen - vec3(hs) - ro) * m;
    vec3  t1    = (cen + vec3(hs) - ro) * m;
    vec3  tN    = min(t0, t1);
    vec3  tF    = max(t0, t1);
    float tNear = max(tN.x, max(tN.y, tN.z));
    float tFar  = min(tF.x, min(tF.y, tF.z));
    if (tNear > tFar || tFar < 1e-3) return -1.0;
    float t = tNear > 1e-3 ? tNear : tFar;
    if      (abs(t - tN.x) < 1e-4) norm = vec3(-sign(rd.x), 0, 0);
    else if (abs(t - tN.y) < 1e-4) norm = vec3(0, -sign(rd.y), 0);
    else                            norm = vec3(0, 0, -sign(rd.z));
    return t;
}

// -- DDA: next voxel boundary. Zero-direction axes get +inf. --

float ddaNext(vec3 p, vec3 rd, float cellSize) {
    vec3 stp = sign(rd);
    vec3 bnd = (floor(p / cellSize) + max(stp, vec3(0.0))) * cellSize;
    vec3 dt  = (bnd - p) / (rd + sign(rd) * 1e-7);
    dt = mix(dt, vec3(1e30), lessThan(abs(rd), vec3(1e-7)));
    return min(dt.x, min(dt.y, dt.z));
}

// -- Sky step: jump over open air safely.
// Derivation: getH has analytic max slope TERRAIN_MAX_SLOPE (world/world).
// For a step s along rd, terrain can rise at most TERRAIN_MAX_SLOPE*s*sqrt(1-rd.y^2).
// Ray descends rd.y*s. Safe when: hAbove > s*(TERRAIN_MAX_SLOPE - rd.y).
// Since rd.y is negative for downward rays, -rd.y adds to the denominator.

float skyStep(float hAbove, vec3 rd) {
    float slopeScale = (uAmplitude == 0.0) ? 1.0 : max(1.0, uAmplitude / 1104.0);
    float maxSlope = 3.0 * slopeScale;
    return max(hAbove / (maxSlope + abs(rd.y) + 0.05), CELL);
}

// -- Primary trace --

float trace(vec3 ro, vec3 rd, out vec3 norm, out Cube hitC) {
    float t    = 0.05;
    float tHit = -1.0;
    for (int i = 0; i < 256; i++) {
        if (t > uRenderDistance) break;

        // Determine LOD based on planar depth 'd' from camera plane using uLodThreshold
        float d = t * dot(rd, uCamFwd);
        int lod = 1;
        if      (d > uLodThreshold * 8.0) lod = 16;
        else if (d > uLodThreshold * 4.0) lod = 8;
        else if (d > uLodThreshold * 2.0) lod = 4;
        else if (d > uLodThreshold)       lod = 2;

        float cellSize = CELL * float(lod);
        vec3  p    = ro + rd * (t + 0.01);
        float maxTerrainH = 256.0 + ((uAmplitude == 0.0) ? 1104.0 : uAmplitude);
        if (p.y > maxTerrainH) {
            if (rd.y >= 0.0) break;
            t += (p.y - maxTerrainH) / abs(rd.y);
            continue;
        }
        ivec3 cell = ivec3(floor(p / CELL));

        Cube c;
        if (getCubeLOD(cell, lod, c)) {
            vec3  n;
            float th = hitCube(ro, rd, c.cen, c.halfSz, n);
            if (th > 1e-3) { tHit = th; norm = n; hitC = c; break; }
        }

        float terrainY = getH(vec2(cell.xz), lod) * CELL;
        float hAbove   = p.y - terrainY;
        if (hAbove > CELL * 2.0) {
            t += skyStep(hAbove, rd);
        } else {
            t += max(ddaNext(p, rd, cellSize), 0.01);
        }
    }
    return tHit;
}

// -- Shadow trace --

bool shadowTrace(vec3 ro, vec3 rd) {
    float t = 0.05;
    for (int i = 0; i < 512; i++) {
        if (t > 512.0) return false;
        vec3  p        = ro + rd * t;
        int   lod      = (t > 500.0) ? 4 : 1;
        float cellSize = CELL * float(lod);
        ivec3 cell     = ivec3(floor(p / cellSize)) * lod;
        Cube  c;
        if (getCubeLOD(cell, lod, c) && c.matType != MAT_GLASS) {
            vec3 n;
            if (hitCube(ro, rd, c.cen, c.halfSz, n) > 1e-3) return true;
        }
        float terrainY = getH(vec2(ivec3(floor(p / CELL)).xz)) * CELL;
        float hAbove   = p.y - terrainY;
        if (hAbove > CELL * 2.0) {
            t += skyStep(hAbove, rd);
        } else {
            t += max(ddaNext(p, rd, cellSize), 0.01);
        }
    }
    return false;
}

// -- Rotate --

vec3 rotate(vec3 v, float pitch, float yaw, float roll) {
    float cp = cos(pitch), sp = sin(pitch);
    vec3 v1 = vec3(v.x, cp * v.y - sp * v.z, sp * v.y + cp * v.z);
    float cy = cos(yaw), sy = sin(yaw);
    vec3 v2 = vec3(cy * v1.x + sy * v1.z, v1.y, -sy * v1.x + cy * v1.z);
    float cr = cos(roll), sr = sin(roll);
    return vec3(cr * v2.x - sr * v2.y, sr * v2.x + cr * v2.y, v2.z);
}

// -- Sky --

vec3 sky(vec3 rd) {
    float t   = clamp(rd.y * 0.5 + 0.5, 0.0, 1.0);
    vec3  col = mix(vec3(0.03, 0.05, 0.12), vec3(0.12, 0.28, 0.72), sqrt(t));
    return col;
}

// Chunk grid overlay: red=X boundary, green=Z boundary, blue=Y boundary.
vec3 chunkGrid(vec3 color, vec3 pos) {
    if (uShowGrid < 0.5) return color;
    const float CHUNK = 256.0;  // 32 cells * 8 world units
    const float LW    = 4.0;    // line half-width in world units
    vec3  fp  = mod(pos + 1e6 * CHUNK, CHUNK);
    bool onX = fp.x < LW || fp.x > CHUNK - LW;
    bool onZ = fp.z < LW || fp.z > CHUNK - LW;
    bool onY = fp.y < LW || fp.y > CHUNK - LW;
    if (!onX && !onZ && !onY) return color;
    vec3 gc = vec3(float(onX), float(onZ), float(onY)) * 0.9;
    return mix(color, gc, 0.85);
}

vec3 errPat(ivec2 px) {
    return (((px.x / 8 + px.y / 8) & 1) == 0) ? vec3(0.5, 0, 0.5) : vec3(0);
}

// -- Main --

void main() {
    ivec2 px = ivec2(gl_GlobalInvocationID.xy);
    ivec2 sz = imageSize(imgColor);
    if (px.x >= sz.x || px.y >= sz.y) return;

    initRNG(px, uint(uFrame));

    vec2 ndc = ((vec2(px) + 0.5 + uJitter) / vec2(sz)) * 2.0 - 1.0;
    ndc.x *= float(sz.x) / float(sz.y);

    vec3 rd0 = normalize(uCamFwd + uCamRgt * ndc.x * uFov + uCamUp * ndc.y * uFov);
    vec3 ro   = uCamPos;

    vec3  radiance    = vec3(0.0);
    vec3  throughput  = vec3(1.0);
    vec3  curRo = ro, curRd = rd0;
    bool  wrotePos    = false;
    float primaryT    = -1.0;

    Cube hitC;
    hitC.matType    = MAT_CLAY;
    hitC.albedo     = vec3(0);
    hitC.emissive   = 0.0;
    hitC.cen        = vec3(0);
    hitC.halfSz     = 0.0;
    hitC.smoothness = 0.0;
    hitC.metallic   = 0.0;
    hitC.ior        = 1.0;

    for (int bounce = 0; bounce < 2; bounce++) {
        vec3  hNorm;
        float tHit = trace(curRo, curRd, hNorm, hitC);

        if (tHit < 0.0) {
            vec3 skyCol = sky(curRd);

            // Intersect with the rotating glowing cube in the sky
            vec3  cubeCen = curRo + SUN * 8000.0;
            float pitch = uTime * 0.4;
            float yaw   = uTime * 0.6;
            float roll  = uTime * 0.3;
            vec3  rotatedRo = rotate(curRo - cubeCen, -pitch, -yaw, -roll);
            vec3  rotatedRd = rotate(curRd, -pitch, -yaw, -roll);
            vec3  dummyNorm;
            float tCube = hitCube(rotatedRo, rotatedRd, vec3(0.0), 300.0, dummyNorm);

            if (tCube > 0.0) {
                float faceShading = max(dot(dummyNorm, normalize(vec3(1.0, 2.0, 1.5))), 0.45);
                skyCol = uSunCol * uSunBrightness * 4.0 * faceShading;
            }

            // Clouds: Deleted as requested (saving as fluid for later)

            radiance += throughput * skyCol;
            if (!wrotePos) {
                imageStore(imgPos, px, vec4(0.0));
                // Write rotating cube sun to emissive so it blooms beautifully with a wide radius
                vec3  sunEmit = (tCube > 0.0) ? uSunCol * uSunBloom * 6.0 : vec3(0.0);
                imageStore(imgEmissive, px, vec4(sunEmit, 1.0));
                wrotePos = true;
            }
            break;
        }

        vec3 hitPos = curRo + curRd * tHit;

        if (!wrotePos) {
            primaryT = tHit;
            imageStore(imgPos, px, vec4(hitPos, 1.0));
            vec3 emOut = (hitC.matType == MAT_EMISSIVE)
                       ? hitC.albedo * hitC.emissive : vec3(0.0);
            imageStore(imgEmissive, px, vec4(emOut, 1.0));
            wrotePos = true;
        }

        if (hitC.matType == MAT_EMISSIVE) {
            radiance += throughput * hitC.albedo * hitC.emissive;
            break;
        }

        // Combined optimization: Cut secondary bounces early for less-detailed mid-range layers.
        if (bounce >= 1 && tHit > uLodThreshold * 0.35) {
             break;
        }

        // Far terrain: flat directional lighting, no shadows, no bounces at all.
        if (tHit > uLodThreshold * 0.75) {
            float diff    = max(dot(hNorm, SUN), 0.0);
            float amb     = uAmbientBase + uAmbientSlope * (hNorm.y * 0.5 + 0.5);
            vec3  farCol  = hitC.albedo * (diff * uSunCol + vec3(amb));
            radiance += throughput * farCol;
            break;
        }

        // Shadow fade: linearly reduce shadow strength before the LOD threshold
        // so the hard border is replaced by a gradual lightening.
        float shadowStr = 1.0 - smoothstep(uLodThreshold * 0.65, uLodThreshold, tHit);
        bool  inShadow  = (shadowStr > 0.02) && shadowTrace(hitPos + hNorm * 0.01, SUN);
        float shadowF   = inShadow ? mix(1.0, 0.04, shadowStr) : 1.0;

        float diff  = max(dot(hNorm, SUN), 0.0) * shadowF;
        vec3  halfV = normalize(SUN - curRd);
        float NdH   = max(dot(hNorm, halfV), 0.0);
        float shin  = 4.0 + hitC.smoothness * 252.0;
        float spec  = pow(NdH, shin) * shadowF;

        vec3  F0  = mix(vec3(0.04), hitC.albedo, hitC.metallic);
        float cos0 = max(dot(hNorm, -curRd), 0.0);
        vec3  F   = F0 + (vec3(1.0) - F0) * pow(1.0 - cos0, 5.0);
        float amb  = uAmbientBase + uAmbientSlope * (hNorm.y * 0.5 + 0.5);

        vec3 direct;
        if (hitC.matType == MAT_GLASS) {
            direct = hitC.albedo * amb + F * spec;
        } else {
            vec3 kD = (vec3(1.0) - F) * (1.0 - hitC.metallic);
            direct  = kD * hitC.albedo * (diff + amb) + F * uSunCol * spec;
        }
        radiance += throughput * direct;

        if (hitC.matType == MAT_GLASS) {
            vec3  n    = dot(-curRd, hNorm) > 0.0 ? hNorm : -hNorm;
            float eta  = dot(-curRd, hNorm) > 0.0 ? (1.0 / hitC.ior) : hitC.ior;
            float cosI = dot(n, -curRd);
            float k    = 1.0 - eta * eta * (1.0 - cosI * cosI);
            if (k < 0.0) {
                curRd = normalize(reflect(curRd, n));
                curRo = hitPos + n * 0.005;
            } else {
                vec3 refr = normalize(eta * curRd + (eta * cosI - sqrt(k)) * n);
                if (any(isnan(refr)) || any(isinf(refr))) {
                    radiance += throughput * errPat(px);
                    break;
                }
                curRd = refr;
                curRo = hitPos - n * 0.005;
            }
            throughput *= hitC.albedo * 0.94;
        } else if (hitC.metallic > 0.3 || hitC.smoothness > 0.55) {
            curRd      = normalize(reflect(curRd, hNorm));
            curRo      = hitPos + hNorm * 0.005;
            throughput *= hitC.albedo * hitC.smoothness;
        } else {
            curRd      = randomHemisphere(hNorm);
            curRo      = hitPos + hNorm * 0.005;
            throughput *= hitC.albedo;
        }
    }

    // Distance fog: noise-modulated gradient exponential fog.
    if (uEnableFog > 0.5 && primaryT > 0.0) {
        vec3  hitPos = ro + rd0 * primaryT;
        vec2  np = hitPos.xz * 0.003;
        vec2  ni = floor(np), nf = fract(np);
        nf = nf * nf * (3.0 - 2.0 * nf);
        float n00 = fract(sin(dot(ni,             vec2(127.1, 311.7))) * 43758.5453);
        float n10 = fract(sin(dot(ni + vec2(1,0), vec2(127.1, 311.7))) * 43758.5453);
        float n01 = fract(sin(dot(ni + vec2(0,1), vec2(127.1, 311.7))) * 43758.5453);
        float n11 = fract(sin(dot(ni + vec2(1,1), vec2(127.1, 311.7))) * 43758.5453);
        float noise = mix(mix(n00, n10, nf.x), mix(n01, n11, nf.x), nf.y);

        float fogDensity = -log(0.02) / uFogEnd;
        float d = max(primaryT - uFogStart + (noise - 0.5) * 512.0, 0.0);
        float fog = 1.0 - exp(-fogDensity * d);
        fog = clamp(fog, 0.0, 1.0);
        radiance = mix(radiance, sky(rd0), fog);
    }

    imageStore(imgColor, px, vec4(radiance, 1.0));
}
