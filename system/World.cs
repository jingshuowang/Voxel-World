using System.Numerics;

namespace Voxel {
    public class World {
        public const int Size = 128;
        public float[] Voxels = new float[Size * Size * Size * 4];

        public World() {
            Generate();
        }

        public void Generate() {
            Random rand = new Random();
            for (int x = 0; x < Size; x++) {
                for (int z = 0; z < Size; z++) {
                    // Simple Procedural Terrain
                    float height = 20 + 15 * MathF.Sin(x * 0.1f) * MathF.Cos(z * 0.1f);
                    height += 5 * MathF.Sin(x * 0.3f + z * 0.2f);
                    
                    for (int y = 0; y < Size; y++) {
                        int idx = (x + y * Size + z * Size * Size) * 4;
                        
                        if (y < height) {
                            // Grassy top, Dirt below
                            if (y > height - 1) {
                                Voxels[idx] = 0.2f; Voxels[idx+1] = 0.6f; Voxels[idx+2] = 0.2f; // Green
                            } else {
                                Voxels[idx] = 0.4f; Voxels[idx+1] = 0.3f; Voxels[idx+2] = 0.2f; // Brown
                            }
                            Voxels[idx + 3] = 0.0f;
                        }
                    }
                }
            }
        }
    }
}
