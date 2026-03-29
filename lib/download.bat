@echo off
if not exist "javalib" mkdir javalib
cd javalib
echo Downloading LWJGL Core...
curl -O https://repo1.maven.org/maven2/org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3.jar
curl -O https://repo1.maven.org/maven2/org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-windows.jar

echo Downloading LWJGL GLFW...
curl -O https://repo1.maven.org/maven2/org/lwjgl/lwjgl-glfw/3.3.3/lwjgl-glfw-3.3.3.jar
curl -O https://repo1.maven.org/maven2/org/lwjgl/lwjgl-glfw/3.3.3/lwjgl-glfw-3.3.3-natives-windows.jar

echo Downloading LWJGL OpenGL...
curl -O https://repo1.maven.org/maven2/org/lwjgl/lwjgl-opengl/3.3.3/lwjgl-opengl-3.3.3.jar
curl -O https://repo1.maven.org/maven2/org/lwjgl/lwjgl-opengl/3.3.3/lwjgl-opengl-3.3.3-natives-windows.jar

echo Downloading JOML...
curl -O https://repo1.maven.org/maven2/org/joml/joml/1.10.5/joml-1.10.5.jar

echo Downloading STB Image...
curl -O https://repo1.maven.org/maven2/org/lwjgl/lwjgl-stb/3.3.3/lwjgl-stb-3.3.3.jar
curl -O https://repo1.maven.org/maven2/org/lwjgl/lwjgl-stb/3.3.3/lwjgl-stb-3.3.3-natives-windows.jar

echo Done downloading dependencies!
cd ..
