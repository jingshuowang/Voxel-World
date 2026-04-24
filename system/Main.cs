using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using System.Numerics;
using Voxel.Rendering;
using Voxel.System;

namespace Voxel {
    class Program {
        static IWindow? w;
        static GL? gl;
        static Gfx.S? s;
        static Gfx.T? t;
        static Vector3 p = new(0, 2060, 0);
        static float yaw = -90f, pit = 0;
        static Dictionary<long, Cks.C> cks = new();
        static uint vao, vbo;
        static int selX, selY, selZ;
        static bool hasSel;
        static Vector2 lM;

        static void Main() {
            GC.TryStartNoGCRegion(1024 * 1024 * 128);
            var o = WindowOptions.Default;
            o.Size = new(1280, 720);
            o.Title = "Voxel C# [No-GC]";
            w = Window.Create(o);
            w.Load += OnLoad;
            w.Update += OnUpdate;
            w.Render += OnRender;
            w.Run();
        }

        static void OnLoad() {
            gl = w!.CreateOpenGL();
            Lib.Init(gl);
            s = new Gfx.S(gl, "Asset/shader/chunk.vs", "Asset/shader/chunk.fs");
            t = new Gfx.T(gl, "Asset/texture.png");
            
            vao = gl.GenVertexArray(); gl.BindVertexArray(vao);
            vbo = gl.GenBuffer(); gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            unsafe { gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)100_000_000, null, BufferUsageARB.DynamicDraw); }
            unsafe { gl.VertexAttribIPointer(0, 1, GLEnum.UnsignedInt, 4, null); }
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribDivisor(0, 1);
            
            uint q = gl.GenBuffer(); gl.BindBuffer(BufferTargetARB.ArrayBuffer, q);
            unsafe { 
                float[] quad = {0,0,1,0,0,1,1,1};
                fixed(float* ptr = quad) gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * 4), ptr, BufferUsageARB.StaticDraw);
                gl.VertexAttribPointer(1, 2, GLEnum.Float, false, 8, null);
            }
            gl.EnableVertexAttribArray(1);

            Task.Run(() => {
                while(true) {
                    int cx = (int)p.X/16, cz = (int)p.Z/16;
                    for(int x=-4; x<=4; x++) for(int z=-4; z<=4; z++) {
                        long k = ((long)(cx+x)&0xFFFFFFL)|(((long)(cz+z)&0xFFFFFFL)<<24);
                        if(!cks.ContainsKey(k)) {
                            var c = new Cks.C(cx+x, 128, cz+z);
                            c.Gen(); c.Mesh(); cks[k] = c;
                        }
                    }
                    Thread.Sleep(100);
                }
            });
        }

        static void OnUpdate(double d) {
            var inp = w!.CreateInput().Keyboards[0];
            var m = w.CreateInput().Mice[0];
            float spd = 10f * (float)d;
            Vector3 v = Vector3.Zero;
            if (inp.IsKeyPressed(Key.W)) v += Vector3.Normalize(new Vector3(MathF.Cos(yaw*MathF.PI/180), 0, MathF.Sin(yaw*MathF.PI/180))) * spd;
            if (inp.IsKeyPressed(Key.S)) v -= Vector3.Normalize(new Vector3(MathF.Cos(yaw*MathF.PI/180), 0, MathF.Sin(yaw*MathF.PI/180))) * spd;
            
            p = Phys.Move(p, v, new Phys.A(p, 0.6f, 1.8f), new List<Phys.A>());
            
            if (m.Cursor.CursorMode == CursorMode.Raw) {
                yaw += (m.Position.X - lM.X) * 0.1f;
                pit = Math.Clamp(pit - (m.Position.Y - lM.Y) * 0.1f, -89, 89);
            }
            lM = m.Position;
            if (inp.IsKeyPressed(Key.Escape)) m.Cursor.CursorMode = CursorMode.Normal;
            if (m.IsButtonPressed(MouseButton.Left)) m.Cursor.CursorMode = CursorMode.Raw;

            UpdateRaycast();
        }

        static void UpdateRaycast() {
            hasSel = false;
            Vector3 d = new(MathF.Cos(yaw*MathF.PI/180)*MathF.Cos(pit*MathF.PI/180), MathF.Sin(pit*MathF.PI/180), MathF.Sin(yaw*MathF.PI/180)*MathF.Cos(pit*MathF.PI/180));
            int x=(int)MathF.Floor(p.X), y=(int)MathF.Floor(p.Y), z=(int)MathF.Floor(p.Z);
            int sX=Math.Sign(d.X), sY=Math.Sign(d.Y), sZ=Math.Sign(d.Z);
            float tDX=Math.Abs(1/d.X), tDY=Math.Abs(1/d.Y), tDZ=Math.Abs(1/d.Z);
            float tMX=(sX>0?MathF.Floor(p.X)+1-p.X:p.X-MathF.Floor(p.X))*tDX;
            float tMY=(sY>0?MathF.Floor(p.Y)+1-p.Y:p.Y-MathF.Floor(p.Y))*tDY;
            float tMZ=(sZ>0?MathF.Floor(p.Z)+1-p.Z:p.Z-MathF.Floor(p.Z))*tDZ;
            for(int i=0;i<50;i++) {
                long k = ((long)(x/16)&0xFFFFFFL)|(((long)(z/16)&0xFFFFFFL)<<24);
                if(cks.TryGetValue(k, out var c) && c.B[(x&15)+(z&15)*16+(y&15)*256]!=0) {
                    hasSel=true; selX=x; selY=y; selZ=z; return;
                }
                if(tMX<tMY) { if(tMX<tMZ){x+=sX;tMX+=tDX;}else{z+=sZ;tMZ+=tDZ;} }
                else { if(tMY<tMZ){y+=sY;tMY+=tDY;}else{z+=sZ;tMZ+=tDZ;} }
            }
        }

        static void OnRender(double d) {
            gl!.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            gl.Enable(EnableCap.DepthTest);
            s!.B();
            var vM = Matrix4x4.CreateLookAt(p, p + new Vector3(MathF.Cos(yaw*MathF.PI/180)*MathF.Cos(pit*MathF.PI/180), MathF.Sin(pit*MathF.PI/180), MathF.Sin(yaw*MathF.PI/180)*MathF.Cos(pit*MathF.PI/180)), Vector3.UnitY);
            s.Set("vM", vM);
            s.Set("pM", Matrix4x4.CreatePerspectiveFieldOfView(1.2f, (float)w!.Size.X/w.Size.Y, 0.1f, 1000f));
            s.Set("cP", p);
            s.Set("sD", Vector3.Normalize(new(1,1,1)));
            s.Set("day", 1f);
            t!.B();
            gl.BindVertexArray(vao);
            
            unsafe {
                foreach(var c in cks.Values) {
                    if(c.Ok) {
                        for(int f=0; f<6; f++) {
                            if(c.Data[f]?.Length > 0) {
                                fixed(int* ptr = c.Data[f]) {
                                    gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(c.Data[f].Length * 4), ptr);
                                    gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)c.Data[f].Length);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
