using System.Numerics;
using System.Collections.Concurrent;
using Voxel.World;

namespace Voxel.Server {
    /// <summary>
    /// World: Server-side world state and terrain generation.
    /// The server is authoritative -- clients receive voxel data via the network.
    /// Generation parameters come from WConfig (seed, octaves, amplitude, etc.).
    /// </summary>
    public class World {
        public static long Width  => WConfig.WorldWidth;
        public static long Height => WConfig.WorldHeight;
        public static long Depth  => WConfig.WorldDepth;

        public const int LocalSize = 32;

        private ConcurrentDictionary<Vector3, Vector4> _modifications = new();

        public void SetModification(Vector3 pos, Vector4 val) {
            _modifications[new Vector3(MathF.Floor(pos.X), MathF.Floor(pos.Y), MathF.Floor(pos.Z))] = val;
        }
        public void ClearModifications() => _modifications.Clear();

        public World() { }

        public bool IsSolid(Vector3 pos) {
            if (pos.Y < 0 || pos.Y >= Height) return pos.Y < 0;
            return GetVoxel(pos).W > 0.0f;
        }

        public static bool IsWithinProcessDistance(Vector3 playerPos, Vector3 targetPos) =>
            Vector3.Distance(playerPos, targetPos) <= WConfig.ProcessRadius;

        public Vector4 GetVoxel(Vector3 pos) {
            Vector3 fp = new(MathF.Floor(pos.X), MathF.Floor(pos.Y), MathF.Floor(pos.Z));

            if (_modifications.TryGetValue(fp, out var val)) {
                if (val.W < 0.0f) return Vector4.Zero;
                return val;
            }

            float h = GetHeight(fp.X, fp.Z);

            float relativeH = h - 20.0f; // Base height is 20 in GetHeight
            float amp = WConfig.Amplitude;

            if (fp.Y <= h) {
                // Subsurface
                if (fp.Y < h - 5.0f) return UI.BlockRegistry.Stone.ToGpuData();
                if (fp.Y < h - 1.0f) return UI.BlockRegistry.Dirt.ToGpuData();
                
                // Surface biomes scaled by amplitude
                if (relativeH > amp * 0.75f) return UI.BlockRegistry.Snow.ToGpuData();
                if (relativeH > amp * 0.25f) return UI.BlockRegistry.Grass.ToGpuData();
                return UI.BlockRegistry.Dirt.ToGpuData(); // Lowlands
            }

            // Trees (spawn on grass biome)
            float ys = MathF.Floor(h);
            bool isGrass = (relativeH > amp * 0.25f) && (relativeH <= amp * 0.75f);

            // 1. Check if CURRENT column is a trunk
            if (isGrass && HasTree(fp.X, fp.Z)) {
                if (fp.Y >= ys + 1f && fp.Y <= ys + 3f) return UI.BlockRegistry.Wood.ToGpuData();
            }

            // 2. Check if current block is part of a canopy from a nearby tree
            for (int dx = -1; dx <= 1; dx++) {
                for (int dz = -1; dz <= 1; dz++) {
                    float nx = fp.X + dx;
                    float nz = fp.Z + dz;
                    float nh = GetHeight(nx, nz);
                    float nys = MathF.Floor(nh);
                    float nRelH = nh - 20.0f;
                    bool nIsGrass = (nRelH > amp * 0.25f) && (nRelH <= amp * 0.75f);

                    if (nIsGrass && HasTree(nx, nz)) {
                        // Leaves spawn at height nys + 4 in a 3x3 area
                        if (fp.Y == nys + 4f) return UI.BlockRegistry.Leaves.ToGpuData();
                        // Peak of the tree on top of the trunk
                        if (fp.Y == nys + 5f && dx == 0 && dz == 0) return UI.BlockRegistry.Leaves.ToGpuData();
                    }
                }
            }

            return UI.BlockRegistry.Air.ToGpuData();
        }

        private static bool HasTree(float x, float z) {
            int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z);
            uint h = (uint)(ix * 73856093 ^ iz * 19349663) * 2246822519u;
            h ^= h >> 13; h *= 0x45d9f3bu; h ^= h >> 16;
            return (h & 0xFFF) < 100;
        }

        public static float GetHeight(float x, float z) {
            if (WConfig.Seed == 0) return 30.0f;

            Vector2 p = new Vector2(x, z) * WConfig.Frequency * WConfig.VoxelSize;

            if (WConfig.WarpStrength > 0) {
                Vector2 warp = new Vector2(
                    FBM(p + new Vector2(0.0f, 0.0f), (uint)WConfig.Seed),
                    FBM(p + new Vector2(5.2f, 1.3f), (uint)WConfig.Seed));
                p += warp * WConfig.WarpStrength;
            }

            float v = FBM(p, (uint)WConfig.Seed);

            if (WConfig.Ridged)   v = 1.0f - MathF.Abs(v * 2.0f - 1.0f);
            v = MathF.Pow(v, WConfig.Exponent);
            if (WConfig.Terraced) v = MathF.Round(v * WConfig.TerraceSteps) / WConfig.TerraceSteps;

            return 20.0f + v * WConfig.Amplitude;
        }

        private static float FBM(Vector2 p, uint seed) {
            float v = 0f, a = 0.5f, freq = 1f, maxAmp = 0f;
            Vector2 shift = new Vector2(100f, 100f);
            for (int i = 0; i < WConfig.Octaves; i++) {
                v     += a * ValueNoise(p * freq, seed);
                maxAmp += a;
                p    += shift;
                freq *= WConfig.Lacunarity;
                a    *= WConfig.Persistence;
            }
            return v / maxAmp;
        }

        private static float ValueNoise(Vector2 p, uint seed) {
            int ix = (int)MathF.Floor(p.X), iy = (int)MathF.Floor(p.Y);
            float fx = p.X - ix, fy = p.Y - iy;
            float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
            return Lerp(Lerp(Hash(ix,   iy,   seed), Hash(ix+1, iy,   seed), ux),
                        Lerp(Hash(ix,   iy+1, seed), Hash(ix+1, iy+1, seed), ux), uy);
        }

        private static float Hash(int x, int y, uint seed) {
            uint h = (uint)x * 73856093U ^ (uint)y * 83492791U ^ seed;
            h = (h ^ (h >> 16)) * 0x85ebca6b;
            h = (h ^ (h >> 13)) * 0xc2b2ae35;
            h ^= (h >> 16);
            return (float)h / 4294967295.0f;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        public void SetVoxel(Vector3 pos, Vector4 val, Vector3 playerPos) {
            if (!IsWithinProcessDistance(playerPos, pos)) return;
            _modifications[new Vector3(MathF.Floor(pos.X), MathF.Floor(pos.Y), MathF.Floor(pos.Z))] = val;
        }

        public void FillLocalBuffer(float[] buffer, Vector3 center) {
            Array.Clear(buffer, 0, buffer.Length);
            int half = LocalSize / 2;
            int sx = (int)MathF.Floor(center.X) - half;
            int sy = (int)MathF.Floor(center.Y) - half;
            int sz = (int)MathF.Floor(center.Z) - half;
            foreach (var kvp in _modifications) {
                int rx = (int)kvp.Key.X - sx, ry = (int)kvp.Key.Y - sy, rz = (int)kvp.Key.Z - sz;
                if (rx < 0 || rx >= LocalSize || ry < 0 || ry >= LocalSize || rz < 0 || rz >= LocalSize) continue;
                int idx = (rx + ry * LocalSize + rz * LocalSize * LocalSize) * 4;
                buffer[idx] = kvp.Value.X; buffer[idx+1] = kvp.Value.Y;
                buffer[idx+2] = kvp.Value.Z; buffer[idx+3] = kvp.Value.W;
            }
        }
    }
}
