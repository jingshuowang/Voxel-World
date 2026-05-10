// bloom.glsl  --  ONE pass of a separable Gaussian blur.
// This shader is dispatched TWICE from C# (C# controls axis via uHorizontal):
//   Pass 1: uHorizontal=1  reads imgEmissive, writes imgBloom  (horizontal blur)
//   Pass 2: uHorizontal=0  reads imgBloom,    writes imgBloom  (vertical   blur)
// Result: a mathematically correct 2D Gaussian with zero artifacts.
// CURRENTLY DISABLED (BloomIntensity = 0). Do not enable until ready.

#version 460 core
layout(local_size_x = 8, local_size_y = 8) in;
layout(rgba32f, binding = 0) uniform image2D imgSrc;   // read
layout(rgba32f, binding = 1) uniform image2D imgDst;   // write

uniform int   uHorizontal; // 1 = horizontal pass, 0 = vertical pass
uniform float uThreshold;  // HDR threshold above which pixels are allowed to bloom (typically 1.0)

// 13-tap Gaussian weights (sigma ~= 4, radius 6).
// Normalized so sum(weights) = 1 for a single axis.
const float W[7] = float[7](0.227027, 0.194595, 0.121621, 0.054054, 0.016216, 0.003243, 0.000405);

vec3 sampleBright(ivec2 p, ivec2 sz) {
    p = clamp(p, ivec2(0), sz - 1);
    vec3 col = imageLoad(imgSrc, p).rgb;
    // Threshold: only pass through pixels brighter than uThreshold
    return max(vec3(0.0), col - uThreshold);
}

void main() {
    ivec2 px = ivec2(gl_GlobalInvocationID.xy);
    ivec2 sz = imageSize(imgSrc);
    if (px.x >= sz.x || px.y >= sz.y) return;

    ivec2 dir = (uHorizontal == 1) ? ivec2(1, 0) : ivec2(0, 1);

    // Center tap
    vec3 acc = sampleBright(px, sz) * W[0];

    // Symmetric taps
    for (int r = 1; r < 7; r++) {
        acc += sampleBright(px + dir * r, sz) * W[r];
        acc += sampleBright(px - dir * r, sz) * W[r];
    }

    imageStore(imgDst, px, vec4(acc, 1.0));
}
