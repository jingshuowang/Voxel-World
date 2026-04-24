using System.Numerics;

namespace Voxel.System {
    public class Phys {
        public struct A {
            public Vector3 min, max;
            public A(Vector3 p, float w, float h) {
                min = p - new Vector3(w/2, 0, w/2);
                max = p + new Vector3(w/2, h, w/2);
            }
            public bool Intersects(A o) => 
                min.X < o.max.X && max.X > o.min.X &&
                min.Y < o.max.Y && max.Y > o.min.Y &&
                min.Z < o.max.Z && max.Z > o.min.Z;
        }

        public static Vector3 Move(Vector3 p, Vector3 v, A pA, List<A> obs) {
            Vector3 nP = p + v;
            foreach (var o in obs) {
                if (new A(nP, 0.6f, 1.8f).Intersects(o)) {
                    // Simple AABB stop-on-hit
                    return p; 
                }
            }
            return nP;
        }
    }
}
