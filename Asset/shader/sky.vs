#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec2 aTexCoord;
out vec2 TexCoord;
uniform mat4 projectionMatrix;
uniform mat4 viewMatrix;
void main() {
    TexCoord = aTexCoord;
    gl_Position = projectionMatrix * viewMatrix * vec4(aPos, 1.0);
}
