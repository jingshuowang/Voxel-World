#version 330 core
in vec3 BlockColor;
out vec4 FragColor;

void main() {
    FragColor = vec4(BlockColor, 1.0);
}
