package rendering;

import java.util.HashMap;
import java.util.Map;

public class ImageSelector {
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
