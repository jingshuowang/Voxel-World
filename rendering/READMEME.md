# Rendering Module

Basic rendering. GPU calls via OpenGL.

## Files
- **Shader.java** - GLSL shader compilation and uniform management
- **Camera.java** - View/projection matrices, position, FOV
- **Texture.java** - Texture loading (currently unused, white pixel instead)

## Shader Files (Asset/shader/)
- **chunk.vs / chunk.fs** - Block rendering (flat color, no lighting)
- **sky.vs / sky.fs** - Sky sun/moon circle drawing

## Responsibilities
- Block mesh rendering (flat vertex colors)
- Sky basic rendering
- sky objects rendering sun and moon which is just circle for now.
-rendering daylight cycle
-
