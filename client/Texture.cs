using System.Numerics;

namespace Voxel.Rendering {
    public struct BlockMaterial {
        public Vector3 Color;
        public float Roughness;
        public float Brightness;
        public Vector2 UV; // Top-left UV in the spritesheet atlas

        public Vector4 ToGpuData() {
            // Encode material properties for the GPU:
            // X, Y: UV coordinates (or base color if no texture)
            // Z: Roughness
            // W: Brightness (Emissive intensity)
            return new Vector4(UV.X, UV.Y, Roughness, Brightness);
        }
    }

    public static class MaterialRegistry {
        public static readonly BlockMaterial Air = new() { Color = Vector3.Zero, Roughness = 0, Brightness = 0, UV = Vector2.Zero};
        
        public static readonly BlockMaterial Grass = new() { 
            Color = new(0.2f, 0.6f, 0.2f), 
            Roughness = 0.9f, 
            Brightness = 0.0f, 
            UV = new(0.0f, 0.0f) 
        };

        public static readonly BlockMaterial Stone = new() { 
            Color = new(0.5f, 0.5f, 0.5f), 
            Roughness = 0.7f, 
            Brightness = 0.0f, 
            UV = new(0.1f, 0.0f) 
        };

        public static readonly BlockMaterial Glowstone = new() { 
            Color = new(1.0f, 0.8f, 0.4f), 
            Roughness = 0.5f, 
            Brightness = 2.0f, 
            UV = new(0.2f, 0.0f) 
        };
    }
}
