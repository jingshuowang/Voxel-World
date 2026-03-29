package rendering;

import org.lwjgl.openal.*;
import org.lwjgl.stb.STBVorbis;
import org.lwjgl.system.MemoryStack;
import java.nio.*;
import java.util.HashMap;
import java.util.Map;
import static org.lwjgl.openal.AL10.*;
import static org.lwjgl.openal.ALC10.*;

public class AudioPlayer {
    private static long device;
    private static long context;
    private static boolean initialized = false;

    private static Map<String, int[]> soundPools = new HashMap<>();
    private static Map<String, Integer> poolIndex = new HashMap<>();

    public static void init() {
        if (initialized) return;
        String defaultDevice = alcGetString(0, ALC_DEFAULT_DEVICE_SPECIFIER);
        device = alcOpenDevice(defaultDevice);
        if (device == 0) return;
        context = alcCreateContext(device, (IntBuffer) null);
        alcMakeContextCurrent(context);
        AL.createCapabilities(ALC.createCapabilities(device));
        initialized = true;
    }

    public static void preloadSound(String filePath) {
        if (!initialized) init();
        int poolSize = 4;
        int[] sources = new int[poolSize];
        for (int i = 0; i < poolSize; i++) {
            try (MemoryStack stack = MemoryStack.stackPush()) {
                IntBuffer channelsBuf = stack.mallocInt(1);
                IntBuffer sampleRateBuf = stack.mallocInt(1);
                ShortBuffer rawAudio = STBVorbis.stb_vorbis_decode_filename(filePath, channelsBuf, sampleRateBuf);
                if (rawAudio == null) continue;

                int channels = channelsBuf.get(0);
                int sampleRate = sampleRateBuf.get(0);
                int format = (channels == 1) ? AL_FORMAT_MONO16 : AL_FORMAT_STEREO16;

                int buffer = alGenBuffers();
                alBufferData(buffer, format, rawAudio, sampleRate);
                int source = alGenSources();
                alSourcei(source, AL_BUFFER, buffer);
                sources[i] = source;
            }
        }
        soundPools.put(filePath, sources);
        poolIndex.put(filePath, 0);
    }

    public static void playSound(String filePath, float volume, float pitch) {
        if (!initialized || !soundPools.containsKey(filePath)) return;
        int[] pool = soundPools.get(filePath);
        int idx = poolIndex.get(filePath);
        int source = pool[idx % pool.length];
        poolIndex.put(filePath, idx + 1);
        alSourcef(source, AL_GAIN, volume);
        alSourcef(source, AL_PITCH, pitch);
        alSourcePlay(source);
    }

    public static void cleanup() {
        if (!initialized) return;
        for (int[] pool : soundPools.values()) {
            for (int s : pool) {
                alDeleteSources(s);
            }
        }
        alcDestroyContext(context);
        alcCloseDevice(device);
    }
}
