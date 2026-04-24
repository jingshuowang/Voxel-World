using Silk.NET.OpenGL;
using System.Numerics;

namespace Voxel.Rendering {
    public class Gfx {
        public class S {
            public uint Id;
            private GL _gl;
            public S(GL gl, string vP, string fP) {
                _gl = gl;
                Id = _gl.CreateProgram();
                uint vs = C(File.ReadAllText(vP), ShaderType.VertexShader);
                uint fs = C(File.ReadAllText(fP), ShaderType.FragmentShader);
                _gl.AttachShader(Id, vs); _gl.AttachShader(Id, fs);
                _gl.LinkProgram(Id);
            }
            uint C(string s, ShaderType t) {
                uint i = _gl.CreateShader(t); _gl.ShaderSource(i, s); _gl.CompileShader(i);
                return i;
            }
            public void B() => _gl.UseProgram(Id);
            public void Set(string n, float v) => _gl.Uniform1(_gl.GetUniformLocation(Id, n), v);
            public void Set(string n, Vector3 v) => _gl.Uniform3(_gl.GetUniformLocation(Id, n), v);
            public unsafe void Set(string n, Matrix4x4 v) => _gl.UniformMatrix4(_gl.GetUniformLocation(Id, n), 1, false, (float*)&v);
        }

        public class T {
            public uint Id;
            private GL _gl;
            public T(GL gl, string p) {
                _gl = gl;
                Id = _gl.GenTexture(); _gl.BindTexture(TextureTarget.Texture2D, Id);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
                _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            }
            public void B() => _gl.BindTexture(TextureTarget.Texture2D, Id);
        }
    }
}
