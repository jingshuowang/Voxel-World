# Shader & Lighting API - Future Implementation Notes

This document preserves the lighting/rendering logic that was stripped from the engine
for future reimplementation as a proper shader API.

---

## Removed Features (Save for Later)

### 1. Directional Lighting (Sun/Moon)
later

### 2. Face Shading (Ambient Occlusion)
add later

### 3. Distance Fog
add later
### 4. Glowing:
Works by letting brightness go past 1 (normally 1 to 0 0 is black 1 is white) by making the glow leak into suroundings which is know as bloom




## API Design Ideas
- `ShaderAPI.setLighting(sunDir, sunColor, ambientColor)`
- `ShaderAPI.setFog(fogEnd, fogColor)`
- `ShaderAPI.enableShadows(resolution, cascades)`
- `ShaderAPI.setCelestial(sunPos, moonPos, glowParams)`
