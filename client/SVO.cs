using System.Numerics;

namespace Voxel.Rendering {
    public class SVO {
        public class Node {
            public Vector3  Position;
            public float    Size;
            public bool     IsLeaf;
            public Vector4  Color;
            public Node?[]? Children;

            public Node(Vector3 position, float size, bool isLeaf) {
                Position = position; Size = size; IsLeaf = isLeaf; Color = Vector4.Zero;
            }
        }

        public Node  Root      { get; private set; }
        public float WorldSize { get; private set; }

        public SVO(float worldSize = 16384.0f) {
            WorldSize = worldSize;
            Root = new Node(new Vector3(-worldSize / 2.0f), worldSize, false);
        }

        public void Insert(Vector3 pos, Vector4 color) => InsertRecursive(Root, pos, color);

        void InsertRecursive(Node node, Vector3 pos, Vector4 color) {
            if (node.Size <= 8.0f) { node.IsLeaf = true; node.Color = color; return; }
            node.Children ??= new Node?[8];
            float childSize = node.Size * 0.5f;
            Vector3 center  = node.Position + new Vector3(childSize);
            int idx = 0;
            if (pos.X >= center.X) idx |= 1;
            if (pos.Y >= center.Y) idx |= 2;
            if (pos.Z >= center.Z) idx |= 4;
            node.Children[idx] ??= new Node(
                node.Position + new Vector3(
                    (idx & 1) != 0 ? childSize : 0f,
                    (idx & 2) != 0 ? childSize : 0f,
                    (idx & 4) != 0 ? childSize : 0f),
                childSize, childSize <= 8.0f);
            InsertRecursive(node.Children[idx]!, pos, color);
        }

        public bool RayCast(Vector3 ro, Vector3 rd, out float hitT, out Vector3 hitNorm, out Vector4 hitColor) {
            hitT = -1f; hitNorm = Vector3.Zero; hitColor = Vector4.Zero;
            return RayCastNode(Root, ro, rd, ref hitT, ref hitNorm, ref hitColor);
        }

        bool RayCastNode(Node node, Vector3 ro, Vector3 rd, ref float hitT, ref Vector3 hitNorm, ref Vector4 hitColor) {
            if (!RayAABB(ro, rd, node.Position, node.Position + new Vector3(node.Size), out float tN, out _)) return false;
            if (node.IsLeaf) {
                if (node.Color.W <= 0f) return false;
                hitT = tN; hitColor = node.Color;
                Vector3 hp  = ro + rd * tN;
                Vector3 cen = node.Position + new Vector3(node.Size * 0.5f);
                Vector3 d   = hp - cen;
                float ax = MathF.Abs(d.X), ay = MathF.Abs(d.Y), az = MathF.Abs(d.Z);
                hitNorm = ax > ay && ax > az ? new Vector3(MathF.Sign(d.X), 0, 0)
                        : ay > az           ? new Vector3(0, MathF.Sign(d.Y), 0)
                                            : new Vector3(0, 0, MathF.Sign(d.Z));
                return true;
            }
            if (node.Children == null) return false;
            float minT = float.MaxValue; Vector3 bNorm = Vector3.Zero; Vector4 bCol = Vector4.Zero; bool any = false;
            for (int i = 0; i < 8; i++) {
                if (node.Children[i] == null) continue;
                float ct = 0f; Vector3 cn = Vector3.Zero; Vector4 cc = Vector4.Zero;
                if (RayCastNode(node.Children[i]!, ro, rd, ref ct, ref cn, ref cc) && ct < minT) {
                    minT = ct; bNorm = cn; bCol = cc; any = true;
                }
            }
            if (!any) return false;
            hitT = minT; hitNorm = bNorm; hitColor = bCol; return true;
        }

        bool RayAABB(Vector3 ro, Vector3 rd, Vector3 bMin, Vector3 bMax, out float tN, out float tF) {
            tN = float.MinValue; tF = float.MaxValue;
            for (int i = 0; i < 3; i++) {
                float o = i == 0 ? ro.X : i == 1 ? ro.Y : ro.Z;
                float d = i == 0 ? rd.X : i == 1 ? rd.Y : rd.Z;
                float mn = i == 0 ? bMin.X : i == 1 ? bMin.Y : bMin.Z;
                float mx = i == 0 ? bMax.X : i == 1 ? bMax.Y : bMax.Z;
                if (MathF.Abs(d) < 1e-6f) { if (o < mn || o > mx) return false; }
                else {
                    float t1 = (mn - o) / d, t2 = (mx - o) / d;
                    if (t1 > t2) (t1, t2) = (t2, t1);
                    tN = MathF.Max(tN, t1); tF = MathF.Min(tF, t2);
                    if (tN > tF || tF < 0f) return false;
                }
            }
            return true;
        }
    }
}
