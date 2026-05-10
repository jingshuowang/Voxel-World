THE METHOD LIBRARY
optimizations simple and data
 Next step is to rethink data optimization since we moved to GPU Raycasting! Our older ideas (like chunk meshing, instancing faces, storing bitmasked vertices, greedy meshing, SSBO indirect drawing) were designed for rasterization (drawing triangles). Since we are now using a compute shader and raymarching against a 3D texture grid (DDA algorithm), those optimizations are mostly useless. Instead, the main GPU optimizations focus on: 
 1) A sliding 3D texture window (passing active voxels directly as a 3D texture).
 2) Skipping empty space efficiently during DDA raymarching.
 3) Storing block IDs/Data compactly in the 3D texture (RGBA32f for now, could be compressed later).

Physics:
Cauculates every tick, but separated form the rendering which is deltatime, but this is fixed step. Lets say 64 steps, yes not 20 its 64, cus its balanced, with clientside prediction. But we are focusing on physics.  
Step 1: Kinematics
so this is very terribly designed but its ok, no collision no detection just simple movement. 
the postion of the pervious tick plus the velocity times tickspeed is the new postion. Very efficient. with acceleration its just velocity of the previous tick plus the acceleration times tickspeed is the new one, same thing. But cus this is still kinematics literally just do when button held set velocity to a vlaue like lets say 10 and when let go set to 0, and add decay like eveyrtick vel = 0.95 of previous. this is obviously fake and terrible.
Step 2: Basic Forces:
So rember the setting, thats stupid, lets use forces, so now we do not often literally jsut change velocity and acceleratio ndirection, instead we give forces.clientside can also send forces like detection, wait ignore that no clientside sends input, physics processes it, so forces, accleeration, and mass are closely related, lets say for now our player is 64 unit in mass, just do f=ma, done, boom, and for the proviosu multiply by 0.16 that was stupid literally just use drag force, its diffrent from friction, which add later cus its accualy kidna compelx drag is just not here be gona fake it, just applie a force opposite to the velocity but wiht magnitude so the force is equal to the velocity(vector) times the speed( scalar) and then -1 and the drag coeffficeint which lets say is just 0.96 right now..also force velocity accleration , accualy most of these physics thingys are all vectors.
Step 2.5: Before Step 3 we gotta setup collision detection and other stuff. firstly the hitbox of a player is a cappsule but is shown as a prism (1 by 1 by 2). Capsule physics collision detection is easy, the landform(static) voxels uses aabb cus its alined to axis and particle/dynamic objects cannot do that but we only have voxels so i guess dynamic voxels just use SAT on gpu. It spits out info like collision depth and normal which makes respond easy. At this step we dont need responce just yet. just make colliding objects change to red color. for this one, we cnat directl;y put in our engine since theres only one enityt but we can add a second entity which just is gona be our test subject. so wait we can do a special seed 0, which is just a flat world but same physics and that will be enter, no collision responce just yet but we can spawn random things floating in the sky and turn red when colliding.
new idea: static is aligned to the grid while dynamic is not. all collision with dynamic and static must use a 32th SV32 lmao tree not a octree, and its maximum size is infiniti or to the server size. only cauculate collisions within each cell. then broad is sphere check and then at finer levels its obb with sat. very simple. this is onyl detection plus sat give you the pen depth.
soits like this for the entitys it only checks within2 times the aabb of stuff it acts like its phantom hitbox and thats the svo cell it takes up, and within the svo cells it can see what svo cells and taken up and detects collision within that place. the reason its 2 times is because if its a capsule hitbox made of voxels the bottom might break with aabb cus its too close. sphere also is bad so we eather use a aabb hitbix for player which is fake for capsule which is not voxels this is weird. and thats for entitys while the static objects are just aligned to the grid and literally jut aabbing and only gets detected within entity radius. entitys can also be connected and eather rigid connection which in SVO is treated as one large aabb big back. but normal slim check. only checking the face voxels and corner voxels and the edge voxels. dynamicconnection is accualy much simpler just connection/effectance factor and eveyrhting follows the joints perfect. player is the onlything that dosent use box physics for narrow phase though still just the 2x aabb for the broad phase, it will use a capsule tsince its very efficent. Later: Cellular automata fluids,  softbody. Thermal and eletric energy. Raycasted audio.
Step 3: Friction:
The force of friction is the friction coefficent times the normal force. The force that counters gravity and friction is cauculated at collision, there are 2 types , kinetic and static friction.  When at rest its static and motion is kinetic but static is always higher than kinetic. When colision the friction of one object is averaged with the other object. 
Step 4 Drag:
Fianlly we replacing stoppid multiplicaion decay with drag. The force of drag is -b, the drag coefficient times the velocity. A object terminal velocity is the force of gravity times the mass divided by the drag coefficient. nice :D.
Step 5 Collision(responce) and Momentum:
The force of momentum is mass times velocity, p=mv.And elastiticity or restitution or bounciness is how much energy is conserved at collision. Direction and other stuff is basic physics.
Step 6 Rotational Physics and Momentum:
theres angular pos, angular vel, angular acc. Which is theta omega and alpha.  same equation as the kinematics for angular kinematics but just diffrent names, they can be transfered and thats it.






Improve: Phong is stoopid, remeber the sun in the sky? thats the light source, its a directional light os its very simple, we move on to PBR,IBL and raycasting for realism all the fnacy stuff, which onl scacrificing liuke 10% of preformance, its worth it. Later improvement is smaller voxels, physics, and partial voxels. Also effects like gamma crrections bloom, tonemapping, etc. Also fluid shaders, diaphanity, reflectivit, and all that. Colorbleed like gloom and stuf, etc.

Image selector for file management, theres going to be a spritsheet for all the main textures, works like this, the image are in a folder, the top left is labled image 0, its a mthethod. so for example if theres 9 things the top left is 0 and the one to the right is 1 and the one to the right of that is 2 but then when theres no more to the right it goes to the most left of the second row and so on so the bottom right is 8 which is the 9th image. very good for texture selection in ssbo.

info plane, we going to renderr this at the bottom right corner, it shows kinda like a lable, so it shows comments, later going to add ctutors, info about block ,and it acts as info page, but now just add the hint, water not added yet.

superpowers: super jump, build. use a radial menu with pressing x and then clicking to select. currently unsophisticated idea.

AI: have likings, ur rating, mobs, etc. Unsophisticated for now.

BRAINSTORMING: maybe add biomes, pvp enhancement, pve enhancement, more blocks, more tools, more armor, more mobs, more structures, more biomes, more everything. make a systemized system, like a tech tree, or skill tree, or something. 

UI: it looks good, we can steal some ideas from my other project "new game". just slots and windows. Radial menu and so on. theres also inventory, storage, menu, and just ui in general.

inventory: very simple hotbar, 1-0 or scroll for 10 spots to select in the current selected hotbar + and - to change sleected hotbar ex: before it was 1 - 10 after + its 2-11. basic coloring since this is still a prototype(the whole current game). fore inventory it shwos selected hotbar and selected block which can be draged or taped to change slots. for example if i click a block, and let go , it goes intothe slot it was let go, what if its let go at same slot? then go into click and click mode so u then can click an other slot to swap, if selected is epty dont do anythign theres a glithc that can duplicate.



 volumetric light, because classic methods of 3d rendering is way too costly I have ummerized better methods, we choose random positions and draw flat squares there all of them facing the same direction and stretched up in the y axis and its a rectangle. add a sun colored gradient to the rectangle and lastly angle these planes towards the sun. The fectrangle is easily rendered as 2 triangles. but theres a problem the light isnt rendered in corret spots since its random, we can fix this by cheaking samples aroudn the light and the light is brightest when theres 
over half of them in sunlight the beam is brightest(opacity = 1-abs(percentSunlight)*2). we can use the transform feedback buffer to average each ray of lights opacity to make it flicker less. vertex sahder gives to transform readback buffer then trb gives to cpu then cpu cauculates average gives to ssbo and lastly goes back to vertex shaders.



More Techniques: Since we are raycasting, traditional Frustum Culling, Backface Culling, Occlusion Culling, and Greedy Meshing are no longer applicable or necessary. The raymarcher inherently handles occlusion (rays stop at the first block hit) and frustum culling (we only shoot rays for pixels on screen). For Level of Detail (LOD), we can sample lower resolution volumes or switch to simplified procedural noise for distant blocks instead of lowering mesh resolution. Multithreading is still useful for chunk generation logic.

Servers: this is the most easiest, its not fancy it just uses seeds and make player stuf sychronize player position. new player made when new username/passord, the ip is the seed and they join and idk if we need ports. player just a prism, yes just a rectangular prim 2 meter tall. theres a secret the server only save changes so it dosent crash, the same world seed makes the same world every time. server saves chunks player stats etc.
future adtitives after im done learning them:w
Complex sahdoers, shadows, realistic fluids, gloom(pixel blend), fog, reflection, PBR(likely not), raycast(probably not too lmao)temporary antiailias, gamma correction, motion blur, refined voxel, tone mapping, advanced Abient occlusion, physcial . Combatt, bultiplyaer, biome etc.

Aliasing, just use anti aliasing.
Could use fractal perlin noise+ secret sauce, its a gradient trick,, complex thouhg. a better method is DLA though this is insanely slow, but we can first use a smaller image and in=crase size and run again and reopeat to limit the time the dual filter blur we can add a version at each image enlargement a version thast blurry and one thast crips and stack them together which leads to a blurry unscale and a crisp upsale on the crip one too.

hotbar system, 1-0 or scroll for 10 spots to select in the current selected hotbar + and - to change sleected hotbar ex: before it was 1 - 10 after + its 2-11. basic coloring since this is still a prototype(the whole current game). 
tweeks:
show a really transparent translucent of the block you are about to place in the center of the screen. make sure its the same color. in inventory for now make the first one water second one dirt thirds one tgrass and so on. make inventory 20 for storage for now. the translucant of the block should be 0.1. dont tweek render distance, it should be 64 chunks(radius). super jump for viewing trough fps and superbuild as an test for cpu speed. blocks turn darker, not redder when mined.



 volumetric light further explained, because classic methods of 3d rendering is way too costly I have ummerized better methods, we choose random positions and draw flat squares there all of them facing the same direction and stretched up in the y axis and its a rectangle. add a sun colored gradient to the rectangle and lastly angle these planes towards the sun. The fectrangle is easily rendered as 2 triangles. but theres a problem the light isnt rendered in corret spots since its random, we can fix this by cheaking samples aroudn the light and the light is brightest when theres 
over half of them in sunlight the beam is brightest(opacity = 1-abs(percentSunlight)*2). we can use the transform feedback buffer to average each ray of lights opacity to make it flicker less. vertex sahder gives to transform readback buffer then trb gives to cpu then cpu cauculates average gives to ssbo and lastly goes back to vertex shaders.

ambient occlusion further explained, kinda simple kinda compl;ex, defered shading, for each pixel if the sample point is futher away to the camera than the other sampler points we consider that to be blocking ambient light. but this is bad a shpere sahped area will fial on a floor and too costly. we can further imporve this by move the sampler point away form the noirmal slightly for better reselts. this is ok but we can use baked ambient oclusion, the blocks don't move so this is ok for now, for example if the blocks that are ajcent to a edge are 90 degrees from each other, parts close to the edge will we sahdered darker idk how t o explain but you get it.

More Techniques: Since we are raycasting, traditional Frustum Culling, Backface Culling, Occlusion Culling, and Greedy Meshing are no longer applicable or necessary. The raymarcher inherently handles occlusion (rays stop at the first block hit) and frustum culling (we only shoot rays for pixels on screen). For Level of Detail (LOD), we can sample lower resolution volumes or switch to simplified procedural noise for distant blocks instead of lowering mesh resolution. Multithreading is still useful for chunk generation logic.

Servers: this is the most easiest, its not fancy it just uses seeds and make player stuf sychronize player position. new player made when new username/passord, the ip is the seed and they join and idk if we need ports. player just a prism, yes just a rectangular prim 2 meter tall. theres a secret the server only save changes so it dosent crash, the same world seed makes the same world every time. server saves chunks player stats etc.
future adtitives after im done learning them:w
Complex sahdoers, shadows, realistic fluids, gloom(pixel blend), fog, reflection, PBR(likely not), raycast(probably not too lmao)temporary antiailias, gamma correction, motion blur, refined voxel, tone mapping, advanced Abient occlusion, physcial . Combatt, bultiplyaer, biome etc.

Aliasing, just use anti aliasing.
Could use fractal perlin noise+ secret sauce, its a gradient trick,, complex thouhg. a better method is DLA though this is insanely slow, but we can first use a smaller image and in=crase size and run again and reopeat to limit the time the dual filter blur we can add a version at each image enlargement a version thast blurry and one thast crips and stack them together which leads to a blurry unscale and a crisp upsale on the crip one too.

hotbar system, 1-0 or scroll for 10 spots to select in the current selected hotbar + and - to change sleected hotbar ex: before it was 1 - 10 after + its 2-11. basic coloring since this is still a prototype(the whole current game). 
tweeks:
show a really transparent translucent of the block you are about to place in the center of the screen. make sure its the same color. in inventory for now make the first one water second one dirt thirds one tgrass and so on. make inventory 20 for storage for now. the translucant of the block should be 0.1. dont tweek render distance, it should be 64 chunks(radius). super jump for viewing trough fps and superbuild as an test for cpu speed. blocks turn darker, not redder when mined.
Raycast+gamma correction+colorbleed+blur+occlusion+stats like diaphanity, rouchness, reflectiveness, texture, normal etc. these are the way we run raycast, its really good for nowadays gpu. since it is cauculated at like a million o1s.


Now time for system/CPU cauculation and data optimization, previously we all handled with rendering, but what aobut data? Fopr the cpu we gotta rebuild, firstly physics uses JOML for AABB OOB cauculation, we using a hybrid, also use gpu for this since its tiny cauculations but still start it in system, we need to rebuild everything in the system file, then we can move on to data optimization, which is basically just using bytes instead of vec3 and vec4 and stuff, and also using bit masking to store data, we can use 6 bits for position, 3 bits for direction, 7 bits for texture, 5 bits for length, and 5 bits for width, so 26 bits in total, which is less than 32 bits, so we can use 32 bits to store data, which is a byte. And other dtat nmanament, taking advantage of 4.6 opengl to patch to preformance gap between vulkan. now its seed port server system. pretty simple. for genrration its a simple fractal perlin noise . or add based on other symmaries in this readme. there is trees at certain level, and terrain is based on hieght for now. Upgade noise and generation later. not much videos ill get more summaries later. With chun kssytem of only detecting within active radius.the world is generated wit ha seed, and onyl rendered and processin within the render and processing active radius. only cauculate for stuf in the active radius. world size for each world, : 2^24 chunks by 2^24 chunks in x and z. and for y we have 2^12 chunks all of them is in tootal os the world is 2^24 by 2^24 by 2^12, And to handel these updates we use seed system. ALSO, NEVER create objects inside gmae loop, and best to not test iwht cpu. NEVER cauculate large quantity with the cpu, use gpu, and limit data and if statements.
 
 Player control, wasd for movement, space for jump, 1-0 for hotbar selection, + and - for switching selected hotbar, esc for menu, tab to open inventory, eneter for chat later or making a better menu that suports everything, ctrl to sprint, toggleed wihich is on until velocity resets to 0. the FOV is based on velocity directly. shift is crouch. add more contorls later like x and other ablititys and stats. f3 for game stats and f5 for change camera mode. left click for breaking block and right click to place blocks. right click hold places blocks eveyr 5 ticks and left click hold is unlimited. if you click then both isnt limited to speed since thats just skills.
 Brainstorm: akybe this be a good sewrver hoster, with pbr and textured 3d sides, like a tree 

basic archtechture:
System handles actual system, it handles some major data transfer, physics, and calculations. For example if i break a place,system folder handles changing the block, storing stuf in list, and server management, world storing, handling clientside, and controling render, may conain some c++ and compute shaders for dectection. Rendering getss order from system or data from it and renders it also uses raycasting and just rendering and other stuff that dont require complex calculations or serverside , its just what client sees, may contain player settings, like fov and keyboard, and shaders etc. so basically lets say a player is mining a peice of wood, the system folder handles know its mining updating the block ,detecting collision and pushing the player back, physics, accual locations world etc, and storing player status. While render folder handles, rendering with ryacast and simple big amount O(1) calculations, detecting keys pressed, effects, player settings. So the key gives the player its key pressed and the system folder starts to process that data into acceleration and adds to player current vel.System folder computes we might rename rendering into client for better naming.

sHADER.BUMP AND normal maps: bump map adds thickenss to the texture while normal map adds details like lighting and shadows on it. These are black and white images, black for low and white for high. 

Add decompisition after mined, entityu uses newtons laws and phsycis. but not the world. when block created, particles are considered entitys.

advanced contorls: tab for inventory, f3 for stats, f5 for loop though the 4 render methods, raycasting, normal map(direction),  depth map, and triangle edge.

PROBLEMS: when i look down or up too much the screen flips for soem reason and i cnat mvoem y mouse properly. no add phsyics, seed ysstme,  everythings a mess, need deep cleanup foir code. Need to optimize data bit masking and unneccesary code. Not dynamic movement is linear.

IDEAS: Add realistic grouping, each voxel can be in a group whch sticks together, Raycast is way too lggy and noisy, seeds use a similar math function to hashmapping to generate a world the secreat thing could be hexadecimal of "Jingshuo67". chunks are 16x16x16 world has lots of chunks so only process changes. and render/processing within range. World is 2^20 by 2^20 by 2^12. and seed is 64 bit int. For fluids we could add eatiehr a softbody or particle based simulation. Texture file can be in assets, stores texture, brighjtness, roughness, and UV(base texture) becaue currentl;y its just simpley only blocks just place on the outside of a block. PHysics inthe game can be complex later, realistic physics for voxels is cool.Generate with DLA paired with fractak perlin noice. So generation dosent use chunkswe use chunks for faster, chunksi ngeneration istoo problems if we use chunks like cutoffs. Compute shader is a very genius idea, it is good for complex caucualtuions liek rycasting and colision detection and physics. We can name the game BitBox.

Game is made up of Clientside, Serverside, Boot, Assets, Guide, and Lib, Lib stores the libraries, Clientside stores the detections render and UI, while serversude oricesses that data and gives a respond, also handles world stuf. Boot startups the gmae, technally should be a part of Client or serverside but its neat to put in one folder? Assets have my textures and audios, and guide just has my summary about the project and guides for the agent. 

Future ideas: Mana, Volumumetric light, Abilitys, Civilatzation, better serverside, low level physics, space/exploration and biomes, socialing, chat, goups, spawn area, better menu and UI. Colorbleed.
AI- mutual ones have a health your rating and groups, harmful ones pathfind you atack and have health too since its an entity.

Minigame idea: blob jump around can flip gravity and goes based on ground dir. 

PROBLEM BASH: 
- **Circular Rendering Patterns**: Concentric rings appearing on terrain, likely due to float precision issues at extreme coordinates or LOD transitions. The steeper the terrain, the more serious the problem.
- **Insanely Long Render Time**: Raymarching performance is poor (barely 20 FPS), needs optimization or a rewrite later.
- **General Code Messiness**: Some parts are a bit messy/corrupted. Deprioritizing rendering fixes for now to focus on World Generation features.
