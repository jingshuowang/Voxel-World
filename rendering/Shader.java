package rendering;

import org.joml.Matrix4f;
import org.joml.Vector3f;

import java.nio.file.Files;
import java.nio.file.Paths;

import static org.lwjgl.opengl.GL20.*;

public class Shader {
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
