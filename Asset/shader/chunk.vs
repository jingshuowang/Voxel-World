#version 460 core

layout(location=0) in uint packedData; // per-instance (one packed int per face quad)

// Chunk world offsets, one per draw call, indexed by gl_DrawID
layout(std430, binding=0) readonly buffer ChunkPositions {
    vec4 offsets[]; // xyz = chunk world position (chunkX/Y/Z * 16), w = unused
};

out vec3 BlockColor;
out vec3 Normal;
out float clipDepth;

uniform mat4 viewMatrix;
uniform mat4 projectionMatrix;
uniform int  renderMode;
uniform float dayBrightness;

// Normals: 0=+Z, 1=-Z, 2=+Y, 3=-Y, 4=+X, 5=-X
const vec3 NORMALS[6] = vec3[](
    vec3( 0,  0,  1),
    vec3( 0,  0, -1),
    vec3( 0,  1,  0),
    vec3( 0, -1,  0),
    vec3( 1,  0,  0),
    vec3(-1,  0,  0)
);

const float SHADE[6] = float[](0.8, 0.8, 1.0, 0.5, 0.75, 0.75);

vec3 getColor(uint type) {
    if(type == 1u) return vec3(0.25, 0.55, 0.15); // Grass
    if(type == 2u) return vec3(0.45, 0.28, 0.12); // Dirt
    if(type == 3u) return vec3(0.50, 0.50, 0.50); // Stone
    if(type == 4u) return vec3(0.92, 0.95, 0.98); // Snow
    if(type == 5u) return vec3(0.15, 0.35, 0.65); // Water
    if(type == 6u) return vec3(0.40, 0.26, 0.13); // Wood
    if(type == 7u) return vec3(0.18, 0.50, 0.10); // Leaves
    return vec3(1.0, 0.0, 1.0);                   // Error magenta
}

void main() {
    // Unpack bit fields (layout: lllllwwwwwtttttttfffzzzzzyyyyyxxxxx)
    uint x    =  packedData        & 0x1Fu;
    uint y    = (packedData >>  5) & 0x1Fu;
    uint z    = (packedData >> 10) & 0x1Fu;
    uint face = (packedData >> 15) & 0x7u;
    uint type = (packedData >> 18) & 0x3Fu;
    float fw  = float(((packedData >> 24) & 0xFu) + 1u);
    float fl  = float(((packedData >> 28) & 0xFu) + 1u);

    // gl_VertexID: 0,1,2,3 for the 4 strip vertices per instance.
    // Z-order UV: (0,0),(1,0),(0,1),(1,1) -> correct CCW winding for all 6 faces.
    float uvx = float(gl_VertexID & 1);  // 0,1,0,1
    float uvy = float(gl_VertexID >> 1); // 0,0,1,1

    float bx = float(x), by = float(y), bz = float(z);

    // Per-face corner formulas (verified against original CORNERS[24] for w=l=1)
    vec3 localPos;
    if      (face == 0u) localPos = vec3(bx + uvx*fw,        by + uvy*fl,        bz + 1.0        ); // +Z
    else if (face == 1u) localPos = vec3(bx + (1.0-uvx)*fw,  by + uvy*fl,        bz              ); // -Z
    else if (face == 2u) localPos = vec3(bx + uvx*fw,        by + 1.0,           bz + (1.0-uvy)*fl); // +Y
    else if (face == 3u) localPos = vec3(bx + uvx*fw,        by,                 bz + uvy*fl     ); // -Y
    else if (face == 4u) localPos = vec3(bx + 1.0,           by + uvy*fw,        bz + (1.0-uvx)*fl); // +X
    else                 localPos = vec3(bx,                  by + uvy*fw,        bz + uvx*fl     ); // -X

    // gl_DrawID = which chunk this draw belongs to (0-indexed in MDI call)
    vec3 chunkWorld = offsets[gl_DrawID].xyz;
    vec3 worldPos   = localPos + chunkWorld;

    Normal     = NORMALS[face];
    BlockColor = getColor(type) * SHADE[face];

    gl_Position = projectionMatrix * viewMatrix * vec4(worldPos, 1.0);
    clipDepth   = gl_Position.z / gl_Position.w;
}
