const fs = require('fs');
const https = require('https');
const path = require('path');

const javalib = path.join(__dirname, 'javalib');
if (!fs.existsSync(javalib)) {
    fs.mkdirSync(javalib);
}

const urls = [
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3.jar",
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-windows.jar",
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl-glfw/3.3.3/lwjgl-glfw-3.3.3.jar",
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl-glfw/3.3.3/lwjgl-glfw-3.3.3-natives-windows.jar",
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl-opengl/3.3.3/lwjgl-opengl-3.3.3.jar",
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl-opengl/3.3.3/lwjgl-opengl-3.3.3-natives-windows.jar",
    "https://repo1.maven.org/maven2/org/joml/joml/1.10.5/joml-1.10.5.jar",
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl-stb/3.3.3/lwjgl-stb-3.3.3.jar",
    "https://repo1.maven.org/maven2/org/lwjgl/lwjgl-stb/3.3.3/lwjgl-stb-3.3.3-natives-windows.jar"
];

async function download(url, dest) {
    return new Promise((resolve, reject) => {
        const file = fs.createWriteStream(dest);
        https.get(url, (response) => {
            if (response.statusCode >= 300 && response.statusCode < 400 && response.headers.location) {
                return download(response.headers.location, dest).then(resolve).catch(reject);
            }
            response.pipe(file);
            file.on('finish', () => {
                file.close(resolve);
            });
        }).on('error', (err) => {
            fs.unlink(dest, () => reject(err));
        });
    });
}

(async () => {
    for (const url of urls) {
        const filename = path.basename(url);
        const dest = path.join(javalib, filename);
        console.log(`Downloading ${filename}...`);
        try {
            await download(url, dest);
        } catch (e) {
            console.error(e);
        }
    }
    console.log("Done!");
})();
