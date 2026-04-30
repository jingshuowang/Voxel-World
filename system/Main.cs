using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using System;
using System.Numerics;
using System.Diagnostics;
using System.Threading;
using Voxel.Rendering;
using Voxel.System;
using Voxel.Experimental;
using Silk.NET.OpenGL.Extensions.ImGui;
using ImGuiNET;

namespace Voxel {
    public class Program {
        static IWindow? w; static GL? gl;
        static Render? render;
        static World? world;
        static Player? player;
        static IKeyboard? kb; static IMouse? mouse;
        static float currentVelocity = 0;
        static Stopwatch sw = new();
        static Stopwatch cpuSw = new();
        static bool showStats = false;
        static Vector3 lastIntPos = new(-999, -999, -999);
        static float[] entityData = new float[1024 * 16];
        static int entityCount = 0;
        static ImGuiController? imGuiController;

        static global::System.Collections.Generic.Dictionary<string, double> avgTimers = new();
        static long lastTimer;
        public static void RecordTimer(string label) { 
            double ms = (Stopwatch.GetTimestamp() - lastTimer) * 1000.0 / Stopwatch.Frequency; 
            if (!avgTimers.ContainsKey(label)) avgTimers[label] = 0;
            avgTimers[label] = avgTimers[label] * 0.9 + ms * 0.1;
            lastTimer = Stopwatch.GetTimestamp();
        }

        public static void Main() {
            Console.Clear();
            Console.WriteLine("======================================");
            Console.WriteLine("    WELCOME TO BITZEL ENGINE v1.0     ");
            Console.WriteLine("======================================");
            Console.WriteLine("Select Mode:");
            Console.WriteLine("1. Bitzel (Voxel Game)");
            Console.WriteLine("2. Physics Experiment (Sphere Sandbox)");
            Console.Write("\nChoice [1-2]: ");
            
            string? choice = Console.ReadLine();
            if (choice == "2") {
                Experiment.ExperimentMain();
            } else {
                GameMain();
            }
        }

        static void GameMain() {
            Console.WriteLine("Starting Bitzel...");
            Console.Write("Enter World Seed (or leave empty for default 1): ");
            string? input = Console.ReadLine();
            if (uint.TryParse(input, out uint parsedSeed)) {
                World.Seed = parsedSeed;
            } else {
                World.Seed = 1;
            }
            Console.WriteLine($"Using Seed: {World.Seed}");
            Console.WriteLine("Initializing Game Components...");

            var o = WindowOptions.Default;
            o.Size = new(1920, 1080);
            o.Title = "Bitzel";
            o.WindowState = WindowState.Fullscreen;
            o.VSync = true;
            o.FramesPerSecond = 0;
            o.UpdatesPerSecond = 0;
            o.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(4, 6));
            w = Window.Create(o);
            w.Load += OnLoad; w.Update += OnUpdate; w.Render += OnRender;
            w.Run();
        }


        static void OnLoad() {
            gl = w!.CreateOpenGL();
            if (w != null) w.VSync = true; 
            var inp = w!.CreateInput();
            kb = inp.Keyboards[0]; mouse = inp.Mice[0];
            mouse.Cursor.CursorMode = CursorMode.Raw;

            imGuiController = new ImGuiController(gl, w, inp);
            ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

            kb.KeyDown += (_, k, _) => {
                if (k == Key.F11) w!.WindowState = (w.WindowState == WindowState.Fullscreen) ? WindowState.Normal : WindowState.Fullscreen;
                if (k == Key.F3) {
                    showStats = !showStats;
                    mouse.Cursor.CursorMode = showStats ? CursorMode.Normal : CursorMode.Raw;
                }
                if (k == Key.F5) {
                    if (render != null) render.RenderMode = (render.RenderMode + 1) % 4;
                }
                if (k == Key.Escape) {
                    mouse.Cursor.CursorMode = (mouse.Cursor.CursorMode == CursorMode.Normal) ? CursorMode.Raw : CursorMode.Normal;
                }
            };

            world = new World();
            player = new Player();
            render = new Render(gl!, w!.Size.X, w!.Size.Y);
            
            // Initial Player
            entityCount = 1;
            entityData[0] = player.Position.X; entityData[1] = player.Position.Y; entityData[2] = player.Position.Z; entityData[3] = 1.0f;
            entityData[4] = 0; entityData[5] = 0; entityData[6] = 0; entityData[7] = 0;
            entityData[8] = 0; // Type: 0=Player
            entityData[9] = 1.8f; // Size
            entityData[10] = 70.0f; // Mass
            entityData[11] = 0.0f; // Restitution (0 = no bounce)
            entityData[12] = 0.5f; // Friction
            
            // No initial rain
            render.WriteEntities(entityData, entityCount);
            
            sw.Start();
        }

        static Vector3 tickMove = Vector3.Zero;
        static bool tickJump = false;
        static bool tickSprint = false;
        static bool tickCrouch = false;
        static bool lastQ = false;
        static Vector3 prevPlayerPos = Vector3.Zero;

        static double cpuTimeAvg = 0;
        static double gpuTimeAvg = 0;

        static double accumulator = 0;
        static void OnUpdate(double dt) {
            imGuiController!.Update((float)dt);

            // Clicking re-enters the game from Menu
            if (mouse!.IsButtonPressed(MouseButton.Left) && mouse.Cursor.CursorMode == CursorMode.Normal) {
                mouse.Cursor.CursorMode = CursorMode.Raw;
            }

            // Input is high-frequency (every frame)
            var (move, jump, sprint, crouch) = render!.GetInputState(kb!, mouse!);
            tickMove = move;
            tickJump = jump;
            tickSprint = sprint;
            tickCrouch = crouch;

            accumulator += dt;
            float tickRate = 1.0f / Config.TickSpeed;

            while (accumulator >= tickRate) {
                cpuSw.Restart();
                lastTimer = Stopwatch.GetTimestamp();
                
                prevPlayerPos = player!.Position;
                // Dispatch GPU Physics
                // Spectator Noclip Movement for Player
                float speed = player!.Speed * 2.0f; // Spectator is usually faster
                if (tickSprint) speed *= 2.0f;
                
                Vector3 moveDir = Vector3.Zero;
                if (kb!.IsKeyPressed(Key.W)) moveDir += render!.Forward;
                if (kb.IsKeyPressed(Key.S)) moveDir -= render!.Forward;
                
                Vector3 right = Vector3.Normalize(Vector3.Cross(render!.Forward, Vector3.UnitY));
                if (kb.IsKeyPressed(Key.D)) moveDir += right;
                if (kb.IsKeyPressed(Key.A)) moveDir -= right;
                
                if (kb.IsKeyPressed(Key.Space)) moveDir += Vector3.UnitY;
                if (kb.IsKeyPressed(Key.ShiftLeft)) moveDir -= Vector3.UnitY; // Crouch goes down in spectator
                
                if (moveDir.LengthSquared() > 0) moveDir = Vector3.Normalize(moveDir);
                
                player.Position += moveDir * speed * tickRate;
                
                // Keep the GPU entity updated for other entities to collide with
                render.UpdatePlayerEntity(player.Position);

                Vector3 playerParams = new Vector3(player.JumpForce, player.Friction, player.AirResistance);
                RecordTimer("L141: Pre-Physics Setup");

                render!.DispatchPhysics(tickRate, World.Seed, Vector3.Zero, playerParams, entityCount);
                RecordTimer("L144: DispatchPhysics");
                
                // Read back state (for other entities)
                render!.ReadEntities(entityData, entityCount);
                RecordTimer("L148: ReadEntities (GPU sync)");
                RecordTimer("L153: Update Player State");

                // Throw Entity (Q)
                bool currentQ = kb!.IsKeyPressed(Key.Q);
                if (currentQ && !lastQ && entityCount < 1024) {
                    Random rnd = new Random();
                    int idx = entityCount * 16;
                    entityData[idx] = player.Position.X + render.Forward.X * 2.0f;
                    entityData[idx+1] = player.Position.Y + render.Forward.Y * 2.0f;
                    entityData[idx+2] = player.Position.Z + render.Forward.Z * 2.0f;
                    entityData[idx+3] = 0.0f; // Grounded
                    
                    entityData[idx+4] = render.Forward.X * 30.0f;
                    entityData[idx+5] = render.Forward.Y * 30.0f;
                    entityData[idx+6] = render.Forward.Z * 30.0f;
                    
                    entityData[idx+8] = rnd.Next(1, 3); // Type
                    entityData[idx+9] = 1.0f + (float)rnd.NextDouble(); // Size
                    entityData[idx+10] = 5.0f; // Mass
                    entityData[idx+11] = 0.5f; // Restitution
                    entityData[idx+12] = 0.5f; // Friction
                    
                    // Rotation (Euler angles in data2.yzw)
                    entityData[idx+13] = (float)(rnd.NextDouble() * Math.PI * 2); // Pitch
                    entityData[idx+14] = (float)(rnd.NextDouble() * Math.PI * 2); // Yaw
                    entityData[idx+15] = (float)(rnd.NextDouble() * Math.PI * 2); // Roll
                    
                    entityCount++;
                    render.WriteEntities(entityData, entityCount);
                }
                lastQ = currentQ;
                RecordTimer("L183: Throw Entity");

                Vector3 currentIntPos = new(MathF.Floor(player!.Position.X / 16.0f), MathF.Floor(player.Position.Y / 16.0f), MathF.Floor(player.Position.Z / 16.0f));
                if (currentIntPos != lastIntPos) {
                    render!.PrepareSlidingWindow(world!, player.Position);
                    lastIntPos = currentIntPos;
                }
                RecordTimer("L190: PrepareSlidingWindow");
                
                currentVelocity = player!.Velocity.Length();
                double frameCpuTime = cpuSw.Elapsed.TotalMilliseconds;
                cpuTimeAvg = cpuTimeAvg * 0.9 + frameCpuTime * 0.1;
                RecordTimer("L195: Tick Cleanup");
                
                accumulator -= tickRate;
            }
        }

        static void OnRender(double dt) {
            Stopwatch renderSw = Stopwatch.StartNew();
            lastTimer = Stopwatch.GetTimestamp();

            render!.UploadSlidingWindow(); // Thread-safe texture upload
            RecordTimer("L202: UploadSlidingWindow");

            float alpha = (float)(accumulator / (1.0 / Config.TickSpeed));
            Vector3 lerpPos = Vector3.Lerp(prevPlayerPos, player!.Position, alpha);
            render!.Draw(lerpPos, player.Size.Y, currentVelocity, (float)sw.Elapsed.TotalSeconds, entityCount);
            RecordTimer("L205: Render Draw Call");

            renderSw.Stop();

            double frameGpuTime = renderSw.Elapsed.TotalMilliseconds;
            gpuTimeAvg = gpuTimeAvg * 0.95 + frameGpuTime * 0.05;

            if (showStats) {
                ImGui.SetNextWindowBgAlpha(0.7f);
                if (ImGui.Begin("F3 Profiler / Stats", ImGuiWindowFlags.AlwaysAutoResize)) {
                    ImGui.Text($"FPS: {1.0/dt:F0}");
                    ImGui.Text($"Entities: {entityCount}");
                    ImGui.Separator();
                    ImGui.Text($"Position: {player!.Position.X:F1}, {player.Position.Y:F1}, {player.Position.Z:F1}");
                    ImGui.Text($"Velocity: {player.Velocity.X:F1}, {player.Velocity.Y:F1}, {player.Velocity.Z:F1}");
                    ImGui.Text($"Accel(XZ): {tickMove.X*player.Speed:F1}, {tickMove.Z*player.Speed:F1}");
                    ImGui.Separator();
                    ImGui.Text("Player Physics");
                    ImGui.SliderFloat("Acceleration", ref player.Speed, 1.0f, 200.0f);
                    ImGui.SliderFloat("Jump Force", ref player.JumpForce, 1.0f, 30.0f);
                    ImGui.SliderFloat("Ground Friction", ref player.Friction, 0.0f, 1.0f);
                    ImGui.SliderFloat("Air Resistance", ref player.AirResistance, 0.0f, 1.0f);
                    ImGui.Separator();
                    ImGui.Text("Timers (CPU ms)");
                    ImGui.Text($"Calculate: {cpuTimeAvg:F3} ms");
                    ImGui.Text($"Render Dispatch:    {gpuTimeAvg:F3} ms");
                    ImGui.Separator();
                    ImGui.Text("Detailed Line Timers (Avg ms)");
                    foreach(var kv in avgTimers) {
                        ImGui.Text($"{kv.Key}: {kv.Value:F3} ms");
                    }
                    ImGui.End();
                }
            }
            
            imGuiController!.Render();
            w!.Title = "Voxel World";
        }
    }
}
