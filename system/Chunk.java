package system;

import java.util.ArrayList;
import java.util.List;
import static org.lwjgl.opengl.GL11.*;
import static org.lwjgl.opengl.GL15.*;
import static org.lwjgl.opengl.GL20.*;
import static org.lwjgl.opengl.GL30.*;
import static org.lwjgl.opengl.GL31.*; // glDrawArraysInstanced
import static org.lwjgl.opengl.GL33.*; // glVertexAttribDivisor

public class Chunk {
    public static final int CHUNK_SIZE = 16;

    @FunctionalInterface
    public interface BlockLookup {
        byte getBlock(int globalX, int globalY, int globalZ);
    }

    // Block types
    public static final byte AIR = 0;
    public static final byte GRASS = 1;
    public static final byte DIRT = 2;
    public static final byte STONE = 3;
    public static final byte SNOW = 4;
    public static final byte WATER = 5;
    public static final byte WOOD = 6;
    public static final byte LEAVES = 7;

    public static final int WATER_LEVEL = 2000; // Sea level (within 0-4096 terrain range)

    public final int chunkX;
    public final int chunkY;
    public final int chunkZ;

    private byte[] blocks = new byte[CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE];

    private int vaoId; // kept for compatibility during transition (unused in MDI path)
    public int faceCount;  // number of face quad instances
    public int[] faceData; // packed face ints kept alive for MDI assembly each frame

    // Staged mesh data (computed on background thread, accepted on main thread)
    public int[] stagedVertices;
    public volatile boolean meshReady = false;

    public Chunk(int cx, int cy, int cz) {
        this.chunkX = cx;
        this.chunkY = cy;
        this.chunkZ = cz;
    }

    private int getIndex(int x, int y, int z) {
        return x + (z * CHUNK_SIZE) + (y * CHUNK_SIZE * CHUNK_SIZE);
    }

    public void setBlock(int x, int y, int z, byte type) {
        if (x < 0 || x >= CHUNK_SIZE || y < 0 || y >= CHUNK_SIZE || z < 0 || z >= CHUNK_SIZE)
            return;
        blocks[getIndex(x, y, z)] = type;
    }

    public byte getBlock(int x, int y, int z) {
        if (x < 0 || x >= CHUNK_SIZE || y < 0 || y >= CHUNK_SIZE || z < 0 || z >= CHUNK_SIZE)
            return 0;
        return blocks[getIndex(x, y, z)];
    }

    private byte getBlockOrGlobal(int x, int y, int z, BlockLookup lookup) {
        if (x >= 0 && x < CHUNK_SIZE && y >= 0 && y < CHUNK_SIZE && z >= 0 && z < CHUNK_SIZE)
            return blocks[getIndex(x, y, z)];
        if (lookup != null)
            return lookup.getBlock(chunkX * CHUNK_SIZE + x, chunkY * CHUNK_SIZE + y, chunkZ * CHUNK_SIZE + z);
        return AIR;
    }

    private boolean isRenderedAsAir(byte blockType) {
        return blockType == AIR || blockType == WATER;
    }

    // ---- Perlin Noise ----
    private static double fade(double t) {
        return t * t * t * (t * (t * 6 - 15) + 10);
    }

    private static double lerp(double t, double a, double b) {
        return a + t * (b - a);
    }

    private static double grad(int hash, double x, double z) {
        int h = hash & 3;
        double u = h < 2 ? x : z;
        double v = h < 2 ? z : x;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    private static final int[] PERM = new int[512];
    static {
        int[] p = { 151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
                140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148,
                247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32,
                57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175,
                74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
                60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
                65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169,
                200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64,
                52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
                207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
                119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
                129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104,
                218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241,
                81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106, 157,
                184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
                222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180 };
        for (int i = 0; i < 256; i++) {
            PERM[i] = p[i];
            PERM[256 + i] = p[i];
        }
    }

    public static double perlinNoise2D(double x, double z) {
        int xi = (int) Math.floor(x) & 255;
        int zi = (int) Math.floor(z) & 255;
        double xf = x - Math.floor(x);
        double zf = z - Math.floor(z);
        double u = fade(xf);
        double w = fade(zf);
        int aa = PERM[PERM[xi] + zi];
        int ab = PERM[PERM[xi] + zi + 1];
        int ba = PERM[PERM[xi + 1] + zi];
        int bb = PERM[PERM[xi + 1] + zi + 1];
        return lerp(w, lerp(u, grad(aa, xf, zf), grad(ba, xf - 1, zf)),
                lerp(u, grad(ab, xf, zf - 1), grad(bb, xf - 1, zf - 1)));
    }

    public static int getTerrainHeight(int globalX, int globalZ) {
        // Adjust terrain via variables in Config.java
        double baseNx = globalX * Config.PERLIN_FREQUENCY;
        double baseNz = globalZ * Config.PERLIN_FREQUENCY;

        // Standard fractal Brownian motion: each octave doubles frequency and halves amplitude
        double combined = 0.0;
        double freq = 1.0;
        double amp = 1.0;
        double totalAmp = 0.0;
        for (int i = 0; i < Config.PERLIN_OCTAVES; i++) {
            combined += perlinNoise2D(baseNx * freq, baseNz * freq) * amp;
            totalAmp += amp;
            freq *= 2.0;  // higher frequency each octave (finer detail)
            amp  *= 0.5;  // lower amplitude each octave
        }
        combined /= totalAmp; // normalize to [-1, 1]
        // Raise to power while preserving sign: creates dramatic peaks and flat valleys
        double powered = Math.signum(combined) * Math.pow(Math.abs(combined), Config.PERLIN_POWER);

        double height = powered * Config.PERLIN_AMPLITUDE;
        return 2048 + (int) height; // Base at Y=2048, full range 0-4096 (2^12)
    }

    public void generateTerrain() {
        int globalYBase = chunkY * CHUNK_SIZE;

        for (int x = 0; x < CHUNK_SIZE; x++) {
            for (int z = 0; z < CHUNK_SIZE; z++) {
                int globalX = chunkX * CHUNK_SIZE + x;
                int globalZ = chunkZ * CHUNK_SIZE + z;
                int terrainHeight = getTerrainHeight(globalX, globalZ);

                for (int y = 0; y < CHUNK_SIZE; y++) {
                    int globalY = globalYBase + y;

                    if (globalY <= terrainHeight) {
                        // Underground / surface
                        if (globalY == terrainHeight) {
                            // Surface block - biome by relative height vs water level
                            // (Lowered thresholds so snow appears more often)
                            if (terrainHeight > WATER_LEVEL + 40) {
                                setBlock(x, y, z, SNOW); // High = Snow
                            } else if (terrainHeight > WATER_LEVEL + 15) {
                                setBlock(x, y, z, STONE); // Moderately high = Stone
                            } else if (terrainHeight >= WATER_LEVEL - 2) {
                                setBlock(x, y, z, GRASS); // Ground/shore = Grass
                            } else {
                                setBlock(x, y, z, DIRT); // Underwater bottom = Dirt
                            }
                        } else if (globalY > terrainHeight - 4) {
                            // Just below surface: dirt
                            setBlock(x, y, z, DIRT);
                        } else {
                            setBlock(x, y, z, STONE); // Deep = always stone
                        }
                    } else if (globalY <= WATER_LEVEL) {
                        // Air above terrain but below water level = water
                        setBlock(x, y, z, WATER);
                    }
                    // else: air
                }
            }
        }

        // --- Tree generation ---
        // Place trees on grass blocks within this chunk
        for (int x = 0; x < CHUNK_SIZE; x++) {
            for (int z = 0; z < CHUNK_SIZE; z++) {
                int globalX = chunkX * CHUNK_SIZE + x;
                int globalZ = chunkZ * CHUNK_SIZE + z;
                int terrainHeight = getTerrainHeight(globalX, globalZ);

                // Only place trees on grass (above water, not too high)
                if (terrainHeight < WATER_LEVEL || terrainHeight > WATER_LEVEL + 35)
                    continue;

                // Use noise-based pseudorandom to decide tree placement
                double treeNoise = perlinNoise2D(globalX * 0.8, globalZ * 0.8);
                if (treeNoise < 0.35 || treeNoise > 0.40)
                    continue;

                // Avoid edges so leaves don't get cut off at chunk boundary
                if (x < 2 || x > 13 || z < 2 || z > 13)
                    continue;

                int trunkBase = terrainHeight + 1;
                int trunkHeight = 4 + ((globalX * 7 + globalZ * 13) & 3); // 4-7 blocks tall

                // Place trunk
                for (int ty = 0; ty < trunkHeight; ty++) {
                    int gy = trunkBase + ty;
                    int ly = gy - globalYBase;
                    if (ly >= 0 && ly < CHUNK_SIZE) {
                        setBlock(x, ly, z, WOOD);
                    }
                }

                // Place leaves (sphere-ish canopy at top)
                int leafCenter = trunkBase + trunkHeight - 1;
                for (int lx = -2; lx <= 2; lx++) {
                    for (int ly = -1; ly <= 2; ly++) {
                        for (int lz = -2; lz <= 2; lz++) {
                            if (lx * lx + ly * ly + lz * lz > 6)
                                continue; // rough sphere
                            int bx = x + lx;
                            int by = (leafCenter + ly) - globalYBase;
                            int bz = z + lz;
                            if (bx >= 0 && bx < CHUNK_SIZE && by >= 0 && by < CHUNK_SIZE && bz >= 0 && bz < CHUNK_SIZE) {
                                if (getBlock(bx, by, bz) == AIR) {
                                    setBlock(bx, by, bz, LEAVES);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // Phase 1: Greedy meshing -- runs on ANY thread (no GL calls!).
    // For each of the 6 face directions we slice the chunk along the face-normal
    // axis, build a type mask per slice, then greedily merge adjacent same-type
    // cells into the largest possible rectangles. Each rectangle becomes a single
    // packed quad, dramatically reducing triangle count on flat/uniform surfaces.
    public void buildMeshData() {
        buildMeshData(null);
    }

    // Bit layout: lllllwwwwwtttttttfffzzzzzyyyyyxxxxx  (matches chunk.vs decoder)
    // x,y,z: 5 bits each | face: 3 bits | type: 6 bits | w-1: 4 bits | l-1: 4 bits
    private static int packVertex(int x, int y, int z, int face, int type, int w, int l) {
        int x_bits   = x & 0x1F;
        int y_bits   = y & 0x1F;
        int z_bits   = z & 0x1F;
        int face_bits = face & 0x7;
        int tex_bits  = type & 0x3F;
        int w_bits    = (w - 1) & 0xF;
        int l_bits    = (l - 1) & 0xF;
        return (l_bits << 28) | (w_bits << 24) | (tex_bits << 18) | (face_bits << 15)
                | (z_bits << 10) | (y_bits << 5) | x_bits;
    }

    public void buildMeshData(BlockLookup lookup) {
        List<Integer> verticesList = new ArrayList<>();

        // --- 6 face directions ---
        // face 0: +Z  face 1: -Z  face 2: +Y  face 3: -Y  face 4: +X  face 5: -X
        // For each face we define:
        //   normal axis (na), u axis, v axis  -- all as x/y/z component indices (0/1/2)
        //   normalDir: +1 or -1 (which side of the neighbor to look)
        //   neighborOffset: (dnx, dny, dnz) -- the neighbor in the face's direction
        final int[] na  = { 2, 2, 1, 1, 0, 0 }; // slice axis
        final int[] ua  = { 0, 0, 0, 0, 1, 1 }; // u (width)  axis
        final int[] va  = { 1, 1, 2, 2, 2, 2 }; // v (length) axis
        final int[] dn  = { 1,-1, 1,-1, 1,-1 }; // neighbor step along na

        int[] sampleCoord = new int[3];

        // Reusable mask: mask[u][v] = block type at that cell (0 = skip)
        byte[][] mask = new byte[CHUNK_SIZE][CHUNK_SIZE];

        for (int face = 0; face < 6; face++) {
            int normalAxis = na[face];
            int uAxis      = ua[face];
            int vAxis      = va[face];
            int dir        = dn[face];

            // Iterate over each slice along the normal axis
            for (int slice = 0; slice < CHUNK_SIZE; slice++) {

                // Build mask: exposed faces on this slice
                for (int u = 0; u < CHUNK_SIZE; u++) {
                    for (int v = 0; v < CHUNK_SIZE; v++) {
                        sampleCoord[normalAxis] = slice;
                        sampleCoord[uAxis] = u;
                        sampleCoord[vAxis] = v;
                        int bx = sampleCoord[0], by = sampleCoord[1], bz = sampleCoord[2];

                        byte type = getBlock(bx, by, bz);
                        if (isRenderedAsAir(type)) {
                            mask[u][v] = 0;
                            continue;
                        }

                        // Check neighbor in face direction
                        sampleCoord[normalAxis] = slice + dir;
                        int nx = sampleCoord[0], ny = sampleCoord[1], nz = sampleCoord[2];
                        sampleCoord[normalAxis] = slice; // restore

                        byte neighbor = getBlockOrGlobal(nx, ny, nz, lookup);
                        mask[u][v] = isRenderedAsAir(neighbor) ? type : 0;
                    }
                }

                // Greedy merge over the mask
                boolean[][] visited = new boolean[CHUNK_SIZE][CHUNK_SIZE];

                for (int u = 0; u < CHUNK_SIZE; u++) {
                    for (int v = 0; v < CHUNK_SIZE; v++) {
                        byte type = mask[u][v];
                        if (type == 0 || visited[u][v])
                            continue;

                        // Expand width (u direction)
                        int w = 1;
                        while (u + w < CHUNK_SIZE && mask[u + w][v] == type && !visited[u + w][v])
                            w++;

                        // Expand length (v direction): each new v-row must fully match
                        int l = 1;
                        outer:
                        while (v + l < CHUNK_SIZE) {
                            for (int du = 0; du < w; du++) {
                                if (mask[u + du][v + l] != type || visited[u + du][v + l])
                                    break outer;
                            }
                            l++;
                        }

                        // Mark all covered cells as visited
                        for (int du = 0; du < w; du++)
                            for (int dv = 0; dv < l; dv++)
                                visited[u + du][v + dv] = true;

                        // Origin block coordinate for this quad
                        sampleCoord[normalAxis] = slice;
                        sampleCoord[uAxis] = u;
                        sampleCoord[vAxis] = v;
                        // For -direction faces, the quad sits on the "far" side of the slice
                        if (dir < 0) sampleCoord[normalAxis] = slice + dir + 1; // shift back
                        int qx = sampleCoord[0], qy = sampleCoord[1], qz = sampleCoord[2];

                        int packed = packVertex(qx, qy, qz, face, type, w, l);

                        // ONE int per face quad (shader generates 4 strip corners from gl_VertexID)
                        verticesList.add(packed);
                    }
                }
            }
        }

        if (verticesList.isEmpty()) {
            stagedVertices = null;
        } else {
            stagedVertices = new int[verticesList.size()];
            for (int i = 0; i < stagedVertices.length; i++)
                stagedVertices[i] = verticesList.get(i);
        }
        meshReady = true;
    }

    // Accept mesh from background thread -- stores faceData on CPU, no GL calls.
    // Main thread calls this; actual render data is assembled per-frame in Main.java MDI loop.
    public void uploadMesh() {
        if (stagedVertices == null) {
            faceCount = 0;
            faceData  = null;
        } else {
            faceCount = stagedVertices.length;
            faceData  = stagedVertices;
            stagedVertices = null;
        }
        meshReady = false;
    }

    // Legacy single-thread buildMesh (for immediate rebuild after block edit)
    public void buildMesh() {
        buildMesh(null);
    }

    public void buildMesh(BlockLookup lookup) {
        buildMeshData(lookup);
        uploadMesh();
    }

    public void render() { /* no-op: rendering handled by global MDI in Main */ }

    public void cleanup() {
        faceData  = null;
        faceCount = 0;
    }
}
