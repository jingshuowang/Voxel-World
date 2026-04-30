namespace Voxel.Rendering {
    public static class Config {
       
        public static long WorldWidth = 1L << 20;
        public static long WorldHeight = 1L << 12;
        public static long WorldDepth = 1L << 20;

        
        public static float RenderDistance = 1024.0f; 
        public static float ProcessDistance = 2.0f;
        public static int TickSpeed = 20;

        public static float LoDRadius = 64.0f; 
        
        // Performance & Features
        public static float ResolutionScale = 0.5f;
        public static int MaxSamples = 16;
        public static int MinSamples = 16;
        
        public static bool EnableTAA = true;
        public static bool EnableJitter = true;
        public static bool EnableLOD = true;
        public static float[] LodDistances = { 64.0f, 128.0f };
        public static float Exposure = 1.0f;
        public static float Contrast = 1.0f;
        public static float Saturation = 1.2f;
        public static float Brightness = 0.0f;
        public static float WhitePoint = 1.0f;
        public static float Gamma = 2.2f;

        // Derived values in voxels
        public static float RenderRadius => RenderDistance * 16.0f;
        public static float ProcessRadius => ProcessDistance * 16.0f;
    }
}
