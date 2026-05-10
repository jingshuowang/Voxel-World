using System;
using Voxel.World;
using Voxel.Client;

namespace Voxel {
    public class Program {
        public static void Main(string[] args) {
            Console.Clear();
            Console.WriteLine("=== Voxel World ===");
            Console.WriteLine();
            Console.WriteLine("  [h] Host");
            Console.WriteLine("  [j] Join");
            Console.WriteLine("  [s] Solo");
            Console.WriteLine("  [e] Experimental");
            Console.WriteLine();
            Console.Write("> ");
            string choice = Console.ReadLine()?.Trim().ToLower() ?? "";

            switch (choice) {
                case "h": case "host":
                    Console.Write("Port [25565]: ");
                    string portStr = Console.ReadLine()?.Trim() ?? "25565";
                    int port = int.TryParse(portStr, out int p) ? p : 25565;
                    Console.WriteLine($"Host on port {port} -- not yet implemented.");
                    break;

                case "j": case "join":
                    Console.Write("Host IP: ");
                    string ip = Console.ReadLine()?.Trim() ?? "localhost";
                    Console.Write("Seed: ");
                    if (int.TryParse(Console.ReadLine()?.Trim(), out int joinSeed))
                        WConfig.Seed = joinSeed;
                    Console.WriteLine($"Joining {ip} -- not yet implemented.");
                    break;

                case "s": case "solo":
                    Console.Write("Seed (0 = flat): ");
                    if (int.TryParse(Console.ReadLine()?.Trim(), out int seed))
                        WConfig.Seed = seed;
                    Program.StartServer();
                    ClientLauncher.Launch();
                    break;

                case "e": case "experimental":
                    Program.StartServer();
                    ClientLauncher.Launch();
                    break;

                default:
                    Console.WriteLine("Unknown option.");
                    break;
            }
        }

        public static void StartServer() {
            global::System.Threading.Tasks.Task.Run(() => {
                var swTotal = new global::System.Diagnostics.Stopwatch();
                var swTick  = new global::System.Diagnostics.Stopwatch();
                double targetMs = 1000.0 / 144.0; // Target exactly 144Hz (~6.94 ms)
                float fixedDt = 1.0f / 144.0f;

                while (true) {
                    swTotal.Restart();
                    swTick.Restart();
                    
                    // 1. Pull atomic input values from Client for asynchronous step
                    var moveDir = ClientLauncher.Camera.MoveDirection;
                    bool sprint = ClientLauncher.Camera.IsSprinting;
                    
                    // 2. Fixed physics simulation step completely decoupled from renderer FPS
                    ClientLauncher.Phys.Update(moveDir, sprint, fixedDt);
                    
                    // 3. Commit results back to atomic network registers for Client Prediction
                    ClientLauncher.ServerPosition = ClientLauncher.Phys.Position;
                    ClientLauncher.ServerVelocity = ClientLauncher.Phys.Velocity;
                    
                    swTick.Stop();
                    ClientLauncher.SystemTimers["S:01 Physics Simulation"] = swTick.Elapsed.TotalMilliseconds;
                    
                    // 4. Hybrid High-Resolution wait for exact 144Hz fixed timing
                    while (swTotal.Elapsed.TotalMilliseconds < targetMs) {
                        double remaining = targetMs - swTotal.Elapsed.TotalMilliseconds;
                        if (remaining > 1.5) global::System.Threading.Thread.Sleep(1);
                        else global::System.Threading.Thread.SpinWait(10);
                    }
                }
            });
        }
    }
}
