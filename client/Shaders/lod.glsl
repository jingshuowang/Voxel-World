// Material type IDs
const int MAT_CLAY     = 0;
const int MAT_PLASTIC  = 1;
const int MAT_METALLIC = 2;
const int MAT_EMISSIVE = 3;
const int MAT_GLASS    = 4;

const float CELL = 8.0;

struct Cube {
    vec3  cen;
    float halfSz;
    vec3  albedo;
    int   matType;
    float smoothness;
    float metallic;
    float emissive;
    float ior;
};

// -- Hash --

uint uh(ivec3 p) {
    uvec3 v = uvec3(p) * uvec3(1664525u, 22695477u, 1013904223u);
    v.x ^= v.y; v.x *= 0x45d9f3bu;
    v.y ^= v.z; v.y *= 0x9e3779b9u;
    v.z ^= v.x; v.z *= 0x6c62272eu;
    return v.x ^ v.y ^ v.z;
}

float hf(uint h, uint salt) {
    uint v = (h ^ salt) * 2246822519u;
    v ^= v >> 13; v *= 0x45d9f3bu; v ^= v >> 16;
    return float(v >> 8) / 16777215.0;
}

uniform float uSeed;
uniform float uAmplitude;
uniform float uFrequency;

float getH(vec2 p, int lod) {
    float h = 0.0, a = 0.6, freq = uFrequency, maxA = 0.0;
    uint seed = (uSeed == 0.0) ? 91u : uint(uSeed);
    int octaves = 6; // Always use 6 octaves to prevent circular LOD banding patterns
    
    for (int i = 0; i < octaves; i++) {
        vec2  sp = p * freq;
        ivec2 si = ivec2(floor(sp));
        vec2  sf = fract(sp);
        sf = sf * sf * (3.0 - 2.0 * sf);           // smoothstep
        float n00 = hf(uh(ivec3(si.x,   si.y,   i)), seed);
        float n10 = hf(uh(ivec3(si.x+1, si.y,   i)), seed);
        float n01 = hf(uh(ivec3(si.x,   si.y+1, i)), seed);
        float n11 = hf(uh(ivec3(si.x+1, si.y+1, i)), seed);
        h += a * mix(mix(n00, n10, sf.x), mix(n01, n11, sf.x), sf.y);
        maxA += a;
        a    *= 0.5;
        freq *= 2.1;
    }
    float ampCells = (uAmplitude == 0.0) ? 138.0 : (uAmplitude / CELL);
    return 32.0 + (h / maxA) * ampCells;
}

float getH(vec2 p) {
    return getH(p, 1);
}

// -- Surface color by elevation --
// sh:    terrain height at this XZ in cell units
// cellY: this voxel's Y cell index

vec3 surfaceColor(int cellY, float sh) {
    float ampCells = (uAmplitude == 0.0) ? 138.0 : (uAmplitude / CELL);
    if (cellY < int(sh) - 5) {
        // Deep underground: stone
        return vec3(0.38, 0.37, 0.35);
    } else if (cellY < int(sh) - 1) {
        // Subsurface layer: dirt
        return vec3(0.50, 0.38, 0.28);
    } else {
        // Top surface: biome by altitude scaled proportionally with amplitude
        float relativeH = sh - 32.0;
        
        float snowThresh      = ampCells * 0.89;
        float snowGrassThresh = ampCells * 0.78;
        float grassThresh     = ampCells * 0.42;
        float dirtGrassThresh = ampCells * 0.20;

        if (relativeH > snowThresh) {
            return vec3(0.93, 0.97, 1.00);                             // snow
        } else if (relativeH > snowGrassThresh) {
            float t = (relativeH - snowGrassThresh) / (snowThresh - snowGrassThresh + 1e-5);
            return mix(vec3(0.18, 0.55, 0.12), vec3(0.93, 0.97, 1.00), t); // snow-grass
        } else if (relativeH > grassThresh) {
            return vec3(0.18, 0.55, 0.12);                             // grass
        } else if (relativeH > dirtGrassThresh) {
            float t = (relativeH - dirtGrassThresh) / (grassThresh - dirtGrassThresh + 1e-5);
            return mix(vec3(0.50, 0.38, 0.28), vec3(0.18, 0.55, 0.12), t); // dirt->grass
        } else {
            return vec3(0.45, 0.42, 0.35);                             // bare rock/sand
        }
    }
}

// -- LOD voxel lookup --
// cell: raw voxel coordinate in CELL units
// lod:  merge factor (1 = full detail, 2 = 2x2x2 merged, ...)

bool getCubeLOD(ivec3 cell, int lod, out Cube c) {
    float flod    = float(lod);
    ivec3 lodCell = ivec3(floor(vec3(cell) / flod)) * lod;

    float height = getH(vec2(lodCell.xz), lod);
    if (float(lodCell.y) > height) return false;

    c.cen    = (vec3(lodCell) + flod * 0.5) * CELL;
    c.halfSz = CELL * flod * 0.5;

    // Average color and material properties across the merged footprint using elevation-based biomes and rare features
    vec3  colorSum = vec3(0.0);
    float emissiveSum = 0.0;
    float smoothnessSum = 0.0;
    float metallicSum = 0.0;
    float iorSum = 0.0;
    float weight   = 0.0;
    int   s        = max(1, lod / 2);
    for (int dx = 0; dx < lod; dx += s) {
        for (int dz = 0; dz < lod; dz += s) {
            ivec3 sc = lodCell + ivec3(dx, 0, dz);
            float sh = getH(vec2(sc.xz), lod);
            
            vec3  alb = surfaceColor(lodCell.y, sh);
            float em  = 0.0;
            float sm  = 0.0;
            float mt  = 0.0;
            float io  = 1.0;

            // Rare features can occur at any voxel coordinate inside the footprint
            if (lodCell.y < int(sh) - 1) {
                uint  h = uh(sc);
                float r = hf(h, 25u);
                if (r < 0.015) {
                    alb = vec3(1.0, 0.45, 0.0);
                    em  = 10.0;
                } else if (r < 0.04) {
                    alb = vec3(0.95, 0.78, 0.25);
                    sm  = 0.85;
                    mt  = 0.95;
                } else if (r < 0.05) {
                    alb = vec3(0.15, 0.75, 0.95);
                    sm  = 0.98;
                    io  = 1.6;
                }
            }

            colorSum      += alb;
            emissiveSum   += em;
            smoothnessSum += sm;
            metallicSum   += mt;
            iorSum        += io;
            weight        += 1.0;
        }
    }

    c.albedo     = colorSum / weight;
    c.emissive   = emissiveSum / weight;
    c.smoothness = smoothnessSum / weight;
    c.metallic   = metallicSum / weight;
    c.ior        = iorSum / weight;
    c.matType    = (c.emissive > 0.1) ? MAT_EMISSIVE : ((c.metallic > 0.1) ? MAT_METALLIC : ((c.ior > 1.05) ? MAT_GLASS : MAT_PLASTIC));

    // Per-voxel brightness variation: same type, subtle +-10% brightness.
    // Gives terrain a natural non-uniform look without changing the biome color.
    float bv = 0.9 + 0.2 * hf(uh(lodCell), 77u);
    c.albedo *= bv;

    return true;
}
