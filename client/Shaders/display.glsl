#version 460 core
uniform sampler2D uTex;
in vec2 vUV;
out vec4 o;

uniform float uExposure;
uniform float uContrast;
uniform float uSaturation;
uniform float uBrightness;
uniform float uWhitePoint;
uniform float uGamma;

#include "tonemap.glsl"
#include "gamma.glsl"

void main() {
    vec3 c = texture(uTex, vUV).rgb;
    c = applyTonemap(c, uExposure, uContrast, uSaturation, uBrightness, uWhitePoint);
    c = applyGamma(c, uGamma);
    o = vec4(c, 1.0);
}
