# Designing Stuff

## Multiplayer World Architecture (claim system)

Core split: **Wilderness** (pure math, seed-based, zero storage) vs **Claims** (sphere-shaped areas that store only deltas/changes).

### Claims
- Sphere area carved from wilderness — everything inside is non-wilderness
- Stores only block changes (deltas) from base seed generation
- Each claim has a **Flag** (core block) — destroy the flag = claim + all sub-claims revert to wilderness, anyone can re-claim
- Size starts small, grows with player level, hard cap on max size
- Level system: XP from everything (kills, mining, building, quests, exploration, trading, time). Tied to a skill tree (details TBD)

### Clan System
- Players form a clan with a **claim lead** (leader)
- Shared claim budget pool — members draw from the same total
- Claim lead controls member permissions (who can build, who can claim outside, etc.)
- No proximity requirement — social link, not geographic

### Wilderness Rules
- Block breaking: allowed — items drop immediately, stay in inventory permanently
- Block placing: blocked entirely. Only **Tents** (temporary claims) are the exception
- Full open PvP anywhere (wilderness + claims) + mobs — no safe zones
- Night cleanup for some wilderness stuff: TBD, deferred to multiplayer expansion update

### Tents
- Cost resources/items to place
- Real-time timer (e.g. 30 min) then poof
- Limited count per player (exact TBD)

### Inventory
- Fixed size, no expanding
- Items picked up stay permanently
- If inventory full, extras stay on ground — despawn after 1 in-game day

### Wilderness Ores
- Regenerate nightly but spawn in a different location each time (roaming ore veins)

### Multiplayer Modes
- Singleplayer: saves locally, no decay, no restrictions, no claims (wilderness only with full build freedom)
- Local host: LAN, host machine runs server
- Dedicated/Database: remote server/DB, full persistent multiplayer

### Future Ideas (not now)
- Claims shoot a ray upward as map beacon/marker
- Map view showing claim locations
- Clan neighbor claim UI
- Night cleanup system for wilderness blocks (multiplayer expansion)
- Roaming ore vein details

---

## Physics Architecture

### What exists and works
- `system/Physics/Physics.cs` — player physics: force-based movement, drag, gravity, terrain AABB, friction, jump
- `system/Physics/Entity.cs` + `EntitySystem` — 3-step: integrate → terrain AABB → entity-entity OBB impulse
- `client/Detection.cs` — `CollisionDetection`: full 15-axis OBB SAT, circumsphere broadphase, contact generation
- `client/Rendering/OffGrid.cs` — **the active renderer**: spatial hash broadphase, OBB SAT, instanced rendering

### Bugs fixed this session
1. **Cubes rotating into ground** (`OffGrid.cs` terrain collision):
   - Was using `Pos.Y - HalfSize` (axis-aligned) as bottom — rotated corners clipped through terrain
   - Fixed with `RotatedYExtent()`: computes true OBB Y-extent using local axes
   - Formula: `HalfSize * (|sin(ry)*sin(rx)| + |cos(rx)| + |cos(ry)*sin(rx)|)`
2. **Angular velocity never settling**:
   - Root cause: AngVel updates RotX/Y → changes YExtent → cube briefly leaves ground → micro-bounce → adds AngVel → loop
   - Fixed: apply 50% angular damping BEFORE updating rotation, using previous tick's OnGround state
   - Threshold raised to 1e-4 for zeroing
3. **`GetAxes` heap allocation**: was `new Vector3[3]` inside 144hz physics loop — changed to `out` params, zero GC

### No external library needed
- Terrain collision = heightmap lookup
- Entity-entity = SAT OBB already implemented and correct
- "Trillions of entities" = spatial DB of plain structs, never all simulated
- Live simulation = only entities near player (~thousands max)
- Bepuphysics / Box2D etc are overkill

### Next physics task
- Non-uniform half-extents (`Vector3 HalfExtents` instead of `float HalfSize`)
- Enables: elongated bodies (1×1×16 chain = one rigid entity), player hitbox (16×16×32)
- README says: "rigid connection treated as one large AABB" — not a rope sim, just non-uniform OBB

### Player dimensions (from user)
- 16 voxels wide × 16 voxels deep × 32 voxels tall
- README: capsule for narrow phase, AABB × 2 for broad phase

---

## Roadmap (user's stated order)
1. Physics (current)
2. Wilderness generation
3. Effects
4. Browser + GUI (WebGL/WASM port)

---

## Notes on README
- Physics steps 1-6 documented in `notes/README.md`
- Step 2.5: broadphase SVO cell, SAT narrow, detection only — connected entities = rigid (one large AABB) or dynamic (joint with influence factor)
- World size: 2^24 × 2^24 × 2^12 chunks
- GPU raycasting is the renderer — traditional culling/meshing not applicable
- Chunk size 16×16×16, only process within active radius
- Server saves changes only (seed-based baseline, delta storage)
