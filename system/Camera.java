package system;

import org.joml.Matrix4f;
import org.joml.Vector3f;

public class Camera {
    public final Vector3f position;
    public float pitch; // up/down
    public float yaw; // left/right

    private float fov = (float) Math.toRadians(100.0f);
    private float zNear = 0.1f;
    private float zFar = 30000.f;

    public Camera() {
        position = new Vector3f(0, 50, 0);
        pitch = 0;
        yaw = 0;
    }

    public Matrix4f getViewMatrix() {
        Matrix4f viewMatrix = new Matrix4f();
        viewMatrix.identity();
        viewMatrix.rotate((float) Math.toRadians(pitch), new Vector3f(1, 0, 0));
        viewMatrix.rotate((float) Math.toRadians(yaw), new Vector3f(0, 1, 0));
        viewMatrix.translate(-position.x, -position.y, -position.z);
        return viewMatrix;
    }

    public Matrix4f getProjectionMatrix(float aspect) {
        Matrix4f projectionMatrix = new Matrix4f();
        projectionMatrix.identity();
        // Reversed-Z: swap near and far for better depth precision
        projectionMatrix.perspective(fov, aspect, zFar, zNear);
        return projectionMatrix;
    }

    public void movePosition(float offsetX, float offsetY, float offsetZ) {
        if (offsetZ != 0) {
            position.x += (float) Math.sin(Math.toRadians(yaw)) * -1.0f * offsetZ;
            position.z += (float) Math.cos(Math.toRadians(yaw)) * offsetZ;
        }
        if (offsetX != 0) {
            position.x += (float) Math.sin(Math.toRadians(yaw - 90)) * -1.0f * offsetX;
            position.z += (float) Math.cos(Math.toRadians(yaw - 90)) * offsetX;
        }
        position.y += offsetY;
    }

    public void setFov(float fovDegrees) {
        this.fov = (float) Math.toRadians(fovDegrees);
    }
}
