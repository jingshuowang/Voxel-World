namespace Voxel.World
{
    /// <summary>
    /// WConfig: World configuration -- dimensions, chunk sizing, and generation parameters.
    /// Owned by the Server (authoritative world state). Client reads these via the network handshake.
    /// </summary>
    public static class WConfig
    {
        // -- World dimensions (in voxels) --
        public static long WorldWidth { get; set; } = 1L << 20;
        public static long WorldHeight { get; set; } = 1L << 12;
        public static long WorldDepth { get; set; } = 1L << 20;

        // -- Chunk / voxel sizing --
        public static int ChunkSize { get; set; } = 32;       // voxels per chunk axis
        public static float VoxelSize { get; set; } = 1.0f / 16.0f;
        public static int ProcessRadius { get; set; } = 64;     // chunk radius the server keeps active

        // -- World generation (server sets these; seed is sent to client on join) --
        public static int Seed { get; set; } = 1;
        public static int Octaves { get; set; } = 8;
        public static float Frequency { get; set; } = 0.003125f;
        public static float Amplitude { get; set; } = 3072.0f;
        public static float Lacunarity { get; set; } = 5.0f;
        public static float Persistence { get; set; } = 0.2f;
        public static float WarpStrength { get; set; } = 0.0f;
        public static bool Ridged { get; set; } = true;
        public static float Exponent { get; set; } = 3.0f;
        public static bool Terraced { get; set; } = true;
        public static float TerraceSteps { get; set; } = 4.0f;
    }
}
