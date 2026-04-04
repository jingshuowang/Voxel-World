package system;

public class Config {
    // --- MOVEMENT ---
    public static float BASE_SPEED = 4.8f; // Default walking speed
    public static float SPRINT_SPEED = 6.24f; // Speed when sprinting (WASD by default)
    public static float CROUCH_SPEED_MULT = 0.3f; // Multiplier applied to speed when crouching
    public static float SWIM_SPEED = 2.2f; // Horizontal swim speed
    public static float SWIM_RISE_SPEED = 2.4f; // Upward swim speed while holding space
    public static float SWIM_SINK_SPEED = 0.8f; // Constant slow sinking in water
    public static float SWIM_CONTROL = 3.2f; // Water movement response
    public static float SWIM_SPRINT_MULT = 1.6f; // Faster swim while holding ctrl
    public static float SWIM_SLOW_MULT = 0.45f; // Slower swim while holding shift

    // --- PHYSICS ---
    public static float GRAVITY = 32.0f;
    public static float JUMP_HEIGHT = 1.6f; // How high the player jumps in blocks
    public static float JUMP_VELOCITY = (float) Math.sqrt(2 * GRAVITY * JUMP_HEIGHT);

    // --- CAMERA & DISPLAY ---
    public static float BASE_FOV = 110.0f; // FOV when standing still or crouching
    public static float SPRINT_FOV = 130.0f; // FOV when moving at max sprint speed
    public static float FOV_TRANSITION_SPEED = 10.0f; // How fast FOV zooms in and out

    // --- CONTROLS ---
    public static float MOUSE_SENSITIVITY = 0.15f; // How fast the camera rotates

    // --- MOMENTUM ---
    public static float GROUND_ACCELERATION = 50.0f; // Quick responsive acceleration (higher = snappier)
    public static float GROUND_SLIPPERINESS = 0.6f; // Slipperiness (lower = faster stop)
    public static float AIR_ACCELERATION = 1f; // Sluggish air control
    public static float AIR_SLIPPERINESS = 0.9f; // Less momentum retained in the air (0.75)

    // --- TERRAIN GENERATION ---
    public static float PERLIN_AMPLITUDE = 96.0f; // Multiplier for terrain height
    public static float PERLIN_FREQUENCY = 0.03f; // Zoom level / frequency of features
    public static float PERLIN_POWER = 2.0f; // Exponent for valleys and peaks
}
