using System;
using System.Numerics;
using Silk.NET.Input;
using Voxel.Rendering;

namespace Voxel.Client {
    /// <summary>
    /// Client: The primary client-side Main orchestrator.
    /// Holds all client modules, drives prediction/input/network, and owns dense line timers.
    /// </summary>
    public static class ClientLauncher {
        // ── Module Instances ────────────────────────────────────────────────
        public static Network   Net    = new();
        public static Detection Camera = new();
        public static SVO       Svo    = new();
        public static Voxel.System.Physics Phys = new();

        // ── Authoritative state from system/server ───────────────────────
        public static Vector3 ServerPosition { get; set; } = new Vector3(0, 1700000f, 0);
        public static Vector3 ServerVelocity { get; set; } = Vector3.Zero;

        // ── Client-side smooth predicted position ────────────────────────
        public static Vector3 PredictedPosition { get; set; } = new Vector3(0, 1700000f, 0);

        // ── Dense line-level profiling ───────────────────────────────────
        // ConcurrentDictionary: render thread writes, F4 panel reads — no lock needed.
        public static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, double> LineTimers
            = new();

        // ── Separate System/Server Thread-level profiling ─────────────────
        // ConcurrentDictionary: 144Hz server thread writes, render thread reads — thread-safe.
        public static readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, double> SystemTimers
            = new();

        private static global::System.Diagnostics.Stopwatch _probe = new();

        /// <summary>Call this at the START of a timed block to reset the probe.</summary>
        public static void TimerReset() => _probe.Restart();

        /// <summary>
        /// Record elapsed ms since the last TimerReset/TimerMark under a label like "R:L319".
        /// Does NOT reset — call TimerReset() to begin a new segment.
        /// </summary>
        public static void TimerMark(string label) {
            LineTimers[label] = _probe.Elapsed.TotalMilliseconds;
            _probe.Restart();   // start the next segment immediately
        }

        // ── Per-step coarse timers (still useful for quick summary) ───────
        public static global::System.Diagnostics.Stopwatch swPredict = new();
        public static global::System.Diagnostics.Stopwatch swInput   = new();
        public static global::System.Diagnostics.Stopwatch swNetwork = new();

        // ── Launch ───────────────────────────────────────────────────────
        public static void Launch() {
            ServerPosition    = new Vector3(0, 0f, 0);
            PredictedPosition = ServerPosition;
            Phys.Position     = ServerPosition;
            Voxel.Rendering.RaycastExperiment.RaycastMain();
        }

        // ── Per-frame update: prediction, input, networking ──────────────
        public static void Predict(double dt, IKeyboard kb, IMouse mouse) {
            swInput.Restart();
            Camera.Update(kb, mouse, (float)dt);                           // L68
            swInput.Stop();

            swPredict.Restart();
            // Pure Fixed-Step snap: visuals strictly mirror the 144Hz simulation without smoothing
            PredictedPosition = ServerPosition;
            swPredict.Stop();

            swNetwork.Restart();
            Net.Poll();
            swNetwork.Stop();
            SystemTimers["C:02 Network Polling"] = swNetwork.Elapsed.TotalMilliseconds;
        }
    }
}
