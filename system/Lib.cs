using Silk.NET.OpenGL;
using Silk.NET.OpenAL;
using Silk.NET.OpenCL;

namespace Voxel.System {
    public static class Lib {
        public static GL gl { get; set; } = null!;
        public static AL al { get; set; } = null!;
        public static CL cl { get; set; } = null!;
        
        public static void Init(GL g) {
            gl = g;
            try { al = AL.GetApi(); } catch { Console.WriteLine("[WARN] OpenAL not found."); }
            try { cl = CL.GetApi(); } catch { Console.WriteLine("[WARN] OpenCL not found."); }
        }
    }
}
