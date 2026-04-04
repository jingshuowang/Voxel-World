#version 460 compatibility
layout (location=0) in vec3 aPos;
layout (location=2) in vec3 aNormal;
layout (location=4) in vec3 aColor;
out vec3 BlockColor;
out vec3 Normal;
out float clipDepth;
uniform mat4 modelMatrix;
uniform mat4 viewMatrix;
uniform mat4 projectionMatrix;
void main() {
    vec4 worldP = modelMatrix * vec4(aPos, 1.0);
    Normal = aNormal;
    gl_Position = projectionMatrix * viewMatrix * worldP;
    clipDepth = gl_Position.z / gl_Position.w;
    BlockColor = aColor;
}
