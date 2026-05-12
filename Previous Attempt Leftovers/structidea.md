# Voxel World - Structure & Design Ideas

## Core World Model

- World is split into two zones: **Wilderness** and **Claimed Areas**
- Wilderness is 100% math/procedural — zero storage cost, infinite, deterministic by seed
- Claimed areas store only **deltas (changes)** from the base seed generation
- Claims are stored as a **sphere/bounded area** — only modified blocks within that area are saved

---

## Wilderness

- Fully procedural, never stored
- Players can move through it, fight mobs, gather resources
- Block **breaking**: allowed — items drop immediately on break and persist in inventory normally
- Block **placing**: UNSOLVED — storing placed blocks in wilderness conflicts with the pure-math model; TBD
- Temporary structures (see below) are the exception to the placing problem
- Future: wilderness is "visible" on a map showing where claims are located

---

## Claim System

- Claims are permanent and stored on the server (or locally in singleplayer)
- A claim is literally a **sphere** carved out of wilderness — everything inside is non-wilderness, everything outside is pure math
- Anyone can enter a claim at any time
- Inside a claim: players can **fight PvP** and **break/place blocks freely** (full raid)
- Claims **shoot a ray upward** (future: used as a map marker / beacon)

### Claim Size
- Starts small, grows as you play/level up
- Hard cap on max size per claim — can't grow infinitely

### Clan / Team System
- Players form a clan with a **claim lead** (the leader)
- Clan shares a pool of claim area — members draw from the same total budget
- Claim lead controls:
  - Allowing/revoking members' ability to claim outside the main claim
  - Access permissions per member (who can build, who can't)
- No proximity requirement — clan is social, not geographic
- Claims can be anywhere; clan status is what links them

---

## Temporary Structures

- **Tents** = temporary claim in wilderness, costs a resource/item to place
- Has a real-time timer (e.g. 30 min), then disappears
- Limited count per player (exact number TBD)
- Night cleanup scrapped — decay is purely timer-based

---

## Invasion

- Any player can enter any claim at any time — no permission system
- Inside an enemy claim: full PvP, full block breaking/placing
- No "declare war" step — wilderness and claims are both open-PvP zones

---

## Multiplayer Modes

| Mode | Storage | Notes |
|------|---------|-------|
| Singleplayer | Local files | No decay, no restrictions, saves all changes |
| Local host | Host machine | LAN-style, host runs the server |
| Dedicated / Database | Remote server / DB | Full persistent multiplayer, server owns all claim data |

---

## Singleplayer

- Changes saved locally
- No decay, no invasion, no restrictions
- Still uses the same seed-based wilderness generation
- Claims behave the same architecturally, just persisted to disk instead of a server

---

## Physics Architecture

### What exists and works
- **Player physics** ([system/Physics/Physics.cs](system/Physics/Physics.cs)): force-based movement, drag, gravity, terrain AABB, friction, jump impulse
- **Entity physics** ([system/Physics/Entity.cs](system/Physics/Entity.cs)): 3-step loop — integrate → terrain AABB bounce → entity-entity OBB impulse
- **OBB-OBB collision** ([client/Detection.cs](client/Detection.cs)): full 15-axis SAT (6 face + 9 edge-cross), circumsphere broadphase, contact generation

### No external physics library needed
- Terrain collision = heightmap lookup, no library can beat this
- Entity-entity = SAT OBB already implemented and correct
- "Trillions of entities" = spatial DB of plain structs, never all simulated at once
- Live simulation = only entities near player (~few thousand max)

### Known bottleneck
- Broadphase in `CollisionDetection.Detect` is O(n^2) brute force — 10k entities = 50M checks/tick at 144hz
- Fix: replace with spatial grid (hash cells by position, only check same-cell neighbors)
- Libraries like Bepuphysics would solve this but are overkill given the rest is already working

### Entity struct design (planned)
- Structs store only: Vec3 position, velocity, acceleration, force, angular velocity, rotation
- Players are entities with extra fields
- Only entities within player range are active in simulation — rest are stored as DB records (zero CPU)

### Compound voxel entities (e.g. voxel ball)
- Entity is made of many voxels — interior voxels fully exist (rendered, part of the shape)
- **Only surface voxels** (edge + corner shell) participate in collision detection
- Interior voxels are invisible to the physics system — not "non-existent", just not checked
- Collision checks surface shell only → keeps detection fast regardless of entity volume

---

## Future / Later Ideas

- [ ] Level system: player level gates number of allowed claims
- [ ] Claims shoot a ray upward as a visible beacon / map marker
- [ ] Map view showing wilderness with claim locations marked
- [ ] Wilderness "visibility" — players can see the world map and plan where to claim
- [ ] Tent / temporary claim mechanics fleshed out (timer, cost, size)
- [ ] Neighbor claim UI — easy way to place a claim adjacent to an ally
- [ ] Claim expansion mechanic (earn more space over time?)

---

## Open Questions (Quiz in Progress)

### Answered
1. **Invasion** — Full open PvP + block breaking/placing inside claims anytime. Confirmed.
2. **Wilderness block breaking** — Allowed. Items drop immediately and stay in inventory forever. Confirmed.
3. **Inventory** — Fixed size (no expanding). Items stay permanently. If full, extras stay on ground and despawn after 1 in-game day. Confirmed.
4. **Wilderness block placing** — Blocked entirely. Tents are the only exception (timer-based, costs resources). Night cleanup TBD — deferred to multiplayer expansion update. Confirmed.
5. **Claim size** — Starts small, grows with level/play, hard cap on max. Confirmed.
6. **Clan system** — Claim lead controls member permissions + shared claim budget. Confirmed.
7. **Tents** — Costs resources, real-time timer (e.g. 30 min), limited count per player. Confirmed.

### Unresolved
8. **Wilderness combat** — Full open PvP anywhere (wilderness + claims) AND mobs. No safe zones. Confirmed.
10. **Wilderness ores** — Regenerate nightly but spawn in a different location each time (roaming ore veins). Makes wilderness worth continuously exploring. Confirmed.
9. **Level system** — XP from everything: kills (mobs + players), mining, building, quests, time played, trading, exploring, etc. More activities = more XP sources. Tied to a **skill tree** (details TBD). Confirmed direction.
10. **Claim destruction** — Each claim has a **Flag** (core block). If the flag is destroyed: claim + all sub-claims/minor claims convert back to wilderness immediately. Anyone can then re-claim that land. Confirmed.
