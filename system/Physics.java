package system;

import com.bulletphysics.collision.broadphase.DbvtBroadphase;
import com.bulletphysics.collision.dispatch.CollisionDispatcher;
import com.bulletphysics.collision.dispatch.DefaultCollisionConfiguration;
import com.bulletphysics.collision.shapes.BvhTriangleMeshShape;
import com.bulletphysics.collision.shapes.CapsuleShape;
import com.bulletphysics.collision.shapes.TriangleIndexVertexArray;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import com.bulletphysics.dynamics.DiscreteDynamicsWorld;
import com.bulletphysics.dynamics.RigidBody;
import com.bulletphysics.dynamics.RigidBodyConstructionInfo;
import com.bulletphysics.dynamics.constraintsolver.SequentialImpulseConstraintSolver;
import com.bulletphysics.linearmath.DefaultMotionState;
import com.bulletphysics.linearmath.Transform;

import javax.vecmath.Vector3f;
import java.util.HashMap;
import java.util.Map;
import java.util.List;
import java.util.ArrayList;
import java.util.Iterator;
import com.bulletphysics.collision.shapes.BoxShape;

public class Physics {
    public DiscreteDynamicsWorld dynamicsWorld;
    public RigidBody playerBody;
    private final Map<Long, RigidBody> chunkBodies = new HashMap<>();

    public static class Debris {
        public RigidBody body;
        public float life;
        public Debris(RigidBody b, float l) { body=b; life=l; }
    }
    public List<Debris> activeDebris = new ArrayList<>();

    public Physics() {
        DefaultCollisionConfiguration collisionConfiguration = new DefaultCollisionConfiguration();
        CollisionDispatcher dispatcher = new CollisionDispatcher(collisionConfiguration);
        DbvtBroadphase broadphase = new DbvtBroadphase();
        SequentialImpulseConstraintSolver solver = new SequentialImpulseConstraintSolver();

        dynamicsWorld = new DiscreteDynamicsWorld(dispatcher, broadphase, solver, collisionConfiguration);
        dynamicsWorld.setGravity(new Vector3f(0, -Config.GRAVITY, 0));

        initPlayer();
    }

    private void initPlayer() {
        CapsuleShape playerShape = new CapsuleShape(0.4f, 1.8f);
        Transform startTransform = new Transform();
        startTransform.setIdentity();
        startTransform.origin.set(0, 100, 0); // Temporary high spawn

        float mass = 70.0f;
        Vector3f localInertia = new Vector3f(0, 0, 0);
        playerShape.calculateLocalInertia(mass, localInertia);

        DefaultMotionState myMotionState = new DefaultMotionState(startTransform);
        RigidBodyConstructionInfo rbInfo = new RigidBodyConstructionInfo(mass, myMotionState, playerShape, localInertia);
        playerBody = new RigidBody(rbInfo);

        // Prevent angular rotation so camera doesn't fall over
        playerBody.setAngularFactor(0.0f);
        // Keep player body from going to sleep
        playerBody.setActivationState(com.bulletphysics.collision.dispatch.CollisionObject.DISABLE_DEACTIVATION);

        dynamicsWorld.addRigidBody(playerBody);
    }

    public void addChunkMesh(long key, float[] stagedVertices, int[] stagedIndices) {
        if (chunkBodies.containsKey(key)) {
            dynamicsWorld.removeRigidBody(chunkBodies.get(key));
            chunkBodies.remove(key);
        }
        
        if (stagedVertices == null || stagedIndices == null || stagedIndices.length == 0) return;

        int numTriangles = stagedIndices.length / 3;
        int numVertices = stagedVertices.length / 12;

        ByteBuffer verticesBuffer = ByteBuffer.allocateDirect(numVertices * 3 * 4);
        verticesBuffer.order(ByteOrder.nativeOrder());
        for (int i = 0; i < stagedVertices.length; i += 12) {
            verticesBuffer.putFloat(stagedVertices[i]);
            verticesBuffer.putFloat(stagedVertices[i + 1]);
            verticesBuffer.putFloat(stagedVertices[i + 2]);
        }
        verticesBuffer.flip();

        ByteBuffer indicesBuffer = ByteBuffer.allocateDirect(stagedIndices.length * 4);
        indicesBuffer.order(ByteOrder.nativeOrder());
        // Since we extracted only the 3 position floats from the 12 staged floats,
        // our new vertex stride is 3, so we must adjust the index values!
        for (int index : stagedIndices) {
            indicesBuffer.putInt(index);
        }
        indicesBuffer.flip();

        TriangleIndexVertexArray indexVertexArray = new TriangleIndexVertexArray(
                numTriangles, indicesBuffer, 3 * 4,
                numVertices, verticesBuffer, 3 * 4);

        BvhTriangleMeshShape shape = new BvhTriangleMeshShape(indexVertexArray, true);

        Transform transform = new Transform();
        transform.setIdentity();

        RigidBodyConstructionInfo rbInfo = new RigidBodyConstructionInfo(0.0f, new DefaultMotionState(transform), shape, new Vector3f(0, 0, 0));
        RigidBody body = new RigidBody(rbInfo);
        body.setFriction(1.0f); // Ground grip
        
        dynamicsWorld.addRigidBody(body);
        chunkBodies.put(key, body);
    }

    public void removeChunkMesh(long key) {
        RigidBody body = chunkBodies.remove(key);
        if (body != null) {
            dynamicsWorld.removeRigidBody(body);
        }
    }

    public void stepSimulation(float dt) {
        dynamicsWorld.stepSimulation(dt, 10);
    }
}
