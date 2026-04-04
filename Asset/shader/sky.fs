#version 460 compatibility
in vec2 TexCoord;
out vec4 FragColor;
uniform vec3 bodyColor;
void main() {
    float dist = length(TexCoord);
    if (dist > 0.1) discard;
    FragColor = vec4(bodyColor, 1.0);
}
