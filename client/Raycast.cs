using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using Silk.NET.OpenGL.Extensions.ImGui;
using ImGuiNET;
using System.Numerics;

namespace Voxel.Rendering {
    public static unsafe class RaycastExperiment {
        static IWindow?          w;
        static GL?               gl;
        static IKeyboard?        kb;
        static IMouse?           mouse;
        static ImGuiController?  imgui;
        static Detection         camera => Voxel.Client.ClientLauncher.Camera;

        static uint progRay, progVol, progBloom, progTAA, progDisplay;

        static uint texColor, texPos, texEmissive;
        static uint texBloom;
        static uint texHistory, texResolved;

        static uint quadVao, quadVbo;
        static int W = 1280, H = 720;
        static int  frame;
        static double dt_last = 0.016;

        static float   time  = 0f;
        static Vector3 prevPos, prevFwd, prevRgt, prevUp;
        static bool showF3    = false;
        static bool showF4    = false;
        static int  _paramSel = 0;
        
        // Visual Line Profiler Buffers
        static float[] histClient = new float[128];
        static float[] histServer = new float[128];
        static float[] histNet    = new float[128];
        static int histIdx = 0;

        // Static lists to prevent per-frame allocations (GC pressure/leak)
        static readonly global::System.Collections.Generic.List<(string Name, float Alpha)> _activeKeysBuffer = new();
        static readonly global::System.Collections.Generic.List<string> _systemKeysBuffer = new();
        static readonly global::System.Collections.Generic.List<string> _sortedKeysBuffer = new();

        // Keystroke overlay fade factors
        static float fadeW = 0f, fadeA = 0f, fadeS = 0f, fadeD = 0f, fadeSpace = 0f, fadeShift = 0f, fadeCtrl = 0f, fadeQ = 0f, fadeE = 0f, fadeTab = 0f, fadeAlt = 0f;

        // Mouse arrow delta variables
        static Vector2 prevMouseFrame = Vector2.Zero;
        static bool firstMouseFrame = true;
        static Vector2 mouseDeltaDecay = Vector2.Zero;

        static uint _p;

        // F3 parameter list -- arrow up/down selects, left/right changes value.
        readonly record struct Param(string Name, Func<float> Get, Action<float> Set,
                                     float Step, float Min, float Max);
        static readonly Param[] _params = [
            new("RenderDistance",  () => Config.RenderDistance,             v => Config.RenderDistance = (int)MathF.Round(v), 256,      64,      65536),
            new("LodThreshold",    () => Config.LodThreshold,               v => Config.LodThreshold = v,                     32,       32,      2048),
            new("FogStart",        () => Config.FogStart,                   v => Config.FogStart = v,                         16,       0,       512),
            new("SunBrightness",   () => Config.SunBrightness,              v => Config.SunBrightness = v,                    0.5f,     0.5f,    20),
            new("BloomIntensity",  () => Config.BloomIntensity,             v => Config.BloomIntensity = v,                   0.05f,    0,       5),
            new("AmbientBase",     () => Config.AmbientBase,                v => Config.AmbientBase = v,                      0.01f,    0,       0.5f),
            new("AmbientSlope",    () => Config.AmbientSlope,               v => Config.AmbientSlope = v,                     0.01f,    0,       0.3f),
            new("CloudAltitude",   () => Config.CloudAltitude,              v => Config.CloudAltitude = v,                    200,      0,       20000),
            new("CloudCover",      () => Config.CloudCover,                 v => Config.CloudCover = v,                       0.05f,    0,       1),
            new("CloudScale",      () => Config.CloudScale,                 v => Config.CloudScale = v,                       0.00005f, 0.00001f,0.005f),
            new("Exposure",        () => Config.Exposure,                   v => Config.Exposure = v,                         0.1f,     0.1f,    5),
            new("Saturation",      () => Config.Saturation,                 v => Config.Saturation = v,                       0.1f,     0,       3),
            new("Gamma",           () => Config.Gamma,                      v => Config.Gamma = v,                            0.1f,     0.5f,    4),
            new("EnableFog",       () => Config.EnableFog     ? 1 : 0, v => Config.EnableFog     = v > 0.5f, 1, 0, 1),
        ];

        public static void RaycastMain() {
            var o = WindowOptions.Default;
            o.Size  = new(W, H);
            o.Title = "Voxel -- WASD move, Shift sprint, F3 stats, ESC quit";
            o.VSync = false; // OFF — VSync was causing 3-frame 50ms stalls at SwapBuffers
            o.API   = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core,
                                      ContextFlags.Default, new APIVersion(4, 6));
            w = Window.Create(o);
            w.Load   += Load;
            w.Update += Update;
            w.Render += RenderFrame;
            w.Resize += OnResize;
            w.Run();
        }

        static void OnResize(Silk.NET.Maths.Vector2D<int> size) {
            if (size.X <= 0 || size.Y <= 0) return;
            W = size.X; H = size.Y;

            if (gl == null) return;

            // Delete old textures to avoid GPU memory leaks
            gl.DeleteTexture(texColor);
            gl.DeleteTexture(texPos);
            gl.DeleteTexture(texEmissive);
            gl.DeleteTexture(texBloom);
            gl.DeleteTexture(texHistory);
            gl.DeleteTexture(texResolved);

            // Recreate render targets with new window dimensions
            texColor    = MakeTex(W, H);
            texPos      = MakeTex(W, H);
            texEmissive = MakeTex(W, H);
            texBloom    = MakeTex(W, H);
            texHistory  = MakeTex(W, H);
            texResolved = MakeTex(W, H);
        }

        static void Draw3DLine(Vector3 A, Vector3 B, uint color, float thickness) {
            var drawList = ImGui.GetForegroundDrawList();
            Vector3 camPos = Voxel.Client.ClientLauncher.PredictedPosition;
            Vector3 toA = A - camPos;
            Vector3 toB = B - camPos;

            float zA = Vector3.Dot(toA, camera.Forward);
            float zB = Vector3.Dot(toB, camera.Forward);

            const float zNear = 0.1f;

            // If both points are behind the near plane, don't draw.
            if (zA < zNear && zB < zNear) return;

            // Clip A if it's behind the near plane
            if (zA < zNear) {
                float t = (zNear - zA) / (zB - zA);
                A = A + t * (B - A);
                toA = A - camPos;
                zA = zNear;
            }
            // Clip B if it's behind the near plane
            else if (zB < zNear) {
                float t = (zNear - zB) / (zA - zB);
                B = B + t * (A - B);
                toB = B - camPos;
                zB = zNear;
            }

            // Project A
            float rgtA = Vector3.Dot(toA, camera.Right);
            float upA  = Vector3.Dot(toA, camera.Up);
            float fovScale = MathF.Tan(80f * MathF.PI / 360f);
            float aspect = (float)W / H;

            float xA = (rgtA / (zA * fovScale * aspect)) * 0.5f + 0.5f;
            float yA = 0.5f - (upA / (zA * fovScale)) * 0.5f;
            Vector2 screenA = new Vector2(xA * W, yA * H);

            // Project B
            float rgtB = Vector3.Dot(toB, camera.Right);
            float upB  = Vector3.Dot(toB, camera.Up);

            float xB = (rgtB / (zB * fovScale * aspect)) * 0.5f + 0.5f;
            float yB = 0.5f - (upB / (zB * fovScale)) * 0.5f;
            Vector2 screenB = new Vector2(xB * W, yB * H);

            drawList.AddLine(screenA, screenB, color, thickness);
        }

        static void DrawChunkBox(float cx, float cy, float cz, uint color, float thickness) {
            const float CHUNK = 256.0f;
            Vector3[] corners = new Vector3[8];
            corners[0] = new Vector3(cx, cy, cz);
            corners[1] = new Vector3(cx + CHUNK, cy, cz);
            corners[2] = new Vector3(cx + CHUNK, cy + CHUNK, cz);
            corners[3] = new Vector3(cx, cy + CHUNK, cz);
            corners[4] = new Vector3(cx, cy, cz + CHUNK);
            corners[5] = new Vector3(cx + CHUNK, cy, cz + CHUNK);
            corners[6] = new Vector3(cx + CHUNK, cy + CHUNK, cz + CHUNK);
            corners[7] = new Vector3(cx, cy + CHUNK, cz + CHUNK);

            // 12 edges
            // Bottom face
            Draw3DLine(corners[0], corners[1], color, thickness);
            Draw3DLine(corners[1], corners[2], color, thickness);
            Draw3DLine(corners[2], corners[3], color, thickness);
            Draw3DLine(corners[3], corners[0], color, thickness);

            // Top face
            Draw3DLine(corners[4], corners[5], color, thickness);
            Draw3DLine(corners[5], corners[6], color, thickness);
            Draw3DLine(corners[6], corners[7], color, thickness);
            Draw3DLine(corners[7], corners[4], color, thickness);

            // Pillars
            Draw3DLine(corners[0], corners[4], color, thickness);
            Draw3DLine(corners[1], corners[5], color, thickness);
            Draw3DLine(corners[2], corners[6], color, thickness);
            Draw3DLine(corners[3], corners[7], color, thickness);
        }

        static void DrawChunkGrid3D() {
            if (w != null) return; // Always returns, silences unreachable code warning

            var pos = Voxel.Client.ClientLauncher.PredictedPosition;
            const float CHUNK = 256.0f;
            float cx = MathF.Floor(pos.X / CHUNK) * CHUNK;
            float cy = MathF.Floor(pos.Y / CHUNK) * CHUNK;
            float cz = MathF.Floor(pos.Z / CHUNK) * CHUNK;

            // Huge span to stretch all the way to the far LOD horizon
            const float lineSpan = 131072.0f;
            float minX = cx - lineSpan;
            float maxX = cx + lineSpan;
            float minZ = cz - lineSpan;
            float maxZ = cz + lineSpan;

            // Constrain line loops locally (e.g. 10 chunks around player) for blazing-fast CPU performance
            float localRadius = 10.0f * CHUNK;

            uint currentChunkColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.9f, 1.0f, 0.85f)); // Bold Cyan
            uint currentChunkSubColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.0f, 0.7f, 0.9f, 0.35f)); // Semi-transparent Cyan
            uint neighborChunkColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.2f, 0.6f, 0.4f)); // Magenta/Pink

            // 1. Draw continuous infinite lines parallel to X (varying X, constant Z)
            for (float z = cz - localRadius; z <= cz + localRadius; z += CHUNK) {
                uint col = (z == cz) ? currentChunkColor : neighborChunkColor;
                float thickness = (z == cz) ? 2.5f : 1.5f;
                // Bottom floor lines
                Draw3DLine(new Vector3(minX, cy, z), new Vector3(maxX, cy, z), col, thickness);
                // Top ceiling lines
                Draw3DLine(new Vector3(minX, cy + CHUNK, z), new Vector3(maxX, cy + CHUNK, z), col, thickness);
            }

            // 2. Draw continuous infinite lines parallel to Z (constant X, varying Z)
            for (float x = cx - localRadius; x <= cx + localRadius; x += CHUNK) {
                uint col = (x == cx) ? currentChunkColor : neighborChunkColor;
                float thickness = (x == cx) ? 2.5f : 1.5f;
                // Bottom floor lines
                Draw3DLine(new Vector3(x, cy, minZ), new Vector3(x, cy, maxZ), col, thickness);
                // Top ceiling lines
                Draw3DLine(new Vector3(x, cy + CHUNK, minZ), new Vector3(x, cy + CHUNK, maxZ), col, thickness);
            }

            // 3. Draw vertical pillars at grid intersections within local range
            for (float x = cx - localRadius; x <= cx + localRadius; x += CHUNK) {
                for (float z = cz - localRadius; z <= cz + localRadius; z += CHUNK) {
                    uint col = (x == cx && z == cz) ? currentChunkColor : neighborChunkColor;
                    float thickness = (x == cx && z == cz) ? 2.5f : 1.5f;
                    Draw3DLine(new Vector3(x, cy, z), new Vector3(x, cy + CHUNK, z), col, thickness);
                }
            }

            // 4. Draw detailed 32x32 block-subdivisions inside the current chunk
            float step = 32.0f;
            for (float x = step; x < CHUNK; x += step) {
                Draw3DLine(new Vector3(cx + x, cy, cz), new Vector3(cx + x, cy, cz + CHUNK), currentChunkSubColor, 1.0f);
                Draw3DLine(new Vector3(cx + x, cy + CHUNK, cz), new Vector3(cx + x, cy + CHUNK, cz + CHUNK), currentChunkSubColor, 1.0f);
                Draw3DLine(new Vector3(cx + x, cy, cz), new Vector3(cx + x, cy + CHUNK, cz), currentChunkSubColor, 1.0f);
                Draw3DLine(new Vector3(cx + x, cy, cz + CHUNK), new Vector3(cx + x, cy + CHUNK, cz + CHUNK), currentChunkSubColor, 1.0f);
            }
            for (float z = step; z < CHUNK; z += step) {
                Draw3DLine(new Vector3(cx, cy, cz + z), new Vector3(cx + CHUNK, cy, cz + z), currentChunkSubColor, 1.0f);
                Draw3DLine(new Vector3(cx, cy + CHUNK, cz + z), new Vector3(cx + CHUNK, cy + CHUNK, cz + z), currentChunkSubColor, 1.0f);
                Draw3DLine(new Vector3(cx, cy, cz + z), new Vector3(cx, cy + CHUNK, cz + z), currentChunkSubColor, 1.0f);
                Draw3DLine(new Vector3(cx + CHUNK, cy, cz + z), new Vector3(cx + CHUNK, cy + CHUNK, cz + z), currentChunkSubColor, 1.0f);
            }
        }

        static void Load() {
            gl    = w!.CreateOpenGL();
            var inp = w!.CreateInput();
            kb    = inp.Keyboards[0];
            mouse = inp.Mice[0];
            mouse.Cursor.CursorMode = CursorMode.Raw;
            imgui = new ImGuiController(gl, w, inp);

            kb.KeyDown += (_, k, _) => {
                // F3: toggle info overlay, mouse stays captured
                if (k == Key.F3) showF3 = !showF3;
                
                // F4: toggle system/physics overlay
                if (k == Key.F4) showF4 = !showF4;

                // F11: fullscreen toggle
                if (k == Key.F11)
                    w!.WindowState = w.WindowState == WindowState.Fullscreen
                        ? WindowState.Normal : WindowState.Fullscreen;

                // ESC: release / recapture mouse (menu mode)
                if (k == Key.Escape) {
                    if (mouse!.Cursor.CursorMode == CursorMode.Raw) {
                        mouse.Cursor.CursorMode = CursorMode.Normal;
                    } else {
                        mouse.Cursor.CursorMode = CursorMode.Raw;
                    }
                }

                // Arrow keys navigate F3 param list
                if (showF3) {
                    if (k == Key.Up)    _paramSel = Math.Max(0, _paramSel - 1);
                    if (k == Key.Down)  _paramSel = Math.Min(_params.Length - 1, _paramSel + 1);
                    if (k == Key.Left || k == Key.Right) {
                        var par = _params[_paramSel];
                        float delta = k == Key.Right ? par.Step : -par.Step;
                        par.Set(Math.Clamp(par.Get() + delta, par.Min, par.Max));
                    }
                }
            };

            // Mouse look handled automatically in camera.Update inside Update loop

            texColor    = MakeTex(W, H);
            texPos      = MakeTex(W, H);
            texEmissive = MakeTex(W, H);
            texBloom    = MakeTex(W, H);
            texHistory  = MakeTex(W, H);
            texResolved = MakeTex(W, H);

            progRay     = CS(GetPath("client/Shaders/raycast.glsl"));
            progVol     = CS(GetPath("client/Shaders/vollight.glsl"));
            progBloom   = CS(GetPath("client/Shaders/bloom.glsl"));
            progTAA     = CS(GetPath("client/Shaders/taa.glsl"));
            progDisplay = Display();

            float[] quad = [-1,-1,0,0, 1,-1,1,0, -1,1,0,1, 1,1,1,1];
            quadVao = gl.GenVertexArray(); quadVbo = gl.GenBuffer();
            gl.BindVertexArray(quadVao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, quadVbo);
            fixed (float* p = quad)
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * 4), p, BufferUsageARB.StaticDraw);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 16, (void*)0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 16, (void*)8);

            prevPos = Voxel.Client.ClientLauncher.PredictedPosition; prevFwd = camera.Forward; prevRgt = camera.Right; prevUp = camera.Up;
            InitQueries();
        }

        static global::System.Diagnostics.Stopwatch swUpdate = new();

        static void Update(double dt) {
            swUpdate.Restart();
            dt_last = dt; time += (float)dt;
            imgui!.Update((float)dt);

            Voxel.Client.ClientLauncher.TimerReset();
            // Run client-side prediction, input gathering and network polling
            Voxel.Client.ClientLauncher.Predict(dt, kb!, mouse!);
            Voxel.Client.ClientLauncher.TimerMark("U:L247 Predict+Input+Net");

            // Smoothly fade-in when pressed, and fade-out when released
            float f = (float)dt;
            fadeW = kb!.IsKeyPressed(Key.W) ? Math.Min(fadeW + f * 8.0f, 1.0f) : Math.Max(fadeW - f * 3.0f, 0.0f);
            fadeA = kb!.IsKeyPressed(Key.A) ? Math.Min(fadeA + f * 8.0f, 1.0f) : Math.Max(fadeA - f * 3.0f, 0.0f);
            fadeS = kb!.IsKeyPressed(Key.S) ? Math.Min(fadeS + f * 8.0f, 1.0f) : Math.Max(fadeS - f * 3.0f, 0.0f);
            fadeD = kb!.IsKeyPressed(Key.D) ? Math.Min(fadeD + f * 8.0f, 1.0f) : Math.Max(fadeD - f * 3.0f, 0.0f);
            fadeSpace = kb!.IsKeyPressed(Key.Space) ? Math.Min(fadeSpace + f * 8.0f, 1.0f) : Math.Max(fadeSpace - f * 3.0f, 0.0f);
            fadeShift = (kb!.IsKeyPressed(Key.ShiftLeft) || kb!.IsKeyPressed(Key.ShiftRight)) ? Math.Min(fadeShift + f * 8.0f, 1.0f) : Math.Max(fadeShift - f * 3.0f, 0.0f);
            fadeCtrl = (kb!.IsKeyPressed(Key.ControlLeft) || kb!.IsKeyPressed(Key.ControlRight)) ? Math.Min(fadeCtrl + f * 8.0f, 1.0f) : Math.Max(fadeCtrl - f * 3.0f, 0.0f);
            fadeQ = kb!.IsKeyPressed(Key.Q) ? Math.Min(fadeQ + f * 8.0f, 1.0f) : Math.Max(fadeQ - f * 3.0f, 0.0f);
            fadeE = kb!.IsKeyPressed(Key.E) ? Math.Min(fadeE + f * 8.0f, 1.0f) : Math.Max(fadeE - f * 3.0f, 0.0f);
            fadeTab = kb!.IsKeyPressed(Key.Tab) ? Math.Min(fadeTab + f * 8.0f, 1.0f) : Math.Max(fadeTab - f * 3.0f, 0.0f);
            fadeAlt = (kb!.IsKeyPressed(Key.AltLeft) || kb!.IsKeyPressed(Key.AltRight)) ? Math.Min(fadeAlt + f * 8.0f, 1.0f) : Math.Max(fadeAlt - f * 3.0f, 0.0f);
            Voxel.Client.ClientLauncher.TimerMark("U:L262 KeyFades");

            // Compute mouse movement vector
            if (mouse != null) {
                var curPos = new Vector2(mouse.Position.X, mouse.Position.Y);
                if (firstMouseFrame) {
                    prevMouseFrame = curPos;
                    firstMouseFrame = false;
                }
                Vector2 rawDelta = curPos - prevMouseFrame;
                prevMouseFrame = curPos;

                // Smoothly decay mouse delta for the bottom-left arrow HUD
                mouseDeltaDecay = Vector2.Lerp(mouseDeltaDecay, rawDelta, 0.15f);
            }
            Voxel.Client.ClientLauncher.TimerMark("U:L275 MouseDelta");

            swUpdate.Stop();
        }

        static global::System.Diagnostics.Stopwatch swRay = new();
        static global::System.Diagnostics.Stopwatch swVol = new();
        static global::System.Diagnostics.Stopwatch swBloom = new();
        static global::System.Diagnostics.Stopwatch swTAA = new();
        static global::System.Diagnostics.Stopwatch swDisplay = new();
        static global::System.Diagnostics.Stopwatch swTotal = new();

        // GPU Fence: limits the driver command queue to 1 frame in flight.
        // Without this, the CPU races N frames ahead and SwapBuffers stalls to drain the queue.
        static nint _frameFence = 0;

        // GPU Timer Query Objects — each measures real GPU nanoseconds via GL_TIME_ELAPSED
        static uint qRay, qTAA, qDisplay;
        static double gpuRayMs, gpuTAAMs, gpuDisplayMs;
        static bool queriesReady = false;

        static void InitQueries() {
            var ids = new uint[3];
            gl!.GenQueries(3, ids);
            qRay = ids[0]; qTAA = ids[1]; qDisplay = ids[2];
            // Prime each query so the first ReadQuery doesn't block
            gl!.BeginQuery(QueryTarget.TimeElapsed, qRay);     gl!.EndQuery(QueryTarget.TimeElapsed);
            gl!.BeginQuery(QueryTarget.TimeElapsed, qTAA);     gl!.EndQuery(QueryTarget.TimeElapsed);
            gl!.BeginQuery(QueryTarget.TimeElapsed, qDisplay);  gl!.EndQuery(QueryTarget.TimeElapsed);
            queriesReady = true;
        }

        // Non-blocking readback of a query from the PREVIOUS frame.
        static double ReadQuery(uint q) {
            gl!.GetQueryObject(q, QueryObjectParameterName.ResultNoWait, out int avail);
            if (avail == 0) return -1.0; // not ready — skip, keep old value
            gl.GetQueryObject(q, QueryObjectParameterName.Result, out uint ns);
            return ns / 1_000_000.0; // nanoseconds → milliseconds
        }

        static void RenderFrame(double fdt) {
            swTotal.Restart();
            frame++;
            var fwd = camera.Forward; var rgt = camera.Right; var up = camera.Up;
            float fov = MathF.Tan(80f * MathF.PI / 360f);
            uint gx = (uint)((W + 7) / 8), gy = (uint)((H + 7) / 8);
            float jx = Halton(frame % 16 + 1, 2) - 0.5f;
            float jy = Halton(frame % 16 + 1, 3) - 0.5f;
            // Frame pacing fence: wait for previous frame to be retired before queuing new commands.
            // This limits the driver queue to 1 frame in flight, preventing SwapBuffer stalls.
            if (_frameFence != 0) {
                gl!.ClientWaitSync(_frameFence, 1u, 5_000_000_000ul); // 1u = GL_SYNC_FLUSH_COMMANDS_BIT
                gl!.DeleteSync(_frameFence);
                _frameFence = 0;
            }

            // --- Non-blocking read of PREVIOUS frame's real GPU timings ---
            if (queriesReady) {
                double r = ReadQuery(qRay);     if (r >= 0) { gpuRayMs     = r; Voxel.Client.ClientLauncher.LineTimers["G:Ray GPU ms"]     = r; }
                double t = ReadQuery(qTAA);     if (t >= 0) { gpuTAAMs     = t; Voxel.Client.ClientLauncher.LineTimers["G:TAA GPU ms"]     = t; }
                double d = ReadQuery(qDisplay); if (d >= 0) { gpuDisplayMs = d; Voxel.Client.ClientLauncher.LineTimers["G:Display GPU ms"] = d; }
            }

            swRay.Restart();
            Use(progRay);
            Img(0, texColor,    BufferAccessARB.WriteOnly);
            Img(1, texPos,      BufferAccessARB.WriteOnly);
            Img(2, texEmissive, BufferAccessARB.WriteOnly);
            Cam(fwd, rgt, up, fov); S1("uTime", time); S1("uFrame", frame);
            S2("uJitter", new Vector2(jx, jy));
            S1("uSeed",            (float)Voxel.World.WConfig.Seed);
            S1("uAmplitude",       Voxel.World.WConfig.Amplitude);
            S1("uFrequency",       Voxel.World.WConfig.Frequency);
            float chunkToWorld = 32.0f * 8.0f;
            S1("uRenderDistance",  Config.RenderDistance  * chunkToWorld);
            S1("uLodThreshold",    Config.LodThreshold    * chunkToWorld);
            S1("uLodScale",        Config.LodScale);
            S1("uFogStart",        Config.FogStart         * chunkToWorld);
            S1("uFogEnd",          Config.LodThreshold     * chunkToWorld);
            S3("uSunCol",          Config.SunColor);
            S1("uSunBrightness",   Config.SunBrightness);
            S1("uSunBloom",        40.0f); // Hardcoded since removed from Config
            S1("uAmbientBase",     Config.AmbientBase);
            S1("uAmbientSlope",    Config.AmbientSlope);
            S1("uCloudAlt",        Config.CloudAltitude);
            S1("uCloudCover",      Config.CloudCover);
            S1("uCloudScale",      Config.CloudScale);
            S1("uShowGrid",        0.0f); // Disabled since removed from Config
            S1("uEnableFog",       Config.EnableFog     ? 1.0f : 0.0f);
            gl!.BeginQuery(QueryTarget.TimeElapsed, qRay);
            gl.DispatchCompute(gx, gy, 1);
            gl.EndQuery(QueryTarget.TimeElapsed);
            Bar();
            swRay.Stop();

            // Config.EnableVolumetric was removed
 
            if (Config.BloomIntensity > 0.001f) {
                swBloom.Restart();
                Use(progBloom);
                // Pass 1: Horizontal blur — threshold + blur along X
                S1("uThreshold", 1.0f);
                gl.Uniform1(gl.GetUniformLocation(_p, "uHorizontal"), 1);
                Img(0, texEmissive, BufferAccessARB.ReadOnly);
                Img(1, texBloom,    BufferAccessARB.WriteOnly);
                gl.DispatchCompute(gx, gy, 1);
                Bar();
                // Pass 2: Vertical blur — read blurred X, write final 2D Gaussian
                gl.Uniform1(gl.GetUniformLocation(_p, "uHorizontal"), 0);
                Img(0, texBloom, BufferAccessARB.ReadOnly);
                Img(1, texBloom, BufferAccessARB.WriteOnly);
                gl.DispatchCompute(gx, gy, 1);
                Bar();
                swBloom.Stop();
            }

            swTAA.Restart();
            Use(progTAA);
            Img(0, texColor,    BufferAccessARB.ReadOnly);
            Img(1, texPos,      BufferAccessARB.ReadOnly);
            Img(2, texBloom,    BufferAccessARB.ReadOnly);
            Img(3, texHistory,  BufferAccessARB.ReadOnly);
            Img(4, texResolved, BufferAccessARB.WriteOnly);
            Cam(fwd, rgt, up, fov);
            S3("uPrevPos", prevPos); S3("uPrevFwd", prevFwd);
            S3("uPrevRgt", prevRgt); S3("uPrevUp",  prevUp);
            S1("uPrevFov", fov); S1("uFrame", frame);
            S1("uBloomIntensity", Config.BloomIntensity);
            gl.BeginQuery(QueryTarget.TimeElapsed, qTAA);
            gl.DispatchCompute(gx, gy, 1);
            gl.EndQuery(QueryTarget.TimeElapsed);
            Bar();
            swTAA.Stop();

            (texHistory, texResolved) = (texResolved, texHistory);
            prevPos = Voxel.Client.ClientLauncher.PredictedPosition; prevFwd = fwd; prevRgt = rgt; prevUp = up;

            swDisplay.Restart();
            gl.Viewport(0, 0, (uint)W, (uint)H);
            gl.UseProgram(progDisplay);
            gl.ActiveTexture(TextureUnit.Texture0);
            gl.BindTexture(TextureTarget.Texture2D, texHistory);
            gl.Uniform1(gl.GetUniformLocation(progDisplay, "uTex"),        0);
            gl.Uniform1(gl.GetUniformLocation(progDisplay, "uExposure"),   Config.Exposure);
            gl.Uniform1(gl.GetUniformLocation(progDisplay, "uContrast"),   Config.Contrast);
            gl.Uniform1(gl.GetUniformLocation(progDisplay, "uSaturation"), Config.Saturation);
            gl.Uniform1(gl.GetUniformLocation(progDisplay, "uBrightness"), Config.Brightness);
            gl.Uniform1(gl.GetUniformLocation(progDisplay, "uWhitePoint"), Config.WhitePoint);
            gl.Uniform1(gl.GetUniformLocation(progDisplay, "uGamma"),      Config.Gamma);
            gl.BindVertexArray(quadVao);
            gl.BeginQuery(QueryTarget.TimeElapsed, qDisplay);
            gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            gl.EndQuery(QueryTarget.TimeElapsed);
            swDisplay.Stop();
 
            swTotal.Stop();
            Voxel.Client.ClientLauncher.LineTimers["C:Frame ms (wall)"] = fdt * 1000.0;
            DrawKeystrokes();
            if (showF3) DrawF3();
            if (showF4) DrawF4();
            
            // Push Visual Line Profiler Data
            double getV(string key) => Voxel.Client.ClientLauncher.SystemTimers.TryGetValue(key, out double val) ? val : 0.0;
            histClient[histIdx] = (float)(fdt * 1000.0);
            histServer[histIdx] = (float)getV("S:01 Physics Simulation");
            histNet[histIdx]    = (float)getV("C:02 Network Polling");
            histIdx = (histIdx + 1) % 128;
            DrawVisualProfiler();

            imgui!.Render();
            // Insert fence after all GPU work so next frame can wait for this one
            _frameFence = gl!.FenceSync(SyncCondition.SyncGpuCommandsComplete, SyncBehaviorFlags.None);
        }

        static void DrawKeystrokes() {
            var io = ImGui.GetIO();
            _activeKeysBuffer.Clear();
            if (fadeW > 0.01f) _activeKeysBuffer.Add(("W", fadeW));
            if (fadeA > 0.01f) _activeKeysBuffer.Add(("A", fadeA));
            if (fadeS > 0.01f) _activeKeysBuffer.Add(("S", fadeS));
            if (fadeD > 0.01f) _activeKeysBuffer.Add(("D", fadeD));
            if (fadeSpace > 0.01f) _activeKeysBuffer.Add(("Space", fadeSpace));
            if (fadeShift > 0.01f) _activeKeysBuffer.Add(("Shift", fadeShift));
            if (fadeCtrl > 0.01f) _activeKeysBuffer.Add(("Ctrl", fadeCtrl));
            if (fadeQ > 0.01f) _activeKeysBuffer.Add(("Q", fadeQ));
            if (fadeE > 0.01f) _activeKeysBuffer.Add(("E", fadeE));
            if (fadeTab > 0.01f) _activeKeysBuffer.Add(("Tab", fadeTab));
            if (fadeAlt > 0.01f) _activeKeysBuffer.Add(("Alt", fadeAlt));

            if (_activeKeysBuffer.Count == 0) return;

            // Set size and pos for center-top overlay
            ImGui.SetNextWindowPos(new Vector2(io.DisplaySize.X / 2f - 200f, 25f), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(400f, 50f), ImGuiCond.Always);
            var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground;
            ImGui.Begin("KeystrokesOverlay", flags);

            // Compute total text width to align nicely in the center
            float totalWidth = 0f;
            for (int i = 0; i < _activeKeysBuffer.Count; i++) {
                totalWidth += ImGui.CalcTextSize(_activeKeysBuffer[i].Name).X;
                if (i < _activeKeysBuffer.Count - 1) {
                    totalWidth += ImGui.CalcTextSize(" + ").X;
                }
            }

            ImGui.SetCursorPosX((400f - totalWidth) / 2f);

            for (int i = 0; i < _activeKeysBuffer.Count; i++) {
                var k = _activeKeysBuffer[i];
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, k.Alpha));
                ImGui.Text(k.Name);
                ImGui.PopStyleColor();

                if (i < _activeKeysBuffer.Count - 1) {
                    float nextAlpha = Math.Min(k.Alpha, _activeKeysBuffer[i + 1].Alpha);
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, nextAlpha * 0.4f));
                    ImGui.Text("+");
                    ImGui.PopStyleColor();
                    ImGui.SameLine();
                }
            }

            ImGui.End();
        }

        static void DrawF4() {
            var io = ImGui.GetIO();
            ImGui.SetNextWindowPos(new Vector2(io.DisplaySize.X - 360, 10), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(350, 640), ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(0.78f);
            var flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
                      | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoInputs;
            ImGui.Begin("F4 -- System & Physics State (Top Right)", flags);
            
            ImGui.TextColored(new Vector4(0f, 1f, 1f, 1f), "SYSTEM FORCE & MOVEMENT STATISTICS");
            ImGui.Separator();
            ImGui.Text($"System Pos:   {Voxel.Client.ClientLauncher.ServerPosition.X:F2}, {Voxel.Client.ClientLauncher.ServerPosition.Y:F2}, {Voxel.Client.ClientLauncher.ServerPosition.Z:F2}");
            ImGui.Text($"System Vel:   {Voxel.Client.ClientLauncher.ServerVelocity.X:F2}, {Voxel.Client.ClientLauncher.ServerVelocity.Y:F2}, {Voxel.Client.ClientLauncher.ServerVelocity.Z:F2}");
            ImGui.Text($"System Speed: {Voxel.Client.ClientLauncher.ServerVelocity.Length():F2} units/s");
            ImGui.Text($"Player Mass:  {Voxel.Client.ClientLauncher.Phys.Mass:F1} units (2^13)");
            ImGui.Text($"AppliedForce: {Voxel.Client.ClientLauncher.Phys.AppliedForce.X:F1}, {Voxel.Client.ClientLauncher.Phys.AppliedForce.Y:F1}, {Voxel.Client.ClientLauncher.Phys.AppliedForce.Z:F1} N");
            ImGui.Text($"DragForce:    {Voxel.Client.ClientLauncher.Phys.DragForce.X:F1}, {Voxel.Client.ClientLauncher.Phys.DragForce.Y:F1}, {Voxel.Client.ClientLauncher.Phys.DragForce.Z:F1} N");
            ImGui.Text($"Acceleration: {Voxel.Client.ClientLauncher.Phys.Acceleration.X:F2}, {Voxel.Client.ClientLauncher.Phys.Acceleration.Y:F2}, {Voxel.Client.ClientLauncher.Phys.Acceleration.Z:F2} m/s²");
            


            ImGui.Separator();
            ImGui.TextColored(new Vector4(0f, 0.8f, 1f, 1f), "SYSTEM/SERVER TIMINGS (Separate Thread)");
            _systemKeysBuffer.Clear();
            foreach (var kvp in Voxel.Client.ClientLauncher.SystemTimers) _systemKeysBuffer.Add(kvp.Key);
            _systemKeysBuffer.Sort();
            foreach (var key in _systemKeysBuffer) {
                double ms = Voxel.Client.ClientLauncher.SystemTimers[key];
                ImGui.Text($"{key}: {ms:F3} ms");
            }

            ImGui.Separator();
            ImGui.TextColored(new Vector4(1f, 1.0f, 0f, 1f), "CLIENT GAME LOOP DENSE LINE TIMERS");
            _sortedKeysBuffer.Clear();
            foreach (var kvp in Voxel.Client.ClientLauncher.LineTimers) _sortedKeysBuffer.Add(kvp.Key);
            _sortedKeysBuffer.Sort();
            foreach (var key in _sortedKeysBuffer) {
                double ms = Voxel.Client.ClientLauncher.LineTimers[key];
                if (ms > 1.0) {
                    ImGui.TextColored(new Vector4(1f, 0.2f, 0.2f, 1f), $"{key}: {ms:F3} ms");
                } else {
                    ImGui.Text($"{key}: {ms:F3} ms");
                }
            }

            ImGui.Separator();
            ImGui.TextColored(new Vector4(0f, 1f, 0f, 1f), $"Total Frame:   {swTotal.Elapsed.TotalMilliseconds:F2} ms");
            ImGui.Text($"FPS:           {1.0/dt_last:F0} ({dt_last*1000:F1} ms)");
            
            ImGui.End();
        }

        static void DrawF3() {
            int rowH  = 18;
            int height = 110 + _params.Length * rowH + 10;
            ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(340, height), ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(0.78f);
            var flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove
                      | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoInputs;
            ImGui.Begin("F3  -- up/down select  left/right change", flags);
 
            ImGui.Text($"FPS {1.0/dt_last:F0}   ({dt_last*1000:F1} ms)   Frame {frame}");
            var pos = Voxel.Client.ClientLauncher.PredictedPosition;
            ImGui.Text($"Pos  {pos.X:F0}  {pos.Y:F0}  {pos.Z:F0}");
            ImGui.Text($"Yaw  {camera.Yaw*180/MathF.PI:F1}   Pitch {camera.Pitch*180/MathF.PI:F1}");
            ImGui.Separator();
            ImGui.Text("Config");

            for (int i = 0; i < _params.Length; i++) {
                bool sel = i == _paramSel;
                if (sel) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 0, 1));

                var   par = _params[i];
                float val = par.Get();
                // Show booleans as ON/OFF, others as numbers
                string valStr = (par.Min == 0 && par.Max == 1 && par.Step == 1)
                    ? (val > 0.5f ? "ON" : "OFF")
                    : val.ToString("G5");

                ImGui.Text($"  {(sel ? ">" : " ")} {par.Name,-18} {valStr}");
                if (sel) ImGui.PopStyleColor();
            }

            ImGui.End();

            // Draw clean mouse movement arrow at the bottom-left
            var io = ImGui.GetIO();
            var drawList = ImGui.GetForegroundDrawList();
            Vector2 start = new Vector2(80f, io.DisplaySize.Y - 80f);
            
            // Background anchor circle
            // Background anchor circle: White outline ONLY (Alpha boosted)
            drawList.AddCircle(start, 12f, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.90f)), 16, 2.0f);
            
            Vector2 mVec = mouseDeltaDecay;
            float mLen = mVec.Length();
            if (mLen > 0.1f) {
                float maxLen = 65f;
                if (mLen > maxLen) mVec = Vector2.Normalize(mVec) * maxLen;
                Vector2 end = start + mVec * 1.0f; 
                
                uint whiteColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1.0f)); // Pure white
                drawList.AddLine(start, end, whiteColor, 1.5f); // Thin connecting line
                
                // Terminal Dot at endpoint
                drawList.AddCircleFilled(end, 4.0f, whiteColor);
            }
        }

        static void Use(uint p) { gl!.UseProgram(p); _p = p; }
        static void S1(string n, float v)   => gl!.Uniform1(gl.GetUniformLocation(_p, n), v);
        static void S2(string n, Vector2 v) => gl!.Uniform2(gl.GetUniformLocation(_p, n), v);
        static void S3(string n, Vector3 v) => gl!.Uniform3(gl.GetUniformLocation(_p, n), v);
        static void Cam(Vector3 fwd, Vector3 rgt, Vector3 up, float fov) {
            S3("uCamPos", Voxel.Client.ClientLauncher.PredictedPosition); S3("uCamFwd", fwd); S3("uCamRgt", rgt); S3("uCamUp", up); S1("uFov", fov);
        }
        static void Img(uint slot, uint tex, BufferAccessARB access) =>
            gl!.BindImageTexture(slot, tex, 0, false, 0, access, InternalFormat.Rgba32f);
        static void Bar() =>
            gl!.MemoryBarrier(MemoryBarrierMask.ShaderImageAccessBarrierBit | MemoryBarrierMask.TextureFetchBarrierBit);

        static float Halton(int i, int b) {
            float f = 1f, r = 0f;
            while (i > 0) { f /= b; r += f * (i % b); i /= b; }
            return r;
        }

        static uint MakeTex(int w, int h) {
            uint t = gl!.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, t);
            gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba32f,
                          (uint)w, (uint)h, 0, PixelFormat.Rgba, PixelType.Float, null);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            return t;
        }

        static string GetPath(string rel) {
            foreach (var pfx in new[]{ "", "../", "../../", "../../../" })
                if (File.Exists(pfx + rel)) return pfx + rel;
            return rel;
        }

        static uint CS(string path) {
            string src = File.Exists(path) ? File.ReadAllText(path) : "";
            while (src.Contains("#include")) {
                int idx   = src.IndexOf("#include");
                int start = src.IndexOf('"', idx) + 1;
                int end   = src.IndexOf('"', start);
                string incFile = src[start..end];
                string incPath = Path.Combine(Path.GetDirectoryName(path) ?? "", incFile);
                string incSrc  = File.Exists(incPath) ? File.ReadAllText(incPath) : "";
                src = src.Remove(idx, end - idx + 1).Insert(idx, incSrc);
            }
            uint s = gl!.CreateShader(ShaderType.ComputeShader);
            gl.ShaderSource(s, src); gl.CompileShader(s);
            var i = gl.GetShaderInfoLog(s);
            if (!string.IsNullOrWhiteSpace(i)) Console.WriteLine($"[CS {path}] {i}");
            uint p = gl.CreateProgram();
            gl.AttachShader(p, s); gl.LinkProgram(p);
            var pi = gl.GetProgramInfoLog(p);
            if (!string.IsNullOrWhiteSpace(pi)) Console.WriteLine($"[Prog {path}] {pi}");
            return p;
        }

        static uint Display() {
            const string vs = @"#version 460 core
layout(location=0) in vec2 aPos; layout(location=1) in vec2 aUV;
out vec2 vUV; void main(){ gl_Position=vec4(aPos,0,1); vUV=aUV; }";

            string path = GetPath("client/Shaders/display.glsl");
            string fs = File.Exists(path) ? File.ReadAllText(path) : "";
            while (fs.Contains("#include")) {
                int idx   = fs.IndexOf("#include");
                int start = fs.IndexOf('"', idx) + 1;
                int end   = fs.IndexOf('"', start);
                string incFile = fs[start..end];
                string incPath = Path.Combine(Path.GetDirectoryName(path) ?? "", incFile);
                string incSrc  = File.Exists(incPath) ? File.ReadAllText(incPath) : "";
                fs = fs.Remove(idx, end - idx + 1).Insert(idx, incSrc);
            }
            uint v = gl!.CreateShader(ShaderType.VertexShader);
            gl.ShaderSource(v, vs); gl.CompileShader(v);
            uint f = gl.CreateShader(ShaderType.FragmentShader);
            gl.ShaderSource(f, fs); gl.CompileShader(f);
            var i = gl.GetShaderInfoLog(f);
            if (!string.IsNullOrWhiteSpace(i)) Console.WriteLine($"[FS {path}] {i}");
            uint p = gl.CreateProgram();
            gl.AttachShader(p, v); gl.AttachShader(p, f); gl.LinkProgram(p);
            var pi = gl.GetProgramInfoLog(p);
            if (!string.IsNullOrWhiteSpace(pi)) Console.WriteLine($"[Prog FS {path}] {pi}");
            return p;
        }
        static void DrawVisualProfiler() {
            var io = ImGui.GetIO();
            var drawList = ImGui.GetForegroundDrawList();
            
            float graphW = 220;
            float graphH = 140;
            float paddingLeft = 55;
            float paddingEdge = 25;
            
            float totalW = graphW + paddingLeft;
            Vector2 br = new Vector2(io.DisplaySize.X - paddingEdge, io.DisplaySize.Y - paddingEdge);
            Vector2 tl = new Vector2(br.X - totalW, br.Y - graphH);
            
            // 1. Outer stylized black square background
            drawList.AddRectFilled(tl, br, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.90f)), 6f);
            drawList.AddRect(tl, br, ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.4f, 1f)), 6f);
            
            float graphX = tl.X + paddingLeft;
            
            // 2. Draw Graduated Cylinder scale (0-60ms, one every 10)
            for (int i = 0; i <= 60; i += 10) {
                float f = i / 64.0f;
                float y = br.Y - (f * graphH);
                drawList.AddLine(new Vector2(tl.X + 42, y), new Vector2(graphX, y), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.6f)));
                drawList.AddLine(new Vector2(graphX, y), new Vector2(br.X, y), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.15f))); // Subtle grid lines
                drawList.AddText(new Vector2(tl.X + 8, y - 7), ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1f)), $"{i} ms");
            }

            // 3. Internal graph area clip rect and background grid
            drawList.PushClipRect(new Vector2(graphX, 0), new Vector2(br.X, io.DisplaySize.Y));
            drawList.AddRectFilled(new Vector2(graphX, tl.Y), br, ImGui.GetColorU32(new Vector4(0.05f, 0.05f, 0.08f, 1f)));

            // Line drawing helper
            void DrawHistory(float[] h, Vector4 color) {
                uint c = ImGui.GetColorU32(color);
                for (int i = 0; i < 127; i++) {
                    int a = (histIdx + i) % 128;
                    int b = (histIdx + i + 1) % 128;
                    float vA = h[a] / 64.0f * graphH;
                    float vB = h[b] / 64.0f * graphH;
                    float xA = graphX + (i / 127f) * graphW;
                    float xB = graphX + ((i+1) / 127f) * graphW;
                    drawList.AddLine(new Vector2(xA, br.Y - vA), new Vector2(xB, br.Y - vB), c, 1.8f);
                }
            }

            // Draw overlapping threads (Green=Client, Cyan=Server, Yellow=Net)
            DrawHistory(histClient, new Vector4(0.1f, 0.9f, 0.1f, 0.8f));
            DrawHistory(histServer, new Vector4(0.0f, 0.7f, 1.0f, 0.9f));
            DrawHistory(histNet,    new Vector4(1.0f, 0.9f, 0.1f, 1.0f));

            drawList.PopClipRect();

            // Top Legend overlay
            drawList.AddText(new Vector2(graphX + 5, tl.Y + 4), ImGui.GetColorU32(new Vector4(0.1f, 0.9f, 0.1f, 1f)), "CLNT");
            drawList.AddText(new Vector2(graphX + 65, tl.Y + 4), ImGui.GetColorU32(new Vector4(0.0f, 0.7f, 1.0f, 1f)), "SRVR");
            drawList.AddText(new Vector2(graphX + 125, tl.Y + 4), ImGui.GetColorU32(new Vector4(1.0f, 0.9f, 0.1f, 1f)), "NET");
        }
    }
}
