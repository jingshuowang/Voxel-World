using System.Numerics;

namespace Voxel.System {
    public class Player : Entity {
        public float Speed = 25.0f;
        public float JumpForce = 9.0f;
        public float Friction = 0.90f;
        public float AirResistance = 0.98f;

        public Player() {
            Position = new Vector3(64, 100, 64);
            Size = new Vector3(0.6f, 1.8f, 0.6f);
        }
    }
}
