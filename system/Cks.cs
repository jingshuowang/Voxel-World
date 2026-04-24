using System.Collections.Generic;
using System.Linq;

namespace Voxel.System {
    public class Cks {
        public class C {
            public const int S = 16;
            public int X, Y, Z;
            public byte[] B = new byte[S * S * S];
            public int[][] Data = new int[6][];
            public volatile bool Ok = false;

            public C(int x, int y, int z) { X = x; Y = y; Z = z; }

            public void Set(int x, int y, int z, byte t) {
                if (x >= 0 && x < S && y >= 0 && y < S && z >= 0 && z < S)
                    B[x + z * S + y * S * S] = t;
            }

            public void Gen() {
                for (int i = 0; i < S; i++)
                    for (int j = 0; j < S; j++) {
                        int h = (int)(2048 + Math.Sin((X * S + i) * 0.01) * 10);
                        for (int k = 0; k < S; k++)
                            if (Y * S + k <= h) Set(i, k, j, 3);
                    }
            }

            public void Mesh() {
                for (int f = 0; f < 6; f++) {
                    var r = new List<int>();
                    var mask = new byte[S * S];
                    // Minimalist Greedy: Combine logic for brevity
                    for (int s = 0; s < S; s++) {
                        for (int i = 0; i < S * S; i++) {
                            int u = i % S, v = i / S;
                            mask[i] = B[u + s * S + v * S * S] != 0 ? B[u + s * S + v * S * S] : (byte)0;
                        }
                        for (int u = 0; u < S; u++) {
                            for (int v = 0; v < S; v++) {
                                byte t = mask[u + v * S];
                                if (t == 0) continue;
                                int w = 1, h = 1;
                                while (u + w < S && mask[u + w + v * S] == t) w++;
                                while (v + h < S) {
                                    bool match = true;
                                    for (int du = 0; du < w; du++) if (mask[u + du + (v + h) * S] != t) { match = false; break; }
                                    if (!match) break;
                                    h++;
                                }
                                for (int dv = 0; dv < h; dv++) for (int du = 0; du < w; du++) mask[u + du + (v + dv) * S] = 0;
                                r.Add((h - 1) << 25 | (w - 1) << 20 | (t & 0x7F) << 13 | (u + s * 17 + v * 289));
                            }
                        }
                    }
                    Data[f] = r.ToArray();
                }
                Ok = true;
            }
        }
    }
}
