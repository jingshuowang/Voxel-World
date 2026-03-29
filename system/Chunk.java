package system;
import java.util.ArrayList;
import java.util.List;
import static org.lwjgl.opengl.GL11.*;
import static org.lwjgl.opengl.GL15.*;
import static org.lwjgl.opengl.GL20.*;
import static org.lwjgl.opengl.GL30.*;

public class Chunk {
    public static final int CHUNK_SIZE = 16;

    // Block types
    public static final byte AIR = 0;
    public static final byte GRASS = 1;
    public static final byte DIRT = 2;
    public static final byte STONE = 3;
    public static final byte SNOW = 4;
    public static final byte WATER = 5;

    public static final int WATER_LEVEL = 36; // Sea level

    public final int chunkX;
    public final int chunkY;
    public final int chunkZ;

    private byte[] blocks = new byte[CHUNK_SIZE * CHUNK_SIZE * CHUNK_SIZE];

    private int vaoId;
    private int vboId;
    private int eboId;
    private int vertexCount;

    // Staged mesh data (computed on background thread, uploaded on main thread)
    private float[] stagedVertices;
    private int[] stagedIndices;
    public volatile boolean meshReady = false;

    // Block colors: [R, G, B] per type
    private static final float[][] BLOCK_COLORS = {
        {1.0f, 1.0f, 1.0f},          // 0 = Air (unused)
        {0.25f, 0.55f, 0.15f},        // 1 = Grass - dark green
        {0.45f, 0.28f, 0.12f},        // 2 = Dirt - dark brown
        {0.5f, 0.5f, 0.5f},           // 3 = Stone - gray
        {0.92f, 0.95f, 0.98f},        // 4 = Snow - near white
        {0.15f, 0.35f, 0.65f},        // 5 = Water - blue
    };

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

    // ---- Perlin Noise ----
    private static double fade(double t) { return t * t * t * (t * (t * 6 - 15) + 10); }
    private static double lerp(double t, double a, double b) { return a + t * (b - a); }
    private static double grad(int hash, double x, double z) {
        int h = hash & 3;
        double u = h < 2 ? x : z;
        double v = h < 2 ? z : x;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    private static final int[] PERM = new int[512];
    static {
        int[] p = {151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,
            140,36,103,30,69,142,8,99,37,240,21,10,23,190,6,148,
            247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,
            57,177,33,88,237,149,56,87,174,20,125,136,171,168,68,175,
            74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,
            60,211,133,230,220,105,92,41,55,46,245,40,244,102,143,54,
            65,25,63,161,1,216,80,73,209,76,132,187,208,89,18,169,
            200,196,135,130,116,188,159,86,164,100,109,198,173,186,3,64,
            52,217,226,250,124,123,5,202,38,147,118,126,255,82,85,212,
            207,206,59,227,47,16,58,17,182,189,28,42,223,183,170,213,
            119,248,152,2,44,154,163,70,221,153,101,155,167,43,172,9,
            129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,104,
            218,246,97,228,251,34,242,193,238,210,144,12,191,179,162,241,
            81,51,145,235,249,14,239,107,49,192,214,31,181,199,106,157,
            184,84,204,176,115,121,50,45,127,4,150,254,138,236,205,93,
            222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180};
        for (int i = 0; i < 256; i++) { PERM[i] = p[i]; PERM[256 + i] = p[i]; }
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
        // Higher frequency, squared for dramatic peaks
        double nx = globalX * 0.02;   // Higher frequency for more features
        double nz = globalZ * 0.02;

        // Two octaves of noise
        double n1 = perlinNoise2D(nx, nz);
        double n2 = perlinNoise2D(nx * 2.5, nz * 2.5) * 0.4;

        double combined = n1 + n2;
        // Square it but preserve sign for dramatic valleys and peaks
        double squared = Math.signum(combined) * combined * combined;

        // Lower amplitude - squaring already makes peaks dramatic
        double height = squared * 8.0;
        return 40 + (int) height; // Base ground at Y=40
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
                            // Surface block - biome by height
                            if (terrainHeight > 50) {
                                setBlock(x, y, z, SNOW);      // Highest = Snow
                            } else if (terrainHeight > 44) {
                                setBlock(x, y, z, STONE);     // 2nd highest = Stone
                            } else {
                                setBlock(x, y, z, GRASS);     // Normal = Grass
                            }
                        } else if (globalY > terrainHeight - 4) {
                            // Just below surface: dirt (grass with block above = dirt)
                            setBlock(x, y, z, DIRT);
                        } else {
                            setBlock(x, y, z, STONE);         // Deep = always stone
                        }
                    } else if (globalY <= WATER_LEVEL) {
                        // Air above terrain but below water level = water
                        setBlock(x, y, z, WATER);
                    }
                    // else: air
                }
            }
        }
    }

    // Phase 1: Greedy meshing on ANY thread (no GL calls!)
    // Merges adjacent same-type faces into larger quads for massive triangle reduction.
    public void buildMeshData() {
        List<Float> verticesList = new ArrayList<>();
        List<Integer> indicesList = new ArrayList<>();
        int indexOffset = 0;

        // Normal vectors and shade levels per face direction
        // 0=+Z, 1=-Z, 2=+Y, 3=-Y, 4=+X, 5=-X
        float[][] normals = {{0,0,1},{0,0,-1},{0,1,0},{0,-1,0},{1,0,0},{-1,0,0}};
        float[] shadeLevels = {0.8f, 0.8f, 1.0f, 0.5f, 0.75f, 0.75f};

        // For each face direction, do greedy meshing on 2D slices
        for (int face = 0; face < 6; face++) {
            // Determine which axis is the "depth" (perpendicular to face)
            // and which two axes form the 2D slice
            // face 0(+Z): depth=z, u=x, v=y
            // face 1(-Z): depth=z, u=x, v=y
            // face 2(+Y): depth=y, u=x, v=z
            // face 3(-Y): depth=y, u=x, v=z
            // face 4(+X): depth=x, u=z, v=y
            // face 5(-X): depth=x, u=z, v=y

            for (int d = 0; d < CHUNK_SIZE; d++) {
                // Build a 2D mask of which block type is visible on this face
                byte[] mask = new byte[CHUNK_SIZE * CHUNK_SIZE];

                for (int v = 0; v < CHUNK_SIZE; v++) {
                    for (int u = 0; u < CHUNK_SIZE; u++) {
                        int x, y, z;
                        switch (face) {
                            case 0: x=u; y=v; z=d; break; // +Z
                            case 1: x=u; y=v; z=d; break; // -Z
                            case 2: x=u; y=d; z=v; break; // +Y
                            case 3: x=u; y=d; z=v; break; // -Y
                            case 4: x=d; y=v; z=u; break; // +X
                            case 5: x=d; y=v; z=u; break; // -X
                            default: x=0; y=0; z=0;
                        }
                        byte type = getBlock(x, y, z);
                        if (type == AIR) continue;

                        // Check if face is exposed (neighbor is air or out of chunk)
                        boolean exposed = false;
                        switch (face) {
                            case 0: exposed = (z == CHUNK_SIZE-1 || getBlock(x,y,z+1) == AIR); break;
                            case 1: exposed = (z == 0 || getBlock(x,y,z-1) == AIR); break;
                            case 2: exposed = (y == CHUNK_SIZE-1 || getBlock(x,y+1,z) == AIR); break;
                            case 3: exposed = (y == 0 || getBlock(x,y-1,z) == AIR); break;
                            case 4: exposed = (x == CHUNK_SIZE-1 || getBlock(x+1,y,z) == AIR); break;
                            case 5: exposed = (x == 0 || getBlock(x-1,y,z) == AIR); break;
                        }
                        if (exposed) mask[v * CHUNK_SIZE + u] = type;
                    }
                }

                // Greedy merge: scan the 2D mask and merge rectangles
                boolean[] visited = new boolean[CHUNK_SIZE * CHUNK_SIZE];
                for (int v = 0; v < CHUNK_SIZE; v++) {
                    for (int u = 0; u < CHUNK_SIZE; u++) {
                        int idx = v * CHUNK_SIZE + u;
                        if (visited[idx] || mask[idx] == 0) continue;
                        byte type = mask[idx];

                        // Expand width (u direction)
                        int w = 1;
                        while (u + w < CHUNK_SIZE && mask[v * CHUNK_SIZE + u + w] == type && !visited[v * CHUNK_SIZE + u + w])
                            w++;

                        // Expand height (v direction)
                        int h = 1;
                        outer:
                        while (v + h < CHUNK_SIZE) {
                            for (int k = 0; k < w; k++) {
                                int ci = (v + h) * CHUNK_SIZE + u + k;
                                if (mask[ci] != type || visited[ci]) break outer;
                            }
                            h++;
                        }

                        // Mark visited
                        for (int dv = 0; dv < h; dv++)
                            for (int du = 0; du < w; du++)
                                visited[(v + dv) * CHUNK_SIZE + u + du] = true;

                        // Emit one quad for the merged rectangle
                        float gox = chunkX * CHUNK_SIZE;
                        float goy = chunkY * CHUNK_SIZE;
                        float goz = chunkZ * CHUNK_SIZE;

                        // Calculate 4 corner positions based on face direction
                        float x0, y0, z0, x1, y1, z1, x2, y2, z2, x3, y3, z3;
                        switch (face) {
                            case 0: // +Z face (z = d+1)
                                x0=gox+u;     y0=goy+v;     z0=goz+d+1;
                                x1=gox+u+w;   y1=goy+v;     z1=goz+d+1;
                                x2=gox+u+w;   y2=goy+v+h;   z2=goz+d+1;
                                x3=gox+u;     y3=goy+v+h;   z3=goz+d+1;
                                break;
                            case 1: // -Z face (z = d)
                                x0=gox+u+w;   y0=goy+v;     z0=goz+d;
                                x1=gox+u;     y1=goy+v;     z1=goz+d;
                                x2=gox+u;     y2=goy+v+h;   z2=goz+d;
                                x3=gox+u+w;   y3=goy+v+h;   z3=goz+d;
                                break;
                            case 2: // +Y face (y = d+1)
                                x0=gox+u;     y0=goy+d+1;   z0=goz+v;
                                x1=gox+u+w;   y1=goy+d+1;   z1=goz+v;
                                x2=gox+u+w;   y2=goy+d+1;   z2=goz+v+h;
                                x3=gox+u;     y3=goy+d+1;   z3=goz+v+h;
                                break;
                            case 3: // -Y face (y = d)
                                x0=gox+u;     y0=goy+d;     z0=goz+v+h;
                                x1=gox+u+w;   y1=goy+d;     z1=goz+v+h;
                                x2=gox+u+w;   y2=goy+d;     z2=goz+v;
                                x3=gox+u;     y3=goy+d;     z3=goz+v;
                                break;
                            case 4: // +X face (x = d+1)
                                x0=gox+d+1;   y0=goy+v;     z0=goz+u+w;
                                x1=gox+d+1;   y1=goy+v;     z1=goz+u;
                                x2=gox+d+1;   y2=goy+v+h;   z2=goz+u;
                                x3=gox+d+1;   y3=goy+v+h;   z3=goz+u+w;
                                break;
                            default: // -X face (x = d)
                                x0=gox+d;     y0=goy+v;     z0=goz+u;
                                x1=gox+d;     y1=goy+v;     z1=goz+u+w;
                                x2=gox+d;     y2=goy+v+h;   z2=goz+u+w;
                                x3=gox+d;     y3=goy+v+h;   z3=goz+u;
                                break;
                        }

                        float nx = normals[face][0], ny = normals[face][1], nz = normals[face][2];
                        float shade = shadeLevels[face];
                        float cr = BLOCK_COLORS[type][0];
                        float cg = BLOCK_COLORS[type][1];
                        float cb = BLOCK_COLORS[type][2];

                        // 4 vertices per quad, 12 floats each
                        float[][] corners = {{x0,y0,z0},{x1,y1,z1},{x2,y2,z2},{x3,y3,z3}};
                        float[][] uvs = {{0,0},{w,0},{w,h},{0,h}};
                        for (int c = 0; c < 4; c++) {
                            verticesList.add(corners[c][0]); verticesList.add(corners[c][1]); verticesList.add(corners[c][2]);
                            verticesList.add(uvs[c][0]); verticesList.add(uvs[c][1]);
                            verticesList.add(nx); verticesList.add(ny); verticesList.add(nz);
                            verticesList.add(shade);
                            verticesList.add(cr); verticesList.add(cg); verticesList.add(cb);
                        }

                        // 2 triangles (CCW winding)
                        indicesList.add(indexOffset + 2); indicesList.add(indexOffset + 1); indicesList.add(indexOffset);
                        indicesList.add(indexOffset); indicesList.add(indexOffset + 3); indicesList.add(indexOffset + 2);
                        indexOffset += 4;
                    }
                }
            }
        }

        if (verticesList.isEmpty()) {
            stagedVertices = null;
            stagedIndices = null;
        } else {
            stagedVertices = new float[verticesList.size()];
            for (int i = 0; i < stagedVertices.length; i++)
                stagedVertices[i] = verticesList.get(i);
            stagedIndices = new int[indicesList.size()];
            for (int i = 0; i < stagedIndices.length; i++)
                stagedIndices[i] = indicesList.get(i);
        }
        meshReady = true;
    }

    // Phase 2: Upload to GPU - MUST be called on the OpenGL/main thread!
    public void uploadMesh() {
        if (vaoId != 0) {
            glDeleteVertexArrays(vaoId);
            glDeleteBuffers(vboId);
            glDeleteBuffers(eboId);
            vaoId = 0;
        }

        if (stagedVertices == null) {
            vertexCount = 0;
            meshReady = false;
            return;
        }

        vertexCount = stagedIndices.length;

        vaoId = glGenVertexArrays();
        glBindVertexArray(vaoId);

        vboId = glGenBuffers();
        glBindBuffer(GL_ARRAY_BUFFER, vboId);
        glBufferData(GL_ARRAY_BUFFER, stagedVertices, GL_STATIC_DRAW);

        eboId = glGenBuffers();
        glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, eboId);
        glBufferData(GL_ELEMENT_ARRAY_BUFFER, stagedIndices, GL_STATIC_DRAW);

        int stride = 12 * Float.BYTES; // pos3 + uv2 + normal3 + shade1 + color3

        glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, 0);
        glEnableVertexAttribArray(0);
        glVertexAttribPointer(1, 2, GL_FLOAT, false, stride, 3 * Float.BYTES);
        glEnableVertexAttribArray(1);
        glVertexAttribPointer(2, 3, GL_FLOAT, false, stride, 5 * Float.BYTES);
        glEnableVertexAttribArray(2);
        glVertexAttribPointer(3, 1, GL_FLOAT, false, stride, 8 * Float.BYTES);
        glEnableVertexAttribArray(3);
        glVertexAttribPointer(4, 3, GL_FLOAT, false, stride, 9 * Float.BYTES);
        glEnableVertexAttribArray(4);

        glBindBuffer(GL_ARRAY_BUFFER, 0);
        glBindVertexArray(0);

        stagedVertices = null;
        stagedIndices = null;
        meshReady = false;
    }

    // Legacy single-thread buildMesh (for immediate rebuild after block edit)
    public void buildMesh() {
        buildMeshData();
        uploadMesh();
    }

    public void render() {
        if (vertexCount == 0) return;
        glBindVertexArray(vaoId);
        glDrawElements(GL_TRIANGLES, vertexCount, GL_UNSIGNED_INT, 0);
        glBindVertexArray(0);
    }

    public void cleanup() {
        if (vaoId != 0) {
            glDeleteVertexArrays(vaoId);
            glDeleteBuffers(vboId);
            glDeleteBuffers(eboId);
        }
    }
}
