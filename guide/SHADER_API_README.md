# Shader & Lighting API - Future Implementation Notes

This document preserves the lighting/rendering logic that was stripped from the engine
for future reimplementation as a proper shader API.

---

## Removed Features (Save for Later)

### 1. Directional Lighting (Sun/Moon)
The fragment shader computed per-pixel diffuse lighting:
```glsl
vec3 norm = normalize(Normal);
float diff = max(dot(norm, sunDir), 0.0);
vec3 diffuse = diff * sunColor;
vec3 lighting = (ambientColor + diffuse) * blockColor * faceShade;
```
- `sunDir` = normalized direction from camera to sun
- `sunColor` = warm yellow for day `(1.0, 0.95, 0.8)`, blue for night `(0.2, 0.4, 0.7)`
- `ambientColor` = sky color * 0.6 with minimum `(0.12, 0.12, 0.20)` to prevent total darkness

### 2. Face Shading (Ambient Occlusion)
add later

### 3. Distance Fog
add later
### 4. Sun/Moon Glow Shader (sky.fs)
Celestial bodies rendered as additive-blended quads with:
```glsl
float core = smoothstep(0.08, 0.05, dist);       // Hard circle center
float corona = exp(-dist * 4.0) * 1.5;            // Bright inner glow
float atmosphere = exp(-dist * 0.8) * 0.7;        // Soft outer haze
vec3 finalColor = coreColor * core + glowTint * (corona + atmosphere);
```
- Sun: `coreColor=(1.0, 0.98, 0.85)`, `glowTint=(1.0, 0.85, 0.2)`
- Moon: `coreColor=(0.7, 0.75, 0.95)`, `glowTint=(0.4, 0.5, 0.9)`
- Rendered with `GL_ONE, GL_ONE` additive blending

### 5. Day/Night Sky Interpolation
```java
if (sunDir.y > 0.2f) currentSky = daySky;
else if (sunDir.y > -0.2f) currentSky = lerp(nightSky, daySky, (sunDir.y + 0.2) / 0.4);
else currentSky = nightSky;
```
- Day: `(0.5, 0.8, 1.0)` - bright sky blue
- Night: `(0.02, 0.02, 0.08)` - near black

### 6. Sun Orbital Path
```java
float sunRadius = 8192.0f;
float sunY = camera.y + sin(dayTime) * sunRadius;  // Flat circle orbit
float sunX = camera.x + cos(dayTime) * sunRadius;
float sunZ = camera.z;  // Fixed Z axis
```
Moon is geometrically opposite: `moonPos = camera - (sunPos - camera)`

### 7. Shadow Mapping (FBO Depth Pass) - Already Removed
Was a 2-pass system:
1. Render depth from sun's perspective to FBO texture
2. Sample shadow map in chunk.fs to darken occluded fragments
- Resolution: 2048x2048
- Used `GL_CLAMP_TO_BORDER` with white border

---

## Vertex Format (12 floats per vertex)
```
Position:   3 floats (x, y, z)
TexCoords:  2 floats (u, v)
Normal:     3 floats (nx, ny, nz)
FaceShade:  1 float  (shade multiplier)
BlockColor: 3 floats (r, g, b)
```
Stride = 12 * 4 = 48 bytes per vertex

## API Design Ideas
- `ShaderAPI.setLighting(sunDir, sunColor, ambientColor)`
- `ShaderAPI.setFog(fogEnd, fogColor)`
- `ShaderAPI.enableShadows(resolution, cascades)`
- `ShaderAPI.setCelestial(sunPos, moonPos, glowParams)`
