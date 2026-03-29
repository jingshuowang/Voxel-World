#version 330 core
in vec2 TexCoord;
out vec4 FragColor;
uniform vec3 bodyColor;

void main() {
    float dist = length(TexCoord);
    // Simple solid circle - sharp edge
    float circle = smoothstep(0.12, 0.08, dist);
    if (circle < 0.01) discard;
    FragColor = vec4(bodyColor * circle, 1.0);
}
