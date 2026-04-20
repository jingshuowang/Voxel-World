import * as THREE from 'three';
import { PointerLockControls } from 'three/addons/controls/PointerLockControls.js';

// ---- CONFIGURATION ----
const CHUNK_SIZE = 16;
const TILE_SIZE = 2; // Size of each block
const RENDER_DISTANCE = 12; // Far horizon without lag!
const GRAVITY = 32.0; // 32 m/s^2 per user request
const JUMP_VELOCITY = 10.0; // Math yields ~1.25 block jump height
const SPEED = 6.0; // Base walking speed
const SPRINT_SPEED = 7.8; // Sprinting speed

// Set up Simplex Noise (loaded globally via script tag in index.html)
const simplex = new SimplexNoise();
const NOISE_SCALE = 50.0;
const HEIGHT_SCALE = 20.0; // Max height of hills

// ---- SETUP ----
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x87CEEB); // Sky blue
scene.fog = new THREE.Fog(0x87CEEB, 10, (RENDER_DISTANCE * CHUNK_SIZE * TILE_SIZE) * 1.25);

let cameraFOV = 100;
const camera = new THREE.PerspectiveCamera(cameraFOV, window.innerWidth / window.innerHeight, 0.1, 1000);
const renderer = new THREE.WebGLRenderer({ antialias: false }); // Disable antialias for sharp pixel look
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(window.devicePixelRatio);
// Important for sharp pixel textures
renderer.outputColorSpace = THREE.SRGBColorSpace;
document.body.appendChild(renderer.domElement);

// ---- LIGHTING ----
const ambientLight = new THREE.AmbientLight(0xffffff, 0.6); // White light
scene.add(ambientLight);
const dirLight = new THREE.DirectionalLight(0xffffff, 0.8); // White light
dirLight.position.set(100, 200, 50);
scene.add(dirLight);

// ---- CONTROLS & PLAYER ----
const controls = new PointerLockControls(camera, document.body);
const instructions = document.getElementById('instructions');

instructions.addEventListener('click', () => {
    controls.lock();
});
controls.addEventListener('lock', () => {
    instructions.style.display = 'none';
});
controls.addEventListener('unlock', () => {
    instructions.style.display = 'flex';
});

scene.add(controls.getObject());

let moveForward = false;
let moveBackward = false;
let moveLeft = false;
let moveRight = false;
let isJumping = false; // Spacebar held
let isSprinting = false; // Ctrl held
let isCrouching = false; // Shift held

let isGrounded = false;

// Global Raycast Tracking (for Block Place/Break)
let globalHitPos = null;
let globalPrevPos = null;
let selectedBlockType = 'grass';

let velocity = new THREE.Vector3();
let direction = new THREE.Vector3();

const onKeyDown = (event) => {
    // Prevent browser hotkeys from triggering while sprinting (e.g., Ctrl+E to search)
    if (event.code === 'ControlLeft' || event.code === 'ControlRight' || event.code === 'ShiftLeft') {
        event.preventDefault();
    }

    switch (event.code) {
        case 'ControlLeft':
        case 'ControlRight':
            isSprinting = true;
            break;
        case 'ShiftLeft':
        case 'ShiftRight':
            isCrouching = true;
            break;
        case 'ArrowUp':
        case 'KeyW': moveForward = true; break;
        case 'ArrowLeft':
        case 'KeyA': moveLeft = true; break;
        case 'ArrowDown':
        case 'KeyS': moveBackward = true; break;
        case 'ArrowRight':
        case 'KeyD': moveRight = true; break;
        case 'Space':
            isJumping = true;
            break;
        case 'Digit1':
            selectedBlockType = 'grass';
            document.getElementById('hotbar').innerHTML = '[1] Grass (Selected) &nbsp;|&nbsp; [2] Dirt &nbsp;|&nbsp; [3] Ladder';
            break;
        case 'Digit2':
            selectedBlockType = 'dirt';
            document.getElementById('hotbar').innerHTML = '[1] Grass &nbsp;|&nbsp; [2] Dirt (Selected) &nbsp;|&nbsp; [3] Ladder';
            break;
        case 'Digit3':
            selectedBlockType = 'ladder';
            document.getElementById('hotbar').innerHTML = '[1] Grass &nbsp;|&nbsp; [2] Dirt &nbsp;|&nbsp; [3] Ladder (Selected)';
            break;
    }
};

const onKeyUp = (event) => {
    switch (event.code) {
        case 'ControlLeft':
        case 'ControlRight':
            isSprinting = false;
            break;
        case 'ShiftLeft':
        case 'ShiftRight':
            isCrouching = false;
            break;
        case 'ArrowUp':
        case 'KeyW': moveForward = false; break;
        case 'ArrowLeft':
        case 'KeyA': moveLeft = false; break;
        case 'ArrowDown':
        case 'KeyS': moveBackward = false; break;
        case 'ArrowRight':
        case 'KeyD': moveRight = false; break;
        case 'Space':
            isJumping = false;
            break;
    }
};
document.addEventListener('keydown', onKeyDown);
document.addEventListener('keyup', onKeyUp);

// ---- TEXTURE LOAD & VOXEL SETUP ----
const textureLoader = new THREE.TextureLoader();

const grassTex = textureLoader.load('Asset/image/grass3d.png');
grassTex.magFilter = THREE.NearestFilter;
grassTex.minFilter = THREE.NearestFilter;
grassTex.colorSpace = THREE.SRGBColorSpace;

const dirtTex = textureLoader.load('Asset/image/dirt3d.png');
dirtTex.magFilter = THREE.NearestFilter;
dirtTex.minFilter = THREE.NearestFilter;
dirtTex.colorSpace = THREE.SRGBColorSpace;

const ladderTex = textureLoader.load('Asset/image/ladder3d.png');
ladderTex.magFilter = THREE.NearestFilter;
ladderTex.minFilter = THREE.NearestFilter;
ladderTex.colorSpace = THREE.SRGBColorSpace;

// Use InstancedMesh for extreme performance rendering cubes
const boxGeometry = new THREE.BoxGeometry(TILE_SIZE, TILE_SIZE, TILE_SIZE);
// Shift the plane to the front face of where a box would be

const material = new THREE.MeshLambertMaterial({ map: grassTex });
const dirtMaterial = new THREE.MeshLambertMaterial({ map: dirtTex });
const ladderMaterial = new THREE.MeshLambertMaterial({
    map: ladderTex,
    transparent: true,
    alphaTest: 0.5,
    side: THREE.DoubleSide
});

// ---- AUDIO SETUP ----
const blockSounds = [
    new Audio('Asset/sound/block1.ogg'),
    new Audio('Asset/sound/block2.ogg'),
    new Audio('Asset/sound/block3.ogg'),
    new Audio('Asset/sound/block4.ogg')
];
// Preload them to avoid delay
blockSounds.forEach(audio => {
    audio.preload = 'auto';
    audio.volume = 0.5;
});

// Highlight selector wireframe box
const highlightGeo = new THREE.EdgesGeometry(new THREE.BoxGeometry(TILE_SIZE + 0.05, TILE_SIZE + 0.05, TILE_SIZE + 0.05));
const highlightMat = new THREE.LineBasicMaterial({ color: 0xffffff, linewidth: 2 });
const highlightBox = new THREE.LineSegments(highlightGeo, highlightMat);
highlightBox.visible = false;
scene.add(highlightBox);

// Flipped world configuration
const CEILING_BASE = 100; // Elevation in blocks where the flipped world begins

// Math utility to cleanly snap logic coordinate to physical position
const userHeights = new Map();
function getElevation(wx, wz) {
    const key = `${wx},${wz}`;
    if (userHeights.has(key)) return userHeights.get(key);

    // Generate noise value [-1, 1], scale it, and snap to integer steps (Minecraft style blocks)
    let n = simplex.noise2D(wx / NOISE_SCALE, wz / NOISE_SCALE);
    let h = Math.floor(n * HEIGHT_SCALE);
    userHeights.set(key, h);
    return h;
}

const ceilHeights = new Map();
function getCeilingElevation(wx, wz) {
    const key = `${wx},${wz}`;
    if (ceilHeights.has(key)) return ceilHeights.get(key);
    
    let n = simplex.noise2D(wx / NOISE_SCALE, wz / NOISE_SCALE);
    // Ceiling planet has highly flattened slopes (0.2x)
    let hCeil = Math.floor(n * (HEIGHT_SCALE * 0.2));
    // The visual blocks construct downwards from CEILING_BASE
    let h = CEILING_BASE - hCeil;
    ceilHeights.set(key, h);
    return h;
}

const chunks = new Map();
const addedBlocks = new Map();   // key: "x,y,z", value: {type: 'grass'|'dirt'}
const removedBlocks = new Set(); // key: "x,y,z"

// Helper to check if a specific 3D coordinate contains a solid block
function isBlockSolid(x, y, z) {
    const key = `${x},${y},${z}`;
    if (removedBlocks.has(key)) return false;
    if (addedBlocks.has(key)) {
        if (addedBlocks.get(key).type === 'ladder') return false; // Ladders are NOT solid
        return true;
    }

    // Check natural generation
    if (y <= getElevation(x, z) && y >= -30) return true;

    // Debug Giant Walls for collision testing
    if (x === -5 && y >= 40 && y <= 65 && z >= -10 && z <= 10) return true; // X wall
    if (z === -5 && y >= 40 && y <= 65 && x >= -10 && x <= 10) return true; // Z wall

    const ceilY = getCeilingElevation(x, z);
    if (y >= ceilY && y <= ceilY + 15) {
        // Check cave noise
        let caveNoise = simplex.noise3D(x / 25, y / 25, z / 25);
        if (Math.abs(caveNoise) > 0.12) return true;
    }
    return false;
}

let totalFacesRendered = 0; // Tracking for UI

function generateChunk(cx, cz) {
    const startX = cx * CHUNK_SIZE;
    const startZ = cz * CHUNK_SIZE;

    // First scan to determine how many visible blocks are in the chunk
    // By only instancing blocks that are NOT buried, we drastically reduce render load (user request)
    const blockData = [];
    const ceilingData = [];
    const ladderData = [];

    for (let x = 0; x < CHUNK_SIZE; x++) {
        for (let z = 0; z < CHUNK_SIZE; z++) {
            let wx = startX + x;
            let wz = startZ + z;

            let h = getElevation(wx, wz);

            // Ground planet column (thicker, down to -30 bedrock)
            for (let y = -30; y <= h; y++) {
                if (removedBlocks.has(`${wx},${y},${wz}`)) continue;
                let isTop = (y === h);
                let exposed = isTop || y === -30 ||
                    !isBlockSolid(wx + 1, y, wz) ||
                    !isBlockSolid(wx - 1, y, wz) ||
                    !isBlockSolid(wx, y + 1, wz) ||
                    !isBlockSolid(wx, y - 1, wz) ||
                    !isBlockSolid(wx, y, wz + 1) ||
                    !isBlockSolid(wx, y, wz - 1);
                
                if (exposed) {
                    if (isTop && !isBlockSolid(wx, y + 1, wz)) blockData.push({ x: wx, y: y, z: wz });
                    else ceilingData.push({ x: wx, y: y, z: wz });
                }
            }

            // Ceiling planet crust lowest physical point
            let cyLowest = getCeilingElevation(wx, wz);

            // Ceiling planet column (Extends 15 layers UP into the ceiling base structure)
            for (let cy = cyLowest; cy <= cyLowest + 15; cy++) {
                if (removedBlocks.has(`${wx},${cy},${wz}`)) continue;
                // Add 3D Cave generation to the Ceiling planet using Noise
                let caveNoise = simplex.noise3D(wx / 25, cy / 25, wz / 25);
                if (Math.abs(caveNoise) > 0.12) {
                    let exposed = cy === cyLowest || cy === cyLowest + 15 ||
                        !isBlockSolid(wx + 1, cy, wz) ||
                        !isBlockSolid(wx - 1, cy, wz) ||
                        !isBlockSolid(wx, cy + 1, wz) ||
                        !isBlockSolid(wx, cy - 1, wz) ||
                        !isBlockSolid(wx, cy, wz + 1) ||
                        !isBlockSolid(wx, cy, wz - 1);
                    if (exposed) ceilingData.push({ x: wx, y: cy, z: wz });
                }
            }

            // DEBUG Giant Wall Generation
            if (wx === -5 && wz >= -10 && wz <= 10) {
                for (let y = 40; y <= 65; y++) {
                    if (!removedBlocks.has(`${wx},${y},${wz}`) && (!isBlockSolid(wx+1,y,wz)||!isBlockSolid(wx-1,y,wz)||!isBlockSolid(wx,y+1,wz)||!isBlockSolid(wx,y-1,wz)||!isBlockSolid(wx,y,wz+1)||!isBlockSolid(wx,y,wz-1)))
                        ceilingData.push({ x: wx, y: y, z: wz });
                }
            }
            if (wz === -5 && wx >= -10 && wx <= 10) {
                for (let y = 40; y <= 65; y++) {
                    if (!removedBlocks.has(`${wx},${y},${wz}`) && (!isBlockSolid(wx+1,y,wz)||!isBlockSolid(wx-1,y,wz)||!isBlockSolid(wx,y+1,wz)||!isBlockSolid(wx,y-1,wz)||!isBlockSolid(wx,y,wz+1)||!isBlockSolid(wx,y,wz-1)))
                        ceilingData.push({ x: wx, y: y, z: wz });
                }
            }
        }
    }

    // Inject user-placed blocks
    for (let [key, val] of addedBlocks.entries()) {
        const [px, py, pz] = key.split(',').map(Number);
        // Only inject if it belongs to THIS chunk
        const cxBlock = Math.floor(px / CHUNK_SIZE);
        const czBlock = Math.floor(pz / CHUNK_SIZE);
        if (cxBlock === cx && czBlock === cz) {
            // Prevent duplicate block stacking by verifying it's not already generated natively
            let exists = false;
            for (let b of blockData) if (b.x === px && b.y === py && b.z === pz) { exists = true; break; }
            if (!exists) for (let c of ceilingData) if (c.x === px && c.y === py && c.z === pz) { exists = true; break; }
            if (!exists) for (let l of ladderData) if (l.x === px && l.y === py && l.z === pz) { exists = true; break; }

            if (!exists) {
                if (val.type === 'grass') blockData.push({ x: px, y: py, z: pz });
                if (val.type === 'dirt') ceilingData.push({ x: px, y: py, z: pz });
                if (val.type === 'ladder') ladderData.push({ x: px, y: py, z: pz });
            }
        }
    }

    if (blockData.length === 0 && ceilingData.length === 0 && ladderData.length === 0) return null;

    // Create the InstancedMesh for the chunk
    const instancedMesh = new THREE.InstancedMesh(boxGeometry, material, blockData.length);
    const dirtMesh = new THREE.InstancedMesh(boxGeometry, dirtMaterial, ceilingData.length);

    // Ladders use Plane geometry instead of Box so they are flat
    const ladderMesh = new THREE.InstancedMesh(boxGeometry, ladderMaterial, ladderData.length);

    // Matrix for placing instances
    const dummy = new THREE.Object3D();

    let dirtInstance = 0;
    for (let idx = 0; idx < blockData.length; idx++) {
        const b = blockData[idx];
        dummy.position.set(b.x * TILE_SIZE, b.y * TILE_SIZE, b.z * TILE_SIZE);
        dummy.updateMatrix();
        instancedMesh.setMatrixAt(idx, dummy.matrix);
    }
    for (let idx = 0; idx < ceilingData.length; idx++) {
        const c = ceilingData[idx];
        dummy.position.set(c.x * TILE_SIZE, c.y * TILE_SIZE, c.z * TILE_SIZE);
        dummy.updateMatrix();
        dirtMesh.setMatrixAt(dirtInstance++, dummy.matrix);
    }
    for (let idx = 0; idx < ladderData.length; idx++) {
        const l = ladderData[idx];
        dummy.position.set(l.x * TILE_SIZE, l.y * TILE_SIZE, l.z * TILE_SIZE);
        dummy.updateMatrix();
        ladderMesh.setMatrixAt(idx, dummy.matrix);
    }

    instancedMesh.instanceMatrix.needsUpdate = true;
    dirtMesh.instanceMatrix.needsUpdate = true;
    ladderMesh.instanceMatrix.needsUpdate = true;

    const chunkGroup = new THREE.Group();
    chunkGroup.add(instancedMesh);
    chunkGroup.add(dirtMesh);
    chunkGroup.add(ladderMesh);

    // DEBUG GRIDS
    const gridGeo = new THREE.BufferGeometry();
    const verts = [];
    const colorArr = [];
    const cbX = cx * CHUNK_SIZE * TILE_SIZE - (TILE_SIZE / 2);
    const cbZ = cz * CHUNK_SIZE * TILE_SIZE - (TILE_SIZE / 2);
    const cbS = CHUNK_SIZE * TILE_SIZE;
    const yTop = 60 * TILE_SIZE;
    const yBot = -15 * TILE_SIZE;

    // Blue for X (Horizontal lines along X axis)
    verts.push(cbX, 0, cbZ, cbX + cbS, 0, cbZ); colorArr.push(0, 0, 1, 0, 0, 1);
    verts.push(cbX, 0, cbZ + cbS, cbX + cbS, 0, cbZ + cbS); colorArr.push(0, 0, 1, 0, 0, 1);

    // Green for Z (Horizontal lines along Z axis)
    verts.push(cbX, 0, cbZ, cbX, 0, cbZ + cbS); colorArr.push(0, 1, 0, 0, 1, 0);
    verts.push(cbX + cbS, 0, cbZ, cbX + cbS, 0, cbZ + cbS); colorArr.push(0, 1, 0, 0, 1, 0);

    // Red for Y (Vertical lines)
    verts.push(cbX, yBot, cbZ, cbX, yTop, cbZ); colorArr.push(1, 0, 0, 1, 0, 0);
    verts.push(cbX + cbS, yBot, cbZ, cbX + cbS, yTop, cbZ); colorArr.push(1, 0, 0, 1, 0, 0);
    verts.push(cbX, yBot, cbZ + cbS, cbX, yTop, cbZ + cbS); colorArr.push(1, 0, 0, 1, 0, 0);
    verts.push(cbX + cbS, yBot, cbZ + cbS, cbX + cbS, yTop, cbZ + cbS); colorArr.push(1, 0, 0, 1, 0, 0);

    gridGeo.setAttribute('position', new THREE.Float32BufferAttribute(verts, 3));
    gridGeo.setAttribute('color', new THREE.Float32BufferAttribute(colorArr, 3));
    const lineMat = new THREE.LineBasicMaterial({ vertexColors: true });
    chunkGroup.add(new THREE.LineSegments(gridGeo, lineMat));
    scene.add(chunkGroup);

    // Setup for counting faces to UI
    instancedMesh.count = blockData.length;
    dirtMesh.count = ceilingData.length;
    ladderMesh.count = ladderData.length;

    totalFacesRendered += (instancedMesh.count * 6) + (dirtMesh.count * 6) + (ladderMesh.count * 6);
    document.getElementById('faces').innerText = totalFacesRendered;

    return { mesh: chunkGroup, count: blockData.length + ceilingData.length + ladderData.length, instancedMesh, dirtMesh, ladderMesh };
}

function refreshChunk(cx, cz) {
    const key = `${cx},${cz}`;
    if (chunks.has(key)) {
        const chunkData = chunks.get(key);
        scene.remove(chunkData.mesh);
        // Critical: Explicitly dispose InstancedMesh to flush physical WebGL VRAM & prevent browser crashes
        chunkData.mesh.children.forEach(c => {
            if (c.dispose) c.dispose();
        });

        totalFacesRendered -= (chunkData.count * 6);
        chunks.delete(key);

        let generated = generateChunk(cx, cz);
        if (generated) {
            chunks.set(key, generated);
        }
        document.getElementById('faces').innerText = totalFacesRendered;
    }
}

document.addEventListener('mousedown', (e) => {
    if (!controls.isLocked) return;

    const dir = new THREE.Vector3();
    camera.getWorldDirection(dir);

    let hitPos = null;
    let prevPos = null;
    const start = camera.position.clone();

    // Raycast march
    for (let d = 0; d < TILE_SIZE * 6; d += TILE_SIZE * 0.25) {
        const testP = start.clone().add(dir.clone().multiplyScalar(d));
        const wx = Math.round(testP.x / TILE_SIZE);
        const wy = Math.round(testP.y / TILE_SIZE);
        const wz = Math.round(testP.z / TILE_SIZE);

        if (isBlockSolid(wx, wy, wz)) {
            hitPos = { x: wx, y: wy, z: wz };
            break;
        }
        prevPos = { x: wx, y: wy, z: wz };
    }

    if (e.button === 0 && hitPos) {
        // Left Click: Break block
        const key = `${hitPos.x},${hitPos.y},${hitPos.z}`;
        addedBlocks.delete(key);
        removedBlocks.add(key);

        const cx = Math.floor(hitPos.x / CHUNK_SIZE);
        const cz = Math.floor(hitPos.z / CHUNK_SIZE);
        refreshChunk(cx, cz);

        // Refresh adjacent chunks if right on the border
        let remX = hitPos.x % CHUNK_SIZE;
        let remZ = hitPos.z % CHUNK_SIZE;
        if (remX < 0) remX += CHUNK_SIZE; // JS modulo fix for negative
        if (remZ < 0) remZ += CHUNK_SIZE;

        if (remX === 0) refreshChunk(cx - 1, cz);
        if (remX === CHUNK_SIZE - 1) refreshChunk(cx + 1, cz);
        if (remZ === 0) refreshChunk(cx, cz - 1);
        if (remZ === CHUNK_SIZE - 1) refreshChunk(cx, cz + 1);

    } else if (e.button === 2 && globalHitPos && globalPrevPos) {
        // Right Click: Place block (MUST be aimed at an existing block face!)
        const key = `${globalPrevPos.x},${globalPrevPos.y},${globalPrevPos.z}`;

        // Prevent placing inside player's body
        const playerX = Math.round(controls.getObject().position.x / TILE_SIZE);
        const playerY = Math.round(controls.getObject().position.y / TILE_SIZE);
        const playerZ = Math.round(controls.getObject().position.z / TILE_SIZE);
        if (Math.abs(playerX - globalPrevPos.x) <= 0 && Math.abs(playerZ - globalPrevPos.z) <= 0 && Math.abs(playerY - globalPrevPos.y) <= 2) {
            return;
        }

        removedBlocks.delete(key);
        const type = selectedBlockType;
        addedBlocks.set(key, { type });

        // Play random block sound
        const sound = blockSounds[Math.floor(Math.random() * blockSounds.length)];
        sound.currentTime = 0; // Reset just in case it's still playing
        sound.play().catch(e => console.error("Audio play failed:", e));

        const cx = Math.floor(globalPrevPos.x / CHUNK_SIZE);
        const cz = Math.floor(globalPrevPos.z / CHUNK_SIZE);
        refreshChunk(cx, cz);

        // Refresh adjacent chunks if right on the border
        let remX = globalPrevPos.x % CHUNK_SIZE;
        let remZ = globalPrevPos.z % CHUNK_SIZE;
        if (remX < 0) remX += CHUNK_SIZE;
        if (remZ < 0) remZ += CHUNK_SIZE;

        if (remX === 0) refreshChunk(cx - 1, cz);
        if (remX === CHUNK_SIZE - 1) refreshChunk(cx + 1, cz);
        if (remZ === 0) refreshChunk(cx, cz - 1);
        if (remZ === CHUNK_SIZE - 1) refreshChunk(cx, cz + 1);
    }
});

// ---- MAIN LOOP ----
let prevTime = performance.now();
const fpsCounter = document.getElementById('fps');
const posTracker = document.getElementById('pos');
let frames = 0;
let lastFpsTime = prevTime;

const TICK_RATE = 60; // Fixed physical engine ticks per second
const TICK_DT = 1.0 / TICK_RATE;
let physicsAccumulator = 0;
let lastTime = 0;

// Spawn player high up to start
controls.getObject().position.set(0, 50, 0);

function animate(time) {
    requestAnimationFrame(animate);

    // Initial frame logic
    if (lastTime === 0) { lastTime = time; lastFpsTime = time; return; }
    const deltaVis = (time - lastTime) / 1000;
    lastTime = time;

    // Dynamic FOV based on speed state
    let targetFOV = cameraFOV; // Default is 100
    if (isSprinting && (moveForward || moveBackward || moveLeft || moveRight)) targetFOV = cameraFOV * 1.30; // +30% FOV
    if (isCrouching) targetFOV = cameraFOV * 0.90; // -10% FOV
    camera.fov += (targetFOV - camera.fov) * 10.0 * deltaVis;
    camera.updateProjectionMatrix();

    physicsAccumulator += deltaVis;

    // GUI Updates
    frames++;
    if (time - lastFpsTime >= 1000) {
        fpsCounter.innerText = frames;
        frames = 0;
        lastFpsTime = time;
    }
    const pc = controls.getObject().position;
    posTracker.innerText = `${pc.x.toFixed(1)}, ${pc.y.toFixed(1)}, ${pc.z.toFixed(1)}`;

    // Block Selection Highlight & Head Tilt
    if (controls.isLocked) {
        // Fast Voxel Raycast
        const dir = new THREE.Vector3();
        camera.getWorldDirection(dir);
        let hit = false;

        // Reset globals
        globalHitPos = null;
        globalPrevPos = null;

        const start = camera.position.clone();

        for (let d = 0; d < TILE_SIZE * 6; d += TILE_SIZE * 0.25) { // Cast 6 blocks away max
            const testP = start.clone().add(dir.clone().multiplyScalar(d));
            const wx = Math.round(testP.x / TILE_SIZE);
            const wy = Math.round(testP.y / TILE_SIZE);
            const wz = Math.round(testP.z / TILE_SIZE);

            if (isBlockSolid(wx, wy, wz)) {
                hit = true; globalHitPos = new THREE.Vector3(wx, wy, wz); break;
            }
            globalPrevPos = new THREE.Vector3(wx, wy, wz);
        }

        // Highlight Box updates
        if (hit && globalHitPos) {
            highlightBox.position.set(globalHitPos.x * TILE_SIZE, globalHitPos.y * TILE_SIZE, globalHitPos.z * TILE_SIZE);
            highlightBox.visible = true;
        } else {
            highlightBox.visible = false;
        }
    }

    // Physics Engine Loop (Fixed Timestep)
    while (physicsAccumulator >= TICK_DT) {
        const delta = TICK_DT; // Use fixed, predictable delta for physics step

        if (controls.isLocked) {
            // Flipped World mid-point boundary check
            const pyCamera = controls.getObject().position.y;
            const MIDPOINT = (CEILING_BASE * TILE_SIZE) / 2;

            // Friction
            velocity.x -= velocity.x * 10.0 * delta;
            velocity.z -= velocity.z * 10.0 * delta;

            // Dynamic Gravity: Smoothly approaches 0 as you reach midpoint.
            // When far past midpoint into the ceiling, gravity is extremely weak (~0.1 standard gravity).
            // Gravity NEVER flips. It always pulls down, just very slowly.
            let gravityScale = 1.0 - (pyCamera / MIDPOINT);
            if (gravityScale < 0.1) gravityScale = 0.1; // Cap the minimum gravity at exactly 1/10th normal gravity

            // Helper to check if climbing a ladder
            function checkLadder(px, py, pz) {
                const PLAYER_RADIUS = 0.4 * TILE_SIZE;
                const PLAYER_HEIGHT = 1.6 * TILE_SIZE;
                const minX = Math.round((px - PLAYER_RADIUS) / TILE_SIZE);
                const maxX = Math.round((px + PLAYER_RADIUS) / TILE_SIZE);
                const minY = Math.round(py / TILE_SIZE); // Feet
                const maxY = Math.round((py + PLAYER_HEIGHT) / TILE_SIZE); // Head
                const minZ = Math.round((pz - PLAYER_RADIUS) / TILE_SIZE);
                const maxZ = Math.round((pz + PLAYER_RADIUS) / TILE_SIZE);

                for (let x = minX; x <= maxX; x++) {
                    for (let y = minY; y <= maxY; y++) {
                        for (let z = minZ; z <= maxZ; z++) {
                            const key = `${x},${y},${z}`;
                            if (addedBlocks.has(key) && addedBlocks.get(key).type === 'ladder') return true;
                        }
                    }
                }
                return false;
            }

            const currentFeetY = controls.getObject().position.y - (1.4 * TILE_SIZE); // Py offset
            const onLadder = checkLadder(controls.getObject().position.x, currentFeetY, controls.getObject().position.z);

            if (onLadder) {
                // Ladder physics completely overrides GRAVITY
                // Cancel all vertical velocity to "hang" perfectly still
                velocity.y = 0;
                velocity.x -= velocity.x * 20.0 * delta; // Extreme friction to stick to ladder
                velocity.z -= velocity.z * 20.0 * delta;

                if (isJumping) {
                    velocity.y = 15; // Smooth consistent climb UP
                }
            } else {
                // Vertical air resistance (friction) so you don't drift upward forever after thrusting in low-G
                velocity.y -= velocity.y * 2.0 * delta;

                velocity.y -= GRAVITY * gravityScale * delta;

                // Simple impulse jump
                if (isJumping && isGrounded) {
                    velocity.y = JUMP_VELOCITY;
                    isJumping = false; // Require re-press to jump again
                }
            }

            direction.z = Number(moveForward) - Number(moveBackward);
            direction.x = Number(moveRight) - Number(moveLeft);

            direction.normalize(); // consistent speed in all dirs

            // Determine active movement speed
            let currentSpeed = SPEED;
            if (isCrouching) currentSpeed = SPEED * 0.5;
            else if (isSprinting) currentSpeed = SPRINT_SPEED;

            if (moveForward || moveBackward) velocity.z -= direction.z * currentSpeed * 10.0 * delta;
            if (moveLeft || moveRight) velocity.x -= direction.x * currentSpeed * 10.0 * delta;

            // Apply Horizontal Movement with Collision
            let rightDist = -velocity.x * delta;
            let fwdDist = -velocity.z * delta;

            const vecRight = new THREE.Vector3();
            vecRight.setFromMatrixColumn(camera.matrix, 0); // local Right

            const vecForward = new THREE.Vector3();
            camera.getWorldDirection(vecForward);
            vecForward.y = 0;
            vecForward.normalize();

            // Calculate desired displacement
            let dispX = vecRight.x * rightDist + vecForward.x * fwdDist;
            let dispZ = vecRight.z * rightDist + vecForward.z * fwdDist;
            const dispY = velocity.y * delta;

            const playerObj = controls.getObject();
            const PLAYER_RADIUS = 0.3 * TILE_SIZE; // Width (slightly narrower for less getting stuck on corners)
            
            if (typeof window.playerEyeOffset === 'undefined') window.playerEyeOffset = 3.6;

            let py = playerObj.position.y - window.playerEyeOffset;

            // Adjust physical hitbox based on crouch state (2.0 to 1.5 blocks tall)
            const heightMultiplier = isCrouching ? 1.5 : 2.0;
            const PLAYER_HEIGHT = Math.max(0.1, heightMultiplier * TILE_SIZE); // Height from feet to head
            const TARGET_EYE_OFFSET = Math.max(0.1, (heightMultiplier - 0.2) * TILE_SIZE); // Eyes are near the top of the height
            
            // Smoothly interpolate the eye offset to simulate organic ducking and prevent grounding breaks
            window.playerEyeOffset += (TARGET_EYE_OFFSET - window.playerEyeOffset) * 15.0 * delta;
            const PLAYER_EYE_OFFSET = window.playerEyeOffset;

            // Helper to check if any solid block exists precisely under the footprint
            function checkGround(px, py, pz) {
                const minX = Math.round((px - PLAYER_RADIUS) / TILE_SIZE);
                const maxX = Math.round((px + PLAYER_RADIUS) / TILE_SIZE);
                const y = Math.round((py - 0.1) / TILE_SIZE); // Block immediately below feet
                const minZ = Math.round((pz - PLAYER_RADIUS) / TILE_SIZE);
                const maxZ = Math.round((pz + PLAYER_RADIUS) / TILE_SIZE);

                for (let x = minX; x <= maxX; x++) {
                    for (let z = minZ; z <= maxZ; z++) {
                        if (isBlockSolid(x, y, z)) return true;
                    }
                }
                return false;
            }

            // Helper to check AABB collision against all voxels in a bounded box
            function checkCollision(px, py, pz) {
                const minX = Math.round((px - PLAYER_RADIUS) / TILE_SIZE);
                const maxX = Math.round((px + PLAYER_RADIUS) / TILE_SIZE);
                const minY = Math.round(py / TILE_SIZE); // Feet
                const maxY = Math.round((py + PLAYER_HEIGHT) / TILE_SIZE); // Head
                const minZ = Math.round((pz - PLAYER_RADIUS) / TILE_SIZE);
                const maxZ = Math.round((pz + PLAYER_RADIUS) / TILE_SIZE);

                for (let x = minX; x <= maxX; x++) {
                    for (let y = minY; y <= maxY; y++) {
                        for (let z = minZ; z <= maxZ; z++) {
                            if (isBlockSolid(x, y, z)) return true;
                        }
                    }
                }
                return false;
            }

            // Current absolute position of feet (py is calculated safely decoupled from top camera offset)
            let px = playerObj.position.x;
            let pz = playerObj.position.z;

            // Step X
            if (dispX !== 0) {
                // Ledge prevention (Crouching) - Keeps player's box over the edge
                if (isCrouching && isGrounded) {
                    if (!checkGround(px + dispX, py, pz)) {
                        dispX = 0;
                        velocity.x = 0;
                    }
                }
                if (!checkCollision(px + dispX, py, pz)) {
                    px += dispX;
                } else {
                    // Try auto-step up 1 block
                    if (!checkCollision(px + dispX, py + TILE_SIZE, pz)) {
                        px += dispX;
                        py += TILE_SIZE;
                    } else {
                        velocity.x = 0; // Hit wall
                    }
                }
            }

            // Step Z
            if (dispZ !== 0) {
                // Ledge prevention (Crouching) - Keeps player's box over the edge
                if (isCrouching && isGrounded) {
                    if (!checkGround(px, py, pz + dispZ)) {
                        dispZ = 0;
                        velocity.z = 0;
                    }
                }
                if (!checkCollision(px, py, pz + dispZ)) {
                    pz += dispZ;
                } else {
                    // Try auto-step up 1 block
                    if (!checkCollision(px, py + TILE_SIZE, pz + dispZ)) {
                        pz += dispZ;
                        py += TILE_SIZE;
                    } else {
                        velocity.z = 0; // Hit wall
                    }
                }
            }

            // Step Y
            isGrounded = false;
            if (dispY !== 0) {
                if (!checkCollision(px, py + dispY, pz)) {
                    py += dispY;
                } else {
                    if (dispY < 0) {
                        isGrounded = true; // Hit floor perfectly
                    }
                    velocity.y = 0;
                    // Intentionally NOT snapping `py` mathematically to avoid float oscillation bouncing.
                    // This makes Y-axis collision identical in smoothness to X and Z.
                }
            }

            // Anti-Suffocation Emergency Pushback (If block placed directly inside head/body)
            if (checkCollision(px, py, pz)) {
                py += TILE_SIZE; // Shove up instantly
                velocity.y = Math.max(0, velocity.y); // Prevent downward momentum from forcing us back in
            }

            // Apply finalized physics position back to Three.js camera
            playerObj.position.x = px;
            playerObj.position.y = py + PLAYER_EYE_OFFSET;
            playerObj.position.z = pz;

        } // End of controls.isLocked

        physicsAccumulator -= TICK_DT;
    } // End of Fixed Physics Tick Loop

    // Failsafe: If physics exploded into NaN, explicitly reset player so the map doesn't vanish
    if (isNaN(controls.getObject().position.x) || isNaN(controls.getObject().position.y)) {
        controls.getObject().position.set(0, 50, 0);
        velocity.set(0, 0, 0);
    }

    // Infinite Chunk Generation
    const px = controls.getObject().position.x;
    const pz = controls.getObject().position.z;
    const currentChunkX = Math.floor(px / (CHUNK_SIZE * TILE_SIZE));
    const currentChunkZ = Math.floor(pz / (CHUNK_SIZE * TILE_SIZE));

    // Despawn far chunks
    for (let [key, chunkData] of chunks) {
        const [cx, cz] = key.split(',').map(Number);
        if (Math.abs(cx - currentChunkX) > RENDER_DISTANCE || Math.abs(cz - currentChunkZ) > RENDER_DISTANCE) {
            if (chunkData && chunkData.mesh) {
                scene.remove(chunkData.mesh);
                // Critical: Explicitly dispose InstancedMesh to flush physical WebGL VRAM & prevent browser crashes
                chunkData.mesh.children.forEach(c => {
                    if (c.dispose) c.dispose();
                });
            }

            if (chunkData) {
                totalFacesRendered -= (chunkData.count * 6);
                document.getElementById('faces').innerText = totalFacesRendered;
            }
            chunks.delete(key);
        }
    }

    // Spawn new chunks via prioritized queue
    const missingChunks = [];
    for (let x = -RENDER_DISTANCE; x <= RENDER_DISTANCE; x++) {
        for (let z = -RENDER_DISTANCE; z <= RENDER_DISTANCE; z++) {
            let cx = currentChunkX + x;
            let cz = currentChunkZ + z;
            let key = `${cx},${cz}`;

            if (!chunks.has(key)) {
                // Calculate Chebyshev distance (chunk radius distance)
                let dist = Math.max(Math.abs(x), Math.abs(z));
                missingChunks.push({ cx, cz, key, dist });
            }
        }
    }

    if (missingChunks.length > 0) {
        // Sort to generate closest chunks first
        missingChunks.sort((a, b) => a.dist - b.dist);

        let chunksGenerated = 0;
        for (let chunkInfo of missingChunks) {
            let generated = generateChunk(chunkInfo.cx, chunkInfo.cz);
            chunks.set(chunkInfo.key, generated);
            chunksGenerated++;

            // Detect & load instantly within 2 chunks radius for physics safety
            // For blocks further away, throttle to 1 generation per frame to eliminate CPU stutters
            if (chunkInfo.dist > 2 && chunksGenerated >= 1) {
                break;
            }
        }
    }

    renderer.render(scene, camera);
}

// Handle resizing
window.addEventListener('resize', () => {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
});

// Start loop
requestAnimationFrame(animate);
