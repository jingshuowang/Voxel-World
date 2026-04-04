package system;

import org.lwjgl.*;
import org.lwjgl.glfw.*;
import org.lwjgl.opengl.*;
import org.lwjgl.system.*;
import org.joml.Matrix4f;
import org.joml.Vector3f;

import rendering.Shader;
import rendering.AudioPlayer;

import java.nio.*;
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

import static org.lwjgl.glfw.Callbacks.*;
import static org.lwjgl.glfw.GLFW.*;
import static org.lwjgl.opengl.GL11.*;
import static org.lwjgl.opengl.GL15.*;
import static org.lwjgl.opengl.GL20.*;
import static org.lwjgl.opengl.GL30.*;
import static org.lwjgl.system.MemoryStack.*;
import static org.lwjgl.system.MemoryUtil.*;

public class Main {

    private long window;
    private Shader shader;
    private Shader skyShader;
    private Shader depthShader;
    private Camera camera;

    private int whiteTextureId; // Procedural 1x1 white pixel (no image files needed)
    private Map<Long, Chunk> worldChunks;
    private static final int LOAD_RADIUS_XZ = 10;
    private static final int LOAD_RADIUS_Y = 16; // 16 chunks vertical (256 blocks up and down)
    private static final int UNLOAD_RADIUS_XZ = 14; // XZ unload distance
    private static final int UNLOAD_RADIUS_Y = 20; // Y unload distance
    private static final int WORLD_HEIGHT_BLOCKS = 16384;
    private static final int WORLD_HEIGHT_CHUNKS = WORLD_HEIGHT_BLOCKS / Chunk.CHUNK_SIZE;
    private static final int XZ_BLOCK_BITS = 24;
    private static final int XZ_CHUNK_BITS = XZ_BLOCK_BITS - 4;
    private static final int Y_CHUNK_BITS = 10;
    private static final long XZ_CHUNK_MASK = (1L << XZ_CHUNK_BITS) - 1L;
    private static final long Y_CHUNK_MASK = (1L << Y_CHUNK_BITS) - 1L;

    // Background chunk generation thread
    private final ConcurrentLinkedQueue<Chunk> chunksToUpload = new ConcurrentLinkedQueue<>();
    private final Set<Long> chunksLoading = java.util.Collections.synchronizedSet(new HashSet<>());
    private final java.util.ArrayDeque<Long> chunksNeedingRebuild = new java.util.ArrayDeque<>();
    private volatile boolean chunkThreadRunning = true;
    private Thread chunkGenThread;

    public static Physics physics;
    private boolean[] keys = new boolean[GLFW_KEY_LAST];
    private double lastMouseX = -1;
    private double lastMouseY = -1;

    private float velocityX = 0.0f;
    private float velocityY = 0.0f;
    private float velocityZ = 0.0f;
    private boolean isGrounded = false;
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
    private byte selectedBlockType = Chunk.STONE;
    private int renderMode = 0; // 0=normal, 1=normals, 2=depth

    // Mouse Auto-Fire Timers
    private boolean isLeftMouseDown = false;
    private boolean isRightMouseDown = false;
    private float leftMouseTimer = 0.0f;
    private float rightMouseTimer = 0.0f;
    private static final float MOUSE_HOLD_DELAY = 0.25f; // 5 game ticks at 20 ticks/sec

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

    // Shaders have been completely extracted into Asset/shader/*.glsl equivalents

    public void run() {
        System.out.println("Hello LWJGL " + Version.getVersion() + "!");
        init();
        loop();

        if (shader != null)
            shader.cleanup();
        chunkThreadRunning = false;
        if (chunkGenThread != null) {
            try {
                chunkGenThread.join(1000);
            } catch (InterruptedException e) {
            }
        }
        if (whiteTextureId != 0)
            glDeleteTextures(whiteTextureId);
        if (worldChunks != null) {
            for (Chunk c : worldChunks.values()) {
                c.cleanup();
            }
        }

        glfwFreeCallbacks(window);
        glfwDestroyWindow(window);
        glfwTerminate();
        glfwSetErrorCallback(null).free();
    }

    private void init() {
        GLFWErrorCallback.createPrint(System.err).set();
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
            }
            // Hotbar: 1=Water, 2=Dirt, 3=Stone, 4=Snow, 5=Projectile tool
            if (key == GLFW_KEY_1 && action == GLFW_PRESS)
                selectedBlockType = Chunk.WATER;
            if (key == GLFW_KEY_2 && action == GLFW_PRESS)
                selectedBlockType = Chunk.DIRT;
            if (key == GLFW_KEY_3 && action == GLFW_PRESS)
                selectedBlockType = Chunk.STONE;
            if (key == GLFW_KEY_4 && action == GLFW_PRESS)
                selectedBlockType = Chunk.SNOW;
            if (key == GLFW_KEY_5 && action == GLFW_PRESS)
                selectedBlockType = TOOL_PROJECTILE;
            if (key == GLFW_KEY_F5 && action == GLFW_PRESS)
                renderMode = (renderMode + 1) % 3; // Cycle: normal -> normals -> depth
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
                    leftMouseTimer = MOUSE_HOLD_DELAY; // Wait full delay before NEXT auto-fire
                    breakBlock(); // Fire exactly on click
                } else if (button == GLFW_MOUSE_BUTTON_RIGHT) {
                    isRightMouseDown = true;
                    rightMouseTimer = MOUSE_HOLD_DELAY; // Wait full delay before NEXT auto-fire
                    placeBlock(); // Fire exactly on click
                }
            } else if (action == GLFW_RELEASE) {
                if (button == GLFW_MOUSE_BUTTON_LEFT) {
                    isLeftMouseDown = false;
                } else if (button == GLFW_MOUSE_BUTTON_RIGHT) {
                    isRightMouseDown = false;
                }
            }
        });

        // Lock mouse cursor to window
        glfwSetInputMode(window, GLFW_CURSOR, GLFW_CURSOR_DISABLED);

        glfwMakeContextCurrent(window);
        glfwSwapInterval(0); // Enable v-sync
        glfwShowWindow(window);

        // physics = new Physics();
    }

    public Chunk setBlockGlobal(int x, int y, int z, byte type) {
        int chunkX = (int) Math.floor(x / 16.0f);
        int chunkY = (int) Math.floor(y / 16.0f);
        int chunkZ = (int) Math.floor(z / 16.0f);
        int localX = x - (chunkX * 16);
        int localY = y - (chunkY * 16);
        int localZ = z - (chunkZ * 16);
        Chunk c = worldChunks.get(packChunkKey(chunkX, chunkY, chunkZ));
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
        Chunk c = worldChunks.get(packChunkKey(chunkX, chunkY, chunkZ));
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
        while (uploaded < 96) {
            Chunk chunk = chunksToUpload.poll();
            if (chunk == null)
                break;
            chunk.uploadMesh();
            long key = packChunkKey(chunk.chunkX, chunk.chunkY, chunk.chunkZ);
            worldChunks.put(key, chunk);
            // if (chunk.stagedVertices != null && chunk.stagedIndices != null) {
            // physics.addChunkMesh(key, chunk.stagedVertices, chunk.stagedIndices);
            // }
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
            Chunk nb = worldChunks.get(nk);
            if (nb != null) {
                nb.buildMeshData(this::getBlockGlobal);
                nb.uploadMesh();
                // if (nb.stagedVertices != null && nb.stagedIndices != null) {
                // physics.addChunkMesh(nk, nb.stagedVertices, nb.stagedIndices);
                // }
                rebuilt++;
            }
        }

        // Unload far chunks (cylindrical distance)
        Iterator<Map.Entry<Long, Chunk>> it = worldChunks.entrySet().iterator();
        while (it.hasNext()) {
            Map.Entry<Long, Chunk> entry = it.next();
            Chunk c = entry.getValue();
            int dx = c.chunkX - playerChunkX;
            int dz = c.chunkZ - playerChunkZ;
            if (dx * dx + dz * dz > UNLOAD_RADIUS_XZ * UNLOAD_RADIUS_XZ || c.chunkY < 0
                    || c.chunkY >= WORLD_HEIGHT_CHUNKS) {
                c.cleanup();
                // physics.removeChunkMesh(entry.getKey());
                it.remove();
            }
        }
    }

    private void loop() {
        GL.createCapabilities();
        glClearColor(0.5f, 0.8f, 1.0f, 0.0f); // Sky blue
        glEnable(GL_DEPTH_TEST);
        glDepthFunc(GL_GEQUAL); // Reversed-Z: greater = closer
        glClearDepth(0.0); // Clear to 0 (farthest in reversed-Z)
        glEnable(GL_CULL_FACE); // Cull back faces for performance
        glEnable(GL_BLEND);
        glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

        try {
            shader = new Shader();
            shader.createVertexShaderFromFile("Asset/shader/chunk.vs");
            shader.createFragmentShaderFromFile("Asset/shader/chunk.fs");
            shader.link();

            skyShader = new Shader();
            skyShader.createVertexShaderFromFile("Asset/shader/sky.vs");
            skyShader.createFragmentShaderFromFile("Asset/shader/sky.fs");
            skyShader.link();

            // Create a 1x1 white pixel texture (no image file needed!)
            int whiteTexId = glGenTextures();
            glBindTexture(GL_TEXTURE_2D, whiteTexId);
            ByteBuffer pixel = ByteBuffer.allocateDirect(4);
            pixel.put((byte) 0xFF).put((byte) 0xFF).put((byte) 0xFF).put((byte) 0xFF).flip();
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, 1, 1, 0, GL_RGBA, GL_UNSIGNED_BYTE, pixel);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            whiteTextureId = whiteTexId;
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
        System.out.println("Pre-generating spawn chunks...");
        int spawnRadius = LOAD_RADIUS_XZ; // Full view distance at spawn
        for (int dx = -spawnRadius; dx <= spawnRadius; dx++) {
            for (int dz = -spawnRadius; dz <= spawnRadius; dz++) {
                if (dx * dx + dz * dz > spawnRadius * spawnRadius)
                    continue;
                for (int cy = 0; cy <= LOAD_RADIUS_Y; cy++) {
                    long key = packChunkKey(dx, cy, dz);
                    if (!worldChunks.containsKey(key)) {
                        Chunk chunk = new Chunk(dx, cy, dz);
                        chunk.generateTerrain();
                        chunk.buildMeshData(this::getBlockGlobal);
                        chunk.uploadMesh(); // Direct GPU upload on main thread
                        // if (chunk.stagedVertices != null && chunk.stagedIndices != null) {
                        // physics.addChunkMesh(key, chunk.stagedVertices, chunk.stagedIndices);
                        // }
                        worldChunks.put(key, chunk);
                    }
                }
            }
        }
        System.out.println("Spawn chunks ready! (" + worldChunks.size() + " chunks)");

        // Set spawn to actual terrain height at (0,0) + player eye height
        int spawnTerrainY = Chunk.getTerrainHeight(0, 0);
        camera.position.set(0, spawnTerrainY + 2.5f, 0);
        System.out.println("Dynamic chunk loading enabled (radius: " + LOAD_RADIUS_XZ + " chunks)");

        // Start background chunk generation thread
        chunkGenThread = new Thread(() -> {
            System.out.println("[ChunkGen] Background thread started!");
            while (chunkThreadRunning) {
                int pcx = (int) Math.floor(camera.position.x / 16.0f);
                int pcz = (int) Math.floor(camera.position.z / 16.0f);

                int generated = 0;
                // Spiral outward from player for nearest-first loading (XZ only)
                for (int r = 0; r <= LOAD_RADIUS_XZ && generated < 96; r++) {
                    for (int dx = -r; dx <= r && generated < 96; dx++) {
                        for (int dz = -r; dz <= r && generated < 96; dz++) {
                            if (Math.abs(dx) != r && Math.abs(dz) != r)
                                continue;
                            if (dx * dx + dz * dz > LOAD_RADIUS_XZ * LOAD_RADIUS_XZ)
                                continue;
                            // Generate full column of Y chunks
                            for (int cy = 0; cy <= LOAD_RADIUS_Y; cy++) {
                                int wx = pcx + dx;
                                int wz = pcz + dz;
                                long key = packChunkKey(wx, cy, wz);
                                if (!worldChunks.containsKey(key) && chunksLoading.add(key)) {
                                    Chunk chunk = new Chunk(wx, cy, wz);
                                    chunk.generateTerrain();
                                    chunk.buildMeshData(this::getBlockGlobal);
                                    chunksToUpload.add(chunk);
                                    generated++;
                                }
                            }
                        }
                    }
                }

                if (generated == 0) {
                    try {
                        Thread.sleep(50);
                    } catch (InterruptedException e) {
                        break;
                    }
                }
            }
            System.out.println("[ChunkGen] Background thread stopped.");
        }, "ChunkGen");
        chunkGenThread.setDaemon(true);
        chunkGenThread.start();

        System.out.println("Preloading Audio to RAM...");
        AudioPlayer.preloadSound("Asset/sound/block1.wav");
        AudioPlayer.preloadSound("Asset/sound/block2.wav");
        AudioPlayer.preloadSound("Asset/sound/block3.wav");
        AudioPlayer.preloadSound("Asset/sound/block4.wav");

        float lastTime = (float) glfwGetTime();
        int frameCount = 0;
        float fpsTimer = 0.0f;
        int fps = 0;

        long globalTicks = 0;
        float tickAccumulator = 0.0f;
        final float TICK_RATE = 60.0f;
        final float TIME_PER_TICK = 1.0f / TICK_RATE;
        int ticksThisSecond = 0;
        float tpsTimer = 0.0f;
        int tps = 0;

        while (!glfwWindowShouldClose(window) && !shouldExit) {
            float currentTime = (float) glfwGetTime();
            float rawDt = currentTime - lastTime;
            lastTime = currentTime;

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
                    // physics.stepSimulation(TIME_PER_TICK);
                }
            }

            // Chunk management once per render frame
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
            float skyR = 0.02f + 0.48f * dayFactor;
            float skyG = 0.02f + 0.78f * dayFactor;
            float skyB = 0.08f + 0.92f * dayFactor;

            // 2. Render
            int[] width = new int[1], height = new int[1];
            glfwGetFramebufferSize(window, width, height);
            glViewport(0, 0, width[0], height[0]);

            byte camBlock = getBlockGlobal((int) Math.floor(camera.position.x), (int) Math.floor(camera.position.y),
                    (int) Math.floor(camera.position.z));
            boolean blind = (camBlock != 0 && camBlock != Chunk.WATER);

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

                // --- Draw Chunks (flat color, no lighting) ---
                shader.bind();
                shader.setUniform("projectionMatrix", camera.getProjectionMatrix(aspect));
                shader.setUniform("viewMatrix", camera.getViewMatrix());
                shader.setUniform("modelMatrix", new org.joml.Matrix4f().identity());
                shader.setUniform("renderMode", renderMode);

                org.joml.Matrix4f viewProj = new org.joml.Matrix4f(camera.getProjectionMatrix(aspect))
                        .mul(camera.getViewMatrix());
                org.joml.FrustumIntersection frustum = new org.joml.FrustumIntersection(viewProj);

                for (Chunk chunk : worldChunks.values()) {
                    chunk.render();
                }

                shader.unbind();

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
                /*
                 * for (Physics.Debris d : physics.activeDebris) {
                 * com.bulletphysics.linearmath.Transform trans = new
                 * com.bulletphysics.linearmath.Transform();
                 * d.body.getMotionState().getWorldTransform(trans);
                 * 
                 * glPushMatrix();
                 * glTranslatef(trans.origin.x, trans.origin.y, trans.origin.z);
                 * glColor3f(0.2f, 0.2f, 0.2f); // Dark grey object
                 * 
                 * float s = 0.12f; // Bullet / Debris radius
                 * glBegin(GL_QUADS);
                 * // Front Face
                 * glVertex3f(-s, -s, s);
                 * glVertex3f(s, -s, s);
                 * glVertex3f(s, s, s);
                 * glVertex3f(-s, s, s);
                 * // Back Face
                 * glVertex3f(-s, -s, -s);
                 * glVertex3f(-s, s, -s);
                 * glVertex3f(s, s, -s);
                 * glVertex3f(s, -s, -s);
                 * // Top Face
                 * glVertex3f(-s, s, -s);
                 * glVertex3f(-s, s, s);
                 * glVertex3f(s, s, s);
                 * glVertex3f(s, s, -s);
                 * // Bottom Face
                 * glVertex3f(-s, -s, -s);
                 * glVertex3f(s, -s, -s);
                 * glVertex3f(s, -s, s);
                 * glVertex3f(-s, -s, s);
                 * // Right Face
                 * glVertex3f(s, -s, -s);
                 * glVertex3f(s, s, -s);
                 * glVertex3f(s, s, s);
                 * glVertex3f(s, -s, s);
                 * // Left Face
                 * glVertex3f(-s, -s, -s);
                 * glVertex3f(-s, -s, s);
                 * glVertex3f(-s, s, s);
                 * glVertex3f(-s, s, -s);
                 * glEnd();
                 * glPopMatrix();
                 * }
                 */

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
                    glDisable(GL_BLEND);
                }

            } // end if (!blind)

            drawChunkGrid(aspect);

            // Draw Crosshair using simple Ortho compatibility pass
            int[] wWidth = new int[1], wHeight = new int[1];
            glfwGetFramebufferSize(window, wWidth, wHeight);

            glDisable(GL_DEPTH_TEST);
            glMatrixMode(GL_PROJECTION);
            glPushMatrix();
            glLoadIdentity();
            glOrtho(0, wWidth[0], wHeight[0], 0, -1, 1);
            glMatrixMode(GL_MODELVIEW);
            glPushMatrix();
            glLoadIdentity();

            glColor3f(1.0f, 1.0f, 1.0f); // White crosshair
            float cx = wWidth[0] / 2.0f;
            float cy = wHeight[0] / 2.0f;
            glLineWidth(2.0f);
            glBegin(GL_LINES);
            glVertex2f(cx - 10, cy);
            glVertex2f(cx + 10, cy);
            glVertex2f(cx, cy - 10);
            glVertex2f(cx, cy + 10);
            glEnd();

            glPopMatrix();
            glMatrixMode(GL_PROJECTION);
            glPopMatrix();
            glMatrixMode(GL_MODELVIEW);
            glEnable(GL_DEPTH_TEST);

            // Update Window Title with Velocity and Pos
            String title = String.format(
                    "Java 3D Voxel Engine | FPS: %d | TPS: %d | Vel: %.2f, %.2f, %.2f | Pos: X:%.1f Y:%.1f Z:%.1f",
                    fps, tps, velocityX, velocityY, velocityZ, camera.position.x, camera.position.y, camera.position.z);
            glfwSetWindowTitle(window, title);

            glfwSwapBuffers(window);
            glfwPollEvents();
        }
    }

    private void rebuildChunkAndNeighbors(Chunk c) {
        c.buildMesh(this::getBlockGlobal);
        int[][] dirs = { { 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 } };
        for (int[] d : dirs) {
            Chunk nb = worldChunks.get(packChunkKey(c.chunkX + d[0], c.chunkY + d[1], c.chunkZ + d[2]));
            if (nb != null)
                nb.buildMesh(this::getBlockGlobal);
        }
    }

    public void breakBlock() {
        if (!hasSelection)
            return;
        Chunk c = setBlockGlobal(selectedBlockX, selectedBlockY, selectedBlockZ, (byte) 0);
        if (c != null) {
            AudioPlayer.playSound("Asset/sound/block" + (int) (Math.random() * 4 + 1) + ".wav", 1.0f, 1.0f);
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
            AudioPlayer.playSound("Asset/sound/block" + (int) (Math.random() * 4 + 1) + ".wav", 1.0f, 1.0f);
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
            Chunk c = setBlockGlobal(placeX, placeY, placeZ, selectedBlockType);
            if (c != null) {
                AudioPlayer.playSound("Asset/sound/block" + (int) (Math.random() * 4 + 1) + ".wav", 1.0f, 1.0f);
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
        // Break every tick when held - no cooldown!
        if (isLeftMouseDown) {
            breakBlock();
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
            if (isJumpPressed) {
                rawSwimY += 1.0f;
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

        if (!isInWater) {
            // Gravity
            velocityY -= Config.GRAVITY * dt;
            if (isJumpPressed && isGrounded) {
                velocityY = Config.JUMP_VELOCITY;
                isGrounded = false;
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
                collisionX = true;
                dx = 0; // Prevent pushing mathematically
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
        return blockType != Chunk.AIR && blockType != Chunk.WATER;
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
                    if (getBlockGlobal(x, y, z) == Chunk.WATER) {
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

        for (int i = 0; i < 5000; i++) { // Max reach basically infinite
            float dist = (float) Math.sqrt(Math.pow(x - px, 2) + Math.pow(y - py, 2) + Math.pow(z - pz, 2));
            if (dist > 1000.0f)
                break; // Maximum reach

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

    // 1D Perlin-style noise for organic sun XZ drift
    private float perlinNoise1D(float x) {
        int xi = (int) Math.floor(x);
        float xf = x - xi;
        // Smooth interpolation curve
        float u = xf * xf * (3.0f - 2.0f * xf);
        // Hash-based pseudo-random gradients
        float g0 = perlinGrad(xi);
        float g1 = perlinGrad(xi + 1);
        return g0 + u * (g1 - g0);
    }

    private float perlinGrad(int hash) {
        // Simple hash to produce a repeatable float in [-1, 1]
        hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
        hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
        hash = (hash >> 16) ^ hash;
        return (hash & 0xFFFF) / 32768.0f - 1.0f;
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
}
