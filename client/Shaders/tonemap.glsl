// Applies exposure, contrast, saturation, brightness, and whitePoint to linear HDR.

vec3 applyTonemap(vec3 color, float exposure, float contrast, float saturation, float brightness, float whitePoint) {
    color = color * exposure + brightness;

    // ACES film curve
    float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
    color = clamp((color * (a * color + b)) / (color * (c * color + d) + e), 0.0, whitePoint);

    color = clamp(pow(color, vec3(contrast)), 0.0, 1.0);

    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    color = mix(vec3(luma), color, saturation);

    return clamp(color, 0.0, 1.0);
}
