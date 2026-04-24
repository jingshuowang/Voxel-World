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
            al = AL.GetApi();
            cl = CL.GetApi();
        }
    }
}
