using System;
using System.Numerics;

namespace Voxel.System
{
    /// <summary>
    /// README Step 4 physics: F_drag = -b * velocity (linear drag, not quadratic).
    /// README Step 2 confirmed: F = ma, forces are vectors.
    ///
    /// Tick-based integration (no dt multiplication):
    ///   velocity  += acceleration       (all acceleration transfers instantly per tick)
    ///   position  += velocity           (all velocity transfers instantly per tick)
    ///
    /// At 144 Hz, each tick is 1/144 s ≈ 6.94 ms.
    /// Terminal velocity = F_applied / b  (when drag force equals applied force).
    /// When key released: F_applied = 0, drag continues decaying velocity naturally.
    /// </summary>
    public class Physics
    {
        public Vector3 Position     = Vector3.Zero;
        public Vector3 Velocity     = Vector3.Zero;
        public Vector3 Acceleration = Vector3.Zero;

        // Exposed for HUD display
        public Vector3 AppliedForce = Vector3.Zero;
        public Vector3 DragForce    = Vector3.Zero;
        public Vector3 TotalForce   = Vector3.Zero;
        public Vector3 AppliedAcceleration = Vector3.Zero;
        public Vector3 DragAcceleration    = Vector3.Zero;

        // README: "player is 2^13 units in mass"
        public float Mass = 8192.0f;

        // README Step 4: drag coefficient 'b' — linear: F_drag = -b * v
        // Terminal velocity (walk) = F_walk / b = 5000 / 2500 = 2.0 units/tick
        // At 144 Hz that's 288 world-units/sec. Sprint = 8 units/tick = 1152/sec.
        // Convergence rate per tick = (1 - b/m) = (1 - 2500/8192) ≈ 0.695
        // Reaches ~90% terminal velocity in ~7 ticks (0.05 s). Very snappy.
        public float DragCoefficient = 2500.0f;

        public void Update(Vector3 moveDirection, bool isSprinting, float _dt)
        {
            // Safety: reset on NaN/Inf
            if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z)) {
                Position = Vector3.Zero;
                Velocity = Vector3.Zero;
            }

            // 1. Applied input force (key-driven)
            float forceMag = isSprinting ? 20000.0f : 5000.0f;
            AppliedForce = moveDirection * forceMag;

            // 2. Drag: F_drag = -b * v  (README Step 4, linear, opposes velocity)
            DragForce = -DragCoefficient * Velocity;

            // 3. Net force → acceleration  (F = ma)
            TotalForce           = AppliedForce + DragForce;
            Acceleration         = TotalForce   / Mass;
            AppliedAcceleration  = AppliedForce / Mass;
            DragAcceleration     = DragForce    / Mass;

            // 4. Tick-based integration — ALL acceleration transfers to velocity this tick,
            //    ALL velocity transfers to position this tick (no dt multiplication).
            Velocity += Acceleration;
            Position += Velocity;

            // Safety: kill NaN velocity
            if (!float.IsFinite(Velocity.X) || !float.IsFinite(Velocity.Y) || !float.IsFinite(Velocity.Z))
                Velocity = Vector3.Zero;
        }
    }
}
