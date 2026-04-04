import sys
with open('system/Main.java', 'r', encoding='ascii') as f:
    text = f.read()

rep1_old = '''// 2. Render
        int[] width = new int[1], height = new int[1];
        glfwGetFramebufferSize(window, width, height);
        glViewport(0, 0, width[0], height[0]);
        glClearColor(skyR, skyG, skyB, 1.0f);
        glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

        float aspect = width[0] == 0 ? 1 : (float) width[0] / (float) height[0];'''

rep1_new = '''// 2. Render
        int[] width = new int[1], height = new int[1];
        glfwGetFramebufferSize(window, width, height);
        glViewport(0, 0, width[0], height[0]);

        byte camBlock = getBlockGlobal((int)Math.floor(camera.position.x), (int)Math.floor(camera.position.y), (int)Math.floor(camera.position.z));
        boolean blind = (camBlock != 0);

        if (blind) {
            glClearColor(0.0f, 0.0f, 0.0f, 1.0f);
        } else {
            glClearColor(skyR, skyG, skyB, 1.0f);
        }
        glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

        float aspect = width[0] == 0 ? 1 : (float) width[0] / (float) height[0];

        if (!blind) {'''

rep2_old = '''        // --- Draw Chunks (flat color, no lighting) ---
        shader.bind();
        shader.setUniform("projectionMatrix", camera.getProjectionMatrix(aspect));
        shader.setUniform("viewMatrix", camera.getViewMatrix());
        shader.setUniform("modelMatrix", new org.joml.Matrix4f().identity());

        for (Chunk chunk : worldChunks.values()) {
            chunk.render();
        }

        shader.unbind();'''

rep2_new = '''        // --- Draw Chunks (flat color, no lighting) ---
        shader.bind();
        shader.setUniform("projectionMatrix", camera.getProjectionMatrix(aspect));
        shader.setUniform("viewMatrix", camera.getViewMatrix());
        shader.setUniform("modelMatrix", new org.joml.Matrix4f().identity());

        org.joml.Matrix4f viewProj = new org.joml.Matrix4f(camera.getProjectionMatrix(aspect)).mul(camera.getViewMatrix());
        org.joml.FrustumIntersection frustum = new org.joml.FrustumIntersection(viewProj);

        for (Chunk chunk : worldChunks.values()) {
            float cx = chunk.chunkX * Chunk.CHUNK_SIZE;
            float cy = chunk.chunkY * Chunk.CHUNK_SIZE;
            float cz = chunk.chunkZ * Chunk.CHUNK_SIZE;
            float s = Chunk.CHUNK_SIZE;
            if (frustum.testAab(cx, cy, cz, cx + s, cy + s, cz + s)) {
                chunk.render();
            }
        }

        shader.unbind();'''

rep3_old = '''        drawChunkGrid(aspect);

        // Draw Crosshair using simple Ortho compatibility pass'''

rep3_new = '''        } // end if (!blind)

        drawChunkGrid(aspect);

        // Draw Crosshair using simple Ortho compatibility pass'''

# Normalize line endings
text = text.replace('\r\n', '\n')
rep1_old = rep1_old.replace('\r\n', '\n')
rep2_old = rep2_old.replace('\r\n', '\n')
rep3_old = rep3_old.replace('\r\n', '\n')
rep1_new = rep1_new.replace('\r\n', '\n')
rep2_new = rep2_new.replace('\r\n', '\n')
rep3_new = rep3_new.replace('\r\n', '\n')
import re
# Do replacements with whitespace flexibility
def flex_replace(old_str, new_str, txt):
    escaped = re.escape(old_str)
    escaped = re.sub(r'\\[ \t]+', r'[ \t]+', escaped)
    escaped = re.sub(r'\\n', r'\\n[ \t]*', escaped)
    return re.sub(escaped, new_str, txt)

text = flex_replace(rep1_old, rep1_new, text)
text = flex_replace(rep2_old, rep2_new, text)
text = flex_replace(rep3_old, rep3_new, text)

with open('system/Main.java', 'w', encoding='ascii') as f:
    f.write(text)
print("Replacements done!")
