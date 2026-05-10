using System;
using System.IO;
using System.Numerics;

namespace Voxel.UI
{
    public struct Block
    {
        public string Name { get; set; }
        public Vector3 BaseColor { get; set; }
        public float Roughness { get; set; }
        public float Hardness { get; set; }
        public float Brightness { get; set; }

        public Vector4 ToGpuData()
        {
            if (Name == "Air") return Vector4.Zero;
            // Pack: R, G, B, Emissive. Solid blocks without glow use 1.0f as an identifier.
            return new Vector4(BaseColor.X, BaseColor.Y, BaseColor.Z, Brightness > 0 ? Brightness : 1.0f);
        }
    }

    public static class BlockRegistry
    {
        public static Block[] All = Array.Empty<Block>();

        public static void LoadFromJson(string path)
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                All = global::System.Text.Json.JsonSerializer.Deserialize<Block[]>(json) ?? Array.Empty<Block>();
            }
            else
            {
                Console.WriteLine($"[Warning] {path} not found. Using empty block registry.");
                All = Array.Empty<Block>();
            }
        }

        public static Block Air => GetBlock("Air");
        public static Block Grass => GetBlock("Grass");
        public static Block Stone => GetBlock("Stone");
        public static Block Dirt => GetBlock("Dirt");
        public static Block Snow => GetBlock("Snow");
        public static Block Wood => GetBlock("Wood");
        public static Block Leaves => GetBlock("Leaves");
        public static Block Glowstone => GetBlock("Glowstone");

        private static Block GetBlock(string name)
        {
            foreach (var b in All) {
                if (b.Name == name) return b;
            }
            return All.Length > 0 ? All[0] : new Block { Name = "Air" };
        }
    }
}
