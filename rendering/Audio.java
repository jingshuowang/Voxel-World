package rendering;

import javax.sound.sampled.*;
import java.io.File;
import java.util.HashMap;
import java.util.Map;

public class Audio {
    private static final Map<String, byte[]> audioCache = new HashMap<>();
    private static final Map<String, AudioFormat> formatCache = new HashMap<>();

    public static void init() {
    }

    public void cleanup() {
        audioCache.clear();
        formatCache.clear();
    }

    public static void preloadSound(String path) {
        if (audioCache.containsKey(path))
            return;
        File file = new File(path);
        if (!file.exists()) {
            System.err.println("[Audio] File not found: " + path + " (will play when added)");
            return;
        }
        try {
            AudioInputStream stream = AudioSystem.getAudioInputStream(file);
            AudioFormat format = stream.getFormat();
            byte[] data = stream.readAllBytes();
            stream.close();
            audioCache.put(path, data);
            formatCache.put(path, format);
            System.out.println("[Audio] Loaded: " + path);
        } catch (Exception e) {
            System.err.println("[Audio] Failed to load: " + path);
            e.printStackTrace();
        }
    }

    public static void playSound(String path, float volume, float pitch) {
        byte[] data = audioCache.get(path);
        AudioFormat format = formatCache.get(path);
        if (data == null || format == null)
            return;
        try {
            Clip clip = AudioSystem.getClip();
            clip.open(format, data, 0, data.length);
            clip.start();
            // Auto-close when done playing
            clip.addLineListener(event -> {
                if (event.getType() == LineEvent.Type.STOP) {
                    clip.close();
                }
            });
        } catch (Exception e) {
            // Silently ignore if too many clips open
        }
    }
}
