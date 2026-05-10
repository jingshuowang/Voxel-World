using System.Numerics;

namespace Voxel.Rendering
{
    public static class Config
    {
        // -- Render distance / LOD (in 32x32x32 chunks; 1 chunk = 256 world units) --
        public static int RenderDistance { get; set; } = 81920;
        public static float LodThreshold { get; set; } = 512.0f;
        public static float LodScale { get; set; } = 2.0f;

        // -- Fog (in chunks) --
        public static float FogStart { get; set; } = 64.0f;
        public static bool EnableFog { get; set; } = true;

        // -- Sun --
        public static Vector3 SunColor { get; set; } = new Vector3(1.5f, 1.0f, 0.35f);
        public static float SunBrightness { get; set; } = 4.0f; //BLOOM DEPENDS ON BRIGHTNESS 

        // -- Ambient lighting --
        public static float AmbientBase { get; set; } = 0.08f;
        public static float AmbientSlope { get; set; } = 0.05f;

        // -- Clouds --
        public static float CloudAltitude { get; set; } = 3200.0f;
        public static float CloudCover { get; set; } = 0.55f;
        public static float CloudScale { get; set; } = 0.0003f;

        // -- Bloom --
        public static float BloomIntensity { get; set; } = 0.5f;

        // -- Resolution / TAA --
        public static float ResolutionScale { get; set; } = 0.5f;
        public static bool EnableTAA { get; set; } = true;

        // -- Post-processing --
        public static float Exposure { get; set; } = 1.5f;
        public static float Contrast { get; set; } = 1.4f;
        public static float Saturation { get; set; } = 1.3f;
        public static float Brightness { get; set; } = 0.0f;
        public static float WhitePoint { get; set; } = 1.0f;
        public static float Gamma { get; set; } = 2.2f;
    }
}
