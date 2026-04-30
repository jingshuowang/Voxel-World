using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using Silk.NET.OpenGL.Extensions.ImGui;
using System;
using System.Numerics;
using System.Collections.Generic;
using StbImageSharp;

namespace Voxel.Experimental {
    public class Entity {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Radius = 0.5f;
        public Vector3 Color = new Vector3(0.2f, 0.6f, 1.0f);
        public Vector3 OriginalColor;
        public bool IsColliding = false;
        public float Mass => Radius * Radius * Radius;
        public float InvMass => 1.0f / Mass;
    }

    public class Experiment {
        static IWindow? w;
        static GL? gl;
        static IKeyboard? kb;
        static IMouse? mouse;
        static ImGuiController? imgui;
        static uint vao, vbo, ebo;
        static int indexCount;
        static uint cubeVao, cubeVbo;
        static uint shader;
        static int uModel, uView, uProj, uColor, uLightDir, uUseTex, uMainTex;
        static uint mainTex;
        
        static Vector3 playerPos = new Vector3(0, 5, 25);
        static Vector3 playerVel = Vector3.Zero;
        static Vector3 playerAcc = Vector3.Zero;
        static Vector3 inputForceVec = Vector3.Zero;
        static Vector3 frictionForceVec = Vector3.Zero;
        
        static float camYaw = -90.0f;
        static float camPitch = -15.0f;
        static Vector2 lastMouse;
        static bool firstMouse = true;
        
        static List<Entity> entities = new List<Entity>();
        static float shootCooldown = 0;
        
        public static void ExperimentMain() {
            var options = WindowOptions.Default;
            options.Size = new Silk.NET.Maths.Vector2D<int>(1280, 720);
            options.Title = "Bitzel Experiment Sandbox (Entity Physics)";
            options.VSync = true;
            w = Window.Create(options);
            w.Load += OnLoad; w.Update += OnUpdate; w.Render += OnRender; w.FramebufferResize += s => gl?.Viewport(s);
            w.Run();
        }
        
        static unsafe void OnLoad() {
            gl = w!.CreateOpenGL();
            var input = w.CreateInput();
            imgui = new ImGuiController(gl, w, input);
            kb = input.Keyboards[0];
            mouse = input.Mice[0];
            mouse.Cursor.CursorMode = CursorMode.Raw;
            
            kb.KeyDown += (k, key, arg3) => {
                if (key == Key.Escape) w.Close();
                if (key == Key.F11) w.WindowState = w.WindowState == WindowState.Fullscreen ? WindowState.Normal : WindowState.Fullscreen;
            };

            gl.Enable(EnableCap.DepthTest);
            
            List<float> verts = new List<float>(); List<uint> indices = new List<uint>();
            int rings = 24; int sectors = 24;
            for(int r = 0; r < rings; r++) for(int s = 0; s < sectors; s++) {
                float u = (float)s / (sectors - 1); float v = (float)r / (rings - 1);
                float y = MathF.Sin(-MathF.PI / 2 + MathF.PI * v);
                float x = MathF.Cos(2 * MathF.PI * u) * MathF.Sin(MathF.PI * v);
                float z = MathF.Sin(2 * MathF.PI * u) * MathF.Sin(MathF.PI * v);
                verts.Add(x); verts.Add(y); verts.Add(z); verts.Add(x); verts.Add(y); verts.Add(z); verts.Add(u); verts.Add(v);
            }
            for(int r = 0; r < rings - 1; r++) for(int s = 0; s < sectors - 1; s++) {
                indices.Add((uint)(r * sectors + s)); indices.Add((uint)(r * sectors + (s + 1))); indices.Add((uint)((r + 1) * sectors + (s + 1)));
                indices.Add((uint)((r + 1) * sectors + (s + 1))); indices.Add((uint)((r + 1) * sectors + s)); indices.Add((uint)(r * sectors + s));
            }
            indexCount = indices.Count;
            vao = gl.GenVertexArray(); vbo = gl.GenBuffer(); ebo = gl.GenBuffer();
            gl.BindVertexArray(vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            fixed(float* v = verts.ToArray()) gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Count * sizeof(float)), v, BufferUsageARB.StaticDraw);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
            fixed(uint* i = indices.ToArray()) gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Count * sizeof(uint)), i, BufferUsageARB.StaticDraw);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)0); gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(3 * sizeof(float))); gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(6 * sizeof(float))); gl.EnableVertexAttribArray(2);

            float[] cubeVerts = {
                -0.5f,-0.5f,-0.5f, 0,0,-1, 0,0,  0.5f,-0.5f,-0.5f, 0,0,-1, 1,0,  0.5f, 0.5f,-0.5f, 0,0,-1, 1,1,  0.5f, 0.5f,-0.5f, 0,0,-1, 1,1, -0.5f, 0.5f,-0.5f, 0,0,-1, 0,1, -0.5f,-0.5f,-0.5f, 0,0,-1, 0,0,
                -0.5f,-0.5f, 0.5f, 0,0, 1, 0,0,  0.5f,-0.5f, 0.5f, 0,0, 1, 1,0,  0.5f, 0.5f, 0.5f, 0,0, 1, 1,1,  0.5f, 0.5f, 0.5f, 0,0, 1, 1,1, -0.5f, 0.5f, 0.5f, 0,0, 1, 0,1, -0.5f,-0.5f, 0.5f, 0,0, 1, 0,0,
                -0.5f, 0.5f, 0.5f,-1,0, 0, 0,0, -0.5f, 0.5f,-0.5f,-1,0, 0, 1,0, -0.5f,-0.5f,-0.5f,-1,0, 0, 1,1, -0.5f,-0.5f,-0.5f,-1,0, 0, 1,1, -0.5f,-0.5f, 0.5f,-1,0, 0, 0,1, -0.5f, 0.5f, 0.5f,-1,0, 0, 0,0,
                 0.5f, 0.5f, 0.5f, 1,0, 0, 0,0,  0.5f, 0.5f,-0.5f, 1,0, 0, 1,0,  0.5f,-0.5f,-0.5f, 1,0, 0, 1,1,  0.5f,-0.5f,-0.5f, 1,0, 0, 1,1,  0.5f,-0.5f, 0.5f, 1,0, 0, 0,1,  0.5f, 0.5f, 0.5f, 1,0, 0, 0,0,
                -0.5f,-0.5f,-0.5f, 0,-1,0, 0,0,  0.5f,-0.5f,-0.5f, 0,-1,0, 1,0,  0.5f,-0.5f, 0.5f, 0,-1,0, 1,1,  0.5f,-0.5f, 0.5f, 0,-1,0, 1,1, -0.5f,-0.5f, 0.5f, 0,-1,0, 0,1, -0.5f,-0.5f,-0.5f, 0,-1,0, 0,0,
                -0.5f, 0.5f,-0.5f, 0, 1,0, 0,0,  0.5f, 0.5f,-0.5f, 0, 1,0, 1,0,  0.5f, 0.5f, 0.5f, 0, 1,0, 1,1,  0.5f, 0.5f, 0.5f, 0, 1,0, 1,1, -0.5f, 0.5f, 0.5f, 0, 1,0, 0,1, -0.5f, 0.5f,-0.5f, 0, 1,0, 0,0
            };
            cubeVao = gl.GenVertexArray(); cubeVbo = gl.GenBuffer();
            gl.BindVertexArray(cubeVao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, cubeVbo);
            fixed(float* v = cubeVerts) gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(cubeVerts.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)0); gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(3 * sizeof(float))); gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(6 * sizeof(float))); gl.EnableVertexAttribArray(2);

            mainTex = LoadTexture(ExpConfig.CurrentTexture);

            string vs = @"#version 330 core
layout (location = 0) in vec3 aPos; layout (location = 1) in vec3 aNormal; layout (location = 2) in vec2 aTexCoord;
uniform mat4 uModel; uniform mat4 uView; uniform mat4 uProj;
out vec3 FragPos; out vec3 Normal; out vec2 TexCoord;
void main() { FragPos = vec3(uModel * vec4(aPos, 1.0)); Normal = mat3(transpose(inverse(uModel))) * aNormal; TexCoord = aTexCoord; gl_Position = uProj * uView * vec4(FragPos, 1.0); }";
            string fs = @"#version 330 core
in vec3 FragPos; in vec3 Normal; in vec2 TexCoord; out vec4 FragColor;
uniform vec3 uColor; uniform vec3 uLightDir; uniform bool uUseTex; uniform sampler2D uMainTex;
void main() { 
    vec3 norm = normalize(Normal); vec3 lightDir = normalize(-uLightDir); float diff = max(dot(norm, lightDir), 0.0); 
    vec3 base = uColor;
    if (uUseTex) base *= texture(uMainTex, TexCoord).rgb;
    FragColor = vec4((vec3(0.15) + diff) * base, 1.0); 
}";
            uint vS = gl.CreateShader(ShaderType.VertexShader); gl.ShaderSource(vS, vs); gl.CompileShader(vS);
            uint fS = gl.CreateShader(ShaderType.FragmentShader); gl.ShaderSource(fS, fs); gl.CompileShader(fS);
            shader = gl.CreateProgram(); gl.AttachShader(shader, vS); gl.AttachShader(shader, fS); gl.LinkProgram(shader);
            uModel = gl.GetUniformLocation(shader, "uModel"); uView = gl.GetUniformLocation(shader, "uView"); uProj = gl.GetUniformLocation(shader, "uProj"); uColor = gl.GetUniformLocation(shader, "uColor"); uLightDir = gl.GetUniformLocation(shader, "uLightDir");
            uUseTex = gl.GetUniformLocation(shader, "uUseTex"); uMainTex = gl.GetUniformLocation(shader, "uMainTex");
        }

        static unsafe uint LoadTexture(string path) {
            if (!global::System.IO.File.Exists(path)) return 0;
            using var stream = global::System.IO.File.OpenRead(path);
            var img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            uint tex = gl!.GenTexture(); gl.BindTexture(TextureTarget.Texture2D, tex);
            fixed(byte* p = img.Data) gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba, (uint)img.Width, (uint)img.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Linear);
            gl.GenerateMipmap(TextureTarget.Texture2D);
            return tex;
        }

        static Vector3 GetForward() {
            float y = camYaw * MathF.PI / 180.0f; float p = camPitch * MathF.PI / 180.0f;
            return Vector3.Normalize(new Vector3(MathF.Cos(y) * MathF.Cos(p), MathF.Sin(p), MathF.Sin(y) * MathF.Cos(p)));
        }

        static void OnUpdate(double dt) {
            float fdt = (float)dt;
            var forward = GetForward(); var horizontalForward = Vector3.Normalize(new Vector3(forward.X, 0, forward.Z));
            var right = Vector3.Normalize(Vector3.Cross(horizontalForward, Vector3.UnitY));
            var up = Vector3.Normalize(Vector3.Cross(right, forward));
            
            // Calculate Forces separately for GUI representation
            inputForceVec = Vector3.Zero;
            if (kb!.IsKeyPressed(Key.W)) inputForceVec += horizontalForward * ExpConfig.InputForce;
            if (kb.IsKeyPressed(Key.S)) inputForceVec -= horizontalForward * ExpConfig.InputForce;
            if (kb.IsKeyPressed(Key.D)) inputForceVec += right * ExpConfig.InputForce;
            if (kb.IsKeyPressed(Key.A)) inputForceVec -= right * ExpConfig.InputForce;
            if (kb.IsKeyPressed(Key.Space)) inputForceVec += Vector3.UnitY * ExpConfig.InputForce;
            if (kb.IsKeyPressed(Key.ShiftLeft)) inputForceVec -= Vector3.UnitY * ExpConfig.InputForce;
            
            frictionForceVec = -playerVel * ExpConfig.FrictionK;
            playerAcc = inputForceVec + frictionForceVec;
            
            playerVel += playerAcc * fdt;
            if (playerVel.Length() < 0.01f) playerVel = Vector3.Zero; // Precision cutoff
            playerPos += playerVel * fdt;

            var mPos = mouse!.Position; if (firstMouse) { lastMouse = mPos; firstMouse = false; }
            camYaw += (mPos.X - lastMouse.X) * 0.15f; camPitch -= (mPos.Y - lastMouse.Y) * 0.15f; camPitch = Math.Clamp(camPitch, -89f, 89f); lastMouse = mPos;

            shootCooldown -= fdt;
            if (kb.IsKeyPressed(Key.Q) && shootCooldown <= 0) {
                shootCooldown = 0.25f;
                for (int i = 0; i < 100; i++) {
                    float b = 0.3f + (float)Random.Shared.NextDouble() * 0.7f;
                    var c = new Vector3(0.1f * b, b, 0.2f * b);
                    float r = (0.3f + (float)Random.Shared.NextDouble() * 1.0f) / 4.0f;
                    Vector3 d = Vector3.Normalize(forward + right * ((float)Random.Shared.NextDouble() * 2 - 1) * 0.25f + up * ((float)Random.Shared.NextDouble() * 2 - 1) * 0.25f);
                    entities.Add(new Entity { Position = playerPos + forward * 2.0f, Velocity = playerVel + d * (20.0f + (float)Random.Shared.NextDouble() * 15.0f), Radius = r, Color = c, OriginalColor = c });
                }
            }

            foreach (var s in entities) {
                s.IsColliding = false; s.Velocity.Y -= 20.0f * fdt; s.Position += s.Velocity * fdt;
                float b = ExpConfig.Bounciness;
                if (s.Position.Y - s.Radius < 0) { s.Position.Y = s.Radius; s.Velocity.Y *= -b; s.Velocity.X *= 0.95f; s.Velocity.Z *= 0.95f; }
                if (s.Position.Y + s.Radius > 30) { s.Position.Y = 30 - s.Radius; s.Velocity.Y *= -b; }
                if (s.Position.X - s.Radius < -30) { s.Position.X = -30 + s.Radius; s.Velocity.X *= -b; }
                if (s.Position.X + s.Radius > 30) { s.Position.X = 30 - s.Radius; s.Velocity.X *= -b; }
                if (s.Position.Z - s.Radius < -30) { s.Position.Z = -30 + s.Radius; s.Velocity.Z *= -b; }
                if (s.Position.Z + s.Radius > 30) { s.Position.Z = 30 - s.Radius; s.Velocity.Z *= -b; }
            }

            for (int i = 0; i < entities.Count; i++) {
                for (int j = i + 1; j < entities.Count; j++) {
                    var a = entities[i]; var b = entities[j];
                    Vector3 diff = a.Position - b.Position; float dist = diff.Length(); float sumR = a.Radius + b.Radius;
                    if (dist < sumR) {
                        a.IsColliding = true; b.IsColliding = true; Vector3 n = Vector3.Normalize(diff); float overlap = sumR - dist;
                        a.Position += n * overlap * 0.5f; b.Position -= n * overlap * 0.5f;
                        Vector3 relVel = a.Velocity - b.Velocity; float vNormal = Vector3.Dot(relVel, n);
                        if (vNormal < 0) { float impulse = -(1.0f + ExpConfig.Bounciness) * vNormal / (a.InvMass + b.InvMass); a.Velocity += impulse * n * a.InvMass; b.Velocity -= impulse * n * b.InvMass; }
                    }
                }
            }
            foreach (var s in entities) s.Color = s.IsColliding ? new Vector3(1, 0, 0) : s.OriginalColor;
            if (entities.Count > 1000) entities.RemoveRange(0, 100);
            imgui?.Update(fdt);
        }

        static unsafe void OnRender(double dt) {
            gl!.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
            gl.ClearColor(0.05f, 0.05f, 0.1f, 1.0f); gl.UseProgram(shader);
            Matrix4x4 view = Matrix4x4.CreateLookAt(playerPos, playerPos + GetForward(), Vector3.UnitY);
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, 1280f / 720f, 0.1f, 1000f);
            gl.UniformMatrix4(uView, 1, false, (float*)&view); gl.UniformMatrix4(uProj, 1, false, (float*)&proj);
            gl.Uniform3(uLightDir, -0.5f, -1.0f, -0.5f);
            gl.Uniform1(uUseTex, ExpConfig.UseTexture ? 1 : 0);
            gl.ActiveTexture(TextureUnit.Texture0); gl.BindTexture(TextureTarget.Texture2D, mainTex);
            gl.Uniform1(uMainTex, 0);
            
            gl.BindVertexArray(cubeVao);
            gl.Uniform3(uColor, 0.2f, 0.2f, 0.25f);
            Matrix4x4 mFloor = Matrix4x4.CreateScale(new Vector3(60, 2, 60)) * Matrix4x4.CreateTranslation(new Vector3(0, -1, 0));
            gl.UniformMatrix4(uModel, 1, false, (float*)&mFloor); gl.DrawArrays(PrimitiveType.Triangles, 0, 36);
            gl.Uniform3(uColor, 0.15f, 0.15f, 0.2f);
            Matrix4x4 mCeil = Matrix4x4.CreateScale(new Vector3(60, 2, 60)) * Matrix4x4.CreateTranslation(new Vector3(0, 31, 0));
            gl.UniformMatrix4(uModel, 1, false, (float*)&mCeil); gl.DrawArrays(PrimitiveType.Triangles, 0, 36);
            gl.Uniform3(uColor, 0.1f, 0.1f, 0.15f);
            Matrix4x4 mBack = Matrix4x4.CreateScale(new Vector3(60, 30, 2)) * Matrix4x4.CreateTranslation(new Vector3(0, 15, -31));
            gl.UniformMatrix4(uModel, 1, false, (float*)&mBack); gl.DrawArrays(PrimitiveType.Triangles, 0, 36);
            Matrix4x4 mFront = Matrix4x4.CreateScale(new Vector3(60, 30, 2)) * Matrix4x4.CreateTranslation(new Vector3(0, 15, 31));
            gl.UniformMatrix4(uModel, 1, false, (float*)&mFront); gl.DrawArrays(PrimitiveType.Triangles, 0, 36);
            gl.Uniform3(uColor, 0.08f, 0.08f, 0.12f);
            Matrix4x4 mLeft = Matrix4x4.CreateScale(new Vector3(2, 30, 60)) * Matrix4x4.CreateTranslation(new Vector3(-31, 15, 0));
            gl.UniformMatrix4(uModel, 1, false, (float*)&mLeft); gl.DrawArrays(PrimitiveType.Triangles, 0, 36);
            Matrix4x4 mRight = Matrix4x4.CreateScale(new Vector3(2, 30, 60)) * Matrix4x4.CreateTranslation(new Vector3(31, 15, 0));
            gl.UniformMatrix4(uModel, 1, false, (float*)&mRight); gl.DrawArrays(PrimitiveType.Triangles, 0, 36);
            
            gl.BindVertexArray(vao);
            foreach (var s in entities) {
                Matrix4x4 m = Matrix4x4.CreateScale(s.Radius) * Matrix4x4.CreateTranslation(s.Position);
                gl.UniformMatrix4(uModel, 1, false, (float*)&m); gl.Uniform3(uColor, s.Color.X, s.Color.Y, s.Color.Z);
                gl.DrawElements(PrimitiveType.Triangles, (uint)indexCount, DrawElementsType.UnsignedInt, (void*)0);
            }
            
            ImGuiNET.ImGui.Begin("ExpConfig & Stats");
            ImGuiNET.ImGui.Checkbox("Use Textures", ref ExpConfig.UseTexture);
            ImGuiNET.ImGui.SliderFloat("Friction K", ref ExpConfig.FrictionK, 0f, 20f);
            ImGuiNET.ImGui.SliderFloat("Input Force", ref ExpConfig.InputForce, 0f, 500f);
            ImGuiNET.ImGui.SliderFloat("Bounciness", ref ExpConfig.Bounciness, 0f, 1.0f);
            ImGuiNET.ImGui.Separator();
            ImGuiNET.ImGui.Text($"Entities: {entities.Count}");
            ImGuiNET.ImGui.Text($"Pos: {playerPos:F2}");
            ImGuiNET.ImGui.Text($"Vel: {playerVel:F2}");
            ImGuiNET.ImGui.Text($"Acc (Net): {playerAcc:F2}");
            ImGuiNET.ImGui.Text($"  - Input Force: {inputForceVec:F2}");
            ImGuiNET.ImGui.Text($"  - Friction Force: {frictionForceVec:F2}");
            ImGuiNET.ImGui.End();
            imgui?.Render();
        }
    }
}
