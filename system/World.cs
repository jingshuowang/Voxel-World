using System.Numerics;
using System.Collections.Concurrent;
using Voxel.Rendering;

namespace Voxel {
    public class World {
        public static long Width => Config.WorldWidth;
        public static long Height => Config.WorldHeight;
        public static long Depth => Config.WorldDepth;

        public const int LocalSize = 32; 
        
        private ConcurrentDictionary<Vector3, Vector4> _modifications = new();

        public static uint Seed = 1;

        public World() { }

        public bool IsSolid(Vector3 pos) {
            if (pos.Y < 0 || pos.Y >= Height) return pos.Y < 0;
            return GetVoxel(pos).W > 0.0f;
        }

        public bool IsWithinProcessDistance(Vector3 playerPos, Vector3 targetPos) {
            return Vector3.Distance(playerPos, targetPos) <= Config.ProcessRadius;
        }

        public Vector4 GetVoxel(Vector3 pos) {
            Vector3 floorPos = new(MathF.Floor(pos.X), MathF.Floor(pos.Y), MathF.Floor(pos.Z));
            
            if (_modifications.TryGetValue(floorPos, out var val)) {
                if (val.W < 0.0f) return Vector4.Zero; 
                return val;
            }

            // Procedural FBM logic matches shader exactly
            float fbmVal = FBM(new Vector2(floorPos.X, floorPos.Z) * 0.01f, Seed);
            float h = 20.0f + fbmVal * 30.0f;
            
            if (floorPos.Y <= h) {
                if (floorPos.Y < h - 4.0f) return MaterialRegistry.Stone.ToGpuData();
                return MaterialRegistry.Grass.ToGpuData();
            }
            return MaterialRegistry.Air.ToGpuData();
        }

        private float FBM(Vector2 p, uint seed) {
            float v = 0.0f;
            float a = 0.5f;
            Vector2 shift = new Vector2(100.0f, 100.0f);
            for (int i = 0; i < 4; i++) {
                v += a * ValueNoise(p, seed);
                p = p * 2.0f + shift;
                a *= 0.5f;
            }
            return v;
        }

        private float ValueNoise(Vector2 p, uint seed) {
            int ix = (int)MathF.Floor(p.X);
            int iy = (int)MathF.Floor(p.Y);
            float fx = p.X - ix;
            float fy = p.Y - iy;

            float ux = fx * fx * (3.0f - 2.0f * fx);
            float uy = fy * fy * (3.0f - 2.0f * fy);

            float a = Hash(ix, iy, seed);
            float b = Hash(ix + 1, iy, seed);
            float c = Hash(ix, iy + 1, seed);
            float d = Hash(ix + 1, iy + 1, seed);

            return Lerp(Lerp(a, b, ux), Lerp(c, d, ux), uy);
        }

        private float Hash(int x, int y, uint seed) {
            uint h = (uint)x * 73856093U ^ (uint)y * 83492791U ^ seed;
            h = (h ^ (h >> 16)) * 0x85ebca6b;
            h = (h ^ (h >> 13)) * 0xc2b2ae35;
            h ^= (h >> 16);
            return (float)h / 4294967295.0f;
        }

        private float Lerp(float a, float b, float t) => a + (b - a) * t;

        public void SetVoxel(Vector3 pos, Vector4 val, Vector3 playerPos) {
            if (!IsWithinProcessDistance(playerPos, pos)) return;
            Vector3 floorPos = new(MathF.Floor(pos.X), MathF.Floor(pos.Y), MathF.Floor(pos.Z));
            _modifications[floorPos] = val;
        }

        public void FillLocalBuffer(float[] buffer, Vector3 center) {
            Array.Clear(buffer, 0, buffer.Length);

            int half = LocalSize / 2;
            int startX = (int)MathF.Floor(center.X) - half;
            int startY = (int)MathF.Floor(center.Y) - half;
            int startZ = (int)MathF.Floor(center.Z) - half;

            Program.RecordTimer("FillLocalBuffer: Start");
            foreach (var kvp in _modifications) {
                int rx = (int)kvp.Key.X - startX;
                int ry = (int)kvp.Key.Y - startY;
                int rz = (int)kvp.Key.Z - startZ;

                if (rx >= 0 && rx < LocalSize && ry >= 0 && ry < LocalSize && rz >= 0 && rz < LocalSize) {
                    int idx = (rx + ry * LocalSize + rz * LocalSize * LocalSize) * 4;
                    buffer[idx] = kvp.Value.X;
                    buffer[idx + 1] = kvp.Value.Y;
                    buffer[idx + 2] = kvp.Value.Z;
                    buffer[idx + 3] = kvp.Value.W;
                }
            }
            Program.RecordTimer("FillLocalBuffer: End");
        }
    }
}
