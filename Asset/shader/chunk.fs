#version 460 compatibility
in vec3 BlockColor;
in vec3 Normal;
in float clipDepth;
out vec4 FragColor;
uniform int renderMode;
void main() {
    float alpha = 1.0;
    if (renderMode == 1) {
        // Normals visualization
        vec3 n = normalize(Normal) * 0.5 + 0.5;
        FragColor = vec4(n, alpha);
    } else if (renderMode == 2) {
        // Depth buffer visualization (reversed-Z: 1.0=near, 0.0=far)
        float d = clipDepth;
        // Nearby stays bright; far falls off dark while keeping reversed-Z mapping.
        float viz = clamp(pow(d, 32.0), 0.0, 1.0);
        FragColor = vec4(vec3(viz), alpha);
    } else {
        // Normal lit rendering
        vec3 lightDir = normalize(vec3(0.5, 1.0, 0.5));
        float ambient = 0.4;
        float diff = max(dot(normalize(Normal), lightDir), 0.0);
        float lighting = ambient + diff * 0.6;
        FragColor = vec4(BlockColor * lighting, alpha);
    }
}
