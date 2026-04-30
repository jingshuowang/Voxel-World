using Silk.NET.OpenGL;
using Silk.NET.Input;
using System.Numerics;

namespace Voxel.Rendering {
    public unsafe class Render {
        private GL _gl;
        private uint _traceProg, _displayProg, _physicsProg, _taaProg;
        private uint _screenTex, _voxelTex, _lutTex, _entitySsbo;
        private uint _worldPosTex, _historyTex, _resolvedTex;
        private uint _quadVao, _quadVbo;
        private float* _entityMappedPtr;
        private int _frame;
        private Vector3 Pos, PrevPos;
        public Vector3 Forward, PrevForward;
        private Vector3 PrevRight, PrevUp;
        private float[] _localVoxels = new float[32 * 32 * 32 * 4];
        private int Width, Height;
        private bool _firstMouse = true;
        private Vector2 _lastMouse;
        public float Yaw = -90, Pitch = 0;
        public int RenderMode = 0; // 0=Raytraced, 1=Edges, 2=Normals, 3=Depth

        public Render(GL gl, int w, int h) {
            _gl = gl; Width = w; Height = h;
            _screenTex = CreateTex(w, h);
            _worldPosTex = CreateTex(w, h);
            _historyTex = CreateTex(w, h);
            _resolvedTex = CreateTex(w, h);
            _voxelTex = _gl.GenTexture(); _gl.BindTexture(TextureTarget.Texture3D, _voxelTex);
            _gl.TexImage3D(TextureTarget.Texture3D, 0, (int)InternalFormat.Rgba32f, 32, 32, 32, 0, PixelFormat.Rgba, PixelType.Float, null);
            _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Linear);

            _traceProg = Compile(CS_TRACE, ShaderType.ComputeShader);
            _physicsProg = Compile(CS_PHYSICS, ShaderType.ComputeShader);
            _taaProg = Compile(CS_TAA, ShaderType.ComputeShader);
            _displayProg = CompileProg(VS, FS);

            _entitySsbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _entitySsbo);
            uint flags = (uint)(MapBufferAccessMask.ReadBit | MapBufferAccessMask.WriteBit | MapBufferAccessMask.PersistentBit | MapBufferAccessMask.CoherentBit);
            _gl.BufferStorage((GLEnum)BufferTargetARB.ShaderStorageBuffer, 65536, null, flags);
            _entityMappedPtr = (float*)_gl.MapBufferRange(BufferTargetARB.ShaderStorageBuffer, 0, 65536, flags);
            _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 1, _entitySsbo);

            _lutTex = LoadTexture("Asset/Image/Spritesheet/LUT.png");

            float[] quad = { -1,-1,0,0, 1,-1,1,0, -1,1,0,1, 1,1,1,1 };
            _quadVao = _gl.GenVertexArray(); _quadVbo = _gl.GenBuffer();
            _gl.BindVertexArray(_quadVao); _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
            fixed (float* p = quad) _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * 4), p, BufferUsageARB.StaticDraw);
            _gl.EnableVertexAttribArray(0); _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 16, (void*)0);
            _gl.EnableVertexAttribArray(1); _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 16, (void*)8);
        }

        private uint LoadTexture(string path) {
            if (!global::System.IO.File.Exists(path)) return 0;
            using var stream = global::System.IO.File.OpenRead(path);
            var image = StbImageSharp.ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            uint t = _gl.GenTexture(); _gl.BindTexture(TextureTarget.Texture2D, t);
            fixed (byte* p = image.Data) _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba, (uint)image.Width, (uint)image.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            return t;
        }

        public (Vector3 move, bool jump, bool sprint, bool crouch) GetInputState(IKeyboard kb, IMouse mouse) {
            float yawRad = Yaw * MathF.PI / 180.0f;
            Vector3 f = new Vector3(MathF.Cos(yawRad), 0, MathF.Sin(yawRad));
            Vector3 r = new Vector3(-MathF.Sin(yawRad), 0, MathF.Cos(yawRad));
            
            Vector3 move = Vector3.Zero;
            if (kb.IsKeyPressed(Key.W)) move += f;
            if (kb.IsKeyPressed(Key.S)) move -= f;
            if (kb.IsKeyPressed(Key.A)) move -= r;
            if (kb.IsKeyPressed(Key.D)) move += r;
            if (move.LengthSquared() > 0) move = Vector3.Normalize(move);

            bool jump = kb.IsKeyPressed(Key.Space);
            bool sprint = kb.IsKeyPressed(Key.ControlLeft) || kb.IsKeyPressed(Key.ControlRight);
            bool crouch = kb.IsKeyPressed(Key.ShiftLeft) || kb.IsKeyPressed(Key.ShiftRight);

            var mPos = mouse.Position;
            if (_firstMouse) { _lastMouse = mPos; _firstMouse = false; }
            if (mouse.Cursor.CursorMode == CursorMode.Raw) {
                float dx = mPos.X - _lastMouse.X;
                float dy = mPos.Y - _lastMouse.Y;
                Yaw = (Yaw + dx * 0.15f) % 360.0f;
                Pitch = Math.Clamp(Pitch - dy * 0.15f, -89.0f, 89.0f);
            }
            _lastMouse = mPos;

            float pitchRad = Pitch * MathF.PI / 180.0f;
            Forward = new(
                MathF.Cos(yawRad) * MathF.Cos(pitchRad),
                MathF.Sin(pitchRad),
                MathF.Sin(yawRad) * MathF.Cos(pitchRad)
            );

            return (move, jump, sprint, crouch);
        }

        private float Halton(int index, int baseVal) {
            float f = 1; float r = 0;
            while (index > 0) {
                f = f / baseVal;
                r = r + f * (index % baseVal);
                index = (int)(index / baseVal);
            }
            return r;
        }

        public bool NeedTextureUpload = false;

        public void PrepareSlidingWindow(World world, Vector3 playerPos) {
            Program.RecordTimer("PrepareSlidingWindow: Start");
            PrevPos = Pos;
            PrevForward = Forward;
            Pos = playerPos;
            world.FillLocalBuffer(_localVoxels, playerPos);
            Program.RecordTimer("PrepareSlidingWindow: FillLocalBuffer");
            NeedTextureUpload = true;
        }

        public void UploadSlidingWindow() {
            if (!NeedTextureUpload) return;
            Program.RecordTimer("UploadSlidingWindow: Start");
            _gl.BindTexture(TextureTarget.Texture3D, _voxelTex);
            fixed (float* p = _localVoxels) _gl.TexImage3D(TextureTarget.Texture3D, 0, (int)InternalFormat.Rgba32f, 32, 32, 32, 0, PixelFormat.Rgba, PixelType.Float, p);
            Program.RecordTimer("UploadSlidingWindow: TexImage3D");
            NeedTextureUpload = false;
        }

        public void Draw(Vector3 playerPos, float playerHeight, float velocity, float time, int entityCount) {
            Program.RecordTimer("Render: Start");
            Pos = playerPos;
            _frame++;
            int scaledW = (int)(Width * Config.ResolutionScale);
            int scaledH = (int)(Height * Config.ResolutionScale);

            Vector3 Right = Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));
            Vector3 Up = Vector3.Normalize(Vector3.Cross(Right, Forward));

            float jitterX = Config.EnableTAA ? (Halton(_frame % 16 + 1, 2) - 0.5f) : 0.0f;
            float jitterY = Config.EnableTAA ? (Halton(_frame % 16 + 1, 3) - 0.5f) : 0.0f;

            // Pass 1: Raytrace
            _gl.UseProgram(_traceProg);
            _gl.BindImageTexture(0, _screenTex, 0, false, 0, BufferAccessARB.ReadWrite, InternalFormat.Rgba32f);
            _gl.BindImageTexture(1, _worldPosTex, 0, false, 0, BufferAccessARB.WriteOnly, InternalFormat.Rgba32f);
            _gl.BindTexture(TextureTarget.Texture3D, _voxelTex);
            _gl.Uniform1(_gl.GetUniformLocation(_traceProg, "voxels"), 0);

            _gl.Uniform3(_gl.GetUniformLocation(_traceProg, "uCamPos"), playerPos + new Vector3(0, playerHeight - 0.2f, 0));
            _gl.Uniform3(_gl.GetUniformLocation(_traceProg, "uCamForward"), Forward);
            _gl.Uniform3(_gl.GetUniformLocation(_traceProg, "uCamRight"), Right);
            _gl.Uniform3(_gl.GetUniformLocation(_traceProg, "uCamUp"), Up);
            _gl.Uniform2(_gl.GetUniformLocation(_traceProg, "uJitter"), new Vector2(jitterX, jitterY));
            _gl.Uniform1(_gl.GetUniformLocation(_traceProg, "uFrame"), _frame);
            _gl.Uniform1(_gl.GetUniformLocation(_traceProg, "uEnableLOD"), Config.EnableLOD ? 1 : 0);
            _gl.Uniform1(_gl.GetUniformLocation(_traceProg, "uRenderRadius"), (float)Config.RenderDistance);
            _gl.Uniform1(_gl.GetUniformLocation(_traceProg, "uLodHigh"), (float)Config.LodDistances[0]);
            _gl.Uniform1(_gl.GetUniformLocation(_traceProg, "uLodMed"), (float)Config.LodDistances[1]);
            _gl.Uniform1(_gl.GetUniformLocation(_traceProg, "uEntityCount"), entityCount);
            // Dynamic FOV based on velocity
            _gl.Uniform1(_gl.GetUniformLocation(_traceProg, "uFovFactor"), 1.0f + (velocity * 0.02f));
            
            // Fix: pass sliding window offset to the shader
            int half = 32 / 2;
            Vector3 windowOffset = new Vector3(MathF.Floor(Pos.X) - half, MathF.Floor(Pos.Y) - half, MathF.Floor(Pos.Z) - half);
            _gl.Uniform3(_gl.GetUniformLocation(_traceProg, "uWindowOffset"), windowOffset);
            
            _gl.Uniform1(_gl.GetUniformLocation(_traceProg, "uRenderMode"), RenderMode);
            _gl.Uniform1(_gl.GetUniformLocation(_traceProg, "uSeed"), (int)World.Seed);
            
            Program.RecordTimer("Render: Pass 1 Raytrace Uniforms");
            _gl.DispatchCompute((uint)((scaledW + 7) / 8), (uint)((scaledH + 7) / 8), 1);
            _gl.MemoryBarrier(MemoryBarrierMask.ShaderImageAccessBarrierBit);
            Program.RecordTimer("Render: Pass 1 Raytrace Dispatch");
            
            // Pass 2: TAA Resolve
            if (Config.EnableTAA) {
                _gl.UseProgram(_taaProg);
                _gl.BindImageTexture(0, _screenTex, 0, false, 0, BufferAccessARB.ReadOnly, InternalFormat.Rgba32f);
                _gl.BindImageTexture(1, _worldPosTex, 0, false, 0, BufferAccessARB.ReadOnly, InternalFormat.Rgba32f);
                _gl.BindImageTexture(2, _historyTex, 0, false, 0, BufferAccessARB.ReadOnly, InternalFormat.Rgba32f);
                _gl.BindImageTexture(3, _resolvedTex, 0, false, 0, BufferAccessARB.WriteOnly, InternalFormat.Rgba32f);
                
                _gl.Uniform3(_gl.GetUniformLocation(_taaProg, "uPrevCamPos"), PrevPos + new Vector3(0, playerHeight - 0.2f, 0));
                _gl.Uniform3(_gl.GetUniformLocation(_taaProg, "uPrevCamForward"), PrevForward);
                _gl.Uniform3(_gl.GetUniformLocation(_taaProg, "uPrevCamRight"), PrevRight);
                _gl.Uniform3(_gl.GetUniformLocation(_taaProg, "uPrevCamUp"), PrevUp);
                _gl.Uniform1(_gl.GetUniformLocation(_taaProg, "uFovFactor"), 1.0f + (velocity * 0.02f)); // Assuming approx same velocity

                Program.RecordTimer("Render: Pass 2 TAA Resolve Setup");
                _gl.DispatchCompute((uint)((scaledW + 7) / 8), (uint)((scaledH + 7) / 8), 1);
                _gl.MemoryBarrier(MemoryBarrierMask.ShaderImageAccessBarrierBit);
                Program.RecordTimer("Render: Pass 2 TAA Resolve Dispatch");

                // Ping-Pong buffers
                uint temp = _historyTex;
                _historyTex = _resolvedTex;
                _resolvedTex = temp;
            }

            // Store history state
            PrevPos = Pos;
            PrevForward = Forward;
            PrevRight = Right;
            PrevUp = Up;

            // Pass 3: Display
            _gl.Viewport(0, 0, (uint)Width, (uint)Height);
            _gl.UseProgram(_displayProg);
            _gl.ActiveTexture(TextureUnit.Texture0); _gl.BindTexture(TextureTarget.Texture2D, Config.EnableTAA ? _historyTex : _screenTex);
            _gl.Uniform1(_gl.GetUniformLocation(_displayProg, "uTex"), 0);
            _gl.ActiveTexture(TextureUnit.Texture1); _gl.BindTexture(TextureTarget.Texture2D, _lutTex);
            _gl.Uniform1(_gl.GetUniformLocation(_displayProg, "uLutTex"), 1);
            _gl.Uniform1(_gl.GetUniformLocation(_displayProg, "uExposure"), Config.Exposure);
            _gl.Uniform1(_gl.GetUniformLocation(_displayProg, "uContrast"), Config.Contrast);
            _gl.Uniform1(_gl.GetUniformLocation(_displayProg, "uSaturation"), Config.Saturation);
            _gl.Uniform1(_gl.GetUniformLocation(_displayProg, "uBrightness"), Config.Brightness);
            _gl.Uniform1(_gl.GetUniformLocation(_displayProg, "uWhitePoint"), Config.WhitePoint);
            _gl.Uniform1(_gl.GetUniformLocation(_displayProg, "uGamma"), Config.Gamma);

            _gl.BindVertexArray(_quadVao);
            _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        }

        public void DispatchPhysics(float dt, uint seed, Vector3 input, Vector3 playerParams, int entityCount) {
            Program.RecordTimer("DispatchPhysics: Start");
            _gl.UseProgram(_physicsProg);
            _gl.Uniform1(_gl.GetUniformLocation(_physicsProg, "uDt"), dt);
            _gl.Uniform1(_gl.GetUniformLocation(_physicsProg, "uSeed"), seed);
            _gl.Uniform3(_gl.GetUniformLocation(_physicsProg, "uInput"), input);
            _gl.Uniform3(_gl.GetUniformLocation(_physicsProg, "uPlayerParams"), playerParams);
            _gl.Uniform1(_gl.GetUniformLocation(_physicsProg, "uEntityCount"), entityCount);
            
            _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 1, _entitySsbo);
            _gl.DispatchCompute((uint)((entityCount + 63) / 64), 1, 1);
            _gl.MemoryBarrier(MemoryBarrierMask.BufferUpdateBarrierBit);
            Program.RecordTimer("DispatchPhysics: Dispatch");
        }

        public unsafe void ReadEntities(float[] buffer, int count) {
            Program.RecordTimer("ReadEntities: Start");
            if (_entityMappedPtr != null) {
                fixed (float* p = buffer) {
                    global::System.Buffer.MemoryCopy(_entityMappedPtr, p, count * 16 * 4, count * 16 * 4);
                }
            }
            Program.RecordTimer("ReadEntities: MemoryCopy");
        }

        public unsafe void WriteEntities(float[] buffer, int count) {
            Program.RecordTimer("WriteEntities: Start");
            if (_entityMappedPtr != null) {
                fixed (float* p = buffer) {
                    global::System.Buffer.MemoryCopy(p, _entityMappedPtr, 65536, count * 16 * 4);
                }
            }
            Program.RecordTimer("WriteEntities: MemoryCopy");
        }
        public unsafe void UpdatePlayerEntity(Vector3 pos) {
            if (_entityMappedPtr != null) {
                _entityMappedPtr[0] = pos.X;
                _entityMappedPtr[1] = pos.Y;
                _entityMappedPtr[2] = pos.Z;
            }
        }

        private uint CreateTex(int w, int h) {
            uint t = _gl.GenTexture(); _gl.BindTexture(TextureTarget.Texture2D, t);
            int sw = (int)(w * Config.ResolutionScale);
            int sh = (int)(h * Config.ResolutionScale);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba32f, (uint)sw, (uint)sh, 0, PixelFormat.Rgba, PixelType.Float, null);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            return t;
        }

        private uint Compile(string src, ShaderType type) {
            uint s = _gl.CreateShader(type); _gl.ShaderSource(s, src); _gl.CompileShader(s);
            string info = _gl.GetShaderInfoLog(s);
            if (!string.IsNullOrWhiteSpace(info)) Console.WriteLine($"Shader Error ({type}): {info}");
            uint p = _gl.CreateProgram(); _gl.AttachShader(p, s); _gl.LinkProgram(p); 
            string pInfo = _gl.GetProgramInfoLog(p);
            if (!string.IsNullOrWhiteSpace(pInfo)) Console.WriteLine($"Program Error: {pInfo}");
            return p;
        }
        private uint CompileProg(string vs, string fs) {
            uint v = _gl.CreateShader(ShaderType.VertexShader); _gl.ShaderSource(v, vs); _gl.CompileShader(v);
            string vInfo = _gl.GetShaderInfoLog(v);
            if (!string.IsNullOrWhiteSpace(vInfo)) Console.WriteLine($"Vertex Error: {vInfo}");
            uint f = _gl.CreateShader(ShaderType.FragmentShader); _gl.ShaderSource(f, fs); _gl.CompileShader(f);
            string fInfo = _gl.GetShaderInfoLog(f);
            if (!string.IsNullOrWhiteSpace(fInfo)) Console.WriteLine($"Fragment Error: {fInfo}");
            uint p = _gl.CreateProgram(); _gl.AttachShader(p, v); _gl.AttachShader(p, f); _gl.LinkProgram(p); 
            string pInfo = _gl.GetProgramInfoLog(p);
            if (!string.IsNullOrWhiteSpace(pInfo)) Console.WriteLine($"Program Error: {pInfo}");
            return p;
        }

        const string CS_TRACE = @"#version 460 core
layout(local_size_x = 8, local_size_y = 8) in;
layout(rgba32f, binding = 0) uniform image2D imgOut;
layout(rgba32f, binding = 1) uniform image2D imgWorldPos;
uniform sampler3D voxels;
uniform vec3 uCamPos, uCamForward, uCamRight, uCamUp;
uniform int uFrame, uEnableLOD, uRenderMode;
uniform float uRenderRadius, uLodHigh, uLodMed, uFovFactor;
uniform vec3 uWindowOffset;
uniform int uEntityCount;
uniform uint uSeed;
uniform vec2 uJitter;

struct Entity {
    vec4 pos;
    vec4 vel;
    vec4 data; // x=type(0p, 1c, 2s), y=size, z=mass, w=restitution
    vec4 friction; // x=friction, yzw=unused
};

layout(std430, binding = 1) buffer EntityBuffer {
    Entity entities[];
};

float hash(uint n) { n = (n << 13U) ^ n; return float((n * (n * n * 15731U + 789221U) + 1376312589U) & 0x7FFFFFFFu) / 2147483648.0; }
vec3 randDir(uint seed) {
    float u = hash(seed) * 2.0 - 1.0;
    float v = hash(seed + 1234) * 6.283;
    float s = sqrt(max(0.0, 1.0 - u*u));
    return vec3(s * cos(v), s * sin(v), u);
}

float jingshuo_hash(ivec2 p, uint seed) {
    uint h = uint(p.x) * 73856093U ^ uint(p.y) * 83492791U ^ seed;
    h = (h ^ (h >> 16)) * 0x85ebca6b;
    h = (h ^ (h >> 13)) * 0xc2b2ae35;
    h ^= (h >> 16);
    return float(h) / 4294967295.0;
}

float value_noise(vec2 p, uint seed) {
    ivec2 i = ivec2(floor(p));
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    float a = jingshuo_hash(i + ivec2(0, 0), seed);
    float b = jingshuo_hash(i + ivec2(1, 0), seed);
    float c = jingshuo_hash(i + ivec2(0, 1), seed);
    float d = jingshuo_hash(i + ivec2(1, 1), seed);
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float fbm(vec2 p, uint seed) {
    float v = 0.0;
    float a = 0.5;
    vec2 shift = vec2(100.0);
    for (int i = 0; i < 4; i++) {
        v += a * value_noise(p, seed);
        p = p * 2.0 + shift;
        a *= 0.5;
    }
    return v;
}

float getH(vec2 p) {
    // Math exactly matches C# World.cs fallback
    return 20.0 + fbm(p * 0.01, uSeed) * 30.0;
}

vec3 getSky(vec3 rd, vec3 sunDir, vec3 sunCol) {
    float t = 0.5 * (rd.y + 1.0);
    vec3 sky = mix(vec3(0.01, 0.02, 0.05), vec3(0.2, 0.4, 0.8), t);
    float sun = pow(max(dot(rd, sunDir), 0.0), 1024.0) * 40.0;
    return sky + sunCol * sun;
}

float intersect(vec3 ro, vec3 rd, out vec3 norm, out vec3 color, out float emissive) {
    vec3 mapPos = floor(ro); vec3 deltaDist = abs(1.0 / rd); vec3 rayStep = sign(rd);
    vec3 sideDist = (rayStep * (mapPos - ro) + (rayStep * 0.5 + 0.5)) * deltaDist;
    int side = 0;
    float lastH = -1.0;
    vec2 lastXZ = vec2(1e9);

    for (int i = 0; i < 1500; i++) {
        // Fix: subtract windowOffset to map world coords to the 3D texture buffer
        vec3 texPos = (mapPos - uWindowOffset + 0.5) / 32.0;
        bool inBounds = texPos.x >= 0.0 && texPos.x < 1.0 && texPos.y >= 0.0 && texPos.y < 1.0 && texPos.z >= 0.0 && texPos.z < 1.0;
        
        bool isHit = false;
        if (inBounds) {
            vec4 val = texture(voxels, texPos);
            if (val.w > 0.0) {
                // Explicitly Placed Solid Block
                color = vec3(val.x, val.y, 0.0);
                if (val.x == 0.0 && val.y == 0.0) color = vec3(0.2, 0.6, 0.2); // Grass fallback
                if (val.x > 0.0) color = vec3(0.5, 0.5, 0.5); // Stone fallback
                emissive = val.w; 
                isHit = true;
            } else if (val.w == 0.0) {
                // Unmodified block, use procedural FBM
                if (mapPos.y < 20.0) {
                    isHit = true;
                    color = vec3(0.5, 0.5, 0.5);
                    emissive = 0.0;
                } else if (mapPos.y > 51.0) {
                    // Fast path: above terrain bounds, do nothing
                } else {
                    if (mapPos.xz != lastXZ) { lastH = getH(mapPos.xz); lastXZ = mapPos.xz; }
                    if (mapPos.y < lastH) {
                        color = (mapPos.y < lastH - 4.0) ? vec3(0.5, 0.5, 0.5) : vec3(0.18, 0.55, 0.18);
                        emissive = 0.0;
                        isHit = true;
                    }
                }
            }
            // If val.w < 0.0, it is explicitly air, so we continue raymarching.
        } else {
            if (mapPos.y > 51.0) {
                if (rayStep.y >= 0.0) return -1.0; // Early exit: ray is above terrain and going up
            } else if (mapPos.y < 20.0) {
                isHit = true;
                color = vec3(0.5, 0.5, 0.5);
                emissive = 0.0;
            } else {
                if (mapPos.xz != lastXZ) { lastH = getH(mapPos.xz); lastXZ = mapPos.xz; }
                if (mapPos.y < lastH) {
                    color = (mapPos.y < lastH - 4.0) ? vec3(0.5, 0.5, 0.5) : vec3(0.18, 0.55, 0.18);
                    emissive = 0.0;
                    isHit = true;
                }
            }
        }
        
        if (isHit) {
            if (side == 0) { norm = vec3(-rayStep.x, 0, 0); return (sideDist.x - deltaDist.x); }
            if (side == 1) { norm = vec3(0, -rayStep.y, 0); return (sideDist.y - deltaDist.y); }
            norm = vec3(0, 0, -rayStep.z); return (sideDist.z - deltaDist.z);
        }
        
        if (sideDist.x < sideDist.y && sideDist.x < sideDist.z) { sideDist.x += deltaDist.x; mapPos.x += rayStep.x; side = 0; }
        else if (sideDist.y < sideDist.z) { sideDist.y += deltaDist.y; mapPos.y += rayStep.y; side = 1; }
        else { sideDist.z += deltaDist.z; mapPos.z += rayStep.z; side = 2; }
        if (distance(ro, mapPos) > uRenderRadius) break;
    }
    return -1.0;
}

mat3 eulerToMat3(vec3 euler) {
    float c1 = cos(euler.x); float s1 = sin(euler.x);
    float c2 = cos(euler.y); float s2 = sin(euler.y);
    float c3 = cos(euler.z); float s3 = sin(euler.z);
    mat3 rx = mat3(1,0,0, 0,c1,-s1, 0,s1,c1);
    mat3 ry = mat3(c2,0,s2, 0,1,0, -s2,0,c2);
    mat3 rz = mat3(c3,-s3,0, s3,c3,0, 0,0,1);
    return rz * ry * rx;
}

float intersect_entities(vec3 ro, vec3 rd, out vec3 norm, out vec3 color) {
    float hitT = 1e9;
    for(int i = 0; i < uEntityCount; i++) {
        if (i == 0) continue; // Skip rendering the local player
        
        Entity e = entities[i];
        float t = -1.0;
        vec3 n;
        
        if (e.data.x > 1.5) { // Sphere
            vec3 oc = ro - e.pos.xyz;
            float b = dot(oc, rd);
            float c = dot(oc, oc) - e.data.y * e.data.y;
            float h = b*b - c;
            if (h >= 0.0) {
                t = -b - sqrt(h);
                if (t > 0.0) n = normalize((ro + rd * t) - e.pos.xyz);
                else t = -1.0;
            }
        } else { // Cube (OBB via local space AABB)
            vec3 size = vec3(e.data.y);
            if (e.data.x < 0.5) size = vec3(0.6, 1.8, 0.6); // Player
            
            vec3 minP = -size * 0.5;
            vec3 maxP = size * 0.5;
            if (e.data.x < 0.5) { // Player is bottom-center origin
                minP = vec3(-0.3, 0.0, -0.3);
                maxP = vec3(0.3, 1.8, 0.3);
            }
            
            mat3 rot = eulerToMat3(e.friction.yzw); // using data2 for euler
            mat3 invRot = transpose(rot);
            
            vec3 localRo = invRot * (ro - e.pos.xyz);
            vec3 localRd = invRot * rd;
            
            vec3 invR = 1.0 / localRd;
            vec3 t0 = (minP - localRo) * invR;
            vec3 t1 = (maxP - localRo) * invR;
            vec3 tmin = min(t0, t1);
            vec3 tmax = max(t0, t1);
            float t_start = max(max(tmin.x, tmin.y), tmin.z);
            float t_end = min(min(tmax.x, tmax.y), tmax.z);
            if (t_start <= t_end && t_start > 0.0) {
                t = t_start;
                vec3 hp = localRo + localRd * t;
                vec3 dc = abs(hp - (minP + maxP) * 0.5) / ((maxP - minP) * 0.5);
                vec3 localN;
                if (dc.x > dc.y && dc.x > dc.z) localN = vec3(sign(hp.x - (minP.x + maxP.x) * 0.5), 0, 0);
                else if (dc.y > dc.z) localN = vec3(0, sign(hp.y - (minP.y + maxP.y) * 0.5), 0);
                else localN = vec3(0, 0, sign(hp.z - (minP.z + maxP.z) * 0.5));
                n = rot * localN; // Transform normal back to world space
            }
        }
        
        if (t > 0.0 && t < hitT) {
            hitT = t;
            norm = n;
            if (e.data.x < 0.5) color = vec3(1.0, 0.8, 0.4);
            else if (e.data.x < 1.5) color = vec3(0.8, 0.2, 0.2);
            else color = vec3(0.2, 0.2, 0.8);
        }
    }
    return hitT < 1e8 ? hitT : -1.0;
}

void main() {
    ivec2 p = ivec2(gl_GlobalInvocationID.xy); ivec2 sz = imageSize(imgOut);
    if (p.x >= sz.x || p.y >= sz.y) return;
    uint seed = uint(p.x + p.y * sz.x + uFrame * 1234);
    vec3 sunDir = normalize(vec3(0.5, 0.8, 0.3));
    vec3 sunCol = vec3(1.1, 0.9, 0.7);
    vec2 uv = (vec2(p) + uJitter) / vec2(sz) * 2.0 - 1.0; uv.x *= float(sz.x) / float(sz.y);
    vec3 rd = normalize(uCamForward + uCamRight * uv.x * uFovFactor + uCamUp * uv.y * uFovFactor);
    
    vec3 eNorm, eColor;
    float eDist = intersect_entities(uCamPos, rd, eNorm, eColor);
    
    vec3 norm, color; float emissive; float dist = intersect(uCamPos, rd, norm, color, emissive);
    
    if (eDist > 0.0 && (dist < 0.0 || eDist < dist)) {
        dist = eDist; norm = eNorm; color = eColor; emissive = 0.0;
    }
    
    vec3 final;
    
    if (dist > 0.0) {
        vec3 hitPos = uCamPos + rd * dist;
        
        if (uRenderMode == 2) {
            // Normal Mode
            final = norm * 0.5 + 0.5;
        } else if (uRenderMode == 3) {
            // Depth Mode
            float d = clamp(dist / 100.0, 0.0, 1.0);
            final = vec3(1.0 - d);
        } else {
            // Raytraced Mode
            if (uEnableLOD == 1) {
                if (dist < uLodHigh) {
                    vec3 sN, sC; float sE;
                    float sD = intersect(hitPos + norm * 0.01, sunDir, sN, sC, sE);
                    float shad = (sD > 0.0) ? 0.2 : 1.0;
                    vec3 bDir = normalize(norm + randDir(seed));
                    vec3 bN, bC; float bE;
                    float bD = intersect(hitPos + norm * 0.01, bDir, bN, bC, bE);
                    vec3 bounce = (bD > 0.0) ? bC * 0.2 : getSky(bDir, sunDir, sunCol) * 0.1;
                    final = color * (sunCol * max(dot(norm, sunDir), 0.0) * shad + bounce);
                } else if (dist < uLodMed) final = color * (sunCol * max(dot(norm, sunDir), 0.0) + 0.1);
                else final = color * (sunCol * max(dot(norm, sunDir), 0.0) * 0.8 + 0.2);
            } else {
                vec3 sN, sC; float sE;
                float sD = intersect(hitPos + norm * 0.01, sunDir, sN, sC, sE);
                float shad = (sD > 0.0) ? 0.2 : 1.0;
                final = color * (sunCol * max(dot(norm, sunDir), 0.0) * shad + 0.1);
            }
        }
    } else {
        final = getSky(rd, sunDir, sunCol);
    }
    imageStore(imgOut, p, vec4(final, dist));
    
    if (dist > 0.0) {
        imageStore(imgWorldPos, p, vec4(uCamPos + rd * dist, 1.0));
    } else {
        imageStore(imgWorldPos, p, vec4(0.0));
    }
}";

        const string VS = @"#version 460 core
layout(location=0) in vec2 aPos; layout(location=1) in vec2 aUV; out vec2 vUV;
void main() { gl_Position = vec4(aPos, 0.0, 1.0); vUV = aUV; }";

        const string FS = @"#version 460 core
in vec2 vUV; out vec4 fC;
uniform sampler2D uTex;
uniform float uExposure, uContrast, uSaturation, uBrightness, uWhitePoint, uGamma;

vec3 ACESFilm(vec3 x) {
    float a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main() { 
    vec3 c = texture(uTex, vUV).rgb;
    c = c * uExposure + uBrightness;
    c = (c - 0.5) * uContrast + 0.5;
    float luma = dot(c, vec3(0.2126, 0.7152, 0.0722));
    c = mix(vec3(luma), c, uSaturation);
    
    // Tone mapping with White Point
    vec3 curr = ACESFilm(c);
    vec3 white = ACESFilm(vec3(uWhitePoint));
    c = curr / white;
    
    c = pow(max(c, 0.0), vec3(1.0/uGamma));
    fC = vec4(c, 1.0); 
}";

        const string CS_PHYSICS = @"#version 460 core
layout(local_size_x = 64) in;

struct Entity {
    vec4 pos;
    vec4 vel;
    vec4 data; // x=type(0p, 1c, 2s), y=size, z=mass, w=restitution
    vec4 friction; // x=friction, yzw=unused
};

layout(std430, binding = 1) buffer EntityBuffer {
    Entity entities[];
};

uniform float uDt;
uniform uint uSeed;
uniform vec3 uInput;
uniform vec3 uPlayerParams; // x=JumpForce, y=Friction, z=AirResistance
uniform int uEntityCount;

float jingshuo_hash(ivec2 p, uint seed) {
    uint h = uint(p.x) * 73856093U ^ uint(p.y) * 83492791U ^ seed;
    h = (h ^ (h >> 16)) * 0x85ebca6b;
    h = (h ^ (h >> 13)) * 0xc2b2ae35;
    h ^= (h >> 16);
    return float(h) / 4294967295.0;
}

float value_noise(vec2 p, uint seed) {
    ivec2 i = ivec2(floor(p));
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    float a = jingshuo_hash(i + ivec2(0, 0), seed);
    float b = jingshuo_hash(i + ivec2(1, 0), seed);
    float c = jingshuo_hash(i + ivec2(0, 1), seed);
    float d = jingshuo_hash(i + ivec2(1, 1), seed);
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float fbm(vec2 p, uint seed) {
    float v = 0.0;
    float a = 0.5;
    vec2 shift = vec2(100.0);
    for (int i = 0; i < 4; i++) {
        v += a * value_noise(p, seed);
        p = p * 2.0 + shift;
        a *= 0.5;
    }
    return v;
}

bool is_solid(vec3 pos) {
    if (pos.y < 0.0) return true;
    if (pos.y > 256.0) return false;
    float h = 20.0 + fbm(floor(pos.xz) * 0.01, uSeed) * 30.0;
    return floor(pos.y) <= h;
}

bool check_collision(vec3 pos, vec3 size) {
    vec3 minP = pos - vec3(size.x * 0.5, 0.0, size.z * 0.5);
    vec3 maxP = pos + vec3(size.x * 0.5, size.y, size.z * 0.5);
    for(int y = int(floor(minP.y)); y <= int(floor(maxP.y)); y++) {
        for(int x = int(floor(minP.x)); x <= int(floor(maxP.x)); x++) {
            for(int z = int(floor(minP.z)); z <= int(floor(maxP.z)); z++) {
                if(is_solid(vec3(x, y, z))) return true;
            }
        }
    }
    return false;
}

mat3 eulerToMat3(vec3 euler) {
    float c1 = cos(euler.x); float s1 = sin(euler.x);
    float c2 = cos(euler.y); float s2 = sin(euler.y);
    float c3 = cos(euler.z); float s3 = sin(euler.z);
    mat3 rx = mat3(1,0,0, 0,c1,-s1, 0,s1,c1);
    mat3 ry = mat3(c2,0,s2, 0,1,0, -s2,0,c2);
    mat3 rz = mat3(c3,-s3,0, s3,c3,0, 0,0,1);
    return rz * ry * rx;
}

bool check_collision_obb(vec3 pos, vec3 ext, mat3 rot) {
    vec3 c[8];
    c[0] = pos + rot * vec3(-ext.x, -ext.y, -ext.z);
    c[1] = pos + rot * vec3( ext.x, -ext.y, -ext.z);
    c[2] = pos + rot * vec3(-ext.x,  ext.y, -ext.z);
    c[3] = pos + rot * vec3( ext.x,  ext.y, -ext.z);
    c[4] = pos + rot * vec3(-ext.x, -ext.y,  ext.z);
    c[5] = pos + rot * vec3( ext.x, -ext.y,  ext.z);
    c[6] = pos + rot * vec3(-ext.x,  ext.y,  ext.z);
    c[7] = pos + rot * vec3( ext.x,  ext.y,  ext.z);
    for(int i=0; i<8; i++) {
        if(is_solid(c[i])) return true;
    }
    return is_solid(pos);
}

bool check_terrain(Entity e, vec3 pos) {
    if (e.data.x < 0.5) return check_collision(pos, vec3(0.6, 1.8, 0.6));
    if (e.data.x == 1.0) {
        mat3 rot = eulerToMat3(e.friction.yzw);
        return check_collision_obb(pos, vec3(e.data.y * 0.5), rot);
    }
    float r = e.data.y * 0.5;
    if (is_solid(pos)) return true;
    if (is_solid(pos + vec3(0, -r, 0))) return true;
    if (is_solid(pos + vec3(r, 0, 0))) return true;
    if (is_solid(pos + vec3(-r, 0, 0))) return true;
    if (is_solid(pos + vec3(0, 0, r))) return true;
    if (is_solid(pos + vec3(0, 0, -r))) return true;
    return false;
}

vec3 get_closest_point_aabb(vec3 p, vec3 bMin, vec3 bMax) {
    return clamp(p, bMin, bMax);
}

bool testAxis(vec3 axis, vec3 pA, mat3 rotA, vec3 extA, vec3 pB, mat3 rotB, vec3 extB, out float overlap) {
    if (dot(axis, axis) < 1e-6) { overlap = 1e8; return true; }
    axis = normalize(axis);
    float rA = extA.x * abs(dot(rotA[0], axis)) + extA.y * abs(dot(rotA[1], axis)) + extA.z * abs(dot(rotA[2], axis));
    float rB = extB.x * abs(dot(rotB[0], axis)) + extB.y * abs(dot(rotB[1], axis)) + extB.z * abs(dot(rotB[2], axis));
    float d = abs(dot(pB - pA, axis));
    overlap = (rA + rB) - d;
    return overlap > 0.0;
}

void resolve_collision(inout Entity a, Entity b) {
    // Broad Phase
    float d = distance(a.pos.xyz, b.pos.xyz);
    float rA = (a.data.x < 1.5) ? length(vec3(a.data.y)) * 0.5 : a.data.y;
    float rB = (b.data.x < 1.5) ? length(vec3(b.data.y)) * 0.5 : b.data.y;
    if (d > rA + rB + 1.0) return;
    
    vec3 n = vec3(0, 1, 0);
    float overlap = 0.0;
    bool colliding = false;
    
    if (a.data.x == 1.0 && b.data.x == 1.0) {
        // OBB - OBB (SAT)
        mat3 rotA = eulerToMat3(a.friction.yzw);
        mat3 rotB = eulerToMat3(b.friction.yzw);
        vec3 extA = vec3(a.data.y * 0.5);
        vec3 extB = vec3(b.data.y * 0.5);
        
        vec3 axes[15];
        axes[0] = rotA[0]; axes[1] = rotA[1]; axes[2] = rotA[2];
        axes[3] = rotB[0]; axes[4] = rotB[1]; axes[5] = rotB[2];
        axes[6] = cross(rotA[0], rotB[0]); axes[7] = cross(rotA[0], rotB[1]); axes[8] = cross(rotA[0], rotB[2]);
        axes[9] = cross(rotA[1], rotB[0]); axes[10] = cross(rotA[1], rotB[1]); axes[11] = cross(rotA[1], rotB[2]);
        axes[12] = cross(rotA[2], rotB[0]); axes[13] = cross(rotA[2], rotB[1]); axes[14] = cross(rotA[2], rotB[2]);
        
        float minOverlap = 1e8;
        vec3 minAxis = vec3(0, 1, 0);
        colliding = true;
        for (int i=0; i<15; i++) {
            float o;
            if (!testAxis(axes[i], a.pos.xyz, rotA, extA, b.pos.xyz, rotB, extB, o)) {
                colliding = false; break;
            }
            if (o < minOverlap) { minOverlap = o; minAxis = axes[i]; }
        }
        if (colliding) { overlap = minOverlap; n = normalize(minAxis); }
    } else {
        // Sphere-Sphere fallback
        overlap = (a.data.y + b.data.y) * 0.5 - d;
        if (overlap > 0.0) { colliding = true; n = (d < 0.01) ? vec3(0, 1, 0) : normalize(a.pos.xyz - b.pos.xyz); }
    }
    
    if (colliding) {
        if (dot(b.pos.xyz - a.pos.xyz, n) < 0.0) n = -n; // n points from A to B
        
        float mA = a.data.z; float mB = b.data.z;
        float invMassSum = 1.0/mA + 1.0/mB;
        float percent = 0.5; float slop = 0.01;
        vec3 correction = max(overlap - slop, 0.0) / invMassSum * percent * n;
        a.pos.xyz -= correction / mA; 
        
        vec3 relVel = a.vel.xyz - b.vel.xyz;
        float velAlongNormal = dot(relVel, n);
        if (velAlongNormal < 0) { 
            float uA = dot(a.vel.xyz, n); float uB = dot(b.vel.xyz, n);
            float restitution = (a.data.w + b.data.w) * 0.5;
            float j = -(1.0 + restitution) * velAlongNormal / invMassSum;
            vec3 impulse = j * n;
            a.vel.xyz += impulse / mA;
            
            // Friction
            vec3 tangent = relVel - dot(relVel, n) * n;
            if (length(tangent) > 0.01) {
                tangent = normalize(tangent);
                float f = (a.friction.x + b.friction.x) * 0.5;
                float jt = -dot(relVel, tangent) / invMassSum;
                jt = clamp(jt, -j * f, j * f);
                a.vel.xyz += (jt * tangent) / mA;
            }
        }
    }
}

void main() {
    uint id = gl_GlobalInvocationID.x;
    if (id >= uEntityCount) return;
    
    Entity e = entities[id];
    if (e.data.x > 2.5 || e.data.x < 0.5) return; // Static or Player: skip physics update
    
    // 1. Gravity & Input
    if (e.data.x < 0.5) { // Player
        e.vel.xz += uInput.xz * uDt * 40.0;
        if (uInput.y > 0.5 && e.pos.w > 0.5) e.vel.y = uPlayerParams.x;
    }
    e.vel.y -= 32.0 * uDt; // Gravity
    
    // 2. World Collision Sweep
    bool grounded = false;
    vec3 step;
    
    // Y Axis Sweep
    step = vec3(0, e.vel.y * uDt, 0);
    if (check_terrain(e, e.pos.xyz + step)) {
        float t0 = 0.0; float t1 = 1.0;
        for(int i=0; i<8; i++) {
            float mid = (t0 + t1) * 0.5;
            if(check_terrain(e, e.pos.xyz + step * mid)) t1 = mid;
            else t0 = mid;
        }
        e.pos.xyz += step * t0;
        e.pos.y -= sign(step.y) * 0.001;
        
        vec3 n = vec3(0, sign(step.y), 0);
        float velAlongNormal = dot(e.vel.xyz, n);
        e.vel.xyz -= (1.0 + e.data.w) * velAlongNormal * n;
        
        // Friction
        vec3 tangent = e.vel.xyz - dot(e.vel.xyz, n) * n;
        float f = (e.data.x < 0.5) ? uPlayerParams.y : e.friction.x;
        e.vel.xyz -= tangent * f * uDt * 10.0;
        
        if (step.y < 0.0) grounded = true;
    } else {
        e.pos.xyz += step;
    }
    
    // X Axis Sweep
    step = vec3(e.vel.x * uDt, 0, 0);
    if (check_terrain(e, e.pos.xyz + step)) {
        float t0 = 0.0; float t1 = 1.0;
        for(int i=0; i<8; i++) {
            float mid = (t0 + t1) * 0.5;
            if(check_terrain(e, e.pos.xyz + step * mid)) t1 = mid;
            else t0 = mid;
        }
        e.pos.xyz += step * t0;
        e.pos.x -= sign(step.x) * 0.001;
        
        vec3 n = vec3(sign(step.x), 0, 0);
        float velAlongNormal = dot(e.vel.xyz, n);
        e.vel.xyz -= (1.0 + e.data.w) * velAlongNormal * n;
        
        vec3 tangent = e.vel.xyz - dot(e.vel.xyz, n) * n;
        float f = (e.data.x < 0.5) ? uPlayerParams.y : e.friction.x;
        e.vel.xyz -= tangent * f * uDt * 10.0;
    } else {
        e.pos.xyz += step;
    }
    
    // Z Axis Sweep
    step = vec3(0, 0, e.vel.z * uDt);
    if (check_terrain(e, e.pos.xyz + step)) {
        float t0 = 0.0; float t1 = 1.0;
        for(int i=0; i<8; i++) {
            float mid = (t0 + t1) * 0.5;
            if(check_terrain(e, e.pos.xyz + step * mid)) t1 = mid;
            else t0 = mid;
        }
        e.pos.xyz += step * t0;
        e.pos.z -= sign(step.z) * 0.001;
        
        vec3 n = vec3(0, 0, sign(step.z));
        float velAlongNormal = dot(e.vel.xyz, n);
        e.vel.xyz -= (1.0 + e.data.w) * velAlongNormal * n;
        
        vec3 tangent = e.vel.xyz - dot(e.vel.xyz, n) * n;
        float f = (e.data.x < 0.5) ? uPlayerParams.y : e.friction.x;
        e.vel.xyz -= tangent * f * uDt * 10.0;
    } else {
        e.pos.xyz += step;
    }
    
    // 3. Universal Damping
    if (e.data.x < 0.5) { // Player
        if (!grounded) e.vel.xyz *= pow(uPlayerParams.z, uDt * 20.0);
    } else {
        e.vel.xyz *= pow(0.95, uDt * 20.0); 
        if (!grounded) {
            e.friction.yzw += vec3(1.0, 1.5, 0.5) * uDt * clamp(length(e.vel.xyz), 0.0, 3.0);
        }
    }
    
    e.vel.xyz = clamp(e.vel.xyz, vec3(-100.0), vec3(100.0));
    e.pos.w = grounded ? 1.0 : 0.0;
    
    // 4. Entity-Entity Collision
    for (int i = 0; i < uEntityCount; i++) {
        if (uint(i) == id) continue;
        resolve_collision(e, entities[i]);
    }
    
    entities[id] = e;
}
";

        const string CS_TAA = @"#version 460 core
layout(local_size_x = 8, local_size_y = 8) in;
layout(rgba32f, binding = 0) uniform readonly image2D imgCurrent;
layout(rgba32f, binding = 1) uniform readonly image2D imgWorldPos;
layout(rgba32f, binding = 2) uniform readonly image2D imgHistory;
layout(rgba32f, binding = 3) uniform writeonly image2D imgResolved;

uniform vec3 uPrevCamPos, uPrevCamForward, uPrevCamRight, uPrevCamUp;
uniform float uFovFactor;

void main() {
    ivec2 p = ivec2(gl_GlobalInvocationID.xy);
    ivec2 sz = imageSize(imgCurrent);
    if (p.x >= sz.x || p.y >= sz.y) return;

    vec4 current = imageLoad(imgCurrent, p);
    vec4 worldPos = imageLoad(imgWorldPos, p);

    if (worldPos.w < 0.5) {
        // Sky: no reprojection for now
        imageStore(imgResolved, p, current);
        return;
    }

    vec3 prevDir = normalize(worldPos.xyz - uPrevCamPos);
    float z = dot(prevDir, uPrevCamForward);
    float x = dot(prevDir, uPrevCamRight);
    float y = dot(prevDir, uPrevCamUp);

    vec2 prevUV = vec2(x, y) / (z * uFovFactor);
    prevUV.x /= float(sz.x) / float(sz.y);
    
    vec2 prevP = (prevUV * 0.5 + 0.5) * vec2(sz);
    ivec2 prevP_i = ivec2(round(prevP));

    if (prevP_i.x < 0 || prevP_i.x >= sz.x || prevP_i.y < 0 || prevP_i.y >= sz.y) {
        imageStore(imgResolved, p, current);
        return;
    }

    vec4 history = imageLoad(imgHistory, prevP_i);

    // 3x3 Neighborhood Clamping
    vec4 colorMin = current;
    vec4 colorMax = current;
    for(int dy = -1; dy <= 1; ++dy) {
        for(int dx = -1; dx <= 1; ++dx) {
            if(dx==0 && dy==0) continue;
            ivec2 np = clamp(p + ivec2(dx, dy), ivec2(0), sz - ivec2(1));
            vec4 neighbor = imageLoad(imgCurrent, np);
            colorMin = min(colorMin, neighbor);
            colorMax = max(colorMax, neighbor);
        }
    }
    history = clamp(history, colorMin, colorMax);

    vec4 resolved = mix(history, current, 0.1);
    imageStore(imgResolved, p, resolved);
}
";
    }
}
