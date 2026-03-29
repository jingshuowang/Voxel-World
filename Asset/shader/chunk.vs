#version 330 core
layout (location=0) in vec3 aPos;
layout (location=4) in vec3 aColor;

out vec3 BlockColor;

uniform mat4 modelMatrix;
uniform mat4 viewMatrix;
uniform mat4 projectionMatrix;

void main() {
    gl_Position = projectionMatrix * viewMatrix * modelMatrix * vec4(aPos, 1.0);
    BlockColor = aColor;
}
