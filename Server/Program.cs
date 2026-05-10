using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Linq;
using WatsonWebsocket;

namespace VoxelServer
{
    public class ServerSettings
    {
        public int Port { get; set; } = 25565;
        public int Seed { get; set; } = 1; // Keeping it int to match engine's format
    }

    class Program
    {
        static WatsonWsServer server = null!;
        static ServerSettings settings = null!;
        static int nextId = 1;
        static ConcurrentDictionary<Guid, int> clientIds = new();
        static ConcurrentDictionary<int, string> playerStates = new(); // Raw JSON strings

        static void Main(string[] args)
        {
            if (!File.Exists("settings.json"))
            {
                var defaultSettings = new ServerSettings();
                File.WriteAllText("settings.json", JsonSerializer.Serialize(defaultSettings, new JsonSerializerOptions { WriteIndented = true }));
            }

            string json = File.ReadAllText("settings.json");
            settings = JsonSerializer.Deserialize<ServerSettings>(json) ?? new ServerSettings();

            Console.WriteLine($"======================================");
            Console.WriteLine($"   BITZEL C# SERVER (Host Database)   ");
            Console.WriteLine($"======================================");
            Console.WriteLine($"Starting server with Seed: {settings.Seed} on Port: {settings.Port}");

            try
            {
                server = new WatsonWsServer("*", settings.Port, false);
                server.ClientConnected += ClientConnected;
                server.ClientDisconnected += ClientDisconnected;
                server.MessageReceived += MessageReceived;
                server.Start();
                Console.WriteLine("Server successfully bound to '*' (listening on all interfaces).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Binding to '*' failed (requires Administrator privileges): {ex.Message}");
                Console.WriteLine("Falling back to '127.0.0.1' (local connections only).");
                server = new WatsonWsServer("127.0.0.1", settings.Port, false);
                server.ClientConnected += ClientConnected;
                server.ClientDisconnected += ClientDisconnected;
                server.MessageReceived += MessageReceived;
                server.Start();
            }

            UpdateTitle();

            // Broadcast loop at 20Hz
            System.Threading.Tasks.Task.Run(async () =>
            {
                while (true)
                {
                    await System.Threading.Tasks.Task.Delay(50); // 20Hz
                    BroadcastSnapshot();
                }
            });

            Console.WriteLine("\n[INFO] Server running! Type /kick <id> or /seed or 'exit' to quit.");
            
            while (true)
            {
                string input = Console.ReadLine() ?? "";
                if (input.ToLower() == "exit") break;
                
                if (input.StartsWith("/kick "))
                {
                    string target = input.Substring(6);
                    if (int.TryParse(target, out int idToKick))
                    {
                        var kvp = clientIds.FirstOrDefault(x => x.Value == idToKick);
                        if (kvp.Key != Guid.Empty)
                        {
                            server.DisconnectClient(kvp.Key);
                            Console.WriteLine($"[Admin] Kicked player {idToKick}");
                        }
                    }
                }
                else if (input.ToLower() == "/seed")
                {
                    Console.WriteLine($"[Admin] Current World Seed is: {settings.Seed}");
                }
            }
        }

        static void UpdateTitle()
        {
            Console.Title = $"My Game Server | Players: {clientIds.Count}/10";
        }

        static void ClientConnected(object? sender, ConnectionEventArgs args)
        {
            int id = nextId++;
            clientIds[args.Client.Guid] = id;
            Console.WriteLine($"[+] Player {id} connected from {args.Client.IpPort}");
            
            // Send welcome with ID and Seed
            string welcomeMsg = $"{{\"type\":\"welcome\",\"id\":{id},\"seed\":{settings.Seed}}}";
            server.SendAsync(args.Client.Guid, welcomeMsg);
            
            UpdateTitle();
        }

        static void ClientDisconnected(object? sender, DisconnectionEventArgs args)
        {
            if (clientIds.TryRemove(args.Client.Guid, out int id))
            {
                Console.WriteLine($"[-] Player {id} disconnected");
                playerStates.TryRemove(id, out _);
                
                // Broadcast leave
                string leaveMsg = $"{{\"type\":\"leave\",\"id\":{id}}}";
                foreach (var client in server.ListClients())
                {
                    server.SendAsync(client.Guid, leaveMsg);
                }
            }
            UpdateTitle();
        }

        static void MessageReceived(object? sender, MessageReceivedEventArgs args)
        {
            if (clientIds.TryGetValue(args.Client.Guid, out int id))
            {
                string msg = Encoding.UTF8.GetString(args.Data.Array!, args.Data.Offset, args.Data.Count);
                // Print package info to terminal for hoster
                Console.WriteLine($"[Network] Package received from Player {id} -> Size: {args.Data.Count} bytes");

                if (msg.Contains("\"type\":\"state\""))
                {
                    // The string looks like {"type":"state","pos":{...},"vel":{...}}
                    // We extract just the state part to assemble into a snapshot later.
                    // This avoids deserializing on the server to keep it purely a relay database!
                    int firstComma = msg.IndexOf(',');
                    if (firstComma != -1) {
                        string stateOnly = "{" + msg.Substring(firstComma + 1);
                        playerStates[id] = stateOnly;
                    }
                }
            }
        }

        static void BroadcastSnapshot()
        {
            if (playerStates.IsEmpty) return;

            var sb = new StringBuilder();
            sb.Append("{\"type\":\"snapshot\",\"players\":{");
            bool first = true;
            foreach (var kvp in playerStates)
            {
                if (!first) sb.Append(",");
                sb.Append($"\"{kvp.Key}\":{kvp.Value}");
                first = false;
            }
            sb.Append("}}");

            string snapshotMsg = sb.ToString();

            foreach (var client in server.ListClients())
            {
                server.SendAsync(client.Guid, snapshotMsg);
            }
        }
    }
}
