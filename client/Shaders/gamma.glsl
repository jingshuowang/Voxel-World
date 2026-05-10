vec3 applyGamma(vec3 color, float gamma) {
    return pow(max(color, 0.0), vec3(1.0 / gamma));
}
