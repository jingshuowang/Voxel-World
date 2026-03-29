package system;
public class VoxelData {

        // UV coordinates for the block texture atlas or single textures
        // Right now we just map 0.0 to 1.0 for the whole texture

        public static final float[] VERTICES = {
                        // Front face (+Z)
                        0.0f, 0.0f, 1.0f, 0.0f, 0.0f, // 0 Bottom-Left
                        1.0f, 0.0f, 1.0f, 1.0f, 0.0f, // 1 Bottom-Right
                        1.0f, 1.0f, 1.0f, 1.0f, 1.0f, // 2 Top-Right
                        0.0f, 1.0f, 1.0f, 0.0f, 1.0f, // 3 Top-Left

                        // Back face (-Z)
                        0.0f, 0.0f, 0.0f, 1.0f, 0.0f, // 4
                        1.0f, 0.0f, 0.0f, 0.0f, 0.0f, // 5
                        1.0f, 1.0f, 0.0f, 0.0f, 1.0f, // 6
                        0.0f, 1.0f, 0.0f, 1.0f, 1.0f, // 7

                        // Top face (+Y)
                        0.0f, 1.0f, 0.0f, 0.0f, 1.0f, // 8
                        1.0f, 1.0f, 0.0f, 1.0f, 1.0f, // 9
                        1.0f, 1.0f, 1.0f, 1.0f, 0.0f, // 10
                        0.0f, 1.0f, 1.0f, 0.0f, 0.0f, // 11

                        // Bottom face (-Y)
                        0.0f, 0.0f, 0.0f, 0.0f, 0.0f, // 12
                        1.0f, 0.0f, 0.0f, 1.0f, 0.0f, // 13
                        1.0f, 0.0f, 1.0f, 1.0f, 1.0f, // 14
                        0.0f, 0.0f, 1.0f, 0.0f, 1.0f, // 15

                        // Right face (+X)
                        1.0f, 0.0f, 1.0f, 0.0f, 0.0f, // 16
                        1.0f, 0.0f, 0.0f, 1.0f, 0.0f, // 17
                        1.0f, 1.0f, 0.0f, 1.0f, 1.0f, // 18
                        1.0f, 1.0f, 1.0f, 0.0f, 1.0f, // 19

                        // Left face (-X)
                        0.0f, 0.0f, 0.0f, 0.0f, 0.0f, // 20
                        0.0f, 0.0f, 1.0f, 1.0f, 0.0f, // 21
                        0.0f, 1.0f, 1.0f, 1.0f, 1.0f, // 22
                        0.0f, 1.0f, 0.0f, 0.0f, 1.0f // 23
        };

        public static final int[] INDICES = {
                        0, 1, 2, 2, 3, 0, // Front
                        5, 4, 7, 7, 6, 5, // Back
                        8, 9, 10, 10, 11, 8, // Top
                        12, 13, 14, 14, 15, 12, // Bottom
                        16, 17, 18, 18, 19, 16, // Right
                        20, 21, 22, 22, 23, 20 // Left
        };

}
