package system;

/**
 * Global Configuration Variables for Voxel Engine
 * Nested structurally into distinct domains as requested.
 */
public class Config {
    
    public static class Player {
        public static float BASE_SPEED = 4.8f; 
        public static float SPRINT_SPEED = 6.24f; 
        public static float CROUCH_SPEED_MULT = 0.3f; 
        
        // Water Physics
        public static float SWIM_SPEED = 8.5f; 
        public static float SWIM_RISE_SPEED = 12.0f; 
        public static float SWIM_SINK_SPEED = 0.8f; 
        public static float SWIM_CONTROL = 3.2f; 
        public static float SWIM_SPRINT_MULT = 1.6f; 
        public static float SWIM_SLOW_MULT = 0.45f;
    }

    public static class Physics {
        public static float GRAVITY = 32.0f;
        public static float JUMP_HEIGHT = 1.6f;
        public static float JUMP_VELOCITY = (float) Math.sqrt(2 * GRAVITY * JUMP_HEIGHT);
        
        public static float GROUND_ACCELERATION = 50.0f; 
        public static float GROUND_SLIPPERINESS = 0.6f; 
        public static float AIR_ACCELERATION = 8.5f; 
        public static float AIR_SLIPPERINESS = 0.995f; 
    }

    public static class Render {
        public static float BASE_FOV = 110.0f; 
        public static float SPRINT_FOV = 130.0f; 
        public static float FOV_TRANSITION_SPEED = 10.0f; 
        public static float MOUSE_SENSITIVITY = 0.15f; 
    }

    public static class Terrain {
        public static float PERLIN_AMPLITUDE = 204.0f; // Softened peaks natively for base grass rolling hills
        public static float PERLIN_FREQUENCY = 0.003f;
        public static int   PERLIN_OCTAVES   = 6;
        public static float PERLIN_POWER     = 1.4f; // More graceful fractal perlin topology!
    }

    // Proxy properties used directly by previous codebase instances so nothing breaks natively!
    public static float BASE_SPEED = Player.BASE_SPEED;
    public static float SPRINT_SPEED = Player.SPRINT_SPEED;
    public static float CROUCH_SPEED_MULT = Player.CROUCH_SPEED_MULT;
    public static float SWIM_SPEED = Player.SWIM_SPEED;
    public static float SWIM_RISE_SPEED = Player.SWIM_RISE_SPEED;
    public static float SWIM_SINK_SPEED = Player.SWIM_SINK_SPEED;
    public static float SWIM_CONTROL = Player.SWIM_CONTROL;
    public static float SWIM_SPRINT_MULT = Player.SWIM_SPRINT_MULT;
    public static float SWIM_SLOW_MULT = Player.SWIM_SLOW_MULT;

    public static float GRAVITY = Physics.GRAVITY;
    public static float JUMP_HEIGHT = Physics.JUMP_HEIGHT;
    public static float JUMP_VELOCITY = Physics.JUMP_VELOCITY;
    public static float GROUND_ACCELERATION = Physics.GROUND_ACCELERATION;
    public static float GROUND_SLIPPERINESS = Physics.GROUND_SLIPPERINESS;
    public static float AIR_ACCELERATION = Physics.AIR_ACCELERATION;
    public static float AIR_SLIPPERINESS = Physics.AIR_SLIPPERINESS;

    public static float BASE_FOV = Render.BASE_FOV;
    public static float SPRINT_FOV = Render.SPRINT_FOV;
    public static float FOV_TRANSITION_SPEED = Render.FOV_TRANSITION_SPEED;
    public static float MOUSE_SENSITIVITY = Render.MOUSE_SENSITIVITY;

    public static float PERLIN_AMPLITUDE = Terrain.PERLIN_AMPLITUDE;
    public static float PERLIN_FREQUENCY = Terrain.PERLIN_FREQUENCY;
    public static int   PERLIN_OCTAVES   = Terrain.PERLIN_OCTAVES;
    public static float PERLIN_POWER     = Terrain.PERLIN_POWER;
}
