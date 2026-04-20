package system;

import com.bulletphysics.collision.broadphase.DbvtBroadphase;
import com.bulletphysics.collision.dispatch.CollisionDispatcher;
import com.bulletphysics.collision.dispatch.DefaultCollisionConfiguration;
import com.bulletphysics.collision.shapes.BoxShape;
import com.bulletphysics.collision.shapes.BvhTriangleMeshShape;
import com.bulletphysics.collision.shapes.CapsuleShape;
import com.bulletphysics.collision.shapes.TriangleIndexVertexArray;
import com.bulletphysics.dynamics.DiscreteDynamicsWorld;
import com.bulletphysics.dynamics.RigidBody;
import com.bulletphysics.dynamics.RigidBodyConstructionInfo;
import com.bulletphysics.dynamics.constraintsolver.SequentialImpulseConstraintSolver;
import com.bulletphysics.linearmath.DefaultMotionState;
import com.bulletphysics.linearmath.Transform;
import java.nio.*;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import java.util.Random;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentLinkedQueue;
import org.joml.Matrix4f;
import org.lwjgl.*;
import org.lwjgl.glfw.*;
import org.lwjgl.opengl.*;
import rendering.Audio;

import static org.lwjgl.glfw.Callbacks.*;
import static org.lwjgl.glfw.GLFW.*;
import static org.lwjgl.opengl.GL11.*;
import static org.lwjgl.opengl.GL15.*;
import static org.lwjgl.opengl.GL20.*;
import static org.lwjgl.opengl.GL30.*;
import static org.lwjgl.opengl.GL31.*; // glDrawArraysInstanced, GL_UNIFORM_BUFFER
import static org.lwjgl.opengl.GL33.*; // glVertexAttribDivisor
import static org.lwjgl.opengl.GL43.*; // GL_SHADER_STORAGE_BUFFER, glBindBufferBase, GL_DRAW_INDIRECT_BUFFER, glMultiDrawArraysIndirect
import static org.lwjgl.system.MemoryStack.*;
import static org.lwjgl.system.MemoryUtil.*;





import static org.lwjgl.opengl.GL31.*; // glDrawArraysInstanced, GL_UNIFORM_BUFFER
import static org.lwjgl.opengl.GL33.*; // glVertexAttribDivisor
import static org.lwjgl.opengl.GL43.*; // GL_SHADER_STORAGE_BUFFER, glBindBufferBase, GL_DRAW_INDIRECT_BUFFER, glMultiDrawArraysIndirect

public class Main {

    private long window;
    private rendering.Graphics.Shader shader;
    private rendering.Graphics.Shader skyShader;
    private Camera camera;

    private Map<Long, Chunks.Chunk> worldChunks;
    private static final int LOAD_RADIUS_XZ = 100;
    private static final int LOAD_RADIUS_Y = 256;   // 256 chunks * 16 = 4096 blocks (2^12)
    private static final int UNLOAD_RADIUS_XZ = 110;
    private static final int UNLOAD_RADIUS_Y = 264;  // slightly beyond load radius
    private static final int WORLD_HEIGHT_BLOCKS = 16384;
    private static final int WORLD_HEIGHT_CHUNKS = WORLD_HEIGHT_BLOCKS / Chunks.Chunk.CHUNK_SIZE;
    private static final int XZ_BLOCK_BITS = 24;
    private static final int XZ_CHUNK_BITS = XZ_BLOCK_BITS - 4;
    private static final int Y_CHUNK_BITS = 10;
    private static final long XZ_CHUNK_MASK = (1L << XZ_CHUNK_BITS) - 1L;
    private static final long Y_CHUNK_MASK = (1L << Y_CHUNK_BITS) - 1L;

    // Background chunk generation -- multiple threads for large radius
    private final ConcurrentLinkedQueue<Chunks.Chunk> chunksToUpload = new ConcurrentLinkedQueue<>();
    // Lock-free set: ConcurrentHashMap.newKeySet() has no global lock unlike synchronizedSet
    private final Set<Long> chunksLoading = java.util.concurrent.ConcurrentHashMap.newKeySet();
    private final java.util.ArrayDeque<Long> chunksNeedingRebuild = new java.util.ArrayDeque<>();
    private volatile boolean chunkThreadRunning = true;
    private static final int GEN_THREAD_COUNT = 2;
    private Thread[] chunkGenThreads = new Thread[GEN_THREAD_COUNT];
    // No unloadFrameCounter -- unload is incremental every frame (small batch).

    // Global MDI render resources (created once, reused every frame)
    private int globalVaoId;       // one VAO for all chunks
    private int globalVboId;       // face data for visible chunks (rebuilt per frame)
    private int globalVboOffset = 0; // Linear pointer to VRAM allocation
    private int globalIndirectBuf; // DrawArraysIndirect commands (rebuilt per frame)
    private int globalPositionSSBO;// chunk world offsets indexed by gl_DrawID

    // Pre-allocated MDI assembly buffers -- grown as needed, NEVER freed each frame.
    // Eliminates per-frame large array allocation and GC pressure.
        private java.nio.IntBuffer mdiIndirectBuffer = org.lwjgl.system.MemoryUtil.memAllocInt(16384 * 4);
    private java.nio.FloatBuffer mdiPosBuffer = org.lwjgl.system.MemoryUtil.memAllocFloat(16384 * 4);

    private boolean[] keys = new boolean[GLFW_KEY_LAST];
    private double lastMouseX = -1;
    private double lastMouseY = -1;

    private float velocityX = 0.0f;
    private float velocityY = 0.0f;
    private float velocityZ = 0.0f;
    private boolean isGrounded = false;
    private float coyoteTimeTimer = 0.0f; // Allow jump briefly after falling off ledges
    private float currentFov = Config.BASE_FOV;

    private int selectedBlockX = -1, selectedBlockY = -1, selectedBlockZ = -1;
    private int selectedFaceNormalX = 0, selectedFaceNormalY = 0, selectedFaceNormalZ = 0;
    private boolean hasSelection = false;
    private boolean wasCrouching = false;
    private boolean isFullscreen = false;
    private volatile boolean isPaused = false;
    private volatile boolean shouldExit = false;
    private volatile boolean needsCursorLock = false;
    private boolean isSprintingState = false;
    private static final byte TOOL_PROJECTILE = (byte) 127;
    private byte selectedBlockType = Chunks.Chunk.STONE;
    private int renderMode = 0; // 0=normal, 1=normals, 2=depth, 3=wireframe
    private boolean showDebugStatsAndGrid = false; // F3 toggles debug view
    private boolean wasSpacePressed = false;

    // Special Abilities
    private int selectedSpecialAbility = 1; // 1 = Dash, 2 = Air Block
    private float dashCooldownTimer = 0.0f;
    private static final float DASH_COOLDOWN = 1.5f;
    private static final float DASH_SPEED = 25.0f;

    private float airBlockCooldownTimer = 0.0f;
    private static final float AIR_BLOCK_COOLDOWN = 2.5f;

    // Water Jump
    private float waterJumpCooldownTimer = 0.0f;
    private static final float WATER_JUMP_COOLDOWN = 1.0f;
    private static final float WATER_JUMP_POWER = 12.0f; // Approx 2 blocks high

    // Mouse Auto-Fire Timers
    private boolean isLeftMouseDown = false;
    private boolean isRightMouseDown = false;
    private float leftMouseTimer = 0.0f;
    private float rightMouseTimer = 0.0f;
    private static final float MOUSE_HOLD_DELAY = 0.25f; // 5 game ticks at 20 ticks/sec

    // Block Mining State
    private HashMap<Long, Float> blockHealthMap = new HashMap<>();
    private static final float BLOCK_MAX_HEALTH = 1.0f; // 1 second to mine
    private int miningBlockX = 0, miningBlockY = 0, miningBlockZ = 0;

    private static long packBlockKey(int x, int y, int z) {
        return ((long) (x & 0x1FFFFFF)) | (((long) (z & 0x1FFFFFF) << 25)) | (((long) (y & 0x3FFF) << 50));
    }

    // Builder Mode State
    private boolean isBuilderModeDragging = false;
    private int builderStartX = 0, builderStartY = 0, builderStartZ = 0;

    private static class Projectile {
        float x, y, z;
        float vx, vy, vz;
        float life;
    }

    private static class CubeParticle {
        float x, y, z;
        float vx, vy, vz;
        float life;
        float size;
        float r, g, b;
    }

    private final List<Projectile> activeProjectiles = new ArrayList<>();
    private final List<CubeParticle> activeCubeParticles = new ArrayList<>();
    private final Random rng = new Random();

    public void run() {
        System.out.println("Hello LWJGL " + Version.getVersion() + "!");
        init();
        loop();

        if (shader != null)
            shader.cleanup();
        chunkThreadRunning = false;
        for (Thread t : chunkGenThreads) {
            if (t != null) {
                try { t.join(1000); } catch (InterruptedException e) {}
            }
        }
        if (worldChunks != null) {
            for (Chunks.Chunk c : worldChunks.values()) {
                c.cleanup();
            }
        }

        glfwFreeCallbacks(window);
        glfwDestroyWindow(window);
        glfwTerminate();
        glfwSetErrorCallback(null).free();
    }

    private void init() {
        // Keep only meaningful GLFW errors; suppress noisy platform messages.
        GLFWErrorCallback.create((error, description) -> {
            String msg = GLFWErrorCallback.getDescription(description);
            if (msg == null)
                return;
            String lower = msg.toLowerCase();
            if (lower.contains("wgl") || lower.contains("pixel format") || lower.contains("monitor callback")) {
                return;
            }
            System.err.println("[GLFW] " + msg);
        }).set();
        if (!glfwInit())
            throw new IllegalStateException("Unable to initialize GLFW");

        glfwDefaultWindowHints();

        // Request OpenGL 4.6 Compatibility Profile
        glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 4);
        glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 6);
        glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_COMPAT_PROFILE);

        glfwWindowHint(GLFW_VISIBLE, GLFW_FALSE);
        glfwWindowHint(GLFW_RESIZABLE, GLFW_TRUE);

        window = glfwCreateWindow(1600, 900, "Java 3D Voxel Engine", NULL, NULL);
        if (window == NULL)
            throw new RuntimeException("Failed to create the GLFW window");

        // Input Callbacks
        glfwSetKeyCallback(window, (window, key, scancode, action, mods) -> {
            if (key == GLFW_KEY_ESCAPE && action == GLFW_RELEASE) {
                if (isPaused)
                    return; // Prevent spamming

                isPaused = true;
                // Free cursor native
                glfwSetInputMode(window, GLFW_CURSOR, GLFW_CURSOR_NORMAL);

                // Spawn a detached worker thread for Swing to prevent GLFW from freezing the OS
                // Window
                new Thread(() -> {
                    String[] options = { "Resume", "Controls", "Exit Game" };

                    javax.swing.JOptionPane pane = new javax.swing.JOptionPane("Game Paused",
                            javax.swing.JOptionPane.PLAIN_MESSAGE, javax.swing.JOptionPane.DEFAULT_OPTION, null,
                            options, options[0]);
                    javax.swing.JDialog dialog = pane.createDialog("Pause Menu");
                    dialog.setModal(true);
                    dialog.setAlwaysOnTop(true);
                    dialog.setLocation(0, 0); // Reposition to Top Left!
                    dialog.setVisible(true);

                    Object selectedValue = pane.getValue();
                    int choice = -1;
                    if (selectedValue != null) {
                        for (int i = 0; i < options.length; i++) {
                            if (options[i].equals(selectedValue))
                                choice = i;
                        }
                    }

                    if (choice == 1) { // Controls
                        javax.swing.JOptionPane.showMessageDialog(null,
                                "W, A, S, D - Move\nSpace - Jump\nShift - Crouch\nCtrl + W - Sprint\nLeft Click - Break block\nRight Click - Place block",
                                "Controls", javax.swing.JOptionPane.INFORMATION_MESSAGE);
                    } else if (choice == 2) { // Exit
                        shouldExit = true;
                    }

                    needsCursorLock = true;
                    isPaused = false;
                }).start();
            }
            if (key == GLFW_KEY_F11 && action == GLFW_RELEASE) {
                long monitor = glfwGetPrimaryMonitor();
                GLFWVidMode videoMode = glfwGetVideoMode(monitor);
                if (isFullscreen) {
                    glfwSetWindowMonitor(window, NULL, 100, 100, 800, 600, 0); // Windowed Mode
                    isFullscreen = false;
                } else {
                    // Pass -1 (GLFW_DONT_CARE) for refresh rate to prevent graphics drivers from
                    // crashing!
                    glfwSetWindowMonitor(window, monitor, 0, 0, videoMode.width(), videoMode.height(), -1);
                    isFullscreen = true;
                }
                // CRITICAL: glfwSetWindowMonitor resets swap interval! Re-disable vsync.
                glfwSwapInterval(0);
            }
            // Hotbar: 1=Launch, 2=Platform, 3=Stone, 4=Snow, 5=Projectile tool
            if (key == GLFW_KEY_1 && action == GLFW_PRESS) {
                selectedSpecialAbility = 1;
                System.out.println("Selected Ability: Launch");
            }
            if (key == GLFW_KEY_2 && action == GLFW_PRESS) {
                selectedSpecialAbility = 2;
                System.out.println("Selected Ability: Platform");
            }
            if (key == GLFW_KEY_3 && action == GLFW_PRESS)
                selectedBlockType = Chunks.Chunk.STONE;
            if (key == GLFW_KEY_4 && action == GLFW_PRESS)
                selectedBlockType = Chunks.Chunk.SNOW;
            if (key == GLFW_KEY_5 && action == GLFW_PRESS)
                selectedBlockType = TOOL_PROJECTILE;
            if (key == GLFW_KEY_F3 && action == GLFW_PRESS)
                showDebugStatsAndGrid = !showDebugStatsAndGrid;
            if (key == GLFW_KEY_F5 && action == GLFW_PRESS)
                renderMode = (renderMode + 1) % 4; // Cycle: normal -> normals -> depth -> wireframe
            if (key >= 0 && key < GLFW_KEY_LAST) {
                keys[key] = (action != GLFW_RELEASE);
            }
        });

        glfwSetCursorPosCallback(window, (window, xpos, ypos) -> {
            if (lastMouseX == -1) {
                lastMouseX = xpos;
                lastMouseY = ypos;
            }
            double dx = xpos - lastMouseX;
            double dy = ypos - lastMouseY;
            lastMouseX = xpos;
            lastMouseY = ypos;

            camera.yaw += dx * Config.MOUSE_SENSITIVITY;
            camera.pitch += dy * Config.MOUSE_SENSITIVITY;

            if (camera.pitch > 89.0f)
                camera.pitch = 89.0f;
            if (camera.pitch < -89.0f)
                camera.pitch = -89.0f;
        });

        glfwSetMouseButtonCallback(window, (window, button, action, mods) -> {
            if (action == GLFW_PRESS) {
                if (button == GLFW_MOUSE_BUTTON_LEFT) {
                    isLeftMouseDown = true;
                    // We no longer instantly breakBlock() because of the 1-second mining duration
                    // mechanics
                } else if (button == GLFW_MOUSE_BUTTON_RIGHT) {
                    if (selectedSpecialAbility == 2 && hasSelection) {
                        isBuilderModeDragging = true;
                        builderStartX = selectedBlockX + selectedFaceNormalX;
                        builderStartY = selectedBlockY + selectedFaceNormalY;
                        builderStartZ = selectedBlockZ + selectedFaceNormalZ;
                    } else {
                        isRightMouseDown = true;
                        rightMouseTimer = MOUSE_HOLD_DELAY; // Wait full delay before NEXT auto-fire
                        placeBlock(); // Fire exactly on click
                    }
                }
            } else if (action == GLFW_RELEASE) {
                if (button == GLFW_MOUSE_BUTTON_LEFT) {
                    isLeftMouseDown = false;
                } else if (button == GLFW_MOUSE_BUTTON_RIGHT) {
                    isRightMouseDown = false;
                    if (selectedSpecialAbility == 2 && isBuilderModeDragging) {
                        isBuilderModeDragging = false;
                        if (hasSelection) {
                            placeBuilderVolume();
                        }
                    }
                }
            }
        });

        // Lock mouse cursor to window
        glfwSetInputMode(window, GLFW_CURSOR, GLFW_CURSOR_DISABLED);

        glfwMakeContextCurrent(window);
        glfwSwapInterval(0); // Disable v-sync for uncapped FPS
        glfwShowWindow(window);
    }

    public Chunks.Chunk setBlockGlobal(int x, int y, int z, byte type) {
        int chunkX = (int) Math.floor(x / 16.0f);
        int chunkY = (int) Math.floor(y / 16.0f);
        int chunkZ = (int) Math.floor(z / 16.0f);
        int localX = x - (chunkX * 16);
        int localY = y - (chunkY * 16);
        int localZ = z - (chunkZ * 16);
        Chunks.Chunk c = worldChunks.get(packChunkKey(chunkX, chunkY, chunkZ));
        if (c != null) {
            c.setBlock(localX, localY, localZ, type);
            return c;
        }
        return null;
    }

    public byte getBlockGlobal(int x, int y, int z) {
        int chunkX = (int) Math.floor(x / 16.0f);
        int chunkY = (int) Math.floor(y / 16.0f);
        int chunkZ = (int) Math.floor(z / 16.0f);
        int localX = x - (chunkX * 16);
        int localY = y - (chunkY * 16);
        int localZ = z - (chunkZ * 16);
        Chunks.Chunk c = worldChunks.get(packChunkKey(chunkX, chunkY, chunkZ));
        if (c != null) {
            return c.getBlock(localX, localY, localZ);
        }
        return 0; // Air if not loaded
    }

    private static long packChunkKey(int cx, int cy, int cz) {
        // Pack chunk coordinates using actual block-world limits.
        // X/Z: 2^24 blocks per axis -> 2^20 chunk coordinates.
        // Y: 16384 blocks -> 1024 chunk coordinates.
        return ((cx & XZ_CHUNK_MASK) << (XZ_CHUNK_BITS + Y_CHUNK_BITS))
                | ((cz & XZ_CHUNK_MASK) << Y_CHUNK_BITS)
                | (cy & Y_CHUNK_MASK);
    }

    private void updateChunks() {
        int playerChunkX = (int) Math.floor(camera.position.x / 16.0f);
        int playerChunkZ = (int) Math.floor(camera.position.z / 16.0f);

        // Upload chunks that the background thread has finished generating (max 32 per
        // frame)
        int uploaded = 0;
        while (uploaded < 256) {
            Chunks.Chunk chunk = chunksToUpload.poll();
            if (chunk == null)
                break;
            chunk.uploadMesh();
            long key = packChunkKey(chunk.chunkX, chunk.chunkY, chunk.chunkZ);
            worldChunks.put(key, chunk);
            chunksLoading.remove(key);
            // Queue neighbors for deferred rebuild
            int[][] dirs = { { 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 } };
            for (int[] d : dirs) {
                long nk = packChunkKey(chunk.chunkX + d[0], chunk.chunkY + d[1], chunk.chunkZ + d[2]);
                if (worldChunks.containsKey(nk)) {
                    chunksNeedingRebuild.add(nk);
                }
            }
            uploaded++;
        }

        // Process deferred neighbor rebuilds (max 4 per frame)
        int rebuilt = 0;
        while (rebuilt < 4 && !chunksNeedingRebuild.isEmpty()) {
            long nk = chunksNeedingRebuild.poll();
            Chunks.Chunk nb = worldChunks.get(nk);
            if (nb != null) {
                nb.buildMeshData(this::getBlockGlobal);
                nb.uploadMesh();
                rebuilt++;
            }
        }

        // Unload far chunks incrementally: max 32 per frame to spread the cost.
        // Iterates a subset of the map each frame, cycling through over time.
        int unloaded = 0;
        Iterator<Map.Entry<Long, Chunks.Chunk>> it = worldChunks.entrySet().iterator();
        while (it.hasNext() && unloaded < 32) {
            Map.Entry<Long, Chunks.Chunk> entry = it.next();
            Chunks.Chunk c = entry.getValue();
            int ddx = c.chunkX - playerChunkX;
            int ddz = c.chunkZ - playerChunkZ;
            if ((long)ddx * ddx + (long)ddz * ddz > (long)UNLOAD_RADIUS_XZ * UNLOAD_RADIUS_XZ
                    || Math.abs(c.chunkY) > LOAD_RADIUS_Y) {
                c.cleanup();
                it.remove();
                unloaded++;
            }
        }
    }

    private void loop() {
        GL.createCapabilities();

        System.out.println("LWJGL Version: " + org.lwjgl.Version.getVersion());
        System.out.println("OpenGL Version: " + glGetString(GL_VERSION));

        glClearColor(0.5f, 0.8f, 1.0f, 0.0f); // Sky blue
        glEnable(GL_DEPTH_TEST);
        glDepthFunc(GL_LEQUAL); // Standard Z
        glClearDepth(1.0); // Clear to 1 (farthest in standard Z)
        glEnable(GL_CULL_FACE); // Cull back faces for performance
        glEnable(GL_BLEND);
        glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

        // --- Create global MDI rendering resources ---
                globalVaoId = glGenVertexArrays();
        glBindVertexArray(globalVaoId);
        globalVboId = glGenBuffers();
        glBindBuffer(GL_ARRAY_BUFFER, globalVboId);
        glBufferData(GL_ARRAY_BUFFER, 200_000_000L, GL_STATIC_DRAW); // 200MB global streaming geometry VBO limit
        // Attribute 0: per-instance packed face uint, divisor=1
        glVertexAttribIPointer(0, 1, GL_UNSIGNED_INT, Integer.BYTES, 0);
        glEnableVertexAttribArray(0);
        glVertexAttribDivisor(0, 1);
        
        // --- INTEL DRIVER CRASH FIX ---
        // Bind a dummy per-vertex attribute (divisor=0) to prevent native driver crash on empty vertex streams!
        int dummyVbo = glGenBuffers();
        glBindBuffer(GL_ARRAY_BUFFER, dummyVbo);
        glBufferData(GL_ARRAY_BUFFER, new float[]{0,0,0,0}, GL_STATIC_DRAW);
        glVertexAttribPointer(1, 1, GL_FLOAT, false, 0, 0L);
        glEnableVertexAttribArray(1);
        glVertexAttribDivisor(1, 0); // per-vertex
        // ------------------------------

        glBindBuffer(GL_ARRAY_BUFFER, 0);
        glBindVertexArray(0);
        globalIndirectBuf = glGenBuffers();
        globalPositionSSBO = glGenBuffers();

        try {
            shader = new rendering.Graphics.Shader();
            shader.createVertexShaderFromFile("Asset/shader/chunk.vs");
            shader.createFragmentShaderFromFile("Asset/shader/chunk.fs");
            shader.link();

            skyShader = new rendering.Graphics.Shader();
            skyShader.createVertexShaderFromFile("Asset/shader/sky.vs");
            skyShader.createFragmentShaderFromFile("Asset/shader/sky.fs");
            skyShader.link();
        } catch (Exception e) {
            e.printStackTrace();
            return;
        }

        camera = new Camera();
        camera.setFov(Config.BASE_FOV);

        // Dynamic chunk loading - start with empty map, chunks load on background
        // thread!
        worldChunks = new ConcurrentHashMap<>();

        // Pre-generate spawn area SYNCHRONOUSLY so ground exists before player falls!
        System.out.println("Pre-generating spawn chunks (small radius)...");
        int spawnTerrainY = Chunks.Chunk.getTerrainHeight(0, 0);
        int spawnCY = spawnTerrainY / Chunks.Chunk.CHUNK_SIZE;
        int spawnRadius = 2;
        for (int dx = -spawnRadius; dx <= spawnRadius; dx++) {
            for (int dz = -spawnRadius; dz <= spawnRadius; dz++) {
                if (dx * dx + dz * dz > spawnRadius * spawnRadius)
                    continue;
                // Only generate Y slices around the terrain surface (+-4 chunks), allow negative Y
                for (int cy = Math.max(-LOAD_RADIUS_Y, spawnCY - 4); cy <= Math.min(LOAD_RADIUS_Y, spawnCY + 4); cy++) {
                    long key = packChunkKey(dx, cy, dz);
                    if (!worldChunks.containsKey(key)) {
                        Chunks.Chunk chunk = new Chunks.Chunk(dx, cy, dz);
                        chunk.generateTerrain();
                        chunk.buildMeshData(this::getBlockGlobal);
                        chunk.uploadMesh();
                        worldChunks.put(key, chunk);
                    }
                }
            }
        }
        System.out.println("Spawn chunks ready! (" + worldChunks.size() + " chunks)");

        // Set spawn to actual terrain height at (0,0) + player eye height
        camera.position.set(0, spawnTerrainY + 2.5f, 0);
        System.out.println("Dynamic chunk loading enabled (radius: " + LOAD_RADIUS_XZ + " chunks)");

        // Start background chunk generation threads (multiple for large radius)
        Runnable genTask = () -> {
            System.out.println("[ChunkGen] Thread " + Thread.currentThread().getName() + " started!");
            // Each thread tracks its own spiral frontier so it doesn't restart from r=0 each pass
            int scanR = 0;
            int lastPcx = Integer.MIN_VALUE, lastPcz = Integer.MIN_VALUE;
            while (chunkThreadRunning) {
                int pcx = (int) Math.floor(camera.position.x / 16.0f);
                int pcz = (int) Math.floor(camera.position.z / 16.0f);
                // Reset frontier when player moves to a new chunk
                if (pcx != lastPcx || pcz != lastPcz) {
                    scanR = 0;
                    lastPcx = pcx;
                    lastPcz = pcz;
                }

                int generated = 0;
                // Scan the current shell at scanR, advance when exhausted
                while (scanR <= LOAD_RADIUS_XZ && generated < 256) {
                    boolean foundWork = false;
                    int r = scanR;
                    for (int dx = -r; dx <= r && generated < 256; dx++) {
                        for (int dz = -r; dz <= r && generated < 256; dz++) {
                            if (Math.abs(dx) != r && Math.abs(dz) != r) continue;
                            if ((long)dx*dx + (long)dz*dz > (long)LOAD_RADIUS_XZ * LOAD_RADIUS_XZ) continue;
                            int terrainH = Chunks.Chunk.getTerrainHeight(
                                    (pcx + dx) * Chunks.Chunk.CHUNK_SIZE,
                                    (pcz + dz) * Chunks.Chunk.CHUNK_SIZE);
                            int surfaceCY = terrainH / Chunks.Chunk.CHUNK_SIZE;
                            int cyMin = Math.max(-LOAD_RADIUS_Y, surfaceCY - 4);
                            int cyMax = Math.min(LOAD_RADIUS_Y, surfaceCY + 6);
                            for (int cy = cyMin; cy <= cyMax; cy++) {
                                long key = packChunkKey(pcx + dx, cy, pcz + dz);
                                if (!worldChunks.containsKey(key) && chunksLoading.add(key)) {
                                    Chunks.Chunk chunk = new Chunks.Chunk(pcx + dx, cy, pcz + dz);
                                    chunk.generateTerrain();
                                    chunk.buildMeshData(this::getBlockGlobal);
                                    chunksToUpload.add(chunk);
                                    generated++;
                                    foundWork = true;
                                }
                            }
                        }
                    }
                    if (!foundWork) scanR++; // advance frontier when this shell is fully loaded
                    else break;             // come back to same shell next pass
                }
                if (scanR > LOAD_RADIUS_XZ) scanR = 0; // full sweep done, restart

                if (generated == 0) {
                    try { Thread.sleep(50); } catch (InterruptedException e) { break; }
                }
            }
            System.out.println("[ChunkGen] Thread " + Thread.currentThread().getName() + " stopped.");
        };

        for (int t = 0; t < GEN_THREAD_COUNT; t++) {
            chunkGenThreads[t] = new Thread(genTask, "ChunkGen-" + t);
            chunkGenThreads[t].setDaemon(true);
            chunkGenThreads[t].setPriority(Thread.NORM_PRIORITY - 1); // slightly below main thread
            chunkGenThreads[t].start();
        }

        System.out.println("Preloading Audio to RAM...");
        Audio.preloadSound("Asset/sound/block1.wav");
        Audio.preloadSound("Asset/sound/block2.wav");
        Audio.preloadSound("Asset/sound/block3.wav");
        Audio.preloadSound("Asset/sound/block4.wav");

        float lastTime = (float) glfwGetTime();
        int frameCount = 0;
        float fpsTimer = 0.0f;
        int fps = 0;

        float introFadeTimer = 2.0f; // 2 seconds of pure fade-in loading screen
        float introFadeAlpha = 1.0f;

        long globalTicks = 0;
        float tickAccumulator = 0.0f;
        final float TICK_RATE = 60.0f;
        final float TIME_PER_TICK = 1.0f / TICK_RATE;
        int ticksThisSecond = 0;
        float tpsTimer = 0.0f;
        int tps = 0;

        float cpuTimeMs = 0;
        float gpuTimeMs = 0;

        while (!glfwWindowShouldClose(window) && !shouldExit) {
            long frameStartNano = System.nanoTime();

            float currentTime = (float) glfwGetTime();
            float rawDt = currentTime - lastTime;
            lastTime = currentTime;

            if (introFadeTimer > 0.0f) {
                introFadeTimer -= rawDt;
                if (introFadeTimer < 1.0f) {
                    introFadeAlpha = introFadeTimer; // Fade out over the last 1 second
                }
            } else {
                introFadeAlpha = 0.0f;
            }

            if (needsCursorLock) {
                glfwSetInputMode(window, GLFW_CURSOR, GLFW_CURSOR_DISABLED);
                lastMouseX = -1;
                lastMouseY = -1;
                needsCursorLock = false;
            }

            // --- FIXED UPDATE TICK LOOP (60 TPS) ---
            tickAccumulator += rawDt;
            if (tickAccumulator > 0.25f)
                tickAccumulator = 0.25f;
            while (tickAccumulator >= TIME_PER_TICK) {
                globalTicks++;
                ticksThisSecond++;
                tickAccumulator -= TIME_PER_TICK;

                if (!isPaused) {
                    handleInput(TIME_PER_TICK);
                    updateRaycast();
                    updateProjectiles(TIME_PER_TICK);
                }
            }

            // Chunks.Chunk management once per render frame
            updateChunks();

            tpsTimer += rawDt;
            if (tpsTimer >= 1.0f) {
                tps = ticksThisSecond;
                ticksThisSecond = 0;
                tpsTimer = 0.0f;
            }

            // Map globalTicks proportionally to the day cycle angle!
            float dayTime = 1.0f + (globalTicks * 0.00167f);

            frameCount++;
            fpsTimer += rawDt;
            if (fpsTimer >= 1.0f) {
                fps = frameCount;
                frameCount = 0;
                fpsTimer = 0.0f;
            }

            // Simple sky gradient: blend from dark blue (top) to light blue (horizon)
            // The clear color IS the sky - simple and clean
            float sunRadius = 8192.0f;
            float sunY = camera.position.y + (float) Math.sin(dayTime) * sunRadius;
            float sunX = camera.position.x + (float) Math.cos(dayTime) * sunRadius;
            float sunZ = camera.position.z;
            org.joml.Vector3f sunPos = new org.joml.Vector3f(sunX, sunY, sunZ);
            org.joml.Vector3f sunDir = new org.joml.Vector3f(sunPos).sub(camera.position).normalize();

            // Sky color: bright blue during day, dark at night
            float dayFactor = Math.max(0, Math.min(1, (sunDir.y + 0.2f) / 0.4f));
            float skyR = 0.05f * (1 - dayFactor) + 0.50f * dayFactor;
            float skyG = 0.05f * (1 - dayFactor) + 0.80f * dayFactor;
            float skyB = 0.12f * (1 - dayFactor) + 1.00f * dayFactor;

            // 2. Render
            int[] width = new int[1], height = new int[1];
            glfwGetFramebufferSize(window, width, height);
            glViewport(0, 0, width[0], height[0]);

            byte camBlock = getBlockGlobal((int) Math.floor(camera.position.x), (int) Math.floor(camera.position.y),
                    (int) Math.floor(camera.position.z));
            boolean blind = (camBlock != 0 && camBlock != Chunks.Chunk.WATER);

            if (blind) {
                glClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            } else {
                glClearColor(skyR, skyG, skyB, 1.0f);
            }
            glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

            float aspect = width[0] == 0 ? 1 : (float) width[0] / (float) height[0];

            if (!blind) {

                // --- Draw Sun and Moon as simple circles ---
                glDisable(GL_DEPTH_TEST);
                skyShader.bind();
                skyShader.setUniform("projectionMatrix", camera.getProjectionMatrix(aspect));
                skyShader.setUniform("viewMatrix", camera.getViewMatrix());

                // Sun - yellow circle
                skyShader.setUniform("bodyColor", new org.joml.Vector3f(1.0f, 0.9f, 0.3f));
                drawCelestialBody(sunPos, sunDir, 4000.0f);

                // Moon - opposite side, blue-white circle
                org.joml.Vector3f moonPos = new org.joml.Vector3f(
                        camera.position.x - (sunX - camera.position.x),
                        camera.position.y - (sunY - camera.position.y),
                        camera.position.z);
                org.joml.Vector3f moonDir = new org.joml.Vector3f(moonPos).sub(camera.position).normalize();
                skyShader.setUniform("bodyColor", new org.joml.Vector3f(0.7f, 0.75f, 0.9f));
                drawCelestialBody(moonPos, moonDir, 3000.0f);

                skyShader.unbind();
                glEnable(GL_DEPTH_TEST);

                // --- Draw Chunks: Multi-Draw Indirect (one GPU call for all visible chunks) ---
                shader.bind();
                shader.setUniform("projectionMatrix", camera.getProjectionMatrix(aspect));
                shader.setUniform("viewMatrix", camera.getViewMatrix());
                shader.setUniform("renderMode", renderMode == 3 ? 0 : renderMode);
                shader.setUniform("dayBrightness", dayFactor);

                if (renderMode == 3) glPolygonMode(GL_FRONT_AND_BACK, GL_LINE);

                org.joml.Matrix4f viewProj = new org.joml.Matrix4f(camera.getProjectionMatrix(aspect))
                        .mul(camera.getViewMatrix());
                org.joml.FrustumIntersection frustum = new org.joml.FrustumIntersection(viewProj);

                // 1. Frustum cull and collect visible chunks
                java.util.ArrayList<Chunks.Chunk> visible = new java.util.ArrayList<>();
                for (Chunks.Chunk chunk : worldChunks.values()) {
                    if (chunk.faceCount == 0) continue;
                    float cMinX = chunk.chunkX * Chunks.Chunk.CHUNK_SIZE;
                    float cMinY = chunk.chunkY * Chunks.Chunk.CHUNK_SIZE;
                    float cMinZ = chunk.chunkZ * Chunks.Chunk.CHUNK_SIZE;
                    if (frustum.testAab(cMinX, cMinY, cMinZ,
                            cMinX + Chunks.Chunk.CHUNK_SIZE, cMinY + Chunks.Chunk.CHUNK_SIZE, cMinZ + Chunks.Chunk.CHUNK_SIZE)) {
                        visible.add(chunk);
                    }
                }

                int drawCount = visible.size();
                if (drawCount > 0) {
                    // 2. Assemble face data + indirect commands + positions
                    //    into pre-allocated buffers -- zero per-frame GC allocation.
                    
                    if (drawCount * 4 > mdiIndirectBuffer.capacity()) {
                        org.lwjgl.system.MemoryUtil.memFree(mdiIndirectBuffer);
                        org.lwjgl.system.MemoryUtil.memFree(mdiPosBuffer);
                        mdiIndirectBuffer = org.lwjgl.system.MemoryUtil.memAllocInt(drawCount * 5);
                        mdiPosBuffer      = org.lwjgl.system.MemoryUtil.memAllocFloat(drawCount * 5);
                    }

                    mdiIndirectBuffer.clear();
                    mdiPosBuffer.clear();
                    
                    glBindBuffer(GL_ARRAY_BUFFER, globalVboId);

                    for (int i = 0; i < drawCount; i++) {
                        Chunks.Chunk c = visible.get(i);
                        
                        // Wait, if c.vboOffset == -1, upload it to the GPU permanently in static VRAM limit
                        if (c.vboOffset == -1 && c.faceData != null && c.faceCount > 0) {
                            c.vboOffset = globalVboOffset;
                            glBufferSubData(GL_ARRAY_BUFFER, (long) c.vboOffset * Integer.BYTES, c.faceData);
                            globalVboOffset += c.faceCount;
                            
                            // Let the JVM GC sweep out internal integer face array since the GPU handles it indefinitely
                            c.faceData = null;  
                        }
                        
                        // If it has faces mapped (or just uploaded!), build indirect parameters referencing that memory bounds linearly.
                        if (c.vboOffset != -1) {
                            mdiIndirectBuffer.put(i * 4    , 4);
                            mdiIndirectBuffer.put(i * 4 + 1, c.faceCount);
                            mdiIndirectBuffer.put(i * 4 + 2, 0);
                            mdiIndirectBuffer.put(i * 4 + 3, c.vboOffset); // Instance bounds index perfectly
                            
                            mdiPosBuffer.put(i * 4    , c.chunkX * Chunks.Chunk.CHUNK_SIZE);
                            mdiPosBuffer.put(i * 4 + 1, c.chunkY * Chunks.Chunk.CHUNK_SIZE);
                            mdiPosBuffer.put(i * 4 + 2, c.chunkZ * Chunks.Chunk.CHUNK_SIZE);
                            mdiPosBuffer.put(i * 4 + 3, 0);
                        } else {
                            mdiIndirectBuffer.put(i * 4    , 0);
                            mdiIndirectBuffer.put(i * 4 + 1, 0);
                            mdiIndirectBuffer.put(i * 4 + 2, 0);
                            mdiIndirectBuffer.put(i * 4 + 3, 0);
                            
                            mdiPosBuffer.put(i * 4    , c.chunkX * Chunks.Chunk.CHUNK_SIZE);
                            mdiPosBuffer.put(i * 4 + 1, c.chunkY * Chunks.Chunk.CHUNK_SIZE);
                            mdiPosBuffer.put(i * 4 + 2, c.chunkZ * Chunks.Chunk.CHUNK_SIZE);
                            mdiPosBuffer.put(i * 4 + 3, 0);
                        }
                    }

                    glBindBuffer(GL_ARRAY_BUFFER, 0);

                    mdiIndirectBuffer.position(0);
                    mdiIndirectBuffer.limit(drawCount * 4);
                    mdiPosBuffer.position(0);
                    mdiPosBuffer.limit(drawCount * 4);



                    glBindBuffer(GL_DRAW_INDIRECT_BUFFER, globalIndirectBuf);
                    glBufferData(GL_DRAW_INDIRECT_BUFFER, (long) drawCount * 4 * Integer.BYTES, GL_DYNAMIC_DRAW);
                    glBufferSubData(GL_DRAW_INDIRECT_BUFFER, 0L, mdiIndirectBuffer);

                    glBindBufferBase(GL_SHADER_STORAGE_BUFFER, 0, globalPositionSSBO);
                    glBindBuffer(GL_SHADER_STORAGE_BUFFER, globalPositionSSBO);
                    glBufferData(GL_SHADER_STORAGE_BUFFER, (long) drawCount * 4 * Float.BYTES, GL_DYNAMIC_DRAW);
                    glBufferSubData(GL_SHADER_STORAGE_BUFFER, 0L, mdiPosBuffer);
                    glBindBuffer(GL_SHADER_STORAGE_BUFFER, 0);



                    // 4. One MDI call
                    glBindVertexArray(globalVaoId);
                    glBindBuffer(GL_DRAW_INDIRECT_BUFFER, globalIndirectBuf);
                    glMultiDrawArraysIndirect(GL_TRIANGLE_STRIP, 0L, drawCount, 0);
                    glBindVertexArray(0);
                    glBindBuffer(GL_DRAW_INDIRECT_BUFFER, 0);
                }

                shader.unbind();
                if (renderMode == 3) glPolygonMode(GL_FRONT_AND_BACK, GL_FILL);

                // --- Draw projectile and explosion cubes ---
                glDisable(GL_TEXTURE_2D);
                glEnable(GL_COLOR_MATERIAL);
                for (Projectile p : activeProjectiles) {
                    drawSolidCube(p.x, p.y, p.z, 0.08f, 1.0f, 0.7f, 0.2f, 1.0f);
                }
                for (CubeParticle part : activeCubeParticles) {
                    float alpha = Math.max(0.0f, Math.min(1.0f, part.life));
                    drawSolidCube(part.x, part.y, part.z, part.size, part.r, part.g, part.b, alpha);
                }

                // Draw Block Highlight in the 3D world!
                if (hasSelection) {
                    glMatrixMode(GL_PROJECTION);
                    glLoadMatrixf(camera.getProjectionMatrix(aspect).get(new float[16]));
                    glMatrixMode(GL_MODELVIEW);
                    glLoadMatrixf(camera.getViewMatrix().get(new float[16]));

                    glDisable(GL_TEXTURE_2D);
                    glEnable(GL_BLEND);
                    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
                    glLineWidth(3.0f);

                    float bx = selectedBlockX;
                    float by = selectedBlockY;
                    float bz = selectedBlockZ;

                    // Wireframe Box around block
                    glColor4f(1.0f, 1.0f, 1.0f, 1.0f); // Solid white line
                    float s = 1.002f; // Slightly larger to prevent Z-fighting
                    float o = -0.001f;

                    glBegin(GL_LINES);
                    // Bottom
                    glVertex3f(bx + o, by + o, bz + o);
                    glVertex3f(bx + o + s, by + o, bz + o);
                    glVertex3f(bx + o + s, by + o, bz + o);
                    glVertex3f(bx + o + s, by + o, bz + o + s);
                    glVertex3f(bx + o + s, by + o, bz + o + s);
                    glVertex3f(bx + o, by + o, bz + o + s);
                    glVertex3f(bx + o, by + o, bz + o + s);
                    glVertex3f(bx + o, by + o, bz + o);
                    // Top
                    glVertex3f(bx + o, by + o + s, bz + o);
                    glVertex3f(bx + o + s, by + o + s, bz + o);
                    glVertex3f(bx + o + s, by + o + s, bz + o);
                    glVertex3f(bx + o + s, by + o + s, bz + o + s);
                    glVertex3f(bx + o + s, by + o + s, bz + o + s);
                    glVertex3f(bx + o, by + o + s, bz + o + s);
                    glVertex3f(bx + o, by + o + s, bz + o + s);
                    glVertex3f(bx + o, by + o + s, bz + o);
                    // Verticals
                    glVertex3f(bx + o, by + o, bz + o);
                    glVertex3f(bx + o, by + o + s, bz + o);
                    glVertex3f(bx + o + s, by + o, bz + o);
                    glVertex3f(bx + o + s, by + o + s, bz + o);
                    glVertex3f(bx + o + s, by + o, bz + o + s);
                    glVertex3f(bx + o + s, by + o + s, bz + o + s);
                    glVertex3f(bx + o, by + o, bz + o + s);
                    glVertex3f(bx + o, by + o + s, bz + o + s);
                    glEnd();

                    // Draw face highlight slightly transparent white
                    glColor4f(1.0f, 1.0f, 1.0f, 0.2f);
                    float ps = 1.003f; // Face overlay must be slightly more offset
                    glBegin(GL_QUADS);
                    if (selectedFaceNormalY == 1) { // Top
                        glVertex3f(bx, by + ps, bz);
                        glVertex3f(bx, by + ps, bz + 1);
                        glVertex3f(bx + 1, by + ps, bz + 1);
                        glVertex3f(bx + 1, by + ps, bz);
                    } else if (selectedFaceNormalY == -1) { // Bottom
                        glVertex3f(bx, by - 0.003f, bz);
                        glVertex3f(bx + 1, by - 0.003f, bz);
                        glVertex3f(bx + 1, by - 0.003f, bz + 1);
                        glVertex3f(bx, by - 0.003f, bz + 1);
                    } else if (selectedFaceNormalX == 1) { // Right
                        glVertex3f(bx + ps, by, bz);
                        glVertex3f(bx + ps, by + 1, bz);
                        glVertex3f(bx + ps, by + 1, bz + 1);
                        glVertex3f(bx + ps, by, bz + 1);
                    } else if (selectedFaceNormalX == -1) { // Left
                        glVertex3f(bx - 0.003f, by, bz);
                        glVertex3f(bx - 0.003f, by, bz + 1);
                        glVertex3f(bx - 0.003f, by + 1, bz + 1);
                        glVertex3f(bx - 0.003f, by + 1, bz);
                    } else if (selectedFaceNormalZ == 1) { // Front
                        glVertex3f(bx, by, bz + ps);
                        glVertex3f(bx + 1, by, bz + ps);
                        glVertex3f(bx + 1, by + 1, bz + ps);
                        glVertex3f(bx, by + 1, bz + ps);
                    } else if (selectedFaceNormalZ == -1) { // Back
                        glVertex3f(bx, by, bz - 0.003f);
                        glVertex3f(bx, by + 1, bz - 0.003f);
                        glVertex3f(bx + 1, by + 1, bz - 0.003f);
                        glVertex3f(bx + 1, by, bz - 0.003f);
                    }
                    glEnd();

                    // Draw Ghost Block / Builder Volume Preview
                    glDisable(GL_CULL_FACE);
                    if (selectedSpecialAbility == 2 && isBuilderModeDragging) {
                        int endX = selectedBlockX + selectedFaceNormalX;
                        int endY = selectedBlockY + selectedFaceNormalY;
                        int endZ = selectedBlockZ + selectedFaceNormalZ;

                        float minX = Math.min(builderStartX, endX);
                        float maxX = Math.max(builderStartX, endX) + 1.0f;
                        float minY = Math.min(builderStartY, endY);
                        float maxY = Math.max(builderStartY, endY) + 1.0f;
                        float minZ = Math.min(builderStartZ, endZ);
                        float maxZ = Math.max(builderStartZ, endZ) + 1.0f;

                        glColor4f(1.0f, 1.0f, 1.0f, 0.4f); // Transparent white volume
                        glBegin(GL_QUADS);
                        // Bottom
                        glVertex3f(minX, minY, minZ);
                        glVertex3f(maxX, minY, minZ);
                        glVertex3f(maxX, minY, maxZ);
                        glVertex3f(minX, minY, maxZ);
                        // Top
                        glVertex3f(minX, maxY, minZ);
                        glVertex3f(maxX, maxY, minZ);
                        glVertex3f(maxX, maxY, maxZ);
                        glVertex3f(minX, maxY, maxZ);
                        // Front
                        glVertex3f(minX, minY, maxZ);
                        glVertex3f(maxX, minY, maxZ);
                        glVertex3f(maxX, maxY, maxZ);
                        glVertex3f(minX, maxY, maxZ);
                        // Back
                        glVertex3f(minX, minY, minZ);
                        glVertex3f(maxX, minY, minZ);
                        glVertex3f(maxX, maxY, minZ);
                        glVertex3f(minX, maxY, minZ);
                        // Left
                        glVertex3f(minX, minY, minZ);
                        glVertex3f(minX, minY, maxZ);
                        glVertex3f(minX, maxY, maxZ);
                        glVertex3f(minX, maxY, minZ);
                        // Right
                        glVertex3f(maxX, minY, minZ);
                        glVertex3f(maxX, minY, maxZ);
                        glVertex3f(maxX, maxY, maxZ);
                        glVertex3f(maxX, maxY, minZ);
                        glEnd();
                    } else if (selectedBlockType != TOOL_PROJECTILE) {
                        float gx = selectedBlockX + selectedFaceNormalX;
                        float gy = selectedBlockY + selectedFaceNormalY;
                        float gz = selectedBlockZ + selectedFaceNormalZ;
                        glColor4f(1.0f, 1.0f, 1.0f, 0.3f); // Transparent white single block
                        glBegin(GL_QUADS);
                        // Bottom
                        glVertex3f(gx, gy, gz);
                        glVertex3f(gx + 1, gy, gz);
                        glVertex3f(gx + 1, gy, gz + 1);
                        glVertex3f(gx, gy, gz + 1);
                        // Top
                        glVertex3f(gx, gy + 1, gz);
                        glVertex3f(gx + 1, gy + 1, gz);
                        glVertex3f(gx + 1, gy + 1, gz + 1);
                        glVertex3f(gx, gy + 1, gz + 1);
                        // Front
                        glVertex3f(gx, gy, gz + 1);
                        glVertex3f(gx + 1, gy, gz + 1);
                        glVertex3f(gx + 1, gy + 1, gz + 1);
                        glVertex3f(gx, gy + 1, gz + 1);
                        // Back
                        glVertex3f(gx, gy, gz);
                        glVertex3f(gx + 1, gy, gz);
                        glVertex3f(gx + 1, gy + 1, gz);
                        glVertex3f(gx, gy + 1, gz);
                        // Left
                        glVertex3f(gx, gy, gz);
                        glVertex3f(gx, gy, gz + 1);
                        glVertex3f(gx, gy + 1, gz + 1);
                        glVertex3f(gx, gy + 1, gz);
                        // Right
                        glVertex3f(gx + 1, gy, gz);
                        glVertex3f(gx + 1, gy, gz + 1);
                        glVertex3f(gx + 1, gy + 1, gz + 1);
                        glVertex3f(gx + 1, gy + 1, gz);
                        glEnd();
                    }
                    glEnable(GL_CULL_FACE);

                    glDisable(GL_BLEND);
                }

            } // end if (!blind)

            if (showDebugStatsAndGrid) {
                drawChunkGrid(aspect);
            }

            // Draw Crosshair using simple Ortho compatibility pass
            int[] wWidth = new int[1], wHeight = new int[1];
            glfwGetFramebufferSize(window, wWidth, wHeight);

            glDisable(GL_DEPTH_TEST);
            glDisable(GL_CULL_FACE);
            glBindVertexArray(0); // UNBIND VAO! Otherwise immediate mode (glBegin/glEnd) fails.
            glUseProgram(0);
            glMatrixMode(GL_PROJECTION);
            glPushMatrix();
            glLoadIdentity();
            glOrtho(0, wWidth[0], wHeight[0], 0, -1, 1);
            glMatrixMode(GL_MODELVIEW);
            glPushMatrix();
            glLoadIdentity();

            glColor4f(1.0f, 1.0f, 1.0f, 0.4f); // Semi-transparent white crosshair
            float cx = wWidth[0] / 2.0f;
            float cy = wHeight[0] / 2.0f;
            glLineWidth(2.0f);
            glBegin(GL_LINES);
            glVertex2f(cx - 10, cy);
            glVertex2f(cx + 10, cy);
            glVertex2f(cx, cy - 10);
            glVertex2f(cx, cy + 10);
            glEnd();

            // Always draw the two cooldown semicircles in the center!
            float dashCooldownRatio = Math.max(0.0f, Math.min(1.0f, dashCooldownTimer / DASH_COOLDOWN));
            float airBlockCooldownRatio = Math.max(0.0f, Math.min(1.0f, airBlockCooldownTimer / AIR_BLOCK_COOLDOWN));

            glEnable(GL_BLEND);
            glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
            glDisable(GL_TEXTURE_2D);
            glEnable(GL_POLYGON_SMOOTH); // Enable anti-aliasing for the circle
            glHint(GL_POLYGON_SMOOTH_HINT, GL_NICEST);

            float baseRadius = 14.0f;
            int halfSegments = 90; // Half circle

            float baseInner = baseRadius - 2.5f; // 5.0f thick
            float baseOuter = baseRadius + 2.5f;

            // 1. Draw Dash Semi-Circle (Blue, left side)
            // Left side corresponds to -180 to 0 degrees, or 90 to 270 depending on how you
            // map.
            // Let's say top is -90. Left side is -90 to -270.
            if (selectedSpecialAbility == 1) {
                // Apply bloom/glow by drawing a larger blurred quad or just stronger color
                glColor4f(0.0f, 0.4f, 1.0f, 0.9f);
            } else {
                glColor4f(1.0f, 1.0f, 1.0f, 0.3f);
            }
            glBegin(GL_TRIANGLE_STRIP);
            for (int i = 0; i <= halfSegments; i++) {
                float angle = (float) Math.toRadians(-90.0f - (i * 180.0f / halfSegments));
                glVertex2f(cx + baseInner * (float) Math.cos(angle), cy + baseInner * (float) Math.sin(angle));
                glVertex2f(cx + baseOuter * (float) Math.cos(angle), cy + baseOuter * (float) Math.sin(angle));
            }
            glEnd();

            // Dash Blue Progress
            float dashRecovered = 1.0f - dashCooldownRatio;
            if (dashRecovered > 0.0f) {
                float blueInner = baseRadius - 1.5f;
                float blueOuter = baseRadius + 1.5f;
                int arcSegments = Math.max(1, (int) (halfSegments * dashRecovered));
                float sweepAngle = dashRecovered * 180.0f;

                if (selectedSpecialAbility == 1) {
                    glColor4f(0.0f, 0.8f, 1.0f, 1.0f); // Solid bright blue
                } else {
                    glColor4f(0.0f, 0.5f, 0.8f, 0.3f); // Dimmed out
                }
                glBegin(GL_TRIANGLE_STRIP);
                for (int i = 0; i <= arcSegments; i++) {
                    float angle = (float) Math.toRadians(-90.0f - (sweepAngle * i / arcSegments));
                    glVertex2f(cx + blueInner * (float) Math.cos(angle), cy + blueInner * (float) Math.sin(angle));
                    glVertex2f(cx + blueOuter * (float) Math.cos(angle), cy + blueOuter * (float) Math.sin(angle));
                }
                glEnd();
            }

            // 2. Draw Air Block Semi-Circle (Yellowish white, right side)
            // Right side is -90 to +90.
            if (selectedSpecialAbility == 2) {
                glColor4f(1.0f, 1.0f, 0.6f, 0.9f);
            } else {
                glColor4f(1.0f, 1.0f, 1.0f, 0.3f);
            }
            glBegin(GL_TRIANGLE_STRIP);
            for (int i = 0; i <= halfSegments; i++) {
                float angle = (float) Math.toRadians(-90.0f + (i * 180.0f / halfSegments));
                glVertex2f(cx + baseInner * (float) Math.cos(angle), cy + baseInner * (float) Math.sin(angle));
                glVertex2f(cx + baseOuter * (float) Math.cos(angle), cy + baseOuter * (float) Math.sin(angle));
            }
            glEnd();

            // Air Block Yellow Progress
            float airRecovered = 1.0f - airBlockCooldownRatio;
            if (airRecovered > 0.0f) {
                float yellInner = baseRadius - 1.5f;
                float yellOuter = baseRadius + 1.5f;
                int arcSegments = Math.max(1, (int) (halfSegments * airRecovered));
                float sweepAngle = airRecovered * 180.0f;

                if (selectedSpecialAbility == 2) {
                    glColor4f(1.0f, 1.0f, 0.8f, 1.0f); // Bright yellowish white
                } else {
                    glColor4f(0.8f, 0.8f, 0.6f, 0.3f); // Dimmed out
                }
                glBegin(GL_TRIANGLE_STRIP);
                for (int i = 0; i <= arcSegments; i++) {
                    float angle = (float) Math.toRadians(-90.0f + (sweepAngle * i / arcSegments));
                    glVertex2f(cx + yellInner * (float) Math.cos(angle), cy + yellInner * (float) Math.sin(angle));
                    glVertex2f(cx + yellOuter * (float) Math.cos(angle), cy + yellOuter * (float) Math.sin(angle));
                }
                glEnd();
            }

            glDisable(GL_BLEND);
            glDisable(GL_POLYGON_SMOOTH);
            glLineWidth(2.0f); // Restore

            if (introFadeAlpha > 0.0f) {
                glEnable(GL_BLEND);
                glColor4f(0.0f, 0.0f, 0.0f, introFadeAlpha);
                glBegin(GL_QUADS);
                glVertex2f(0, 0);
                glVertex2f(wWidth[0], 0);
                glVertex2f(wWidth[0], wHeight[0]);
                glVertex2f(0, wHeight[0]);
                glEnd();
                glDisable(GL_BLEND);
            }

            glPopMatrix();
            glMatrixMode(GL_PROJECTION);
            glPopMatrix();
            glMatrixMode(GL_MODELVIEW);
            glEnable(GL_DEPTH_TEST);
            glEnable(GL_CULL_FACE);

            long renderEndNano = System.nanoTime();
            // Smooth out the timings slightly over frames, or simply sample
            cpuTimeMs = (renderEndNano - frameStartNano) / 1000000.0f; // We'll just measure the whole loop up to
                                                                       // SwapBuffers as CPU time

            // Keep title clean by default; F3 toggles detailed debug stats.
            String title;
            if (showDebugStatsAndGrid) {
                title = String.format(
                        "Java 3D Voxel Engine | FPS: %d | TPS: %d | CPU: %.2fms | Render: %.2fms | Vel: %.2f, %.2f, %.2f | Pos: X:%.1f Y:%.1f Z:%.1f | Dash:%.2fs",
                        fps, tps, cpuTimeMs, gpuTimeMs, velocityX, velocityY, velocityZ, camera.position.x,
                        camera.position.y,
                        camera.position.z, dashCooldownTimer);
            } else {
                title = "Java 3D Voxel Engine";
            }
            glfwSetWindowTitle(window, title);

            long swapStart = System.nanoTime();
            glfwSwapBuffers(window);
            long swapEnd = System.nanoTime();

            // Swap buffers blocks for vsync and is truly the GPU's rendering pipeline time
            // + waiting
            // To get purely the CPU cost of issuing Render commands we could time the
            // OpenGL draw calls, but usually CPU+Render + Sync is informative enough
            gpuTimeMs = (swapEnd - swapStart) / 1000000.0f;

            glfwPollEvents();
        }
    }

    private void rebuildChunkAndNeighbors(Chunks.Chunk c) {
        c.buildMesh(this::getBlockGlobal);
        int[][] dirs = { { 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 } };
        for (int[] d : dirs) {
            Chunks.Chunk nb = worldChunks.get(packChunkKey(c.chunkX + d[0], c.chunkY + d[1], c.chunkZ + d[2]));
            if (nb != null)
                nb.buildMesh(this::getBlockGlobal);
        }
    }

    public void breakBlock() {
        if (!hasSelection)
            return;
        Chunks.Chunk c = setBlockGlobal(selectedBlockX, selectedBlockY, selectedBlockZ, (byte) 0);
        if (c != null) {
            Audio.playSound("Asset/sound/block" + (int) (Math.random() * 4 + 1) + ".wav", 1.0f, 1.0f);
            rebuildChunkAndNeighbors(c);
        }
    }

    private void placeBuilderVolume() {
        if (!hasSelection)
            return;
        int endX = selectedBlockX + selectedFaceNormalX;
        int endY = selectedBlockY + selectedFaceNormalY;
        int endZ = selectedBlockZ + selectedFaceNormalZ;

        int minX = Math.min(builderStartX, endX);
        int maxX = Math.max(builderStartX, endX);
        int minY = Math.min(builderStartY, endY);
        int maxY = Math.max(builderStartY, endY);
        int minZ = Math.min(builderStartZ, endZ);
        int maxZ = Math.max(builderStartZ, endZ);

        // Cap size to avoid freezing the game
        if ((maxX - minX + 1) * (maxY - minY + 1) * (maxZ - minZ + 1) > 100000) {
            System.out.println("Volume too large!");
            return;
        }

        boolean isCrouching = keys[GLFW_KEY_LEFT_SHIFT];
        float ph = isCrouching ? 1.5f : 1.61f;
        float pMinX = camera.position.x - 0.3f;
        float pMaxX = camera.position.x + 0.3f;
        float pMinY = camera.position.y - ph;
        float pMaxY = camera.position.y + 0.18f;
        float pMinZ = camera.position.z - 0.3f;
        float pMaxZ = camera.position.z + 0.3f;

        boolean playedSound = false;
        HashSet<Chunks.Chunk> chunksToUpdate = new HashSet<>();

        for (int x = minX; x <= maxX; x++) {
            for (int y = minY; y <= maxY; y++) {
                for (int z = minZ; z <= maxZ; z++) {
                    // Skip blocks that would overlap the player
                    if (checkAABBIntersect(pMinX, pMinY, pMinZ, pMaxX, pMaxY, pMaxZ,
                            x, y, z, x + 1.0f, y + 1.0f, z + 1.0f))
                        continue;
                    if (getBlockGlobal(x, y, z) == 0) {
                        Chunks.Chunk c = setBlockGlobal(x, y, z, selectedBlockType);
                        if (c != null)
                            chunksToUpdate.add(c);
                        if (!playedSound) {
                            Audio.playSound("Asset/sound/block" + (int) (Math.random() * 4 + 1) + ".wav", 1.0f,
                                    1.0f);
                            playedSound = true;
                        }
                    }
                }
            }
        }

        for (Chunks.Chunk c : chunksToUpdate) {
            rebuildChunkAndNeighbors(c);
        }
    }

    private void placeBlock() {
        if (selectedBlockType == TOOL_PROJECTILE) {
            float px = camera.position.x;
            float py = camera.position.y;
            float pz = camera.position.z;
            float yawRad = (float) Math.toRadians(camera.yaw);
            float pitchRad = (float) Math.toRadians(camera.pitch);
            float dirX = (float) (Math.sin(yawRad) * Math.cos(pitchRad));
            float dirY = (float) -Math.sin(pitchRad);
            float dirZ = (float) (-Math.cos(yawRad) * Math.cos(pitchRad));
            spawnProjectile(px, py, pz, dirX, dirY, dirZ);
            Audio.playSound("Asset/sound/block" + (int) (Math.random() * 4 + 1) + ".wav", 1.0f, 1.0f);
            return;
        }

        if (!hasSelection)
            return;
        int placeX = selectedBlockX + selectedFaceNormalX;
        int placeY = selectedBlockY + selectedFaceNormalY;
        int placeZ = selectedBlockZ + selectedFaceNormalZ;

        boolean isCrouching = keys[GLFW_KEY_LEFT_SHIFT];
        float ph = isCrouching ? 1.5f : 1.61f;

        // Player AABB
        float pMinX = camera.position.x - 0.3f;
        float pMaxX = camera.position.x + 0.3f;
        float pMinY = camera.position.y - ph;
        float pMaxY = camera.position.y + 0.18f;
        float pMinZ = camera.position.z - 0.3f;
        float pMaxZ = camera.position.z + 0.3f;

        // Block AABB
        float bMinX = placeX;
        float bMaxX = placeX + 1.0f;
        float bMinY = placeY;
        float bMaxY = placeY + 1.0f;
        float bMinZ = placeZ;
        float bMaxZ = placeZ + 1.0f;

        if (!checkAABBIntersect(pMinX, pMinY, pMinZ, pMaxX, pMaxY, pMaxZ, bMinX, bMinY, bMinZ, bMaxX, bMaxY, bMaxZ)) {
            Chunks.Chunk c = setBlockGlobal(placeX, placeY, placeZ, selectedBlockType);
            if (c != null) {
                Audio.playSound("Asset/sound/block" + (int) (Math.random() * 4 + 1) + ".wav", 1.0f, 1.0f);
                rebuildChunkAndNeighbors(c);
            }
        }
    }

    private void spawnProjectile(float px, float py, float pz, float dirX, float dirY, float dirZ) {
        Projectile p = new Projectile();
        p.x = px + dirX * 0.6f;
        p.y = py + dirY * 0.6f;
        p.z = pz + dirZ * 0.6f;
        float speed = 32.0f;
        p.vx = dirX * speed;
        p.vy = dirY * speed;
        p.vz = dirZ * speed;
        p.life = 3.0f;
        activeProjectiles.add(p);
    }

    private void explodeIntoCubes(float x, float y, float z) {
        for (int i = 0; i < 42; i++) {
            float dx = rng.nextFloat() * 2.0f - 1.0f;
            float dy = rng.nextFloat() * 2.0f - 1.0f;
            float dz = rng.nextFloat() * 2.0f - 1.0f;
            float len = (float) Math.sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 0.0001f) {
                dx = 0.0f;
                dy = 1.0f;
                dz = 0.0f;
                len = 1.0f;
            }
            dx /= len;
            dy /= len;
            dz /= len;

            CubeParticle part = new CubeParticle();
            part.x = x;
            part.y = y;
            part.z = z;
            float speed = 3.0f + rng.nextFloat() * 7.0f;
            part.vx = dx * speed;
            part.vy = dy * speed;
            part.vz = dz * speed;
            part.life = 0.35f + rng.nextFloat() * 0.75f;
            part.size = 0.05f + rng.nextFloat() * 0.09f;
            part.r = 0.35f + rng.nextFloat() * 0.25f;
            part.g = 0.25f + rng.nextFloat() * 0.20f;
            part.b = 0.15f + rng.nextFloat() * 0.15f;
            activeCubeParticles.add(part);
        }
    }

    private void updateProjectiles(float dt) {
        Iterator<Projectile> pit = activeProjectiles.iterator();
        while (pit.hasNext()) {
            Projectile p = pit.next();
            p.life -= dt;
            if (p.life <= 0.0f) {
                pit.remove();
                continue;
            }

            p.vy -= 8.0f * dt;
            float nextX = p.x + p.vx * dt;
            float nextY = p.y + p.vy * dt;
            float nextZ = p.z + p.vz * dt;

            float segDx = nextX - p.x;
            float segDy = nextY - p.y;
            float segDz = nextZ - p.z;
            float segLen = (float) Math.sqrt(segDx * segDx + segDy * segDy + segDz * segDz);
            int steps = Math.max(1, (int) Math.ceil(segLen / 0.2f));
            boolean hit = false;

            for (int s = 1; s <= steps; s++) {
                float t = (float) s / (float) steps;
                float sx = p.x + segDx * t;
                float sy = p.y + segDy * t;
                float sz = p.z + segDz * t;
                byte b = getBlockGlobal((int) Math.floor(sx), (int) Math.floor(sy), (int) Math.floor(sz));
                if (isSolidBlock(b)) {
                    explodeIntoCubes(sx, sy, sz);
                    pit.remove();
                    hit = true;
                    break;
                }
            }

            if (!hit) {
                p.x = nextX;
                p.y = nextY;
                p.z = nextZ;
            }
        }

        Iterator<CubeParticle> cit = activeCubeParticles.iterator();
        while (cit.hasNext()) {
            CubeParticle part = cit.next();
            part.life -= dt;
            if (part.life <= 0.0f) {
                cit.remove();
                continue;
            }

            part.vy -= 14.0f * dt;
            float nx = part.x + part.vx * dt;
            float ny = part.y + part.vy * dt;
            float nz = part.z + part.vz * dt;

            byte b = getBlockGlobal((int) Math.floor(nx), (int) Math.floor(ny), (int) Math.floor(nz));
            if (isSolidBlock(b)) {
                part.vx *= -0.25f;
                part.vy *= -0.35f;
                part.vz *= -0.25f;
            } else {
                part.x = nx;
                part.y = ny;
                part.z = nz;
            }
        }
    }

    private void drawSolidCube(float x, float y, float z, float s, float r, float g, float b, float a) {
        glColor4f(r, g, b, a);
        float x0 = x - s;
        float x1 = x + s;
        float y0 = y - s;
        float y1 = y + s;
        float z0 = z - s;
        float z1 = z + s;

        glBegin(GL_QUADS);
        glVertex3f(x0, y0, z1);
        glVertex3f(x1, y0, z1);
        glVertex3f(x1, y1, z1);
        glVertex3f(x0, y1, z1);
        glVertex3f(x1, y0, z0);
        glVertex3f(x0, y0, z0);
        glVertex3f(x0, y1, z0);
        glVertex3f(x1, y1, z0);
        glVertex3f(x0, y1, z0);
        glVertex3f(x0, y1, z1);
        glVertex3f(x1, y1, z1);
        glVertex3f(x1, y1, z0);
        glVertex3f(x0, y0, z1);
        glVertex3f(x0, y0, z0);
        glVertex3f(x1, y0, z0);
        glVertex3f(x1, y0, z1);
        glVertex3f(x1, y0, z1);
        glVertex3f(x1, y0, z0);
        glVertex3f(x1, y1, z0);
        glVertex3f(x1, y1, z1);
        glVertex3f(x0, y0, z0);
        glVertex3f(x0, y0, z1);
        glVertex3f(x0, y1, z1);
        glVertex3f(x0, y1, z0);
        glEnd();
    }

    private void handleInput(float dt) {
        if (dashCooldownTimer > 0.0f) {
            dashCooldownTimer = Math.max(0.0f, dashCooldownTimer - dt);
        }
        if (airBlockCooldownTimer > 0.0f) {
            airBlockCooldownTimer = Math.max(0.0f, airBlockCooldownTimer - dt);
        }
        if (waterJumpCooldownTimer > 0.0f) {
            waterJumpCooldownTimer = Math.max(0.0f, waterJumpCooldownTimer - dt);
        }

        // Left-click: mine block
        if (isLeftMouseDown && hasSelection) {
            long bKey = packBlockKey(selectedBlockX, selectedBlockY, selectedBlockZ);

            // If we moved to a new block, we optionally reset or just continue.
            // The map lets us store health indefinitely.
            float hp = blockHealthMap.getOrDefault(bKey, BLOCK_MAX_HEALTH);
            hp -= dt;

            if (hp <= 0.0f) {
                breakBlock();
                blockHealthMap.remove(bKey);
            } else {
                blockHealthMap.put(bKey, hp);
            }
        }

        if (isRightMouseDown) {
            rightMouseTimer -= dt;
            if (rightMouseTimer <= 0.0f) {
                rightMouseTimer = MOUSE_HOLD_DELAY;
                placeBlock();
            }
        }

        boolean isCrouching = keys[GLFW_KEY_LEFT_SHIFT];
        boolean isJumpPressed = keys[GLFW_KEY_SPACE] || (glfwGetKey(window, GLFW_KEY_SPACE) == GLFW_PRESS);
        boolean isJumpPressedEdge = isJumpPressed && !wasSpacePressed;

        // Let the player maintain their sprint through a jump!
        if (!keys[GLFW_KEY_W] || isCrouching) {
            isSprintingState = false;
        }
        if (keys[GLFW_KEY_LEFT_CONTROL] && keys[GLFW_KEY_W]) {
            isSprintingState = true;
        }

        float ph = isCrouching ? 1.5f : 1.61f; // Player fixed height golden ratio
        boolean isInWater = isPlayerInWater(ph);

        float targetSpeed = Config.BASE_SPEED;
        boolean isSwimSprint = isInWater && keys[GLFW_KEY_LEFT_CONTROL];
        boolean isSwimSlow = isInWater && keys[GLFW_KEY_LEFT_SHIFT];
        if (isInWater) {
            targetSpeed = Config.SWIM_SPEED;
            if (isSwimSprint) {
                targetSpeed *= Config.SWIM_SPRINT_MULT;
            }
            if (isSwimSlow) {
                targetSpeed *= Config.SWIM_SLOW_MULT;
            }
        } else if (isCrouching) {
            targetSpeed = Config.BASE_SPEED * Config.CROUCH_SPEED_MULT;
        } else if (isSprintingState) {
            targetSpeed = Config.SPRINT_SPEED;
        }

        float yawRad = (float) Math.toRadians(camera.yaw);
        float pitchRad = (float) Math.toRadians(camera.pitch);

        if (isInWater) {
            float lookX = (float) (Math.sin(yawRad) * Math.cos(pitchRad));
            float lookY = (float) -Math.sin(pitchRad);
            float lookZ = (float) (-Math.cos(yawRad) * Math.cos(pitchRad));
            float rightX = (float) Math.sin(Math.toRadians(camera.yaw + 90.0f));
            float rightZ = (float) -Math.cos(Math.toRadians(camera.yaw + 90.0f));

            float rawSwimX = 0.0f;
            float rawSwimY = 0.0f;
            float rawSwimZ = 0.0f;

            if (keys[GLFW_KEY_W]) {
                rawSwimX += lookX;
                rawSwimY += lookY;
                rawSwimZ += lookZ;
            }
            if (keys[GLFW_KEY_S]) {
                rawSwimX -= lookX;
                rawSwimY -= lookY;
                rawSwimZ -= lookZ;
            }
            if (keys[GLFW_KEY_A]) {
                rawSwimX -= rightX;
                rawSwimZ -= rightZ;
            }
            if (keys[GLFW_KEY_D]) {
                rawSwimX += rightX;
                rawSwimZ += rightZ;
            }

            float swimLength = (float) Math.sqrt(rawSwimX * rawSwimX + rawSwimY * rawSwimY + rawSwimZ * rawSwimZ);
            float targetVelX = 0.0f;
            float targetVelY = -Config.SWIM_SINK_SPEED;
            float targetVelZ = 0.0f;
            if (swimLength > 0.0f) {
                targetVelX = (rawSwimX / swimLength) * targetSpeed;
                targetVelY = (rawSwimY / swimLength) * targetSpeed;
                targetVelZ = (rawSwimZ / swimLength) * targetSpeed;
            }

            if (isJumpPressed) {
                targetVelY += Config.SWIM_RISE_SPEED;
            }

            float waterControl = Math.min(1.0f, Config.SWIM_CONTROL * dt);
            velocityX += (targetVelX - velocityX) * waterControl;
            velocityY += (targetVelY - velocityY) * waterControl;
            velocityZ += (targetVelZ - velocityZ) * waterControl;

            isGrounded = false;
        } else {
            // Horizontal Movement logic
            float rawDx = 0, rawDz = 0;

            if (keys[GLFW_KEY_W]) {
                rawDx += (float) Math.sin(yawRad);
                rawDz -= (float) Math.cos(yawRad);
            }
            if (keys[GLFW_KEY_S]) {
                rawDx -= (float) Math.sin(yawRad);
                rawDz += (float) Math.cos(yawRad);
            }
            if (keys[GLFW_KEY_A]) {
                rawDx += (float) Math.sin(Math.toRadians(camera.yaw - 90));
                rawDz -= (float) Math.cos(Math.toRadians(camera.yaw - 90));
            }
            if (keys[GLFW_KEY_D]) {
                rawDx += (float) Math.sin(Math.toRadians(camera.yaw + 90));
                rawDz -= (float) Math.cos(Math.toRadians(camera.yaw + 90));
            }

            // Normalize direction vector to prevent faster diagonal movement
            float length = (float) Math.sqrt(rawDx * rawDx + rawDz * rawDz);
            if (length > 0) {
                velocityX = (rawDx / length) * targetSpeed;
                velocityZ = (rawDz / length) * targetSpeed;
            } else {
                velocityX = 0;
                velocityZ = 0;
            }
        }

        // Smoothly interpolate FOV based on actual horizontal velocity
        float targetFov = isSprintingState ? Config.SPRINT_FOV : Config.BASE_FOV;
        currentFov += (targetFov - currentFov) * Config.FOV_TRANSITION_SPEED * dt;
        camera.setFov(currentFov);

        float dx = velocityX * dt;
        float dz = velocityZ * dt;

        if (!isGrounded) {
            coyoteTimeTimer += dt;
        }

        if (!isInWater) {
            // Check for Special Abilities (Air Dash, Air Block)
            boolean isXPressed = keys[GLFW_KEY_X] || (glfwGetKey(window, GLFW_KEY_X) == GLFW_PRESS);
            if (isXPressed) {
                if (selectedSpecialAbility != 0) {
                    System.out.println("Special Ability Canceled!");
                }
                selectedSpecialAbility = 0; // Press X to cancel special ability
            }

            // Normal Gravity
            velocityY -= Config.GRAVITY * dt;

            // Air Friction (Horizontal)
            velocityX -= velocityX * (1.0f - Config.AIR_SLIPPERINESS) * dt * 60.0f;
            velocityZ -= velocityZ * (1.0f - Config.AIR_SLIPPERINESS) * dt * 60.0f;

            if (isJumpPressed && (isGrounded || coyoteTimeTimer < 0.15f)) {
                if (selectedSpecialAbility == 1 && dashCooldownTimer <= 0.0f) {
                    // Massive Launch jump (100+ blocks high jump)
                    // v = sqrt(2 * g * h). 2 * 32.0f * 100.0f = 6400, sqrt = 80.0f
                    velocityY = 80.0f;
                    dashCooldownTimer = DASH_COOLDOWN; // Reuse dash cooldown timer
                    System.out.println("Launch Activated!");
                } else {
                    velocityY = Config.JUMP_VELOCITY;
                }
                isGrounded = false;
                coyoteTimeTimer = 100.0f; // Consume coyote time so no multiple jumps
            }
        }
        float dy = velocityY * dt;

        // Prevent feet from snapping up/down by adjusting eye position to compensate
        // for height change
        if (isCrouching && !wasCrouching) {
            camera.position.y -= (1.61f - 1.5f);
        } else if (!isCrouching && wasCrouching) {
            camera.position.y += (1.61f - 1.5f);
        }
        wasCrouching = isCrouching;

        // AABB parameters
        float pRadiusX = 0.3f; // Player half-width
        float pRadiusZ = 0.3f; // Player half-depth
        float pHeight = ph; // Player height
        float pEyeY = 0.18f; // Camera offset above feet

        // --- 1. Move X ---
        float nextX = camera.position.x + dx;
        float minX = nextX - pRadiusX;
        float maxX = nextX + pRadiusX;
        float minY = camera.position.y - pHeight;
        float maxY = camera.position.y + pEyeY;
        float minZ = camera.position.z - pRadiusZ;
        float maxZ = camera.position.z + pRadiusZ;

        boolean collisionX = false;
        for (int x = (int) Math.floor(minX); x <= (int) Math.floor(maxX); x++) {
            for (int y = (int) Math.floor(minY); y <= (int) Math.floor(maxY); y++) {
                for (int z = (int) Math.floor(minZ); z <= (int) Math.floor(maxZ); z++) {
                    if (isSolidBlock(getBlockGlobal(x, y, z)))
                        collisionX = true;
                }
            }
        }
        // Crouch Edge Detection
        if (!collisionX && isCrouching && isGrounded && !isInWater) {
            if (!isSupportedAt(nextX, camera.position.z, camera.position.y, pRadiusX, pRadiusZ, pHeight)) {
                dx = 0;
            }
        }

        if (collisionX) {
            if (dx > 0) { // Moving positive X, hit left face of block
                camera.position.x = (float) Math.floor(maxX) - pRadiusX - 0.001f;
            } else if (dx < 0) { // Moving negative X, hit right face of block
                camera.position.x = (float) Math.floor(minX) + 1.0f + pRadiusX + 0.001f;
            }
            velocityX = 0;
            velocityZ = 0;
        } else {
            camera.position.x = nextX;
        }

        // --- 2. Move Y ---
        float nextY = camera.position.y + dy;
        minX = camera.position.x - pRadiusX; // Update with resolved X
        maxX = camera.position.x + pRadiusX;
        minY = nextY - pHeight;
        maxY = nextY + pEyeY;
        minZ = camera.position.z - pRadiusZ; // Z is still original
        maxZ = camera.position.z + pRadiusZ;

        boolean collisionY = false;
        isGrounded = false;
        for (int x = (int) Math.floor(minX); x <= (int) Math.floor(maxX); x++) {
            for (int y = (int) Math.floor(minY); y <= (int) Math.floor(maxY); y++) {
                for (int z = (int) Math.floor(minZ); z <= (int) Math.floor(maxZ); z++) {
                    if (isSolidBlock(getBlockGlobal(x, y, z)))
                        collisionY = true;
                }
            }
        }
        if (collisionY) {
            if (dy < 0) { // Falling, hit top of block
                camera.position.y = (float) Math.floor(minY) + 1.0f + pHeight + 0.001f;
                isGrounded = true;
                coyoteTimeTimer = 0.0f; // Reset coyote timer when grounded
            } else if (dy > 0) { // Jumping, hit bottom of block
                camera.position.y = (float) Math.floor(maxY) - pEyeY - 0.001f;
            }
            velocityY = 0;
        } else {
            camera.position.y = nextY;
        }

        // --- 3. Move Z ---
        float nextZ = camera.position.z + dz;
        minX = camera.position.x - pRadiusX; // Resolved X
        maxX = camera.position.x + pRadiusX;
        minY = camera.position.y - pHeight; // Resolved Y
        maxY = camera.position.y + pEyeY;
        minZ = nextZ - pRadiusZ;
        maxZ = nextZ + pRadiusZ;

        boolean collisionZ = false;
        for (int x = (int) Math.floor(minX); x <= (int) Math.floor(maxX); x++) {
            for (int y = (int) Math.floor(minY); y <= (int) Math.floor(maxY); y++) {
                for (int z = (int) Math.floor(minZ); z <= (int) Math.floor(maxZ); z++) {
                    if (isSolidBlock(getBlockGlobal(x, y, z)))
                        collisionZ = true;
                }
            }
        }
        // Crouch Edge Detection
        if (!collisionZ && isCrouching && isGrounded && !isInWater) {
            if (!isSupportedAt(camera.position.x, nextZ, camera.position.y, pRadiusX, pRadiusZ, pHeight)) {
                collisionZ = true;
                dz = 0;
            }
        }

        if (collisionZ) {
            if (dz > 0) { // Moving positive Z, hit front face of block
                camera.position.z = (float) Math.floor(maxZ) - pRadiusZ - 0.001f;
            } else if (dz < 0) { // Moving negative Z, hit back face of block
                camera.position.z = (float) Math.floor(minZ) + 1.0f + pRadiusZ + 0.001f;
            }
            velocityX = 0;
            velocityZ = 0;
        } else {
            camera.position.z = nextZ;
        }

        wasSpacePressed = isJumpPressed;
    }

    private void drawCelestialBody(org.joml.Vector3f pos, org.joml.Vector3f dir, float size) {
        org.joml.Vector3f up = new org.joml.Vector3f(0, 0, 1);
        org.joml.Vector3f right = new org.joml.Vector3f(dir).cross(up).normalize();
        up = new org.joml.Vector3f(right).cross(dir).normalize();
        right.mul(size);
        up.mul(size);

        glBegin(GL_QUADS);
        glVertexAttrib2f(1, -1.0f, -1.0f);
        glVertex3f(pos.x - right.x - up.x, pos.y - right.y - up.y, pos.z - right.z - up.z);
        glVertexAttrib2f(1, 1.0f, -1.0f);
        glVertex3f(pos.x + right.x - up.x, pos.y + right.y - up.y, pos.z + right.z - up.z);
        glVertexAttrib2f(1, 1.0f, 1.0f);
        glVertex3f(pos.x + right.x + up.x, pos.y + right.y + up.y, pos.z + right.z + up.z);
        glVertexAttrib2f(1, -1.0f, 1.0f);
        glVertex3f(pos.x - right.x + up.x, pos.y - right.y + up.y, pos.z - right.z + up.z);
        glEnd();
    }

    private boolean isSupportedAt(float posX, float posZ, float posY, float pRadiusX, float pRadiusZ, float pHeight) {
        float minX = posX - pRadiusX;
        float maxX = posX + pRadiusX;
        int yCheck = (int) Math.floor(posY - pHeight - 0.05f);
        float minZ = posZ - pRadiusZ;
        float maxZ = posZ + pRadiusZ;

        for (int x = (int) Math.floor(minX); x <= (int) Math.floor(maxX); x++) {
            for (int z = (int) Math.floor(minZ); z <= (int) Math.floor(maxZ); z++) {
                if (isSolidBlock(getBlockGlobal(x, yCheck, z)))
                    return true;
            }
        }
        return false;
    }

    private boolean isSolidBlock(byte blockType) {
        return blockType != Chunks.Chunk.AIR && blockType != Chunks.Chunk.WATER;
    }

    private boolean isPlayerInWater(float playerHeight) {
        float minX = camera.position.x - 0.3f;
        float maxX = camera.position.x + 0.3f;
        float minY = camera.position.y - playerHeight;
        float maxY = camera.position.y + 0.18f;
        float minZ = camera.position.z - 0.3f;
        float maxZ = camera.position.z + 0.3f;

        for (int x = (int) Math.floor(minX); x <= (int) Math.floor(maxX); x++) {
            for (int y = (int) Math.floor(minY); y <= (int) Math.floor(maxY); y++) {
                for (int z = (int) Math.floor(minZ); z <= (int) Math.floor(maxZ); z++) {
                    if (getBlockGlobal(x, y, z) == Chunks.Chunk.WATER) {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private boolean checkAABBIntersect(float minX1, float minY1, float minZ1, float maxX1, float maxY1, float maxZ1,
            float minX2, float minY2, float minZ2, float maxX2, float maxY2, float maxZ2) {
        return (minX1 < maxX2 && maxX1 > minX2) &&
                (minY1 < maxY2 && maxY1 > minY2) &&
                (minZ1 < maxZ2 && maxZ1 > minZ2);
    }

    private void updateRaycast() {
        hasSelection = false;
        float px = camera.position.x;
        float py = camera.position.y;
        float pz = camera.position.z;

        float yawRad = (float) Math.toRadians(camera.yaw);
        float pitchRad = (float) Math.toRadians(camera.pitch);

        float dirX = (float) (Math.sin(yawRad) * Math.cos(pitchRad));
        float dirY = (float) -Math.sin(pitchRad);
        float dirZ = (float) (-Math.cos(yawRad) * Math.cos(pitchRad));

        int x = (int) Math.floor(px);
        int y = (int) Math.floor(py);
        int z = (int) Math.floor(pz);

        int stepX = (int) Math.signum(dirX);
        int stepY = (int) Math.signum(dirY);
        int stepZ = (int) Math.signum(dirZ);

        float tDeltaX = stepX != 0 ? Math.abs(1.0f / dirX) : Float.POSITIVE_INFINITY;
        float tDeltaY = stepY != 0 ? Math.abs(1.0f / dirY) : Float.POSITIVE_INFINITY;
        float tDeltaZ = stepZ != 0 ? Math.abs(1.0f / dirZ) : Float.POSITIVE_INFINITY;

        float tMaxX = stepX != 0
                ? (stepX > 0 ? ((float) Math.floor(px) + 1.0f - px) * tDeltaX : (px - (float) Math.floor(px)) * tDeltaX)
                : Float.POSITIVE_INFINITY;
        float tMaxY = stepY != 0
                ? (stepY > 0 ? ((float) Math.floor(py) + 1.0f - py) * tDeltaY : (py - (float) Math.floor(py)) * tDeltaY)
                : Float.POSITIVE_INFINITY;
        float tMaxZ = stepZ != 0
                ? (stepZ > 0 ? ((float) Math.floor(pz) + 1.0f - pz) * tDeltaZ : (pz - (float) Math.floor(pz)) * tDeltaZ)
                : Float.POSITIVE_INFINITY;

        int normX = 0, normY = 0, normZ = 0;

        for (int i = 0; i < 200; i++) {
            float dx2 = x - px, dy2 = y - py, dz2 = z - pz;
            float maxReachSq = (selectedSpecialAbility == 2) ? 10000.0f : 64.0f; // 100 blocks vs 8 blocks
            if (dx2 * dx2 + dy2 * dy2 + dz2 * dz2 > maxReachSq)
                break;

            // Early-exit: if chunk at this position isn't loaded, stop
            int rcx = (int) Math.floor(x / 16.0f);
            int rcy = (int) Math.floor(y / 16.0f);
            int rcz = (int) Math.floor(z / 16.0f);
            if (!worldChunks.containsKey(packChunkKey(rcx, rcy, rcz)))
                break;

            if (isSolidBlock(getBlockGlobal(x, y, z))) {
                hasSelection = true;
                selectedBlockX = x;
                selectedBlockY = y;
                selectedBlockZ = z;
                selectedFaceNormalX = normX;
                selectedFaceNormalY = normY;
                selectedFaceNormalZ = normZ;
                break;
            }

            if (tMaxX < tMaxY) {
                if (tMaxX < tMaxZ) {
                    x += stepX;
                    tMaxX += tDeltaX;
                    normX = -stepX;
                    normY = 0;
                    normZ = 0;
                } else {
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                    normX = 0;
                    normY = 0;
                    normZ = -stepZ;
                }
            } else {
                if (tMaxY < tMaxZ) {
                    y += stepY;
                    tMaxY += tDeltaY;
                    normX = 0;
                    normY = -stepY;
                    normZ = 0;
                } else {
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                    normX = 0;
                    normY = 0;
                    normZ = -stepZ;
                }
            }
        }
    }

    private void drawChunkGrid(float aspect) {
        glUseProgram(0);
        glDisable(GL_TEXTURE_2D);
        glLineWidth(2.0f);

        glMatrixMode(GL_PROJECTION);
        glLoadMatrixf(camera.getProjectionMatrix(aspect).get(new float[16]));
        glMatrixMode(GL_MODELVIEW);
        glLoadMatrixf(camera.getViewMatrix().get(new float[16]));

        int pChunkX = (int) Math.floor(camera.position.x / 16.0f);
        int pChunkZ = (int) Math.floor(camera.position.z / 16.0f);

        glBegin(GL_LINES);
        for (int cx = pChunkX - 2; cx <= pChunkX + 2; cx++) {
            for (int cz = pChunkZ - 2; cz <= pChunkZ + 2; cz++) {
                float minX = cx * 16.0f;
                float maxX = (cx + 1) * 16.0f;
                float minZ = cz * 16.0f;
                float maxZ = (cz + 1) * 16.0f;
                float maxH = 120.0f;

                // Y Axis Lines (Red) going up at corners
                glColor3f(1.0f, 0.0f, 0.0f);
                glVertex3f(minX, 0, minZ);
                glVertex3f(minX, maxH, minZ);

                // Outline outer boundaries so the 5x5 grid isn't open
                if (cx == pChunkX + 2) {
                    glVertex3f(maxX, 0, minZ);
                    glVertex3f(maxX, maxH, minZ);
                    if (cz == pChunkZ + 2) {
                        glVertex3f(maxX, 0, maxZ);
                        glVertex3f(maxX, maxH, maxZ);
                    }
                }
                if (cz == pChunkZ + 2) {
                    glVertex3f(minX, 0, maxZ);
                    glVertex3f(minX, maxH, maxZ);
                }

                for (float y = 0; y <= maxH; y += 16.0f) { // Every 16 blocks vertically
                    glColor3f(0.0f, 1.0f, 0.0f); // X Axis Lines (Green)
                    glVertex3f(minX, y, minZ);
                    glVertex3f(maxX, y, minZ);
                    if (cz == pChunkZ + 2) {
                        glVertex3f(minX, y, maxZ);
                        glVertex3f(maxX, y, maxZ);
                    }
                    glColor3f(0.0f, 0.0f, 1.0f); // Z Axis Lines (Blue)
                    glVertex3f(minX, y, minZ);
                    glVertex3f(minX, y, maxZ);
                    if (cx == pChunkX + 2) {
                        glVertex3f(maxX, y, minZ);
                        glVertex3f(maxX, y, maxZ);
                    }
                }
            }
        }
        glEnd();
    }

    public static void main(String[] args) {
        try {
            new Main().run();
        } catch (Exception e) {
            e.printStackTrace();
            javax.swing.JOptionPane.showMessageDialog(null, "Error: " + e.getMessage() + "\n" + e.toString(),
                    "Engine Crash", javax.swing.JOptionPane.ERROR_MESSAGE);
        }
    }


    // --- Nested from system/Camera.java ---
    
    
    public static class Camera {
        public final org.joml.Vector3f position;
        public float pitch; // up/down
        public float yaw; // left/right
    
        private float fov = (float) Math.toRadians(100.0f);
        private float zNear = 0.1f;
        private float zFar = 8000.f;
    
        public Camera() {
            position = new org.joml.Vector3f(0, 50, 0);
            pitch = 0;
            yaw = 0;
        }
    
        public Matrix4f getViewMatrix() {
            Matrix4f viewMatrix = new Matrix4f();
            viewMatrix.identity();
            viewMatrix.rotate((float) Math.toRadians(pitch), new org.joml.Vector3f(1, 0, 0));
            viewMatrix.rotate((float) Math.toRadians(yaw), new org.joml.Vector3f(0, 1, 0));
            viewMatrix.translate(-position.x, -position.y, -position.z);
            return viewMatrix;
        }
    
        public Matrix4f getProjectionMatrix(float aspect) {
            Matrix4f projectionMatrix = new Matrix4f();
            projectionMatrix.identity();
            // Standard Z
            projectionMatrix.perspective(fov, aspect, zNear, zFar);
            return projectionMatrix;
        }
    
        public void movePosition(float offsetX, float offsetY, float offsetZ) {
            if (offsetZ != 0) {
                position.x += (float) Math.sin(Math.toRadians(yaw)) * -1.0f * offsetZ;
                position.z += (float) Math.cos(Math.toRadians(yaw)) * offsetZ;
            }
            if (offsetX != 0) {
                position.x += (float) Math.sin(Math.toRadians(yaw - 90)) * -1.0f * offsetX;
                position.z += (float) Math.cos(Math.toRadians(yaw - 90)) * offsetX;
            }
            position.y += offsetY;
        }
    
        public void setFov(float fovDegrees) {
            this.fov = (float) Math.toRadians(fovDegrees);
        }
    }
    
    // --- Nested from system/AABB.java ---
    public static class AABB {
        public float minX, minY, minZ;
        public float maxX, maxY, maxZ;
    
        public AABB(float minX, float minY, float minZ, float maxX, float maxY, float maxZ) {
            this.minX = minX;
            this.minY = minY;
            this.minZ = minZ;
            this.maxX = maxX;
            this.maxY = maxY;
            this.maxZ = maxZ;
        }
    
        public boolean intersects(AABB other) {
            return (this.minX < other.maxX && this.maxX > other.minX) &&
                    (this.minY < other.maxY && this.maxY > other.minY) &&
                    (this.minZ < other.maxZ && this.maxZ > other.minZ);
        }
    
        public void move(float dx, float dy, float dz) {
            this.minX += dx;
            this.maxX += dx;
            this.minY += dy;
            this.maxY += dy;
            this.minZ += dz;
            this.maxZ += dz;
        }
    }
    
    // --- Nested from system/Physics.java ---
    
    
    
    public static class Physics {
        public DiscreteDynamicsWorld dynamicsWorld;
        public RigidBody playerBody;
        private final Map<Long, RigidBody> chunkBodies = new HashMap<>();
    
        public static class Debris {
            public RigidBody body;
            public float life;
    
            public Debris(RigidBody b, float l) {
                body = b;
                life = l;
            }
        }
    
        public List<Debris> activeDebris = new ArrayList<>();
    
        public Physics() {
            DefaultCollisionConfiguration collisionConfiguration = new DefaultCollisionConfiguration();
            CollisionDispatcher dispatcher = new CollisionDispatcher(collisionConfiguration);
            DbvtBroadphase broadphase = new DbvtBroadphase();
            SequentialImpulseConstraintSolver solver = new SequentialImpulseConstraintSolver();
    
            dynamicsWorld = new DiscreteDynamicsWorld(dispatcher, broadphase, solver, collisionConfiguration);
            dynamicsWorld.setGravity(new javax.vecmath.Vector3f(0, -Config.GRAVITY, 0));
    
            initPlayer();
        }
    
        private void initPlayer() {
            CapsuleShape playerShape = new CapsuleShape(0.4f, 1.8f);
            Transform startTransform = new Transform();
            startTransform.setIdentity();
            startTransform.origin.set(0, 100, 0); // Temporary high spawn
    
            float mass = 70.0f;
            javax.vecmath.Vector3f localInertia = new javax.vecmath.Vector3f(0, 0, 0);
            playerShape.calculateLocalInertia(mass, localInertia);
    
            DefaultMotionState myMotionState = new DefaultMotionState(startTransform);
            RigidBodyConstructionInfo rbInfo = new RigidBodyConstructionInfo(mass, myMotionState, playerShape,
                    localInertia);
            playerBody = new RigidBody(rbInfo);
    
            // Prevent angular rotation so camera doesn't fall over
            playerBody.setAngularFactor(0.0f);
            // Keep player body from going to sleep
            playerBody.setActivationState(com.bulletphysics.collision.dispatch.CollisionObject.DISABLE_DEACTIVATION);
    
            dynamicsWorld.addRigidBody(playerBody);
        }
    
        public void addChunkMesh(long key, float[] stagedVertices, int[] stagedIndices) {
            if (chunkBodies.containsKey(key)) {
                dynamicsWorld.removeRigidBody(chunkBodies.get(key));
                chunkBodies.remove(key);
            }
    
            if (stagedVertices == null || stagedIndices == null || stagedIndices.length == 0)
                return;
    
            int numTriangles = stagedIndices.length / 3;
            int numVertices = stagedVertices.length / 12;
    
            ByteBuffer verticesBuffer = ByteBuffer.allocateDirect(numVertices * 3 * 4);
            verticesBuffer.order(ByteOrder.nativeOrder());
            for (int i = 0; i < stagedVertices.length; i += 12) {
                verticesBuffer.putFloat(stagedVertices[i]);
                verticesBuffer.putFloat(stagedVertices[i + 1]);
                verticesBuffer.putFloat(stagedVertices[i + 2]);
            }
            verticesBuffer.flip();
    
            ByteBuffer indicesBuffer = ByteBuffer.allocateDirect(stagedIndices.length * 4);
            indicesBuffer.order(ByteOrder.nativeOrder());
            // Since we extracted only the 3 position floats from the 12 staged floats,
            // our new vertex stride is 3, so we must adjust the index values!
            for (int index : stagedIndices) {
                indicesBuffer.putInt(index);
            }
            indicesBuffer.flip();
    
            TriangleIndexVertexArray indexVertexArray = new TriangleIndexVertexArray(
                    numTriangles, indicesBuffer, 3 * 4,
                    numVertices, verticesBuffer, 3 * 4);
    
            BvhTriangleMeshShape shape = new BvhTriangleMeshShape(indexVertexArray, true);
    
            Transform transform = new Transform();
            transform.setIdentity();
    
            RigidBodyConstructionInfo rbInfo = new RigidBodyConstructionInfo(0.0f, new DefaultMotionState(transform), shape,
                    new javax.vecmath.Vector3f(0, 0, 0));
            RigidBody body = new RigidBody(rbInfo);
            body.setFriction(1.0f); // Ground grip
    
            dynamicsWorld.addRigidBody(body);
            chunkBodies.put(key, body);
        }
    
        public void removeChunkMesh(long key) {
            RigidBody body = chunkBodies.remove(key);
            if (body != null) {
                dynamicsWorld.removeRigidBody(body);
            }
        }
    
        public void shootBullet(float x, float y, float z, float dx, float dy, float dz) {
            BoxShape boxShape = new BoxShape(new javax.vecmath.Vector3f(0.08f, 0.08f, 0.08f));
            Transform startTransform = new Transform();
            startTransform.setIdentity();
            // Spawn right at the eye piece and push it forward
            startTransform.origin.set(x + (dx * 0.5f), y + 0.18f + (dy * 0.5f), z + (dz * 0.5f));
    
            float mass = 15.0f; // Heavy bullet
            javax.vecmath.Vector3f localInertia = new javax.vecmath.Vector3f(0, 0, 0);
            boxShape.calculateLocalInertia(mass, localInertia);
    
            DefaultMotionState myMotionState = new DefaultMotionState(startTransform);
            RigidBodyConstructionInfo rbInfo = new RigidBodyConstructionInfo(mass, myMotionState, boxShape, localInertia);
            RigidBody body = new RigidBody(rbInfo);
    
            float speed = 80.0f;
            body.setLinearVelocity(new javax.vecmath.Vector3f(dx * speed, dy * speed, dz * speed));
            body.setRestitution(0.1f); // low bounce
    
            dynamicsWorld.addRigidBody(body);
            activeDebris.add(new Debris(body, 6.0f)); // Give bullets 6 seconds to fly/fall
        }
    
        public void stepSimulation(float dt) {
            dynamicsWorld.stepSimulation(dt, 10);
    
            // Clean up debris
            Iterator<Debris> it = activeDebris.iterator();
            while (it.hasNext()) {
                Debris d = it.next();
                d.life -= dt;
                if (d.life <= 0) {
                    dynamicsWorld.removeRigidBody(d.body);
                    it.remove();
                }
            }
        }
    }
    
    // --- Nested from system/VoxelData.java ---
    public static class VoxelData {
    
            // UV coordinates for the block texture atlas or single textures
            // Right now we just map 0.0 to 1.0 for the whole texture
    
            public static final float[] VERTICES = {
                            // Front face (+Z)
                            0.0f, 0.0f, 1.0f, 0.0f, 0.0f, // 0 Bottom-Left
                            1.0f, 0.0f, 1.0f, 1.0f, 0.0f, // 1 Bottom-Right
                            1.0f, 1.0f, 1.0f, 1.0f, 1.0f, // 2 Top-Right
                            0.0f, 1.0f, 1.0f, 0.0f, 1.0f, // 3 Top-Left
    
                            // Back face (-Z)
                            0.0f, 0.0f, 0.0f, 1.0f, 0.0f, // 4
                            1.0f, 0.0f, 0.0f, 0.0f, 0.0f, // 5
                            1.0f, 1.0f, 0.0f, 0.0f, 1.0f, // 6
                            0.0f, 1.0f, 0.0f, 1.0f, 1.0f, // 7
    
                            // Top face (+Y)
                            0.0f, 1.0f, 0.0f, 0.0f, 1.0f, // 8
                            1.0f, 1.0f, 0.0f, 1.0f, 1.0f, // 9
                            1.0f, 1.0f, 1.0f, 1.0f, 0.0f, // 10
                            0.0f, 1.0f, 1.0f, 0.0f, 0.0f, // 11
    
                            // Bottom face (-Y)
                            0.0f, 0.0f, 0.0f, 0.0f, 0.0f, // 12
                            1.0f, 0.0f, 0.0f, 1.0f, 0.0f, // 13
                            1.0f, 0.0f, 1.0f, 1.0f, 1.0f, // 14
                            0.0f, 0.0f, 1.0f, 0.0f, 1.0f, // 15
    
                            // Right face (+X)
                            1.0f, 0.0f, 1.0f, 0.0f, 0.0f, // 16
                            1.0f, 0.0f, 0.0f, 1.0f, 0.0f, // 17
                            1.0f, 1.0f, 0.0f, 1.0f, 1.0f, // 18
                            1.0f, 1.0f, 1.0f, 0.0f, 1.0f, // 19
    
                            // Left face (-X)
                            0.0f, 0.0f, 0.0f, 0.0f, 0.0f, // 20
                            0.0f, 0.0f, 1.0f, 1.0f, 0.0f, // 21
                            0.0f, 1.0f, 1.0f, 1.0f, 1.0f, // 22
                            0.0f, 1.0f, 0.0f, 0.0f, 1.0f // 23
            };
    
            public static final int[] INDICES = {
                            0, 1, 2, 2, 3, 0, // Front
                            5, 4, 7, 7, 6, 5, // Back
                            8, 9, 10, 10, 11, 8, // Top
                            12, 13, 14, 14, 15, 12, // Bottom
                            16, 17, 18, 18, 19, 16, // Right
                            20, 21, 22, 22, 23, 20 // Left
            };
    
    }
    
}
