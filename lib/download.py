"""Download LWJGL/JOML jars into the workspace lib folder."""

import urllib.request
import urllib.error
from urllib.parse import urlsplit
import os

LWJGL_VERSION = "3.3.6"
MAVEN_BASE = "https://repo1.maven.org/maven2"
LIB_DIR = os.path.dirname(os.path.abspath(__file__))

# Ensure downloads go to the same folder used by run_java.bat classpath: lib/*
os.chdir(LIB_DIR)

# Remove old LWJGL jars first so Java doesn't mix versions on classpath
for name in os.listdir(LIB_DIR):
    if name.startswith("lwjgl-") and name.endswith(".jar"):
        try:
            os.remove(os.path.join(LIB_DIR, name))
        except OSError:
            pass


def lwjgl_url(module: str, artifact: str) -> str:
    """Build a Maven Central URL for a LWJGL artifact."""
    return (
        f"{MAVEN_BASE}/org/lwjgl/{module}/{LWJGL_VERSION}/"
        f"{artifact}-{LWJGL_VERSION}.jar"
    )


urls = [
    lwjgl_url("lwjgl", "lwjgl"),
    lwjgl_url("lwjgl", "lwjgl-natives-windows"),
    lwjgl_url("lwjgl-glfw", "lwjgl-glfw"),
    lwjgl_url("lwjgl-glfw", "lwjgl-glfw-natives-windows"),
    lwjgl_url("lwjgl-opengl", "lwjgl-opengl"),
    lwjgl_url("lwjgl-opengl", "lwjgl-opengl-natives-windows"),
    f"{MAVEN_BASE}/org/joml/joml/1.10.5/joml-1.10.5.jar",
    lwjgl_url("lwjgl-stb", "lwjgl-stb"),
    lwjgl_url("lwjgl-stb", "lwjgl-stb-natives-windows"),
]

for url in urls:
    filename = os.path.basename(urlsplit(url).path)
    filepath = os.path.join(LIB_DIR, filename)
    print(f"Downloading {filename}...")
    try:
        urllib.request.urlretrieve(url, filepath)
    except (urllib.error.URLError, OSError) as e:
        print(f"Failed to download {filename}: {e}")

print("All downloads complete!")
