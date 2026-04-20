package rendering;

import java.nio.ByteBuffer;
import java.nio.IntBuffer;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.util.HashMap;
import java.util.Map;
import org.joml.Matrix4f;
import org.joml.Vector3f;
import org.lwjgl.system.MemoryStack;
import static org.lwjgl.opengl.GL11.*;
import static org.lwjgl.opengl.GL20.*;
import static org.lwjgl.opengl.GL30.glGenerateMipmap;
import static org.lwjgl.stb.STBImage.*;

public class Graphics {
    // Top level rendering code


    // --- Nested from rendering/Shader.java ---
    
    
    
    
    public static class Shader {
        private int programId;
        private int vertexShaderId;
        private int fragmentShaderId;
    
        public Shader() {
            programId = glCreateProgram();
            if (programId == 0) {
                throw new RuntimeException("Could not create Shader");
            }
        }
    
        public void createVertexShaderFromFile(String filePath) throws Exception {
            String code = new String(Files.readAllBytes(Paths.get(filePath)));
            vertexShaderId = createShader(code, GL_VERTEX_SHADER);
        }
    
        public void createFragmentShaderFromFile(String filePath) throws Exception {
            String code = new String(Files.readAllBytes(Paths.get(filePath)));
            fragmentShaderId = createShader(code, GL_FRAGMENT_SHADER);
        }
    
        protected int createShader(String shaderCode, int shaderType) throws Exception {
            int shaderId = glCreateShader(shaderType);
            if (shaderId == 0) {
                throw new Exception("Error creating shader. Type: " + shaderType);
            }
            glShaderSource(shaderId, shaderCode);
            glCompileShader(shaderId);
            if (glGetShaderi(shaderId, GL_COMPILE_STATUS) == 0) {
                throw new Exception("Error compiling Shader code: " + glGetShaderInfoLog(shaderId, 1024));
            }
            glAttachShader(programId, shaderId);
            return shaderId;
        }
    
        public void link() throws Exception {
            glLinkProgram(programId);
            if (glGetProgrami(programId, GL_LINK_STATUS) == 0) {
                throw new Exception("Error linking Shader code: " + glGetProgramInfoLog(programId, 1024));
            }
            if (vertexShaderId != 0)
                glDetachShader(programId, vertexShaderId);
            if (fragmentShaderId != 0)
                glDetachShader(programId, fragmentShaderId);
            glValidateProgram(programId);
        }
    
        public void bind() {
            glUseProgram(programId);
        }
    
        public void unbind() {
            glUseProgram(0);
        }
    
        public void setUniform(String name, int value) {
            glUniform1i(glGetUniformLocation(programId, name), value);
        }
    
        public void setUniform(String name, float value) {
            glUniform1f(glGetUniformLocation(programId, name), value);
        }
    
        public void setUniform(String name, Vector3f value) {
            glUniform3f(glGetUniformLocation(programId, name), value.x, value.y, value.z);
        }
    
        public void setUniform(String name, Matrix4f value) {
            float[] fb = new float[16];
            value.get(fb);
            glUniformMatrix4fv(glGetUniformLocation(programId, name), false, fb);
        }
    
        public void cleanup() {
            unbind();
            if (programId != 0)
                glDeleteProgram(programId);
        }
    }
    
    // --- Nested from rendering/Texture.java ---
    
    
    public static class Texture {
        private final int id;
    
        public Texture(String filePath) {
            try (MemoryStack stack = MemoryStack.stackPush()) {
                IntBuffer w = stack.mallocInt(1);
                IntBuffer h = stack.mallocInt(1);
                IntBuffer channels = stack.mallocInt(1);
    
                stbi_set_flip_vertically_on_load(true);
    
                ByteBuffer image = stbi_load(filePath, w, h, channels, 4);
                if (image == null) {
                    throw new RuntimeException("Failed to load texture file: " + filePath + "\n" + stbi_failure_reason());
                }
    
                id = glGenTextures();
                bind();
    
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);
    
                glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, w.get(), h.get(), 0, GL_RGBA, GL_UNSIGNED_BYTE, image);
                glGenerateMipmap(GL_TEXTURE_2D);
    
                stbi_image_free(image);
            }
        }
    
        public void bind() { glBindTexture(GL_TEXTURE_2D, id); }
    
        public void cleanup() { glDeleteTextures(id); }
    }
    
    // --- Nested from rendering/ImageSelector.java ---
    
    
    public static class ImageSelector {
        private final Map<String, Texture> textures = new HashMap<>();
    
        public void loadTexture(String name, String path) {
            if (!textures.containsKey(name)) {
                textures.put(name, new Texture(path));
            }
        }
    
        public Texture getTexture(String name) {
            return textures.get(name);
        }
    
        public void bindTexture(String name) {
            Texture tex = textures.get(name);
            if (tex != null) {
                tex.bind();
            }
        }
    
        public void cleanup() {
            for (Texture tex : textures.values()) {
                tex.cleanup();
            }
            textures.clear();
        }
    }
    
}
