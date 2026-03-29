package system;
public class AABB {
    public float minX, minY, minZ;
    public float maxX, maxY, maxZ;

    public AABB(float minX, float minY, float minZ, float maxX, float maxY, float maxZ) {
        this.minX = minX;
        this.minY = minY;
        this.minZ = minZ;
        this.maxX = maxX;
        this.maxY = maxY;
        this.maxZ = maxZ;
    }

    public boolean intersects(AABB other) {
        return (this.minX < other.maxX && this.maxX > other.minX) &&
                (this.minY < other.maxY && this.maxY > other.minY) &&
                (this.minZ < other.maxZ && this.maxZ > other.minZ);
    }

    public void move(float dx, float dy, float dz) {
        this.minX += dx;
        this.maxX += dx;
        this.minY += dy;
        this.maxY += dy;
        this.minZ += dz;
        this.maxZ += dz;
    }
}
