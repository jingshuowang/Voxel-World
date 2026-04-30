using System;
using System.Numerics;

namespace Voxel.System {
    public struct AABB {
        public Vector3 Min;
        public Vector3 Max;

        public AABB(Vector3 min, Vector3 max) {
            Min = min;
            Max = max;
        }
    }

    public class Entity {
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public Vector3 Size { get; set; }
        public bool IsOnGround { get; set; }
    }
}
