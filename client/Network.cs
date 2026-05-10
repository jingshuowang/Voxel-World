using System;
using System.Collections.Concurrent;
using Voxel.World;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;

namespace Voxel.Rendering {
    public class PlayerState {
        public float X, Y, Z, VX, VY, VZ;
    }

    public class Network {
        readonly ClientWebSocket _ws = new();
        readonly ConcurrentQueue<string> _inbox = new();

        public int LocalId = -1;
        public bool Connected => _ws.State == WebSocketState.Open;
        public ConcurrentDictionary<int, PlayerState> Players = new();
        readonly global::System.Collections.Generic.HashSet<int> _tempPlayerIds = new();

        public async Task Connect(string host, int port = 25565) {
            await _ws.ConnectAsync(new Uri($"ws://{host}:{port}"), CancellationToken.None);
            _ = Task.Run(ReceiveLoop);
        }

        async Task ReceiveLoop() {
            var buf = new byte[65536];
            while (_ws.State == WebSocketState.Open) {
                try {
                    var r = await _ws.ReceiveAsync(buf, CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    _inbox.Enqueue(Encoding.UTF8.GetString(buf, 0, r.Count));
                } catch { break; }
            }
        }

        public void SendState(Vector3 pos, Vector3 vel) {
            if (!Connected) return;
            var json = $"{{\"type\":\"state\",\"pos\":{{\"x\":{pos.X:G6},\"y\":{pos.Y:G6},\"z\":{pos.Z:G6}}},\"vel\":{{\"x\":{vel.X:G6},\"y\":{vel.Y:G6},\"z\":{vel.Z:G6}}}}}";
            var bytes = Encoding.UTF8.GetBytes(json);
            _ = _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public void Poll() {
            while (_inbox.TryDequeue(out var json)) {
                try {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var type = root.GetProperty("type").GetString();

                    if (type == "welcome") {
                        LocalId = root.GetProperty("id").GetInt32();
                        if (root.TryGetProperty("seed", out var seedProp)) {
                            WConfig.Seed = seedProp.GetInt32();
                            Console.WriteLine($"[Net] Server assigned Seed: {WConfig.Seed}");
                        }
                        Console.WriteLine($"[Net] Joined as player {LocalId}");

                    } else if (type == "snapshot") {
                        var snap = root.GetProperty("players");
                        _tempPlayerIds.Clear();
                        foreach (var entry in snap.EnumerateObject()) {
                            if (!int.TryParse(entry.Name, out int id)) continue;
                            _tempPlayerIds.Add(id);
                            
                            var pos = entry.Value.GetProperty("pos");
                            var vel = entry.Value.GetProperty("vel");
                            
                            if (!Players.TryGetValue(id, out var state)) {
                                state = new PlayerState();
                                Players[id] = state;
                            }
                            state.X  = pos.GetProperty("x").GetSingle();
                            state.Y  = pos.GetProperty("y").GetSingle();
                            state.Z  = pos.GetProperty("z").GetSingle();
                            state.VX = vel.GetProperty("x").GetSingle();
                            state.VY = vel.GetProperty("y").GetSingle();
                            state.VZ = vel.GetProperty("z").GetSingle();
                        }
                        
                        // Remove players that were not in this snapshot
                        foreach (var kvp in Players) {
                            if (!_tempPlayerIds.Contains(kvp.Key)) {
                                Players.TryRemove(kvp.Key, out _);
                            }
                        }

                    } else if (type == "leave") {
                        Players.TryRemove(root.GetProperty("id").GetInt32(), out _);
                    }
                } catch { }
            }
        }

        public void Disconnect() {
            try { _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(1000); } catch { }
        }
    }
}
