import urllib.request
import os

os.makedirs('javalib', exist_ok=True)

urls = [
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3.jar",
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-windows.jar",
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl-glfw/3.3.3/lwjgl-glfw-3.3.3.jar",
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl-glfw/3.3.3/lwjgl-glfw-3.3.3-natives-windows.jar",
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl-opengl/3.3.3/lwjgl-opengl-3.3.3.jar",
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl-opengl/3.3.3/lwjgl-opengl-3.3.3-natives-windows.jar",
    "https://repo1.maven.org/maven2/org/joml/joml/1.10.5/joml-1.10.5.jar",
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl-stb/3.3.3/lwjgl-stb-3.3.3.jar",
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl-stb/3.3.3/lwjgl-stb-3.3.3-natives-windows.jar"
]

for url in urls:
    filename = url.split('/')[-1]
    filepath = os.path.join('javalib', filename)
    print(f"Downloading {filename}...")
    try:
        urllib.request.urlretrieve(url, filepath)
    except Exception as e:
        print(f"Failed to download {filename}: {e}")

print("All downloads complete!")
