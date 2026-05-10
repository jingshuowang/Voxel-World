using Silk.NET.Input;
using System;
using System.Numerics;

namespace Voxel.Rendering
{
    public class Detection
    {
        public Vector3 MoveDirection = Vector3.Zero;
        public bool IsSprinting = false;

        public float Pitch = 0;
        public float Yaw = -MathF.PI / 2; // Face forward initially

        public Vector3 Forward => new Vector3(
            MathF.Cos(Pitch) * MathF.Cos(Yaw),
            MathF.Sin(Pitch),
            MathF.Cos(Pitch) * MathF.Sin(Yaw)
        );

        public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));
        public Vector3 Up => Vector3.Normalize(Vector3.Cross(Right, Forward));

        private Vector2 _lastMousePos;
        private bool _firstMove = true;

        public void Update(IKeyboard kb, IMouse mouse, float dt)
        {
            // Camera Rotation
            if (mouse.Cursor.CursorMode == CursorMode.Raw)
            {
                var pos = mouse.Position;
                if (_firstMove)
                {
                    _lastMousePos = pos;
                    _firstMove = false;
                }

                float xOffset = pos.X - _lastMousePos.X;
                float yOffset = _lastMousePos.Y - pos.Y;
                _lastMousePos = pos;

                float sensitivity = 0.002f;
                Yaw += xOffset * sensitivity;
                Pitch += yOffset * sensitivity;
                Pitch = Math.Clamp(Pitch, -89.0f * MathF.PI / 180.0f, 89.0f * MathF.PI / 180.0f);
            }
            else
            {
                _firstMove = true;
            }

            // Read Movement Input
            IsSprinting = kb.IsKeyPressed(Key.ControlLeft);
            Vector3 move = Vector3.Zero;

            if (kb.IsKeyPressed(Key.W)) move += Forward;
            if (kb.IsKeyPressed(Key.S)) move -= Forward;
            if (kb.IsKeyPressed(Key.A)) move -= Right;
            if (kb.IsKeyPressed(Key.D)) move += Right;
            if (kb.IsKeyPressed(Key.Space)) move += Vector3.UnitY;
            if (kb.IsKeyPressed(Key.ShiftLeft)) move -= Vector3.UnitY;

            if (move.LengthSquared() > 0) {
                MoveDirection = Vector3.Normalize(move);
            } else {
                MoveDirection = Vector3.Zero;
            }
        }
    }
}
