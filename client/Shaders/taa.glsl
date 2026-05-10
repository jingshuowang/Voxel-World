#version 460 core
layout(local_size_x = 8, local_size_y = 8) in;
layout(rgba32f, binding = 0) readonly  uniform image2D imgColor;
layout(rgba32f, binding = 1) readonly  uniform image2D imgPos;
layout(rgba32f, binding = 2) readonly  uniform image2D imgBloom;
layout(rgba32f, binding = 3) readonly  uniform image2D imgHistory;
layout(rgba32f, binding = 4) writeonly uniform image2D imgResolved;

uniform vec3  uCamPos,  uCamFwd,  uCamRgt,  uCamUp;
uniform vec3  uPrevPos, uPrevFwd, uPrevRgt, uPrevUp;
uniform float uFov, uPrevFov, uFrame;
uniform float uBloomIntensity;

bool reproject(vec3 worldP, ivec2 sz, out vec2 uv) {
    vec3  rel    = worldP - uPrevPos;
    float depth  = dot(rel, uPrevFwd);
    if (depth < 0.01) return false;
    float aspect = float(sz.x) / float(sz.y);
    float nx = dot(rel, uPrevRgt) / (depth * uPrevFov * aspect);
    float ny = dot(rel, uPrevUp)  / (depth * uPrevFov);
    uv = vec2(nx, ny) * 0.5 + 0.5;
    return uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0;
}

vec3 historyBilinear(vec2 uv, ivec2 sz) {
    vec2  fp = uv * vec2(sz) - 0.5;
    ivec2 p  = ivec2(floor(fp));
    vec2  f  = fract(fp);
    vec3 c00 = imageLoad(imgHistory, clamp(p,             ivec2(0), sz-1)).rgb;
    vec3 c10 = imageLoad(imgHistory, clamp(p+ivec2(1,0), ivec2(0), sz-1)).rgb;
    vec3 c01 = imageLoad(imgHistory, clamp(p+ivec2(0,1), ivec2(0), sz-1)).rgb;
    vec3 c11 = imageLoad(imgHistory, clamp(p+ivec2(1,1), ivec2(0), sz-1)).rgb;
    return mix(mix(c00, c10, f.x), mix(c01, c11, f.x), f.y);
}

vec3 neighbourClamp(vec3 history, ivec2 px, ivec2 sz) {
    vec3 lo = vec3(1e9), hi = vec3(-1e9);
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++) {
        vec3 c = imageLoad(imgColor, clamp(px+ivec2(x,y), ivec2(0), sz-1)).rgb;
        lo = min(lo, c); hi = max(hi, c);
    }
    return clamp(history, lo, hi);
}

void main() {
    ivec2 px = ivec2(gl_GlobalInvocationID.xy);
    ivec2 sz = imageSize(imgColor);
    if (px.x >= sz.x || px.y >= sz.y) return;

    vec3 current = imageLoad(imgColor, px).rgb;
    vec3 bloom   = imageLoad(imgBloom, px).rgb;
    vec4 posData = imageLoad(imgPos,   px);

    vec3 worldP = (posData.w > 0.5) ? posData.xyz : uCamPos + uCamFwd * 1000.0;

    vec3 resolved;
    vec2 prevUV;
    if (reproject(worldP, sz, prevUV)) {
        vec3 history = historyBilinear(prevUV, sz);
        history  = neighbourClamp(history, px, sz);
        float alpha = (uFrame <= 2.0) ? 1.0 : 0.1;
        resolved = mix(history, current, alpha);
    } else {
        resolved = current;
    }

    // Bloom added after TAA so it does not ghost
    resolved += bloom * uBloomIntensity;

    imageStore(imgResolved, px, vec4(resolved, 1.0));
}
