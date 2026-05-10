using ImGuiNET;
using System.Collections.Generic;
using System.Numerics;

namespace Voxel.Rendering
{
    public class GUI
    {
        public static void Draw(double dt, int entityCount, Vector3 playerPos, Vector3 serverPos, Vector3 velocity, Vector3 acceleration, Vector3 appliedAcc, Vector3 dragAcc, Vector3 force, Vector3 appliedForce, Vector3 dragForce, float mass, float drag, double cpuTimeAvg, double gpuTimeAvg, Dictionary<string, double> avgTimers)
        {
            ImGui.SetNextWindowBgAlpha(0.7f);
            if (ImGui.Begin("F3 Profiler / Stats", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text($"FPS: {1.0 / dt:F0}  ({dt * 1000.0:F1} ms/frame)");
                ImGui.Text($"Entities: {entityCount}");
                ImGui.Separator();
                
                ImGui.Text($"Client Pos: {playerPos.X:F1}, {playerPos.Y:F1}, {playerPos.Z:F1}");
                ImGui.Text($"Server Pos: {serverPos.X:F1}, {serverPos.Y:F1}, {serverPos.Z:F1} (20Hz Tick)");
                ImGui.Text($"Velocity: {velocity.X:F2}, {velocity.Y:F2}, {velocity.Z:F2} (Speed: {velocity.Length():F2})");
                
                ImGui.Separator();
                ImGui.Text("--- PHYSICS ---");
                ImGui.Text($"Total Accel: {acceleration.X:F2}, {acceleration.Y:F2}, {acceleration.Z:F2}");
                ImGui.Text($"  Input Accel: {appliedAcc.X:F2}, {appliedAcc.Y:F2}, {appliedAcc.Z:F2}");
                ImGui.Text($"  Drag Accel:  {dragAcc.X:F2}, {dragAcc.Y:F2}, {dragAcc.Z:F2}");
                
                ImGui.Text($"Total Force: {force.X:F2}, {force.Y:F2}, {force.Z:F2}");
                ImGui.Text($"  Input Force: {appliedForce.X:F2}, {appliedForce.Y:F2}, {appliedForce.Z:F2}");
                ImGui.Text($"  Drag Force:  {dragForce.X:F2}, {dragForce.Y:F2}, {dragForce.Z:F2}");
                
                ImGui.Text($"Mass: {mass:F0} | Drag Coeff: {drag:F2}");
                ImGui.Separator();

                ImGui.Text("Timers (avg ms)");
                double frameMs = dt * 1000.0;
                double unaccounted = frameMs - (cpuTimeAvg + gpuTimeAvg);
                if (unaccounted < 0) unaccounted = 0;
                
                ImGui.Text($"CPU Main Loop: {cpuTimeAvg:F3}");
                ImGui.Text($"Render submit: {gpuTimeAvg:F3}");
                ImGui.Text($"GPU Execution / VSync: {unaccounted:F3} ms");
                foreach (var kv in avgTimers)
                    ImGui.Text($"{kv.Key}: {kv.Value:F3}");
                
                ImGui.End();
            }

            // Draw clean 2D centered crosshair on the GUI foreground
            var io = ImGui.GetIO();
            var center = io.DisplaySize / 2.0f;
            var drawList = ImGui.GetForegroundDrawList();
            uint crossColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.75f));
            
            // Draw horizontal crossbar
            drawList.AddLine(new Vector2(center.X - 8, center.Y), new Vector2(center.X - 2, center.Y), crossColor, 1.5f);
            drawList.AddLine(new Vector2(center.X + 2, center.Y), new Vector2(center.X + 8, center.Y), crossColor, 1.5f);
            // Draw vertical crossbar
            drawList.AddLine(new Vector2(center.X, center.Y - 8), new Vector2(center.X, center.Y - 2), crossColor, 1.5f);
            drawList.AddLine(new Vector2(center.X, center.Y + 2), new Vector2(center.X, center.Y + 8), crossColor, 1.5f);
        }
    }
}
