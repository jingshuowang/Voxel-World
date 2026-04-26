using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using System.Numerics;
using System.Diagnostics;
using Voxel.Client;

namespace Voxel {
    class Program {
        static IWindow? w; static GL? gl;
        static Gfx? gfx;
        static IKeyboard? kb; static IMouse? mouse;
        static float currentVelocity = 0;
        static Vector3 prevPos = new(0, 20, 80);
        static Stopwatch sw = new();

        static void Main() {
            var o = WindowOptions.Default;
            o.Size = new(1920, 1080);
            o.Title = "Advanced PBR Engine (Client-Side Input)";
            o.WindowState = WindowState.Fullscreen;
            o.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(4, 6));
            w = Window.Create(o);
            w.Load += OnLoad; w.Update += OnUpdate; w.Render += OnRender;
            w.Run();
        }

        static void OnLoad() {
            gl = w!.CreateOpenGL();
            var inp = w.CreateInput();
            kb = inp.Keyboards[0]; mouse = inp.Mice[0];
            mouse.Cursor.CursorMode = CursorMode.Raw;

            var world = new World();
            gfx = new Gfx(gl, w.Size.X, w.Size.Y);
            gfx.UpdateVoxels(world.Voxels);
            sw.Start();
        }

        static void OnUpdate(double dt) {
            if (kb!.IsKeyPressed(Key.Escape)) w!.Close();
            
            // Move input handling to Gfx (Client-Side)
            gfx!.ProcessInput(kb, mouse!, (float)dt);

            float dist = Vector3.Distance(gfx.Pos, prevPos);
            currentVelocity = dist * 2.0f;
            prevPos = gfx.Pos;
        }

        static void OnRender(double dt) {
            gfx!.Render(currentVelocity, (float)sw.Elapsed.TotalSeconds);
        }
    }
}
