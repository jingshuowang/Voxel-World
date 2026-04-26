using Silk.NET.OpenGL;
using Silk.NET.Input;
using System.Numerics;

namespace Voxel.Client {
    public unsafe class Gfx {
        private GL _gl;
        private uint _rayProg, _displayProg;
        private uint _voxelTex, _screenTex, _prevTex;
        private uint _quadVao, _quadVbo;
        public int Width, Height;
        private int _frame = 0;

        public Vector3 Pos = new(64, 50, 150);
        public float Yaw = -90, Pitch = -25;
        private Vector2 _lastMouse;
        private bool _firstMouse = true;

        public Gfx(GL gl, int width, int height) {
            _gl = gl; Width = width; Height = height;
            _voxelTex = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture3D, _voxelTex);
            _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
            _screenTex = CreateTex(width, height);
            _prevTex = CreateTex(width, height);
            _rayProg = Compile(CS_RAY, ShaderType.ComputeShader);
            _displayProg = CompileProg(VS, FS);
            float[] quad = { -1, 1, 0, 1,  -1, -1, 0, 0,  1, 1, 1, 1,  1, -1, 1, 0 };
            _quadVao = _gl.GenVertexArray(); _gl.BindVertexArray(_quadVao);
            _quadVbo = _gl.GenBuffer(); _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
            fixed (float* p = quad) _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * 4), p, BufferUsageARB.StaticDraw);
            _gl.EnableVertexAttribArray(0); _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 16, (void*)0);
            _gl.EnableVertexAttribArray(1); _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 16, (void*)8);
        }

        public void ProcessInput(IKeyboard kb, IMouse mouse, float dt) {
            float s = 40.0f * dt;
            Vector3 f = Forward;
            Vector3 r = Vector3.Normalize(Vector3.Cross(f, Vector3.UnitY));
            if (kb.IsKeyPressed(Key.W)) Pos += f * s;
            if (kb.IsKeyPressed(Key.S)) Pos -= f * s;
            if (kb.IsKeyPressed(Key.A)) Pos -= r * s;
            if (kb.IsKeyPressed(Key.D)) Pos += r * s;
            if (kb.IsKeyPressed(Key.Space)) Pos.Y += s;
            if (kb.IsKeyPressed(Key.ShiftLeft)) Pos.Y -= s;
            var mPos = mouse.Position;
            if (_firstMouse) { _lastMouse = mPos; _firstMouse = false; }
            Yaw += (mPos.X - _lastMouse.X) * 0.1f;
            Pitch = Math.Clamp(Pitch - (mPos.Y - _lastMouse.Y) * 0.1f, -89, 89);
            _lastMouse = mPos;
        }

        public Vector3 Forward => new(
            MathF.Cos(Yaw * MathF.PI / 180) * MathF.Cos(Pitch * MathF.PI / 180),
            MathF.Sin(Pitch * MathF.PI / 180),
            MathF.Sin(Yaw * MathF.PI / 180) * MathF.Cos(Pitch * MathF.PI / 180)
        );

        public void UpdateVoxels(float[] voxels) {
            _gl.BindTexture(TextureTarget.Texture3D, _voxelTex);
            fixed (float* p = voxels) _gl.TexImage3D(TextureTarget.Texture3D, 0, (int)InternalFormat.Rgba32f, 128, 128, 128, 0, PixelFormat.Rgba, PixelType.Float, p);
        }

        private uint CreateTex(int w, int h) {
            uint t = _gl.GenTexture(); _gl.BindTexture(TextureTarget.Texture2D, t);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba32f, (uint)w, (uint)h, 0, PixelFormat.Rgba, PixelType.Float, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Linear);
            return t;
        }

        public void Render(float velocity, float time) {
            _frame++;
            _gl.UseProgram(_rayProg);
            _gl.BindImageTexture(0, _screenTex, 0, false, 0, BufferAccessARB.ReadWrite, InternalFormat.Rgba32f);
            _gl.BindImageTexture(1, _prevTex, 0, false, 0, BufferAccessARB.ReadOnly, InternalFormat.Rgba32f);
            _gl.BindTexture(TextureTarget.Texture3D, _voxelTex);
            _gl.Uniform3(_gl.GetUniformLocation(_rayProg, "uCamPos"), Pos);
            _gl.Uniform3(_gl.GetUniformLocation(_rayProg, "uCamForward"), Forward);
            _gl.Uniform1(_gl.GetUniformLocation(_rayProg, "uFrame"), _frame);
            _gl.Uniform1(_gl.GetUniformLocation(_rayProg, "uVelocity"), velocity);
            _gl.Uniform1(_gl.GetUniformLocation(_rayProg, "uTime"), time);
            _gl.DispatchCompute((uint)((Width + 7) / 8), (uint)((Width + 7) / 8), 1);
            _gl.MemoryBarrier(MemoryBarrierMask.ShaderImageAccessBarrierBit);
            _gl.Viewport(0, 0, (uint)Width, (uint)Height);
            _gl.UseProgram(_displayProg);
            _gl.ActiveTexture(TextureUnit.Texture0); _gl.BindTexture(TextureTarget.Texture2D, _screenTex);
            _gl.Uniform1(_gl.GetUniformLocation(_displayProg, "uTex"), 0);
            _gl.BindVertexArray(_quadVao);
            _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            _gl.CopyImageSubData(_screenTex, (GLEnum)TextureTarget.Texture2D, 0, 0, 0, 0, _prevTex, (GLEnum)TextureTarget.Texture2D, 0, 0, 0, 0, (uint)Width, (uint)Height, 1);
        }

        private uint Compile(string src, ShaderType type) {
            uint s = _gl.CreateShader(type); _gl.ShaderSource(s, src); _gl.CompileShader(s);
            uint p = _gl.CreateProgram(); _gl.AttachShader(p, s); _gl.LinkProgram(p); return p;
        }
        private uint CompileProg(string vs, string fs) {
            uint v = _gl.CreateShader(ShaderType.VertexShader); _gl.ShaderSource(v, vs); _gl.CompileShader(v);
            uint f = _gl.CreateShader(ShaderType.FragmentShader); _gl.ShaderSource(f, fs); _gl.CompileShader(f);
            uint p = _gl.CreateProgram(); _gl.AttachShader(p, v); _gl.AttachShader(p, f); _gl.LinkProgram(p); return p;
        }

        const string CS_RAY = @"#version 460 core
layout(local_size_x = 8, local_size_y = 8) in;
layout(rgba32f, binding = 0) uniform image2D imgOut;
layout(rgba32f, binding = 1) uniform readonly image2D imgPrev;
uniform sampler3D voxels;
uniform vec3 uCamPos, uCamForward;
uniform int uFrame;
uniform float uVelocity, uTime;

float hash(uint n) { n = (n << 13U) ^ n; return float((n * (n * n * 15731U + 789221U) + 1376312589U) & 0x7FFFFFFFu) / 2147483648.0; }
vec3 randDir(uint seed) {
    float u = hash(seed) * 2.0 - 1.0;
    float v = hash(seed + 1234) * 6.283;
    float s = sqrt(max(0.0, 1.0 - u*u));
    return vec3(s * cos(v), s * sin(v), u);
}

float intersect(vec3 ro, vec3 rd, out vec3 norm, out vec3 color, out float emissive) {
    vec3 mapPos = floor(ro); vec3 deltaDist = abs(1.0 / rd); vec3 rayStep = sign(rd);
    vec3 sideDist = (rayStep * (mapPos - ro) + (rayStep * 0.5 + 0.5)) * deltaDist;
    int side = 0;
    for (int i = 0; i < 400; i++) {
        vec3 texPos = (mapPos + 64.0) / 128.0;
        if (texPos.x >= 0.0 && texPos.x <= 1.0 && texPos.y >= 0.0 && texPos.y <= 1.0 && texPos.z >= 0.0 && texPos.z <= 1.0) {
            vec4 val = texture(voxels, texPos);
            if (val.a != 0.0 || val.x > 0.0) { 
                color = val.rgb; emissive = val.a; 
                if (side == 0) { norm = vec3(-rayStep.x, 0, 0); return (sideDist.x - deltaDist.x); }
                if (side == 1) { norm = vec3(0, -rayStep.y, 0); return (sideDist.y - deltaDist.y); }
                norm = vec3(0, 0, -rayStep.z); return (sideDist.z - deltaDist.z);
            }
        }
        if (sideDist.x < sideDist.y && sideDist.x < sideDist.z) { sideDist.x += deltaDist.x; mapPos.x += rayStep.x; side = 0; }
        else if (sideDist.y < sideDist.z) { sideDist.y += deltaDist.y; mapPos.y += rayStep.y; side = 1; }
        else { sideDist.z += deltaDist.z; mapPos.z += rayStep.z; side = 2; }
    }
    return -1.0;
}

vec3 getSky(vec3 rd, vec3 sunDir, vec3 sunCol) {
    float t = 0.5 * (rd.y + 1.0);
    vec3 sky = mix(vec3(0.0, 0.0, 0.01), vec3(0.1, 0.2, 0.5), t);
    float sun = pow(max(dot(rd, sunDir), 0.0), 512.0) * 30.0;
    return sky + sunCol * sun;
}

vec3 samplePixel(vec2 uv, uint seed, vec3 sunDir, vec3 sunCol) {
    vec3 r = normalize(cross(uCamForward, vec3(0,1,0))), u = cross(r, uCamForward);
    vec3 rd = normalize(uCamForward + r * uv.x + u * uv.y);
    vec3 norm, color; float emissive; float dist = intersect(uCamPos, rd, norm, color, emissive);
    
    if (dist > 0.0) {
        vec3 hitPos = uCamPos + rd * dist;
        vec3 sN, sC; float sE;
        float sD = intersect(hitPos + norm * 0.01, sunDir, sN, sC, sE);
        float shad = (sD > 0.0) ? 0.0 : 1.0;
        
        // MULTI-SAMPLE COLOR BLEEDING for stability
        vec3 bleedAccum = vec3(0.0);
        for(int i=0; i<2; i++) {
            vec3 bDir = normalize(norm + randDir(seed + i*777));
            vec3 bN, bC; float bE;
            float bD = intersect(hitPos + norm * 0.01, bDir, bN, bC, bE);
            bleedAccum += (bD > 0.0) ? bC * 0.3 : getSky(bDir, sunDir, sunCol) * 0.1;
        }
        vec3 bleedCol = min(bleedAccum / 2.0, vec3(1.0));
        return color * (sunCol * max(dot(norm, sunDir), 0.0) * shad + bleedCol);
    }
    return getSky(rd, sunDir, sunCol);
}

void main() {
    ivec2 p = ivec2(gl_GlobalInvocationID.xy); ivec2 sz = imageSize(imgOut);
    if (p.x >= sz.x || p.y >= sz.y) return;
    uint seed = uint(p.x + p.y * sz.x + uFrame * 1234);
    
    vec3 sunDir = normalize(vec3(0.5, 0.8, 0.3));
    vec3 sunCol = vec3(1.0, 0.6, 0.2);
    
    // AVERAGE MULTIPLE RAYS PER PIXEL
    vec3 colorAccum = vec3(0.0);
    float jitterAmt = 0.05 + uVelocity * 1.2;
    for(int i=0; i<2; i++) {
        float h = hash(seed + i*1337);
        vec2 jitter = (vec2(h, hash(seed + i*555)) - 0.5) * jitterAmt;
        vec2 uv = ((vec2(p) + jitter) / vec2(sz)) * 2.0 - 1.0; uv.x *= float(sz.x) / float(sz.y);
        colorAccum += samplePixel(uv, seed + i, sunDir, sunCol);
    }
    vec3 final = colorAccum / 2.0;

    // VOLUMETRIC (Still single sample per main ray for performance)
    float vol = 0.0;
    vec3 r_main = normalize(cross(uCamForward, vec3(0,1,0))), u_main = cross(r_main, uCamForward);
    vec2 uv_main = (vec2(p) / vec2(sz)) * 2.0 - 1.0; uv_main.x *= float(sz.x) / float(sz.y);
    vec3 rd_main = normalize(uCamForward + r_main * uv_main.x + u_main * uv_main.y);
    vec3 n_m, c_m; float e_m; float dist_main = intersect(uCamPos, rd_main, n_m, c_m, e_m);
    float maxVolDist = (dist_main > 0.0) ? dist_main : 400.0;
    for (int i = 0; i < 4; i++) {
        float t = maxVolDist * (float(i) + hash(seed + uint(i))) / 4.0;
        vec3 vPos = uCamPos + rd_main * t;
        vec3 vN, vC; float vE;
        if (intersect(vPos, sunDir, vN, vC, vE) < 0.0) vol += 1.0;
    }
    final += sunCol * (vol / 4.0) * 0.1;

    vec3 prev = imageLoad(imgPrev, p).rgb;
    imageStore(imgOut, p, vec4(mix(final, prev, 0.3), 1.0)); 
}";

        const string VS = @"#version 460 core
layout(location=0) in vec2 aPos; layout(location=1) in vec2 aUV; out vec2 vUV;
void main() { gl_Position = vec4(aPos, 0.0, 1.0); vUV = aUV; }";

        const string FS = @"#version 460 core
in vec2 vUV; out vec4 fC;
uniform sampler2D uTex;
void main() { 
    vec3 c = texture(uTex, vUV).rgb;
    c = c / (c + 1.0); c = pow(c, vec3(1.0/2.2)); fC = vec4(c, 1.0); 
}";
    }
}
